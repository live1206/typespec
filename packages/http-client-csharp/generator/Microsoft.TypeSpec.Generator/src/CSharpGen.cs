// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Utilities;

namespace Microsoft.TypeSpec.Generator
{
    internal sealed class CSharpGen
    {
        private const string ConfigurationFileName = "Configuration.json";
        private const string CodeModelFileName = "tspCodeModel.json";

        private static readonly string[] _filesToKeep = [ConfigurationFileName, CodeModelFileName];

        // Diagnostic observations only; never name permission.
        internal bool? LastObservedOwnedRootPhase { get; private set; }
        internal int LastObservedPocCandidatesBeforeOutput { get; private set; }
        internal IReadOnlyList<string> LastObservedPocCandidateFilesBeforeOutput { get; private set; } = [];
        internal bool? LastObservedPocOwnedStreamReceipt { get; private set; }

        private sealed class GenerationOwner(CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<TypeProvider> roots) : IDisposable
        {
            private ProviderReferenceMapSession? _session;
            private int _active = 1;

            internal void Bind(ProviderReferenceMapSession session)
            {
                if (Interlocked.CompareExchange(ref _session, session, null) is not null)
                {
                    Interlocked.Exchange(ref _active, 0);
                    return;
                }
                session.TryBindGenerationOwner(this, generator, output, roots);
            }

            internal void ObserveEnumerationRoot(ProviderReferenceMapSession session, OutputLibrary candidateOutput,
                IReadOnlyList<TypeProvider> candidateRoots) => session.ObserveOwnedEnumerationRoot(this, generator, candidateOutput, candidateRoots);

            internal bool Matches(CodeModelGenerator candidate, OutputLibrary candidateOutput,
                IReadOnlyList<TypeProvider> candidateRoots, ProviderReferenceMapSession session)
            {
                if (Volatile.Read(ref _active) == 0 || !ReferenceEquals(generator, candidate) ||
                    !ReferenceEquals(output, candidateOutput) || !ReferenceEquals(roots, candidateRoots) ||
                    !ReferenceEquals(_session, session)) { return false; }
                try { return ReferenceEquals(CodeModelGenerator.Instance, generator); }
                catch (InvalidOperationException) { return false; }
            }

            public void Dispose() => Interlocked.Exchange(ref _active, 0);
        }

        internal static bool IsActiveGenerationOwner(object owner, CodeModelGenerator generator, OutputLibrary output,
            IReadOnlyList<TypeProvider> roots, ProviderReferenceMapSession session) =>
            owner is GenerationOwner generationOwner && generationOwner.Matches(generator, output, roots, session);

        /// <summary>
        /// Executes the generator task with the <see cref="CodeModelGenerator"/> instance.
        /// </summary>
        public Task ExecuteAsync() => ExecuteCoreAsync(null);

        // A false residual-input result requires the trusted caller to discard
        // the isolated output. A true result is not a complete host certificate:
        // publication, all consumers and compilation still require validation.
        // Generation mutates the generator instance; fallback must use a fresh
        // instance rather than reuse this one, including after an exception.
        internal async Task<CompilerBuildGenerationResult> ExecuteForCompilerBuildAsync(string isolatedOutputDirectory,
            CompilerBuildInputProof.ITrustedCompilerBuildSession session)
        {
            var generator = CodeModelGenerator.Instance;
            var previousContext = generator.CompilerBuildContext;
            var generation = new CompilerBuildGenerationContext(isolatedOutputDirectory, session);
            generator.CompilerBuildContext = generation;
            try
            {
                generation.Initialize(generator);
                await ExecuteCoreAsync(generation);
                return new CompilerBuildGenerationResult(generation.UsedCapturedResidualInputs);
            }
            finally
            {
                generator.CompilerBuildContext = previousContext;
            }
        }

        internal sealed record CompilerBuildGenerationResult(bool UsedCapturedResidualInputs);

        private async Task ExecuteCoreAsync(CompilerBuildGenerationContext? compilerBuild)
        {
            LastObservedOwnedRootPhase = null;
            LastObservedPocCandidatesBeforeOutput = 0;
            LastObservedPocCandidateFilesBeforeOutput = [];
            LastObservedPocOwnedStreamReceipt = null;
            var generator = CodeModelGenerator.Instance;
            var outputPath = compilerBuild?.OutputDirectory ?? generator.Configuration.OutputDirectory;
            var generatedSourceOutputPath = compilerBuild?.GeneratedSourceDirectory ?? generator.Configuration.ProjectGeneratedDirectory;

            generator.Emitter.Info("Starting code generation");
            generator.Stopwatch.Start();

            // Resolve PackageReference items from the .csproj so custom code referencing external
            // NuGet types compiles correctly.
            await GeneratedCodeWorkspace.AddPackageReferencesFromProject();

            // Pre-walk the input library and resolve any external types that point at NuGet packages.
            // This populates ExternalTypeReferenceResolver's cache and registers each resolved assembly
            // as an additional metadata reference *before* the generated/custom code workspaces are
            // constructed, so their cached Roslyn projects pick the references up.
            await ExternalTypeReferenceResolver.ResolveAllAsync();

            // Initialize the workspace project AFTER all metadata references have been added so the
            // eagerly-cached project sees them.
            GeneratedCodeWorkspace.Initialize();

            GeneratedCodeWorkspace customCodeWorkspace = await GeneratedCodeWorkspace.Create(isCustomCodeProject: true);
            // The generated attributes need to be added into the workspace before loading the custom code. Otherwise,
            // Roslyn doesn't load the attributes completely and we are unable to get the attribute arguments.

            List<Task> generateAttributeTasks = new();
            foreach (var attributeProvider in CodeModelGenerator.Instance.CustomCodeAttributeProviders)
            {
                generateAttributeTasks.Add(customCodeWorkspace.AddInMemoryFile(attributeProvider));
            }

            await Task.WhenAll(generateAttributeTasks);

            var sourceInputModel = new SourceInputModel(
                await customCodeWorkspace.GetCompilationAsync(),
                await GeneratedCodeWorkspace.LoadBaselineContract(),
                GeneratedCodeWorkspace.LoadApiCompatBaseline());
            compilerBuild?.ApplySourceProof(sourceInputModel);
            CodeModelGenerator.Instance.SourceInputModel = sourceInputModel;

            GeneratedCodeWorkspace generatedCodeWorkspace = await GeneratedCodeWorkspace.Create(isCustomCodeProject: false);

            // Retain the actual receiver of the one virtual output getter: a
            // reentrant setter/getter may change the global generator instance.
            var outputOwner = CodeModelGenerator.Instance;
            var output = outputOwner.OutputLibrary;
            Directory.CreateDirectory(Path.Combine(generatedSourceOutputPath, "Models"));
            List<Task> generateFilesTasks = new();

            // Build all TypeProviders
            foreach (var type in output.TypeProviders)
            {
                type.EnsureBuilt();
            }

            LoggingHelpers.LogElapsedTime("All generated type providers built");

            // visit the entire library before generating files
            foreach (var visitor in CodeModelGenerator.Instance.Visitors)
            {
                visitor.VisitLibrary(output);
            }

            FilterAllCustomizedMembers(output);

            LoggingHelpers.LogElapsedTime("All visitors have been applied");

            foreach (var outputType in output.TypeProviders)
            {
                // Ensure back-compatibility processing is done after all visitors have run
                outputType.ProcessTypeForBackCompatibility();
            }

            try
            {
                var referenceMapRoots = output.TypeProviders;
                var observeRootPhase = generator.Configuration.EnablePreEmissionSimplification && !generator.Configuration.DisableRoslynReduce;
                using var generationOwner = observeRootPhase && ReferenceEquals(generator, outputOwner)
                    ? new GenerationOwner(generator, output, referenceMapRoots) : null;
                if (observeRootPhase && generationOwner is null) { LastObservedOwnedRootPhase = false; }
                using var referenceMapSession = generationOwner is not null
                    ? ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, referenceMapRoots)
                    : ProviderReferenceMapAnalyzer.PrepareForGeneration(referenceMapRoots);
                generationOwner?.Bind(referenceMapSession);
                // This one finalized-provider lifetime owns both existing field spellings and
                // non-authorizing POC proposals. Only the active, root-bound generation phase
                // may capture the POC plan; unknown ownership keeps the existing path.
                var preparationGenerator = CodeModelGenerator.Instance;
                var preparationRoots = output.TypeProviders;
                var capturePocPlan = generationOwner is not null && ReferenceEquals(preparationGenerator, generator) &&
                    ReferenceEquals(preparationRoots, referenceMapRoots) &&
                    referenceMapSession.IsOwnedGenerationPhaseCurrent(generator, output, referenceMapRoots);
                using var preparedHeaders = capturePocPlan ? new PreparedPocHeaderFinalization(preparationGenerator) : null;
                using var preparedFields = capturePocPlan
                    ? PreparedFieldTypes.CreateForGenerationWithFinalizedPocHeaders(preparationGenerator, preparationRoots, referenceMapSession.ShouldWriteProvider, preparedHeaders!)
                    : PreparedFieldTypes.CreateForGeneration(preparationGenerator, preparationRoots, referenceMapSession.ShouldWriteProvider);
                LastObservedPocCandidatesBeforeOutput = preparedFields?.PocCandidateCount ?? 0;
                LastObservedPocCandidateFilesBeforeOutput = preparedFields?.PocCandidateFilePaths ?? [];

                // Keep the existing streaming path when preparation is disabled.
                // With prepared spellings, stage every file until the complete
                // provider snapshot has survived all writer callbacks.
                List<CodeFile>? stagedFiles = preparedFields is null && compilerBuild?.SourceProof is null
                    ? null : new List<CodeFile>();

                void WriteFiles(PreparedFieldTypes? fields)
                {
                    void AddFile(TypeProvider provider)
                    {
                        fields?.ObserveSnapshot();
                        var writer = CodeModelGenerator.Instance.GetWriter(provider);
                        writer.PreparedFieldTypes = fields;
                        writer.PreparedPocHeaders = preparedHeaders;
                        if (fields?.HasPocProviderStream == true) { fields.BeginPocProviderWrite(provider, writer); }
                        var file = writer.Write();
                        if (fields?.HasPocProviderStream == true) { fields.EndPocProviderWrite(provider, writer, file); }
                        fields?.ObserveSnapshot();
                        if (stagedFiles is null)
                        {
                            generateFilesTasks.Add(generatedCodeWorkspace.AddGeneratedFile(file));
                        }
                        else
                        {
                            stagedFiles.Add(file);
                        }
                    }

                    foreach (var provider in GetWrittenProviders(output, referenceMapSession, generationOwner))
                    {
                        AddFile(provider);
                    }
                }

                WriteFiles(preparedFields);
                if (preparedFields?.HasPocProviderStream == true)
                {
                    LastObservedPocOwnedStreamReceipt = preparedFields.IsPocProviderWriteReceiptComplete;
                }
                if (generationOwner is not null)
                {
                    LastObservedOwnedRootPhase = referenceMapSession.IsOwnedEnumerationRootCurrent(generator, output, referenceMapRoots) &&
                        referenceMapSession.IsObservedEnumerationRootCurrent(output, referenceMapRoots);
                }
                if (stagedFiles is not null)
                {
                    // A writer callback may register a rewriter after preparation
                    // started. Recheck after the proof itself, then use the already
                    // captured qualified variants if the opt-out became active.
                    var fieldsCanCommit = preparedFields is not null && preparedFields.CanCommit() && PreparedFieldTypes.IsPolicyEnabled(generator);
                    var hasBodyNames = stagedFiles.Any(file => file.PreparedBodyNameCount != 0);
                    var bodyChoices = stagedFiles.Select(file => (File: file,
                        BodyAllowed: hasBodyNames && preparedFields?.CanCommitBodyNames(file) == true)).ToArray();
                    // Preserve the established field-only path when no body name
                    // was actually emitted; unused new facts add no veto/callbacks.
                    var sharedCanCommit = hasBodyNames
                        ? preparedFields is not null && preparedFields.CanCommitSharedSnapshot() && PreparedFieldTypes.IsPolicyEnabled(generator)
                        : fieldsCanCommit;
                    if (compilerBuild is not null)
                    {
                        var residualWorkspace = await compilerBuild.TryCreateResidualWorkspaceAsync(generator);
                        if (residualWorkspace is not null)
                        {
                            generatedCodeWorkspace = residualWorkspace;
                        }
                        sharedCanCommit &= residualWorkspace is not null;
                    }
                    if (hasBodyNames)
                    {
                        sharedCanCommit &= preparedFields is not null && preparedFields.CanCommitSharedSnapshot() && PreparedFieldTypes.IsPolicyEnabled(generator);
                    }
                    foreach (var (file, bodyAllowed) in bodyChoices)
                    {
                        // Both variants were emitted during the same writer pass;
                        // never rerun callbacks if the prepared proof changed.
                        generateFilesTasks.Add(generatedCodeWorkspace.AddGeneratedFile(
                            file.SelectPreparedNames(sharedCanCommit && fieldsCanCommit, sharedCanCommit && (!hasBodyNames || bodyAllowed))));
                    }
                }

                // Add all the generated files to the workspace
                await Task.WhenAll(generateFilesTasks);

                referenceMapSession.RestorePreWriteModelFactoryMethods();

                LoggingHelpers.LogElapsedTime("All generated types have been written into memory");

                // Delete any old generated files
                DeleteDirectory(generatedSourceOutputPath, _filesToKeep);

                LoggingHelpers.LogElapsedTime("All old generated files have been deleted");
            }
            finally
            {
                ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
            }

            var generatedFiles = new List<(string Name, string Text)>();
            await foreach (var file in generatedCodeWorkspace.GetGeneratedFilesAsync())
            {
                if (string.IsNullOrEmpty(file.Text))
                {
                    continue;
                }

                generatedFiles.Add((file.Name, file.Text));
            }

            // Write the generated files to the output directory
            foreach (var file in generatedFiles)
            {
                var filename = compilerBuild?.ResolveOutputFile(file.Name) ?? Path.Combine(outputPath, file.Name);
                CodeModelGenerator.Instance.Emitter.Info($"Writing {Path.GetFullPath(filename)}");
                Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
                await File.WriteAllTextAsync(filename, file.Text);
            }

            // A compiler-host session emits only provider files into its private
            // output. Its trusted caller must reject generators that require
            // additional C# files; normal generation retains all extra outputs.
            if (compilerBuild is null)
            {
                await CodeModelGenerator.Instance.WriteAdditionalFiles(outputPath);
            }

            // Write project scaffolding files (after additional files so schema existence can be checked)
            if (CodeModelGenerator.Instance.IsNewProject)
            {
                await CodeModelGenerator.Instance.TypeFactory.CreateNewProjectScaffolding().Execute();
            }

            LoggingHelpers.LogElapsedTime("All files have been written to disk");

            compilerBuild?.ValidateResult(generator);
        }

        private static IEnumerable<TypeProvider> GetWrittenProviders(OutputLibrary output, ProviderReferenceMapSession referenceMapSession, GenerationOwner? generationOwner)
        {
            var roots = output.TypeProviders;
            referenceMapSession.ObserveEnumerationRoot(output, roots);
            generationOwner?.ObserveEnumerationRoot(referenceMapSession, output, roots);
            referenceMapSession.BeginPocProviderStream();
            var exhausted = false;
            try
            {
                foreach (var outputType in roots)
                {
                    if (!referenceMapSession.ShouldWriteProvider(outputType) ||
                        outputType is ModelFactoryProvider && outputType.Methods.Count == 0)
                    {
                        continue;
                    }
                    referenceMapSession.ObservePocProviderYield(outputType);
                    yield return outputType;
                    foreach (var serialization in outputType.SerializationProviders)
                    {
                        if (referenceMapSession.ShouldWriteProvider(serialization))
                        {
                            referenceMapSession.ObservePocProviderYield(serialization);
                            yield return serialization;
                        }
                    }
                }
                exhausted = true;
            }
            finally { referenceMapSession.EndPocProviderStream(exhausted); }
        }

        internal static void FilterAllCustomizedMembers(OutputLibrary output)
        {
            var visited = new HashSet<TypeProvider>();
            foreach (var typeProvider in output.TypeProviders)
            {
                FilterAllCustomizedMembers(typeProvider, visited);
            }
        }

        private static void FilterAllCustomizedMembers(TypeProvider typeProvider, HashSet<TypeProvider> visited)
        {
            if (!visited.Add(typeProvider))
            {
                return;
            }

            // Update the type with the potentially modified members, filtering out customized members
            // after the visitors have been applied so that the filtering is done against the final version.
            FilterCustomizedMembers(typeProvider);
            foreach (var serializationProvider in typeProvider.SerializationProviders)
            {
                FilterAllCustomizedMembers(serializationProvider, visited);
            }
            foreach (var nestedType in typeProvider.NestedTypes)
            {
                FilterAllCustomizedMembers(nestedType, visited);
            }
        }

        private static void FilterCustomizedMembers(TypeProvider typeProvider)
        {
            // Update applies customization filtering internally, so passing the current cached members
            // is sufficient to apply the filter (e.g., after EnsureBuilt populated unfiltered caches).
            typeProvider.Update(
                typeProvider.Methods,
                typeProvider.Constructors,
                typeProvider.Properties,
                typeProvider.Fields);
        }

        /// <summary>
        /// Clears the output directory specified by <paramref name="path"/>. If <paramref name="filesToKeep"/> is not null,
        /// the specified files in the output directory will not be deleted.
        /// </summary>
        /// <param name="path">The path of the directory to delete.</param>
        /// <param name="filesToKeep">The list of file names to retain.</param>
        private static void DeleteDirectory(string path, string[] filesToKeep)
        {
            var directoryInfo = new DirectoryInfo(path);
            if (!directoryInfo.Exists)
            {
                return;
            }

            var fileNamesToKeep = filesToKeep.ToHashSet(StringComparer.Ordinal);
            foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                if (!fileNamesToKeep.Contains(file.Name))
                {
                    file.Delete();
                }
            }

            foreach (var directory in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                if (!Directory.Exists(directory.FullName))
                {
                    continue;
                }

                if (!directory.EnumerateFiles("*", SearchOption.AllDirectories).Any())
                {
                    directory.Delete(true);
                }
            }

            if (!directoryInfo.EnumerateFileSystemInfos().Any())
            {
                directoryInfo.Delete();
            }
        }
    }
}

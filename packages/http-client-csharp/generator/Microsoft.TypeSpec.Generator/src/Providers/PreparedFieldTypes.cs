// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.SourceInput;

namespace Microsoft.TypeSpec.Generator.Providers
{
    /// <summary>
    /// A per-generation snapshot of the field occurrences whose simple type names
    /// are known to bind to their intended generated types. Absence means fallback.
    /// </summary>
    internal sealed partial class PreparedFieldTypes : IDisposable
    {
        private readonly Dictionary<TypeProvider, OwnerState> _owners = new(ReferenceEqualityComparer.Instance);
        private readonly List<TypeProvider> _attached = [];
        private IReadOnlyList<TypeProvider>? _roots;
        private TypeProvider[] _rootSnapshot = [];
        private RootState[] _rootStates = [];
        private ProviderState[] _providerStates = [];
        private Func<TypeProvider, bool>? _shouldWrite;
        private CompleteSourceInputSnapshot? _sourceInputs;
        private SourceInputModel? _sourceModel;
        private string? _sourceOutputDirectory;
        private string? _outputRoot;
        private bool _captureProspectiveLifetime;
        private bool _prospectiveAwaitingDecision;
        private OutputLibrary? _prospectiveOutput;
        private CodeModelGenerator? _prospectiveGenerator;
        private CodeModelGenerator? _preparedGenerator;
        private long _prospectiveRootRevision;
        private List<TypeProvider>.Enumerator? _prospectiveListVersion;
        private int _prospectiveRootStale;
        private int _prospectiveGeneratorStale;
        private int _prospectiveRootMembersStale;
        private int _prospectiveOwnerSnapshotsStale;
        private int _valid;

        private sealed record OwnerState(CSharpType Type, TypeProvider[] Parts, string[] SourceScopeNames, HashSet<string> PlannedScopeNames)
        {
            internal Dictionary<FieldProvider, Candidate> Fields { get; } = new(ReferenceEqualityComparer.Instance);
            internal HashSet<string>? ForbiddenImports { get; set; }
        }

        private sealed record Candidate(CSharpType FieldType, TypeProvider? Target,
            TypeProvider[] TargetParts, INamedTypeSymbol? ReferencedTarget = null, long FieldTypeVersion = 0,
            bool CrossNamespace = false);

        private sealed record RootState(TypeProvider Provider, bool Written, TypeProvider[] Serializations, bool[] SerializationWritten);
        private sealed record ProviderState(TypeProvider Provider, CSharpType Type, string Name, string Namespace,
            int Arity, TypeProvider? Declaring, CSharpType? DeclaringType, TypeProvider[] Nested, string RelativeFilePath);

        private PreparedFieldTypes() { }

        internal static bool IsPolicyEnabled(CodeModelGenerator generator)
        {
            if (!generator.Configuration.EnablePreEmissionSimplification || generator.Configuration.DisableRoslynReduce ||
                generator.Rewriters.Count != 0)
            {
                return false;
            }
            var writerFactories = generator.GetType().GetMethods().Where(method =>
                method.Name == nameof(CodeModelGenerator.GetWriter) && !method.IsGenericMethod &&
                method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(TypeProvider)).ToArray();
            return writerFactories.Length == 1 && writerFactories[0].DeclaringType == typeof(CodeModelGenerator);
        }

        internal static PreparedFieldTypes? CreateForGeneration(CodeModelGenerator generator, TypeProvider[] providers,
            Func<TypeProvider, bool> shouldWrite) => CreateForGeneration(generator, (IEnumerable<TypeProvider>)providers, shouldWrite);

        internal static PreparedFieldTypes? CreateForGeneration(
            CodeModelGenerator generator,
            IEnumerable<TypeProvider> providers,
            Func<TypeProvider, bool> shouldWrite) => CreateForGenerationCore(generator, providers, shouldWrite, false, null);

        // This isolated pre-write entry point captures POC candidates in the
        // existing lifetime. It is not yet a writer permission or production
        // group-planning activation.
        internal static PreparedFieldTypes? CreateForGenerationWithPocPlans(
            CodeModelGenerator generator,
            IEnumerable<TypeProvider> providers,
            Func<TypeProvider, bool> shouldWrite) => CreateForGenerationCore(generator, providers, shouldWrite, false, null, true);

        // Explicitly approved header lifecycle, distinct from legacy diagnostic
        // POC capture. Unknown/side-effecting headers are never forced here.
        internal static PreparedFieldTypes? CreateForGenerationWithFinalizedPocHeaders(
            CodeModelGenerator generator, IEnumerable<TypeProvider> providers, Func<TypeProvider, bool> shouldWrite,
            PreparedPocHeaderFinalization headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            return headers.IsActiveFor(generator)
                ? CreateForGenerationCore(generator, providers, shouldWrite, false, null, true, headers)
                : null;
        }

        internal PreparedPocHeaderFinalization? FinalizedPocHeaders { get; private set; }

        // This retains a non-authorizing lifetime only. An overridden/unknown
        // output getter requires the active CSharpGen-owned reference-map phase;
        // the inherited base auto-getter can be audited without host callbacks.
        internal static PreparedFieldTypes? CreateProspectiveForGeneration(
            CodeModelGenerator generator,
            OutputLibrary output,
            Func<TypeProvider, bool> shouldWrite)
        {
            var owned = shouldWrite.Target is ProviderReferenceMapSession referenceMap &&
                shouldWrite.Equals((Func<TypeProvider, bool>)referenceMap.ShouldWriteProvider) &&
                output.TryCaptureMaterializedTypeProviders(out var roots, out _) &&
                referenceMap.IsOwnedGenerationPhaseCurrent(generator, output, roots!);
            if (!owned && !HasKnownDirectBaseOutput(generator, output)) { return null; }
            return CreateForGenerationCore(generator, null, shouldWrite, true, output);
        }

        private static bool HasKnownDirectBaseOutput(CodeModelGenerator generator, OutputLibrary output)
        {
            if (!IsCurrentGenerator(generator)) { return false; }
            // Include covariant or hidden properties in the audit before
            // selecting the single known inherited base auto-getter.
            var accessors = generator.GetType().GetProperties().Where(property => property.Name == nameof(CodeModelGenerator.OutputLibrary) &&
                property.GetIndexParameters().Length == 0).ToArray();
            return accessors.Length == 1 && accessors[0].PropertyType == typeof(OutputLibrary) &&
                accessors[0].GetMethod?.DeclaringType == typeof(CodeModelGenerator) && ReferenceEquals(generator.OutputLibrary, output);
        }

        private static bool IsCurrentGenerator(CodeModelGenerator generator)
        {
            try { return ReferenceEquals(CodeModelGenerator.Instance, generator); }
            catch (InvalidOperationException) { return false; }
        }

        private static PreparedFieldTypes? CreateForGenerationCore(
            CodeModelGenerator generator,
            IEnumerable<TypeProvider>? providers,
            Func<TypeProvider, bool> shouldWrite,
            bool captureProspectiveLifetime,
            OutputLibrary? prospectiveOutput,
            bool capturePocPlans = false, PreparedPocHeaderFinalization? finalizedHeaders = null)
        {
            if (!IsPolicyEnabled(generator))
            {
                return null;
            }

            var sourceModel = generator.SourceInputModel;
            var sourceInputs = sourceModel.PreEmissionSourceInputs;
            var outputDirectory = generator.Configuration.OutputDirectory;
            // The build proof can include linked and target-added inputs that the
            // existing customization workspace does not. Keep that workspace's
            // behavior unchanged while using the independent complete proof here.
            if (sourceInputs is null || sourceModel.Customization is null || !sourceInputs.IsCurrentFor(outputDirectory))
            {
                return null;
            }

            IReadOnlyList<TypeProvider>? prospectiveRoots = null;
            long prospectiveRootRevision = 0;
            List<TypeProvider>.Enumerator? prospectiveListVersion = null;
            if (captureProspectiveLifetime)
            {
                if (prospectiveOutput is null || !prospectiveOutput.TryCaptureMaterializedTypeProviders(out prospectiveRoots, out prospectiveRootRevision)) { return null; }
                if (prospectiveRoots!.GetType() == typeof(List<TypeProvider>))
                {
                    prospectiveListVersion = ((List<TypeProvider>)prospectiveRoots).GetEnumerator();
                }
                providers = prospectiveRoots;
            }
            if (providers is null) { return null; }
            ProviderReferenceMapSession? pocReferenceMap = null;
            OutputLibrary? pocOutput = null;
            IReadOnlyList<TypeProvider>? pocRoots = null;
            ImmutableHashSet<string>? pocRemovalCandidates = null;
            var pocBoundaryBudget = capturePocPlans ? new PocWorkBudget(8192) : null;
            if (capturePocPlans && shouldWrite.Target is ProviderReferenceMapSession referenceMap &&
                shouldWrite.Equals((Func<TypeProvider, bool>)referenceMap.ShouldWriteProvider) &&
                referenceMap.TryGetObservedReferenceMapRoot(out var observedOutput, out var observedRoots) &&
                ReferenceEquals(providers, observedRoots) && referenceMap.TryCaptureObservedRemovalCandidates(pocBoundaryBudget!, out pocRemovalCandidates))
            {
                pocReferenceMap = referenceMap;
                pocOutput = observedOutput;
                pocRoots = observedRoots;
            }

            PreparedPocPostMapAttributeBoundary? postMapAttributeBoundary = null;
            if (finalizedHeaders is not null && pocReferenceMap is not null && pocOutput is not null &&
                pocRoots is not null && pocRemovalCandidates is not null)
            {
                postMapAttributeBoundary = PreparedPocPostMapAttributeBoundary.TryCapture(
                    generator,
                    pocOutput,
                    pocRoots,
                    pocReferenceMap,
                    pocRemovalCandidates,
                    pocBoundaryBudget!);
            }

            var result = CreateCore(providers, sourceInputs.Compilation, shouldWrite, outputDirectory, sourceInputs, sourceModel,
                captureProspectiveLifetime, capturePocPlans, finalizedHeaders, generator.CachedInputLibrary?.CachedInputNamespace,
                postMapAttributeBoundary);
            result._sourceInputs = sourceInputs;
            result._sourceModel = sourceModel;
            result._sourceOutputDirectory = outputDirectory;
            result._preparedGenerator = generator;
            result._pocReferenceMap = pocReferenceMap;
            result._pocOutput = pocOutput;
            result._pocRoots = pocRoots;
            result._pocRemovalCandidates = pocRemovalCandidates;
            result._pocHasDirectSourceGetter = pocReferenceMap is not null && HasKnownDirectSourceInputGetter(generator);
            result._prospectiveOutput = prospectiveOutput;
            result._prospectiveGenerator = captureProspectiveLifetime ? generator : null;
            result._prospectiveRootRevision = prospectiveRootRevision;
            result._prospectiveListVersion = prospectiveListVersion;
            if (capturePocPlans) { result.CapturePocOrdinaryCandidates(); }
            if ((System.Threading.Volatile.Read(ref result._valid) == 0 && (captureProspectiveLifetime || !result.HasSourceFiles)) ||
                !sourceInputs.IsCurrentFor(outputDirectory))
            {
                result.Dispose();
                return null;
            }
            if (capturePocPlans)
            {
                result.FinalizePocOrdinaryCandidates();
                result.AttachPocProviderStream();
            }
            // Source currentness can run host code. Check the prospective facts
            // after its final callback rather than trusting an earlier result.
            var prospectiveReferenceCurrent = !captureProspectiveLifetime ||
                prospectiveOutput!.IsMaterializedTypeProviderSnapshotCurrent(prospectiveRoots!, prospectiveRootRevision);
            var prospectiveGeneratorCurrent = !captureProspectiveLifetime || IsCurrentGenerator(generator);
            var prospectiveMembersCurrent = prospectiveListVersion is not { } listVersion || IsProspectiveListVersionCurrent(listVersion);
            if (!prospectiveReferenceCurrent) { System.Threading.Interlocked.Exchange(ref result._prospectiveRootStale, 1); }
            if (!prospectiveGeneratorCurrent) { System.Threading.Interlocked.Exchange(ref result._prospectiveGeneratorStale, 1); }
            if (!prospectiveMembersCurrent) { System.Threading.Interlocked.Exchange(ref result._prospectiveRootMembersStale, 1); }
            if (result._prospectiveAwaitingDecision && (!prospectiveReferenceCurrent || !prospectiveGeneratorCurrent || !prospectiveMembersCurrent))
            {
                result.Dispose();
                return null;
            }
            // A prospective-only failure cannot erase an independent field/body
            // decision that the ordinary preparation path would have retained.
            return result;
        }

        internal static PreparedFieldTypes Create(TypeProvider[] providers, Compilation? customization,
            Func<TypeProvider, bool> shouldWrite, string? outputDirectory = null) =>
            Create((IEnumerable<TypeProvider>)providers, customization, shouldWrite, outputDirectory);

        internal static PreparedFieldTypes Create(
            IEnumerable<TypeProvider> providers,
            Compilation? customization,
            Func<TypeProvider, bool> shouldWrite,
            string? outputDirectory = null)
            => CreateCore(providers, customization, shouldWrite, outputDirectory, null, null);

        private static PreparedFieldTypes CreateCore(IEnumerable<TypeProvider> providers, Compilation? customization,
            Func<TypeProvider, bool> shouldWrite, string? outputDirectory, CompleteSourceInputSnapshot? sourceInputs, SourceInputModel? sourceModel,
            bool captureProspectiveLifetime = false, bool capturePocPlans = false, PreparedPocHeaderFinalization? finalizedHeaders = null,
            InputNamespace? loadedInput = null, PreparedPocPostMapAttributeBoundary? postMapAttributeBoundary = null)
        {
            var result = new PreparedFieldTypes
            {
                _sourceInputs = sourceInputs,
                _sourceModel = sourceModel,
                _sourceOutputDirectory = sourceInputs is null ? null : outputDirectory,
                _captureProspectiveLifetime = captureProspectiveLifetime
            };
            if (providers is not IReadOnlyList<TypeProvider> roots || !ContractCollections.TryCopy(roots, out var capturedRoots) ||
                customization is not CSharpCompilation compilation || HasUnsupportedSource(compilation))
            {
                return result;
            }

            var all = new List<TypeProvider>();
            var visited = new HashSet<TypeProvider>(ReferenceEqualityComparer.Instance);
            var rootStates = new List<RootState>();
            var rootSnapshot = capturedRoots.ToArray();
            foreach (var provider in rootSnapshot)
            {
                var written = capturePocPlans ? ShouldWriteRoot(provider, shouldWrite, result) : ShouldWriteRoot(provider, shouldWrite);
                var serializations = written ? provider.SerializationProviders.ToArray() : [];
                bool[] serializationWritten;
                if (capturePocPlans)
                {
                    serializationWritten = new bool[serializations.Length];
                    for (var index = 0; index < serializations.Length; index++)
                    {
                        serializationWritten[index] = shouldWrite(serializations[index]);
                        result.CapturePocWriteDecisionProvider(serializations[index]);
                    }
                }
                else { serializationWritten = serializations.Select(shouldWrite).ToArray(); }
                rootStates.Add(new RootState(provider, written, serializations, serializationWritten));
                if (!written)
                {
                    continue;
                }
                if (!AddProviderAndNested(provider, null, all, visited))
                {
                    return result;
                }
                for (int i = 0; i < serializations.Length; i++)
                {
                    if (serializationWritten[i] && !AddProviderAndNested(serializations[i], null, all, visited))
                    {
                        return result;
                    }
                }
            }

            // Unsupported identifiers elsewhere can normalize to an otherwise clean
            // spelling, so do not infer absence of collisions from their raw text.
            if (all.Any(p => !IsSimpleIdentifier(p.Type.Name) ||
                p.Type.Namespace.Split('.').Any(part => !IsSimpleIdentifier(part))))
            {
                return result;
            }

            var topLevel = all.Where(p => p.DeclaringTypeProvider is null && p.Type.DeclaringType is null).ToArray();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string outputRoot;
            try
            {
                outputRoot = Path.GetFullPath(outputDirectory ?? CodeModelGenerator.Instance.Configuration.OutputDirectory);
                foreach (var provider in topLevel)
                {
                    var path = Path.GetFullPath(provider.RelativeFilePath, outputRoot);
                    if (HasUnstablePath(path) || !paths.Add(path))
                    {
                        return result;
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return result;
            }
            var byName = topLevel.GroupBy(p => (p.Type.Namespace, p.Type.Name))
                .ToDictionary(g => g.Key, g => g.ToArray());
            var namespacePrefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var provider in topLevel)
            {
                var ns = provider.Type.Namespace;
                while (ns.Length > 0 && namespacePrefixes.Add(ns))
                {
                    var lastDot = ns.LastIndexOf('.');
                    ns = lastDot < 0 ? string.Empty : ns[..lastDot];
                }
            }
            var importLookup = new PreparedFieldImportLookup(compilation, byName, namespacePrefixes);
            result._pocLookup = capturePocPlans ? importLookup : null;
            var safeTargets = new Dictionary<CSharpType, bool>(ReferenceEqualityComparer.Instance);
            var referencedTargets = new Dictionary<CSharpType, INamedTypeSymbol?>(ReferenceEqualityComparer.Instance);
            var sourceScopes = new Dictionary<(string Namespace, string Name), (bool Supported, bool Exists, string[] Names)>();

            foreach (var owner in topLevel)
            {
                var ownerType = owner.Type;
                var ownerParts = byName[(ownerType.Namespace, ownerType.Name)];
                var ownerKey = (ownerType.Namespace, ownerType.Name);
                if (!sourceScopes.TryGetValue(ownerKey, out var sourceScope))
                {
                    sourceScope = CaptureSourceOwnerScope(compilation, ownerType.Namespace, ownerType.Name);
                    sourceScopes.Add(ownerKey, sourceScope);
                }
                if (!sourceScope.Supported ||
                    !sourceScope.Exists && ownerParts.Any(part => part.CustomCodeView is not null) ||
                    !IsSupportedDeclarationGroup(ownerParts, requireSimpleScope: true))
                {
                    continue;
                }

                var scopeNames = CaptureScopeNames(ownerParts);
                scopeNames.UnionWith(sourceScope.Names);
                if (scopeNames.Any(name => !IsSimpleIdentifier(name)))
                {
                    continue;
                }

                var seenFields = new HashSet<FieldProvider>(ReferenceEqualityComparer.Instance);
                var duplicateFields = new HashSet<FieldProvider>(ReferenceEqualityComparer.Instance);
                foreach (var field in owner.Fields)
                {
                    if (!seenFields.Add(field))
                    {
                        duplicateFields.Add(field);
                    }
                }

                var ownerState = new OwnerState(ownerType, ownerParts, sourceScope.Names, scopeNames);
                foreach (var field in owner.Fields)
                {
                    var type = field.Type;
                    if (duplicateFields.Contains(field) || !ReferenceEquals(field.EnclosingType, owner) ||
                        !HasSupportedFieldTypeShape(type) ||
                        !IsSimpleIdentifier(type.Name) || scopeNames.Contains(type.Name))
                    {
                        continue;
                    }
                    Candidate candidate;
                    if (byName.TryGetValue((type.Namespace, type.Name), out var targets))
                    {
                        // Generated targets keep their canonical provider identity;
                        // a same-spelled reference cannot replace that relationship.
                        if (type.IsFrameworkType)
                        {
                            continue;
                        }
                        var matchingTargets = targets.Where(target => ReferenceEquals(target.Type, type)).ToArray();
                        if (matchingTargets.Length != 1)
                        {
                            continue;
                        }
                        if (!safeTargets.TryGetValue(type, out var safeTarget))
                        {
                            safeTarget = IsSupportedDeclarationGroup(targets, requireSimpleScope: false) &&
                                targets.All(target => target.CustomCodeView is null) &&
                                !HasExistingNamespaceMember(compilation, type.Namespace, type.Name, byName) &&
                                !namespacePrefixes.Contains(type.Namespace + "." + type.Name);
                            safeTargets.Add(type, safeTarget);
                        }
                        if (!safeTarget)
                        {
                            continue;
                        }
                        candidate = new Candidate(type, matchingTargets[0], targets);
                    }
                    else
                    {
                        if (!referencedTargets.TryGetValue(type, out var referencedTarget))
                        {
                            referencedTarget = namespacePrefixes.Contains(type.Namespace + "." + type.Name)
                                ? null : ResolveReferencedTarget(compilation, type, byName);
                            referencedTargets.Add(type, referencedTarget);
                        }
                        if (referencedTarget is null)
                        {
                            continue;
                        }
                        candidate = new Candidate(type, null, [], referencedTarget, type.IdentityVersion);
                    }

                    if (type.Namespace != ownerType.Namespace)
                    {
                        if (!importLookup.TryPrepare(owner, type, candidate.ReferencedTarget, out var forbiddenImports))
                        {
                            continue;
                        }
                        candidate = candidate with { CrossNamespace = true };
                        (ownerState.ForbiddenImports ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(forbiddenImports);
                    }

                    ownerState.Fields.Add(field, candidate);
                }
                if (ownerState.Fields.Count > 0)
                {
                    result._owners.Add(owner, ownerState);
                }
            }

            if (finalizedHeaders is { } headers)
            {
                foreach (var root in rootStates)
                {
                    if (!root.Written) { continue; }
                    headers.Materialize(root.Provider, loadedInput, postMapAttributeBoundary);
                    for (var index = 0; index < root.Serializations.Length; index++)
                    { if (root.SerializationWritten[index]) { headers.Materialize(root.Serializations[index], loadedInput, postMapAttributeBoundary); } }
                }
                result.FinalizedPocHeaders = headers;
            }

            result.CaptureSourceFiles(topLevel, compilation, roots, all, byName, namespacePrefixes,
                rootStates.All(root => root.Written && root.SerializationWritten.All(written => written)));

            result.CaptureBodyNames();
            if (result._owners.Count == 0 && !result.HasBodyNameCandidates)
            {
                if (!captureProspectiveLifetime || !result.HasSourceFiles) { return result; }
                // Retain the lifetime without changing the existing no-decision
                // observation path. A future admitted group must explicitly
                // activate its own choices; the lifetime alone grants none.
                result._prospectiveAwaitingDecision = true;
            }

            result._roots = roots;
            result._outputRoot = outputRoot;
            result._rootSnapshot = rootSnapshot;
            result._rootStates = rootStates.ToArray();
            if (!result._prospectiveAwaitingDecision)
            {
                result._providerStates = all.Select(provider => new ProviderState(provider, provider.Type,
                    provider.Type.Name, provider.Type.Namespace, provider.Type.Arguments.Count,
                    provider.DeclaringTypeProvider, provider.Type.DeclaringType, provider.NestedTypes.ToArray(), provider.RelativeFilePath)).ToArray();
            }
            result._shouldWrite = shouldWrite;

            // The snapshot is valid only while all participating providers remain
            // unchanged. A competing session or any supported mutation fails closed.
            System.Threading.Volatile.Write(ref result._valid, 1);
            foreach (var provider in all)
            {
                if (!provider.TryAttachFieldTypePreparation(result))
                {
                    result.Dispose();
                    return result;
                }
                result._attached.Add(provider);
            }
            return result;
        }

        internal void Invalidate() => System.Threading.Interlocked.Exchange(ref _valid, 0);

        // The symbol work is completed during preparation. Writing only validates
        // that later namespace registration does not violate the captured scope.
        internal void ObserveImport(TypeProvider fileOwner, string namespaceName)
        {
            ObserveBodyImport(fileOwner, namespaceName);
            if (_owners.TryGetValue(fileOwner, out var owner) && owner.ForbiddenImports is { } forbidden &&
                (namespaceName.Split('.').Any(part => !IsSimpleIdentifier(part)) || forbidden.Contains(namespaceName)))
            {
                Invalidate();
            }
        }

        public void Dispose()
        {
            PocGenerationEnvelope?.Reject();
            System.Threading.Interlocked.Exchange(ref _sharedSnapshotStale, 1);
            Invalidate();
            InvalidateSourceFiles();
            foreach (var provider in _attached)
            {
                provider.DetachFieldTypePreparation(this);
            }
            _attached.Clear();
        }

        internal bool CanCommit()
        {
            ObserveSourceSnapshot();
            if (_prospectiveAwaitingDecision) { return false; }
            if (!ValidateSnapshot())
            {
                return false;
            }
            foreach (var (owner, state) in _owners)
            {
                foreach (var (field, candidate) in state.Fields)
                {
                    // Keep the observation boundary for each occurrence: callbacks
                    // and retained collections can diverge between snapshot checks.
                    ObserveSourceSnapshot();
                    if (!ValidateSnapshot() || !ValidateCandidateScopes() ||
                        !ValidateOccurrence(owner, state, field, candidate))
                    {
                        return false;
                    }
                }
            }
            // A divergence first seen by the final check must also be sticky.
            return ValidateSnapshot();
        }

        internal void ObserveSnapshot()
        {
            ObserveSourceSnapshot();
            if (_prospectiveAwaitingDecision) { return; }
            if (ValidateSnapshot())
            {
                ValidateCandidateScopes();
            }
        }

        private bool ValidateSnapshot() => Validate(
            System.Threading.Volatile.Read(ref _valid) != 0 && IsSnapshotCurrent());

        private bool Validate(bool current)
        {
            if (!current)
            {
                Invalidate();
            }
            return System.Threading.Volatile.Read(ref _valid) != 0;
        }

        private bool ValidateCandidateScopes()
        {
            foreach (var state in _owners.Values)
            {
                if (!Validate(IsSupportedDeclarationGroup(state.Parts, requireSimpleScope: true)))
                {
                    return false;
                }
                foreach (var candidate in state.Fields.Values)
                {
                    if (!Validate(IsTargetCurrent(state, candidate)))
                    {
                        return false;
                    }
                }
            }
            return System.Threading.Volatile.Read(ref _valid) != 0;
        }

        internal bool CanWriteSimpleName(TypeProvider owner, FieldProvider field)
        {
            ObserveSnapshot();
            return System.Threading.Volatile.Read(ref _valid) != 0 &&
                _owners.TryGetValue(owner, out var state) && state.Fields.TryGetValue(field, out var candidate) &&
                ValidateOccurrence(owner, state, field, candidate);
        }

        private bool ValidateOccurrence(TypeProvider owner, OwnerState state, FieldProvider field, Candidate candidate)
        {
            if (!Validate(ReferenceEquals(candidate.FieldType, field.Type) && ReferenceEquals(state.Type, owner.Type) &&
                ReferenceEquals(field.EnclosingType, owner) &&
                owner.Fields.Count(current => ReferenceEquals(current, field)) == 1 &&
                IsTargetCurrent(state, candidate) && IsSupportedDeclarationGroup(state.Parts, requireSimpleScope: true)))
            {
                return false;
            }

            // Public update APIs can retain mutable lists, and method signatures can
            // be updated independently of their providers. A divergence is sticky:
            // reverting the model later cannot make already-emitted text safe again.
            var scopeNames = CaptureScopeNames(state.Parts);
            scopeNames.UnionWith(state.SourceScopeNames);
            return Validate(!scopeNames.Contains(candidate.FieldType.Name) && scopeNames.All(IsSimpleIdentifier) &&
                SourceInputsAreCurrent());
        }

        private static bool HasSupportedFieldTypeShape(CSharpType type) =>
            !type.IsArray && !type.IsNullable && !type.IsGenericType && type.DeclaringType is null && !type.IsLiteral && !type.IsUnion;

        private static bool IsTargetCurrent(OwnerState owner, Candidate candidate) => candidate.Target is not null
            ? ReferenceEquals(candidate.FieldType, candidate.Target.Type) && IsSupportedDeclarationGroup(candidate.TargetParts, requireSimpleScope: false)
            // Retained argument lists can change shape without incrementing the
            // canonical identity version. Observation/use must recheck the same
            // supported shape as initial preparation, before a name is emitted.
            : candidate.ReferencedTarget is not null && HasSupportedFieldTypeShape(candidate.FieldType) &&
                candidate.FieldType.IdentityVersion == candidate.FieldTypeVersion &&
                candidate.FieldType.Name == candidate.ReferencedTarget.Name &&
                (candidate.CrossNamespace || candidate.FieldType.Namespace == owner.Type.Namespace) &&
                candidate.FieldType.Namespace == candidate.ReferencedTarget.ContainingNamespace.ToDisplayString();

        // The metadata namespace view already exists before the writer runs.
        // Resolve only the bounded top-level same-namespace case; never construct
        // generated syntax or speculate on a generated expression to make this decision.
        internal static INamedTypeSymbol? ResolveReferencedTarget(CSharpCompilation compilation, CSharpType type,
            IReadOnlyDictionary<(string Namespace, string Name), TypeProvider[]> generated, bool forStaticCall = false, bool forLoadedSourceCall = false)
        {
            var current = compilation.GlobalNamespace;
            var prefix = string.Empty;
            foreach (var part in type.Namespace.Split('.'))
            {
                if (!IsSimpleIdentifier(part) || generated.ContainsKey((prefix, part)))
                {
                    return null;
                }
                var members = current.GetMembers(part).Take(2).ToArray();
                if (members.Length != 1 || members[0] is not INamespaceSymbol next)
                {
                    return null;
                }
                current = next;
                prefix = prefix.Length == 0 ? part : prefix + "." + part;
            }
            var targets = current.GetMembers(type.Name).Take(2).ToArray();
            if (targets.Length != 1 || targets[0] is not INamedTypeSymbol target || target.Arity != 0 || target.ContainingType is not null ||
                target.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate) ||
                !forStaticCall && (target.SpecialType != SpecialType.None || target.IsStatic) ||
                !compilation.IsSymbolAccessibleWithin(target, compilation.Assembly))
            {
                return null;
            }

            if (target.Locations.Any(location => location.IsInSource) || SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly))
            {
                // Call-only admission for an already loaded declaration. Field
                // spelling and metadata value-type lookup keep their old veto.
                return forStaticCall && forLoadedSourceCall && !target.IsFileLocal && target.TypeKind == TypeKind.Class &&
                    target.BaseType?.SpecialType == SpecialType.System_Object && target.DeclaringSyntaxReferences.Length != 0 &&
                    SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly) &&
                    target.DeclaringSyntaxReferences.All(r => compilation.ContainsSyntaxTree(r.SyntaxTree)) ? target : null;
            }

            // Extern-alias-only metadata is not visible to the writer's unaliased
            // global:: spelling, and cannot authorize a simple spelling either.
            return compilation.References.Any(reference =>
                (reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global")) &&
                SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), target.ContainingAssembly)) ? target : null;
        }

        private static bool IsSupportedDeclarationGroup(TypeProvider[] parts, bool requireSimpleScope)
        {
            if (parts.Length == 0 || (requireSimpleScope && parts.Any(part => !part.HasSimpleFieldTypeScope)))
            {
                return false;
            }
            if (parts.Length == 1)
            {
                return true;
            }

            var firstType = parts[0].Type;
            if (parts.Any(part => !part.HasSimpleFieldTypeScope ||
                !part.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Partial) ||
                part.Type.Name != firstType.Name || part.Type.Namespace != firstType.Namespace))
            {
                return false;
            }

            // A name match alone is insufficient: each other written part must
            // be a current serialization child or an explicitly registered part
            // of the same emitted owner (for example a client's REST part).
            return parts.Count(root => parts.All(part => ReferenceEquals(part, root) ||
                ReferenceEquals(part.SerializationProviderOwner, root) &&
                    root.SerializationProviders.Any(child => ReferenceEquals(child, part)) ||
                ReferenceEquals(part.PartialDeclarationOwner, root) &&
                    root.PartialDeclarationParts.Any(child => ReferenceEquals(child, part)))) == 1;
        }

        private static HashSet<string> CaptureScopeNames(TypeProvider[] parts)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                names.UnionWith(part.CaptureFieldTypeScopeNames());
            }
            return names;
        }

        private bool CheckSnapshotCurrent()
        {
            if (!SourceInputsAreCurrent() || _roots is null || _outputRoot is null || _shouldWrite is null ||
                !SameReferences(_roots, _rootSnapshot))
            {
                return false;
            }
            foreach (var root in _rootStates)
            {
                if (ShouldWriteRoot(root.Provider, _shouldWrite) != root.Written)
                {
                    return false;
                }
                if (!root.Written)
                {
                    continue;
                }
                if (!SameReferences(root.Provider.SerializationProviders, root.Serializations))
                {
                    return false;
                }
                for (int i = 0; i < root.Serializations.Length; i++)
                {
                    if (_shouldWrite(root.Serializations[i]) != root.SerializationWritten[i])
                    {
                        return false;
                    }
                }
            }
            foreach (var state in _providerStates)
            {
                var type = state.Provider.Type;
                if (!ReferenceEquals(type, state.Type) || type.Name != state.Name || type.Namespace != state.Namespace ||
                    type.Arguments.Count != state.Arity || !ReferenceEquals(state.Provider.DeclaringTypeProvider, state.Declaring) ||
                    !ReferenceEquals(type.DeclaringType, state.DeclaringType) || !SameReferences(state.Provider.NestedTypes, state.Nested) ||
                    state.Provider.RelativeFilePath != state.RelativeFilePath)
                {
                    return false;
                }
                if (state.Declaring is null)
                {
                    try
                    {
                        if (HasUnstablePath(Path.GetFullPath(state.RelativeFilePath, _outputRoot)))
                        {
                            return false;
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool HasUnstablePath(string path)
        {
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException) { return true; }
                catch (UnauthorizedAccessException) { return true; }
                catch (System.Security.SecurityException) { return true; }
            }
            return false;
        }

        private bool SourceInputsAreCurrent()
        {
            var current = _sourceInputs is null || _sourceModel is not null && ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs) &&
                _sourceOutputDirectory is not null && _sourceInputs.IsCurrentFor(_sourceOutputDirectory);
            // No callback replay: inspect an already-captured active header
            // immediately after the existing source-proof observation.
            ObservePocActiveHeaderAfterSourceCallback();
            return current;
        }

        private static bool ShouldWriteRoot(TypeProvider provider, Func<TypeProvider, bool> shouldWrite) =>
            shouldWrite(provider) && (provider is not ModelFactoryProvider || provider.Methods.Count != 0);

        private static bool ShouldWriteRoot(TypeProvider provider, Func<TypeProvider, bool> shouldWrite, PreparedFieldTypes poc)
        {
            if (provider is not ModelFactoryProvider)
            {
                var written = ShouldWriteRoot(provider, shouldWrite);
                poc.CapturePocWriteDecisionProvider(provider);
                return written;
            }
            // Preserve the existing policy-before-method-count order, but
            // capture identity before the latter can invoke a lazy builder.
            var permitted = shouldWrite(provider);
            poc.CapturePocWriteDecisionProvider(provider);
            var result = permitted && provider.Methods.Count != 0;
            poc.CapturePocModelFactoryMethods(provider);
            return result;
        }

        private static bool SameReferences(IReadOnlyList<TypeProvider> current, IReadOnlyList<TypeProvider> snapshot)
        {
            if (current.Count != snapshot.Count)
            {
                return false;
            }
            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(current[i], snapshot[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AddProviderAndNested(TypeProvider provider, TypeProvider? declaring,
            List<TypeProvider> all, HashSet<TypeProvider> visited)
        {
            if (!visited.Add(provider) || !ReferenceEquals(provider.DeclaringTypeProvider, declaring) ||
                !ReferenceEquals(provider.Type.DeclaringType, declaring?.Type))
            {
                return false;
            }
            all.Add(provider);
            foreach (var nested in provider.NestedTypes)
            {
                if (!AddProviderAndNested(nested, provider, all, visited))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasUnsupportedSource(CSharpCompilation compilation) =>
            compilation.SyntaxTrees.Any(tree =>
            {
                var root = tree.GetRoot();
                return tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
                    root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                        .Any(directive => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)) ||
                    root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.GetStructure() is
                        IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax or ElseDirectiveTriviaSyntax or EndIfDirectiveTriviaSyntax);
            });

        private static (bool Supported, bool Exists, string[] Names) CaptureSourceOwnerScope(
            Compilation compilation, string namespaceName, string name)
        {
            var current = compilation.Assembly.GlobalNamespace;
            foreach (var part in namespaceName.Split('.'))
            {
                var members = current.GetMembers(part).Take(2).ToArray();
                if (members.Length == 0)
                {
                    return (true, false, []);
                }
                // A source type occupying a namespace prefix cannot be treated
                // as absence of source declarations at the intended namespace.
                if (members.Length != 1 || members[0] is not INamespaceSymbol next)
                {
                    return (false, false, []);
                }
                current = next;
            }
            var owners = current.GetMembers(name).Take(2).ToArray();
            if (owners.Length == 0)
            {
                return (true, false, []);
            }
            if (owners.Length != 1 || owners[0] is not INamedTypeSymbol owner || owner.TypeKind != TypeKind.Class ||
                owner.IsRecord || owner.IsFileLocal || owner.Arity != 0 || owner.ContainingType is not null ||
                owner.BaseType?.SpecialType != SpecialType.System_Object || owner.Interfaces.Length != 0 ||
                owner.DeclaringSyntaxReferences.Length == 0 ||
                owner.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is not ClassDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.TypeParameterList is not null || declaration.ConstraintClauses.Count != 0))
            {
                return (false, true, []);
            }

            // These are already-loaded source symbols, not generated syntax.
            // Constructors, accessors and implicit backing fields do not introduce
            // ordinary lookup names in the generated field's containing scope.
            var names = owner.GetMembers().Where(member => !member.IsImplicitlyDeclared &&
                (member is INamedTypeSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol ||
                 member is IMethodSymbol { MethodKind: MethodKind.Ordinary }))
                .Select(member => member.Name).ToArray();
            return (true, true, names);
        }

        private static bool HasExistingNamespaceMember(Compilation compilation, string namespaceName, string typeName,
            IReadOnlyDictionary<(string Namespace, string Name), TypeProvider[]> generated)
        {
            INamespaceSymbol? current = compilation.GlobalNamespace;
            var prefix = string.Empty;
            foreach (var part in namespaceName.Split('.'))
            {
                // Qualification can carry namespace/type-conflict diagnostics even
                // when the simple spelling binds to the intended generated type.
                if (generated.ContainsKey((prefix, part)) || current?.GetTypeMembers(part).Length > 0)
                {
                    return true;
                }
                prefix = prefix.Length == 0 ? part : prefix + "." + part;
                // An absent metadata namespace does not rule out a generated type
                // occupying a later prefix, so finish the provider-graph checks.
                if (current is null)
                {
                    continue;
                }
                var namespaces = current.GetNamespaceMembers();
                if (namespaces.Any(ns => !IsSimpleIdentifier(ns.Name)))
                {
                    return true;
                }
                current = namespaces.FirstOrDefault(ns => ns.Name == part);
            }

            return current?.GetMembers().Any(member => !IsSimpleIdentifier(member.Name) || member.Name == typeName) == true;
        }

        private static bool IsSimpleIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsAsciiIdentifierStart(name[0]) ||
                SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
                SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None)
            {
                return false;
            }
            for (int i = 1; i < name.Length; i++)
            {
                if (!IsAsciiIdentifierStart(name[i]) && (name[i] < '0' || name[i] > '9'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiIdentifierStart(char value) =>
            value == '_' || (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
    }
}

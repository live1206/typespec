// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ClientModel.Primitives;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.SourceInput;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PreparedPocGenerationClientTests
    {
        [Test]
        public async Task RealFooProposalIsPreparedBeforeActualOutputProviderWrites() => await RunOwnedHost(null);

        [TestCase("false")]
        [TestCase("throw")]
        [TestCase("writer")]
        public async Task RealFooOwnedHostRetainsHeaderLifecycleThroughFailure(string failure) => await RunOwnedHost(failure);

        [Test]
        public async Task RealFooOwnedHostAfterPriorMockUsesObservedContextPath() => await RunOwnedHost(null, initializeMockFirst: true);

        private static async Task RunOwnedHost(string? failure, bool initializeMockFirst = false)
        {
            CodeModelGenerator? previous = null;
            try { previous = CodeModelGenerator.Instance; }
            catch (InvalidOperationException) { }
            var scmField = typeof(ScmCodeModelGenerator).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var previousScm = scmField.GetValue(null);
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var previousPolicy = policy.GetValue(null);
            var directory = Path.Combine(Path.GetTempPath(), "typespec-poc-foo-generation", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(directory, "original");
            var isolated = Path.Combine(directory, "isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            var source = Path.Combine(directory, "linked.cs");
            File.WriteAllText(source, "namespace Other { class Unrelated {} }");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PreparedFieldTypes", "ClientStructureDefault.json"),
                Path.Combine(original, "tspCodeModel.json"));
            try
            {
                // Selected alone in a fresh test process, this exercises the
                // inherited Sample-first static context-name initialization.
                // The same scope restores both generators and static policy.
                if (initializeMockFirst) { MockHelpers.LoadMockGenerator(); }
                var configuration = Configuration.Load(original,
                    "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = generator;
                ((CodeModelGenerator)generator).Configure();
                var references = new[] { FindRuntimeReference(), typeof(PipelineMessage).Assembly.Location }.Distinct(StringComparer.Ordinal).ToArray();
                var compilation = CSharpCompilation.Create("FooReferenceProbe", references: references.Select(path => MetadataReference.CreateFromFile(path)));
                var target = compilation.GetTypeByMetadataName(typeof(PipelineMessage).FullName!);
                Assert.That(target, Is.Not.Null, "The controlled compiler reference inventory must resolve the actual Foo local type.");
                Assert.That(compilation.IsSymbolAccessibleWithin(target!, compilation.Assembly), Is.True);
                var lifecycle = new ContextAttributeLifecycleObservation(generator);
                var probe = new OwnedWriterProbe(failure == "writer");
                int finalChecks = 0;
                bool injected = false;
                bool Current()
                {
                    lifecycle.Observe();
                    if (failure is null || injected || !lifecycle.HasCanonical) { return true; }
                    var frames = new System.Diagnostics.StackTrace().GetFrames();
                    bool finalDirect = Enumerable.Range(0, frames.Length - 1).Any(index =>
                        frames[index].GetMethod()?.DeclaringType == typeof(CompleteSourceInputSnapshot) &&
                        frames[index].GetMethod()?.Name == nameof(CompleteSourceInputSnapshot.IsCurrentFor) &&
                        frames[index + 1].GetMethod()?.DeclaringType == typeof(Microsoft.TypeSpec.Generator.Providers.PreparedFieldTypes) &&
                        frames[index + 1].GetMethod()?.Name == "CreateForGenerationCore");
                    if (!finalDirect) { return true; }
                    finalChecks++;
                    injected = true;
                    var foo = generator.OutputLibrary.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(type => type.Name == "Foo");
                    var method = foo.Methods.First(method => method.BodyStatements is not null);
                    method.Update(bodyStatements: new Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[] { method.BodyStatements!, probe });
                    if (failure == "throw") { throw new InvalidOperationException("Controlled owned-host unavailable proof."); }
                    return failure == "writer";
                }
                var session = new FooFixtureSession(original, source, references, Current);
                var generation = new CSharpGen();

                if (failure == "writer")
                {
                    var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await generation.ExecuteForCompilerBuildAsync(isolated, session));
                    Assert.That(error!.Message, Is.EqualTo(OwnedWriterProbe.FailureMessage));
                    Assert.That(finalChecks, Is.EqualTo(1));
                    Assert.That(probe.Writes, Is.EqualTo(1), "An escaping writer failure must not replay the native writer.");
                    Assert.That(probe.Preparation, Is.Not.Null);
                    Assert.That(probe.Preparation!.CanCommit(), Is.False);
                    Assert.That(probe.Headers, Is.Not.Null);
                    Assert.That(probe.Headers!.IsActiveFor(generator), Is.False, "The actual host's scoped header owner must be disposed on exception.");
                    Assert.That(probe.Headers.Count, Is.Zero);
                    lifecycle.AssertCanonicalCacheRetained(requireWriterObservation: false);
                    Assert.That(Directory.EnumerateFiles(isolated, "*.cs", SearchOption.AllDirectories), Is.Empty,
                        "The failed one-pass generation must not publish staged files.");
                    return;
                }

                var result = await generation.ExecuteForCompilerBuildAsync(isolated, session);
                if (failure is not null)
                {
                    Assert.That(finalChecks, Is.EqualTo(1), "Reject only the final direct source check, after canonicalization.");
                    Assert.That(result.UsedCapturedResidualInputs, Is.False);
                    Assert.That(generation.LastObservedOwnedRootPhase, Is.True);
                    Assert.That(generation.LastObservedPocCandidateFilesBeforeOutput, Is.Empty);
                    Assert.That(probe.Writes, Is.EqualTo(1), "The actual output must still run once after the name plan was discarded.");
                    Assert.That(probe.Preparation, Is.Null, "The final physical writer must receive no stale name authority.");
                    lifecycle.AssertCanonicalCacheRetained(requireWriterObservation: false);
                    Assert.That(File.Exists(Path.Combine(isolated, lifecycle.Context.RelativeFilePath)), Is.True);
                    return;
                }

                Assert.That(result.UsedCapturedResidualInputs, Is.True, "The controlled trusted-host source proof must remain active.");
                Assert.That(generation.LastObservedOwnedRootPhase, Is.True);
                Assert.That(generation.LastObservedPocCandidateFilesBeforeOutput.Any(path => path.EndsWith("/Foo.cs", StringComparison.Ordinal)), Is.True,
                    "The actual generation path must precompute the Foo proposal before final output-provider writing.");
                Assert.That(generation.LastObservedPocOwnedStreamReceipt, Is.True,
                    "The actual owned provider stream and standard writers must complete the planned receipt.");
                lifecycle.AssertFinalWriterConsumedCanonicalCache();
                Assert.That(File.Exists(Path.Combine(isolated, lifecycle.Context.RelativeFilePath)), Is.True,
                    "The exact observed admitted context must actually reach the owned output writer.");
            }
            finally
            {
                Microsoft.TypeSpec.Generator.ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
                CodeModelGenerator.Instance = previous!;
                scmField.SetValue(null, previousScm);
                policy.SetValue(null, previousPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class OwnedWriterProbe(bool throwOnWrite) : Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement
        {
            internal const string FailureMessage = "Controlled owned-host writer failure.";
            internal int Writes { get; private set; }
            internal Microsoft.TypeSpec.Generator.Providers.PreparedFieldTypes? Preparation { get; private set; }
            internal Microsoft.TypeSpec.Generator.Primitives.PreparedPocHeaderFinalization? Headers { get; private set; }
            internal override void WriteCore(CodeWriter writer)
            {
                Writes++;
                Preparation = (Microsoft.TypeSpec.Generator.Providers.PreparedFieldTypes?)typeof(CodeWriter)
                    .GetField("_preparedFieldTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(writer);
                Headers = Preparation?.FinalizedPocHeaders;
                if (throwOnWrite) { throw new InvalidOperationException(FailureMessage); }
                writer.WriteRawLine("// owned-host source-discard writer probe");
            }
        }

        private static string FindRuntimeReference()
        {
            var runtime = new DirectoryInfo(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            var dotnet = runtime.Parent?.Parent?.Parent;
            var packs = dotnet is null ? null : Path.Combine(dotnet.FullName, "packs", "Microsoft.NETCore.App.Ref");
            if (packs is not null && Directory.Exists(packs))
            {
                var reference = Directory.EnumerateDirectories(packs)
                    .Select(path => Path.Combine(path, "ref", $"net{Environment.Version.Major}.0", "System.Runtime.dll"))
                    .Where(File.Exists).OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();
                if (reference is not null) { return reference; }
            }
            return typeof(object).Assembly.Location;
        }

        private sealed class ContextAttributeLifecycleObservation(ScmCodeModelGenerator generator)
        {
            private Microsoft.TypeSpec.Generator.Providers.TypeProvider? _context;
            private System.Collections.Generic.IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? _before, _canonical;
            private object? _beforeOrigin, _canonicalOrigin;
            private int _writerObservations;
            internal bool HasCanonical => _canonical is not null;
            internal Microsoft.TypeSpec.Generator.Providers.TypeProvider Context => _context ??
                throw new InvalidOperationException("The native context provider was not observed.");

            internal bool Observe()
            {
                // Test-only nonforcing read of the exact native SCM cache. A
                // getter override would change admission and is not an observer.
                var output = (OutputLibrary?)typeof(ScmCodeModelGenerator).GetField("_scmOutputLibrary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(generator);
                if (output is null || !output.TryCaptureMaterializedTypeProviders(out var roots, out _)) { return true; }
                _context = roots!.SingleOrDefault(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                if (_context?.CachedWriteAttributes is not { } cache || _context.PocNativeAttributeCacheOrigin is not { } origin) { return true; }
                var inWriter = new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                    frame.GetMethod()?.DeclaringType == typeof(Microsoft.TypeSpec.Generator.Primitives.TypeProviderWriter));
                if (_context.PocPostMapAttributeOperation is null)
                {
                    _before = cache;
                    _beforeOrigin = origin;
                    return true;
                }
                if (_canonical is null)
                {
                    Assert.That(inWriter, Is.False, "Approved post-map materialization must be observed BEFORE any final writer.");
                    _canonical = cache;
                    _canonicalOrigin = origin;
                }
                Assert.That(ReferenceEquals(cache, _canonical), Is.True, "Final native writing must not rebuild admitted canonical attributes.");
                Assert.That(ReferenceEquals(origin, _canonicalOrigin), Is.True);
                if (inWriter) { _writerObservations++; }
                return true;
            }

            internal void AssertFinalWriterConsumedCanonicalCache() => AssertCanonicalCacheRetained(requireWriterObservation: true);

            internal void AssertCanonicalCacheRetained(bool requireWriterObservation)
            {
                Assert.That(_before is not null && _canonical is not null && _context is not null, Is.True,
                    "The actual trusted-host callbacks must observe both sides of the admitted post-map boundary.");
                Assert.That(ReferenceEquals(_before, _canonical), Is.False);
                Assert.That(ReferenceEquals(_beforeOrigin, _canonicalOrigin), Is.False);
                if (requireWriterObservation) { Assert.That(_writerObservations, Is.GreaterThan(0)); }
                Assert.That(ReferenceEquals(_context!.CachedWriteAttributes, _canonical), Is.True);
                Assert.That(ReferenceEquals(_context.PocNativeAttributeCacheOrigin, _canonicalOrigin), Is.True);
            }
        }

        private sealed class FooFixtureSession(string outputDirectory, string sourcePath, string[] references, Func<bool>? current = null) : ITrustedCompilerBuildSession
        {
            private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            private readonly string _sourceHash = Hash(sourcePath);
            private readonly CapturedCompilerReference[] _references = references.Select(path => new CapturedCompilerReference(path, Hash(path), [], false)).ToArray();
            public CompilerBuildInputCapture Capture => new(outputDirectory, "foo-poc-generation", "Debug|net10.0", "12.0", "FooPocProof", [],
                [new CapturedCompilerSource(sourcePath, _sourceHash)], _references, false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            public bool IsCurrent() => current?.Invoke() ?? true;
        }
    }
}

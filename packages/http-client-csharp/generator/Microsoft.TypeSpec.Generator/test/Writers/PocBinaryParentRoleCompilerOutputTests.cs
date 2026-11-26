// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocBinaryParentRoleCompilerOutputTests
    {
        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("source-expiry", false)]
        [TestCase("source-expiry", true)]
        [TestCase("result-expiry", false)]
        [TestCase("result-expiry", true)]
        public async Task BareArithmeticUsesActualCompilerOutputAndFinalAuthority(string mode, bool multiply)
        {
            var sandbox = Directory.CreateTempSubdirectory("task05-bare-arithmetic-");
            string source = Path.Combine(sandbox.FullName, "Marker.cs");
            string nativeSource = Path.Combine(sandbox.FullName, "NativeMarker.cs");
            const string input = "namespace Inputs { internal class Marker {} }\n";
            File.WriteAllText(source, input);
            File.WriteAllText(nativeSource, input);
            string retained = multiply ? "=> x + (y * z);" : "=> (x - y) - z;";
            string shortened = multiply ? "=> x + y * z;" : "=> x - y - z;";
            try
            {
                var actual = await WriteBareArithmeticCompilerOutput(sandbox.FullName, source, multiply, mode);
                var native = await WriteBareArithmeticCompilerOutput(sandbox.FullName, nativeSource, multiply, "native");
                Assert.That(native.Selected.Content, Does.Contain(retained));
                Assert.That(actual.Selected.Content, Does.Contain(mode == "source-expiry" ? retained : shortened));
                if (mode == "source-expiry") { Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content)); }
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, actual.Selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                object?[][] arguments = multiply ? [[10, 2, 3], [int.MaxValue, 2, 2]] : [[10, 7, 3], [int.MinValue, 1, 1]];
                object?[] expected = multiply ? [16, int.MinValue + 3] : [0, int.MaxValue - 1];
                foreach (var file in new[] { native.Selected, actual.Selected, native.Final, actual.Final })
                {
                    var compilation = CSharpCompilation.Create("BareArithmeticPhysicalStage", [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)],
                        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                    Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", arguments), Is.EqualTo(expected));
                }
            }
            finally { sandbox.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteBareArithmeticCompilerOutput(string directory, string source, bool multiply, string mode)
        {
            bool enabled = mode != "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, mode + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, mode + "-isolated")).FullName;
            string config = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("BareArithmeticPhysicalInput")));
            var owner = new PocBinaryParentRoleIntegrationTests.BareArithmeticOwner(multiply);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            int rootVisits = 0;
            int childVisits = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (!enabled) { Assert.That(prepared, Is.Null); return; }
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                    Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                    prepared.PocNativeWriteObserver = node =>
                    {
                        if (ReferenceEquals(node, owner.Root)) { rootVisits++; }
                        if (ReferenceEquals(node, owner.Child)) { childVisits++; }
                    };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(enabled ? 1 : 0));
                    if (enabled) { Assert.That(file.Content, Does.Contain(multiply ? "=> x + y * z;" : "=> x - y - z;")); }
                },
                PocGroupingDecisionObserved = (_, allowed) =>
                {
                    Assert.That(allowed, Is.True);
                    if (mode == "source-expiry") { File.AppendAllText(source, "// expired before selection\n"); }
                },
                PocSelectedFileObserved = file =>
                {
                    selected = file;
                    if (mode == "result-expiry") { File.AppendAllText(source, "// expired after selection\n"); }
                }
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"), "False means the caller must discard isolated output");
            Assert.That(rootVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(childVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        [TestCase("stable")]
        [TestCase("isolated-link")]
        [TestCase("original-link")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        [TestCase("absolute-name")]
        public async Task CompilerInvocationUsesActualOutputRootAndPhysicalInputs(string mode)
        {
            if (mode.EndsWith("-link", StringComparison.Ordinal) && OperatingSystem.IsWindows())
            { Assert.Ignore("The controlled fixture requires unprivileged Unix symbolic links"); }
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-compiler-output", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var target = Path.Combine(sandbox, "owned-link-target");
            var source = Path.Combine(sandbox, "linked.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("CompilerOutput")));
                var session = new Session(original, source);
                string name = mode == "absolute-name" ? Path.Combine(isolated, "Clock.cs") : "alias/Clock.cs";
                var owner = new Clock(name);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CompleteSourceInputSnapshot? proof = null;
                CodeFile? selected = null;
                int uses = 0;
                int visits = 0;
                bool decisionSeen = false;
                bool? allowed = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null, "A real captured compiler session must supply physical input authority");
                        prepared!.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Inner)) { visits++; } };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(ReferenceEquals(provider, owner), Is.True);
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo(name));
                        Assert.That(file.Content, Does.Contain("return x + y == z;"));
                    },
                    PocGroupingDecisionObserved = (file, value) =>
                    {
                        decisionSeen = true;
                        allowed = value;
                        Assert.That(value, Is.EqualTo(mode != "absolute-name"), "Only a relative ordinary name can initially admit in a compiler invocation");
                        if (mode.EndsWith("-link", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            Directory.CreateSymbolicLink(Path.Combine(mode == "isolated-link" ? isolated : original, "alias"), target);
                        }
                        if (mode == "source-expiry")
                        { File.AppendAllText(source, "// physical input changed before selection\n"); }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "result-expiry")
                        { File.AppendAllText(source, "// physical input changed after selection\n"); }
                    }
                };
                CSharpGen.CompilerBuildGenerationResult? result = null;
                Exception? failure = null;
                try
                { result = await pipeline.ExecuteForCompilerBuildAsync(isolated, session); }
                catch (Exception exception) { failure = exception; }
                Assert.That(uses, Is.EqualTo(2));
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(decisionSeen, Is.True);
                Assert.That(allowed, Is.EqualTo(mode != "absolute-name"));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(name));
                bool shortForm = mode is "stable" or "original-link" or "result-expiry";
                TestContext.WriteLine($"COMPILER-OUTPUT mode={mode}; uses={uses}; visits={visits}; selectedShort={selected.Content.Contains("return x + y == z;")}; failure={failure?.GetType().Name ?? "none"}; capturedResult={result?.UsedCapturedResidualInputs}; proofCurrent={proof?.IsCurrentFor(original)}");
                if (mode is "isolated-link" or "absolute-name")
                {
                    Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                    Assert.That(failure!.Message, Does.Contain(mode == "absolute-name" ? "must be relative" : "escaped its isolated directory"));
                    Assert.That(proof!.IsCurrentFor(original), Is.True, "The unrelated original inputs remain valid");
                }
                else
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(result, Is.Not.Null);
                    Assert.That(result!.UsedCapturedResidualInputs, Is.EqualTo(mode is not "source-expiry" and not "result-expiry"),
                        "False requires the trusted caller to discard even already-written isolated output");
                    Assert.That(File.Exists(Path.Combine(isolated, "alias", "Clock.cs")), Is.True);
                }
                Assert.That(selected.Content, Does.Contain(shortForm ? "return x + y == z;" : "return ((x + y) == z);"));
                var compilation = CSharpCompilation.Create("ActualCompilerOutput",
                    [CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name)],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [[1, 2, 3], [0, 0, 1]]),
                    Is.EqualTo(new object?[] { true, false }));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task NativeInventorySupportsFilesystemRootWithoutFilesystemWrites(bool filesystemRoot) =>
            RunRootInventory(filesystemRoot, directoryName: false);

        [TestCase(false)]
        [TestCase(true)]
        public Task OutputDirectoryCannotBecomeNativeFile(bool filesystemRoot) =>
            RunRootInventory(filesystemRoot, directoryName: true);

        private static async Task RunRootInventory(bool filesystemRoot, bool directoryName)
        {
            var owned = Path.Combine(Path.GetTempPath(), "task05-root-inventory", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(owned);
            try
            {
                string directory = filesystemRoot ? Path.GetPathRoot(owned)! : owned;
                string name = directoryName ? "." : Path.GetRelativePath(directory, Path.Combine(owned, "Clock.cs"));
                var compilation = CSharpCompilation.Create("RootInventory", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
                // No pipeline/workspace scan or output filesystem writes: the
                // single rendered CodeFile remains strictly in the owned subtree.
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(compilation));
                var owner = new Clock(name);
                var output = new TestOutputLibrary(owner);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
                generator.SourceInputModel = new SourceInputModel(compilation, null)
                {
                    PreEmissionSourceInputs = new CompleteSourceInputSnapshot(directory, "controlled-root-output", compilation, ["immutable-inputs"], () => true)
                };
                CodeModelGenerator.Instance = generator;
                var roots = output.TypeProviders;
                foreach (var root in roots)
                { root.ProcessTypeForBackCompatibility(); }
                using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
                using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                bool allowed = prepared!.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
                var selected = raw.SelectPreparedForms(true, true, allowed);
                TestContext.WriteLine($"ROOT-INVENTORY filesystemRoot={filesystemRoot}; directoryName={directoryName}; uses={prepared.PocGroupingUses}; allowed={allowed}; actualName={raw.Name}");
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(2));
                Assert.That(raw.Name, Is.EqualTo(name));
                Assert.That(selected.Content, Does.Contain(directoryName ? "return ((x + y) == z);" : "return x + y == z;"));
                Assert.That(Directory.GetFiles(owned), Is.Empty, "Rendering must not write under the filesystem root");
            }
            finally { Directory.Delete(owned, recursive: true); }
        }

        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-isolated-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true; // Physical hashes are independently revalidated by the production proof.
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Clock(string path) : TypeProvider
        {
            internal BinaryOperatorExpression? Inner;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Inner = new BinaryOperatorExpression("+", x, y);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    new ParameterProvider[] { x, y, z }), Return(new BinaryOperatorExpression("==", Inner, z)), this)];
            }
        }
    }
}

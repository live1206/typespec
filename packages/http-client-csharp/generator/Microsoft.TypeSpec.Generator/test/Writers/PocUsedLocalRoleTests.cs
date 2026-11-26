// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocUsedLocalRoleTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("used")]
        [TestCase("disabled")]
        [TestCase("unused")]
        [TestCase("nested-operator")]
        [TestCase("extra-statement")]
        [TestCase("alias")]
        [TestCase("shadow")]
        [TestCase("return-hint")]
        [TestCase("ref")]
        [TestCase("default")]
        [TestCase("unsupported-base")]
        [TestCase("unsupported-modifier")]
        [TestCase("unsupported-default")]
        [TestCase("restored-before")]
        [TestCase("restored-opening")]
        public async Task NativeLocalInitializerPairRequiresTheUsedLocalProfile(string mode)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "used-local-role", compilation, ["used-local-role"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            var method = owner.Methods.Single();
            Assert.That(PreparedMethodParameterFrame.TryCapture(method, method.BodyStatements!), Is.Null,
                "The explicit frame still rejects used local declarations");
            int visits = 0;
            int mutations = 0;
            void RestoreLocalName()
            {
                var declaration = owner.Local.Declaration;
                string saved = declaration.RequestedName;
                declaration.Update("other");
                declaration.Update(saved);
                mutations++;
            }
            prepared!.PocNativeWriteObserver = node =>
            {
                if (!ReferenceEquals(node, owner.Initializer)) { return; }
                visits++;
                if (mode == "restored-before") { RestoreLocalName(); }
            };
            if (mode == "restored-opening") { prepared.PocAfterOpening = RestoreLocalName; }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] args = [[1, 2], [0, -5], [int.MaxValue, 2]];
            Assert.That(Execute(selected, args), Is.EqualTo(Execute(native, args)));
            Assert.That(visits, Is.EqualTo(1));
            bool restored = mode is "restored-before" or "restored-opening";
            Assert.That(mutations, Is.EqualTo(restored ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored));
            Assert.That(owner.Initializer.AddParentheses, Is.EqualTo(mode != "disabled"));
            string nativeInitializer = ActualInitializer(native);
            string selectedInitializer = ActualInitializer(selected);
            TestContext.WriteLine($"LOCAL-ROLE mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; raw={raw.PreparedGroupingCount}; native={nativeInitializer}; selected={selectedInitializer}; visits={visits}");
            bool shorten = mode is "used" or "alias";
            int expectedUse = shorten || mode == "restored-opening" ? 1 : 0;
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(shorten || restored ? 1 : 0));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUse));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(selectedInitializer, Is.EqualTo(shorten ? "x + y" : nativeInitializer));
            });
        }

        [TestCase("stable")]
        [TestCase("local-expiry")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task CompilerUsedLocalUsesActualFilesAndFinalAuthority(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-used-local-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("UsedLocalCompilerInputs")));
                var owner = new Clock("used", path: "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                int visits = 0;
                int uses = 0;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Initializer)) { visits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(ActualInitializer(file), Is.EqualTo("x + y"));
                        if (mode == "local-expiry") { owner.Local.Declaration.Update("other"); owner.Local.Declaration.Update("value"); }
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        Assert.That(allowed, Is.EqualTo(mode != "local-expiry"));
                        if (mode == "source-expiry") { File.AppendAllText(source, "// changed before selection\n"); }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "result-expiry") { File.AppendAllText(source, "// changed after selection\n"); }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
                Assert.That(ActualInitializer(selected), Is.EqualTo(mode is "stable" or "result-expiry" ? "x + y" : "(x + y)"));
                bool physicalInputsCurrent = mode is "stable" or "local-expiry";
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(physicalInputsCurrent));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(physicalInputsCurrent),
                    "A false result requires discarding the isolated output even when shortened source was selected");
                var nativeOwner = new Clock("used", path: "alias/Clock.cs");
                nativeOwner.ProcessTypeForBackCompatibility();
                var native = new TypeProviderWriter(nativeOwner).Write();
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Content, final.Content);
                object?[][] args = [[1, 2], [0, -5], [int.MaxValue, 2]];
                object?[] expected = [3, -5, int.MinValue + 1];
                Assert.That(Execute(selected, args), Is.EqualTo(expected));
                Assert.That(Execute(final, args), Is.EqualTo(expected));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task UsedLocalDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var early = new Lazy<int>(() => 0);
            var late = new Lazy<int>(() => 0);
            bool armed = false;
            void ObserveDependency()
            {
                _ = early.Value;
                if (armed) { _ = late.Value; }
            }
            var owner = new Clock("used", observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "used-local-cold", compilation, ["used-local-cold"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            if (dependency == "serialization") { owner.Update(serializations: new SerializationCarrier(ObserveDependency)); }
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(early.IsValueCreated, Is.True);
            Assert.That(late.IsValueCreated, Is.False);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            if (withLegacy)
            {
                var old = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                Assert.That(old.PreparedGroupingCount, Is.EqualTo(1));
                Assert.That(old.Content, Does.Contain("return x == y;"));
            }
            int initializerVisits = 0;
            int leftVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Initializer))
                {
                    initializerVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = true;
                }
                if (ReferenceEquals(node, owner.Initializer.Left))
                {
                    leftVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(initializerVisits, Is.EqualTo(1));
            Assert.That(leftVisits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withLegacy ? 2 : 1));
            Assert.That(ActualInitializer(selected), Is.EqualTo("x + y"));
        }

        private static string ActualInitializer(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single().Initializer!.Value.NormalizeWhitespace().ToFullString();
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("UsedLocalRole",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] args) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", args);
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "used-local-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }
        private sealed class SerializationCarrier(Action observe) : IReadOnlyList<TypeProvider>
        {
            public int Count { get { observe(); return 0; } }
            public TypeProvider this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
            public IEnumerator<TypeProvider> GetEnumerator() => ((IEnumerable<TypeProvider>)Array.Empty<TypeProvider>()).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class DirectControl : TypeProvider
        {
            protected override string BuildName() => "Control";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Control.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]), Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
        private sealed class Clock(string mode, string path = "Clock.cs", Action? observePath = null) : TypeProvider
        {
            internal BinaryOperatorExpression Initializer = null!;
            internal VariableExpression Local = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                observePath?.Invoke();
                return path;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int), isRef: mode == "ref")
                {
                    HasUnsupportedParameterModifiers = mode == "unsupported-modifier"
                };
                var y = new ParameterProvider("y", $"", typeof(int), defaultValue: mode == "default" ? Literal(2) : null)
                {
                    HasUnsupportedDefaultValue = mode == "unsupported-default"
                };
                var declaration = new DeclarationExpression(typeof(int), mode == "shadow" ? "x" : "value", out var local);
                Local = local;
                ValueExpression right = mode == "nested-operator" ? new BinaryOperatorExpression("*", y, y) : (VariableExpression)y;
                Initializer = new BinaryOperatorExpression("+", x, right, mode != "disabled");
                var assign = new AssignmentExpression(declaration, Initializer).Terminate();
                var returned = Return(mode switch
                {
                    "unused" => (VariableExpression)x,
                    "alias" => new VariableExpression(typeof(int), local.Declaration),
                    "return-hint" => new VariableExpression(typeof(long), local.Declaration),
                    _ => local
                });
                var body = mode == "extra-statement"
                    ? new MethodBodyStatements([assign, new AssignmentExpression(local, Literal(0)).Terminate(), returned])
                    : new MethodBodyStatements([assign, returned]);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [x, y])
                {
                    HasUnsupportedBaseContract = mode == "unsupported-base"
                };
                return [new MethodProvider(signature, body, this)];
            }
        }
    }
}

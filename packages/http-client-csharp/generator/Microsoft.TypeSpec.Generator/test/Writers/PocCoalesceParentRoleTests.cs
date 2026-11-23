// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    internal class PocCoalesceParentRoleTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        // Original M2 coalesceRight uses an already-unwrapped native root.
        // Return and root-parenthesized cases are separately labelled bridges.
        [TestCase("right", false)]
        [TestCase("right", true)]
        [TestCase("root-pair", false)]
        [TestCase("root-pair", true)]
        [TestCase("left", false)]
        [TestCase("left", true)]
        [TestCase("disabled", false)]
        [TestCase("disabled", true)]
        public async Task NativeCoalesceRolePreservesActualStringBindingAndAssociation(string kind, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(kind, arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-coalesce-parent", compilation, ["controlled-coalesce-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(kind, arrow);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var visits = new List<object>();
            prepared!.PocNativeWriteObserver = visits.Add;
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = [[null, null, "last"], [null, "middle", "last"], ["first", "middle", "last"], [null, null, null], ["", "middle", null]];
            object?[] values = ["last", "middle", "first", null, ""];
            Assert.That(Execute(native, arguments), Is.EqualTo(values));
            Assert.That(Execute(selected, arguments), Is.EqualTo(values));
            int expected = kind is "right" or "root-pair" ? 1 : 0;
            string expectedExpression = kind switch
            {
                "root-pair" => "(x ?? y ?? z)",
                "left" => "(x ?? y) ?? z",
                _ => "x ?? y ?? z"
            };
            string expression = PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace().ToFullString();
            TestContext.WriteLine($"COALESCE-ROLE kind={kind}; arrow={arrow}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; physical={raw.PreparedGroupingCount}; expression={expression}; binaryVisits={visits.OfType<BinaryOperatorExpression>().Count()}; receipt={raw.SourceConsumption?.WasCurrentAtEndOfWrite}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expected), "Actual right-edge preparation, not legacy final shortening");
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
                Assert.That(expression, Is.EqualTo(expectedExpression));
                Assert.That(visits.OfType<BinaryOperatorExpression>().Count(), Is.EqualTo(2));
                Assert.That(owner.Outer.AddParentheses, Is.EqualTo(kind == "root-pair"));
                Assert.That(owner.Inner.AddParentheses, Is.EqualTo(kind != "disabled"));
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            });
        }

        [TestCase("hint", true)]
        [TestCase("hint", false)]
        [TestCase("unbound", true)]
        [TestCase("object", true)]
        [TestCase("nullable", true)]
        [TestCase("ref", true)]
        [TestCase("default", true)]
        [TestCase("unsupported-default", true)]
        [TestCase("unsupported-modifier", true)]
        [TestCase("unsupported-base", true)]
        [TestCase("nested", false)]
        public async Task CoalesceAdmissionUsesOnlyTheClosedBoundProfile(string defect, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock("right", arrow, defect);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "coalesce-admission", compilation, ["coalesce-admission"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var method = owner.Methods.Single();
            Assert.That(PreparedMethodParameterFrame.TryCapture(method, (object?)method.BodyStatements ?? method.BodyExpression!), Is.Null,
                "The explicit parameter-frame grammar does not admit coalescing");
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared!.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            var expected = defect == "hint" ? 1 : 0;
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(expected));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
            if (defect == "unbound") { return; } // Deliberately not an emitted formal; only admission is asserted.
            var nativeOwner = new Clock("right", arrow, defect);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            if (expected == 0)
            {
                Assert.That(selected.Content, Is.EqualTo(native.Content));
            }
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
        }

        [TestCase("stable")]
        [TestCase("formal-expiry")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task CompilerCoalesceUsesActualFilesAndFinalAuthority(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-coalesce-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("CoalesceCompilerInputs")));
                var owner = new Clock("right", true, path: "alias/Clock.cs");
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
                            if (ReferenceEquals(node, owner.Outer) || ReferenceEquals(node, owner.Inner)) { visits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(Expression(file), Is.EqualTo("x ?? y ?? z"));
                        if (mode == "formal-expiry") { owner.Leaf.Type = typeof(object); owner.Leaf.Type = typeof(string); }
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        Assert.That(allowed, Is.EqualTo(mode != "formal-expiry"));
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
                Assert.That(visits, Is.EqualTo(2));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
                Assert.That(Expression(selected), Is.EqualTo(mode is "stable" or "result-expiry" ? "x ?? y ?? z" : "x ?? (y ?? z)"));
                bool physicalInputsCurrent = mode is "stable" or "formal-expiry";
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(physicalInputsCurrent));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(physicalInputsCurrent),
                    "A false result requires discarding the isolated output even when shortened source was selected");
                var nativeOwner = new Clock("right", true, path: "alias/Clock.cs");
                nativeOwner.ProcessTypeForBackCompatibility();
                var native = new TypeProviderWriter(nativeOwner).Write();
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Content, final.Content);
                object?[][] args = [[null, null, "last"], [null, "middle", "last"], ["first", null, null], [null, null, null]];
                object?[] expected = ["last", "middle", "first", null];
                Assert.That(Execute(selected, args), Is.EqualTo(expected));
                Assert.That(Execute(final, args), Is.EqualTo(expected));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task CoalesceDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
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
            var owner = new Clock("right", true, observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "coalesce-cold", compilation, ["coalesce-cold"], () => true)
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
            int outerVisits = 0;
            int leftVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Outer))
                {
                    outerVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = true;
                }
                if (ReferenceEquals(node, owner.Outer.Left))
                {
                    leftVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(outerVisits, Is.EqualTo(1));
            Assert.That(leftVisits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withLegacy ? 2 : 1));
            Assert.That(Expression(selected), Is.EqualTo("x ?? y ?? z"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedLambdaRemainsOutsideTheCoalesceProfile(bool manual)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new ObservedClock(manual);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "coalesce-capture", compilation, ["coalesce-capture"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = written.SelectPreparedForms(true, true, prepared!.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written));
            Assert.That(prepared.PocGroupingCount, Is.Zero);
            Assert.That(written.PreparedGroupingCount, Is.Zero);
            Assert.That(selected.Content, Does.Contain("x ?? (y ?? z)"));
            object?[][] args = [[null, "middle", "last"]];
            var actual = Execute(selected, args).Single();
            if (manual) { Assert.That(actual, Is.EqualTo("manual")); }
            else { Assert.That(actual, Does.Contain("x ?? (y ?? z)")); }
            string unsafeEdit = selected.Content.Replace("x ?? (y ?? z)", "x ?? y ?? z", StringComparison.Ordinal);
            var changed = Execute(new CodeFile(unsafeEdit, selected.Name), args).Single();
            Assert.That(Equals(actual, changed), Is.EqualTo(manual));
        }

        [Test]
        public void CoalesceOperationOracleRejectsReassociation()
        {
            const string before = "public class Clock { public string M(string x, string y, string z) => x ?? (y ?? z); }";
            const string after = "public class Clock { public string M(string x, string y, string z) => (x ?? y) ?? z; }";
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(before, after));
        }
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("Task05CoalesceParent",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", arguments);
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private static string Expression(CodeFile file) => PreparedParenthesesTests.MethodExpression(file.Content).NormalizeWhitespace().ToFullString();
        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "coalesce-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }
        private sealed class Clock(string kind, bool arrow, string? defect = null, string path = "Clock.cs", Action? observePath = null) : TypeProvider
        {
            internal BinaryOperatorExpression Inner = null!;
            internal BinaryOperatorExpression Outer = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            internal ParameterProvider Leaf = null!;
            protected override string BuildRelativeFilePath()
            {
                observePath?.Invoke();
                return path;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                CSharpType parameter = defect == "object" ? typeof(object) : typeof(string);
                var x = new ParameterProvider("x", $"", defect == "nullable" ? parameter.WithNullable(true) : parameter, isRef: defect == "ref")
                {
                    HasUnsupportedParameterModifiers = defect == "unsupported-modifier"
                };
                Leaf = x;
                var y = new ParameterProvider("y", $"", parameter);
                var z = new ParameterProvider("z", $"", parameter, defaultValue: defect == "default" ? Literal("last") : null)
                {
                    HasUnsupportedDefaultValue = defect == "unsupported-default"
                };
                VariableExpression ownedX = x;
                ValueExpression left = defect switch
                {
                    "hint" => new VariableExpression(typeof(int), ownedX.Declaration),
                    "unbound" => new VariableExpression(typeof(string), "x"),
                    _ => ownedX
                };
                ValueExpression tail = defect == "nested" ? new BinaryOperatorExpression("??", z, x) : (VariableExpression)z;
                Inner = kind == "left" ? new BinaryOperatorExpression("??", left, y) : new BinaryOperatorExpression("??", y, tail, kind != "disabled");
                Outer = kind == "left" ? new BinaryOperatorExpression("??", Inner, z, false) : new BinaryOperatorExpression("??", left, Inner, kind == "root-pair");
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, parameter, null, [x, y, z])
                {
                    HasUnsupportedBaseContract = defect == "unsupported-base"
                };
                return [arrow ? new MethodProvider(signature, Outer, this) : new MethodProvider(signature, Return(Outer), this)];
            }
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
        private sealed class ObservedClock(bool manual) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(string));
                var y = new ParameterProvider("y", $"", typeof(string));
                var z = new ParameterProvider("z", $"", typeof(string));
                var expression = new BinaryOperatorExpression("??", x, new BinaryOperatorExpression("??", y, z), AddParentheses: false);
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(expression));
                var call = new InvokeMethodExpression(null, "Capture", manual ? [lambda, Literal("manual")] : new ValueExpression[] { lambda });
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: [new AttributeStatement(typeof(CallerArgumentExpressionAttribute), Literal("value"))]);
                var capture = new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null,
                    [new ParameterProvider("value", $"", typeof(Func<string>)), text]);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, [x, y, z]), call, this),
                    new MethodProvider(capture, (VariableExpression)text, this)];
            }
        }
    }
}

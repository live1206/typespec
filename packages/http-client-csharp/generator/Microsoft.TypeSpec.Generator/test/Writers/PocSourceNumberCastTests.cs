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
using Microsoft.CodeAnalysis.Operations;
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
    internal class PocSourceNumberCastTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
        private const string NumberSource = """
            namespace Fixtures
            {
                public readonly struct Number
                {
                    public readonly int Value;
                    public static int ConversionCount;
                    private Number(int value) { Value = value; }
                    public static explicit operator Number(int value)
                    {
                        ConversionCount++;
                        return new Number(unchecked(value * 3 + 7));
                    }
                }
            }
            """;

        [TestCase("arrow")]
        [TestCase("return")]
        [TestCase("receiver")]
        [TestCase("primitive-control")]
        [TestCase("implicit-operator")]
        [TestCase("checked-operator")]
        [TestCase("checked-options")]
        [TestCase("optional-formal")]
        [TestCase("restored-target")]
        public async Task NativeNumberCastKeepsItsActuallyResolvedConversionAndTypeSpelling(string mode)
        {
            var compilation = Compilation(mode);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-source-number-cast", compilation, ["source-number-cast"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int casts = 0;
            int operands = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { casts++; }
                if (ReferenceEquals(node, owner.Cast.Inner)) { operands++; }
            };
            bool restored = mode == "restored-target";
            int restores = 0;
            if (restored)
            {
                prepared.PocAfterOpening = () =>
                {
                    var type = owner.Cast.Type;
                    string original = type.Name;
                    type.Update(name: "Other");
                    type.Update(name: original);
                    restores++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            string source = SourceForMode(mode);
            PreparedParenthesesTests.AssertOperations(native.Content + "\n" + source, selected.Content + "\n" + source);
            PreparedParenthesesTests.AssertOperations(native.Content + "\n" + source, qualified.Content + "\n" + source);
            var original = ActualCast(native, mode);
            var actual = ActualCast(selected, mode);
            Assert.That(original.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(ActualCast(qualified, mode).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(actual.Type.NormalizeWhitespace().ToFullString(), Is.EqualTo(original.Type.NormalizeWhitespace().ToFullString()),
                "No Task04 type-shortening permission");
            Assert.That(actual.Expression.ToString(), Is.EqualTo("value"));
            Assert.That(casts, Is.EqualTo(1));
            Assert.That(operands, Is.EqualTo(1));
            Assert.That(restores, Is.EqualTo(restored ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored));
            int[] inputs = [int.MinValue, -3, 0, int.MaxValue];
            int[] expected = mode == "primitive-control" ? inputs : inputs.Select(value => unchecked(value * 3 + 7)).ToArray();
            Assert.That(Execute(native, inputs, mode), Is.EqualTo(expected));
            Assert.That(Execute(selected, inputs, mode), Is.EqualTo(expected));
            bool shorten = mode is "arrow" or "return" or "primitive-control";
            int expectedUse = shorten || restored ? 1 : 0;
            TestContext.WriteLine($"NUMBER-CAST mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; writtenType={actual.Type}; parent={actual.Parent?.Kind()}; visits={casts}/{operands}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUse));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(actual.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
            });
        }

        [Test]
        public async Task MissingCastTargetRetainsTheNativeWriteFailure()
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock("null-target");
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "number-missing-cast", compilation, ["number-missing-cast"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock("null-target");
            nativeOwner.ProcessTypeForBackCompatibility();
            var nativeError = Assert.Throws<ArgumentNullException>(() => new TypeProviderWriter(nativeOwner).Write());
            TestContext.WriteLine($"NUMBER-NULL-TARGET native write={nativeError!.GetType().Name}; parameter={nativeError.ParamName}");
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            // Malformed IR is not accepted output, but preparation must not
            // replace the inherited native writer failure for this type slot.
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.Zero);
            int visits = 0;
            prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Cast)) { visits++; } };
            var preparedError = Assert.Throws<ArgumentNullException>(() => new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write());
            Assert.That(preparedError!.ParamName, Is.EqualTo(nativeError.ParamName));
            Assert.That(visits, Is.EqualTo(1));
        }

        [TestCase("arrow", "stable")]
        [TestCase("return", "stable")]
        [TestCase("arrow", "source-expiry")]
        [TestCase("return", "result-expiry")]
        public async Task CompilerSourceNumberCastUsesActualSelectedFilesAndResult(string role, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-source-number-cast-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Number.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, NumberSource);
            try
            {
                var native = await NativeFinalControl(role, sandbox, source);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(Compilation(role)));
                var owner = new Clock(role, "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                int uses = 0;
                int castVisits = 0;
                int operandVisits = 0;
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        Assert.That(proof!.Compilation.SyntaxTrees.Any(tree => tree.FilePath == source), Is.True);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                            if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(CastSyntax(file).Parent, Is.Not.InstanceOf<ParenthesizedExpressionSyntax>());
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        Assert.That(allowed, Is.True);
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
                Assert.That(castVisits, Is.EqualTo(1));
                Assert.That(operandVisits, Is.EqualTo(1));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(CastSyntax(selected).Parent is ParenthesizedExpressionSyntax, Is.EqualTo(mode == "source-expiry"));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires discard even after short selection");
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Selected.Content + "\n" + NumberSource, selected.Content + "\n" + NumberSource);
                PreparedParenthesesTests.AssertOperations(native.Final.Content + "\n" + NumberSource, final.Content + "\n" + NumberSource);
                int[] inputs = [int.MinValue, -3, 0, int.MaxValue];
                Assert.That(Execute(selected, inputs, role), Is.EqualTo(Execute(native.Selected, inputs, role)));
                Assert.That(Execute(final, inputs, role), Is.EqualTo(Execute(native.Final, inputs, role)));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> NativeFinalControl(string role, string sandbox, string source)
        {
            var original = Path.Combine(sandbox, "native-original");
            var isolated = Path.Combine(sandbox, "native-isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            var config = Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(Compilation(role)));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(new Clock(role, "alias/Clock.cs")));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            var pipeline = new CSharpGen { PocSelectedFileObserved = file => selected = file };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "This disabled control is only for stage-aligned comparison");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
            Assert.That(CastSyntax(selected).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task NumberCastDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
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
            var owner = new Clock("arrow", observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "source-number-cast-cold", compilation, ["source-number-cast-cold"], () => true)
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
            int castVisits = 0;
            int leftVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast))
                {
                    castVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = true;
                }
                if (ReferenceEquals(node, owner.Cast.Inner))
                {
                    leftVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(castVisits, Is.EqualTo(1));
            Assert.That(leftVisits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
            // The metadata proof is intentionally single-owner; the separate
            // legacy control keeps its own choice in the mixed case.
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(withLegacy ? 0 : 1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(CastSyntax(selected).Parent is ParenthesizedExpressionSyntax, Is.EqualTo(withLegacy));
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<CastExpressionSyntax>().Single();

        private static CastExpressionSyntax ActualCast(CodeFile file, string mode)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation(mode).AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var cast = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var operation = model.GetOperation(cast);
            Assert.That(operation, Is.InstanceOf<IConversionOperation>());
            var op = (IConversionOperation)operation!;
            if (mode == "primitive-control") { Assert.That(op.OperatorMethod, Is.Null); }
            else
            {
                Assert.That(op.Conversion.IsUserDefined, Is.True);
                Assert.That(op.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Is.EqualTo("global::Fixtures.Number"));
                Assert.That(SymbolEqualityComparer.Default.Equals(op.Type!.ContainingAssembly, compilation.Assembly), Is.True);
                Assert.That(op.OperatorMethod?.Name, Is.EqualTo(mode == "implicit-operator" ? "op_Implicit" : "op_Explicit"));
                Assert.That(op.OperatorMethod?.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(SymbolEqualityComparer.Default.Equals(op.OperatorMethod!.ContainingType, op.Type), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(op.OperatorMethod!.ReturnType, op.Type), Is.True);
                Assert.That(cast.Type.NormalizeWhitespace().ToFullString(), Is.EqualTo("global::Fixtures.Number"));
                var parent = cast.Parent is ParenthesizedExpressionSyntax pair ? pair.Parent : cast.Parent;
                if (mode != "receiver")
                {
                    var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                    Assert.That(parent, mode == "return" ? Is.InstanceOf<ReturnStatementSyntax>() : Is.InstanceOf<ArrowExpressionClauseSyntax>());
                    ExpressionSyntax returned = mode == "return"
                        ? ((ReturnStatementSyntax)method.Body!.Statements.Single()).Expression!
                        : method.ExpressionBody!.Expression;
                    Assert.That(model.GetTypeInfo(returned).ConvertedType?.SpecialType, Is.EqualTo(SpecialType.System_Object));
                    Assert.That(compilation.ClassifyConversion(op.Type!, compilation.GetSpecialType(SpecialType.System_Object)).IsBoxing, Is.True);
                }
                else { Assert.That(parent, Is.InstanceOf<MemberAccessExpressionSyntax>()); }
            }
            return cast;
        }
        private static string SourceForMode(string mode) => mode switch
        {
            "implicit-operator" => NumberSource.Replace("explicit operator Number", "implicit operator Number", StringComparison.Ordinal),
            "checked-operator" => NumberSource.Replace("public static explicit operator Number(int value)",
                "public static explicit operator checked Number(int value) => new Number(17);\npublic static explicit operator Number(int value)", StringComparison.Ordinal),
            _ => NumberSource
        };
        private static CSharpCompilation Compilation(string mode = "arrow") => CSharpCompilation.Create("NativeNumberCast",
            [CSharpSyntaxTree.ParseText(SourceForMode(mode), path: "Number.cs")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: mode == "checked-options"));
        private static int[] Execute(CodeFile file, int[] inputs, string mode)
        {
            var values = PreparedGeneratedRuntime.Invoke(Compilation(mode).AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)),
                "Sample.Clock", "M", inputs.Select(value => new object?[] { value }).ToArray());
            if (mode is not ("receiver" or "primitive-control"))
            {
                Assert.That(values.Select(value => value?.GetType().FullName), Is.All.EqualTo("Fixtures.Number"));
                var type = values[0]!.GetType();
                Assert.That(type.GetField("ConversionCount")!.GetValue(null), Is.EqualTo(inputs.Length),
                    "The actual user-defined conversion runs once per call");
                return values.Select(value => (int)type.GetField("Value")!.GetValue(value)!).ToArray();
            }
            Assert.That(values, Is.All.InstanceOf<int>());
            return values.Cast<int>().ToArray();
        }
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
            public CompilerBuildInputCapture Capture => new(directory, "source-number-cast-captured", "Debug|net10.0", "12.0", "Actual", [],
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
            internal CastExpression Cast = null!;
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
                var value = new ParameterProvider("value", $"", typeof(int), defaultValue: mode == "optional-formal" ? Literal(0) : null);
                CSharpType target = mode switch
                {
                    "null-target" => null!,
                    "primitive-control" => typeof(int),
                    _ => new CSharpType("Number", "Fixtures", isValueType: true, isNullable: false, declaringType: null,
                        args: [], isPublic: true, isStruct: true)
                };
                Cast = new CastExpression(value, target);
                var returns = mode is "receiver" or "primitive-control" ? typeof(int) : typeof(object);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, returns, null, [value]);
                return [mode == "return" ? new MethodProvider(signature, Return(Cast), this) :
                    new MethodProvider(signature, mode == "receiver" ? new MemberExpression(Cast, "Value") : Cast, this)];
            }
        }
    }
}

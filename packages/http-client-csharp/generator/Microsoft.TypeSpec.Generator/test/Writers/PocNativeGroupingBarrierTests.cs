// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    internal class PocNativeGroupingBarrierTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("generic-ambiguity")]
        [TestCase("relational-sibling")]
        [TestCase("cast-relational")]
        [TestCase("sibling-positive")]
        [TestCase("indexer")]
        [TestCase("optional-default")]
        [TestCase("datetime-receiver")]
        [TestCase("checked-direct")]
        [TestCase("checked-binary")]
        [TestCase("checked-binary-right")]
        [TestCase("checked-binary-minus")]
        [TestCase("checked-binary-restore")]
        [TestCase("unchecked-binary")]
        public async Task NativeGroupingRespectsTheExactWriterOwnedDelimiters(string mode)
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
                    "task05-exact-native-barriers", compilation, ["exact-native-barriers"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int visits = 0;
            int operands = 0;
            if (prepared is not null)
            {
                prepared.PocNativeWriteObserver = node =>
                {
                    if (owner.Observed.Any(expression => ReferenceEquals(node, expression))) { visits++; }
                    if (owner.Cast is { } cast && ReferenceEquals(node, cast.Inner)) { operands++; }
                };
            }
            bool restored = mode == "checked-binary-restore";
            int restores = 0;
            if (restored)
            {
                prepared!.PocAfterOpening = () =>
                {
                    var declaration = ((VariableExpression)owner.Cast!.Inner).Declaration;
                    var name = declaration.RequestedName;
                    declaration.Update("otherValue");
                    declaration.Update(name);
                    restores++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            PreparedParenthesesTests.AssertOperations(native.Content, qualified.Content);
            var syntax = Tree(selected).GetRoot();
            if (mode is "generic-ambiguity" or "relational-sibling" or "cast-relational" or "sibling-positive")
            {
                var call = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var args = call.ArgumentList.Arguments;
                Assert.That(args.Count, Is.EqualTo(2));
                var tree = call.SyntaxTree;
                var symbol = Compilation().AddSyntaxTrees(tree).GetSemanticModel(tree).GetSymbolInfo(call).Symbol;
                Assert.That(symbol, Is.InstanceOf<IMethodSymbol>());
                Assert.That(symbol!.Name, Is.EqualTo("Echo"));
                string expectedCall = mode switch
                {
                    "generic-ambiguity" => "Echo((a < b), (c > d))",
                    "relational-sibling" => "Echo(a < b, (x + y))",
                    "cast-relational" => "Echo(((int)value) < 1, 0 > 1)",
                    _ => "Echo(a + b, c + d)"
                };
                Assert.That(call.NormalizeWhitespace().ToFullString(), Is.EqualTo(expectedCall));
            }
            else if (mode == "indexer")
            {
                var access = syntax.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
                var tree = access.SyntaxTree;
                var property = Compilation().AddSyntaxTrees(tree).GetSemanticModel(tree).GetSymbolInfo(access).Symbol;
                Assert.That(property, Is.InstanceOf<IPropertySymbol>());
                Assert.That(((IPropertySymbol)property!).IsIndexer, Is.True);
                Assert.That(access.NormalizeWhitespace().ToFullString(), Is.EqualTo("this[((int)value)]"));
            }
            else if (mode == "optional-default")
            {
                var parameter = syntax.DescendantNodes().OfType<ParameterSyntax>().Single();
                Assert.That(parameter.Default!.Value.NormalizeWhitespace().ToFullString(), Is.EqualTo("((int)0)"));
            }
            else if (mode == "datetime-receiver")
            {
                var cast = syntax.DescendantNodes().OfType<CastExpressionSyntax>().Single();
                Assert.That(cast.Parent?.Parent, Is.InstanceOf<MemberAccessExpressionSyntax>());
                Assert.That(cast.Type.ToString(), Is.EqualTo("global::System.DateTimeOffset"));
            }
            else
            {
                AssertChecked(native, mode);
                AssertChecked(qualified, mode);
                AssertChecked(selected, mode);
            }

            object?[][] inputs = mode switch
            {
                "generic-ambiguity" or "relational-sibling" or "sibling-positive" => [[1, 2, 3, 4], [5, 0, 4, 1]],
                "checked-direct" => [[(long)int.MinValue], [(long)int.MaxValue]],
                "checked-binary" or "checked-binary-right" or "checked-binary-restore" => [[(long)int.MinValue, 1], [(long)int.MaxValue, -1]],
                "checked-binary-minus" => [[(long)int.MinValue, -1], [(long)int.MaxValue, 1]],
                "unchecked-binary" => [[(long)int.MaxValue + 1, 0], [(long)int.MaxValue, 1]],
                "optional-default" => [[Type.Missing], [-2]],
                _ => [[0], [1], [int.MaxValue]]
            };
            Assert.That(Execute(selected, inputs), Is.EqualTo(Execute(native, inputs)));
            if (mode.StartsWith("checked-", StringComparison.Ordinal))
            {
                object?[][] conversionOverflow = mode == "checked-direct" ? [[(long)int.MaxValue + 1]] : [[(long)int.MaxValue + 1, 0]];
                AssertOverflow(native, conversionOverflow);
                AssertOverflow(selected, conversionOverflow);
                if (mode != "checked-direct")
                {
                    object?[][] arithmeticOverflow = [[(long)int.MaxValue, mode == "checked-binary-minus" ? -1 : 1]];
                    AssertOverflow(native, arithmeticOverflow);
                    AssertOverflow(selected, arithmeticOverflow);
                }
            }
            Assert.That(restores, Is.EqualTo(restored ? 1 : 0));
            int count = mode switch { "checked-binary" or "checked-binary-restore" => 1, "sibling-positive" => 2, _ => 0 };
            if (prepared is not null)
            {
                Assert.That(visits, Is.EqualTo(owner.Observed.Length));
                if (owner.Cast is not null) { Assert.That(operands, Is.EqualTo(1)); }
            }
            if (count > 0) { Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored)); }
            TestContext.WriteLine($"NATIVE-BARRIER mode={mode}; plans={prepared?.PocGroupingCount ?? 0}; uses={prepared?.PocGroupingUses ?? 0}; raw={raw.PreparedGroupingCount}; visits={visits}/{operands}; receipt={raw.SourceConsumption?.WasCurrentAtEndOfWrite}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared?.PocGroupingCount ?? 0, Is.EqualTo(count));
                Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(count));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(count));
                if (mode.Contains("checked-binary", StringComparison.Ordinal))
                {
                    Assert.That(syntax.DescendantNodes().OfType<CastExpressionSyntax>().Single().Parent is BinaryExpressionSyntax,
                        Is.EqualTo(mode == "checked-binary"));
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeReassociationBarriersPreserveObservedArithmetic(bool checkedArithmetic)
        {
            string mode = checkedArithmetic ? "checked-reassociation" : "floating-reassociation";
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-native-reassociation-barrier", compilation, ["native-reassociation-barrier"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int visits = 0;
            if (prepared is not null)
            {
                prepared.PocNativeWriteObserver = node => { if (owner.Observed.Any(expression => ReferenceEquals(node, expression))) { visits++; } };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
            Assert.That(raw.PreparedGroupingCount, Is.Zero);
            if (prepared is not null) { Assert.That(visits, Is.EqualTo(2)); }
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            var body = Tree(selected).GetRoot().DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression;
            var binary = checkedArithmetic ? (BinaryExpressionSyntax)((CheckedExpressionSyntax)body).Expression : (BinaryExpressionSyntax)body;
            Assert.That(binary.Right, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            var flattened = new CodeFile(native.Content.Replace("(y + z)", "y + z", StringComparison.Ordinal), native.Name);
            Assert.That(flattened.Content, Is.Not.EqualTo(native.Content));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, flattened.Content));
            if (checkedArithmetic)
            {
                object?[][] values = [[int.MinValue, int.MaxValue, 1]];
                AssertOverflow(native, values);
                AssertOverflow(selected, values);
                Assert.That(Execute(flattened, values), Is.EqualTo(new object?[] { 0 }));
                var model = Compilation().AddSyntaxTrees(binary.SyntaxTree).GetSemanticModel(binary.SyntaxTree);
                var inner = ((ParenthesizedExpressionSyntax)binary.Right).Expression;
                Assert.That(((IBinaryOperation)model.GetOperation(binary)!).IsChecked, Is.True);
                Assert.That(((IBinaryOperation)model.GetOperation(inner)!).IsChecked, Is.True);
            }
            else
            {
                object?[][] values = [[1e20, -1e20, 3d]];
                Assert.That(Execute(selected, values), Is.EqualTo(new object?[] { 0d }));
                Assert.That(Execute(flattened, values), Is.EqualTo(new object?[] { 3d }));
            }
        }

        [TestCase("stable")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task CompilerCheckedCastUsesActualSelectedFilesAndResult(string mode)
        {
            const string role = "checked-binary";
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-checked-binary-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var native = await NativeFinalControl(role, sandbox, source);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("CheckedBinaryCompilerInputs")));
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
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                            if (ReferenceEquals(node, owner.Cast!.Inner)) { operandVisits++; }
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
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, final.Content);
                object?[][] values = [[(long)int.MaxValue, -1], [(long)int.MinValue, 1]];
                Assert.That(Execute(selected, values), Is.EqualTo(Execute(native.Selected, values)));
                Assert.That(Execute(final, values), Is.EqualTo(Execute(native.Final, values)));
                AssertChecked(selected, role);
                AssertOverflow(final, [[(long)int.MaxValue + 1, 0]]);
                AssertOverflow(final, [[(long)int.MaxValue, 1]]);
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
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeCheckedBinaryInputs")));
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
        public async Task CheckedBinaryDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
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
            var owner = new Clock("checked-binary", observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "checked-binary-cold", compilation, ["checked-binary-cold"], () => true)
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
                if (ReferenceEquals(node, owner.Cast!.Inner))
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
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withLegacy ? 2 : 1));
            Assert.That(CastSyntax(selected).Parent, Is.Not.InstanceOf<ParenthesizedExpressionSyntax>());
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => Tree(file).GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();

        private static void AssertChecked(CodeFile file, string mode)
        {
            var tree = Tree(file);
            var syntax = tree.GetRoot();
            var comp = Compilation().AddSyntaxTrees(tree);
            Assert.That(comp.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var model = comp.GetSemanticModel(tree);
            var checkedExpression = syntax.DescendantNodes().OfType<CheckedExpressionSyntax>().Single();
            Assert.That(checkedExpression.OpenParenToken.IsMissing, Is.False);
            Assert.That(checkedExpression.CloseParenToken.IsMissing, Is.False);
            var cast = syntax.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var boundCast = (IConversionOperation)model.GetOperation(cast)!;
            Assert.That(boundCast.IsChecked, Is.EqualTo(mode != "unchecked-binary"));
            Assert.That(boundCast.Operand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int64));
            Assert.That(boundCast.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            if (mode == "checked-direct") { Assert.That(checkedExpression.Expression.Span, Is.EqualTo(cast.Span)); }
            else
            {
                var binary = (IBinaryOperation)model.GetOperation(checkedExpression.Expression)!;
                Assert.That(binary.IsChecked, Is.EqualTo(mode != "unchecked-binary"));
                Assert.That(binary.OperatorKind, Is.EqualTo(mode == "checked-binary-minus" ? BinaryOperatorKind.Subtract : BinaryOperatorKind.Add));
            }
        }
        private static void AssertOverflow(CodeFile file, object?[][] args) =>
            Assert.That(Assert.Throws<TargetInvocationException>(() => Execute(file, args))!.InnerException, Is.InstanceOf<OverflowException>());
        private static SyntaxTree Tree(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("NativeGroupingBarriers",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] args) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(Tree(file)), "Sample.Clock", "M", args);
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
            public CompilerBuildInputCapture Capture => new(directory, "checked-binary-captured", "Debug|net10.0", "12.0", "Actual", [],
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
            internal ValueExpression[] Observed = [];
            internal CastExpression? Cast;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                observePath?.Invoke();
                return path;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override PropertyProvider[] BuildProperties()
            {
                if (mode != "indexer") { return []; }
                var index = new ParameterProvider("index", $"", typeof(int));
                return [new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(object), index,
                    new ExpressionPropertyBody((VariableExpression)index), this)];
            }
            protected internal override MethodProvider[] BuildMethods()
            {
                if (mode is "checked-reassociation" or "floating-reassociation")
                {
                    bool checkedArithmetic = mode == "checked-reassociation";
                    CSharpType type = checkedArithmetic ? typeof(int) : typeof(double);
                    var parameters = new[] { "x", "y", "z" }.Select(name => new ParameterProvider(name, $"", type)).ToArray();
                    var inner = new BinaryOperatorExpression("+", parameters[1], parameters[2]);
                    var outer = new BinaryOperatorExpression("+", parameters[0], inner, AddParentheses: checkedArithmetic);
                    Observed = [outer, inner];
                    ValueExpression expression = checkedArithmetic ? new KeywordExpression("checked", outer) : outer;
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, type, null, parameters), expression, this)];
                }
                if (mode is "generic-ambiguity" or "relational-sibling" or "sibling-positive")
                {
                    var names = mode == "relational-sibling" ? new[] { "a", "b", "x", "y" } : ["a", "b", "c", "d"];
                    var formals = names.Select(name => new ParameterProvider(name, $"", typeof(int))).ToArray();
                    var first = new BinaryOperatorExpression(mode == "sibling-positive" ? "+" : "<", formals[0], formals[1], mode != "relational-sibling");
                    var second = new BinaryOperatorExpression(mode == "generic-ambiguity" ? ">" : "+", formals[2], formals[3]);
                    Observed = [first, second];
                    return Call(formals, first, second);
                }
                bool arithmetic = mode.Contains("checked-", StringComparison.Ordinal);
                bool binary = mode.Contains("checked-binary", StringComparison.Ordinal);
                var value = new ParameterProvider("value", $"", arithmetic ? typeof(long) : typeof(int));
                if (mode == "optional-default")
                {
                    Cast = new CastExpression(Literal(0), typeof(int));
                    Observed = [Cast];
                    value.DefaultValue = Cast;
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [value]),
                        (VariableExpression)value, this)];
                }
                Cast = mode == "datetime-receiver" ? new CastExpression(Default, typeof(DateTimeOffset)) : new CastExpression(value, typeof(int));
                Observed = [Cast];
                if (mode == "cast-relational")
                {
                    return Call([value], new BinaryOperatorExpression("<", Cast, Literal(1), false), new BinaryOperatorExpression(">", Literal(0), Literal(1), false));
                }
                var other = new ParameterProvider("other", $"", typeof(int));
                ValueExpression body = mode switch
                {
                    "indexer" => new IndexerExpression(This, Cast),
                    "datetime-receiver" => new InvokeMethodExpression(Cast, "ToString", Array.Empty<ValueExpression>()),
                    "checked-binary" or "checked-binary-restore" => new KeywordExpression("checked", new BinaryOperatorExpression("+", Cast, other)),
                    "checked-binary-right" => new KeywordExpression("checked", new BinaryOperatorExpression("+", other, Cast)),
                    "checked-binary-minus" => new KeywordExpression("checked", new BinaryOperatorExpression("-", Cast, other)),
                    "unchecked-binary" => new KeywordExpression("unchecked", new BinaryOperatorExpression("+", Cast, other)),
                    _ => new KeywordExpression("checked", Cast)
                };
                CSharpType result = arithmetic ? typeof(int) : typeof(object);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, binary ? [value, other] : [value]), body, this)];
            }
            private MethodProvider[] Call(ParameterProvider[] formals, ValueExpression first, ValueExpression second)
            {
                bool boolean = mode is "generic-ambiguity" or "cast-relational";
                CSharpType result = boolean ? typeof(bool) : typeof(int);
                var left = new ParameterProvider("left", $"", mode == "sibling-positive" ? typeof(int) : typeof(bool));
                var right = new ParameterProvider("right", $"", result);
                ValueExpression calleeBody = mode == "relational-sibling" ? (VariableExpression)right :
                    new BinaryOperatorExpression(boolean ? "&&" : "+", left, right, false);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, formals),
                    new InvokeMethodExpression(null, "Echo", new ValueExpression[] { first, second }), this),
                    new MethodProvider(new MethodSignature("Echo", null, MethodSignatureModifiers.Private, result, null, [left, right]), calleeBody, this)];
            }
        }
    }
}

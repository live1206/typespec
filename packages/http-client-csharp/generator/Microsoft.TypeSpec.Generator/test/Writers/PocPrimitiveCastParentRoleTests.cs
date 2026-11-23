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
    internal class PocPrimitiveCastParentRoleTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("local")]
        [TestCase("binary-left")]
        [TestCase("conditional-expression")]
        [TestCase("unary-parent")]
        [TestCase("invocation-receiver")]
        [TestCase("direct-int-control")]
        [TestCase("local-alias")]
        [TestCase("local-shadow")]
        [TestCase("local-restored")]
        [TestCase("binary-right")]
        [TestCase("binary-root-pair")]
        [TestCase("binary-long-operand")]
        [TestCase("conditional-object")]
        public async Task NativePrimitiveCastOmissionRequiresTheExactParentRole(string mode)
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
                    "native-primitive-cast-role", compilation, ["native-primitive-cast-role"], () => true)
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
            int operandVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { visits++; }
                if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
            };
            bool restored = mode == "local-restored";
            int restores = 0;
            if (restored)
            {
                prepared.PocAfterOpening = () =>
                {
                    var declaration = owner.Local.Declaration;
                    var old = declaration.RequestedName;
                    declaration.Update("other");
                    declaration.Update(old);
                    restores++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            Assert.That(CastSyntax(qualified).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            PreparedParenthesesTests.AssertOperations(native.Content, qualified.Content);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = mode switch
            {
                "conditional-expression" or "conditional-object" => [[false], [true]],
                "binary-left" or "binary-right" or "binary-root-pair" => [[int.MaxValue, 2], [int.MinValue, -2]],
                "binary-long-operand" => [[(long)int.MaxValue, 2], [(long)int.MinValue, -2]],
                _ => [[int.MinValue], [0], [int.MaxValue]]
            };
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
            Assert.That(CastSyntax(native).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            var cast = CastSyntax(selected);
            Assert.That(cast.Type.NormalizeWhitespace().ToFullString(), Is.EqualTo(CastSyntax(native).Type.NormalizeWhitespace().ToFullString()));
            Assert.That(cast.Expression.NormalizeWhitespace().ToFullString(), Is.EqualTo("value"));
            Assert.That(visits, Is.EqualTo(1));
            Assert.That(operandVisits, Is.EqualTo(1));
            Assert.That(restores, Is.EqualTo(restored ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored));
            bool shorten = mode is "local" or "local-alias" or "binary-left" or "binary-root-pair" or "conditional-expression" or "direct-int-control";
            int expectedUses = shorten || restored ? 1 : 0;
            if (mode == "binary-root-pair")
            {
                Assert.That(cast.Parent?.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>(), "The binary root keeps its own pair");
            }
            TestContext.WriteLine($"CAST-PARENT mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; physical={raw.PreparedGroupingCount}; actualParent={cast.Parent?.Kind()}; visits={visits}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expectedUses));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUses));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expectedUses));
                Assert.That(cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
            });
        }

        [TestCase("local", "stable")]
        [TestCase("binary-left", "stable")]
        [TestCase("local", "source-expiry")]
        [TestCase("binary-left", "result-expiry")]
        public async Task CompilerCastParentUsesActualSelectedFilesAndResult(string role, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-primitive-parent-compiler", Guid.NewGuid().ToString("N"));
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
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("PrimitiveParentCompilerInputs")));
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
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, final.Content);
                object?[][] values = role == "local" ? [[int.MinValue], [int.MaxValue]] : [[int.MaxValue, 2], [int.MinValue, -2]];
                Assert.That(Execute(selected, values), Is.EqualTo(Execute(native.Selected, values)));
                Assert.That(Execute(final, values), Is.EqualTo(Execute(native.Final, values)));
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
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativePrimitiveParentInputs")));
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

        [TestCase("(long)(value + other)")]
        [TestCase("(int)value + other")]
        [TestCase("(long)other + other")]
        public void CastOperationOracleRejectsCompilableAssociationTargetAndOperandCorruption(string expression)
        {
            const string prefix = "namespace Sample { public class Clock { public long M(int value, int other) => ";
            const string suffix = "; } }";
            var native = new CodeFile(prefix + "((long)value) + other" + suffix, "Clock.cs");
            var changed = new CodeFile(prefix + expression + suffix, "Clock.cs");
            object?[][] inputs = [[int.MaxValue, 2], [int.MinValue, -2]];
            Assert.That(Execute(native, inputs), Is.EqualTo(new object?[] { (long)int.MaxValue + 2, (long)int.MinValue - 2 }));
            Assert.That(Execute(changed, inputs), Is.Not.EqualTo(Execute(native, inputs)));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, changed.Content));
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task PrimitiveCastDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
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
            var owner = new Clock("local", observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "primitive-cast-cold", compilation, ["primitive-cast-cold"], () => true)
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
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withLegacy ? 2 : 1));
            Assert.That(CastSyntax(selected).Parent, Is.Not.InstanceOf<ParenthesizedExpressionSyntax>());
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<CastExpressionSyntax>().Single();
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("PrimitiveCastParentRole",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", arguments);
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
            public CompilerBuildInputCapture Capture => new(directory, "primitive-parent-captured", "Debug|net10.0", "12.0", "Actual", [],
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
                var value = new ParameterProvider("value", $"", mode switch
                {
                    "conditional-expression" => typeof(bool), "conditional-object" => typeof(object),
                    "binary-long-operand" => typeof(long), _ => typeof(int)
                });
                CSharpType target = mode switch { "conditional-expression" or "conditional-object" => typeof(bool), "direct-int-control" => typeof(int), _ => typeof(long) };
                Cast = new CastExpression(value, target);
                var other = new ParameterProvider("other", $"", typeof(int));
                CSharpType returns = mode switch { "conditional-expression" or "conditional-object" or "direct-int-control" => typeof(int),
                    "invocation-receiver" => typeof(string), _ => typeof(long) };
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, returns, null,
                    mode.StartsWith("binary-", StringComparison.Ordinal) ? [value, other] : [value]);
                if (mode.StartsWith("local", StringComparison.Ordinal))
                {
                    var declaration = new DeclarationExpression(typeof(long), mode == "local-shadow" ? "value" : "result", out var result);
                    Local = result;
                    var returned = mode == "local-alias" ? new VariableExpression(typeof(long), result.Declaration) : result;
                    return [new MethodProvider(signature, new MethodBodyStatements([new AssignmentExpression(declaration, Cast).Terminate(), Return(returned)]), this)];
                }
                ValueExpression body = mode switch
                {
                    "binary-left" or "binary-root-pair" or "binary-long-operand" => new BinaryOperatorExpression("+", Cast, other, AddParentheses: mode == "binary-root-pair"),
                    "binary-right" => new BinaryOperatorExpression("+", other, Cast, AddParentheses: false),
                    "conditional-expression" or "conditional-object" => new TernaryConditionalExpression(Cast, Literal(1), Literal(2)),
                    "unary-parent" => new UnaryOperatorExpression("-", Cast, OperandOnTheLeft: false),
                    "invocation-receiver" => new InvokeMethodExpression(Cast, "ToString", Array.Empty<ValueExpression>()),
                    _ => Cast
                };
                return [new MethodProvider(signature, body, this)];
            }
        }
    }
}

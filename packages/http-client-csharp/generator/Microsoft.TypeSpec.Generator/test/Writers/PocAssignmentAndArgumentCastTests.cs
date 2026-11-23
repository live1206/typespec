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
    internal class PocAssignmentAndArgumentCastTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("assignment")]
        [TestCase("argument")]
        [TestCase("unused-assignment")]
        [TestCase("competing-callee")]
        [TestCase("initializer-control")]
        [TestCase("assignment-alias")]
        [TestCase("assignment-shadow")]
        [TestCase("assignment-restored")]
        [TestCase("argument-restored")]
        [TestCase("argument-narrowing")]
        [TestCase("argument-optional")]
        public async Task NativeCastUsesExactAssignmentOrArgumentRole(string mode)
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
                    "task05-assignment-argument-cast", compilation, ["assignment-argument-cast"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int castVisits = 0;
            int operandVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
            };
            bool restored = mode is "assignment-restored" or "argument-restored";
            int restores = 0;
            if (restored)
            {
                prepared.PocAfterOpening = () =>
                {
                    if (mode == "assignment-restored")
                    {
                        var declaration = owner.Local.Declaration;
                        var old = declaration.RequestedName;
                        declaration.Update("other");
                        declaration.Update(old);
                    }
                    else
                    {
                        var old = owner.Call.Arguments;
                        owner.Call.Update(arguments: old.ToArray());
                        owner.Call.Update(arguments: old);
                    }
                    restores++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            Assert.That(CastSyntax(native).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(CastSyntax(qualified).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            PreparedParenthesesTests.AssertOperations(native.Content, qualified.Content);
            object?[][] arguments = mode == "argument-narrowing" ? [[(long)int.MaxValue + 2], [long.MaxValue], [long.MinValue]] :
                [[int.MinValue], [0], [int.MaxValue]];
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
            Assert.That(castVisits, Is.EqualTo(1));
            // The unused-local control returns the same formal carrier, so
            // its cast operand and return are distinct native writes.
            Assert.That(operandVisits, Is.EqualTo(mode == "unused-assignment" ? 2 : 1));
            Assert.That(restores, Is.EqualTo(restored ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored));
            if (IsArgument(mode))
            {
                var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
                var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                Assert.That(invocation.ArgumentList.OpenParenToken.IsMissing, Is.False);
                Assert.That(invocation.ArgumentList.CloseParenToken.IsMissing, Is.False);
                Assert.That(invocation.ArgumentList.Arguments.Count, Is.EqualTo(1));
                var actualCompilation = Compilation().AddSyntaxTrees(tree);
                Assert.That(actualCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
                var symbol = actualCompilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol;
                Assert.That(symbol, Is.InstanceOf<IMethodSymbol>());
                var method = (IMethodSymbol)symbol!;
                Assert.That(method.Name, Is.EqualTo("Echo"));
                Assert.That(method.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            }
            else
            {
                var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
                var root = tree.GetRoot();
                var declaration = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(declaration.Declaration.Type.ToString(), Is.EqualTo("long"));
                var local = declaration.Declaration.Variables.Single();
                var actualName = local.Identifier.ValueText;
                if (mode == "assignment-shadow") { Assert.That(actualName, Is.Not.EqualTo("value")); }
                else { Assert.That(actualName, Is.EqualTo("result")); }
                if (mode != "initializer-control")
                {
                    Assert.That(local.Initializer, Is.Null);
                    var assignment = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
                    Assert.That(assignment.Left.ToString(), Is.EqualTo(actualName));
                    Assert.That(assignment.IsKind(SyntaxKind.SimpleAssignmentExpression), Is.True);
                }
                var returned = root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
                Assert.That(returned.Expression!.ToString(), Is.EqualTo(mode == "unused-assignment" ? "value" : actualName));
            }
            bool shorten = mode is "assignment" or "assignment-alias" or "argument" or "initializer-control";
            int expectedUse = shorten || restored ? 1 : 0;
            var cast = CastSyntax(selected);
            Assert.That(cast.Type.NormalizeWhitespace().ToFullString(), Is.EqualTo(CastSyntax(native).Type.NormalizeWhitespace().ToFullString()));
            Assert.That(cast.Expression.NormalizeWhitespace().ToFullString(), Is.EqualTo("value"));
            TestContext.WriteLine($"CAST-SLOT mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; actual={cast.Parent?.Kind()}; visits={castVisits}/{operandVisits}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUse));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
            });
        }

        [TestCase("(int)value", false)]
        [TestCase("(long)(value + 1)", true)]
        public void AssignmentOracleRejectsCompilableTargetAndOperandCorruption(string expression, bool changesRuntime)
        {
            const string prefix = "namespace Sample { public class Clock { public long M(int value) { long result; result = ";
            const string suffix = "; return result; } } }";
            var native = new CodeFile(prefix + "((long)value)" + suffix, "Clock.cs");
            var changed = new CodeFile(prefix + expression + suffix, "Clock.cs");
            object?[][] inputs = [[int.MaxValue], [0], [int.MinValue]];
            var actual = Execute(native, inputs);
            var corrupted = Execute(changed, inputs);
            Assert.That(corrupted.SequenceEqual(actual), Is.EqualTo(!changesRuntime));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, changed.Content));
        }

        [TestCase("assignment", "stable")]
        [TestCase("argument", "stable")]
        [TestCase("assignment", "source-expiry")]
        [TestCase("argument", "result-expiry")]
        public async Task CompilerAssignmentArgumentCastUsesActualSelectedFilesAndResult(string role, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-assignment-argument-cast-compiler", Guid.NewGuid().ToString("N"));
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
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("AssignmentArgumentCompilerInputs")));
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
                object?[][] values = [[int.MinValue], [0], [int.MaxValue]];
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
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeAssignmentArgumentInputs")));
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

        [TestCase("assignment", "path", false)]
        [TestCase("argument", "path", false)]
        [TestCase("assignment", "path", true)]
        [TestCase("argument", "path", true)]
        [TestCase("assignment", "serialization", false)]
        [TestCase("assignment", "serialization", true)]
        public async Task AssignmentArgumentCastDoesNotFirstLoadASecondPhaseDependency(string role, string dependency, bool withLegacy)
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
            var owner = new Clock(role, observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "assignment-argument-cast-cold", compilation, ["assignment-argument-cast-cold"], () => true)
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
            bool shorten = role == "assignment" || !withLegacy;
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(shorten ? 1 : 0));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo((shorten ? 1 : 0) + (withLegacy ? 1 : 0)));
            Assert.That(CastSyntax(selected).Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<CastExpressionSyntax>().Single();
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("NativeAssignmentArgumentCast",
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
            public CompilerBuildInputCapture Capture => new(directory, "assignment-argument-cast-captured", "Debug|net10.0", "12.0", "Actual", [],
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
        private static bool IsArgument(string mode) => mode.StartsWith("argument", StringComparison.Ordinal) || mode == "competing-callee";
        private sealed class Clock(string mode, string path = "Clock.cs", Action? observePath = null) : TypeProvider
        {
            internal CastExpression Cast = null!;
            internal VariableExpression Local = null!;
            internal InvokeMethodExpression Call = null!;
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
                bool argument = IsArgument(mode);
                var value = new ParameterProvider("value", $"", mode == "argument-narrowing" ? typeof(long) : typeof(int));
                CSharpType result = argument ? typeof(int) : typeof(long);
                Cast = new CastExpression(value, result);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, [value]);
                if (argument)
                {
                    var formal = new ParameterProvider("operand", $"", typeof(int));
                    Call = new InvokeMethodExpression(null, "Echo", new ValueExpression[] { Cast });
                    var entry = new MethodProvider(signature, Call, this);
                    var spare = new ParameterProvider("spare", $"", typeof(int), defaultValue: Literal(0));
                    var callee = new MethodProvider(new MethodSignature("Echo", null, MethodSignatureModifiers.Private, typeof(int), null,
                        mode == "argument-optional" ? [formal, spare] : [formal]), (VariableExpression)formal, this);
                    if (mode != "competing-callee") { return [entry, callee]; }
                    var alternative = new ParameterProvider("other", $"", typeof(long));
                    var competing = new MethodProvider(new MethodSignature("Echo", null, MethodSignatureModifiers.Private, typeof(int), null, [alternative]), (ValueExpression)Literal(-17), this);
                    return [entry, callee, competing];
                }
                var declaration = new DeclarationExpression(typeof(long), mode == "assignment-shadow" ? "value" : "result", out var local);
                Local = local;
                MethodBodyStatements block;
                if (mode == "initializer-control")
                {
                    block = new MethodBodyStatements([new AssignmentExpression(declaration, Cast).Terminate(), Return(local)]);
                }
                else
                {
                    var target = mode == "assignment-alias" ? new VariableExpression(typeof(long), local.Declaration) : local;
                    block = new MethodBodyStatements([new ExpressionStatement(declaration), new AssignmentExpression(target, Cast).Terminate(),
                        Return(mode == "unused-assignment" ? (VariableExpression)value : local)]);
                }
                return [new MethodProvider(signature, block, this)];
            }
        }
    }
}

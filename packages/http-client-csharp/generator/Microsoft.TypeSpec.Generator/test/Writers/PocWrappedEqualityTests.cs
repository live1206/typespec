// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    internal class PocWrappedEqualityTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task OriginalWrappedEqualityOwnsBothPairs(bool arrow, bool selectOmission)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-wrapped-equality", compilation, ["wrapped-equality"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(arrow);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            List<object> visits = [];
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Wrapper) || ReferenceEquals(node, owner.Equality) ||
                    ReferenceEquals(node, owner.Equality.Left) || ReferenceEquals(node, owner.Equality.Right))
                { visits.Add(node); }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = raw.SelectPreparedForms(false, false, false);
            var selected = raw.SelectPreparedForms(true, true, selectOmission && prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(native.Name, Is.EqualTo(qualified.Name));
            Assert.That(native.Name, Is.EqualTo(selected.Name));
            Assert.That(native.Content, Is.EqualTo(qualified.Content));
            if (!selectOmission) { Assert.That(native.Content, Is.EqualTo(selected.Content)); }
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(owner.Equality.AddParentheses, Is.True);
            object[] expectedVisits = [owner.Wrapper, owner.Equality, owner.Equality.Left, owner.Equality.Right];
            Assert.That(visits, Has.Count.EqualTo(expectedVisits.Length));
            for (int i = 0; i < expectedVisits.Length; i++) { Assert.That(visits[i], Is.SameAs(expectedVisits[i])); }

            var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            Assert.That(arrow ? method.ExpressionBody!.ToString() : method.DescendantNodes().OfType<ReturnStatementSyntax>().Single().ToString(),
                Is.EqualTo(arrow ? "=> ((x == y))" : "return ((x == y));"));
            var equality = method.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
            var model = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            var symbol = model.GetDeclaredSymbol(method)!;
            var operation = (IBinaryOperation)model.GetOperation(equality)!;
            Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.Equals));
            Assert.That(operation.OperatorMethod, Is.Null);
            Assert.That(operation.LeftOperand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(operation.RightOperand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(equality.Left).Symbol, symbol.Parameters[0]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(equality.Right).Symbol, symbol.Parameters[1]), Is.True);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            Assert.That(Execute(native), Is.EqualTo(new object?[] { true, false, false }));
            Assert.That(Execute(selected), Is.EqualTo(Execute(native)));
            var wrong = new CodeFile("namespace Sample; public class Clock { public bool M(int x, int y) => x != y; }", native.Name);
            Assert.That(Execute(wrong), Is.EqualTo(new object?[] { false, true, true }));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, wrong.Content));
            Assert.That(Expression(selected), Is.EqualTo(selectOmission ? "x == y" : "((x == y))"));
            if (selectOmission)
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(2));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(2));
            }
        }

        [TestCase("restore", false)]
        [TestCase("restore", true)]
        [TestCase("newline", false)]
        [TestCase("newline", true)]
        [TestCase("reverse", true)]
        [TestCase("alias", false)]
        [TestCase("ungrouped", true)]
        [TestCase("other-operator", false)]
        public async Task WrappedEqualityUnknownOrRestoredEvidenceRetains(string mode, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(arrow, mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-wrapped-equality-veto", compilation, ["wrapped-equality"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(arrow, mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            bool restore = mode == "restore";
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(restore ? 2 : 0));
            int openings = 0;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                var declaration = ((VariableExpression)owner.Equality.Right).Declaration;
                string name = declaration.RequestedName;
                declaration.Update("other");
                declaration.Update(name);
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(openings, Is.EqualTo(restore ? 1 : 0));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                Assert.That(Expression(raw), Is.EqualTo("(x == y)"));
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            }
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            Assert.That(allowed, Is.EqualTo(!restore));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            if (mode == "newline") { Assert.That(selected.Content, Does.Contain("y\n")); }
            Assert.That(Execute(selected), Is.EqualTo(Execute(native)));
        }

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("source-expiry", false)]
        [TestCase("source-expiry", true)]
        [TestCase("result-expiry", false)]
        [TestCase("result-expiry", true)]
        public async Task WrappedEqualityUsesActualCompilerOutputAndFinalAuthority(string mode, bool arrow)
        {
            var sandbox = Directory.CreateTempSubdirectory("task05-wrapped-equality-");
            string source = Path.Combine(sandbox.FullName, "Marker.cs");
            string nativeSource = Path.Combine(sandbox.FullName, "NativeMarker.cs");
            const string input = "namespace Inputs { internal class Marker {} }\n";
            File.WriteAllText(source, input);
            File.WriteAllText(nativeSource, input);
            try
            {
                var actual = await WriteCompilerOutput(sandbox.FullName, source, arrow, mode);
                var native = await WriteCompilerOutput(sandbox.FullName, nativeSource, arrow, "native");
                Assert.That(Expression(native.Selected), Is.EqualTo("((x == y))"));
                Assert.That(Expression(actual.Selected), Is.EqualTo(mode == "source-expiry" ? "((x == y))" : "x == y"));
                if (mode == "source-expiry") { Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content)); }
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, actual.Selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                foreach (var file in new[] { native.Selected, actual.Selected, native.Final, actual.Final })
                { Assert.That(Execute(file), Is.EqualTo(new object?[] { true, false, false })); }
            }
            finally { sandbox.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerOutput(string directory, string source, bool arrow, string mode)
        {
            bool enabled = mode != "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, mode + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, mode + "-isolated")).FullName;
            string config = enabled ? Configuration : Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            var owner = new Clock(arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            int wrapperVisits = 0;
            int binaryVisits = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (!enabled) { Assert.That(prepared, Is.Null); return; }
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
                    Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                    prepared.PocNativeWriteObserver = node =>
                    {
                        if (ReferenceEquals(node, owner.Wrapper)) { wrapperVisits++; }
                        if (ReferenceEquals(node, owner.Equality)) { binaryVisits++; }
                    };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(enabled ? 2 : 0));
                    Assert.That(Expression(file), Is.EqualTo(enabled ? "x == y" : "((x == y))"));
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
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"), "False means the caller discards isolated output");
            Assert.That(wrapperVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(binaryVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static string Expression(CodeFile file) => PreparedParenthesesTests.MethodExpression(file.Content).NormalizeWhitespace().ToFullString();

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("NativeWrappedEquality",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CodeFile file) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", [int.MinValue, int.MinValue], [19, 20], [int.MinValue, int.MaxValue]);

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
            public CompilerBuildInputCapture Capture => new(directory, "actual-wrapped-equality-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(bool arrow, string mode = "ordinary") : TypeProvider
        {
            internal ParenthesizedExpression Wrapper { get; private set; } = null!;
            internal BinaryOperatorExpression Equality { get; private set; } = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                VariableExpression left = x;
                VariableExpression right = y;
                if (mode == "newline") { right.Declaration.Update("y\n"); }
                if (mode == "alias") { left = new VariableExpression(typeof(int), left.Declaration); }
                if (mode == "reverse") { (left, right) = (right, left); }
                Equality = new BinaryOperatorExpression(mode == "other-operator" ? "!=" : "==", left, right, mode != "ungrouped");
                Wrapper = new ParenthesizedExpression(Equality);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]);
                return [arrow ? new MethodProvider(signature, Wrapper, this) : new MethodProvider(signature, Return(Wrapper), this)];
            }
        }
    }
}

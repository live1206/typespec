// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocIfConditionIntegrationTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("source-expiry", false)]
        [TestCase("source-expiry", true)]
        [TestCase("result-expiry", false)]
        [TestCase("result-expiry", true)]
        [TestCase("ast-expiry", false)]
        [TestCase("ast-expiry", true)]
        [TestCase("isolated-link", false)]
        [TestCase("isolated-link", true)]
        public async Task CompilerIfPipelinePreservesActualFilesAndFinalAuthority(string mode, bool inline)
        {
            if (mode == "isolated-link" && OperatingSystem.IsWindows())
            { Assert.Ignore("The controlled fixture requires unprivileged Unix symbolic links"); }
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-if-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var target = Path.Combine(sandbox, "owned-target");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("IfCompilerInputs")));
                var owner = new Clock(inline, "Clock", "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                int uses = 0;
                int visits = 0;
                bool? decision = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null, "A real captured compiler session supplies physical input authority");
                        prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Condition)) { visits++; } };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(Condition(file), Is.EqualTo("x == y"), "Actual raw use precedes expiry");
                        if (mode == "ast-expiry") { owner.Leaf.Update(type: typeof(long)); }
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        decision = allowed;
                        Assert.That(allowed, Is.EqualTo(mode != "ast-expiry"));
                        if (mode == "source-expiry") { File.AppendAllText(source, "// physical input changed before selection\n"); }
                        if (mode == "isolated-link")
                        {
                            Directory.CreateDirectory(target);
                            Directory.CreateSymbolicLink(Path.Combine(isolated, "alias"), target);
                        }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "result-expiry") { File.AppendAllText(source, "// physical input changed after selection\n"); }
                    }
                };
                CSharpGen.CompilerBuildGenerationResult? result = null;
                Exception? failure = null;
                try { result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source)); }
                catch (Exception exception) { failure = exception; }
                // The pipeline initializes SourceInputModel before this separate native control.
                var nativeOwner = new Clock(inline, "Clock", "alias/Clock.cs");
                nativeOwner.ProcessTypeForBackCompatibility();
                var native = new TypeProviderWriter(nativeOwner).Write();
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(decision, Is.EqualTo(mode != "ast-expiry"));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(native.Name));
                bool shortened = mode is "stable" or "result-expiry";
                Assert.That(Condition(selected), Is.EqualTo(shortened ? "x == y" : "(x == y)"));
                AssertRequiredDelimiters(selected);
                PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
                object?[][] arguments = [[1, 1], [1, 2], [int.MinValue, int.MaxValue]];
                Assert.That(Execute(selected, "Clock", arguments), Is.EqualTo(new object?[] { true, false, false }));
                if (mode == "isolated-link")
                {
                    Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                    Assert.That(failure!.Message, Does.Contain("escaped its isolated directory"));
                    Assert.That(proof!.IsCurrentFor(original), Is.True);
                }
                else
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(result, Is.Not.Null);
                    Assert.That(result!.UsedCapturedResidualInputs, Is.EqualTo(mode is not "source-expiry" and not "result-expiry"),
                        "False requires the trusted caller to discard even already-written isolated output");
                    var physicalPath = Path.Combine(isolated, "alias", "Clock.cs");
                    Assert.That(File.Exists(physicalPath), Is.True);
                    var final = new CodeFile(File.ReadAllText(physicalPath), "alias/Clock.cs");
                    AssertRequiredDelimiters(final);
                    PreparedParenthesesTests.AssertOperations(native.Content, final.Content);
                    Assert.That(Execute(final, "Clock", arguments), Is.EqualTo(Execute(native, "Clock", arguments)));
                }
                TestContext.WriteLine($"IF-COMPILER mode={mode}; inline={inline}; uses={uses}; visits={visits}; selected={Condition(selected)}; capturedResult={result?.UsedCapturedResidualInputs}; failure={failure?.GetType().Name ?? "none"}");
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ObservedLambdaIfConditionKeepsActualCapturedText(bool explicitText)
        {
            var compilation = Compilation("IfCapture", []);
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new CapturedClock(explicitText);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => true);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new CapturedClock(explicitText);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = written.SelectPreparedForms(true, true, prepared!.CanCommitPocGrouping(written));
            Assert.That(prepared.PocGroupingCount, Is.Zero, "A lambda body is not the direct native method body profile");
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            string captured = CaptureConstant(native.Content, explicitText);
            Assert.That(CaptureConstant(selected.Content, explicitText), Is.EqualTo(captured));
            Assert.That(Execute(selected, "Clock", [[1, 2]]).Single(), Is.EqualTo(captured));
            Assert.That(Condition(selected), Is.EqualTo("(x == y)"));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            string unsafeText = native.Content.Replace("if ((x == y))", "if (x == y)", StringComparison.Ordinal);
            Assert.That(unsafeText, Is.Not.EqualTo(native.Content));
            if (explicitText)
            {
                Assert.That(captured, Is.EqualTo("manual"));
                Assert.That(CaptureConstant(unsafeText, true), Is.EqualTo("manual"));
                Assert.DoesNotThrow(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
            }
            else
            {
                Assert.That(captured, Does.Contain("(x == y)"));
                Assert.That(CaptureConstant(unsafeText, false), Is.Not.EqualTo(captured));
                Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IfFileExpiryRemainsLocalUnlessSharedInputsExpire(bool sharedExpiry)
        {
            var compilation = Compilation("IfLocal", []);
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var first = new Clock(false, "Clock", "Clock.cs");
            var other = new Clock(false, "Other", "Other.cs");
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(first, other));
            bool sourceCurrent = true;
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => sourceCurrent);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
            var firstRaw = new TypeProviderWriter(first) { PreparedFieldTypes = prepared }.Write();
            Assert.That(firstRaw.PreparedGroupingCount, Is.EqualTo(1), "An actual first-file positive precedes expiry");
            if (sharedExpiry) { sourceCurrent = false; }
            else { first.Leaf.Update(type: typeof(long)); }
            var otherRaw = new TypeProviderWriter(other) { PreparedFieldTypes = prepared }.Write();
            Assert.That(otherRaw.PreparedGroupingCount, Is.EqualTo(sharedExpiry ? 0 : 1));
            var firstSelected = firstRaw.SelectPreparedForms(true, true,
                prepared.CanCommitPocGrouping(firstRaw) && prepared.SealPocGroupingWithoutCallbacks(firstRaw));
            var otherSelected = otherRaw.SelectPreparedForms(true, true,
                prepared.CanCommitPocGrouping(otherRaw) && prepared.SealPocGroupingWithoutCallbacks(otherRaw));
            Assert.That(Condition(firstSelected), Is.EqualTo("(x == y)"));
            Assert.That(Condition(otherSelected), Is.EqualTo(sharedExpiry ? "(x == y)" : "x == y"));
            Assert.That(firstSelected.Name, Is.EqualTo("Clock.cs"));
            Assert.That(otherSelected.Name, Is.EqualTo("Other.cs"));
            Assert.That(Execute(otherSelected, "Other", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
        }

        private static SourceInputModel ControlledInputs(NativeGenerator generator, CSharpCompilation compilation, Func<bool> current) => new(compilation, null)
        {
            PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                "task05-if-integration", compilation, ["controlled-if-integration-inputs"], current)
        };
        private static IfStatementSyntax If(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content).GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
        private static string Condition(CodeFile file) => If(file).Condition.NormalizeWhitespace().ToFullString();
        private static void AssertRequiredDelimiters(CodeFile file) => Assert.That(
            If(file).OpenParenToken.IsKind(SyntaxKind.OpenParenToken) && If(file).CloseParenToken.IsKind(SyntaxKind.CloseParenToken), Is.True);
        private static CSharpCompilation Compilation(string name, IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(name,
            trees, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, string owner, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation("ExecutedIfIntegration", [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)]), "Sample." + owner, "M", arguments);
        private static string CaptureConstant(string source, bool explicitText)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = Compilation("CapturedIf", [tree]);
            var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(invocation)!;
            var argument = operation.Arguments.Single(value => value.Parameter!.Name == "text");
            Assert.That(argument.ArgumentKind, Is.EqualTo(explicitText ? ArgumentKind.Explicit : ArgumentKind.DefaultValue));
            return (string)argument.Value.ConstantValue.Value!;
        }
        private static MethodBodyStatement IfBody(ParameterProvider x, ParameterProvider y, bool inline, out BinaryOperatorExpression condition)
        {
            condition = new BinaryOperatorExpression("==", x, y);
            var statement = new IfStatement(condition, inline: inline);
            statement.Add(Return(Literal(true)));
            return new MethodBodyStatements([statement, Return(Literal(false))]);
        }
        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-if-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Clock(bool inline, string name, string path) : TypeProvider
        {
            internal BinaryOperatorExpression Condition = null!;
            internal VariableExpression Leaf = null!;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Leaf = x;
                var body = IfBody(x, y, inline, out Condition);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]), body, this)];
            }
        }
        private sealed class CapturedClock(bool explicitText) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var body = IfBody(x, y, false, out _);
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), body);
                var call = new InvokeMethodExpression(null, "Capture", explicitText ? [lambda, Literal("manual")] : new ValueExpression[] { lambda });
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: [new AttributeStatement(typeof(CallerArgumentExpressionAttribute), Literal("value"))]);
                var capture = new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null,
                    [new ParameterProvider("value", $"", typeof(Func<bool>)), text]);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, [x, y]), call, this),
                    new MethodProvider(capture, (VariableExpression)text, this)];
            }
        }
    }
}

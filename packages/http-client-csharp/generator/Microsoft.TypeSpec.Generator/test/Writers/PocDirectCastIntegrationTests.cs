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
    internal class PocDirectCastIntegrationTests
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
        public async Task CompilerPipelineUsesActualCastFilesAndFinalPhysicalAuthority(string mode, bool arrow)
        {
            if (mode == "isolated-link" && OperatingSystem.IsWindows())
            { Assert.Ignore("The controlled fixture requires unprivileged Unix symbolic links"); }
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-cast-compiler", Guid.NewGuid().ToString("N"));
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
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("CastCompilerInputs")));
                var owner = new Clock(arrow, "Clock", "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                var session = new Session(original, source);
                CompleteSourceInputSnapshot? proof = null;
                CodeFile? selected = null;
                int uses = 0;
                int casts = 0;
                int operands = 0;
                bool? decision = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null, "A real captured compiler session supplies physical input authority");
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Cast)) { casts++; }
                            if (ReferenceEquals(node, owner.Leaf)) { operands++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(Expression(file), Is.EqualTo("(int)value"), "Actual raw shortening precedes expiry");
                        if (mode == "ast-expiry") { owner.Leaf.Update(type: typeof(long)); }
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        decision = allowed;
                        Assert.That(allowed, Is.EqualTo(mode != "ast-expiry"));
                        if (mode == "source-expiry") { File.AppendAllText(source, "// changed before selection\n"); }
                        if (mode == "isolated-link")
                        {
                            Directory.CreateDirectory(target);
                            Directory.CreateSymbolicLink(Path.Combine(isolated, "alias"), target);
                        }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "result-expiry") { File.AppendAllText(source, "// changed after selection\n"); }
                    }
                };
                CSharpGen.CompilerBuildGenerationResult? result = null;
                Exception? failure = null;
                try { result = await pipeline.ExecuteForCompilerBuildAsync(isolated, session); }
                catch (Exception exception) { failure = exception; }
                // The compiler pipeline initializes SourceInputModel. Render the
                // independent native control only after that initialization.
                var nativeOwner = new Clock(arrow, "Clock", "alias/Clock.cs");
                nativeOwner.ProcessTypeForBackCompatibility();
                var native = new TypeProviderWriter(nativeOwner).Write();
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(casts, Is.EqualTo(1));
                Assert.That(operands, Is.EqualTo(1));
                Assert.That(decision, Is.EqualTo(mode != "ast-expiry"));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(native.Name));
                bool shortForm = mode is "stable" or "result-expiry";
                Assert.That(Expression(selected), Is.EqualTo(shortForm ? "(int)value" : "((int)value)"));
                PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
                object?[][] arguments = [[int.MinValue], [int.MaxValue], [17]];
                Assert.That(Execute(selected, "Clock", arguments), Is.EqualTo(new object?[] { int.MinValue, int.MaxValue, 17 }));
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
                    // BASE bfb31's legacy pipeline already removes an identity
                    // cast entirely. Preserve strict raw-to-selected conversion
                    // checks above; compare final output to actual native final
                    // output, not to the pre-legacy conversion operation.
                    var nativeFinal = await NativeFinalControl(arrow, sandbox, source);
                    PreparedParenthesesTests.AssertOperations(nativeFinal.Content, final.Content);
                    Assert.That(Execute(final, "Clock", arguments), Is.EqualTo(Execute(native, "Clock", arguments)));
                }
                TestContext.WriteLine($"CAST-COMPILER mode={mode}; arrow={arrow}; uses={uses}; castVisits={casts}; operandVisits={operands}; selected={Expression(selected)}; capturedResult={result?.UsedCapturedResidualInputs}; failure={failure?.GetType().Name ?? "none"}");
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LambdaSourceCaptureRetainsTheNativeCastText(bool explicitText)
        {
            var compilation = Compilation("CastCapture", []);
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
            Assert.That(prepared.PocGroupingCount, Is.Zero, "Lambda/call methods are outside the direct-root slice");
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            string captured = CaptureConstant(native.Content, explicitText);
            Assert.That(CaptureConstant(selected.Content, explicitText), Is.EqualTo(captured));
            Assert.That(Execute(selected, "Clock", [[17]]).Single(), Is.EqualTo(captured));
            Assert.That(selected.Content, Does.Contain("((int)value)"));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            string unsafeText = native.Content.Replace("((int)value)", "(int)value", StringComparison.Ordinal);
            Assert.That(unsafeText, Is.Not.EqualTo(native.Content));
            if (explicitText)
            {
                Assert.That(captured, Is.EqualTo("manual"));
                Assert.That(CaptureConstant(unsafeText, true), Is.EqualTo("manual"));
                Assert.DoesNotThrow(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
            }
            else
            {
                Assert.That(captured, Does.Contain("((int)value)"));
                Assert.That(CaptureConstant(unsafeText, false), Is.Not.EqualTo(captured));
                Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CastFileLocalExpiryDoesNotBecomeSharedPermission(bool sharedExpiry)
        {
            var compilation = Compilation("CastLocal", []);
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var first = new Clock(true, "Clock", "Clock.cs");
            var other = new Clock(true, "Other", "Other.cs");
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
            bool firstAllowed = prepared.CanCommitPocGrouping(firstRaw) && prepared.SealPocGroupingWithoutCallbacks(firstRaw);
            bool otherAllowed = prepared.CanCommitPocGrouping(otherRaw) && prepared.SealPocGroupingWithoutCallbacks(otherRaw);
            Assert.That(firstAllowed, Is.False);
            var firstSelected = firstRaw.SelectPreparedForms(true, true, firstAllowed);
            var otherSelected = otherRaw.SelectPreparedForms(true, true, otherAllowed);
            Assert.That(Expression(firstSelected), Is.EqualTo("((int)value)"));
            Assert.That(Expression(otherSelected), Is.EqualTo(sharedExpiry ? "((int)value)" : "(int)value"));
            Assert.That(firstSelected.Name, Is.EqualTo("Clock.cs"));
            Assert.That(otherSelected.Name, Is.EqualTo("Other.cs"));
            Assert.That(Execute(otherSelected, "Other", [[int.MinValue], [int.MaxValue]]), Is.EqualTo(new object?[] { int.MinValue, int.MaxValue }));
        }

        private static async Task<CodeFile> NativeFinalControl(bool arrow, string sandbox, string source)
        {
            var previous = CodeModelGenerator.Instance;
            var original = Path.Combine(sandbox, "native-original");
            var isolated = Path.Combine(sandbox, "native-isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            try
            {
                var configuration = Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeFinalCastInputs")));
                var owner = new Clock(arrow, "Clock", "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? raw = null;
                CodeFile? selected = null;
                int uses = -1;
                var pipeline = new CSharpGen
                {
                    PocRawFileObserved = (prepared, _, file) => { raw = file; uses = prepared?.PocGroupingUses ?? 0; },
                    PocSelectedFileObserved = file => selected = file
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
                Assert.That(result.UsedCapturedResidualInputs, Is.False,
                    "This disabled-policy native control is only a comparison, not captured-input publication authority");
                Assert.That(uses, Is.Zero);
                Assert.That(raw, Is.Not.Null);
                Assert.That(selected, Is.Not.Null);
                Assert.That(Expression(raw!), Is.EqualTo("((int)value)"));
                Assert.That(Expression(selected!), Is.EqualTo("((int)value)"));
                Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, "alias", "Clock.cs")), "alias/Clock.cs");
                Assert.That(Expression(final), Is.EqualTo("value"), "Actual BASE-characterized native legacy result");
                Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(selected.Content, final.Content),
                    "The strict conversion oracle still detects inherited identity-cast removal; it is not globally normalized away");
                return final;
            }
            finally { CodeModelGenerator.Instance = previous; }
        }

        private static SourceInputModel ControlledInputs(NativeGenerator generator, CSharpCompilation compilation, Func<bool> current) => new(compilation, null)
        {
            PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                "task05-cast-integration", compilation, ["controlled-cast-integration-inputs"], current)
        };

        private static string Expression(CodeFile file) => PreparedParenthesesTests.MethodExpression(file.Content).NormalizeWhitespace().ToFullString();
        private static CSharpCompilation Compilation(string name, IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(name,
            trees, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, string owner, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation("ExecutedCastIntegration", [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)]), "Sample." + owner, "M", arguments);
        private static string CaptureConstant(string source, bool explicitText)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = Compilation("CapturedCast", [tree]);
            var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(invocation)!;
            var argument = operation.Arguments.Single(value => value.Parameter!.Name == "text");
            Assert.That(argument.ArgumentKind, Is.EqualTo(explicitText ? ArgumentKind.Explicit : ArgumentKind.DefaultValue));
            return (string)argument.Value.ConstantValue.Value!;
        }

        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-cast-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true; // Production proof independently revalidates physical hashes.
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Clock(bool arrow, string name, string path) : TypeProvider
        {
            internal CastExpression Cast { get; private set; } = null!;
            internal VariableExpression Leaf { get; private set; } = null!;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var parameter = new ParameterProvider("value", $"", typeof(int));
                Leaf = parameter;
                Cast = new CastExpression(Leaf, typeof(int));
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [parameter]);
                return [arrow ? new MethodProvider(signature, Cast, this) : new MethodProvider(signature, Return(Cast), this)];
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
                var parameter = new ParameterProvider("value", $"", typeof(int));
                var cast = new CastExpression(parameter, typeof(int));
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(cast));
                var call = new InvokeMethodExpression(null, "Capture", explicitText ? [lambda, Literal("manual")] : new ValueExpression[] { lambda });
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: [new AttributeStatement(typeof(CallerArgumentExpressionAttribute), Literal("value"))]);
                var capture = new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null,
                    [new ParameterProvider("value", $"", typeof(Func<int>)), text]);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, [parameter]), call, this),
                    new MethodProvider(capture, (VariableExpression)text, this)];
            }
        }
    }
}

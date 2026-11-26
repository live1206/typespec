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
    internal class PocReducedReceiverCaptureTests
    {
        [Test]
        public void OriginalReducedExtensionCapturesTheCastLambdaReceiver()
        {
            const string receiver = "((Func<int>)(() => { return ((x + y)); }))";
            const string shortened = "((Func<int>)(() => { return x + y; }))";
            const string source = "using System; namespace Sample { public static class Extensions { public static object Capture(this Func<int> value, " +
                "[System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string text = \"\") => text; } public class Clock { " +
                "public global::System.DateTimeOffset Seed => default; public object M(int x, int y) => " + receiver + ".Capture(); } }";
            string originalCapture = CaptureAndExecute(source, receiver);
            string forcedCapture = CaptureAndExecute(source.Replace(receiver, shortened, StringComparison.Ordinal), shortened);
            Assert.That(originalCapture, Does.Contain("return ((x + y));"));
            Assert.That(forcedCapture, Is.Not.EqualTo(originalCapture));
        }

        [Test]
        public async Task NativeReducedExtensionReceiverCaptureSurvivesPhysicalFinalReduction()
        {
            var directory = Directory.CreateTempSubdirectory("task05-reduced-receiver-");
            try
            {
                var actual = await Generate(directory.FullName, enabled: true);
                var native = await Generate(directory.FullName, enabled: false);
                Assert.That(actual.Selected, Is.EqualTo(native.Selected));
                string selectedCapture = CaptureAndExecute(actual.Selected["Clock.cs"], extensionSource: actual.Selected["Extensions.cs"]);
                Assert.That(selectedCapture, Does.StartWith("(global::System.Func<int>)"));
                Assert.That(selectedCapture, Does.Contain("return ((x + y));"));
                string forced = actual.Selected["Clock.cs"].Replace("return ((x + y));", "return x + y;", StringComparison.Ordinal);
                Assert.That(forced, Is.Not.EqualTo(actual.Selected["Clock.cs"]));
                Assert.That(CaptureAndExecute(forced, extensionSource: actual.Selected["Extensions.cs"]), Is.Not.EqualTo(selectedCapture));

                Assert.That(actual.Final, Is.EqualTo(native.Final));
                Assert.That(actual.Final["Clock.cs"], Does.Contain("Unrelated() => 42;"));
                string finalCapture = CaptureAndExecute(actual.Final["Clock.cs"], extensionSource: actual.Final["Extensions.cs"]);
                string nativeCapture = CaptureAndExecute(native.Final["Clock.cs"], extensionSource: native.Final["Extensions.cs"]);
                TestContext.WriteLine($"REDUCED-RECEIVER selected={System.Text.Json.JsonSerializer.Serialize(selectedCapture)}; enabledFinal={System.Text.Json.JsonSerializer.Serialize(finalCapture)}; disabledFinal={System.Text.Json.JsonSerializer.Serialize(nativeCapture)}");
                Assert.Multiple(() =>
                {
                    Assert.That(finalCapture, Is.EqualTo(selectedCapture));
                    Assert.That(nativeCapture, Is.EqualTo(selectedCapture));
                });
            }
            finally { directory.Delete(recursive: true); }
        }

        [Test]
        public async Task ReducedReceiverWithExtraDocumentKeepsOrdinaryReduction()
        {
            var directory = Directory.CreateTempSubdirectory("task05-reduced-receiver-extra-document-");
            try
            {
                var actual = await Generate(directory.FullName, enabled: true, extraDocument: true);
                var native = await Generate(directory.FullName, enabled: false, extraDocument: true);
                Assert.That(actual.Selected, Is.EqualTo(native.Selected));
                Assert.That(actual.Final, Is.EqualTo(native.Final));
                string selectedCapture = CaptureAndExecute(actual.Selected["Clock.cs"], extensionSource: actual.Selected["Extensions.cs"]);
                Assert.That(selectedCapture, Does.Contain("return ((x + y));"));
                string finalCapture = CaptureAndExecute(actual.Final["Clock.cs"], extensionSource: actual.Final["Extensions.cs"]);
                Assert.That(finalCapture, Does.Contain("return x + y;").And.Not.EqualTo(selectedCapture));
                Assert.That(actual.Final["Clock.cs"], Does.Contain("Unrelated() => 42;"));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<(Dictionary<string, string> Selected, Dictionary<string, string> Final)> Generate(string directory, bool enabled, bool extraDocument = false)
        {
            string prefix = enabled ? "prepared" : "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, prefix + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, prefix + "-isolated")).FullName;
            string configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: configuration,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("ReducedReceiverInputs")));
            TypeProvider[] providers = extraDocument ? [new Clock(), new Extensions(), new Extra()] : [new Clock(), new Extensions()];
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(providers));
            var selected = new Dictionary<string, string>();
            int writes = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
                    if (!enabled) { Assert.That(prepared, Is.Null); }
                },
                PocRawFileObserved = (prepared, _, file) =>
                {
                    writes++;
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.Zero);
                    Assert.That(file.PreparedGroupingCount, Is.Zero);
                    TestContext.WriteLine($"REDUCED-RECEIVER enabled={enabled}; file={file.Name}; prepared={prepared is not null}; receipt={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                },
                PocSelectedFileObserved = file => selected.Add(file.Name, file.Content)
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(enabled));
            Assert.That(writes, Is.EqualTo(providers.Length));
            string[] expectedFiles = extraDocument ? ["Clock.cs", "Extensions.cs", "Extra.cs"] : ["Clock.cs", "Extensions.cs"];
            Assert.That(selected.Keys, Is.EquivalentTo(expectedFiles));
            var final = selected.Keys.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(isolated, name)));
            return (selected, final);
        }

        private static string CaptureAndExecute(string source, string? expectedReceiver = null, string? extensionSource = null)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Clock.cs");
            var extensionTree = extensionSource is null ? tree : CSharpSyntaxTree.ParseText(extensionSource, path: "Extensions.cs");
            var compilation = CSharpCompilation.Create("ReducedReceiverCapture", extensionSource is null ? [tree] : [tree, extensionTree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var receiver = ((MemberAccessExpressionSyntax)call.Expression).Expression;
            if (expectedReceiver is not null) { Assert.That(receiver.ToString(), Is.EqualTo(expectedReceiver)); }
            Assert.That(call.ArgumentList.Arguments, Is.Empty);
            var declaration = extensionTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Capture");
            var target = compilation.GetSemanticModel(extensionTree).GetDeclaredSymbol(declaration)!;
            var selected = (IMethodSymbol)model.GetSymbolInfo(call).Symbol!;
            Assert.That(selected.MethodKind, Is.EqualTo(MethodKind.ReducedExtension));
            Assert.That(SymbolEqualityComparer.Default.Equals(selected.ReducedFrom, target), Is.True);
            var attribute = target.Parameters[1].GetAttributes().Single();
            Assert.That(attribute.AttributeClass?.ToDisplayString(), Is.EqualTo("System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"));
            Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo(target.Parameters[0].Name));
            var invocation = (IInvocationOperation)model.GetOperation(call)!;
            var argument = invocation.Arguments.Single(value => value.Parameter?.Name == "text");
            Assert.That(argument.IsImplicit, Is.True);
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            Assert.That(argument.Value.ConstantValue.HasValue, Is.True);
            string captured = (string)argument.Value.ConstantValue.Value!;
            // Roslyn omits the receiver's outer grouping from the capture,
            // while the cast and the groups inside the lambda stay observable.
            Assert.That(receiver, Is.TypeOf<ParenthesizedExpressionSyntax>());
            Assert.That(captured, Is.EqualTo(((ParenthesizedExpressionSyntax)receiver).Expression.ToString()));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [3, 4], [int.MinValue, -1]),
                Is.EqualTo(new object?[] { captured, captured }));
            return captured;
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Session(string directory) : ITrustedCompilerBuildSession
        {
            private static readonly string _core = typeof(object).Assembly.Location;
            private readonly string _hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_core)));
            public CompilerBuildInputCapture Capture => new(directory, "reduced-receiver-capture", "Debug|net10.0", "12.0", "Actual", [], [],
                [new CapturedCompilerReference(_core, _hash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var nested = new ParenthesizedExpression(new BinaryOperatorExpression("+", x, y));
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(nested));
                var receiver = new CastExpression(new ParenthesizedExpression(lambda), typeof(Func<int>));
                var call = new InvokeMethodExpression(receiver, "Capture", Array.Empty<ValueExpression>());
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, [x, y]), call, this),
                    new MethodProvider(new MethodSignature("Unrelated", null, MethodSignatureModifiers.Public, typeof(int), null, []), new ParenthesizedExpression(Literal(42)), this)];
            }
        }

        private sealed class Extra : TypeProvider
        {
            protected override string BuildName() => "Extra";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Extra.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class Extensions : TypeProvider
        {
            protected override string BuildName() => "Extensions";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Extensions.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Static | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var value = new ParameterProvider("value", $"", typeof(Func<int>));
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: new AttributeStatement[] { new(typeof(CallerArgumentExpressionAttribute), Literal("value")) });
                var signature = new MethodSignature("Capture", null, MethodSignatureModifiers.Public | MethodSignatureModifiers.Static | MethodSignatureModifiers.Extension,
                    typeof(object), null, [value, text]);
                return [new MethodProvider(signature, (VariableExpression)text, this)];
            }
        }
    }
}

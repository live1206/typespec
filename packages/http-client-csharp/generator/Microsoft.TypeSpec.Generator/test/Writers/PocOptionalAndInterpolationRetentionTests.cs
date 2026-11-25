// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    internal class PocOptionalAndInterpolationRetentionTests
    {
        [TestCase("optional-literal", "value = (1)")]
        [TestCase("optional-descendant", "value = (1 + (2))")]
        [TestCase("interpolation", "{(flag ? 1 : 2)}")]
        public async Task OriginalOptionalOrInterpolationPairRetainsAtNativeSelection(string mode, string expected)
        {
            var directory = Directory.CreateTempSubdirectory("task05-optional-interpolation-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(AssertBindingAndRuntime(actual.Selected, mode), Is.EqualTo(expected));
                Assert.That(AssertBindingAndRuntime(native.Selected, mode), Is.EqualTo(expected));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                string nativeFinal = AssertBindingAndRuntime(native.Final, mode);
                string actualFinal = AssertBindingAndRuntime(actual.Final, mode);
                TestContext.WriteLine($"OPTIONAL-INTERPOLATION {mode}: independent native final `{nativeFinal}`, candidate final `{actualFinal}`");
                Assert.That(actualFinal, Is.EqualTo(nativeFinal));
                // The baseline final simplifier removes the optional-default
                // pairs but leaves the interpolation's required pair.
                Assert.That(nativeFinal, Is.EqualTo(mode switch
                {
                    "optional-literal" => "value = 1",
                    "optional-descendant" => "value = 1 + 2",
                    _ => expected
                }));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerOutput(string directory, string mode, bool enabled)
        {
            string prefix = enabled ? "prepared" : "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, prefix + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, prefix + "-isolated")).FullName;
            string configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: configuration,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("OptionalInterpolationInputs")));
            var owner = new Clock(mode);
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeFile? selected = null;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
                    if (enabled) { Assert.That(prepared, Is.Not.Null); }
                    else { Assert.That(prepared, Is.Null); }
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.Zero);
                    Assert.That(file.PreparedGroupingCount, Is.Zero);
                    Assert.That(file.SelectPreparedForms(false, false, false).Content, Is.EqualTo(file.Content));
                    if (enabled) { Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True); }
                    TestContext.WriteLine($"OPTIONAL-INTERPOLATION {mode}: enabled={enabled}; prepared={prepared is not null}; receipt={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                },
                PocSelectedFileObserved = file => selected = file
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(enabled));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static string AssertBindingAndRuntime(CodeFile file, string mode)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("OptionalInterpolationWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var parameter = model.GetDeclaredSymbol(method)!.Parameters.Single();
            if (mode == "interpolation")
            {
                var hole = method.DescendantNodes().OfType<InterpolationSyntax>().Single();
                Assert.That(hole.OpenBraceToken.IsMissing || hole.CloseBraceToken.IsMissing, Is.False);
                var conditional = hole.DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();
                var operation = (IConditionalOperation)model.GetOperation(conditional)!;
                Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(conditional.Condition).Symbol, parameter), Is.True);
                Assert.That(operation.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [true], [false]),
                    Is.EqualTo(new object?[] { "value:1", "value:2" }));
                return hole.ToString();
            }
            int expected = mode == "optional-literal" ? 1 : 3;
            Assert.That(parameter.HasExplicitDefaultValue, Is.True);
            Assert.That(parameter.ExplicitDefaultValue, Is.EqualTo(expected));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [Type.Missing], [17]), Is.EqualTo(new object?[] { expected, 17 }));
            var syntax = method.ParameterList.Parameters.Single();
            return syntax.Identifier.Text + " " + syntax.Default!.ToString();
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Session(string directory) : ITrustedCompilerBuildSession
        {
            private static readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_core)));
            public CompilerBuildInputCapture Capture => new(directory, "optional-interpolation-retention", "Debug|net10.0", "12.0", "Actual", [], [],
                [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(string mode) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                if (mode == "interpolation")
                {
                    var flag = new ParameterProvider("flag", $"", typeof(bool));
                    var hole = new ParenthesizedExpression(new TernaryConditionalExpression(flag, Literal(1), Literal(2)));
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(string), null, [flag]),
                        new FormattableStringExpression("value:{0}", new ValueExpression[] { hole }), this)];
                }
                ValueExpression defaultValue = mode == "optional-literal" ? new ParenthesizedExpression(Literal(1)) :
                    new BinaryOperatorExpression("+", Literal(1), new ParenthesizedExpression(Literal(2)));
                var value = new ParameterProvider("value", $"", typeof(int), defaultValue: defaultValue);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [value]), (VariableExpression)value, this)];
            }
        }
    }
}

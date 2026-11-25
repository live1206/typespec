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
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocDirectDelegateTargetTests
    {
        [Test]
        public void OriginalDelegateTargetKeepsItsOwnPairsAndInvokesOnce()
        {
            const string text = "namespace Sample { public class Clock { public int M(System.Func<int, int> f) => ((f))(1); } }";
            AssertActualDelegateTarget(text, "Clock.cs");
        }

        [Test]
        public async Task NativeDelegateTargetRetainsItsPairsWithoutGroupingPermission()
        {
            var directory = Directory.CreateTempSubdirectory("task05-direct-delegate-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                AssertActualDelegateTarget(native.Selected.Content, native.Selected.Name);
                AssertActualDelegateTarget(actual.Selected.Content, actual.Selected.Name);
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                // Both physical final pipelines still run the inherited
                // simplifier. This is not a prepared grouping choice or proof
                // of the original final-retention requirement.
                AssertActualDelegateTarget(native.Final.Content, native.Final.Name, expectedTargetPairCount: 0);
                AssertActualDelegateTarget(actual.Final.Content, actual.Final.Name, expectedTargetPairCount: 0);
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerOutput(string directory, bool enabled)
        {
            string prefix = enabled ? "prepared" : "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, prefix + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, prefix + "-isolated")).FullName;
            string configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: configuration,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("DelegateTargetInputs")));
            var owner = new Clock();
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeFile? selected = null;
            List<object> visits = [];
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (!enabled) { Assert.That(prepared, Is.Null); return; }
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(prepared!.PocGroupingCount, Is.Zero);
                    var occurrences = prepared.GetSourceFile(owner)!.Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
                    var call = occurrences.Single(node => ReferenceEquals(node.Producer, owner.Call));
                    Assert.That(call.Children.Select(node => node.Id.EdgePath), Is.EqualTo(new[] { call.Id.EdgePath + "/target", call.Id.EdgePath + "/argument" }));
                    var outer = call.Children[0];
                    var inner = outer.Children.Single();
                    var formal = inner.Children.Single();
                    object[] expected = [owner.Call, owner.Outer, owner.Inner, owner.Formal, owner.Argument];
                    var nodes = new[] { call, outer, inner, formal, call.Children[1] };
                    Assert.That(nodes.All(node => !node.IsOpaque), Is.True);
                    Assert.That(nodes.Select(node => node.Id).Distinct().Count(), Is.EqualTo(expected.Length));
                    for (int i = 0; i < expected.Length; i++) { Assert.That(nodes[i].Producer, Is.SameAs(expected[i])); }
                    prepared.PocNativeWriteObserver = node =>
                    {
                        if (Array.Exists(expected, producer => ReferenceEquals(producer, node))) { visits.Add(node); }
                    };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.Zero);
                    Assert.That(file.PreparedGroupingCount, Is.Zero);
                    if (enabled) { Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True); }
                    Assert.That(file.SelectPreparedForms(false, false, false).Content, Is.EqualTo(file.Content));
                },
                PocSelectedFileObserved = file => selected = file
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(enabled));
            if (enabled)
            {
                object[] expected = [owner.Call, owner.Outer, owner.Inner, owner.Formal, owner.Argument];
                Assert.That(visits, Has.Count.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++) { Assert.That(visits[i], Is.SameAs(expected[i])); }
            }
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static void AssertActualDelegateTarget(string text, string name, int expectedTargetPairCount = 2)
        {
            var tree = CSharpSyntaxTree.ParseText(text, path: name);
            var compilation = CSharpCompilation.Create("DelegateTarget", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var call = (InvocationExpressionSyntax)method.ExpressionBody!.Expression;
            Assert.That(call.ToString(), Is.EqualTo(expectedTargetPairCount == 2 ? "((f))(1)" : "f(1)"));
            ExpressionSyntax target = call.Expression;
            int targetPairCount = 0;
            while (target is ParenthesizedExpressionSyntax wrapper)
            {
                targetPairCount++;
                Assert.That(wrapper.OpenParenToken.IsMissing || wrapper.CloseParenToken.IsMissing, Is.False);
                target = wrapper.Expression;
            }
            Assert.That(targetPairCount, Is.EqualTo(expectedTargetPairCount));
            Assert.That(target.ToString(), Is.EqualTo("f"));
            Assert.That(call.ArgumentList.OpenParenToken.IsMissing || call.ArgumentList.CloseParenToken.IsMissing, Is.False);
            Assert.That(call.ArgumentList.Arguments.Single().Expression.ToString(), Is.EqualTo("1"));
            var model = compilation.GetSemanticModel(tree);
            var formal = model.GetDeclaredSymbol(method)!.Parameters.Single();
            var invoke = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target).Symbol, formal), Is.True);
            Assert.That(invoke.TargetMethod.MethodKind, Is.EqualTo(MethodKind.DelegateInvoke));
            Assert.That(SymbolEqualityComparer.Default.Equals(invoke.TargetMethod, ((INamedTypeSymbol)formal.Type).DelegateInvokeMethod), Is.True);
            Assert.That(invoke.TargetMethod.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(invoke.TargetMethod.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(invoke.Arguments.Single().Value.ConstantValue.Value, Is.EqualTo(1));
            int count = 0;
            int observed = -1;
            Func<int, int> witness = value => { count++; observed = value; return value + 41; };
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [witness]).Single(), Is.EqualTo(42));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(1));
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
            public CompilerBuildInputCapture Capture => new(directory, "direct-delegate-target", "Debug|net10.0", "12.0", "Actual", [], [],
                [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock : TypeProvider
        {
            internal DirectDelegateInvocationExpression Call { get; private set; } = null!;
            internal ParenthesizedExpression Outer { get; private set; } = null!;
            internal ParenthesizedExpression Inner { get; private set; } = null!;
            internal VariableExpression Formal { get; private set; } = null!;
            internal LiteralExpression Argument { get; private set; } = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var parameter = new ParameterProvider("f", $"", typeof(Func<int, int>));
                Formal = parameter;
                Inner = new ParenthesizedExpression(Formal);
                Outer = new ParenthesizedExpression(Inner);
                Argument = new LiteralExpression(1);
                Call = new DirectDelegateInvocationExpression(Outer, Argument);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [parameter]);
                return [new MethodProvider(signature, Call, this)];
            }
        }
    }
}

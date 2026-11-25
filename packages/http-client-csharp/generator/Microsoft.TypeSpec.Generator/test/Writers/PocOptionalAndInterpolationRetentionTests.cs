// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
                if (mode != "interpolation")
                {
                    Assert.That(actual.Selected.Content, Does.Contain("Unrelated() => (42);"));
                    Assert.That(actual.Final.Content, Does.Contain("Unrelated() => 42;"));
                    Assert.That(native.Final.Content, Does.Contain("Unrelated() => 42;"));
                }
                Assert.That(nativeFinal, Is.EqualTo(expected), "The exact optional default and its descendants must survive final reduction");
            }
            finally { directory.Delete(recursive: true); }
        }

        [Test]
        public async Task OriginalConditionalAccessPairPreservesUngatedLengthAccess()
        {
            var directory = Directory.CreateTempSubdirectory("task05-conditional-access-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, "conditional-access", enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, "conditional-access", enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(AssertConditionalAccessBindingAndRuntime(actual.Selected), Is.EqualTo("(value?.Trim()).Length"));
                Assert.That(AssertConditionalAccessBindingAndRuntime(native.Selected), Is.EqualTo("(value?.Trim()).Length"));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                string final = AssertConditionalAccessBindingAndRuntime(actual.Final);
                string nativeFinal = AssertConditionalAccessBindingAndRuntime(native.Final);
                TestContext.WriteLine($"CONDITIONAL-ACCESS independent native final `{nativeFinal}`, candidate final `{final}`");
                Assert.That(nativeFinal, Is.EqualTo("(value?.Trim()).Length"));
                Assert.That(final, Is.EqualTo(nativeFinal));

                const string forced = "namespace Sample; public class Clock { public int M(string value) => value?.Trim().Length; }";
                var tree = CSharpSyntaxTree.ParseText(forced, path: "Forced.cs");
                var compilation = ConditionalAccessCompilation(tree);
                var access = tree.GetRoot().DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Single();
                var conditionalType = (INamedTypeSymbol)compilation.GetSemanticModel(tree).GetTypeInfo(access).Type!;
                Assert.That(conditionalType.OriginalDefinition.SpecialType, Is.EqualTo(SpecialType.System_Nullable_T));
                Assert.That(conditionalType.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "CS0266" }));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static CSharpCompilation ConditionalAccessCompilation(SyntaxTree tree) => CSharpCompilation.Create("ConditionalAccessWitness", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static string AssertConditionalAccessBindingAndRuntime(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = ConditionalAccessCompilation(tree);
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var length = (MemberAccessExpressionSyntax)method.ExpressionBody!.Expression;
            var grouped = (ParenthesizedExpressionSyntax)length.Expression;
            var access = (ConditionalAccessExpressionSyntax)grouped.Expression;
            var call = (InvocationExpressionSyntax)access.WhenNotNull;
            var property = ((IPropertyReferenceOperation)model.GetOperation(length)!).Property;
            var invocation = ((IInvocationOperation)model.GetOperation(call)!).TargetMethod;
            Assert.That(property.Name, Is.EqualTo("Length"));
            Assert.That(property.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(property.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(invocation.Name, Is.EqualTo("Trim"));
            Assert.That(invocation.Parameters, Is.Empty);
            Assert.That(invocation.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(access.Expression).Symbol,
                model.GetDeclaredSymbol(method)!.Parameters.Single()), Is.True);
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [["  abc  "]]), Is.EqualTo(new object?[] { 3 }));
            var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [[null]]));
            Assert.That(failure!.InnerException, Is.TypeOf<NullReferenceException>());
            return length.ToString();
        }

        [Test]
        public async Task OriginalStackAllocationRetainsItsInitializerPairAtNativeSelection()
        {
            const string retained = "(stackalloc byte[8])";
            const string source = "namespace Sample { public class Clock { public int M() { System.Span<byte> value = (stackalloc byte[8]); return value.Length; } } }";
            Assert.That(AssertStackAllocationBindingAndRuntime(new CodeFile(source, "Clock.cs")), Is.EqualTo(retained));
            var directory = Directory.CreateTempSubdirectory("task05-stackalloc-retention-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, "stackalloc", enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, "stackalloc", enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(AssertStackAllocationBindingAndRuntime(actual.Selected), Is.EqualTo(retained));
                Assert.That(AssertStackAllocationBindingAndRuntime(native.Selected), Is.EqualTo(retained));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                string final = AssertStackAllocationBindingAndRuntime(actual.Final);
                string nativeFinal = AssertStackAllocationBindingAndRuntime(native.Final);
                // The inherited final simplifier removes the pair from both
                // outputs, independently of native prepared selection.
                Assert.That(nativeFinal, Is.EqualTo("stackalloc byte[8]"));
                Assert.That(final, Is.EqualTo(nativeFinal));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static string AssertStackAllocationBindingAndRuntime(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("StackAllocationWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.Options.AllowUnsafe, Is.False);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var declaration = (LocalDeclarationStatementSyntax)method.Body!.Statements[0];
            var variable = declaration.Declaration.Variables.Single();
            var initializer = variable.Initializer!.Value;
            var allocation = initializer.DescendantNodesAndSelf().OfType<StackAllocArrayCreationExpressionSyntax>().Single();
            var array = (ArrayTypeSyntax)allocation.Type;
            var rank = array.RankSpecifiers.Single();
            Assert.That(allocation.StackAllocKeyword.IsMissing || rank.OpenBracketToken.IsMissing || rank.CloseBracketToken.IsMissing, Is.False);
            Assert.That(model.GetTypeInfo(array.ElementType).Type?.SpecialType, Is.EqualTo(SpecialType.System_Byte));
            Assert.That(model.GetConstantValue(rank.Sizes.Single()).Value, Is.EqualTo(8));
            var local = (ILocalSymbol)model.GetDeclaredSymbol(variable)!;
            var span = (INamedTypeSymbol)local.Type;
            var coreSpan = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly.GetTypeByMetadataName("System.Span`1");
            Assert.That(coreSpan, Is.Not.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(span.OriginalDefinition, coreSpan), Is.True);
            Assert.That(span.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Byte));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(initializer).ConvertedType, local.Type), Is.True);
            var length = (MemberAccessExpressionSyntax)((ReturnStatementSyntax)method.Body.Statements[1]).Expression!;
            var property = ((IPropertyReferenceOperation)model.GetOperation(length)!).Property;
            Assert.That(property.Name, Is.EqualTo("Length"));
            Assert.That(SymbolEqualityComparer.Default.Equals(property.ContainingType, span), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(length.Expression).Symbol, local), Is.True);
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [Array.Empty<object?>()]).Single(), Is.EqualTo(8));
            return initializer.ToString();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalNestedBinaryCallerExpressionRetainsAtNativeSelection(bool explicitText)
        {
            var directory = Directory.CreateTempSubdirectory("task05-nested-binary-cae-");
            try
            {
                string mode = explicitText ? "nested-cae-explicit" : "nested-cae-omitted";
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(actual.Selected.Content, Does.Contain("return ((x + y));"));
                string captured = AssertActualBinaryCapture(actual.Selected, explicitText);
                const string nativeLambda = "() => \n        {\n            return ((x + y));\n        }";
                Assert.That(captured, Is.EqualTo(explicitText ? "manual" : nativeLambda));
                Assert.That(AssertActualBinaryCapture(native.Selected, explicitText), Is.EqualTo(captured));
                if (!explicitText)
                {
                    const string compactLambda = "() => { return ((x + y)); }";
                    const string compact = "namespace Sample; public class Clock { public object M(int x, int y) => Capture(" + compactLambda +
                        "); private object Capture(System.Func<int> value, [System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string text = \"\") => text; }";
                    Assert.That(AssertActualBinaryCapture(new CodeFile(compact, "Compact.cs"), explicitText: false), Is.EqualTo(compactLambda));
                    Assert.That(captured, Is.Not.EqualTo(compactLambda), "Ordinary native formatting is visible to the caller-expression argument");
                }
                const string before = "return ((x + y));";
                const string after = "return x + y;";
                string forced = actual.Selected.Content.Replace(before, after, StringComparison.Ordinal);
                Assert.That(forced, Is.Not.EqualTo(actual.Selected.Content));
                string counterfactual = AssertActualBinaryCapture(new CodeFile(forced, actual.Selected.Name), explicitText);
                if (explicitText) { Assert.That(counterfactual, Is.EqualTo(captured)); }
                else { Assert.That(counterfactual, Is.Not.EqualTo(captured)); }

                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                string nativeFinal = AssertActualBinaryCapture(native.Final, explicitText);
                string actualFinal = AssertActualBinaryCapture(actual.Final, explicitText);
                Assert.That(actualFinal, Is.EqualTo(nativeFinal));
                TestContext.WriteLine($"NESTED-CAE-FINAL explicit={explicitText}; selected={System.Text.Json.JsonSerializer.Serialize(captured)}; nativeFinal={System.Text.Json.JsonSerializer.Serialize(nativeFinal)}; actualFinal={System.Text.Json.JsonSerializer.Serialize(actualFinal)}");
                // Protect the observed lambda, not unrelated reductions in the
                // same document or a lambda whose caller text is explicit.
                Assert.That(actual.Selected.Content, Does.Contain("Unrelated() => (42);"));
                Assert.That(native.Final.Content, Does.Contain("Unrelated() => 42;"));
                Assert.That(actual.Final.Content, Does.Contain("Unrelated() => 42;"));
                if (explicitText)
                {
                    Assert.That(native.Final.Content, Does.Contain(after).And.Not.Contain(before));
                    Assert.That(actual.Final.Content, Does.Contain(after).And.Not.Contain(before));
                    Assert.That(nativeFinal, Is.EqualTo("manual"));
                }
                else
                {
                    Assert.That(nativeFinal, Is.EqualTo(captured), "The final simplifier must not change the actual implicit caller-expression value");
                    Assert.That(native.Final.Content, Does.Contain(before));
                    Assert.That(actual.Final.Content, Does.Contain(before));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("nested-cae-no-attribute", false)]
        [TestCase("nested-cae-disable-reduce", true)]
        public async Task DownstreamNestedBinaryReductionHonorsItsBoundary(string mode, bool disableReduce)
        {
            var directory = Directory.CreateTempSubdirectory("task05-nested-binary-boundary-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true, disableRoslynReduce: disableReduce);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false, disableRoslynReduce: disableReduce);
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(actual.Selected.Content, Does.Contain("return ((x + y));"));
                string selected = AssertActualBinaryCapture(actual.Selected, explicitText: false, hasCaptureAttribute: disableReduce);
                string final = AssertActualBinaryCapture(actual.Final, explicitText: false, hasCaptureAttribute: disableReduce);
                Assert.That(final, Is.EqualTo(selected));
                Assert.That(AssertActualBinaryCapture(native.Final, explicitText: false, hasCaptureAttribute: disableReduce), Is.EqualTo(final));
                Assert.That(native.Final.Content, Is.EqualTo(actual.Final.Content));
                if (disableReduce)
                {
                    Assert.That(final, Does.Contain("return ((x + y));"));
                    Assert.That(actual.Final.Content, Does.Contain("Unrelated() => (42);"));
                }
                else
                {
                    Assert.That(final, Is.Empty);
                    Assert.That(actual.Final.Content, Does.Contain("return x + y;").And.Not.Contain("return ((x + y));"));
                    Assert.That(actual.Final.Content, Does.Contain("Unrelated() => 42;"));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        [Test]
        public async Task DownstreamReductionPreservesBothImplicitCapturesInOneDocument()
        {
            var directory = Directory.CreateTempSubdirectory("task05-two-nested-binary-cae-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, "nested-cae-two-sites", enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, "nested-cae-two-sites", enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                var methods = new[] { "M", "Second" };
                string[] expected = ["() => \n        {\n            return ((x + y));\n        }", "() => \n        {\n            return ((left + right));\n        }"];
                Assert.That(methods.Select(method => AssertActualBinaryCapture(actual.Selected, explicitText: false, entryName: method)).ToArray(), Is.EqualTo(expected));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                Assert.That(native.Final.Content, Does.Contain("Unrelated() => 42;"));
                Assert.That(actual.Final.Content, Does.Contain("Unrelated() => 42;"));
                var nativeCaptures = methods.Select(method => AssertActualBinaryCapture(native.Final, explicitText: false, entryName: method)).ToArray();
                var actualCaptures = methods.Select(method => AssertActualBinaryCapture(actual.Final, explicitText: false, entryName: method)).ToArray();
                Assert.That(actualCaptures, Is.EqualTo(nativeCaptures));
                Assert.That(actualCaptures, Is.EqualTo(expected), "Both actual implicit caller-expression values must survive downstream reduction");
                Assert.That(AssertActualBinaryCapture(actual.Selected, explicitText: true, entryName: "Manual"), Is.EqualTo("manual"));
                Assert.That(actual.Selected.Content, Does.Contain("return ((first + last));"));
                foreach (var final in new[] { actual.Final, native.Final })
                {
                    Assert.That(final.Content, Does.Contain("return first + last;").And.Not.Contain("return ((first + last));"));
                    Assert.That(AssertActualBinaryCapture(final, explicitText: true, entryName: "Manual"), Is.EqualTo("manual"));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("nested-cae-lookalike")]
        [TestCase("nested-cae-three-sites")]
        public async Task UnsupportedCallerExpressionCombinationsKeepOrdinaryReduction(string mode)
        {
            var directory = Directory.CreateTempSubdirectory("task05-cae-unsupported-combination-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(actual.Final.Content, Is.EqualTo(native.Final.Content));
                const string before = "return ((x + y));";
                const string after = "return x + y;";
                string selected = AssertActualBinaryCapture(actual.Selected, explicitText: false);
                Assert.That(selected, Does.Contain(before));
                foreach (var final in new[] { actual.Final, native.Final })
                {
                    Assert.That(AssertActualBinaryCapture(final, explicitText: false), Is.EqualTo(selected.Replace(before, after, StringComparison.Ordinal)));
                    Assert.That(final.Content, Does.Contain("Unrelated() => 42;"));
                    if (mode == "nested-cae-lookalike")
                    {
                        Assert.That(AssertActualBinaryCapture(final, explicitText: false, hasCaptureAttribute: false, entryName: "Second", captureName: "Plain"), Is.Empty);
                        Assert.That(final.Content, Does.Contain("return left + right;"));
                    }
                    else
                    {
                        Assert.That(AssertActualBinaryCapture(final, explicitText: false, entryName: "Second"), Does.Contain("return left + right;"));
                        Assert.That(AssertActualBinaryCapture(final, explicitText: false, entryName: "Third"), Does.Contain("return first + last;"));
                    }
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        private static string AssertActualBinaryCapture(CodeFile file, bool explicitText, bool hasCaptureAttribute = true, string entryName = "M", string captureName = "Capture")
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("NestedBinaryCaptureWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var syntax = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            var capture = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == captureName);
            var entry = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == entryName);
            var call = (InvocationExpressionSyntax)entry.ExpressionBody!.Expression;
            var target = model.GetDeclaredSymbol(capture)!;
            var invocation = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, target), Is.True);
            var attributes = target.Parameters[1].GetAttributes();
            if (hasCaptureAttribute)
            {
                var attribute = attributes.Single();
                Assert.That(attribute.AttributeClass?.ToDisplayString(), Is.EqualTo(typeof(CallerArgumentExpressionAttribute).FullName));
                Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo(target.Parameters[0].Name));
            }
            else { Assert.That(attributes, Is.Empty); }
            var lambda = (LambdaExpressionSyntax)call.ArgumentList.Arguments[0].Expression;
            var binary = lambda.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
            var addition = (IBinaryOperation)model.GetOperation(binary)!;
            Assert.That(addition.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
            Assert.That(addition.OperatorMethod, Is.Null);
            Assert.That(addition.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            var parameters = model.GetDeclaredSymbol(entry)!.Parameters;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Left).Symbol, parameters[0]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Right).Symbol, parameters[1]), Is.True);
            var text = invocation.Arguments.Single(argument => argument.Parameter?.Ordinal == 1);
            Assert.That(text.IsImplicit, Is.EqualTo(!explicitText));
            Assert.That(text.ArgumentKind, Is.EqualTo(explicitText ? ArgumentKind.Explicit : ArgumentKind.DefaultValue));
            Assert.That(text.Value.ConstantValue.HasValue, Is.True);
            var observed = (string)text.Value.ConstantValue.Value!;
            Assert.That(observed, Is.EqualTo(explicitText ? "manual" : hasCaptureAttribute ? lambda.ToString() : ""));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", entryName, [3, 4], [int.MinValue, -1]),
                Is.EqualTo(new object?[] { observed, observed }));
            return observed;
        }

        [TestCase("+")]
        [TestCase("-")]
        public async Task OriginalBareRightGroupingDoesNotAuthorizeReassociation(string operation)
        {
            var directory = Directory.CreateTempSubdirectory("task05-right-reassociation-");
            try
            {
                string mode = operation == "+" ? "right-addition" : "right-subtraction";
                string retained = $"x {operation} (y {operation} z)";
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                var selected = CSharpSyntaxTree.ParseText(actual.Selected.Content, path: actual.Selected.Name);
                var expression = selected.GetRoot().DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression;
                Assert.That(expression.ToString(), Is.EqualTo(retained));
                var binary = (BinaryExpressionSyntax)expression;
                var inner = (BinaryExpressionSyntax)((ParenthesizedExpressionSyntax)binary.Right).Expression;
                Assert.That(inner.Left.ToString(), Is.EqualTo("y"));
                Assert.That(inner.Right.ToString(), Is.EqualTo("z"));
                string forced = $"namespace Sample; public class Clock {{ public int M(int x, int y, int z) => x {operation} y {operation} z; }}";
                Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(actual.Selected.Content, forced));
                if (operation == "+")
                {
                    // Unchecked addition can hide reassociation. The checked
                    // witness distinguishes the actual trees without enabling
                    // a new checked generation or preparation profile.
                    object?[][] arguments = [[int.MaxValue, 1, -1]];
                    Assert.That(ExecuteArithmetic(actual.Selected, arguments, checkOverflow: true), Is.EqualTo(new object?[] { int.MaxValue }));
                    var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                        ExecuteArithmetic(new CodeFile(forced, "Forced.cs"), arguments, checkOverflow: true));
                    Assert.That(failure!.InnerException, Is.TypeOf<OverflowException>());
                }
                else
                {
                    object?[][] arguments = [[10, 7, 3]];
                    Assert.That(ExecuteArithmetic(actual.Selected, arguments), Is.EqualTo(new object?[] { 6 }));
                    Assert.That(ExecuteArithmetic(new CodeFile(forced, "Forced.cs"), arguments), Is.EqualTo(new object?[] { 0 }));
                }
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                object?[][] finalArguments = [[10, 7, 3], [int.MinValue, 1, 1]];
                Assert.That(ExecuteArithmetic(actual.Final, finalArguments), Is.EqualTo(ExecuteArithmetic(native.Final, finalArguments)));
                // The inherited final simplifier reassociates the unchecked
                // addition in both pipelines; subtraction stays right-grouped.
                string finalSpelling = operation == "+" ? "x + y + z" : retained;
                Assert.That(PreparedParenthesesTests.MethodExpression(native.Final.Content).ToString(), Is.EqualTo(finalSpelling));
                Assert.That(PreparedParenthesesTests.MethodExpression(actual.Final.Content).ToString(), Is.EqualTo(finalSpelling));
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalFloatingOrCheckedRightGroupingSurvivesActualFinal(bool checkedArithmetic)
        {
            var directory = Directory.CreateTempSubdirectory("task05-observable-reassociation-");
            try
            {
                string mode = checkedArithmetic ? "checked-right-addition" : "floating-right-addition";
                string retained = checkedArithmetic ? "checked(x + (y + z))" : "x + (y + z)";
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(PreparedParenthesesTests.MethodExpression(actual.Selected.Content).ToString(), Is.EqualTo(retained));
                Assert.That(PreparedParenthesesTests.MethodExpression(native.Selected.Content).ToString(), Is.EqualTo(retained));
                object?[][] arguments = checkedArithmetic ? [[int.MinValue, int.MaxValue, 1]] : [[1e20, -1e20, 3.0]];
                object retainedValue = checkedArithmetic ? nameof(OverflowException) : 0.0;
                Assert.That(ObserveArithmetic(actual.Selected, arguments), Is.EqualTo(retainedValue));
                Assert.That(ObserveArithmetic(native.Selected, arguments), Is.EqualTo(retainedValue));
                string wronglyAssociated = checkedArithmetic ? "checked(x + y + z)" : "x + y + z";
                string forced = actual.Selected.Content.Replace(retained, wronglyAssociated, StringComparison.Ordinal);
                Assert.That(forced, Is.Not.EqualTo(actual.Selected.Content));
                Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(actual.Selected.Content, forced));
                Assert.That(ObserveArithmetic(new CodeFile(forced, actual.Selected.Name), arguments), Is.EqualTo(checkedArithmetic ? 0 : 3.0));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                TestContext.WriteLine($"OBSERVABLE-REASSOCIATION {mode}: disabled final `{PreparedParenthesesTests.MethodExpression(native.Final.Content)}`, enabled final `{PreparedParenthesesTests.MethodExpression(actual.Final.Content)}`");
                Assert.That(PreparedParenthesesTests.MethodExpression(native.Final.Content).ToString(), Is.EqualTo(retained));
                Assert.That(PreparedParenthesesTests.MethodExpression(actual.Final.Content).ToString(), Is.EqualTo(retained));
                Assert.That(ObserveArithmetic(actual.Final, arguments), Is.EqualTo(retainedValue));
                Assert.That(ObserveArithmetic(native.Final, arguments), Is.EqualTo(retainedValue));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static object? ObserveArithmetic(CodeFile file, object?[][] arguments)
        {
            try { return ExecuteArithmetic(file, arguments).Single(); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is OverflowException)
            { return nameof(OverflowException); }
        }

        private static object?[] ExecuteArithmetic(CodeFile file, object?[][] arguments, bool checkOverflow = false)
        {
            var compilation = CSharpCompilation.Create("RightReassociationWitness", [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkOverflow));
            return PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", arguments);
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerOutput(string directory, string mode, bool enabled, bool disableRoslynReduce = false)
        {
            string prefix = enabled ? "prepared" : "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, prefix + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, prefix + "-isolated")).FullName;
            string configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-roslyn-reduce\":" + (disableRoslynReduce ? "true" : "false") + ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: configuration,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("OptionalInterpolationInputs")));
            var owner = new Clock(mode);
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeFile? selected = null;
            bool takesCapturedInputs = enabled && !disableRoslynReduce;
            int preparationObservations = 0;
            int rawObservations = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    preparationObservations++;
                    Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
                    if (takesCapturedInputs) { Assert.That(prepared, Is.Not.Null); }
                    else { Assert.That(prepared, Is.Null); }
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    rawObservations++;
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.Zero);
                    Assert.That(file.PreparedGroupingCount, Is.Zero);
                    Assert.That(file.SelectPreparedForms(false, false, false).Content, Is.EqualTo(file.Content));
                    if (takesCapturedInputs) { Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True); }
                    TestContext.WriteLine($"OPTIONAL-INTERPOLATION {mode}: enabled={enabled}; prepared={prepared is not null}; receipt={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                },
                PocSelectedFileObserved = file => selected = file
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(takesCapturedInputs));
            Assert.That(preparationObservations, Is.EqualTo(1));
            Assert.That(rawObservations, Is.EqualTo(1));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        [TestCase("nested-cae-defaults", true)]
        [TestCase("nested-cae-defaults-over-cap", false)]
        public async Task OptionalDefaultReductionComposesWithExistingCallerCapture(string mode, bool protectDefaults)
        {
            var directory = Directory.CreateTempSubdirectory("task05-cae-default-composition-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(AssertBindingAndRuntime(actual.Selected, "optional-literal", "LiteralDefault"), Is.EqualTo("value = (1)"));
                Assert.That(AssertBindingAndRuntime(actual.Selected, "optional-descendant", "DescendantDefault"), Is.EqualTo("value = (1 + (2))"));
                Assert.That(AssertBindingAndRuntime(actual.Selected, "optional-unmatched", "UnmatchedDefault"), Is.EqualTo("value = (2)"));
                string selectedCapture = AssertActualBinaryCapture(actual.Selected, explicitText: false);
                Assert.That(selectedCapture, Does.Contain("return ((x + y));"));
                if (!protectDefaults)
                {
                    Assert.That(AssertBindingAndRuntime(actual.Selected, "optional-literal", "ThirdExactDefault"), Is.EqualTo("value = (1)"));
                }
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                foreach (var output in new[] { actual.Final, native.Final })
                {
                    Assert.That(AssertActualBinaryCapture(output, explicitText: false), Is.EqualTo(selectedCapture));
                    Assert.That(AssertBindingAndRuntime(output, "optional-literal", "LiteralDefault"), Is.EqualTo(protectDefaults ? "value = (1)" : "value = 1"));
                    Assert.That(AssertBindingAndRuntime(output, "optional-descendant", "DescendantDefault"), Is.EqualTo(protectDefaults ? "value = (1 + (2))" : "value = 1 + 2"));
                    Assert.That(AssertBindingAndRuntime(output, "optional-unmatched", "UnmatchedDefault"), Is.EqualTo("value = 2"));
                    Assert.That(output.Content, Does.Contain("Unrelated() => 42;"));
                    if (!protectDefaults)
                    {
                        Assert.That(AssertBindingAndRuntime(output, "optional-literal", "ThirdExactDefault"), Is.EqualTo("value = 1"));
                    }
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("nested-cae-defaults-delegate", true)]
        [TestCase("nested-cae-defaults-delegates-over-cap", false)]
        public async Task DelegateRetentionComposesWithExistingFinalGuards(string mode, bool protectDelegate)
        {
            var directory = Directory.CreateTempSubdirectory("task05-cae-default-delegate-composition-");
            try
            {
                var actual = await WriteCompilerOutput(directory.FullName, mode, enabled: true);
                var native = await WriteCompilerOutput(directory.FullName, mode, enabled: false);
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                Assert.That(AssertGroupedDelegateWitness(actual.Selected, "InvokeFormal"), Is.EqualTo("((f))(1)"));
                if (!protectDelegate) { Assert.That(AssertGroupedDelegateWitness(actual.Selected, "OtherFormal"), Is.EqualTo("((f))(1)")); }
                string selectedCapture = AssertActualBinaryCapture(actual.Selected, explicitText: false);
                Assert.That(selectedCapture, Does.Contain("return ((x + y));"));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                foreach (var output in new[] { actual.Final, native.Final })
                {
                    Assert.That(AssertGroupedDelegateWitness(output, "InvokeFormal"), Is.EqualTo(protectDelegate ? "((f))(1)" : "f(1)"));
                    if (!protectDelegate) { Assert.That(AssertGroupedDelegateWitness(output, "OtherFormal"), Is.EqualTo("f(1)")); }
                    Assert.That(AssertActualBinaryCapture(output, explicitText: false), Is.EqualTo(selectedCapture));
                    Assert.That(AssertBindingAndRuntime(output, "optional-literal", "LiteralDefault"), Is.EqualTo("value = (1)"));
                    Assert.That(AssertBindingAndRuntime(output, "optional-descendant", "DescendantDefault"), Is.EqualTo("value = (1 + (2))"));
                    Assert.That(AssertBindingAndRuntime(output, "optional-unmatched", "UnmatchedDefault"), Is.EqualTo("value = 2"));
                    Assert.That(output.Content, Does.Contain("Unrelated() => 42;"));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        private static string AssertGroupedDelegateWitness(CodeFile file, string entryName)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("DelegateCompositionWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var entry = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == entryName);
            var call = (InvocationExpressionSyntax)entry.ExpressionBody!.Expression;
            var model = compilation.GetSemanticModel(tree);
            var formal = model.GetDeclaredSymbol(entry)!.Parameters.Single();
            var operation = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(operation.TargetMethod, ((INamedTypeSymbol)formal.Type).DelegateInvokeMethod), Is.True);
            int invocations = 0;
            Func<int, int> witness = value => { invocations++; return value + 42; };
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", entryName, [witness]).Single(), Is.EqualTo(43));
            Assert.That(invocations, Is.EqualTo(1));
            return call.ToString();
        }

        private static string AssertBindingAndRuntime(CodeFile file, string mode, string entryName = "M")
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("OptionalInterpolationWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == entryName);
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
            int expected = mode == "optional-literal" ? 1 : mode == "optional-unmatched" ? 2 : 3;
            Assert.That(parameter.HasExplicitDefaultValue, Is.True);
            Assert.That(parameter.ExplicitDefaultValue, Is.EqualTo(expected));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", entryName, [Type.Missing], [17]), Is.EqualTo(new object?[] { expected, 17 }));
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
                if (mode.StartsWith("nested-cae-", StringComparison.Ordinal))
                {
                    var x = new ParameterProvider("x", $"", typeof(int));
                    var y = new ParameterProvider("y", $"", typeof(int));
                    var grouped = new ParenthesizedExpression(new BinaryOperatorExpression("+", x, y));
                    var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(grouped));
                    var call = new InvokeMethodExpression(null, "Capture", mode == "nested-cae-explicit" ? [lambda, Literal("manual")] : new ValueExpression[] { lambda });
                    var captureValue = new ParameterProvider("value", $"", typeof(Func<int>));
                    var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                        attributes: mode == "nested-cae-no-attribute" ? [] : new AttributeStatement[] { new(typeof(CallerArgumentExpressionAttribute), Literal("value")) });
                    MethodProvider[] methods = [new(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, [x, y]), call, this),
                        new(new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null, [captureValue, text]), (VariableExpression)text, this),
                        new(new MethodSignature("Unrelated", null, MethodSignatureModifiers.Public, typeof(int), null, []), new ParenthesizedExpression(Literal(42)), this)];
                    if (mode is "nested-cae-defaults" or "nested-cae-defaults-over-cap" or "nested-cae-defaults-delegate" or "nested-cae-defaults-delegates-over-cap")
                    {
                        MethodProvider[] composed = [.. methods,
                            BuildOptional("LiteralDefault", new ParenthesizedExpression(Literal(1))),
                            BuildOptional("DescendantDefault", new BinaryOperatorExpression("+", Literal(1), new ParenthesizedExpression(Literal(2)))),
                            BuildOptional("UnmatchedDefault", new ParenthesizedExpression(Literal(2)))];
                        return mode switch
                        {
                            "nested-cae-defaults-over-cap" => [.. composed, BuildOptional("ThirdExactDefault", new ParenthesizedExpression(Literal(1)))],
                            "nested-cae-defaults-delegate" => [.. composed, BuildDelegate("InvokeFormal")],
                            "nested-cae-defaults-delegates-over-cap" => [.. composed, BuildDelegate("InvokeFormal"), BuildDelegate("OtherFormal")],
                            _ => composed
                        };
                    }
                    if (mode is not ("nested-cae-two-sites" or "nested-cae-lookalike" or "nested-cae-three-sites")) { return methods; }

                    MethodProvider BuildEntry(string method, string leftName, string rightName, string target, bool manual = false)
                    {
                        var left = new ParameterProvider(leftName, $"", typeof(int));
                        var right = new ParameterProvider(rightName, $"", typeof(int));
                        var nested = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(),
                            Return(new ParenthesizedExpression(new BinaryOperatorExpression("+", left, right))));
                        var invocation = new InvokeMethodExpression(null, target, manual ? [nested, Literal("manual")] : new ValueExpression[] { nested });
                        return new MethodProvider(new MethodSignature(method, null, MethodSignatureModifiers.Public, typeof(object), null, [left, right]), invocation, this);
                    }
                    if (mode == "nested-cae-lookalike")
                    {
                        var plainValue = new ParameterProvider("value", $"", typeof(Func<int>));
                        var plainText = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""));
                        var plain = new MethodProvider(new MethodSignature("Plain", null, MethodSignatureModifiers.Private, typeof(object), null, [plainValue, plainText]), (VariableExpression)plainText, this);
                        return [.. methods, plain, BuildEntry("Second", "left", "right", "Plain")];
                    }
                    var second = BuildEntry("Second", "left", "right", "Capture");
                    return mode == "nested-cae-three-sites"
                        ? [.. methods, second, BuildEntry("Third", "first", "last", "Capture")]
                        : [.. methods, second, BuildEntry("Manual", "first", "last", "Capture", manual: true)];
                }
                if (mode is "right-addition" or "right-subtraction")
                {
                    var x = new ParameterProvider("x", $"", typeof(int));
                    var y = new ParameterProvider("y", $"", typeof(int));
                    var z = new ParameterProvider("z", $"", typeof(int));
                    string operation = mode == "right-addition" ? "+" : "-";
                    var body = new BinaryOperatorExpression(operation, x, new BinaryOperatorExpression(operation, y, z), AddParentheses: false);
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [x, y, z]), body, this)];
                }
                if (mode is "floating-right-addition" or "checked-right-addition")
                {
                    bool checkedArithmetic = mode == "checked-right-addition";
                    CSharpType arithmeticType = checkedArithmetic ? typeof(int) : typeof(double);
                    var x = new ParameterProvider("x", $"", arithmeticType);
                    var y = new ParameterProvider("y", $"", arithmeticType);
                    var z = new ParameterProvider("z", $"", arithmeticType);
                    var addition = new BinaryOperatorExpression("+", x, new BinaryOperatorExpression("+", y, z), AddParentheses: false);
                    ValueExpression body = checkedArithmetic ? new CheckedExpression(addition) : addition;
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, arithmeticType, null, [x, y, z]), body, this)];
                }
                if (mode == "stackalloc")
                {
                    var declaration = new DeclarationExpression(typeof(Span<byte>), "value", out var local);
                    var array = new NewArrayExpression(typeof(byte), Size: Literal(8), IsStackAlloc: true);
                    var initialized = new AssignmentExpression(declaration, new ParenthesizedExpression(array)).Terminate();
                    var body = new MethodBodyStatements([initialized, Return(new MemberExpression(local, "Length"))]);
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, []), body, this)];
                }
                if (mode == "conditional-access")
                {
                    var receiver = new ParameterProvider("value", $"", typeof(string));
                    var target = new NullConditionalExpression((VariableExpression)receiver);
                    var trim = new InvokeMethodExpression(target, "Trim", Array.Empty<ValueExpression>());
                    var length = new MemberExpression(new ParenthesizedExpression(trim), "Length");
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [receiver]), length, this)];
                }
                if (mode == "interpolation")
                {
                    var flag = new ParameterProvider("flag", $"", typeof(bool));
                    var hole = new ParenthesizedExpression(new TernaryConditionalExpression(flag, Literal(1), Literal(2)));
                    return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(string), null, [flag]),
                        new FormattableStringExpression("value:{0}", new ValueExpression[] { hole }), this)];
                }
                ValueExpression defaultValue = mode == "optional-literal" ? new ParenthesizedExpression(Literal(1)) :
                    new BinaryOperatorExpression("+", Literal(1), new ParenthesizedExpression(Literal(2)));
                return [BuildOptional("M", defaultValue), new MethodProvider(new MethodSignature("Unrelated", null, MethodSignatureModifiers.Public, typeof(int), null, []), new ParenthesizedExpression(Literal(42)), this)];
            }

            private MethodProvider BuildDelegate(string name)
            {
                var formal = new ParameterProvider("f", $"", typeof(Func<int, int>));
                var target = new ParenthesizedExpression(new ParenthesizedExpression(formal));
                var call = new DirectDelegateInvocationExpression(target, Literal(1));
                return new MethodProvider(new MethodSignature(name, null, MethodSignatureModifiers.Public, typeof(int), null, [formal]), call, this);
            }

            private MethodProvider BuildOptional(string name, ValueExpression defaultValue)
            {
                var value = new ParameterProvider("value", $"", typeof(int), defaultValue: defaultValue);
                return new MethodProvider(new MethodSignature(name, null, MethodSignatureModifiers.Public, typeof(int), null, [value]), (VariableExpression)value, this);
            }
        }
    }
}

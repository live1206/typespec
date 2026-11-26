// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    // Original anchors: 7ac8b2640bcfa42f96e1095fb208de83b911fe71,
    // StructuralParenthesesTests / StructuralParenthesesSafetyTests. Keep the
    // original double wrappers in the source oracles. The native binary writer
    // supplies one wrapper, not an arbitrary parenthesized-expression IR node.
    [NonParallelizable]
    internal class PreparedParenthesesTests
    {
        private static readonly MetadataReference[] References =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        ];

        [SetUp]
        public async Task SetUp()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true}",
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            CodeModelGenerator.Instance = new ProbeGenerator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = mock.Object.SourceInputModel };
        }

        private sealed class ProbeGenerator(GeneratorContext context) : CodeModelGenerator(context) { }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void NativeBinaryReturnOrArrowOmitsUnnecessaryOuterGrouping(bool arrow, bool seed)
        {
            var x = Parameter("x");
            var y = Parameter("y");
            var binary = new BinaryOperatorExpression("==", x, y);
            var owner = new BinaryOwner(binary, [x, y], arrow, seed);
            Assert.That(ContractCollections.TryCopy(owner.Methods.Single().Signature.Parameters, out _), Is.True,
                "The native fixture must use an inspectable owned parameter inventory");
            string baseline = new TypeProviderWriter(owner).Write().Content;
            string original = Source(arrow
                ? "public bool M(int x, int y) => ((x == y));"
                : "public bool M(int x, int y) { return ((x == y)); }", seed);
            string expected = Source(arrow
                ? "public bool M(int x, int y) => x == y;"
                : "public bool M(int x, int y) { return x == y; }", seed);

            // Validate the faithful original, the actual native slot and the
            // desired edit before asserting the currently missing behavior.
            TestContext.WriteLine($"Native baseline (arrow={arrow}, seed={seed}):{Environment.NewLine}{baseline}");
            Assert.That(MethodExpression(baseline), Is.TypeOf<ParenthesizedExpressionSyntax>());
            AssertOperations(original, baseline);
            AssertOperations(original, expected);
            if (!seed)
            {
                Assert.That(owner.Fields, Is.Empty);
                Assert.That(owner.Properties, Is.Empty);
                Assert.That(baseline, Does.Not.Contain("global::"), "No dummy recorded type reference");
            }

            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            AssertOperations(original, file.Content);
            object?[][] runtimeArguments = [[0, 0], [1, 2], [-1, -1], [int.MinValue, int.MaxValue]];
            Assert.That(Execute(file.Content, runtimeArguments), Is.EqualTo(Execute(original, runtimeArguments)));
            Assert.That(file.QualifiedContent ?? file.Content, Is.EqualTo(baseline), "Retain native/raw fallback");
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(file.Content));
            Assert.That(binary.AddParentheses, Is.True, "Permission must not mutate the shared expression");
            Assert.That(MethodExpression(file.Content), Is.TypeOf<BinaryExpressionSyntax>(),
                "Task05 must omit the outer grouping in the prepared raw writer, before legacy processing. " +
                "Actual prepared expression: " + MethodExpression(file.Content));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeRightAssociatedSubtractionRetainsRequiredOperandGrouping(bool arrow)
        {
            var x = Parameter("x");
            var y = Parameter("y");
            var z = Parameter("z");
            var right = new BinaryOperatorExpression("-", y, z);
            var binary = new BinaryOperatorExpression("-", x, right);
            var owner = new BinaryOwner(binary, [x, y, z], arrow, seed: false, subtraction: true);
            string original = Source(arrow
                ? "public int M(int x, int y, int z) => x - (y - z);"
                : "public int M(int x, int y, int z) { return x - (y - z); }");
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();

            AssertOperations(original, baseline);
            AssertOperations(original, file.Content);
            object?[][] runtimeArguments = [[10, 7, 3], [0, 1, 2], [-5, -2, 4]];
            Assert.That(Execute(file.Content, runtimeArguments), Is.EqualTo(Execute(original, runtimeArguments)));
            Assert.That(Execute(file.Content, [10, 7, 3]).Single(), Is.EqualTo(6));
            var expression = MethodExpression(file.Content);
            while (expression is ParenthesizedExpressionSyntax wrapper) { expression = wrapper.Expression; }
            Assert.That(expression, Is.TypeOf<BinaryExpressionSyntax>());
            Assert.That(((BinaryExpressionSyntax)expression).Right, Is.TypeOf<ParenthesizedExpressionSyntax>());
            Assert.That(right.AddParentheses, Is.True);
        }

        [Test]
        public void NativeObservedLambdaReturnRetainsGroupingAndCapturedText()
        {
            var x = Parameter("x");
            var y = Parameter("y");
            var binary = new BinaryOperatorExpression("+", x, y);
            var owner = new BinaryOwner(binary, [x, y], arrow: true, seed: false, captured: true);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();

            AssertOperations(baseline, file.Content);
            Assert.That(CapturedText(baseline), Does.Contain("return (x + y);"));
            Assert.That(CapturedText(file.Content), Is.EqualTo(CapturedText(baseline)));
            Assert.That(Execute(file.Content, [3, 4]), Is.EqualTo(Execute(baseline, [3, 4])));
            Assert.That(Execute(file.Content, [3, 4]).Single(), Is.EqualTo(CapturedText(file.Content)));
            var statement = CSharpSyntaxTree.ParseText(file.Content).GetRoot().DescendantNodes()
                .OfType<ReturnStatementSyntax>().Single();
            Assert.That(statement.Expression, Is.TypeOf<ParenthesizedExpressionSyntax>());
            Assert.That(binary.AddParentheses, Is.True);
        }

        [Test]
        public void OriginalObservedLambdaOracleDetectsRemovalInsideCapturedBody()
        {
            // Preserve the complete original mixed fixture, including Seed and
            // the omitted caller-expression argument. Do not normalize its text.
            string original = Source("public object M(int x, int y) => Capture(() => { return ((x + y)); }); " +
                "private object Capture(System.Func<int> value, " +
                "[System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string text = \"\") => text;", seed: true);
            string unsafeEdit = original.Replace("return ((x + y));", "return x + y;", StringComparison.Ordinal);
            Assert.That(CapturedText(original), Is.EqualTo("() => { return ((x + y)); }"));
            Assert.That(CapturedText(unsafeEdit), Is.Not.EqualTo(CapturedText(original)));
            Assert.Throws<AssertionException>(() => AssertOperations(original, unsafeEdit));
        }

        [Test]
        public void NativeChangedChildUsesSamePassRetainedOutputWithoutAnotherTraversal()
        {
            var x = Parameter("x");
            var y = Parameter("y");
            var z = Parameter("z");
            var right = new MutableChildExpression(y);
            int writes = 0;
            var left = new OnWriteExpression(() => { writes++; right.Update(z); }, x);
            var binary = new BinaryOperatorExpression("==", left, right);
            var owner = new BinaryOwner(binary, [x, y, z], arrow: false, seed: false);
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(writes, Is.Zero, "Preparation must not invoke the native writer");
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();

            Assert.That(writes, Is.EqualTo(1), "An opaque changing child must not trigger a replay");
            Assert.That(file.Content, Does.Contain("return (x == z);"));
            AssertOperations(Source("public bool M(int x, int y, int z) { return (x == z); }"), file.Content);
            // This is a conservative native/fallback control, not evidence that
            // an opaque child was positively admitted to a prepared grouping edit.
        }

        [Test]
        public void OperationOracleDetectsSubtractionReassociation()
        {
            string original = Source("public int M(int x, int y, int z) => x - (y - z);");
            string unsafeEdit = Source("public int M(int x, int y, int z) => x - y - z;");
            Assert.Throws<AssertionException>(() => AssertOperations(original, unsafeEdit));
            Assert.That(Execute(original, [10, 7, 3]), Is.Not.EqualTo(Execute(unsafeEdit, [10, 7, 3])));
        }

        private static object?[] Execute(string source, params object?[][] arguments) =>
            PreparedGeneratedRuntime.Invoke(Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(source)),
                "Sample.Clock", "M", arguments);

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("Task05Parentheses",
            references: References, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static ParameterProvider Parameter(string name) => new(name, $"", typeof(int));

        private static string Source(string member, bool seed = false) =>
            "namespace Sample { public class Clock { " +
            (seed ? "public global::System.DateTimeOffset Seed => default; " : "") + member + " } }";

        internal static ExpressionSyntax MethodExpression(string source)
        {
            var method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "M");
            return method.ExpressionBody?.Expression ?? method.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!;
        }

        private static (SemanticModel Model, SyntaxNode Root) Bind(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = Compilation().AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
                "The fixture must compile before it can establish grammar or semantic equivalence");
            return (compilation.GetSemanticModel(tree), tree.GetRoot());
        }

        private static string CapturedText(string source)
        {
            var (model, root) = Bind(source);
            var syntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)model.GetOperation(syntax)!;
            var argument = operation.Arguments.Single(a => a.Parameter!.Name == "text");
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            return (string)argument.Value.ConstantValue.Value!;
        }

        internal static void AssertOperations(string before, string after)
        {
            static string Symbol(ISymbol? symbol) => symbol?.OriginalDefinition.GetDocumentationCommentId() ?? "";
            static string Type(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
            static string Conversion(CommonConversion conversion) => conversion.Exists + ":" + conversion.IsIdentity + ":" +
                conversion.IsNumeric + ":" + conversion.IsReference + ":" + conversion.IsUserDefined + ":" + Symbol(conversion.MethodSymbol);
            string Fingerprint(IOperation operation)
            {
                if (operation is IParenthesizedOperation wrapper) return Fingerprint(wrapper.Operand);
                string detail = operation switch
                {
                    IBinaryOperation binary => binary.OperatorKind + ":" + binary.IsChecked + ":" + binary.IsLifted + ":" + Symbol(binary.OperatorMethod),
                    IConversionOperation conversion => conversion.IsChecked + ":" + conversion.IsTryCast + ":" + Conversion(conversion.Conversion),
                    IInvocationOperation invocation => Symbol(invocation.TargetMethod),
                    IArgumentOperation argument => argument.Parameter?.Ordinal + ":" + argument.ArgumentKind + ":" +
                        Conversion(argument.InConversion) + ":" + Conversion(argument.OutConversion),
                    IParameterReferenceOperation parameter => parameter.Parameter.Ordinal + ":" + parameter.Parameter.Name,
                    ILocalReferenceOperation local => local.Local.Name,
                    ITypeOfOperation typeOf => Type(typeOf.TypeOperand),
                    _ => ""
                };
                string constant = operation.ConstantValue.HasValue
                    ? (operation.ConstantValue.Value?.GetType().FullName ?? "null") + ":" +
                      Convert.ToString(operation.ConstantValue.Value, CultureInfo.InvariantCulture)
                    : "<none>";
                bool implicitFlag = operation is IArgumentOperation { ArgumentKind: ArgumentKind.Explicit } ? false : operation.IsImplicit;
                return operation.Kind + ":" + Type(operation.Type) + ":" + implicitFlag + ":" + detail + ":" + constant +
                    "[" + string.Join("|", operation.ChildOperations.Select(Fingerprint)) + "]";
            }
            string[] Snapshot(string source)
            {
                var (model, root) = Bind(source);
                return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Select(method => Fingerprint(model.GetOperation(method)!)).ToArray();
            }
            Assert.That(Snapshot(after), Is.EqualTo(Snapshot(before)),
                "Operations, associations, conversions, calls and captured constants must remain identical");
        }

        private sealed class BinaryOwner(ValueExpression binary, ParameterProvider[] parameters,
            bool arrow, bool seed, bool subtraction = false, bool captured = false) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override PropertyProvider[] BuildProperties() => seed
                ? [new PropertyProvider(null, MethodSignatureModifiers.Public, typeof(DateTimeOffset), "Seed",
                    new ExpressionPropertyBody(Default), this)] : [];
            protected internal override MethodProvider[] BuildMethods()
            {
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public,
                    captured ? typeof(object) : subtraction ? typeof(int) : typeof(bool), null, parameters);
                if (!captured)
                {
                    return [arrow ? new MethodProvider(signature, binary, this) : new MethodProvider(signature, Return(binary), this)];
                }
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(binary));
                var capture = new InvokeMethodExpression(null, "Capture", [lambda]);
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: [new AttributeStatement(typeof(CallerArgumentExpressionAttribute), Literal("value"))]);
                var captureSignature = new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null,
                    [new ParameterProvider("value", $"", typeof(Func<int>)), text]);
                return [new MethodProvider(signature, capture, this),
                    new MethodProvider(captureSignature, (VariableExpression)text, this)];
            }
        }

        private sealed record OnWriteExpression(Action OnWrite, ValueExpression Inner) : ValueExpression
        {
            internal override void WriteCore(CodeWriter writer)
            {
                OnWrite();
                Inner.Write(writer);
            }
        }

        private sealed record MutableChildExpression(ValueExpression Inner) : ValueExpression
        {
            private ValueExpression _inner = Inner;

            public void Update(ValueExpression inner)
            {
                TouchSource();
                _inner = inner;
            }

            internal override void WriteCore(CodeWriter writer) => _inner.Write(writer);
        }
    }
}

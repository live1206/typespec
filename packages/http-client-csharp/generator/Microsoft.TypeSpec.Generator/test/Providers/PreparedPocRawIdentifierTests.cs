// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocRawIdentifierTests
    {
        [TestCase("SafeMethod", true)]
        [TestCase("nameof", true)]
        [TestCase("method\npublic class Escaped {}", false)]
        [TestCase("class", false)]
        [TestCase("await", false)]
        public void PocCallTokenObservationChecksRawMethodName(string name, bool expected)
        {
            var call = new InvokeMethodExpression(null, name, Array.Empty<ValueExpression>());
            var shared = StructuredSourceShape.Capture(call, disableXmlDocs: false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(call, disableXmlDocs: false);
            Assert.That(shared.IntrinsicTokens, Is.False, "The existing shared graph must remain unchanged.");
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False, "An identifier check does not prove layout or permission.");
        }

        [TestCase("SafeMethod", true)]
        [TestCase("Method\npublic class Escaped {}", false)]
        [TestCase("class", false)]
        [TestCase("await", false)]
        public void PocMethodSignatureObservationChecksItsRawWrittenName(string name, bool expected)
        {
            var signature = new MethodSignature(name, null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>());
            var poc = StructuredSourceShape.CaptureForPocSupplement(signature, false);
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False, "A raw-name check is not a grammar or layout proof.");
        }

        [Test]
        public void PocMethodSignatureObservationRejectsRawCommentEscape()
        {
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>(),
                NonDocumentComment: "comment\npublic class Escaped {}");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(signature, false).IntrinsicTokens, Is.False);
        }

        [TestCase("hello", true)]
        [TestCase("hello {0} end", true)]
        [TestCase("Cannot convert \\\"{0}\\\" to a char", true)]
        [TestCase("safe\\\\{0}", true)]
        [TestCase("dangling\\{0}", false)]
        [TestCase(@"x\\""; public class Escaped", false)]
        [TestCase("x\"; public class Escaped {}", false)]
        [TestCase("x\npublic class Escaped {}", false)]
        [TestCase("x\\\"; public class Escaped {}", false)]
        [TestCase("{{", false)]
        public void PocInterpolatedLiteralObservationOnlyCertifiesSafeRawParts(string format, bool expected)
        {
            var args = format.Contains("{0}", StringComparison.Ordinal)
                ? new ValueExpression[] { new KeywordExpression("this", null) }
                : Array.Empty<ValueExpression>();
            // Braces in hostile cases can also be interpreted as holes by the
            // native formatter; supply one argument for the trailing {}.
            if (format.EndsWith("{}", StringComparison.Ordinal)) { args = [new KeywordExpression("this", null)]; }
            var expression = new FormattableStringExpression(format, args);
            Assert.That(StructuredSourceShape.Capture(expression, false).IntrinsicTokens, Is.False,
                "The shared Task03 source graph stays unchanged.");
            var poc = StructuredSourceShape.CaptureForPocSupplement(expression, false);
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False, "Literal safety is not a layout or child-grammar proof.");
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public void PocInterpolatedLiteralObservationChecksEscapeParityAcrossHoles(int slashCount, bool expected)
        {
            var escapedQuote = new string('\\', slashCount) + "\"";
            foreach (var format in new[] { escapedQuote + "{0}", "{0}" + escapedQuote })
            {
                var expression = new FormattableStringExpression(format, new ValueExpression[] { new LiteralExpression(1) });
                Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false).IntrinsicTokens, Is.EqualTo(expected));
                if (expected)
                {
                    using var writer = new CodeWriter();
                    expression.Write(writer);
                    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText($"class C {{ string M() => {writer.ToString(header: false)}; }}");
                    Assert.That(tree.GetDiagnostics(), Is.Empty, "Test-only syntax validation of the native writer.");
                }
            }
        }

        [TestCase("value {999}", "$\"value {7}\"")]
        [TestCase("{1} {0} {1}", "$\"{7} {8} {9}\"")]
        [TestCase("tail {", "$\"tail \"")]
        public void PocInterpolatedLiteralObservationMatchesNativeSequentialHoles(string format, string expectedOutput)
        {
            var count = format == "tail {" ? 0 : format == "value {999}" ? 1 : 3;
            var args = Enumerable.Range(7, count).Select(value => (ValueExpression)new LiteralExpression(value)).ToArray();
            var expression = new FormattableStringExpression(format, args);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false).IntrinsicTokens, Is.True,
                "Only raw emitted parts and count are checked; numeric indices are not interpreted.");
            using var writer = new CodeWriter();
            expression.Write(writer);
            Assert.That(writer.ToString(header: false), Is.EqualTo(expectedOutput), "The native writer consumes hole arguments sequentially.");
        }

        [TestCase("{}")]
        [TestCase("{abc}")]
        [TestCase("{0:X}")]
        [TestCase("\\\"{{")]
        public void PocInterpolatedLiteralObservationVetoesUnprovedHoleAndBraceForms(string format)
        {
            var args = format == "\\\"{{" ? Array.Empty<ValueExpression>() : new ValueExpression[] { new LiteralExpression(1) };
            var expression = new FormattableStringExpression(format, args);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false).IntrinsicTokens, Is.False);
        }

        [Test]
        public void PocInterpolatedLiteralObservationRespectsExactBudget()
        {
            var expression = new FormattableStringExpression("{0}", new ValueExpression[] { new LiteralExpression(1) });
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false, new PocWorkBudget(7)).IntrinsicTokens, Is.True);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false, new PocWorkBudget(6)).Opaque, Is.True);
        }

        private sealed record UnknownInterpolatedChild : ValueExpression
        {
            internal int Calls { get; private set; }
            internal override void WriteCore(CodeWriter writer) { Calls++; writer.AppendRaw("safe"); }
        }

        [Test]
        public void PocInterpolatedSubtreeDoesNotReplayUnknownChild()
        {
            var child = new UnknownInterpolatedChild();
            var expression = new FormattableStringExpression("{0}", new ValueExpression[] { child });
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
            Assert.That(child.Calls, Is.Zero);
            using var writer = new CodeWriter();
            expression.Write(writer);
            Assert.That(writer.ToString(header: false), Is.EqualTo("$\"{safe}\""));
            Assert.That(child.Calls, Is.EqualTo(1), "Only the native write executes the child.");
        }

        [Test]
        public void PocInterpolatedLiteralObservationRejectsBudgetAndUnownedArguments()
        {
            var longFormat = new FormattableStringExpression(new string('a', 5000), Array.Empty<ValueExpression>());
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(longFormat, false).IntrinsicTokens, Is.False);

            var unknown = new FormattableStringExpression("safe", Array.Empty<ValueExpression>());
            typeof(FormattableStringExpression).GetProperty("Args", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(unknown, new UnknownExpressions());
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(unknown, false).Opaque, Is.True,
                "POC observation must not query an unknown argument carrier.");
        }

        [TestCase("hello")]
        [TestCase("hello {0} end")]
        [TestCase("Cannot convert \\\"{0}\\\" to a char")]
        public void PocSimpleInterpolatedLiteralMatchesNativeWriterSyntax(string format)
        {
            var args = format.Contains("{0}", StringComparison.Ordinal)
                ? new ValueExpression[] { new KeywordExpression("this", null) }
                : Array.Empty<ValueExpression>();
            var expression = new FormattableStringExpression(format, args);
            using var writer = new CodeWriter();
            expression.Write(writer);
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText($"class C {{ string M() => {writer.ToString(header: false)}; }}");
            Assert.That(tree.GetDiagnostics(), Is.Empty, "Test-only syntax validation of native writing, not a planning input.");
        }

        [TestCase("DEBUG", true)]
        [TestCase("NET8_0_OR_GREATER", true)]
        [TestCase("true", true)]
        [TestCase("class", false)]
        [TestCase("DEBUG || OTHER", false)]
        [TestCase("DEBUG\n#endif\npublic class Escaped {}", false)]
        public void PocPreprocessorConditionObservationKeepsDirectiveRetention(string condition, bool expected)
        {
            var statement = new IfElsePreprocessorStatement(condition, new ExpressionStatement(new KeywordExpression("break", null)));
            var shared = StructuredSourceShape.Capture(statement, false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(statement, false);
            Assert.That(shared.IntrinsicTokens, Is.False, "Shared Task03 facts stay unchanged.");
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.HasDirectives, Is.True, "A safe condition does not remove POC directive retention.");
        }

        [Test]
        public void PocGenerationRawInventoryBoundsProviderCountAndLeavesMissingSourcesUncovered()
        {
            var provider = TestTypeProvider.Empty;
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance);
            var large = Enumerable.Repeat(provider, 4097).ToImmutableArray();
            var bounded = PreparedPocGenerationRawInventory.Capture(large, sources);
            Assert.That(bounded.WrittenFileCount, Is.EqualTo(4097));
            Assert.That(bounded.GetFile(provider), Is.Null);
            Assert.That(bounded.HasUncoveredEmission, Is.True);

            var missing = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(provider), sources);
            Assert.That(missing.GetFile(provider), Is.Null);
            Assert.That(missing.HasUncoveredEmission, Is.True);

            var unknown = PreparedPocGenerationRawInventory.Capture(default, sources);
            Assert.That(unknown.WrittenFileCount, Is.Zero);
            Assert.That(unknown.GetFile(provider), Is.Null);
            Assert.That(unknown.HasUncoveredEmission, Is.True);
        }

        [Test]
        public void PocGenerationRawInventoryDoesNotInvokeUnknownSourceMap()
        {
            var provider = TestTypeProvider.Empty;
            var inventory = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(provider), new UnknownSourceFiles());
            Assert.That(inventory.GetFile(provider), Is.Null);
            Assert.That(inventory.HasUncoveredEmission, Is.True);
        }

        [Test]
        public void PocGenerationRawInventoryDoesNotInvokeUnknownComparer()
        {
            var provider = TestTypeProvider.Empty;
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(new UnknownProviderComparer());
            var inventory = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(provider), sources);
            Assert.That(inventory.GetFile(provider), Is.Null);
            Assert.That(inventory.HasUncoveredEmission, Is.True);
        }

        private sealed class UnknownProviderComparer : IEqualityComparer<TypeProvider>
        {
            public bool Equals(TypeProvider? left, TypeProvider? right) => throw new InvalidOperationException("Unknown comparer must not execute.");
            public int GetHashCode(TypeProvider value) => throw new InvalidOperationException("Unknown comparer must not execute.");
        }

        private sealed class UnknownSourceFiles : IReadOnlyDictionary<TypeProvider, PreparedSourceFile>
        {
            public int Count => throw new InvalidOperationException("Unknown source map must not execute.");
            public IEnumerable<TypeProvider> Keys => throw new InvalidOperationException("Unknown source map must not execute.");
            public IEnumerable<PreparedSourceFile> Values => throw new InvalidOperationException("Unknown source map must not execute.");
            public PreparedSourceFile this[TypeProvider key] => throw new InvalidOperationException("Unknown source map must not execute.");
            public bool ContainsKey(TypeProvider key) => throw new InvalidOperationException("Unknown source map must not execute.");
            public bool TryGetValue(TypeProvider key, out PreparedSourceFile value) => throw new InvalidOperationException("Unknown source map must not execute.");
            public IEnumerator<KeyValuePair<TypeProvider, PreparedSourceFile>> GetEnumerator() => throw new InvalidOperationException("Unknown source map must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("+", true)]
        [TestCase("==", true)]
        [TestCase("??=", true)]
        [TestCase("is", true)]
        [TestCase("/*", false)]
        [TestCase("//", false)]
        [TestCase(";", false)]
        [TestCase("+\n} public class Escaped {} /*", false)]
        public void PocBinaryOperatorObservationChecksOnlyRawOperator(string? op, bool expected)
        {
            var binary = new BinaryOperatorExpression(op!, new KeywordExpression("this", null), new LiteralExpression(1));
            Assert.That(StructuredSourceShape.Capture(binary, false).IntrinsicTokens, Is.False, "Shared Task03 facts remain unchanged.");
            var poc = StructuredSourceShape.CaptureForPocSupplement(binary, false);
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False, "Operator text is not an operand or grammar proof.");
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("!", true)]
        [TestCase("++", true)]
        [TestCase("--", true)]
        [TestCase("/*", false)]
        [TestCase(";", false)]
        [TestCase("!\n} public class Escaped {} /*", false)]
        public void PocUnaryOperatorObservationChecksOnlyRawOperator(string? op, bool expected)
        {
            var unary = new UnaryOperatorExpression(op!, new KeywordExpression("this", null), OperandOnTheLeft: false);
            Assert.That(StructuredSourceShape.Capture(unary, false).IntrinsicTokens, Is.False, "Shared Task03 facts remain unchanged.");
            var poc = StructuredSourceShape.CaptureForPocSupplement(unary, false);
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False, "Operator text is not an operand or grammar proof.");
        }

        [TestCase("u8", true, true)]
        [TestCase("u8", false, false)]
        [TestCase(".. ", false, true)]
        [TestCase(".. ", true, false)]
        public void PocUnaryOperatorObservationRespectsDirectionForSpecialTokens(string op, bool operandOnLeft, bool expected)
        {
            var unary = new UnaryOperatorExpression(op, new LiteralExpression(1), operandOnLeft);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(unary, false).IntrinsicTokens, Is.EqualTo(expected));
        }

        [Test]
        public void PocOperatorObservationRespectsExactWorkBoundary()
        {
            var binary = new BinaryOperatorExpression("+", new LiteralExpression(1), new LiteralExpression(2));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(binary, false, new PocWorkBudget(4)).IntrinsicTokens, Is.True);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(binary, false, new PocWorkBudget(3)).Opaque, Is.True);
        }

        [Test]
        public void PocOperatorObservationBoundsRawText()
        {
            var binary = new BinaryOperatorExpression(new string('+', 5000), new LiteralExpression(1), new LiteralExpression(2));
            var shape = StructuredSourceShape.CaptureForPocSupplement(binary, false);
            Assert.That(shape.IntrinsicTokens, Is.False);
            Assert.That(shape.Opaque, Is.True);
        }

        [TestCase("safe literal", true)]
        [TestCase("raw\"; public class Escaped //", false)]
        public void PocSupplementalInterpolatedObservationChecksRawText(string format, bool expected)
        {
            var expression = new FormattableStringExpression(format, Array.Empty<ValueExpression>());
            var node = PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.InterpolatedRawTextIsSafe, Is.EqualTo(expected));
        }

        [TestCase("DEBUG", true)]
        [TestCase("DEBUG\n#endif\npublic class Escaped {}", false)]
        public void PocSupplementalDirectiveObservationRetainsDirective(string condition, bool expected)
        {
            var statement = new IfElsePreprocessorStatement(condition, new ExpressionStatement(new KeywordExpression("break", null)));
            var node = PreparedPocSupplementalFile.Node.TryCapture(statement, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.DirectiveConditionsAreSafe, Is.EqualTo(expected));
            Assert.That(node.ContainsDirectives, Is.True, "A known condition must not erase the POC directive-retention fact.");
        }

        [TestCase("return", true)]
        [TestCase("value", true)]
        [TestCase("yield", true)]
        [TestCase("return\n} public class Escaped {} /*", false)]
        public void PocSupplementalKeywordObservationChecksRawFragment(string keyword, bool expected)
        {
            var expression = new KeywordExpression(keyword, new LiteralExpression(1));
            if (keyword is "value" or "yield")
            { Assert.That(StructuredSourceShape.Capture(expression, false).IntrinsicTokens, Is.False, "Shared Task03 facts remain unchanged."); }
            var node = PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.KeywordRawTokensAreSafe, Is.EqualTo(expected));
        }

        [Test]
        public void PocKeywordObservationBoundsRawText()
        {
            var expression = new KeywordExpression(new string('a', 5000), null);
            var shape = StructuredSourceShape.CaptureForPocSupplement(expression, false);
            Assert.That(shape.Opaque, Is.True);
        }

        [Test]
        public void PocKeywordObservationRejectsNullAndRespectsExactBudget()
        {
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(new KeywordExpression(null!, null), false).Opaque, Is.True);
            var expression = new KeywordExpression("return", null);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false, new PocWorkBudget(8)).IntrinsicTokens, Is.True);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(expression, false, new PocWorkBudget(7)).Opaque, Is.True);
        }

        [TestCase("+", true)]
        [TestCase("+\n} public class Escaped {} /*", false)]
        public void PocSupplementalOperatorObservationChecksRawFragment(string op, bool expected)
        {
            var binary = new BinaryOperatorExpression(op, new LiteralExpression(1), new LiteralExpression(2));
            var node = PreparedPocSupplementalFile.Node.TryCapture(binary, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.OperatorRawTokensAreSafe, Is.EqualTo(expected));
        }

        [Test]
        public void PocRawOperatorControlMatchesNativeWriter()
        {
            const string injected = "+\n} public class Escaped {} /*";
            var binary = new BinaryOperatorExpression(injected, new KeywordExpression("this", null), new LiteralExpression(1));
            using var writer = new CodeWriter();
            binary.Write(writer);
            Assert.That(writer.ToString(header: false), Does.Contain(injected),
                "Test-only output confirms the raw operator writer branch.");
        }

        [TestCase("Member", true)]
        [TestCase("class", false)]
        [TestCase("await", false)]
        [TestCase("Member\n} public class Escaped {} /*", false)]
        public void PocFieldNameObservationRejectsUnsafeRawDeclarationName(string name, bool expected)
        {
            var owner = new TestTypeProvider("EnumOwner", TypeSignatureModifiers.Enum);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), name, owner);
            var shared = StructuredSourceShape.Capture(field, false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(field, false);
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            if (name == "class") { Assert.That(shared.IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged."); }
        }

        [Test]
        public void PocFieldObservationDoesNotMaterializeWriterDeclaration()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            var cache = typeof(FieldProvider).GetField("_declaration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.That(cache.GetValue(field), Is.Null);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(field, false).IntrinsicTokens, Is.True);
            Assert.That(cache.GetValue(field), Is.Null, "A POC-only observation must not materialize the declaration getter.");
            _ = field.Declaration; // Simulates the native writer's first materialization.
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(field, false).IntrinsicTokens, Is.True,
                "A canonical lazy declaration remains a supported raw-name observation.");
        }

        [Test]
        public void PocFieldDeclarationWitnessAllowsCanonicalLazyMaterializationAndDetectsRevisionRestore()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(4096));
            Assert.That(witness, Is.Not.Null);
            Assert.That(field.CachedDeclaration, Is.Null);
            Assert.That(witness!.IsCurrent(new PocWorkBudget(4096)), Is.True);
            var declaration = field.Declaration; // Simulates the native writer's lazy materialization.
            Assert.That(witness.IsCurrent(new PocWorkBudget(4096)), Is.True);
            declaration.Update(name: "unsafe\n} public class Escaped {} /*");
            declaration.Update(name: "safeField");
            Assert.That(witness.IsCurrent(new PocWorkBudget(4096)), Is.False);
            Assert.That(witness.IsCurrent(new PocWorkBudget(4096)), Is.False, "A revision mismatch is sticky.");
        }

        [Test]
        public void PocFieldDeclarationShortQueryDoesNotLatchLazyIdentity()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(4096));
            Assert.That(witness, Is.Not.Null);
            Assert.That(field.CachedDeclaration, Is.Null);
            var nativeDeclaration = field.Declaration;
            var identity = typeof(PreparedPocFieldDeclaration).GetField("_declaration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.That(identity.GetValue(witness), Is.Null);
            // Name comparisons and the first requested-name check fit, but the
            // final declaration check does not. No partially funded query may
            // publish an identity or make an otherwise supported witness stale.
            Assert.That(witness!.IsCurrentWithoutInvalidating(new PocWorkBudget(3 * field.Name.Length)), Is.False);
            Assert.That(identity.GetValue(witness), Is.Null);
            Assert.That(field.CachedDeclaration, Is.SameAs(nativeDeclaration));
            Assert.That(witness.IsCurrentWithoutInvalidating(new PocWorkBudget(4096)), Is.True);
        }

        [Test]
        public void PocFieldDeclarationWitnessBoundsNameWorkWithoutMaterialization()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            Assert.That(PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(8)), Is.Null);
            Assert.That(field.CachedDeclaration, Is.Null);
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(9));
            Assert.That(witness, Is.Not.Null);
            Assert.That(witness!.IsCurrent(new PocWorkBudget(17)), Is.False);
            Assert.That(witness.IsCurrent(new PocWorkBudget(4096)), Is.False, "An observed budget failure is a sticky POC veto.");
            Assert.That(field.CachedDeclaration, Is.Null);
        }

        [Test]
        public void PocFieldDeclarationWitnessRejectsMutatedLazyValueBeforeFirstObservation()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(4096));
            Assert.That(witness, Is.Not.Null);
            var declaration = field.Declaration;
            declaration.Update(name: "unsafe\n} public class Escaped {} /*");
            declaration.Update(name: "safeField");
            Assert.That(witness!.IsCurrent(new PocWorkBudget(4096)), Is.False,
                "A lazy declaration revised before its first observation is not the native zero-revision materialization.");
        }

        [Test]
        public void PocFieldDeclarationWitnessRequiresSeparateOwnerCurrentnessForLazyReplacement()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            void Set(string name, object value) => typeof(TypeProvider).GetField(name, flags)!.SetValue(owner, value);
            Set("_type", new CSharpType("ClassOwner", "Test", false, false, null, Array.Empty<CSharpType>(), true, false));
            Set("_fields", new[] { field });
            Set("_properties", Array.Empty<PropertyProvider>());
            Set("_methods", Array.Empty<MethodProvider>());
            Set("_constructors", Array.Empty<ConstructorProvider>());
            Set("_nestedTypes", Array.Empty<TypeProvider>());
            Set("_serializationProviders", Array.Empty<TypeProvider>());
            Set("_attributes", Array.Empty<MethodBodyStatement>());
            var ownerSnapshot = new PreparedSourceFile.OwnerSnapshot(owner);
            Assert.That(ownerSnapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.True);
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(4096));
            Assert.That(witness, Is.Not.Null);
            var first = field.Declaration;
            field.Update(name: "safeField");
            Assert.That(field.Declaration, Is.Not.SameAs(first));
            Assert.That(witness!.IsCurrent(new PocWorkBudget(4096)), Is.True,
                "A first observation alone cannot distinguish same-name, zero-revision replacement.");
            Assert.That(ownerSnapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False,
                "The enclosing owner revision supplies the separate ordinary-update veto.");
        }

        [Test]
        public void PocFieldDeclarationWitnessRejectsReplacementWithSameName()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            var original = field.Declaration;
            var witness = PreparedPocFieldDeclaration.TryCapture(field, new PocWorkBudget(4096));
            Assert.That(witness, Is.Not.Null);
            field.Update(name: "safeField");
            Assert.That(field.Declaration, Is.Not.SameAs(original));
            Assert.That(witness!.IsCurrent(new PocWorkBudget(4096)), Is.False);
        }

        [TestCase("safeField", true)]
        [TestCase("differentSafeField", false)]
        [TestCase("field\n;} public class Escaped {} /*", false)]
        public void PocFieldObservationChecksCachedWriterDeclaration(string declaredName, bool expected)
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var field = new FieldProvider(FieldModifiers.Public, typeof(int), "safeField", owner);
            field.Declaration.Update(name: declaredName);
            Assert.That(field.Name, Is.EqualTo("safeField"));
            Assert.That(StructuredSourceShape.Capture(field, false).IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged.");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(field, false).IntrinsicTokens, Is.EqualTo(expected));
        }

        [TestCase("safeParam", true)]
        [TestCase("differentSafeParam", false)]
        [TestCase("param\n) {} public class Escaped {} /*", false)]
        public void PocParameterObservationChecksActualWriterDeclaration(string emittedName, bool expected)
        {
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            ParameterProvider.GetVariableExpression(parameter).Update(name: emittedName);
            Assert.That(parameter.Name, Is.EqualTo("safeParam"));
            Assert.That(StructuredSourceShape.Capture(parameter, false).IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged.");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.EqualTo(expected));
        }

        [TestCase(typeof(byte[]))]
        [TestCase(typeof(Type[]))]
        [TestCase(typeof(int[,]))]
        public void PocParameterObservationAcceptsExactRuntimeArrayToken(Type runtimeType)
        {
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), new CSharpType(runtimeType));
            Assert.That(StructuredSourceShape.Capture(parameter, false).IntrinsicTokens, Is.False, "Shared Task03 facts remain unchanged.");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.True,
                "This only recognizes the bounded runtime array token; type binding and grammar remain separate.");
        }

        private sealed class ArrayTokenGeneric<T> { }

        [Test]
        public void PocParameterObservationAcceptsExactGenericParameterArrayToken()
        {
            var runtimeType = typeof(ArrayTokenGeneric<>).GetGenericArguments()[0].MakeArrayType();
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), new CSharpType(runtimeType));
            Assert.That(StructuredSourceShape.Capture(parameter, false).IntrinsicTokens, Is.False);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocArrayTokenObservationRejectsChangedCachedIdentity(bool changeNamespace)
        {
            var type = new CSharpType(typeof(byte[]));
            if (changeNamespace) { type.Update(@namespace: "Different"); }
            else { type.Update(name: "Byte[]\n} public class Escaped {} /*"); }
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.False);
        }

        [Test]
        public void PocParameterObservationAcceptsExactRuntimeGenericParameterToken()
        {
            var runtime = typeof(ArrayTokenGeneric<>).GetGenericArguments()[0];
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), new CSharpType(runtime));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.True);
        }

        [Test]
        public void PocTypeTokenObservationRejectsGenericParameterRenamedAfterRuntimeIdentity()
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new System.Reflection.AssemblyName("PocUnsafeGeneric_" + Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var builder = assembly.DefineDynamicModule("M").DefineType("Sample.PocGeneric", System.Reflection.TypeAttributes.Public);
            builder.DefineGenericParameters("T\n} public class Escaped {} /*");
            var runtime = builder.CreateType()!.GetGenericArguments()[0];
            var type = new CSharpType(runtime);
            type.Update(name: "Safe");
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, false);
            using var writer = new CodeWriter();
            writer.WriteParameter(parameter);
            Assert.That(writer.ToString(header: false), Does.Contain("public class Escaped"), "The native writer uses the runtime generic parameter name.");
            Assert.That(shape.IntrinsicTokens, Is.False, "A safe cached name cannot mask a different runtime generic parameter name.");
        }

        [Test]
        public void PocArrayTokenObservationChargesNamespaceBeforeRuntimeComparison()
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new System.Reflection.AssemblyName("PocLongNamespace_" + Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var runtime = assembly.DefineDynamicModule("M").DefineType(new string('A', 500) + ".Element", System.Reflection.TypeAttributes.Public).CreateType()!.MakeArrayType();
            var type = new CSharpType(runtime);
            type.Update(@namespace: new string('A', 499) + "B");
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, false, new PocWorkBudget(256));
            Assert.That(shape.Opaque, Is.True, "A long cached namespace must exhaust the POC budget before any runtime identity comparison.");
        }

        [Test]
        public void PocArrayTokenObservationRejectsRealArrayRenamedToSimpleIdentifier()
        {
            var type = new CSharpType(typeof(byte[]));
            type.Update(name: "Safe");
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.False,
                "A simple cached spelling must not bypass exact runtime array identity.");
        }

        [Test]
        public void PocArrayTokenObservationRejectsMutableCustomRuntimeWithSimpleCachedName()
        {
            var runtime = new PocMutableSimpleArrayType();
            var type = new CSharpType(runtime);
            Assert.That(type.Name, Is.EqualTo("Safe"));
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            runtime.Escape = true;
            var before = runtime.CallbackCount;
            var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, false);
            Assert.That(runtime.CallbackCount, Is.EqualTo(before), "POC capture must not call a custom runtime Type.");
            using var writer = new CodeWriter();
            writer.WriteParameter(parameter);
            Assert.That(writer.ToString(header: false), Does.Contain("public class Escaped"), "The native writer observes the custom runtime spelling.");
            Assert.That(shape.IntrinsicTokens, Is.False, "A simple cached name does not make a custom runtime Type safe.");
        }

        private sealed class PocMutableSimpleArrayType() : System.Reflection.TypeDelegator(typeof(byte[]))
        {
            internal bool Escape { get; set; }
            internal int CallbackCount { get; private set; }
            public override string Name { get { CallbackCount++; return Escape ? "T\n} public class Escaped {} /*" : "Safe"; } }
            public override System.Reflection.Assembly Assembly { get { CallbackCount++; return typeof(CSharpType).Assembly; } }
            public override bool IsGenericParameter { get { CallbackCount++; return false; } }
            public override Type[] GetGenericArguments() { CallbackCount++; return Type.EmptyTypes; }
            protected override bool IsArrayImpl() { CallbackCount++; return true; }
        }

        [Test]
        public void PocArrayTokenObservationDoesNotInvokeUnknownRuntimeType()
        {
            var runtime = new PocArrayTypeDelegator();
            var type = new CSharpType(runtime);
            var before = runtime.CallbackCount;
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), type);
            var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, false);
            Assert.That(shape.IntrinsicTokens, Is.False);
            Assert.That(runtime.CallbackCount, Is.EqualTo(before), "POC array token checks must not call a custom runtime Type.");
        }

        private sealed class PocArrayTypeDelegator() : System.Reflection.TypeDelegator(typeof(byte[]))
        {
            internal int CallbackCount { get; private set; }
            public override string Name { get { CallbackCount++; return base.Name; } }
            protected override bool IsArrayImpl() { CallbackCount++; return base.IsArrayImpl(); }
        }

        [Test]
        public void PocParameterObservationAcceptsCanonicalContextualValueName()
        {
            var parameter = new ParameterProvider("value", FormattableStringFactory.Create(string.Empty), typeof(int));
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.Null);
        }

        [Test]
        public void PocParameterObservationVetoesMalformedNameWithoutChangingNativeWrite()
        {
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            _ = ParameterProvider.GetVariableExpression(parameter);
            parameter.Update(name: "!!!");
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => writer.WriteParameter(parameter));
            Assert.That(writer.ToString(header: false), Does.Contain("!!!"), "The native cached declaration still writes normally.");
            Assert.DoesNotThrow(() => Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).Opaque, Is.True));
            Assert.DoesNotThrow(() => Assert.That(StructuredSourceShape.CaptureForPocRecheck(parameter, false).Opaque, Is.True));
        }

        [Test]
        public void PocParameterObservationVetoesMissingCachedDeclarationWithoutExecutingIt()
        {
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            var variable = new VariableExpression(typeof(int), (CodeWriterDeclaration)null!);
            typeof(ParameterProvider).GetField("_asVariable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(parameter, variable);
            Assert.DoesNotThrow(() => Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).Opaque, Is.True));
            Assert.DoesNotThrow(() => Assert.That(StructuredSourceShape.CaptureForPocRecheck(parameter, false).Opaque, Is.True));
            using var writer = new CodeWriter();
            Assert.Throws<ArgumentNullException>(() => writer.WriteParameter(parameter), "The native exception remains unchanged.");
        }

        [Test]
        public void PocParameterObservationBoundsInputAndCachedDeclarationNames()
        {
            var longInput = new ParameterProvider(new string('a', 5000), FormattableStringFactory.Create(string.Empty), typeof(int));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(longInput, false).Opaque, Is.True);
            Assert.That(longInput.CachedVariableExpression, Is.Null);
            var longCached = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            ParameterProvider.GetVariableExpression(longCached).Update(name: new string('a', 5000));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(longCached, false).Opaque, Is.True);
        }

        [Test]
        public void PocParameterObservationDoesNotMaterializeWriterVariable()
        {
            var parameter = new ParameterProvider("SafeParameter", FormattableStringFactory.Create(string.Empty), typeof(int));
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(parameter, false).IntrinsicTokens, Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.Null, "The POC must not force the variable getter.");
        }

        [TestCase("this", true)]
        [TestCase("class", false)]
        [TestCase("indexer\n] {} public class Escaped {} /*", false)]
        public void PocIndexerObservationRequiresNativeIndexerName(string name, bool expected)
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            var property = new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), parameter, new AutoPropertyBody(false), owner) { Name = name };
            var shared = StructuredSourceShape.Capture(property, false);
            if (name == "class") { Assert.That(shared.IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged."); }
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(property, false).IntrinsicTokens, Is.EqualTo(expected));
        }

        [TestCase("safeParam", true)]
        [TestCase("differentSafeParam", false)]
        [TestCase("param\n] {} public class Escaped {} /*", false)]
        public void PocIndexerObservationChecksActualWriterParameter(string emittedName, bool expected)
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            ParameterProvider.GetVariableExpression(parameter).Update(name: emittedName);
            var property = new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), parameter, new AutoPropertyBody(false), owner);
            Assert.That(StructuredSourceShape.Capture(property, false).IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged.");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(property, false).IntrinsicTokens, Is.EqualTo(expected));
        }

        [Test]
        public void PocIndexerRecheckBoundsUncachedParameterNameWithoutMaterialization()
        {
            var owner = new TestTypeProvider("ClassOwner", TypeSignatureModifiers.Class);
            var parameter = new ParameterProvider(new string('a', 5000), FormattableStringFactory.Create(string.Empty), typeof(int));
            var property = new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), parameter, new AutoPropertyBody(false), owner);
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(property, false).Opaque, Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.Null);
        }

        [TestCase("safeParam", true)]
        [TestCase("class", false)]
        [TestCase("await", false)]
        [TestCase("param\n) {} public class Escaped {} /*", false)]
        public void PocLocalFunctionObservationChecksRawParameterName(string name, bool expected)
        {
            var parameter = new ParameterProvider(name, FormattableStringFactory.Create(string.Empty), typeof(int));
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new[] { parameter }, typeof(int), new LiteralExpression(1));
            var shared = StructuredSourceShape.Capture(local, false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(local, false);
            if (name == "class") { Assert.That(shared.IntrinsicTokens, Is.True, "Shared Task03 facts remain unchanged."); }
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
        }

        [TestCase("safeParam", true)]
        [TestCase("param\n) {} public class Escaped {} /*", false)]
        public void PocSupplementalLocalFunctionObservationChecksRawParameter(string name, bool expected)
        {
            var parameter = new ParameterProvider(name, FormattableStringFactory.Create(string.Empty), typeof(int));
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new[] { parameter }, typeof(int), new LiteralExpression(1));
            var node = PreparedPocSupplementalFile.Node.TryCapture(local, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.ParameterAndFieldRawNamesAreSafe, Is.EqualTo(expected));
        }

        [Test]
        public void PocLocalFunctionObservationIgnoresUnwrittenCachedParameterVariable()
        {
            var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
            ParameterProvider.GetVariableExpression(parameter).Update(name: "unsafe\n) {} public class Escaped {} /*");
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new[] { parameter }, typeof(int), new LiteralExpression(1));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(local, false).IntrinsicTokens, Is.True,
                "The local-function writer uses ParameterProvider.Name, not the cached method-parameter variable.");
        }

        [Test]
        public void PocLocalFunctionRawParameterControlMatchesNativeWriter()
        {
            const string injected = "param\n) {} public class Escaped {} /*";
            var parameter = new ParameterProvider(injected, FormattableStringFactory.Create(string.Empty), typeof(int));
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new[] { parameter }, typeof(int), new LiteralExpression(1));
            using var writer = new CodeWriter();
            local.Write(writer);
            Assert.That(writer.ToString(header: false), Does.Contain(injected),
                "Test-only output confirms the local-function writer uses the raw parameter name.");
        }

        [TestCase("Safe", true)]
        [TestCase("class", false)]
        [TestCase("await", false)]
        [TestCase("Name\n)] public class Escaped {} [Obsolete(", false)]
        public void PocAttributeNamedArgumentObservationChecksRawWrittenKey(string key, bool expected)
        {
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute),
                new[] { new KeyValuePair<string, ValueExpression>(key, new KeywordExpression("true", null)) });
            var shared = StructuredSourceShape.Capture(attribute, false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(attribute, false);
            Assert.That(shared.IntrinsicTokens, Is.True, "Shared Task03 facts must not change.");
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.Children, Has.Length.EqualTo(1), "The argument value remains a separate child fact.");
        }

        [Test]
        public void PocAttributeNamedArgumentObservationBoundsRawKeyLength()
        {
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute),
                new[] { new KeyValuePair<string, ValueExpression>(new string('a', 5000), new LiteralExpression(true)) });
            var poc = StructuredSourceShape.CaptureForPocSupplement(attribute, false);
            Assert.That(poc.IntrinsicTokens, Is.False);
            Assert.That(poc.Opaque, Is.True);
        }

        [TestCase("Safe", true)]
        [TestCase("Name\n)] public class Escaped {} [Obsolete(", false)]
        public void PocSupplementalAttributeObservationChecksRawKey(string key, bool expected)
        {
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute),
                new[] { new KeyValuePair<string, ValueExpression>(key, new LiteralExpression(true)) });
            var node = PreparedPocSupplementalFile.Node.TryCapture(attribute, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.AttributeRawKeysAreSafe, Is.EqualTo(expected));
        }

        [Test]
        public void PocAttributeRawKeyControlMatchesNativeWriter()
        {
            const string injected = "Name\n)] public class Escaped {} [Obsolete(";
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute),
                new[] { new KeyValuePair<string, ValueExpression>(injected, new KeywordExpression("true", null)) });
            using var writer = new CodeWriter();
            attribute.Write(writer);
            Assert.That(writer.ToString(header: false), Does.Contain(injected),
                "Test-only output confirms the native raw branch, not a planning input.");
        }

        [Test]
        public void PocCallTokenObservationUsesTheActuallyWrittenSignatureName()
        {
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, []);
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            call.Update(methodName: "ignored\npublic class Escaped {}");
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(call, false).IntrinsicTokens, Is.True,
                "The writer uses the signature name when it exists, not the unused MethodName.");
        }

        [Test]
        public void PocMemberTokenObservationUsesTheActuallyWrittenDeclaration()
        {
            var member = new MemberExpression(null, "ignored\npublic class Escaped {}")
            { Declaration = new CodeWriterDeclaration("SafeMember") };
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(member, false).IntrinsicTokens, Is.True,
                "The writer ignores MemberName when a declaration is present.");
        }

        [TestCase("SafeMethod", true)]
        [TestCase("method\npublic class Escaped {}", false)]
        public void PocSubtreeRecordsOnlyCallAndMemberRawNameFact(string name, bool expected)
        {
            var call = new InvokeMethodExpression(new MemberExpression(null, "Receiver"), name, Array.Empty<ValueExpression>());
            var node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.CallAndMemberRawNamesAreSafe, Is.EqualTo(expected));
        }

        [Test]
        public void PocSupplementRejectsNullSignatureNameWithoutChangingNativeFallback()
        {
            var signature = new MethodSignature(null!, null, MethodSignatureModifiers.Public, null, null, []);
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            call.Update(methodName: "SafeFallback");
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            Assert.That(writer.ToString(false), Does.Contain("SafeFallback()"));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null, "A malformed hidden signature must veto only the POC observation.");
        }

        [Test]
        public void PocSupplementRejectsNullDeclarationNameWithoutChangingNativeWrite()
        {
            var member = new MemberExpression(null, "ignored") { Declaration = new CodeWriterDeclaration(null!) };
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => member.Write(writer));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(member, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null, "A malformed hidden declaration must veto only the POC observation.");
        }

        [Test]
        public void PocSupplementRejectsNullSignatureParameterCollectionWithoutChangingNativeWrite()
        {
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, (IReadOnlyList<ParameterProvider>)null!);
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            Assert.That(writer.ToString(false), Does.Contain("SafeMethod()"));
            var node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Null, "A missing signature parameter collection must remain unavailable to the POC observation.");
        }

        [Test]
        public void PocSupplementRejectsUnknownSignatureParametersWithoutExecutingThem()
        {
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, new UnknownParameters());
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            Assert.That(writer.ToString(false), Does.Contain("SafeMethod()"));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null, "Unknown signature metadata cannot establish a POC observation.");
        }

        private sealed class UnknownParameters : IReadOnlyList<ParameterProvider>
        {
            public int Count => throw new InvalidOperationException("Unknown metadata must not execute.");
            public ParameterProvider this[int index] => throw new InvalidOperationException("Unknown metadata must not execute.");
            public IEnumerator<ParameterProvider> GetEnumerator() => throw new InvalidOperationException("Unknown metadata must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PocSupplementRejectsMissingSignatureParameterMetadata(bool missingName)
        {
            var parameter = new ParameterProvider(missingName ? null! : "p", FormattableStringFactory.Create(string.Empty),
                missingName ? (CSharpType)typeof(int) : null!);
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, new[] { parameter });
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            Assert.That(writer.ToString(false), Does.Contain("SafeMethod()"));
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
        }

        [Test]
        public void PocSupplementDefersNativeNullParameterNameException()
        {
            var parameter = new ParameterProvider(null!, FormattableStringFactory.Create(string.Empty), typeof(int));
            using var writer = new CodeWriter();
            Assert.Throws<NullReferenceException>(() => writer.WriteParameter(parameter));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(parameter, false, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementRejectsNullIndexerParameterWithoutChangingSharedCapture()
        {
            var indexer = new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), null!,
                new AutoPropertyBody(false), TestTypeProvider.Empty);
            Assert.Throws<NullReferenceException>(() => StructuredSourceShape.Capture(indexer, false),
                "The existing shared path is not changed by the POC-only guard.");
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(indexer, false, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementDefersNativeNullLocalParameterTypeException()
        {
            var parameter = new ParameterProvider("p", FormattableStringFactory.Create(string.Empty), null!);
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new[] { parameter },
                typeof(void), new KeywordExpression("default", null));
            using var writer = new CodeWriter();
            Assert.Throws<ArgumentNullException>(() => local.Write(writer));
            var node = PreparedPocSupplementalFile.Node.TryCapture(local, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementDefersNativeNullTypeArgumentException()
        {
            var call = new InvokeMethodExpression(null, "SafeMethod", Array.Empty<ValueExpression>()) { TypeArguments = new CSharpType[] { null! } };
            using var writer = new CodeWriter();
            Assert.Throws<ArgumentNullException>(() => call.Write(writer));
            var node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementRejectsUnknownSignatureAttributesWithoutExecutingThem()
        {
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>());
            signature.Attributes = new UnknownAttributes();
            var call = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        private sealed class UnknownAttributes : IReadOnlyList<AttributeStatement>
        {
            public int Count => throw new InvalidOperationException("Unknown metadata must not execute.");
            public AttributeStatement this[int index] => throw new InvalidOperationException("Unknown metadata must not execute.");
            public IEnumerator<AttributeStatement> GetEnumerator() => throw new InvalidOperationException("Unknown metadata must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void PocSupplementBoundsFlattenedStatementDepth()
        {
            MethodBodyStatement body = new ExpressionStatement(new KeywordExpression("default", null));
            for (int i = 0; i < 300; i++) { body = new MethodBodyStatements(new[] { body }); }
            var expression = new TryExpression(body);
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
        }

        [Test]
        public void PocSupplementAcceptsModerateStatementAndTypeDepth()
        {
            MethodBodyStatement body = new ExpressionStatement(new KeywordExpression("default", null));
            CSharpType type = typeof(string);
            for (int i = 0; i < 32; i++)
            {
                body = new MethodBodyStatements(new[] { body });
                type = new CSharpType("Safe", "Test", false, false, null, new[] { type }, true, false);
            }
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(new TryExpression(body), true,
                new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Not.Null);
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(new VariableExpression(type, new CodeWriterDeclaration("x")), true,
                new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Not.Null);
        }

        [Test]
        public void PocSupplementBoundsRepeatedChildStatementWork()
        {
            MethodBodyStatement body = new ExpressionStatement(new KeywordExpression("default", null));
            for (int i = 0; i < 13; i++) { body = new MethodBodyStatements(new[] { body, body }); }
            var expression = new TryExpression(body);
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
        }

        [Test]
        public void PocSupplementBoundsRepeatedChildTypeWork()
        {
            CSharpType type = typeof(string);
            for (int i = 0; i < 13; i++) { type = new CSharpType("Safe", "Test", false, false, null, new[] { type, type }, true, false); }
            var expression = new VariableExpression(type, new CodeWriterDeclaration("x"));
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
        }

        [Test]
        public void PocRecheckDoesNotAcceptOpaqueShapeWhosePriorTypeSnapshotExceedsBudget()
        {
            CSharpType type = typeof(string);
            for (int i = 0; i < 300; i++) { type = new CSharpType("Safe", "Test", false, false, null, new[] { type }, true, false); }
            var call = new InvokeMethodExpression(null, "SafeMethod", new UnknownExpressions()) { ExtensionType = type };
            var shared = StructuredSourceShape.Capture(call, false);
            var poc = StructuredSourceShape.CaptureForPocRecheck(call, false);
            Assert.That(shared.Opaque, Is.True);
            Assert.That(poc.Opaque, Is.True);
            Assert.That(shared.UnavailableReason, Is.EqualTo(poc.UnavailableReason));
            Assert.That(shared.Matches(poc), Is.True, "The existing shared comparison retains its prior behavior.");
            Assert.That(shared.MatchesForPoc(poc, new PocWorkBudget(4096)), Is.False,
                "A bounded POC recheck must not validate through the old deep snapshot.");
        }

        private sealed class UnknownExpressions : IReadOnlyList<ValueExpression>
        {
            public int Count => throw new InvalidOperationException("Unknown arguments must not execute.");
            public ValueExpression this[int index] => throw new InvalidOperationException("Unknown arguments must not execute.");
            public IEnumerator<ValueExpression> GetEnumerator() => throw new InvalidOperationException("Unknown arguments must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void PocRecheckRejectsPreviouslyIncompleteTypeSnapshotAfterRestoration()
        {
            var arguments = new CSharpType[] { null! };
            var type = new CSharpType("Safe", "Test", false, false, null, arguments, true, false);
            var call = new InvokeMethodExpression(null, "SafeMethod", new UnknownExpressions()) { ExtensionType = type };
            var original = StructuredSourceShape.Capture(call, false);
            Assert.That(original.Opaque, Is.True);
            arguments[0] = typeof(string);
            var current = StructuredSourceShape.CaptureForPocRecheck(call, false);
            Assert.That(current.UnavailableReason, Is.EqualTo(original.UnavailableReason));
            Assert.That(original.MatchesForPoc(current, new PocWorkBudget(4096)), Is.False,
                "A missing original type snapshot cannot certify a later repaired child.");
        }

        [Test]
        public void PocRecheckRejectsPreviouslyIncompleteAttributeMetadataAfterRestoration()
        {
            var arguments = new ValueExpression[] { new KeywordExpression("notALiteral", null) };
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute), arguments);
            var signature = new MethodSignature("SafeMethod", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>());
            signature.Attributes = new[] { attribute };
            var call = new InvokeMethodExpression(null, signature, new UnknownExpressions());
            var original = StructuredSourceShape.Capture(call, false);
            Assert.That(original.Opaque, Is.True);
            arguments[0] = new LiteralExpression("safe");
            var current = StructuredSourceShape.CaptureForPocRecheck(call, false);
            Assert.That(current.UnavailableReason, Is.EqualTo(original.UnavailableReason));
            Assert.That(original.MatchesForPoc(current, new PocWorkBudget(4096)), Is.False,
                "An omitted original attribute snapshot cannot certify later metadata.");
        }

        [Test]
        public void PocShapeBudgetExhaustionIsAlwaysOpaque()
        {
            var type = new CSharpType("Safe", "Test", false, false, null, new[] { (CSharpType)typeof(string) }, true, false);
            var budget = new PocWorkBudget(4);
            var shape = StructuredSourceShape.CaptureForPocSupplement(new VariableExpression(type, new CodeWriterDeclaration("x")), false, budget);
            Assert.That(budget.Exhausted, Is.True);
            Assert.That(shape.Opaque, Is.True, "Exhausted token/type work must veto the supplemental shape.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocShapeBoundsRawIdentifierAndNamespaceCharacters(bool namespaceCase)
        {
            var name = new string('a', 10000);
            ValueExpression expression = namespaceCase
                ? new VariableExpression(new CSharpType("Safe", name, false, false, null, Array.Empty<CSharpType>(), true, false), new CodeWriterDeclaration("x"))
                : new MemberExpression(null, name);
            var budget = new PocWorkBudget(32);
            var shape = StructuredSourceShape.CaptureForPocSupplement(expression, false, budget);
            Assert.That(budget.Exhausted, Is.True);
            Assert.That(shape.Opaque, Is.True);
        }

        [Test]
        public void PocOwnerRecheckRejectsPreexistingDeepTypeSnapshotWithoutChangingSharedState()
        {
            CSharpType type = typeof(string);
            for (int i = 0; i < 300; i++) { type = new CSharpType("Safe", "Test", false, false, null, new[] { type }, true, false); }
            var owner = new TestTypeProvider();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(TypeProvider).GetField("_type", flags)!.SetValue(owner, type);
            typeof(TypeProvider).GetField("_fields", flags)!.SetValue(owner, Array.Empty<FieldProvider>());
            typeof(TypeProvider).GetField("_properties", flags)!.SetValue(owner, Array.Empty<PropertyProvider>());
            typeof(TypeProvider).GetField("_methods", flags)!.SetValue(owner, Array.Empty<MethodProvider>());
            typeof(TypeProvider).GetField("_constructors", flags)!.SetValue(owner, Array.Empty<ConstructorProvider>());
            typeof(TypeProvider).GetField("_nestedTypes", flags)!.SetValue(owner, Array.Empty<TypeProvider>());
            typeof(TypeProvider).GetField("_serializationProviders", flags)!.SetValue(owner, Array.Empty<TypeProvider>());
            typeof(TypeProvider).GetField("_attributes", flags)!.SetValue(owner, Array.Empty<MethodBodyStatement>());
            var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
            Assert.That(snapshot.IsCurrent(), Is.True, "The existing shared snapshot remains unchanged.");
            Assert.That(snapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False, "An additional POC recheck must not traverse an unbounded old snapshot.");
            Assert.That(snapshot.IsCurrent(), Is.True, "The POC-only veto must not mutate shared owner state.");
        }

        [Test]
        public void PocSupplementBoundsTypeSnapshotDepth()
        {
            CSharpType type = typeof(string);
            for (int i = 0; i < 300; i++) { type = new CSharpType("Safe", "Test", false, false, null, new[] { type }, true, false); }
            var expression = new VariableExpression(type, new CodeWriterDeclaration("x"));
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => expression.Write(writer));
            Assert.That(PreparedPocSupplementalFile.Node.TryCapture(expression, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0), Is.Null);
        }

        [Test]
        public void PocSupplementRejectsNullMethodProviderSignatureWithoutChangingNativeWrite()
        {
            var call = new InvokeMethodExpression(null, new NullSignatureMethodProvider(), Array.Empty<ValueExpression>());
            call.Update(methodName: "SafeFallback");
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => call.Write(writer));
            Assert.That(writer.ToString(false), Does.Contain("SafeFallback()"));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementRejectsNullConstructorProviderSignatureWithoutChangingNativeWrite()
        {
            var creation = new NewInstanceExpression(typeof(object), Array.Empty<ValueExpression>())
            { ConstructorProviderIntent = new NullSignatureConstructorProvider() };
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => creation.Write(writer));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(creation, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementRejectsNullInitializerProviderSignatureWithoutChangingNativeWrite()
        {
            var initializer = new ConstructorInitializer(false, new NullSignatureConstructorProvider(), Array.Empty<ValueExpression>());
            var signature = new ConstructorSignature(typeof(object), null, MethodSignatureModifiers.Public, Array.Empty<ParameterProvider>(), initializer: initializer);
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => { using var scope = writer.WriteMethodDeclarationNoScope(signature); });
            Assert.That(writer.ToString(false), Does.Contain(": this()"));
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(initializer, false, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null);
        }

        [Test]
        public void PocSupplementDefersNativeNullLocalParameterException()
        {
            var local = new DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"), new ParameterProvider[] { null! },
                typeof(void), new KeywordExpression("default", null));
            using var writer = new CodeWriter();
            Assert.Throws<NullReferenceException>(() => local.Write(writer), "The native writer already rejects the malformed parameter.");
            PreparedPocSupplementalFile.Node? node = null;
            Assert.DoesNotThrow(() => node = PreparedPocSupplementalFile.Node.TryCapture(local, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0));
            Assert.That(node, Is.Null, "POC planning must defer the existing native exception rather than throw early.");
        }

        private sealed class NullSignatureMethodProvider : MethodProvider { }
        private sealed class NullSignatureConstructorProvider : ConstructorProvider { }

        [Test]
        public void PocDeclarationBackedMemberAllowsWriterEscapedKeyword()
        {
            var member = new MemberExpression(null, "ignored") { Declaration = new CodeWriterDeclaration("class") };
            using var writer = new CodeWriter();
            member.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("@class"));
            Assert.That(StructuredSourceShape.CaptureForPocSupplement(member, false).IntrinsicTokens, Is.True,
                "Declaration-backed names use the writer's identifier escaping, not the raw-name branch.");
        }

        [TestCase("SafeMember", true)]
        [TestCase("member\npublic class Escaped {}", false)]
        [TestCase("class", false)]
        [TestCase("record", false)]
        public void PocMemberTokenObservationChecksRawMemberName(string name, bool expected)
        {
            var member = new MemberExpression(null, name);
            var shared = StructuredSourceShape.Capture(member, disableXmlDocs: false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(member, disableXmlDocs: false);
            Assert.That(shared.IntrinsicTokens, Is.False, "The existing shared graph must remain unchanged.");
            Assert.That(poc.IntrinsicTokens, Is.EqualTo(expected));
            Assert.That(poc.IntrinsicLayoutKnown, Is.False);
        }
    }
}

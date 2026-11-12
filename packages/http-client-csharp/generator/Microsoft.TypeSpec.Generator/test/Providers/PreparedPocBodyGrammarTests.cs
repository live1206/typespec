// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocBodyGrammarTests
    {
        private static PreparedPocBodyGrammar? Capture(MethodBodyStatement body) =>
            PreparedPocBodyGrammar.TryCaptureBody(
                body, disableXmlDocs: true, new PocWorkBudget(65536), out _);

        [Test]
        public void UsingDeclarationHasAClosedBodyShape()
        {
            var variable = new VariableExpression(new CSharpType(typeof(string)), "message");
            var declaration = new DeclarationExpression(variable, IsUsing: true);
            var call = new InvokeMethodExpression(
                new KeywordExpression("this", null), "CreateRequest", []);
            var statements = new MethodBodyStatements(
            [
                new ExpressionStatement(new AssignmentExpression(declaration, call)),
                new ExpressionStatement(new KeywordExpression("return", variable))
            ]);

            var grammar = Capture(statements);
            Assert.That(grammar, Is.Not.Null);
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.True);

            variable.Update(name: "changed");
            Assert.That(grammar.IsCurrent(new PocWorkBudget(65536)), Is.False);
        }

        [Test]
        public void UnderfundedGrammarQueryDoesNotPoisonFundedRetry()
        {
            var variable = new VariableExpression(new CSharpType(typeof(string)), "message");
            var declaration = new DeclarationExpression(variable, IsUsing: true);
            var call = new InvokeMethodExpression(new KeywordExpression("this", null), "CreateRequest", []);
            var statements = new MethodBodyStatements([new ExpressionStatement(new AssignmentExpression(declaration, call))]);
            var grammar = Capture(statements);
            Assert.That(grammar, Is.Not.Null);
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.True);
            var insufficient = new PocWorkBudget(0);
            Assert.That(grammar.IsCurrent(insufficient), Is.False);
            Assert.That(insufficient.Exhausted, Is.True);
            Assert.That(grammar.IsCurrent(new PocWorkBudget(65536)), Is.True,
                "A fresh funded grammar query must not inherit another query's exhausted budget.");
        }

        [Test]
        public void ObservedGrammarMismatchRemainsStaleAfterRestoration()
        {
            var variable = new VariableExpression(new CSharpType(typeof(string)), "message");
            var declaration = new DeclarationExpression(variable, IsUsing: true);
            var call = new InvokeMethodExpression(new KeywordExpression("this", null), "CreateRequest", []);
            var grammar = Capture(new MethodBodyStatements([new ExpressionStatement(new AssignmentExpression(declaration, call))]));
            Assert.That(grammar, Is.Not.Null);
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.True);
            variable.Update(name: "changed");
            var funded = new PocWorkBudget(65536);
            Assert.That(grammar.IsCurrent(funded), Is.False);
            Assert.That(funded.Exhausted, Is.False);
            variable.Update(name: "message");
            Assert.That(grammar.IsCurrent(new PocWorkBudget(65536)), Is.False);
        }

        [Test]
        public void SignatureDrivenInvocationUsesTheNativeSelectedName()
        {
            var signature = new MethodSignature("Actual", null, MethodSignatureModifiers.None,
                new CSharpType(typeof(int)), null, []);
            var call = new InvokeMethodExpression(null, signature, []);
            call.Update(methodName: "unused fallback text");
            var grammar = Capture(new ExpressionStatement(call));
            Assert.That(grammar, Is.Not.Null);
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.True);
            signature.Update(name: "Changed");
            Assert.That(grammar.IsCurrent(new PocWorkBudget(65536)), Is.False);
        }

        [Test]
        public void OrdinaryInvocationArgumentsHaveValueGrammar()
        {
            var argument = new VariableExpression(new CSharpType(typeof(int)), "value");
            var grammar = Capture(new ExpressionStatement(new InvokeMethodExpression(null, "Call", [argument])));
            Assert.That(grammar?.IsCurrent(new PocWorkBudget(65536)), Is.True);
            argument.Update(name: "changed");
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.False);
        }

        [Test]
        public void GrownStatementCarrierVetoesBeforeUnboundedCopy()
        {
            var items = new List<MethodBodyStatement> { MethodBodyStatement.Empty };
            var grammar = Capture(new MethodBodyStatements(items));
            Assert.That(grammar, Is.Not.Null);

            for (int i = 0; i < 100000; i++) { items.Add(MethodBodyStatement.Empty); }
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.That(grammar!.IsCurrent(new PocWorkBudget(128)), Is.False);
            Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(65536));
        }

        [Test]
        public void ConflictingArgumentModifiersAreRejected()
        {
            var variable = new VariableExpression(new CSharpType(typeof(int)), "value");
            var argument = new ArgumentExpression(variable, IsRef: true, IsOut: true);
            var call = new InvokeMethodExpression(null, "Call", [argument]);
            Assert.That(Capture(new ExpressionStatement(call)), Is.Null);
        }

        [Test]
        public void LiteralAndStaticPlaceholderAreNotStandaloneStatements()
        {
            Assert.That(Capture(new ExpressionStatement(new LiteralExpression(1))), Is.Null);
            Assert.That(Capture(new ExpressionStatement(ValueExpression.Empty)), Is.Null);
        }

        [Test]
        public void UnbracedIfAndSingleElementTupleAreRejected()
        {
            var conditional = new IfStatement(new LiteralExpression(true), addBraces: false);
            conditional.Add(MethodBodyStatement.Empty);
            Assert.That(Capture(conditional), Is.Null);

            var oneElement = new TupleExpression(new LiteralExpression(1));
            Assert.That(Capture(new ExpressionStatement(
                new KeywordExpression("return", oneElement))), Is.Null);
        }

        [Test]
        public void UnsupportedRawAndInitializerBoundariesAreRejected()
        {
            var initializer = new ArrayInitializerExpression([new LiteralExpression(1)]);
            Assert.That(Capture(new ExpressionStatement(
                new KeywordExpression("return", initializer))), Is.Null);
        }
    }
}

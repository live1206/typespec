// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocAttributeEnvelopeTests
    {
        [Test]
        public void CanonicalCarrierIsPreservedAndArgumentReplacementVetoesCurrentness()
        {
            var original = new LiteralExpression("captured");
            var arguments = new List<ValueExpression> { original };
            var attribute = new AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), arguments, []);
            MethodBodyStatement[] carrier = [attribute];
            var envelope = PreparedPocAttributeEnvelope.TryCapture(carrier, new PocWorkBudget(4096));
            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope!.Carrier, Is.SameAs(carrier));
            Assert.That(envelope.Count, Is.EqualTo(1));
            Assert.That(envelope.IsCurrent(new PocWorkBudget(4096)), Is.True);
            arguments[0] = new LiteralExpression("changed");
            Assert.That(envelope.IsCurrent(new PocWorkBudget(4096)), Is.False);
            arguments[0] = original;
            Assert.That(envelope.IsCurrent(new PocWorkBudget(4096)), Is.False, "A veto is sticky.");
        }

        [Test]
        public void CanonicalAttributeCarrierGrowthIsBoundedBeforeCopy()
        {
            var attribute = new AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], []);
            var carrier = new List<MethodBodyStatement> { attribute };
            var envelope = PreparedPocAttributeEnvelope.TryCapture(carrier, new PocWorkBudget(4096));
            Assert.That(envelope?.IsCurrent(new PocWorkBudget(4096)), Is.True);
            for (var i = 0; i < 100000; i++) { carrier.Add(attribute); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(envelope!.IsCurrent(new PocWorkBudget(128)), Is.False);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(65536));
        }

        [Test]
        public void NullAttributeRootVetoesWithoutPreparationException()
        {
            MethodBodyStatement[] carrier = [null!];
            Assert.That(PreparedPocAttributeEnvelope.TryCapture(carrier, new PocWorkBudget(4096)), Is.Null);
        }

        [Test]
        public void NonAttributeStatementsRemainOutsideCanonicalAttributeProfile()
        {
            MethodBodyStatement[] carrier = [new ExpressionStatement(new InvokeMethodExpression(null, "Call", []))];
            Assert.That(PreparedPocAttributeEnvelope.TryCapture(carrier, new PocWorkBudget(4096)), Is.Null);
        }

        [TestCase("named")]
        [TestCase("binary-left")]
        [TestCase("binary-right")]
        [TestCase("object-value")]
        [TestCase("dictionary-value")]
        public void RequiredNullAttributeOperandsVetoBeforeNativeException(string kind)
        {
            MockHelpers.LoadMockGenerator();
            ValueExpression expression = kind switch
            {
                "binary-left" => new BinaryOperatorExpression("+", null!, new LiteralExpression(1)),
                "binary-right" => new BinaryOperatorExpression("+", new LiteralExpression(1), null!),
                "object-value" => new ObjectInitializerExpression(new Dictionary<ValueExpression, ValueExpression>
                    { [new LiteralExpression("Value")] = null! }, UseSingleLine: true),
                "dictionary-value" => new DictionaryInitializerExpression(new Dictionary<ValueExpression, ValueExpression>
                    { [new LiteralExpression("key")] = null! }),
                _ => new LiteralExpression(1)
            };
            var attribute = kind == "named"
                ? new AttributeStatement(typeof(CLSCompliantAttribute), new KeyValuePair<string, ValueExpression>[] { new("SafeName", null!) })
                : new AttributeStatement(typeof(CLSCompliantAttribute), new ValueExpression[] { expression });
            using var writer = new CodeWriter();
            Assert.Throws<NullReferenceException>(() => attribute.Write(writer), "Native required-operand control.");
            Assert.That(PreparedPocAttributeEnvelope.TryCapture(new MethodBodyStatement[] { attribute }, new PocWorkBudget(4096)), Is.Null);
        }

        [TestCase("receiver")]
        [TestCase("member")]
        [TestCase("indexer")]
        [TestCase("keyword")]
        public void OptionalNullAttributeOperandsKeepNativeSupport(string kind)
        {
            MockHelpers.LoadMockGenerator();
            ValueExpression expression = kind switch
            {
                "receiver" => new InvokeMethodExpression(null, "Call", []),
                "member" => new MemberExpression(null, "Value"),
                "indexer" => new IndexerExpression(null, null),
                _ => new KeywordExpression("default", null)
            };
            var attribute = new AttributeStatement(typeof(CLSCompliantAttribute), new ValueExpression[] { expression });
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => attribute.Write(writer));
            var envelope = PreparedPocAttributeEnvelope.TryCapture(new MethodBodyStatement[] { attribute }, new PocWorkBudget(4096));
            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope!.IsCurrent(new PocWorkBudget(4096)), Is.True);
        }

        [Test]
        public void EmptyCanonicalAttributesAreAnAvailableSnapshotNotARebuildProof()
        {
            MethodBodyStatement[] carrier = [];
            var envelope = PreparedPocAttributeEnvelope.TryCapture(carrier, new PocWorkBudget(4096));
            Assert.That(envelope!.Carrier, Is.SameAs(carrier));
            Assert.That(envelope.Count, Is.Zero);
            Assert.That(envelope.IsCurrent(new PocWorkBudget(4096)), Is.True);
        }
    }
}

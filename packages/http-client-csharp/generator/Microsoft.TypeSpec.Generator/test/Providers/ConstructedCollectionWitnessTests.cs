// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using ConstructedCollectionBridge = Microsoft.TypeSpec.Generator.Primitives.ConstructedCollectionWitness;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class ConstructedCollectionWitnessTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator(configuration: "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Witness.Tests\"}");

        private sealed class UnknownCarrier<T> : IReadOnlyList<T>
        {
            public int Count => throw new InvalidOperationException("Unknown count must not execute.");
            public T this[int index] => throw new InvalidOperationException("Unknown indexer must not execute.");
            public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Unknown enumeration must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            public override bool Equals(object? other) => throw new InvalidOperationException("Carrier equality must not execute.");
            public override int GetHashCode() => throw new InvalidOperationException("Carrier hash must not execute.");
        }

        [Test]
        public void ConstructionCopyPreservesCarrierAndDoesNotGrantCurrentness()
        {
            var value = new AttributeStatement(typeof(ObsoleteAttribute));
            var carrier = new UnknownCarrier<AttributeStatement>();
            Assert.That(ConstructedCollectionBridge.Record<AttributeStatement>(carrier, value), Is.SameAs(carrier));
            Assert.That(ContractCollections.TryCopyPocConstruction<MethodBodyStatement>(carrier, new PocWorkBudget(1), out var elements), Is.True);
            Assert.That(elements[0], Is.SameAs(value), "Covariant native views retain the same construction elements.");
            Assert.That(ContractCollections.TryCopy<AttributeStatement>(carrier, out _), Is.False);
            Assert.That(ContractCollections.TryCopyForPoc<AttributeStatement>(carrier, new PocWorkBudget(1), out _), Is.False,
                "Strict current-element copying must not mistake provenance for live carrier state.");
            Assert.That(ContractCollections.TryCopyPocConstruction(new UnknownCarrier<AttributeStatement>(), new PocWorkBudget(1), out ImmutableArray<AttributeStatement> _), Is.False);
        }

        [Test]
        public void BudgetAndIncompatibleElementTypeRejectWithoutCarrierCalls()
        {
            var first = new AttributeStatement(typeof(ObsoleteAttribute));
            var second = new AttributeStatement(typeof(ObsoleteAttribute));
            var carrier = ConstructedCollectionBridge.Record<AttributeStatement>(new UnknownCarrier<AttributeStatement>(), first, second);
            Assert.That(ContractCollections.TryCopyPocConstruction(carrier, new PocWorkBudget(1), out _), Is.False);
            Assert.That(ContractCollections.TryCopyPocConstruction(carrier, new PocWorkBudget(2), out var elements), Is.True);
            Assert.That(elements[0], Is.SameAs(first));
            Assert.That(elements[1], Is.SameAs(second));
            Assert.That(ConstructedCollectionWitness.TryCopyConstruction(carrier, new PocWorkBudget(2), out ImmutableArray<ParameterProvider> _), Is.False);
        }

        [Test]
        public void ContradictoryRerecordingCannotRepairEarlierWitness()
        {
            var first = new AttributeStatement(typeof(ObsoleteAttribute));
            var second = new AttributeStatement(typeof(ObsoleteAttribute));
            var carrier = new UnknownCarrier<AttributeStatement>();
            ConstructedCollectionBridge.Record<AttributeStatement>(carrier, first);
            ConstructedCollectionBridge.Record<AttributeStatement>(carrier, second);
            ConstructedCollectionBridge.Record<AttributeStatement>(carrier, first);
            Assert.That(ContractCollections.TryCopyPocConstruction<AttributeStatement>(carrier, new PocWorkBudget(8), out _), Is.False);
        }

        [Test]
        public void SupplementalCurrentnessCannotUseRecordedCarrierElements()
        {
            var first = new LiteralExpression(1);
            var values = new ValueExpression[] { first };
            var carrier = Array.AsReadOnly(values);
            ConstructedCollectionBridge.Record<ValueExpression>(carrier, first);
            var expression = new FormattableStringExpression("{0}", carrier);
            var node = PreparedPocSupplementalFile.Node.TryCapture(expression, true,
                new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null, "Construction provenance permits capture, not a live-element proof.");
            values[0] = new LiteralExpression(2);
            using (var writer = new CodeWriter())
            {
                expression.Write(writer);
                Assert.That(writer.ToString(false), Is.EqualTo("$\"{2}\""), "Test-only native control of the actual changed carrier.");
            }
            Assert.That(node!.IsCurrent(), Is.False,
                "A supplemental currentness recheck must not reproduce stale recorded elements.");
        }

        [Test]
        public void OwnedArraySnapshotIsNotAnElementCurrentnessClaim()
        {
            var first = new AttributeStatement(typeof(ObsoleteAttribute));
            var values = new[] { first };
            var carrier = new UnknownCarrier<AttributeStatement>();
            ConstructedCollectionBridge.RecordOwned<AttributeStatement>(carrier, values);
            values[0] = new AttributeStatement(typeof(ObsoleteAttribute));
            Assert.That(ContractCollections.TryCopyPocConstruction<AttributeStatement>(carrier, new PocWorkBudget(1), out var elements), Is.True);
            Assert.That(elements[0], Is.SameAs(first));
            Assert.That(ContractCollections.TryCopyForPoc<AttributeStatement>(carrier, new PocWorkBudget(1), out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InitializerProjectionKeepsOneNativeEnumeration(bool enabled)
        {
            MockHelpers.LoadMockGenerator(configuration: enabled
                ? "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Witness.Tests\"}"
                : "{\"package-name\":\"Witness.Tests\"}");
            var first = new ParameterProvider("first", $"First.", typeof(string));
            var second = new ParameterProvider("second", $"Second.", typeof(string));
            var calls = new List<string>();
            IEnumerable<ParameterProvider> Source()
            {
                calls.Add("start"); yield return first;
                calls.Add("second"); yield return second;
                calls.Add("end");
            }
            var initializer = new ConstructorInitializer(true, Source());
            Assert.That(calls, Is.EqualTo(new[] { "start", "second", "end" }));
            Assert.That(initializer.Arguments[0], Is.SameAs(first.AsVariable()));
            Assert.That(initializer.Arguments[1], Is.SameAs(second.AsVariable()));
            Assert.That(ContractCollections.TryCopyPocConstruction(initializer.Arguments, new PocWorkBudget(2), out _), Is.EqualTo(enabled));
            IEnumerable<ParameterProvider> originals = new[] { first, second };
            IReadOnlyList<ValueExpression> originalShape = [.. originals.Select(parameter => parameter.AsVariable())];
            Assert.That(initializer.Arguments.GetType().Name, Is.EqualTo(originalShape.GetType().Name));
            Assert.That(initializer.Arguments.GetType().Assembly, Is.EqualTo(typeof(ConstructorInitializer).Assembly));
        }

        [Test]
        public void InvalidNullCarrierIsLeftToNativeBehavior()
        {
            Assert.That(ConstructedCollectionBridge.Record<AttributeStatement>(null!, new AttributeStatement(typeof(ObsoleteAttribute))), Is.Null);
        }

        [Test]
        public void DisabledConstructionDoesNotCreateWitness()
        {
            MockHelpers.LoadMockGenerator(configuration: "{\"package-name\":\"Witness.Tests\"}");
            var carrier = new UnknownCarrier<AttributeStatement>();
            Assert.That(ConstructedCollectionBridge.Record<AttributeStatement>(carrier, new AttributeStatement(typeof(ObsoleteAttribute))), Is.SameAs(carrier));
            Assert.That(ContractCollections.TryCopyPocConstruction<AttributeStatement>(carrier, new PocWorkBudget(4), out _), Is.False);
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class ProducerOwnedArgumentTests
    {
        [Test]
        public void ProducerCarrierReadsLiveElementsWithoutWitnessFreshness()
        {
            MockHelpers.LoadMockGenerator();
            ValueExpression[] elements = [new LiteralExpression(1)];
            var carrier = ArgumentCarrierFactory.Create(elements);
            var call = new InvokeMethodExpression(null, "Call", carrier);
            ConstructedArgumentWitness.Record(call, elements);
            var revision = call.DeclarationRevision;
            var node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.IsCurrent(), Is.True);
            Assert.That(carrier, Is.InstanceOf<ReadOnlyCollection<ValueExpression>>());
            Assert.Throws<NotSupportedException>(() => ((IList<ValueExpression>)carrier)[0] = elements[0]);
            var changed = new LiteralExpression(2);
            elements[0] = changed;
            Assert.That(ContractCollections.TryCopyForPoc(carrier, new PocWorkBudget(4096), out var current), Is.True);
            Assert.That(ReferenceEquals(current[0], changed), Is.True);
            Assert.That(call.DeclarationRevision, Is.EqualTo(revision));
            Assert.That(node.IsCurrent(), Is.False);
            using var writer = new CodeWriter();
            call.Write(writer);
            Assert.That(writer.ToString(false), Does.Contain("Call(2)"));
            // Initial capture must also use the actual owned array, not the old
            // witness values: replace an element before taking a new snapshot.
            var recaptured = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(recaptured?.IsCurrent(), Is.True);
        }

        [Test]
        public void ProducerCarrierBudgetAndSharedPolicyStaySeparate()
        {
            ValueExpression[] elements = [new LiteralExpression(1), new LiteralExpression(2)];
            var carrier = ArgumentCarrierFactory.Create(elements);
            Assert.That(ContractCollections.TryCopy(carrier, out _), Is.False, "Do not broaden shared Task03 collection policy.");
            Assert.That(ContractCollections.TryCopyForPoc(carrier, new PocWorkBudget(1), out _), Is.False);
            var exact = new PocWorkBudget(elements.Length);
            Assert.That(ContractCollections.TryCopyForPoc(carrier, exact, out var current), Is.True);
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(ReferenceEquals(current[0], elements[0]) && ReferenceEquals(current[1], elements[1]), Is.True);
            Assert.That(ContractCollections.TryCopyForPoc(carrier, new PocWorkBudget(elements.Length), out _), Is.True,
                "Each observation is funded separately, not from an exhausted capture.");
        }

        [Test]
        public void SharedShapeRecheckKeepsProducerCarrierOpaque()
        {
            MockHelpers.LoadMockGenerator();
            ValueExpression[] elements = [new LiteralExpression(1)];
            var call = new InvokeMethodExpression(null, "Call", ArgumentCarrierFactory.Create(elements));
            var shared = StructuredSourceShape.Capture(call, disableXmlDocs: true);
            Assert.That(shared.Opaque, Is.True);
            var recheck = StructuredSourceShape.CaptureForPocRecheck(call, disableXmlDocs: true, new PocWorkBudget(4096));
            Assert.That(recheck, Is.Not.Null);
            Assert.That(recheck!.Opaque, Is.True, "A bounded recheck must not expand the shared graph's deliberately opaque carrier.");
            Assert.That(shared.MatchesForPoc(recheck, new PocWorkBudget(4096)), Is.True);
            var supplemental = StructuredSourceShape.CaptureForPocSupplementCurrent(call, disableXmlDocs: true, new PocWorkBudget(4096));
            Assert.That(supplemental.Opaque, Is.False, "The independently admitted POC profile still reads actual live elements.");
        }

        [Test]
        public void ArbitraryReadOnlyWrapperStillRejectsWithoutCallbacks()
        {
            var hostile = new HostileList();
            var unknown = new ReadOnlyCollection<ValueExpression>(hostile);
            Assert.That(ContractCollections.TryCopyForPoc(unknown, new PocWorkBudget(4096), out _), Is.False);
            Assert.That(hostile.Calls, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => _ = unknown[0]);
            Assert.That(hostile.Calls, Is.EqualTo(1), "Native foreign indexer positive.");
        }

        private sealed class HostileList : IList<ValueExpression>
        {
            internal int Calls;
            public ValueExpression this[int index] { get { Calls++; throw new InvalidOperationException(); } set => throw new NotSupportedException(); }
            public int Count { get { Calls++; throw new InvalidOperationException(); } }
            public bool IsReadOnly => true;
            public void Add(ValueExpression item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(ValueExpression item) => throw new NotSupportedException();
            public void CopyTo(ValueExpression[] array, int index) => throw new NotSupportedException();
            public IEnumerator<ValueExpression> GetEnumerator() { Calls++; throw new InvalidOperationException(); }
            public int IndexOf(ValueExpression item) => throw new NotSupportedException();
            public void Insert(int index, ValueExpression item) => throw new NotSupportedException();
            public bool Remove(ValueExpression item) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

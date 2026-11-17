// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PocSharedReferenceFastPathTests
    {
        [TestCase(1)]
        [TestCase(64)]
        [TestCase(2048)]
        public void ExactArrayCurrentnessNeedsOneFundedLivePass(int count)
        {
            var values = new object[count];
            for (int i = 0; i < count; i++) { values[i] = new object(); }
            var snapshot = ImmutableArray.CreateRange(values);
            Assert.That(SourceCurrentnessChecks.References(values, snapshot, new PocWorkBudget(2 * count + 1)), Is.True,
                "The actual array and retained reference snapshot must first agree under the original credits.");
            var exact = new PocWorkBudget(count + 1);
            Assert.That(SourceCurrentnessChecks.References(values, snapshot, exact), Is.True,
                "An exact array can compare each live element without an input-sized intermediate copy.");
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(SourceCurrentnessChecks.References(values, snapshot, new PocWorkBudget(count)), Is.False);
            var original = values[count / 2];
            values[count / 2] = new object();
            try
            {
                Assert.That(SourceCurrentnessChecks.References(values, snapshot, new PocWorkBudget(count + 1)), Is.False,
                    "The comparison must inspect the current array, not a construction witness.");
            }
            finally { values[count / 2] = original; }
            Assert.That(SourceCurrentnessChecks.References(values, snapshot, new PocWorkBudget(count + 1)), Is.True);
        }

        [Test]
        public void ExactImmutableArrayCurrentnessNeedsOneFundedLivePass()
        {
            var values = new object[64];
            for (int i = 0; i < values.Length; i++) { values[i] = new object(); }
            var immutable = ImmutableArray.CreateRange(values);
            IReadOnlyList<object> current = immutable;
            var snapshot = ImmutableArray.CreateRange(values);
            Assert.That(SourceCurrentnessChecks.References(current, snapshot, new PocWorkBudget(129)), Is.True);
            var exact = new PocWorkBudget(65);
            Assert.That(SourceCurrentnessChecks.References(current, snapshot, exact), Is.True,
                "The exact immutable-array carrier needs no separately funded array copy.");
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(SourceCurrentnessChecks.References(current, snapshot, new PocWorkBudget(64)), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LengthMismatchRetainsItsUnderfundedBoundary(bool immutable)
        {
            var values = new object[64];
            for (int i = 0; i < values.Length; i++) { values[i] = new object(); }
            IReadOnlyList<object> current = immutable ? ImmutableArray.CreateRange(values) : values;
            var expected = ImmutableArray.CreateRange(new object[65]);
            var shortBudget = new PocWorkBudget(65);
            Assert.That(SourceCurrentnessChecks.References(current, expected, shortBudget), Is.False);
            Assert.That(shortBudget.Exhausted, Is.True,
                "An insufficient budget must not turn a length mismatch into an observed sticky mismatch.");
            var sufficient = new PocWorkBudget(130);
            Assert.That(SourceCurrentnessChecks.References(current, expected, sufficient), Is.False);
            Assert.That(sufficient.Exhausted, Is.False);
        }

        [Test]
        public void OtherCarrierProfilesKeepTheirOriginalBoundary()
        {
            var values = new object[64];
            for (int i = 0; i < values.Length; i++) { values[i] = new object(); }
            var snapshot = ImmutableArray.CreateRange(values);
            var list = new List<object>(values);
            Assert.That(SourceCurrentnessChecks.References(list, snapshot, new PocWorkBudget(65)), Is.False,
                "Mutable lists retain the original copy-based profile.");
            Assert.That(SourceCurrentnessChecks.References(list, snapshot, new PocWorkBudget(129)), Is.True);
            var hostile = new HostileReferences();
            Assert.That(SourceCurrentnessChecks.References(hostile, snapshot, new PocWorkBudget(4096)), Is.False);
            Assert.That(hostile.Calls, Is.Zero, "Unknown collection callbacks must not run.");
        }

        [Test]
        public void DefaultSnapshotKeepsItsOriginalExceptionBoundary()
        {
            var values = new object[] { new() };
            var snapshot = default(ImmutableArray<object>);
            Assert.Throws<NullReferenceException>(() => SourceCurrentnessChecks.References(values, snapshot, new PocWorkBudget(0)));
        }

        [Test]
        public void CovariantArraysKeepTheOriginalSharedCarrierBoundary()
        {
            var actual = new string[] { "first", "second" };
            IReadOnlyList<object> current = actual;
            var snapshot = ImmutableArray.Create<object>(actual[0], actual[1]);
            Assert.That(SourceCurrentnessChecks.References(current, snapshot, new PocWorkBudget(3)), Is.False,
                "A covariant array must retain the original copy-based budget profile.");
            Assert.That(SourceCurrentnessChecks.References(current, snapshot, new PocWorkBudget(5)), Is.True,
                "The original shared carrier accepts covariant arrays with full credits.");
        }

        private sealed class HostileReferences : IReadOnlyList<object>
        {
            internal int Calls;
            public int Count { get { Calls++; throw new InvalidOperationException(); } }
            public object this[int index] { get { Calls++; throw new InvalidOperationException(); } }
            public IEnumerator<object> GetEnumerator() { Calls++; throw new InvalidOperationException(); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

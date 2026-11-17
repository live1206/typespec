// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocTypeConstraintTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator(includeXmlDocs: true);

        [Test]
        public void ExactDictionaryConstraintIsPlannedWithoutMaterializingIt()
        {
            var owner = new ChangeTrackingDictionaryDefinition();
            Assert.That(owner.CachedWhereClause, Is.Null);
            var plan = PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(4096));
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan!.IsCurrent, Is.True);
            Assert.That(owner.CachedWhereClause, Is.Null, "Planning must not invoke the native builder.");
            var where = owner.WhereClause;
            plan.ObserveNative(where);
            Assert.That(plan.HasObservedExpectedEmission, Is.False, "A getter observation alone is not consumption.");
            ObserveOwnedRoot(plan, where!);
            Assert.That(plan.HasObservedExpectedEmission, Is.True);
            using var writer = new CodeWriter();
            where!.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("where TKey : notnull"));
        }

        [Test]
        public void DefaultNullConstraintHasNoBuilderReplay()
        {
            var owner = new TestTypeProvider();
            var plan = PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(32));
            Assert.That(plan, Is.Not.Null);
            Assert.That(owner.CachedWhereClause, Is.Null);
            plan!.ObserveNative(owner.WhereClause);
            Assert.That(plan.HasObservedExpectedEmission, Is.True);
        }

        private sealed class UnknownConstraintOwner : TestTypeProvider
        {
            internal int Calls { get; private set; }
            protected override WhereExpression? BuildWhereClause() { Calls++; throw new InvalidOperationException("Must remain native."); }
        }

        private class HiddenConstraintOwner : TestTypeProvider
        {
            protected new string BuildWhereClause() => throw new InvalidOperationException("Hidden methods must not execute.");
        }

        [Test]
        public void HiddenConstraintDispatchVetoesWithoutReflectionException()
        {
            var owner = new HiddenConstraintOwner();
            Assert.That(PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(32)), Is.Null);
            Assert.That(owner.WhereClause, Is.Null, "The native inherited getter still invokes the base virtual implementation.");
        }

        [Test]
        public void UnknownConstraintBuilderIsNotInvokedDuringPlanning()
        {
            var owner = new UnknownConstraintOwner();
            Assert.That(PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(32)), Is.Null);
            Assert.That(owner.Calls, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => _ = owner.WhereClause);
            Assert.That(owner.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ObservedKeywordMutationAndRestorationStaysRejected()
        {
            var owner = new ChangeTrackingDictionaryDefinition();
            var plan = PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(4096))!;
            var where = owner.WhereClause!;
            plan.ObserveNative(where);
            ObserveOwnedRoot(plan, where);
            Assert.That(plan.HasObservedExpectedEmission, Is.True);
            var keyword = (KeywordExpression)where.Constraints[0];
            keyword.Update("notnull\nclass Escaped {}", null);
            keyword.Update("notnull", null);
            Assert.That(plan.HasObservedExpectedEmission, Is.False, "The revision detects change-and-restore after observation.");
        }

        private sealed class UnknownConstraints : IReadOnlyList<ValueExpression>
        {
            public int Count => throw new InvalidOperationException("Unknown constraints must not execute.");
            public ValueExpression this[int index] => throw new InvalidOperationException("Unknown constraints must not execute.");
            public IEnumerator<ValueExpression> GetEnumerator() => throw new InvalidOperationException("Unknown constraints must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void UnownedConstraintCarrierIsNotQueried()
        {
            var owner = new ChangeTrackingDictionaryDefinition();
            var where = owner.WhereClause!;
            typeof(WhereExpression).GetProperty(nameof(WhereExpression.Constraints))!.SetValue(where, new UnknownConstraints());
            Assert.That(PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(4096)), Is.Null);
        }

        // Mechanics only; the client fixture checks actual native boundaries.
        private static void ObserveOwnedRoot(PreparedPocTypeConstraint plan, WhereExpression where)
        {
            using var root = plan.BeginNativeRoot();
            plan.ObserveNativeProducer(where);
            plan.ObserveNativeProducer(where.Constraints[0]);
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("foreign")]
        [TestCase("repeat")]
        public void NativeRootRequiresExactOccurrenceSequence(string defect)
        {
            var owner = new ChangeTrackingDictionaryDefinition();
            var plan = PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(4096))!;
            var where = owner.WhereClause!;
            plan.ObserveNative(where);
            using (plan.BeginNativeRoot())
            {
                plan.ObserveNativeProducer(where);
                if (defect != "missing")
                { plan.ObserveNativeProducer(defect == "foreign" ? new KeywordExpression("notnull", null) : where.Constraints[0]); }
                if (defect == "extra") { plan.ObserveNativeProducer(where.Constraints[0]); }
            }
            if (defect == "repeat") { ObserveOwnedRoot(plan, where); }
            Assert.That(plan.HasObservedExpectedEmission, Is.False);
        }

        [Test]
        public void BudgetExhaustionAndOwnerChangeVetoTheFragment()
        {
            var owner = new ChangeTrackingDictionaryDefinition();
            Assert.That(PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(1)), Is.Null);
            Assert.That(owner.CachedWhereClause, Is.Null);
            var plan = PreparedPocTypeConstraint.TryCapture(owner, new PocWorkBudget(4096))!;
            owner.Update(name: "Other");
            Assert.That(plan.IsCurrent, Is.False);
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PocLinkedWorkBudgetTests
    {
        [Test]
        public void ChildChargesEveryAncestorBeforeWork()
        {
            var parent = new PocWorkBudget(10);
            var child = new PocWorkBudget(7, parent);
            var leaf = new PocWorkBudget(5, child);
            Assert.That(leaf.TrySpend(3), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Remaining, Is.EqualTo(7));
                Assert.That(child.Remaining, Is.EqualTo(4));
                Assert.That(leaf.Remaining, Is.EqualTo(2));
            });
        }

        [Test]
        public void ParentExhaustionDoesNotPartiallyDebitChild()
        {
            var parent = new PocWorkBudget(2);
            var child = new PocWorkBudget(7, parent);
            Assert.That(child.TrySpend(3), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Remaining, Is.EqualTo(2));
                Assert.That(child.Remaining, Is.EqualTo(7));
                Assert.That(parent.Exhausted, Is.True);
                Assert.That(child.Exhausted, Is.True);
            });
        }

        [Test]
        public void ChildCapExhaustionDoesNotExhaustOrDebitParent()
        {
            var parent = new PocWorkBudget(10);
            var child = new PocWorkBudget(2, parent);
            Assert.That(child.TrySpend(3), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Remaining, Is.EqualTo(10));
                Assert.That(parent.Exhausted, Is.False);
                Assert.That(child.Remaining, Is.EqualTo(2));
                Assert.That(child.Exhausted, Is.True);
            });
        }

        [Test]
        public void SiblingsShareParentAndNegativeDebitDoesNotMutate()
        {
            var parent = new PocWorkBudget(10);
            var first = new PocWorkBudget(8, parent);
            var second = new PocWorkBudget(8, parent);
            Assert.That(first.TrySpend(4), Is.True);
            Assert.That(second.TrySpend(-1), Is.False);
            Assert.That(second.TrySpend(6), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Remaining, Is.Zero);
                Assert.That(parent.Exhausted, Is.False);
                Assert.That(first.Remaining, Is.EqualTo(4));
                Assert.That(second.Remaining, Is.EqualTo(2));
            });
            Assert.That(first.TrySpend(), Is.False);
            Assert.That(parent.Exhausted, Is.True);
            Assert.That(first.Remaining, Is.EqualTo(4));
        }

        [Test]
        public void ExhaustedBudgetCannotResumeWithASmallerDebit()
        {
            var budget = new PocWorkBudget(10);
            Assert.That(budget.TrySpend(11), Is.False);
            Assert.That(budget.Exhausted, Is.True);
            Assert.That(budget.Remaining, Is.EqualTo(10));
            Assert.That(budget.TrySpend(1), Is.False,
                "A failed reservation vetoes this operation, even when a smaller amount remains.");
            Assert.That(budget.Remaining, Is.EqualTo(10));
            Assert.That(new PocWorkBudget(10).TrySpend(1), Is.True,
                "A separate operation receives an independent budget.");
        }

        [Test]
        public void ExhaustedParentCannotFundAnotherChild()
        {
            var parent = new PocWorkBudget(2);
            var first = new PocWorkBudget(7, parent);
            var second = new PocWorkBudget(7, parent);
            Assert.That(first.TrySpend(3), Is.False);
            Assert.That(parent.Exhausted, Is.True);
            Assert.That(parent.Remaining, Is.EqualTo(2));
            Assert.That(second.TrySpend(1), Is.False,
                "Another child cannot resume an exhausted enclosing operation.");
            Assert.That(second.Exhausted, Is.True);
            Assert.That(second.Remaining, Is.EqualTo(7));
            Assert.That(parent.Remaining, Is.EqualTo(2));
            Assert.That(new PocWorkBudget(7).TrySpend(1), Is.True);
        }

        [Test]
        public void DetachingCompletedChildDoesNotRetainOrChargeParent()
        {
            var parent = new PocWorkBudget(10);
            var child = new PocWorkBudget(7, parent);
            Assert.That(child.TrySpend(2), Is.True);
            child.DetachParent();
            Assert.That(child.TrySpend(3), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Remaining, Is.EqualTo(8));
                Assert.That(child.Remaining, Is.EqualTo(2));
            });
        }

        [Test]
        public void ExactLimitAndExplicitChildRejectionRemainLocal()
        {
            var parent = new PocWorkBudget(10);
            var child = new PocWorkBudget(4, parent);
            Assert.That(child.TrySpend(4), Is.True);
            child.Reject();
            Assert.Multiple(() =>
            {
                Assert.That(child.Remaining, Is.Zero);
                Assert.That(child.Exhausted, Is.True);
                Assert.That(parent.Remaining, Is.EqualTo(6));
                Assert.That(parent.Exhausted, Is.False);
            });
        }
    }
}

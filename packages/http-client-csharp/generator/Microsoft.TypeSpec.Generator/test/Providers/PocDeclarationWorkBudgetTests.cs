// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PocDeclarationWorkBudgetTests
    {
        [Test]
        public void ExactLimitAndExhaustionRemainSticky()
        {
            var budget = new PocDeclarationWorkBudget();
            Assert.That(budget.TrySpendReferenceLookup(262143), Is.True);
            Assert.That(budget.TrySpendReferenceLookup(0), Is.False);
            Assert.That(budget.TrySpendReferenceLookup(0), Is.False);
            Assert.That(new PocDeclarationWorkBudget().TrySpendReferenceLookup(0), Is.True);
        }

        [Test]
        public void InvalidLookupCannotReopenTheLedger()
        {
            var budget = new PocDeclarationWorkBudget();
            Assert.That(budget.TrySpendReferenceLookup(-1), Is.False);
            Assert.That(budget.TrySpendReferenceLookup(0), Is.False);
        }

        [Test]
        public void ConcurrentReservationsCannotExceedTheSharedLimit()
        {
            var budget = new PocDeclarationWorkBudget();
            var successes = 0;
            Parallel.For(0, 256, _ =>
            {
                if (budget.TrySpendReferenceLookup(4095)) { Interlocked.Increment(ref successes); }
            });
            Assert.That(successes, Is.InRange(1, 64));
            Assert.That(budget.TrySpendReferenceLookup(0), Is.False);
        }
    }
}

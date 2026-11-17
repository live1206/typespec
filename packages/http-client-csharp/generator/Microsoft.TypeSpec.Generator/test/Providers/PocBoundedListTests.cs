// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PocBoundedListTests
    {
        [Test]
        public void StableExactListsKeepExactAndOneShortBudgetBehavior()
        {
            var one = new object();
            var two = new object();
            var list = new List<object> { one, two };
            var exact = new PocWorkBudget(2);
            Assert.That(ContractCollections.TryCopyForPoc(list, exact, out var copy), Is.True);
            Assert.That(copy, Is.EqualTo(new[] { one, two }));
            Assert.That(exact.Remaining, Is.Zero);
            var shortBudget = new PocWorkBudget(1);
            Assert.That(ContractCollections.TryCopyForPoc(list, shortBudget, out _), Is.False);
            Assert.That(shortBudget.Exhausted, Is.True);
            Assert.That(ContractCollections.TryCopyForPoc(list, new PocWorkBudget(2), out copy), Is.True);
            Assert.That(copy, Is.EqualTo(new[] { one, two }));
            var zero = new PocWorkBudget(0);
            Assert.That(ContractCollections.TryCopyForPoc(new List<object>(), zero, out copy), Is.True);
            Assert.That(copy, Is.Empty);
            Assert.That(zero.Exhausted, Is.False);
        }

        [Test]
        public void FixedLengthAndOwnedCarriersRetainExistingCopyProfile()
        {
            var value = new object();
            object[] array = [value];
            var immutable = ImmutableArray.Create(value);
            var ownedType = typeof(InputLibrary).Assembly.GetType("Microsoft.TypeSpec.Generator.Input.ProducerOwnedReadOnlyList`1")!.MakeGenericType(typeof(object));
            var owned = (IReadOnlyList<object>)Activator.CreateInstance(ownedType, BindingFlags.Instance | BindingFlags.NonPublic, null, [array], null)!;
            Assert.That(ContractCollections.TryCopyForPoc(array, new PocWorkBudget(1), out var a), Is.True);
            Assert.That(ContractCollections.TryCopyForPoc(immutable, new PocWorkBudget(1), out var b), Is.True);
            Assert.That(ContractCollections.TryCopyForPoc(owned, new PocWorkBudget(1), out var c), Is.True);
            Assert.That(a[0], Is.SameAs(value));
            Assert.That(b[0], Is.SameAs(value));
            Assert.That(c[0], Is.SameAs(value));
        }
    }
}

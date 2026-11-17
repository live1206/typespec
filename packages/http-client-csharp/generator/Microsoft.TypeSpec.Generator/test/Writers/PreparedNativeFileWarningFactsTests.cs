// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.Common;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PreparedNativeFileWarningFactsTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator();

        [Test]
        public void ColdDefaultEmptyIsDescribedWithoutMaterializingOrBorrowingAnotherOwner()
        {
            var owner = new WarningOwner();
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
            var facts = PreparedNativeFileWarningFacts.TryCapture(owner);
            Assert.That(facts, Is.Not.Null);
            Assert.That(facts!.IsCurrentAndEmptyFor(owner), Is.True);
            Assert.That(facts.IsCurrentAndEmptyFor(new WarningOwner()), Is.False);
            Assert.That(facts.IsCurrentAndEmptyFor(owner), Is.True);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
            _ = owner.DisabledFileWarnings; // actual native producer, not capture
            Assert.That(owner.CachedDisabledFileWarnings, Is.Not.Null);
            Assert.That(facts.IsCurrentAndEmptyFor(owner), Is.True);
        }

        [Test]
        public void UnknownColdBuilderIsNeverInvokedToDiscoverEmptyData()
        {
            var owner = new CustomWarnings(false);
            Assert.That(PreparedNativeFileWarningFacts.TryCapture(owner), Is.Null);
            Assert.That(owner.Calls, Is.Zero);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
            _ = owner.DisabledFileWarnings;
            Assert.That(owner.Calls, Is.EqualTo(1));
            var facts = PreparedNativeFileWarningFacts.TryCapture(owner);
            Assert.That(facts, Is.Not.Null);
            Assert.That(facts!.IsCurrentAndEmptyFor(owner), Is.True);
            Assert.That(owner.Calls, Is.EqualTo(1));
        }

        [Test]
        public void CachedNonemptyNativeWarningsRemainCallFactsButNotEmptyHeaderEvidence()
        {
            var owner = new CustomWarnings(true);
            _ = owner.DisabledFileWarnings;
            var facts = PreparedNativeFileWarningFacts.TryCapture(owner);
            Assert.That(facts, Is.Not.Null);
            Assert.That(facts!.IsCurrent(), Is.True);
            Assert.That(facts.IsCurrentAndEmptyFor(owner), Is.False);
            Assert.That(owner.Calls, Is.EqualTo(1));
            var array = (SuppressionStatement[])owner.CachedDisabledFileWarnings!;
            var original = array[0];
            array[0] = new SuppressionStatement(null, Literal("CS0168"), "changed");
            Assert.That(facts.IsCurrent(), Is.False);
            array[0] = original;
            Assert.That(facts.IsCurrent(), Is.False);
        }

        [Test]
        public void ColdInputWarningSourceIsUnknownWithoutExecutingTheNativeGetter()
        {
            var owner = new WarningOwner(InputFactory.Model("HasInput"));
            Assert.That(owner.HasFileWarningInput, Is.True);
            Assert.That(PreparedNativeFileWarningFacts.TryCapture(owner), Is.Null);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
        }

        [Test]
        public void ObservedInputSourceChangeIsStickyEvenIfOwnerAssociationIsRestored()
        {
            var owner = new WarningOwner();
            var facts = PreparedNativeFileWarningFacts.TryCapture(owner)!;
            Assert.That(facts.IsCurrentAndEmptyFor(owner), Is.True);
            new WarningOwner(InputFactory.Model("HasInput")).Update(serializations: new[] { owner });
            Assert.That(owner.HasFileWarningInput, Is.True);
            Assert.That(facts.IsCurrent(), Is.False);
            new WarningOwner().Update(serializations: new[] { owner });
            Assert.That(owner.HasFileWarningInput, Is.False);
            Assert.That(facts.IsCurrent(), Is.False);
        }

        private class WarningOwner(InputType? input = null) : TypeProvider(input)
        {
            protected override string BuildName() => "Warnings";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Warnings.cs";
        }

        private sealed class CustomWarnings(bool nonempty) : WarningOwner
        {
            internal int Calls { get; private set; }
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings()
            {
                Calls++;
                return nonempty ? new[] { new SuppressionStatement(null, Literal("CS0618"), "native") } : Array.Empty<SuppressionStatement>();
            }
        }
    }
}

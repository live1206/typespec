// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocRelatedScopeBudgetTests
    {
        [Test]
        public void RelatedCaptureRequiresCreditsBeforeExtraObservation()
        {
            var provider = CachedScope(1024);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider); // Already-captured shared baseline, not extra POC work.
            Assert.That(owner.IsCurrent(), Is.True);
            Assert.That(Capture(provider, owner, new PocWorkBudget(0)), Is.Null,
                "Zero credits must veto before the extra shared-snapshot recheck, member copies or name-set creation.");
        }

        [Test]
        public void RelatedCurrentnessRequiresNewCredits()
        {
            var provider = CachedScope(4);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider);
            var snapshot = Capture(provider, owner, new PocWorkBudget(262144));
            Assert.That(snapshot, Is.Not.Null);
            var current = snapshot!.GetType().GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.True);
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(0) }), Is.False,
                "Capture and a prior currentness observation do not pay for this query's repeated work.");
        }

        [Test]
        public void RelatedScopeKeepsSharedPartialAvailabilityWithoutForcingOtherCaches()
        {
            var provider = CachedScope(4);
            AssertUnmaterialized(provider);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider);
            Assert.That(owner.IsCurrent(), Is.True);
            Assert.That(owner.IsCurrentForPoc(new PocWorkBudget(262144)), Is.False,
                "The complete-file profile is deliberately stronger and cannot substitute for this shared availability profile.");
            var snapshot = Capture(provider, owner, new PocWorkBudget(262144));
            Assert.That(snapshot, Is.Not.Null, "Known related member/name scope does not require constructors, attributes or serializations.");
            Assert.That(snapshot!.GetType().GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.True);
            AssertUnmaterialized(provider);
        }

        [Test]
        public void RelatedNameHashingAndEqualStringComparisonNeedCharacterCredits()
        {
            var provider = CachedScope(4);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider);
            var compact = new PocWorkBudget(262144);
            Assert.That(Capture(provider, owner, compact), Is.Not.Null);
            var compactWork = 262144 - compact.Remaining;
            var field = provider.CachedFields![0];
            var name = new string('N', 2048);
            typeof(FieldProvider).GetProperty(nameof(FieldProvider.Name))!.SetValue(field, name);
            Assert.That(Capture(provider, owner, new PocWorkBudget(compactWork + 32)), Is.Null,
                "A compact observation does not pay for a long ordinal name hash and its possible collisions.");
            var snapshot = Capture(provider, owner, new PocWorkBudget(262144));
            Assert.That(snapshot, Is.Not.Null);
            var current = snapshot!.GetType().GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var referenceFastPath = new PocWorkBudget(262144);
            Assert.That(current.Invoke(snapshot, new object[] { referenceFastPath }), Is.True);
            var pointWork = 262144 - referenceFastPath.Remaining;
            typeof(FieldProvider).GetProperty(nameof(FieldProvider.Name))!.SetValue(field, new string('N', 2048));
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(pointWork + 32) }), Is.False,
                "Equal contents in a different string still require a separately funded character comparison.");
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.True);
        }

        [Test]
        public void SharedAvailabilityRecheckDoesNotDispatchUnknownCollections()
        {
            var provider = CachedScope(4);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider);
            var snapshot = Capture(provider, owner, new PocWorkBudget(262144));
            Assert.That(snapshot, Is.Not.Null);
            var current = snapshot!.GetType().GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var constructors = new HostileCollection<ConstructorProvider>();
            typeof(TypeProvider).GetField("_constructors", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, constructors);
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.True,
                "A collection absent from the shared snapshot stays ignored; this is not complete-file admission.");
            Assert.That(constructors.Calls, Is.Zero);
            var fields = new HostileCollection<FieldProvider>();
            typeof(TypeProvider).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, fields);
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.False);
            Assert.That(fields.Calls, Is.Zero, "A previously captured collection replaced by an unknown carrier is rejected without dispatch.");
        }

        [Test]
        public void SharedReservationStopsBeforeUnfundedGenericCopies()
        {
            var arguments = new CSharpType[4096];
            Array.Fill(arguments, new CSharpType(typeof(int)));
            var provider = CachedScope(4, arguments);
            var owner = new PreparedSourceFile.OwnerSnapshot(provider); // Native/shared capture is outside this extra observation.
            Assert.That(owner.IsCurrent(), Is.True);
            Assert.That(owner.IsCurrentForPocSharedAvailability(new PocWorkBudget(4)), Is.False); // Warm prepass/JIT.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var current = owner.IsCurrentForPocSharedAvailability(new PocWorkBudget(4));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(current, Is.False);
            Assert.That(allocated, Is.LessThan(16384), "The legacy expected/current argument arrays must not be copied before reservation succeeds.");
            Assert.That(owner.IsCurrentForPocSharedAvailability(new PocWorkBudget(262144)), Is.True);
            Assert.That(owner.IsCurrent(), Is.True, "POC budget failure must not invalidate shared currentness.");
        }

        [Test]
        public void SharedReservationDepthVetoDoesNotInvalidateSharedSnapshot()
        {
            CSharpType argument = new(typeof(int));
            for (var i = 0; i < 300; i++)
            {
                argument = new CSharpType("Nested", "Test", false, false, null, new[] { argument }, true, false);
            }
            var provider = CachedScope(0, new[] { argument });
            var owner = new PreparedSourceFile.OwnerSnapshot(provider);
            Assert.That(owner.IsCurrent(), Is.True);
            var budget = new PocWorkBudget(262144);
            Assert.That(owner.IsCurrentForPocSharedAvailability(budget), Is.False);
            Assert.That(budget.Exhausted, Is.True);
            Assert.That(owner.IsCurrent(), Is.True);
        }

        private sealed class HostileCollection<T> : IReadOnlyList<T>
        {
            internal int Calls;
            public int Count { get { Calls++; throw new InvalidOperationException("Unknown Count"); } }
            public T this[int index] { get { Calls++; throw new InvalidOperationException("Unknown indexer"); } }
            public IEnumerator<T> GetEnumerator() { Calls++; throw new InvalidOperationException("Unknown enumeration"); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static object? Capture(TypeProvider provider, PreparedSourceFile.OwnerSnapshot owner, PocWorkBudget budget) =>
            typeof(PreparedFieldTypes).GetNestedType("PocRelatedScopeSnapshot", BindingFlags.NonPublic)!
                .GetMethod("TryCapture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { provider, owner, budget });

        private static TypeProvider CachedScope(int count, CSharpType[]? arguments = null)
        {
            MockHelpers.LoadMockGenerator();
            var provider = new ScopeProvider(arguments);
            provider.Items = new FieldProvider[count];
            for (var i = 0; i < count; i++)
            {
                provider.Items[i] = new FieldProvider(FieldModifiers.Private, new CSharpType(typeof(int)), "Field" + i, provider);
            }
            _ = provider.Type;
            _ = provider.Fields;
            _ = provider.Properties;
            _ = provider.Methods;
            _ = provider.NestedTypes;
            return provider;
        }

        private static void AssertUnmaterialized(TypeProvider provider)
        {
            Assert.That(provider.CachedConstructors, Is.Null);
            Assert.That(provider.CachedWriteAttributes, Is.Null);
            Assert.That(provider.CachedSerializationProviders, Is.Null);
        }

        private sealed class ScopeProvider(CSharpType[]? arguments = null) : TestTypeProvider
        {
            internal FieldProvider[] Items = [];
            protected override CSharpType[] GetTypeArguments() => arguments ?? [];
            protected internal override FieldProvider[] BuildFields() => Items;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocModelFactoryBudgetTests
    {
        [Test]
        public void FactoryCaptureRequiresCreditsBeforeCopyingCachedMethods()
        {
            var provider = CachedMethods(4096);
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            var capture = typeof(PreparedFieldTypes).GetMethod("CapturePocModelFactoryMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
            capture.Invoke(session, new object[] { provider, new PocWorkBudget(0) });
            var snapshots = (ICollection)typeof(PreparedFieldTypes).GetField("_pocModelFactories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.That(snapshots.Count, Is.Zero, "No credits means no extra POC method-carrier copy or retained receipt.");
            Assert.That(typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
        }

        [Test]
        public void FactoryCurrentnessRequiresNewCreditsAndStillReadsActualReferences()
        {
            var provider = CachedMethods(8);
            var methods = (MethodProvider[])provider.CachedMethods!;
            var snapshotType = typeof(PreparedFieldTypes).GetNestedType("PocModelFactorySnapshot", BindingFlags.NonPublic)!;
            var snapshot = Activator.CreateInstance(snapshotType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [provider, ImmutableArray.CreateRange(methods)], culture: null)!;
            var current = snapshotType.GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var exact = new PocWorkBudget(17); // Metadata + eight copied references + eight comparisons.
            Assert.That(current.Invoke(snapshot, new object[] { exact }), Is.True);
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(current.Invoke(snapshot, new object[] { exact }), Is.False, "Spent point-query credits cannot be reused.");
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(16) }), Is.False);
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(0) }), Is.False,
                "Capture and a prior successful observation do not fund this new query.");
            var saved = methods[0];
            methods[0] = NewMethod(provider, "Changed");
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.False);
            methods[0] = saved;
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.True,
                "This private point snapshot is nonsticky; the enclosing POC session owns sticky invalidation.");
        }

        [Test]
        public void FactoryCaptureKeepsRealSnapshotAndDoesNotReuseSpentCredits()
        {
            var provider = CachedMethods(8);
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            var capture = typeof(PreparedFieldTypes).GetMethod("CapturePocModelFactoryMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var budget = new PocWorkBudget(9); // One new receipt plus eight copied references.
            capture.Invoke(session, new object[] { provider, budget });
            var snapshots = (IList)typeof(PreparedFieldTypes).GetField("_pocModelFactories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.That(snapshots.Count, Is.EqualTo(1));
            Assert.That(budget.Remaining, Is.Zero);
            var current = snapshots[0]!.GetType().GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.That(current.Invoke(snapshots[0], new object[] { new PocWorkBudget(17) }), Is.True);
            var live = (MethodProvider[])provider.CachedMethods!;
            var original = live[0];
            live[0] = NewMethod(provider, "ChangedAfterCapture");
            Assert.That(current.Invoke(snapshots[0], new object[] { new PocWorkBudget(17) }), Is.False,
                "A construction snapshot must not alias the live mutable method array.");
            live[0] = original;
            capture.Invoke(session, new object[] { provider, budget });
            Assert.That(snapshots.Count, Is.EqualTo(1), "Previously spent credits cannot fund a second receipt.");
            Assert.That(typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
        }

        [Test]
        public void FactoryObservationsKeepUnknownMethodCarriersOpaque()
        {
            var provider = CachedMethods(1);
            var snapshotType = typeof(PreparedFieldTypes).GetNestedType("PocModelFactorySnapshot", BindingFlags.NonPublic)!;
            var snapshot = Activator.CreateInstance(snapshotType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [provider, ImmutableArray.CreateRange(provider.CachedMethods!)], culture: null)!;
            var hostile = new HostileMethods();
            typeof(TypeProvider).GetField("_methods", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, hostile);
            var current = snapshotType.GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.That(current.Invoke(snapshot, new object[] { new PocWorkBudget(262144) }), Is.False);
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetMethod("CapturePocModelFactoryMethods", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, new object[] { provider, new PocWorkBudget(262144) });
            Assert.That(typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
            Assert.That(hostile.Calls, Is.Zero);
        }

        private sealed class HostileMethods : IReadOnlyList<MethodProvider>
        {
            internal int Calls;
            public int Count { get { Calls++; throw new InvalidOperationException("Unknown Count"); } }
            public MethodProvider this[int index] { get { Calls++; throw new InvalidOperationException("Unknown indexer"); } }
            public IEnumerator<MethodProvider> GetEnumerator() { Calls++; throw new InvalidOperationException("Unknown enumeration"); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static TypeProvider CachedMethods(int count)
        {
            MockHelpers.LoadMockGenerator();
            var provider = new MethodsProvider();
            var methods = new MethodProvider[count];
            Array.Fill(methods, NewMethod(provider, "Create"));
            provider.Items = methods;
            _ = provider.Methods; // Native/customization getter work is outside extra POC observation.
            return provider;
        }

        private static MethodProvider NewMethod(TypeProvider owner, string name) => new(
            new MethodSignature(name, null, MethodSignatureModifiers.Public | MethodSignatureModifiers.Static, null, null, Array.Empty<ParameterProvider>()),
            MethodBodyStatement.Empty, owner);

        private sealed class MethodsProvider : TestTypeProvider
        {
            internal MethodProvider[] Items = [];
            protected internal override MethodProvider[] BuildMethods() => Items;
        }
    }
}

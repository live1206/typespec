// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocNameObservationBudgetTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ArityOnlyCurrentnessDoesNotCopyArgumentElements(bool providerSnapshot)
        {
            MockHelpers.LoadMockGenerator();
            var arguments = new CSharpType[50000];
            Array.Fill(arguments, new CSharpType(typeof(int)));
            var provider = new CachedArityProvider(arguments);
            var type = provider.Type; // Native/cached construction is outside the observation measurement.
            var snapshotType = typeof(PreparedFieldTypes).GetNestedType(
                providerSnapshot ? "PocProviderNameSnapshot" : "PocTypeNameSnapshot", BindingFlags.NonPublic)!;
            object?[] captured = providerSnapshot
                ? [provider, type, type.IdentityVersion, type.Name, type.Namespace, arguments.Length, provider.CachedDeclaringTypeProvider, type.DeclaringType]
                : [type, type.IdentityVersion, type.Name, type.Namespace, arguments.Length, type.DeclaringType];
            var snapshot = Activator.CreateInstance(snapshotType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: captured, culture: null)!;
            var current = snapshotType.GetMethod("IsCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var funded = snapshotType.GetMethod("IsCurrentForPoc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.That(funded.Invoke(snapshot, new object[] { new PocWorkBudget(0) }), Is.False);
            Assert.That(funded.Invoke(snapshot, new object[] { new PocWorkBudget(2) }), Is.False);
            Assert.That(funded.Invoke(snapshot, new object[] { new PocWorkBudget(3) }), Is.True,
                "Metadata and two reference-fast string comparisons suffice; large arity still needs no element walk.");
            Assert.That(current.Invoke(snapshot, null), Is.True); // Warm the same reflection/JIT path.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = current.Invoke(snapshot, null);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, Is.True, "An unchanged arity-only observation remains valid even for a large cached array.");
            Assert.That(allocated, Is.LessThan(16384), "Reading a cached arity must not allocate a copy of the argument elements.");
            arguments[0] = new CSharpType(typeof(string));
            Assert.That(current.Invoke(snapshot, null), Is.True, "This name observation has always tracked arity, not argument element identities.");
            var argumentProperty = typeof(CSharpType).GetProperty(nameof(CSharpType.Arguments))!;
            argumentProperty.SetValue(type, new CSharpType[1]);
            Assert.That(current.Invoke(snapshot, null), Is.False, "A changed arity is still observed.");
            var hostile = new HostileCountCollection();
            argumentProperty.SetValue(type, hostile);
            Assert.That(current.Invoke(snapshot, null), Is.False);
            Assert.That(hostile.Calls, Is.Zero, "An unknown replacement must not dispatch Count, indexer or enumerator callbacks.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArityOnlyWriteDecisionCaptureDoesNotCopyArgumentElements(bool declaringTypeArguments)
        {
            MockHelpers.LoadMockGenerator();
            var arguments = new CSharpType[50000];
            Array.Fill(arguments, new CSharpType(typeof(int)));
            var generic = new CachedArityProvider(arguments);
            TypeProvider provider = declaringTypeArguments ? new NestedArityProvider(generic) : generic;
            _ = provider.Type; // Cache native type/declaring facts before measuring extra POC capture.
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            var capture = typeof(PreparedFieldTypes).GetMethod("CapturePocWriteDecisionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object[] invocation = [provider, new PocWorkBudget(262144)];
            capture.Invoke(session, invocation); // Warm exact reflection/metadata and small inventory capacity.
            var before = GC.GetAllocatedBytesForCurrentThread();
            capture.Invoke(session, invocation);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var invalid = typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(invalid.GetValue(session), Is.False, "An exact cached array remains supported; no policy replay or blanket arity veto.");
            var names = (ICollection)typeof(PreparedFieldTypes).GetField(
                declaringTypeArguments ? "_pocWriteDecisionParents" : "_pocWriteDecisionNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.That(names.Count, Is.EqualTo(2), "Both actual capture calls must retain their observation.");
            Assert.That(allocated, Is.LessThan(16384), "Capturing only arity must not copy provider or declaring-type argument elements.");
            var hostile = new HostileCountCollection();
            typeof(CSharpType).GetProperty(nameof(CSharpType.Arguments))!.SetValue(generic.Type, hostile);
            var unknownSession = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            capture.Invoke(unknownSession, invocation);
            Assert.That(invalid.GetValue(unknownSession), Is.True, "Unknown provider/parent collections remain an unsupported observation.");
            Assert.That(hostile.Calls, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WriteDecisionCaptureRequiresCreditsBeforeRecording(bool declaringParent)
        {
            MockHelpers.LoadMockGenerator();
            var root = new CachedArityProvider(Array.Empty<CSharpType>());
            TypeProvider provider = declaringParent ? new NestedArityProvider(root) : root;
            _ = provider.Type;
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetMethod("CapturePocWriteDecisionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, new object[] { provider, new PocWorkBudget(0) });
            var names = (ICollection)typeof(PreparedFieldTypes).GetField("_pocWriteDecisionNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            var parents = (ICollection)typeof(PreparedFieldTypes).GetField("_pocWriteDecisionParents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.That(names.Count, Is.Zero, "No credits means no extra write-decision receipt allocation.");
            Assert.That(parents.Count, Is.Zero);
            Assert.That(typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WriteDecisionCapturePaysExactReceiptAndParentProbeWork(bool declaringParent)
        {
            MockHelpers.LoadMockGenerator();
            var root = new CachedArityProvider(Array.Empty<CSharpType>());
            TypeProvider provider = declaringParent ? new NestedArityProvider(root) : root;
            _ = provider.Type;
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            var capture = typeof(PreparedFieldTypes).GetMethod("CapturePocWriteDecisionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var budget = new PocWorkBudget(declaringParent ? 8 : 4);
            capture.Invoke(session, new object[] { provider, budget });
            var invalid = typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var names = (ICollection)typeof(PreparedFieldTypes).GetField("_pocWriteDecisionNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            var parents = (ICollection)typeof(PreparedFieldTypes).GetField("_pocWriteDecisionParents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.That(invalid.GetValue(session), Is.False);
            Assert.That(names.Count, Is.EqualTo(1));
            Assert.That(parents.Count, Is.EqualTo(declaringParent ? 1 : 0));
            Assert.That(budget.Remaining, Is.Zero);
            capture.Invoke(session, new object[] { provider, budget });
            Assert.That(invalid.GetValue(session), Is.True);
            Assert.That(names.Count, Is.EqualTo(1));
            Assert.That(parents.Count, Is.EqualTo(declaringParent ? 1 : 0));
        }

        [Test]
        public void WriteDecisionCaptureNeverMaterializesAnUnavailableType()
        {
            MockHelpers.LoadMockGenerator();
            var provider = new CachedArityProvider(Array.Empty<CSharpType>());
            Assert.That(provider.CachedType, Is.Null);
            var session = Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetMethod("CapturePocWriteDecisionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, new object[] { provider, new PocWorkBudget(262144) });
            Assert.That(provider.CachedType, Is.Null);
            Assert.That(((ICollection)typeof(PreparedFieldTypes).GetField("_pocWriteDecisionNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!).Count, Is.Zero);
            Assert.That(typeof(PreparedFieldTypes).GetField("_pocWriteDecisionInventoryInvalid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session), Is.True);
        }

        [Test]
        public void ArityCountMatchesSharedCopyCollectionPolicy()
        {
            var item = new CSharpType(typeof(int));
            IReadOnlyList<CSharpType>?[] collections =
            [
                Array.Empty<CSharpType>(), new[] { item }, new List<CSharpType> { item },
                ImmutableArray.Create(item), ImmutableArray<CSharpType>.Empty,
                default(ImmutableArray<CSharpType>), null, new ReadOnlyCollection<CSharpType>(new[] { item }),
                new DerivedList { item }, new HostileCountCollection()
            ];
            foreach (var collection in collections)
            {
                var copied = ContractCollections.TryCopy(collection, out var elements);
                Assert.That(ContractCollections.TryGetCount(collection, out var count), Is.EqualTo(copied));
                if (copied) { Assert.That(count, Is.EqualTo(elements.Length)); }
                if (collection is HostileCountCollection hostile) { Assert.That(hostile.Calls, Is.Zero); }
            }
            var values = new List<CSharpType> { item };
            Assert.That(ContractCollections.TryGetCount(values, out var initial), Is.True);
            values.Add(item);
            Assert.That(ContractCollections.TryGetCount(values, out var updated), Is.True);
            Assert.That(initial, Is.EqualTo(1));
            Assert.That(updated, Is.EqualTo(2), "The helper reads current count, not construction provenance.");
            var owned = ArgumentCarrierFactory.Create(new ValueExpression[] { new LiteralExpression(1) });
            Assert.That(ContractCollections.TryGetCount(owned, out _), Is.False, "POC-only producer carriers stay opaque to the shared arity profile.");
        }

        [Test]
        public void RemovalPointReservationFundsOrdinalKeyCharactersAndProbes()
        {
            var reserve = typeof(PreparedFieldTypes).GetMethod("ReservePocRemovalComparison", BindingFlags.Static | BindingFlags.NonPublic)!;
            var compact = ImmutableHashSet.Create(StringComparer.Ordinal, "One", "Two");
            var budget = new PocWorkBudget(262144);
            Assert.That(reserve.Invoke(null, new object[] { compact, budget }), Is.True);
            var compactWork = 262144 - budget.Remaining;
            Assert.That(compactWork, Is.GreaterThan(0));
            Assert.That(reserve.Invoke(null, new object[] { compact, new PocWorkBudget(compactWork - 1) }), Is.False);
            var longKey = ImmutableHashSet.Create(StringComparer.Ordinal, "One", new string('N', 2048));
            Assert.That(reserve.Invoke(null, new object[] { longKey, new PocWorkBudget(compactWork + 32) }), Is.False);
            Assert.That(reserve.Invoke(null, new object[] { longKey, new PocWorkBudget(262144) }), Is.True);
            Assert.That(reserve.Invoke(null, new object[] { compact, new PocWorkBudget(0) }), Is.False);
        }

        private sealed class DerivedList : List<CSharpType> { }
        private sealed class HostileCountCollection : IReadOnlyList<CSharpType>
        {
            internal int Calls;
            public int Count { get { Calls++; throw new InvalidOperationException("Unknown Count"); } }
            public CSharpType this[int index] { get { Calls++; throw new InvalidOperationException("Unknown indexer"); } }
            public IEnumerator<CSharpType> GetEnumerator() { Calls++; throw new InvalidOperationException("Unknown enumeration"); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class NestedArityProvider(TypeProvider parent) : TestTypeProvider
        {
            protected override TypeProvider? BuildDeclaringTypeProvider() => parent;
        }

        private sealed class CachedArityProvider(CSharpType[] arguments) : TestTypeProvider
        {
            protected override CSharpType[] GetTypeArguments() => arguments;
        }
    }
}

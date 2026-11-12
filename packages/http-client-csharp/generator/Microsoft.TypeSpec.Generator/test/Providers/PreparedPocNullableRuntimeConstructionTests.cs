// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class PreparedPocNullableRuntimeConstructionTests
    {
        [Test]
        public void NativeNullableRuntimeCopyHasBoundedConstructorFacts()
        {
            var type = new CSharpType(typeof(Uri), Array.Empty<CSharpType>(), isNullable: true);
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(4096), out var cachedBase), Is.True);
            Assert.That(cachedBase, Is.Not.Null);
            Assert.That(type.IsNullable, Is.True, "Preflight must not replace or normalize the current IR type.");
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(1), out _), Is.False);
        }

        [TestCase("name")]
        [TestCase("namespace")]
        public void ChangedIrIdentityDoesNotReuseNativeConstructorNames(string change)
        {
            var type = new CSharpType(typeof(Uri), Array.Empty<CSharpType>(), isNullable: true);
            if (change == "name") { type.Update(name: "Different"); }
            else { type.Namespace = "Different"; }
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(4096), out _), Is.False);
        }

        [Test]
        public void GenericRuntimeConstructorRemainsUnsupported()
        {
            var type = new CSharpType(typeof(List<>), new[] { new CSharpType(typeof(int)) }, isNullable: true);
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(4096), out _), Is.False);
        }

        [Test]
        public void RuntimeCacheCannotDispatchUnknownTypeEqualityDuringPreflight()
        {
            var type = new CSharpType(typeof(Uri), Array.Empty<CSharpType>(), isNullable: true);
            var cache = (Dictionary<Type, CSharpType>)typeof(CSharpType)
                .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var saved = cache.ToArray();
            try
            {
                var probe = new CacheEqualityProbe();
                // Use the ordinary public native cache producer. Depending on
                // runtime Type equality this introduces a foreign collision key
                // or replaces a base cache value; neither is an audited path.
                CSharpType ignored = probe;
                probe.EqualityCalls = 0;
                var admitted = type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(4096), out _);
                Assert.Multiple(() =>
                {
                    Assert.That(admitted, Is.False);
                    Assert.That(probe.EqualityCalls, Is.Zero, "Preflight cannot use a cache lookup that dispatches unknown equality.");
                });
            }
            finally
            {
                cache.Clear();
                foreach (var pair in saved) { cache.Add(pair.Key, pair.Value); }
            }
        }

        [Test]
        public void NullableRuntimeFactsReserveFutureNativeCacheProbeSeparately()
        {
            var type = new CSharpType(typeof(Uri), Array.Empty<CSharpType>(), isNullable: true);
            var cache = (Dictionary<Type, CSharpType>)typeof(CSharpType)
                .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var argumentBudget = new PocWorkBudget(int.MaxValue);
            Assert.That(ContractCollections.TryCopyForPoc(type.Arguments, argumentBudget, out var arguments), Is.True);
            Assert.That(arguments.IsEmpty, Is.True);
            var pureCacheBudget = new PocWorkBudget(int.MaxValue);
            Assert.That(CSharpType.TryGetPocCachedRuntimeType(typeof(Uri).BaseType!, pureCacheBudget, out var expectedBase), Is.True);
            Assert.That(expectedBase, Is.Not.Null);
            var expected = 48 + 2 * (type.Name.Length + type.Namespace.Length) +
                (int.MaxValue - argumentBudget.Remaining) + (int.MaxValue - pureCacheBudget.Remaining) + cache.Count + 1;
            var budget = new PocWorkBudget(int.MaxValue);
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(budget, out var actualBase), Is.True);
            Assert.That(ReferenceEquals(actualBase, expectedBase), Is.True);
            var native = type.WithNullable(false);
            Assert.That(native.FrameworkType, Is.SameAs(typeof(Uri)));
            Assert.That(int.MaxValue - budget.Remaining, Is.GreaterThanOrEqualTo(expected),
                "Pure cache enumeration does not pay for the separate future native TryGetValue chain; no CPU-overrun claim.");
            Assert.That(type.TrySpendPocNullableRuntimeConstruction(new PocWorkBudget(4096), out _), Is.True,
                "The actual compact native cache remains supported under the existing cap.");
        }

        [Test]
        public void NonFrameworkNullableCrefKeepsRuntimeBaseComparisonCallbackFree()
        {
            MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            var probe = new NullableBaseProbe();
            var provider = new TestTypeProvider(name: "Declared", ns: "PocReview", baseType: new CSharpType(probe));
            var type = provider.Type.WithNullable(true);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(
                [System.Runtime.CompilerServices.FormattableStringFactory.Create("{0:C}", type)]));
            Assert.That(type.IsFrameworkType, Is.False);
            Assert.That(type.IsNullable, Is.True);
            Assert.That(ReferenceEquals(type.BaseType, provider.BaseType), Is.True, "Use the actual current declared base.");
            // Correct a never-established positive premise, not an original
            // regression anchor: Type.op_Equality short-circuits when either
            // operand is RuntimeType. All four native exclusion constants are.
            Assert.That(probe.Equals(typeof(object)), Is.False);
            Assert.That(probe.Calls, Is.GreaterThan(0), "The overridden equality probe itself must work.");
            probe.Calls = 0;
            foreach (var excluded in new[] { typeof(object), typeof(ValueType), typeof(Array), typeof(Enum) })
            {
                Assert.That(probe == excluded, Is.False);
            }
            Assert.That(probe.Calls, Is.Zero, "Native Type operators do not invoke the foreign Equals override.");
            var nativeCopy = type.WithNullable(false);
            var copyCalls = probe.Calls;
            Assert.That(ReferenceEquals(nativeCopy.BaseType, type.BaseType), Is.True);
            probe.Calls = 0;
            var envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs);
            Assert.That(probe.Calls, Is.Zero, "Inspecting the current base must not replay constructor equality.");
            using var writer = new CodeWriter();
            writer.WriteXmlDocsNoScope(docs);
            Assert.That(copyCalls, Is.Zero);
            Assert.That(probe.Calls, Is.Zero, "Actual native copying and cref writing keep the base comparison callback-free.");
            Assert.That(writer.ToString(false), Does.Contain("Declared"));
            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope!.IsCurrent, Is.True);
        }

        private sealed class NullableBaseProbe() : TypeDelegator(typeof(Uri))
        {
            internal int Calls;
            public override bool Equals(object? other) { Calls++; return false; }
            public override bool Equals(Type? other) { Calls++; return false; }
            public override int GetHashCode() => typeof(Uri).GetHashCode();
        }

        private sealed class CacheEqualityProbe() : TypeDelegator(typeof(object))
        {
            internal int EqualityCalls { get; set; }
            public override int GetHashCode() => typeof(object).GetHashCode();
            public override bool Equals(object? other)
            {
                EqualityCalls++;
                return ReferenceEquals(other, typeof(object));
            }
            public override bool Equals(Type? other)
            {
                EqualityCalls++;
                return ReferenceEquals(other, typeof(object));
            }
        }
    }
}

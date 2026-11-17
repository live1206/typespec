// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocHeaderReviewRegressionTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator();

        [TestCase(false)]
        [TestCase(true)]
        public void WitnessedArgumentsObserveCurrentArrayElements(bool replaceBeforeCapture)
        {
            ValueExpression[] arguments = [new LiteralExpression(1)];
            var invocation = new InvokeMethodExpression(null, "Call", arguments);
            ConstructedArgumentWitness.Record(invocation, arguments);
            Assert.That(ReferenceEquals(invocation.Arguments, arguments), Is.True);
            var revision = invocation.DeclarationRevision;
            MethodBodyStatement[] attributes = [new AttributeStatement(typeof(ObsoleteAttribute), [invocation])];
            var safe = PreparedPocAttributeEnvelope.TryCapture(attributes, new PocWorkBudget(4096));
            Assert.That(safe, Is.Not.Null, "The unchanged witnessed invocation is the admission control.");
            arguments[0] = new OpaqueArgument();
            Assert.That(invocation.DeclarationRevision, Is.EqualTo(revision));
            if (replaceBeforeCapture)
            {
                Assert.That(PreparedPocAttributeEnvelope.TryCapture(attributes, new PocWorkBudget(4096)), Is.Null,
                    "Construction provenance must not hide an unsupported current child before the native build.");
            }
            else
            {
                Assert.That(safe!.IsCurrent(new PocWorkBudget(4096)), Is.False,
                    "Currentness must inspect the current element even when carrier identity and invocation revision match.");
            }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void InitializerSnapshotFundsSparseDictionarySlots(int liveCount, bool compact)
        {
            var dictionary = new Dictionary<ValueExpression, ValueExpression>();
            var keys = Enumerable.Range(0, 16384).Select(i => (ValueExpression)new LiteralExpression(i)).ToArray();
            foreach (var key in keys) { dictionary.Add(key, new LiteralExpression(0)); }
            for (int i = liveCount; i < keys.Length; i++) { dictionary.Remove(keys[i]); }
            Assert.That(dictionary.Count, Is.EqualTo(liveCount));
            if (compact) { dictionary.TrimExcess(); }
            Assert.That(dictionary.EnsureCapacity(0), compact ? Is.LessThan(4096) : Is.GreaterThan(4096));
            MethodBodyStatement[] attributes = [new AttributeStatement(typeof(ObsoleteAttribute), [new ObjectInitializerExpression(dictionary)])];
            var budget = new PocWorkBudget(4096);
            var envelope = PreparedPocAttributeEnvelope.TryCapture(attributes, budget);
            if (compact)
            {
                Assert.That(envelope, Is.Not.Null, "A compact, supported carrier is a positive control; no compaction is performed by preparation.");
            }
            else
            {
                Assert.That(envelope, Is.Null, "The zero/one live count does not bound the slots scanned by enumeration.");
                Assert.That(budget.Exhausted, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeaderMaterializationFundsActualIdentityCollisionProbe(bool existingKey)
        {
            using var headers = new PreparedPocHeaderFinalization();
            var dictionary = (Dictionary<TypeProvider, PreparedPocHeaderFinalization.Header>)typeof(PreparedPocHeaderFinalization)
                .GetField("_headers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(headers)!;
            int capacity = dictionary.EnsureCapacity(32);
            var owners = new List<TypeProvider>();
            int? bucket = null;
            while (owners.Count < 33)
            {
                var candidate = new TestTypeProvider();
                int current = (int)((uint)RuntimeHelpers.GetHashCode(candidate) % (uint)capacity);
                bucket ??= current;
                if (current == bucket) { owners.Add(candidate); }
            }
            for (int i = 0; i < 32; i++) { dictionary.Add(owners[i], null!); }
            Assert.That(dictionary.EnsureCapacity(0), Is.EqualTo(capacity));
            Assert.That(owners.All(owner => (uint)RuntimeHelpers.GetHashCode(owner) % (uint)capacity == (uint)bucket!), Is.True);
            var target = existingKey ? owners[0] : owners[32];
            Assert.That(dictionary.ContainsKey(target), Is.EqualTo(existingKey));
            var budget = (PocWorkBudget)typeof(PreparedPocHeaderFinalization)
                .GetField("_budget", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(headers)!;
            Assert.That(budget.TrySpend(budget.Remaining - 1), Is.True);
            headers.Materialize(target, null);
            Assert.That(budget.Exhausted, Is.True,
                "One remaining unit cannot fund the actual 32-entry bucket before either a duplicate or missing-key probe.");
        }

        [TestCase("attribute")]
        [TestCase("file-format")]
        [TestCase("file-statement")]
        public void AncestorCycleProbeFundsActualIdentityCollisionChain(string profile)
        {
            object? envelope = null;
            HashSet<object> ancestors;
            PocWorkBudget budget;
            if (profile == "attribute")
            {
                ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
                budget = new PocWorkBudget(1);
            }
            else
            {
                envelope = Activator.CreateInstance(typeof(PreparedGeneratedDocumentationEnvelope),
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { true }, null)!;
                ancestors = (HashSet<object>)envelope.GetType().GetField("_ancestors", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(envelope)!;
                budget = (PocWorkBudget)envelope.GetType().GetField("_captureBudget", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(envelope)!;
                Assert.That(budget.TrySpend(budget.Remaining - 1), Is.True);
            }
            int capacity = ancestors.EnsureCapacity(32);
            var colliding = new List<object>();
            int? bucket = null;
            while (colliding.Count < 32)
            {
                object candidate = profile switch
                {
                    "attribute" => new LiteralExpression(1),
                    "file-format" => FormattableStringFactory.Create(string.Empty, []),
                    _ => new XmlDocSummaryStatement([])
                };
                int current = (int)((uint)RuntimeHelpers.GetHashCode(candidate) % (uint)capacity);
                bucket ??= current;
                if (current == bucket) { colliding.Add(candidate); }
            }
            foreach (var candidate in colliding) { ancestors.Add(candidate); }
            Assert.That(ancestors.EnsureCapacity(0), Is.EqualTo(capacity));
            Assert.That(colliding.All(value => (uint)RuntimeHelpers.GetHashCode(value) % (uint)capacity == (uint)bucket!), Is.True);
            var target = colliding[0];
            bool admitted;
            if (profile == "attribute")
            {
                // The duplicate exits before the node builder is used. This is
                // a budget unit for the real internal cycle probe, not a claim
                // that the seeded parents form a valid emitted attribute.
                admitted = (bool)typeof(PreparedPocAttributeEnvelope).GetMethod("CaptureNode", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object?[] { target, budget, ancestors, null, 32 })!;
            }
            else
            {
                var method = profile == "file-format" ? "CaptureFormat" : "CaptureStatement";
                admitted = (bool)envelope!.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(envelope, new object[] { target, 32 })!;
            }
            Assert.That(admitted, Is.False);
            Assert.That(budget.Exhausted, Is.True,
                "Rejecting a cycle still requires funding the actual 32-entry identity bucket before the probe.");
        }

        [Test]
        public void TypeSnapshotCurrentnessDoesNotAllocateAnUnfundedExpectedArgumentCopy()
        {
            const int count = 256;
            var argument = new CSharpType(typeof(int));
            var type = new CSharpType("Many", "Tests", false, false, null,
                Enumerable.Repeat(argument, count).ToArray(), true, false);
            Assert.That(DeclaredTypeSnapshot.TryCaptureForPoc(type, new PocWorkBudget(4096), out var snapshot), Is.True);
            Assert.That(snapshot!.IsCurrentForPoc(new PocWorkBudget(4096)), Is.True);
            for (int i = 0; i < 4; i++) { snapshot.IsCurrentForPoc(new PocWorkBudget(count + 1)); }
            var budget = new PocWorkBudget(count + 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool current = snapshot.IsCurrentForPoc(budget);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(current, Is.False, "This budget can copy the current carrier, but cannot validate every child.");
            Assert.That(allocated, Is.LessThanOrEqualTo(count * IntPtr.Size + 128),
                "Only the funded current carrier may be copied, not a second projected expected-argument array.");
        }

        private sealed record OpaqueArgument : ValueExpression
        {
            internal override void WriteCore(CodeWriter writer) => writer.AppendRaw("0");
        }
    }
}

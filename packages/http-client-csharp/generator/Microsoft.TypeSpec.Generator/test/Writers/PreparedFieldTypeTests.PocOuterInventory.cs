// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PocOuterInventoryRequiresCreditsAfterRealNonemptySharedPreparation(bool zeroCredits)
        {
            using var host = new BodyOnlyPhysicalHost();
            using var prepared = CreateReadyOuterInventory(host, out var owner);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(((Array)OuterField(prepared!, "_providerStates")!).Length, Is.GreaterThan(0));
            Assert.That(((Array)OuterField(prepared!, "_callLookupScopes")!).Length, Is.GreaterThan(0));
            Assert.That(((IDictionary)OuterField(prepared!, "_owners")!).Count, Is.GreaterThan(0));
            Assert.That(((ICollection)OuterField(prepared!, "_pocWriteDecisionNames")!).Count, Is.GreaterThan(0),
                "The actual native policy phase must have recorded its write-decision observations.");
            Assert.That(prepared!.SourceFiles.Length, Is.GreaterThan(0));
            Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
            var field = owner.Fields[0];
            Assert.That(prepared.CanWriteSimpleName(owner, field), Is.True,
                "The actual shared field choice must be viable before this extra POC observation.");
            var capture = typeof(PreparedFieldTypes).GetMethod("CapturePocProviderNames", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var budget = new PocWorkBudget(zeroCredits ? 0 : 262144);
            var captured = (bool)capture.Invoke(prepared, new object[] { budget })!;
            Assert.That(captured, Is.EqualTo(!zeroCredits));
            if (zeroCredits)
            {
                Assert.That(OuterField(prepared, "_pocWriteDecisionInventoryInvalid"), Is.True);
                Assert.That(((IDictionary)OuterField(prepared, "_pocRelatedOwnerScopes")!).Count, Is.Zero);
                Assert.That(((IDictionary)OuterField(prepared, "_pocScopeNames")!).Count, Is.Zero);
            }
            else
            {
                Assert.That(budget.Remaining, Is.LessThan(262144));
                var stored = OuterField(prepared, "_pocProviderNames")!;
                Assert.That(stored.GetType().GetProperty("Length")!.GetValue(stored), Is.GreaterThan(0));
                Assert.That(((IDictionary)OuterField(prepared, "_pocScopeNames")!).Count, Is.GreaterThan(0));
                var unsupported = OuterField(prepared, "_pocUnsupportedOwners")!;
                Assert.That(unsupported.GetType().GetProperty("Count")!.GetValue(unsupported), Is.Zero);
            }
            Assert.That(prepared.CanWriteSimpleName(owner, field), Is.True,
                "Additional POC funding must not revoke the existing Task03 field choice.");
            Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
        }

        [Test]
        public void PocOuterInventoryShortAndLongNameBudgetsStayBounded()
        {
            var compact = OuterInventoryCaptureWork("_value", 262144, expected: true);
            Assert.That(compact, Is.GreaterThan(0));
            OuterInventoryCaptureWork("_value", compact - 1, expected: false);
            OuterInventoryCaptureWork("_value", compact, expected: true);
            var longName = new string('N', 2048);
            OuterInventoryCaptureWork(longName, compact + 32, expected: false);
            OuterInventoryCaptureWork(longName, 262144, expected: true);
        }

        [Test]
        public void PocOuterRelatedScopeQueryRequiresFreshCredits()
        {
            using var host = new BodyOnlyPhysicalHost();
            using var prepared = CreateReadyOuterInventory(host, out var owner);
            Assert.That(CaptureOuterInventory(prepared, new PocWorkBudget(262144)), Is.True);
            var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
            var query = typeof(PreparedFieldTypes).GetMethod("PocRelatedScopesAreCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(query.Invoke(prepared, new object[] { file, new PocWorkBudget(262144) }), Is.True);
            Assert.That(query.Invoke(prepared, new object[] { file, new PocWorkBudget(0) }), Is.False,
                "A captured inventory and previous successful point observation do not fund this new group query.");
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            Assert.That(file.HasStarted, Is.False);
        }

        [Test]
        public void PocOuterScopeReservesWholeGroupRejectionBeforeObservingMismatch()
        {
            using var host = new BodyOnlyPhysicalHost();
            using var prepared = CreateReadyOuterInventory(host, out var owner, serializationParts: true);
            Assert.That(CaptureOuterInventory(prepared, new PocWorkBudget(262144)), Is.True);
            var owners = (IDictionary)OuterField(prepared, "_owners")!;
            var group = owners[owner]!;
            var parts = (TypeProvider[])group.GetType().GetProperty("Parts")!.GetValue(group)!;
            Assert.That(parts, Has.Length.EqualTo(3), "Exercise the owner and both actual serialization files, not a singleton mark.");
            var groupFiles = prepared.SourceFiles.Where(file => parts.Any(part => ReferenceEquals(part, file.FileOwner))).ToArray();
            Assert.That(groupFiles, Has.Length.EqualTo(3));
            var unrelated = prepared.SourceFiles.Single(file => !parts.Any(part => ReferenceEquals(part, file.FileOwner)));
            var file = groupFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
            var invalid = OuterField(prepared, "_pocInvalidCandidateFiles")!;
            var contains = invalid.GetType().GetMethod("Contains")!;
            var count = invalid.GetType().GetProperty("Count")!;
            Assert.That(count.GetValue(invalid), Is.Zero);
            var query = typeof(PreparedFieldTypes).GetMethod("PocRelatedScopesAreCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var field = owner.Fields[0];
            var nameProperty = typeof(FieldProvider).GetProperty(nameof(FieldProvider.Name))!;
            var original = field.Name;
            nameProperty.SetValue(field, "ChangedScopeName");
            var sourceCount = ((IDictionary)OuterField(prepared, "_sourceFiles")!).Count;
            var markWork = parts.Length * (sourceCount + parts.Length + 2);
            var shortReservation = new PocWorkBudget(owners.Count + 1 + markWork - 1);
            Assert.That(query.Invoke(prepared, new object[] { file, shortReservation }), Is.False);
            Assert.That(count.GetValue(invalid), Is.Zero,
                "Without the complete rejection reserve, no member mismatch may be observed or partially marked.");
            nameProperty.SetValue(field, original);
            Assert.That(query.Invoke(prepared, new object[] { file, new PocWorkBudget(262144) }), Is.True);
            nameProperty.SetValue(field, "ChangedScopeName");
            Assert.That(query.Invoke(prepared, new object[] { file, new PocWorkBudget(262144) }), Is.False);
            foreach (var member in groupFiles) { Assert.That(contains.Invoke(invalid, new object[] { member }), Is.True); }
            Assert.That(contains.Invoke(invalid, new object[] { unrelated }), Is.False);
            nameProperty.SetValue(field, original);
            foreach (var member in groupFiles) { Assert.That(contains.Invoke(invalid, new object[] { member }), Is.True, "POC rejection stays sticky after restoration."); }
            Assert.That(prepared.CanWriteSimpleName(owner, field), Is.True, "POC marks must not invalidate shared field choices.");
            Assert.That(prepared.SourceFiles.All(source => !source.HasStarted && !source.IsInvalid), Is.True);
        }

        private static int OuterInventoryCaptureWork(string fieldName, int credits, bool expected)
        {
            using var host = new BodyOnlyPhysicalHost();
            using var prepared = CreateReadyOuterInventory(host, out var owner, fieldName);
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            var budget = new PocWorkBudget(credits);
            Assert.That(CaptureOuterInventory(prepared, budget), Is.EqualTo(expected));
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True,
                "POC budget failure must not revoke shared field choices.");
            return credits - budget.Remaining;
        }

        private static bool CaptureOuterInventory(PreparedFieldTypes prepared, PocWorkBudget budget) =>
            (bool)typeof(PreparedFieldTypes).GetMethod("CapturePocProviderNames", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(prepared, new object[] { budget })!;

        private static PreparedFieldTypes CreateReadyOuterInventory(BodyOnlyPhysicalHost host, out FieldOwner owner, string fieldName = "_value", bool serializationParts = false)
        {
            var target = Target();
            owner = new FieldOwner(target.Type, fieldName: fieldName, hasSerialization: serializationParts, twoSerializations: serializationParts);
            // Production reaches the policy boundary after IR materialization;
            // the observation itself must never force a cold CachedType.
            owner.EnsureBuilt();
            target.EnsureBuilt();
            _ = owner.Type;
            foreach (var part in owner.SerializationProviders) { part.EnsureBuilt(); _ = part.Type; }
            Assert.That(owner.CachedType, Is.Not.Null);
            Assert.That(target.CachedType, Is.Not.Null);
            var prepared = PrepareOuterInventoryFixture(new TypeProvider[] { owner, target }, host);
            Assert.That(prepared, Is.Not.Null);
            return prepared!;
        }

        private static object? OuterField(PreparedFieldTypes prepared, string name) =>
            typeof(PreparedFieldTypes).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared);

        private static PreparedFieldTypes? PrepareOuterInventoryFixture(TypeProvider[] roots, BodyOnlyPhysicalHost host)
        {
            // Run native policy/source preparation, including its genuine
            // write-decision observations. Stop before PreparePocOwnership,
            // which is where the first outer name inventory would be captured.
            // Supply actual physical compilation/source inputs rather than an
            // absent host context or a fabricated prepared-session inventory.
            var create = typeof(PreparedFieldTypes).GetMethod("CreateCore", BindingFlags.Static | BindingFlags.NonPublic)!;
            var args = create.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
            args[0] = roots;
            args[1] = host.Snapshot.Compilation;
            args[2] = (Func<TypeProvider, bool>)(_ => true);
            args[3] = host.DirectoryPath;
            args[4] = host.Snapshot;
            args[5] = host.Generator.SourceInputModel;
            args[6] = false;
            args[7] = true;
            return (PreparedFieldTypes?)create.Invoke(null, args);
        }
    }
}

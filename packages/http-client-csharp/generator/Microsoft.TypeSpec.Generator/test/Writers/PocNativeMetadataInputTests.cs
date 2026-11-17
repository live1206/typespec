// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    public class PocNativeMetadataInputTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(PocNativeMetadataInputSession.BootstrapCost - 1)]
        [TestCase(PocNativeMetadataInputSession.SourceLimit + 1)]
        public void NativeLayoutBootstrapRejectsBeforeAllocation(int limit)
        {
            Assert.That(PocNativeMetadataInputSession.TryCreateWithLayout(limit), Is.Null);
        }

        [TestCase(100, 2, 10, 0, 20, 80, true)]
        [TestCase(100, 0, 10, 100, 0, 100, true)]
        [TestCase(100, 0, 10, 100, 99, 1, true)]
        [TestCase(100, 2, 10, -1, 20, 80, false)]
        [TestCase(100, -1, 10, 0, 20, 80, false)]
        [TestCase(100, 2, 0, 0, 20, 80, false)]
        [TestCase(100, 2, -1, 0, 20, 80, false)]
        [TestCase(100, 2, 10, 101, 20, 80, false)]
        [TestCase(100, 2, 10, 81, 20, 80, false)]
        [TestCase(100, 2, 10, 0, -1, 80, false)]
        [TestCase(100, 2, 10, 0, 20, -1, false)]
        [TestCase(100, 2, 10, 0, 101, 1, false)]
        [TestCase(100, 2, 10, 0, 20, 81, false)]
        [TestCase(100, 2, 10, 0, 0, 0, false)]
        [TestCase(-1, 0, 1, 0, 0, 1, false)]
        [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, 0, 0, 1, false)]
        [TestCase(int.MaxValue, 1, int.MaxValue, 0, int.MaxValue - 1, 1, true)]
        public void NativeLayoutRangeChecksUseLongAndRejectEmptyHeaps(int length, int rows, int rowSize,
            int table, int strings, int heapSize, bool accepted)
        {
            Assert.That(PocNativeMetadataLayout.TryCreate(length, rows, System.Reflection.Metadata.MetadataKind.Ecma335,
                rowSize, table, strings, heapSize, out var layout), Is.EqualTo(accepted));
            if (accepted)
            {
                Assert.That(layout.TypeDefRowSize, Is.EqualTo(rowSize));
                Assert.That(layout.TypeDefTableOffset, Is.EqualTo(table));
                Assert.That(layout.StringHeapOffset, Is.EqualTo(strings));
                Assert.That(layout.StringHeapSize, Is.EqualTo(heapSize));
            }
            else { Assert.That(layout, Is.EqualTo(default(PocNativeMetadataLayout))); }
        }

        [TestCase(System.Reflection.Metadata.MetadataKind.WindowsMetadata)]
        [TestCase(System.Reflection.Metadata.MetadataKind.ManagedWindowsMetadata)]
        [TestCase((System.Reflection.Metadata.MetadataKind)int.MaxValue)]
        public void NativeLayoutRejectsOtherProfiles(System.Reflection.Metadata.MetadataKind kind)
        {
            Assert.That(PocNativeMetadataLayout.TryCreate(100, 1, kind, 10, 0, 10, 90, out _), Is.False);
        }

        [TestCase(10, 2)]
        [TestCase(12, 4)]
        [TestCase(16, 2)]
        [TestCase(18, 4)]
        public void NativeLayoutModuleWidthUsesSchemaNotHeapLength(int moduleRowSize, int expectedWidth)
        {
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(100, 1, System.Reflection.Metadata.MetadataKind.Ecma335,
                moduleRowSize, 18, 0, 99, 1, out var layout), Is.True);
            Assert.That(layout.ModuleRowSize, Is.EqualTo(moduleRowSize));
            Assert.That(layout.StringReferenceWidth, Is.EqualTo(expectedWidth), "Even a one-byte heap may use four-byte references.");
            Assert.That(layout.StringHeapSize, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        [TestCase(1)]
        [TestCase(11)]
        [TestCase(14)]
        [TestCase(17)]
        [TestCase(20)]
        [TestCase(int.MaxValue)]
        public void NativeLayoutModuleRejectsUnsupportedSizesBeforePublication(int moduleRowSize)
        {
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(100, 1, System.Reflection.Metadata.MetadataKind.Ecma335,
                moduleRowSize, 18, 0, 99, 1, out var layout), Is.False);
            Assert.That(layout, Is.EqualTo(default(PocNativeMetadataLayout)));
            Assert.That(layout.StringReferenceWidth, Is.Zero);
        }

        [TestCase(System.Reflection.Metadata.MetadataKind.WindowsMetadata)]
        [TestCase(System.Reflection.Metadata.MetadataKind.ManagedWindowsMetadata)]
        [TestCase((System.Reflection.Metadata.MetadataKind)int.MaxValue)]
        public void NativeLayoutModuleRejectsOtherProfiles(System.Reflection.Metadata.MetadataKind kind)
        {
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(100, 1, kind, 12, 18, 0, 99, 1, out var layout), Is.False);
            Assert.That(layout, Is.EqualTo(default(PocNativeMetadataLayout)));
        }

        [Test]
        public void NativeLayoutModulePreservesEveryPureExtentCaseAndUnknownWidth()
        {
            Assert.That(default(PocNativeMetadataLayout).ModuleRowSize, Is.Zero);
            Assert.That(default(PocNativeMetadataLayout).StringReferenceWidth, Is.Zero);
            // Replay the original extent matrix through both APIs; the old helper
            // remains an extent-only control, not a mandatory Module observation.
            var cases = typeof(PocNativeMetadataInputTests).GetMethod(nameof(NativeLayoutRangeChecksUseLongAndRejectEmptyHeaps))!
                .GetCustomAttributes<TestCaseAttribute>().ToArray();
            Assert.That(cases, Has.Length.EqualTo(17));
            foreach (var testCase in cases)
            {
                var args = testCase.Arguments;
                var length = (int)args[0]!;
                var rows = (int)args[1]!;
                var rowSize = (int)args[2]!;
                var table = (int)args[3]!;
                var strings = (int)args[4]!;
                var heapSize = (int)args[5]!;
                var expected = (bool)args[6]!;
                Assert.That(PocNativeMetadataLayout.TryCreate(length, rows, System.Reflection.Metadata.MetadataKind.Ecma335,
                    rowSize, table, strings, heapSize, out var pure), Is.EqualTo(expected));
                Assert.That(pure.ModuleRowSize, Is.Zero);
                Assert.That(pure.StringReferenceWidth, Is.Zero);
                Assert.That(PocNativeMetadataLayout.TryCreateWithModule(length, rows, System.Reflection.Metadata.MetadataKind.Ecma335,
                    12, rowSize, table, strings, heapSize, out var live), Is.EqualTo(expected));
                if (expected)
                {
                    Assert.That(live.ModuleRowSize, Is.EqualTo(12));
                    Assert.That(live.StringReferenceWidth, Is.EqualTo(4));
                    Assert.That(live.Kind, Is.EqualTo(pure.Kind));
                    Assert.That(live.TypeDefRowSize, Is.EqualTo(pure.TypeDefRowSize));
                    Assert.That(live.TypeDefTableOffset, Is.EqualTo(pure.TypeDefTableOffset));
                    Assert.That(live.StringHeapOffset, Is.EqualTo(pure.StringHeapOffset));
                    Assert.That(live.StringHeapSize, Is.EqualTo(pure.StringHeapSize));
                }
                else { Assert.That(live, Is.EqualTo(default(PocNativeMetadataLayout))); }
            }
        }

        [Test]
        public void NativeLayoutStorageDoesNotEnlargeHeaderSlotsOrInactiveOwners()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = typeof(PocNativeMetadataLayout).GetFields(flags);
            Assert.Multiple(() =>
            {
                Assert.That(fields, Has.Length.EqualTo(6));
                Assert.That(System.Runtime.CompilerServices.Unsafe.SizeOf<PocNativeMetadataLayout>(), Is.EqualTo(24));
                Assert.That(PocNativeMetadataInputSession.LayoutReadCost, Is.EqualTo(448));
            });
            Assert.That(fields.All(field => field.IsInitOnly &&
                (field.FieldType == typeof(int) || field.FieldType == typeof(System.Reflection.Metadata.MetadataKind))), Is.True);
            var slot = typeof(PocNativeMetadataInputSession).GetNestedType("Slot", BindingFlags.NonPublic)!;
            Assert.That(slot.GetFields(flags), Has.Length.EqualTo(2));
            Assert.That(slot.GetFields(flags).Single(field => field.Name == "Header").FieldType, Is.EqualTo(typeof(PocNativeMetadataExternalHeader)));
            Assert.That(PocNativeMetadataInputSession.SlotCost, Is.EqualTo(232));
            Assert.That(PocNativeMetadataInputSession.LayoutArrayHeaderCost, Is.EqualTo(112));
            Assert.That(PocNativeMetadataInputSession.LayoutElementCost, Is.EqualTo(112));
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            Assert.That(typeof(PocNativeMetadataInputSession).GetField("_layouts", flags)!.GetValue(session), Is.Null);
            Assert.That(session.TryReadExternalLayout(0, out _), Is.False);
            Assert.That(session.LayoutReadCount, Is.Zero);
            Assert.That(typeof(PreparedFieldTypes).GetFields(flags).Any(field => field.FieldType == typeof(PocNativeMetadataLayout[]) ||
                field.FieldType == typeof(PocNativeMetadataLayout)), Is.False);
            Assert.That(typeof(CodeWriter).GetFields(flags).Any(field => field.FieldType == typeof(PocNativeMetadataLayout[]) ||
                field.FieldType == typeof(PocNativeMetadataLayout)), Is.False);
            using var warm = PocNativeMetadataInputSession.TryCreateWithLayout()!;
            var warmAccess = warm.BeginPreparation();
            warm.EndPreparation(warmAccess, null, null, null);
            var before = GC.GetAllocatedBytesForCurrentThread();
            using var measured = PocNativeMetadataInputSession.TryCreateWithLayout()!;
            var access = measured.BeginPreparation();
            var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            measured.EndPreparation(access, null, null, null);
            Assert.That(allocation, Is.LessThanOrEqualTo(160 + 40), "Session, access and parentless ledger share the original paid allocation buckets.");
            TestContext.Progress.WriteLine($"LAYOUT SETUP ALLOCATION: {allocation}; allowance=200");
        }

        [TestCase(0)]
        [TestCase(PocNativeMetadataInputSession.LayoutArrayHeaderCost - 1)]
        [TestCase(PocNativeMetadataInputSession.LayoutArrayHeaderCost)]
        [TestCase(PocNativeMetadataInputSession.LayoutArrayHeaderCost + 2 * PocNativeMetadataInputSession.LayoutElementCost - 1)]
        [TestCase(PocNativeMetadataInputSession.LayoutArrayHeaderCost + 2 * PocNativeMetadataInputSession.LayoutElementCost)]
        public void NativeLayoutArrayIsFullyPrepaidBeforeInitialization(int funding)
        {
            const int beforeArray = PocNativeMetadataInputSession.BootstrapCost + 2 * PocNativeMetadataInputSession.ReceiptCost +
                8 + PocNativeMetadataInputSession.SlotArrayHeaderCost + 2 * PocNativeMetadataInputSession.SlotCost;
            const int arrayCost = PocNativeMetadataInputSession.LayoutArrayHeaderCost + 2 * PocNativeMetadataInputSession.LayoutElementCost;
            using var session = PocNativeMetadataInputSession.TryCreateWithLayout(beforeArray + funding)!;
            var first = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var second = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("layout-init", references: [first, second]);
            var access = session.BeginPreparation()!;
            var owner = new object();
            var root = new object();
            session.Bind(access, owner, root, compilation);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var layouts = typeof(PocNativeMetadataInputSession).GetField("_layouts", flags)!;
            Assert.That(layouts.GetValue(session) is not null, Is.EqualTo(funding == arrayCost));
            Assert.That(session.Remaining, Is.EqualTo(funding == arrayCost ? 0 : funding));
            Assert.That(session.IsRejected, Is.True, "Even the exact array debit cannot fund the following receipt association.");
            Assert.That(session.HeaderReadCount, Is.Zero);
            Assert.That(session.LayoutReadCount, Is.Zero);
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(layouts.GetValue(session), Is.Null);
        }

        [TestCase("null")]
        [TestCase("foreign")]
        [TestCase("wrong-thread")]
        [TestCase("nested")]
        [TestCase("dispose")]
        public void NativeLayoutRetirementDefersToTheActualOperationOwner(string attack)
        {
            using var session = PocNativeMetadataInputSession.TryCreateWithLayout()!;
            using var other = PocNativeMetadataInputSession.TryCreateWithLayout()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("layout-owner", references: [reference]);
            var access = session.BeginPreparation()!;
            var otherAccess = other.BeginPreparation()!;
            var owner = new object();
            var root = new object();
            session.Bind(access, owner, root, compilation);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var layouts = typeof(PocNativeMetadataInputSession).GetField("_layouts", flags)!;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            var operation = typeof(PocNativeMetadataInputSession).GetField("_operation", flags)!;
            var retained = layouts.GetValue(session);
            Assert.That(retained, Is.Not.Null);
            Assert.That(session.TryReadExternalLayout(0, out _), Is.False, "No publication before seal.");
            var remaining = session.Remaining;
            try
            {
                switch (attack)
                {
                    case "null": session.EndPreparation(null, owner, root, compilation); break;
                    case "foreign": session.EndPreparation(otherAccess, owner, root, compilation); break;
                    case "wrong-thread":
                        Assert.That(System.Threading.Tasks.Task.Run(() => session.EndPreparation(access, owner, root, compilation))
                            .Wait(TimeSpan.FromSeconds(5)), Is.True);
                        break;
                    case "nested":
                        Assert.That(typeof(PocNativeMetadataInputSession).GetMethod("TryUsePreparation", flags)!
                            .Invoke(session, [access]), Is.EqualTo(true));
                        session.EndPreparation(access, owner, root, compilation);
                        Assert.That(operation.GetValue(session), Is.EqualTo(1));
                        break;
                    case "dispose": session.Dispose(); break;
                }
                Assert.That(session.IsRejected, Is.True);
                Assert.That(busy.GetValue(session), Is.EqualTo(1));
                Assert.That(layouts.GetValue(session), Is.SameAs(retained));
                Assert.That(session.Remaining, Is.EqualTo(remaining));
                Assert.That(session.LayoutReadCount, Is.Zero);
            }
            finally
            {
                if (attack == "nested") { operation.SetValue(session, 0); }
                session.EndPreparation(access, owner, root, compilation);
                other.EndPreparation(otherAccess, null, null, null);
            }
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            Assert.That(layouts.GetValue(session), Is.Null);
            Assert.That(session.TryReadExternalLayout(0, out _), Is.False);
        }

        [Test]
        public void NativeHeaderReviewUnownedEndRetiresIdleFactoryReceipts()
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            _ = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var receipts = typeof(PocNativeMetadataInputSession).GetField("_receipts", flags)!;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            Assert.That(receipts.GetValue(session), Is.Not.Null);
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            // Same misuse before any preparation frame, under both the reviewed
            // three-argument API and its capability-bearing replacement.
            var end = typeof(PocNativeMetadataInputSession).GetMethod("EndPreparation", flags)!;
            var arguments = new object?[end.GetParameters().Length];
            end.Invoke(session, arguments);
            Assert.That(session.IsRejected, Is.True);
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            Assert.That(receipts.GetValue(session), Is.Null,
                "An unowned end must retire idle roots while deferring only for an actually live owner.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeHeaderReviewForeignEndCannotReleaseAnActivePreparation(bool matchingIdentities)
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("native-header-foreign-end", references: [reference]);
            var owner = new object();
            var root = new object();
            var access = session.BeginPreparation();
            Assert.That(access, Is.Not.Null);
            session.Bind(access, owner, root, compilation);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            var slots = typeof(PocNativeMetadataInputSession).GetField("_slots", flags)!;
            Assert.That(busy.GetValue(session), Is.EqualTo(1));
            var retainedSlots = slots.GetValue(session);
            Assert.That(retainedSlots, Is.Not.Null);
            var remaining = session.Remaining;
            try
            {
                var foreign = System.Threading.Tasks.Task.Run(() => session.EndPreparation(null,
                    matchingIdentities ? owner : new object(), root, compilation));
                Assert.That(foreign.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(session.IsRejected, Is.True);
                Assert.That(busy.GetValue(session), Is.EqualTo(1),
                    "A foreign end must poison optional collection, not release the active invocation's guard.");
                Assert.That(slots.GetValue(session), Is.SameAs(retainedSlots),
                    "The actual owner's funded operation may still be using these slots before header commit.");
                Assert.That(session.Remaining, Is.EqualTo(remaining), "Foreign protocol misuse must not withdraw from the owner's ledger.");
            }
            finally { session.EndPreparation(access, owner, root, compilation); }
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            Assert.That(slots.GetValue(session), Is.Null);
            Assert.That(session.TryReadExternalHeader(0, out _), Is.False);
        }

        [TestCase("bind", false)]
        [TestCase("bind", true)]
        [TestCase("predicate", false)]
        [TestCase("predicate", true)]
        [TestCase("end", false)]
        [TestCase("end", true)]
        public void NativeHeaderReviewSameThreadIdentitiesCannotSubstituteForAccess(string operation, bool foreignSession)
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            using var other = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("access-proof", references: [reference]);
            var target = compilation.GetSpecialType(SpecialType.System_Object);
            var owner = new object();
            var root = new object();
            var access = session.BeginPreparation()!;
            var foreignAccess = other.BeginPreparation()!;
            session.Bind(access, owner, root, compilation);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var slots = typeof(PocNativeMetadataInputSession).GetField("_slots", flags)!;
            var retained = slots.GetValue(session);
            var remaining = session.Remaining;
            try
            {
                var invalidAccess = foreignSession ? foreignAccess : null;
                switch (operation)
                {
                    case "bind": session.Bind(invalidAccess, owner, root, compilation); break;
                    case "predicate": Assert.That(session.CreateOriginalMatchPredicate(invalidAccess, compilation, target), Is.Null); break;
                    case "end": session.EndPreparation(invalidAccess, owner, root, compilation); break;
                }
                Assert.That(session.IsRejected, Is.True);
                Assert.That(typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!.GetValue(session), Is.EqualTo(1));
                Assert.That(slots.GetValue(session), Is.SameAs(retained));
                Assert.That(session.Remaining, Is.EqualTo(remaining));
                Assert.That(session.MetadataAccessCount, Is.Zero);
                Assert.That(other.IsRejected, Is.False, "A proof from another session is not authority over either ledger.");
            }
            finally
            {
                session.EndPreparation(access, owner, root, compilation);
                other.EndPreparation(foreignAccess, null, null, null);
            }
            Assert.That(slots.GetValue(session), Is.Null);
            Assert.That(typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!.GetValue(session), Is.EqualTo(0));
        }

        [TestCase("bind", false)]
        [TestCase("predicate", false)]
        [TestCase("end", false)]
        [TestCase("invoke", false)]
        [TestCase("bind", true)]
        [TestCase("predicate", true)]
        [TestCase("end", true)]
        [TestCase("invoke", true)]
        public void NativeHeaderReviewValidAccessCannotBeBorrowedByWrongThreadOrNestedOperation(string operation, bool nested)
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("access-execution", references: [reference]);
            var target = compilation.GetSpecialType(SpecialType.System_Object);
            var owner = new object();
            var root = new object();
            var access = session.BeginPreparation()!;
            session.Bind(access, owner, root, compilation);
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, target)!;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            var operationField = typeof(PocNativeMetadataInputSession).GetField("_operation", flags)!;
            var slots = typeof(PocNativeMetadataInputSession).GetField("_slots", flags)!;
            var retained = slots.GetValue(session);
            var remaining = session.Remaining;
            // Claim the real private operation gate to model the dynamic extent
            // of an owner's callback, without a mocked compiler/native getter.
            if (nested)
            {
                Assert.That(typeof(PocNativeMetadataInputSession).GetMethod("TryUsePreparation", flags)!
                    .Invoke(session, [access]), Is.EqualTo(true));
            }
            void Attack()
            {
                switch (operation)
                {
                    case "bind": session.Bind(access, owner, root, compilation); break;
                    case "predicate": Assert.That(session.CreateOriginalMatchPredicate(access, compilation, target), Is.Null); break;
                    case "end": session.EndPreparation(access, owner, root, compilation); break;
                    case "invoke": Assert.That(predicate(reference), Is.True, "Optional rejection cannot change the native Boolean."); break;
                }
            }
            try
            {
                if (nested) { Attack(); }
                else
                {
                    var foreign = System.Threading.Tasks.Task.Run(Attack);
                    Assert.That(foreign.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
                Assert.That(session.IsRejected, Is.True);
                Assert.That(busy.GetValue(session), Is.EqualTo(1));
                Assert.That(operationField.GetValue(session), Is.EqualTo(nested ? 1 : 0), "A loser cannot release the active operation gate.");
                Assert.That(slots.GetValue(session), Is.SameAs(retained));
                Assert.That(session.Remaining, Is.EqualTo(remaining));
                Assert.That(session.NativeMatchCount, Is.Zero);
                Assert.That(session.MetadataAccessCount, Is.Zero);
            }
            finally
            {
                if (nested) { operationField.SetValue(session, 0); }
                session.EndPreparation(access, owner, root, compilation);
            }
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            Assert.That(slots.GetValue(session), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeHeaderReviewRetainedPredicateCannotUseAnEndedOrReguardedFrame(bool reguard)
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("retained-access", references: [reference]);
            var target = compilation.GetSpecialType(SpecialType.System_Object);
            var owner = new object();
            var root = new object();
            var access = session.BeginPreparation()!;
            session.Bind(access, owner, root, compilation);
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, target)!;
            Assert.That(predicate(reference), Is.True);
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(session.TryReadExternalHeader(0, out var copy), Is.True);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            var slots = typeof(PocNativeMetadataInputSession).GetField("_slots", flags)!;
            var retained = slots.GetValue(session);
            var remaining = session.Remaining;
            if (reguard)
            {
                // The same real guard used by the late native factory, held at
                // its entry rather than inserting a factory callback/test hook.
                Assert.That(typeof(PocNativeMetadataInputSession).GetMethod("TryEnter", flags)!.Invoke(session, null), Is.EqualTo(true));
            }
            try
            {
                Assert.That(predicate(reference), Is.True);
                Assert.That(predicate(reference.WithAliases(["private-only"])), Is.False, "Late predicates retain the alias short circuit.");
                Assert.Throws<NullReferenceException>(() => predicate(null!), "The original pre-compiler alias access still throws.");
                session.EndPreparation(access, owner, root, compilation);
                Assert.That(session.IsRejected, Is.True);
                Assert.That(busy.GetValue(session), Is.EqualTo(reguard ? 1 : 0));
                if (reguard) { Assert.That(slots.GetValue(session), Is.SameAs(retained), "A live guard defers foreign cleanup."); }
                else { Assert.That(slots.GetValue(session), Is.Null, "An ended idle frame retires rejected scalar storage."); }
                Assert.That(session.Remaining, Is.EqualTo(remaining));
                Assert.That(session.NativeMatchCount, Is.EqualTo(1));
                Assert.That(session.HeaderReadCount, Is.EqualTo(1));
                Assert.That(session.MetadataAccessCount, Is.EqualTo(1));
                Assert.That(session.TryReadExternalHeader(0, out _), Is.False);
                Assert.That(copy.TypeDef, Is.GreaterThan(0));
                Assert.That(session.BeginPreparation(), Is.Null);
            }
            finally
            {
                if (reguard) { typeof(PocNativeMetadataInputSession).GetMethod("Exit", flags)!.Invoke(session, null); }
            }
        }

        [Test]
        public void NativeHeaderReviewDisposeStillDefersCleanupToActualOwner()
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("deferred-stop", references: [reference]);
            var access = session.BeginPreparation()!;
            var owner = new object();
            var root = new object();
            session.Bind(access, owner, root, compilation);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var slots = typeof(PocNativeMetadataInputSession).GetField("_slots", flags)!;
            var busy = typeof(PocNativeMetadataInputSession).GetField("_busy", flags)!;
            var retained = slots.GetValue(session);
            var remaining = session.Remaining;
            session.Dispose();
            Assert.That(busy.GetValue(session), Is.EqualTo(1));
            Assert.That(slots.GetValue(session), Is.SameAs(retained));
            session.EndPreparation(null, owner, root, compilation);
            Assert.That(busy.GetValue(session), Is.EqualTo(1));
            Assert.That(slots.GetValue(session), Is.SameAs(retained));
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(busy.GetValue(session), Is.EqualTo(0));
            Assert.That(slots.GetValue(session), Is.Null);
            Assert.That(session.Remaining, Is.EqualTo(remaining));
        }

        [Test]
        public void NativeHeaderReviewAccessStorageFitsExistingPrepaidBounds()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sessionFields = typeof(PocNativeMetadataInputSession).GetFields(flags);
            // Historical name retained: the single checked-mode reference uses
            // the last eight bytes of the SAME original paid bucket.
            Assert.That(sessionFields.Count(field => !field.FieldType.IsValueType), Is.EqualTo(9));
            Assert.That(sessionFields.Count(field => field.FieldType == typeof(int)), Is.EqualTo(11));
            Assert.That(sessionFields.Count(field => field.FieldType == typeof(bool)), Is.EqualTo(1));
            Assert.That(sessionFields, Has.Length.EqualTo(21));
            Assert.That(((16 + 8 * 8 + 11 * 4 + 1 + 7) / 8) * 8 + 24, Is.EqualTo(152), "Original shape anchor.");
            Assert.That(((16 + 9 * 8 + 11 * 4 + 1 + 7) / 8) * 8 + 24, Is.EqualTo(160));
            Assert.That(160, Is.LessThanOrEqualTo(160));
            Assert.That(typeof(PocNativeMetadataInputSession.PreparationAccess).GetFields(flags), Is.Empty);
            Assert.That(typeof(PocNativeMetadataInputSession.PreparationAccess).GetConstructors(flags).Single().IsPrivate, Is.True);
            var context = typeof(PocNativeMetadataInputSession).GetNestedType("OriginalMatchPredicate", BindingFlags.NonPublic)!;
            Assert.That(context.GetFields(flags), Has.Length.EqualTo(4));
            Assert.That(context.GetFields(flags).All(field => !field.FieldType.IsValueType), Is.True);
            Assert.That(16 + 4 * 8, Is.EqualTo(48));
            Assert.That(PocNativeMetadataInputSession.BootstrapCost, Is.EqualTo(864));
            Assert.That(PocNativeMetadataInputSession.PredicateCost, Is.EqualTo(480));
            using var warm = PocNativeMetadataInputSession.TryCreate()!;
            var warmAccess = warm.BeginPreparation();
            warm.EndPreparation(warmAccess, null, null, null);
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var access = session.BeginPreparation();
            var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            session.EndPreparation(access, null, null, null);
            Assert.That(allocation, Is.EqualTo(24), "Successful Begin allocates only its prepaid constant-size access token.");
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(PocNativeMetadataInputSession.BootstrapCost - 1)]
        [TestCase(PocNativeMetadataInputSession.SourceLimit + 1)]
        public void UnfundedBootstrapAllocatesNoOptionalSession(int limit)
        {
            Assert.That(PocNativeMetadataInputSession.TryCreate(limit), Is.Null);
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(null, typeof(object).Assembly.Location);
            Assert.That(reference, Is.InstanceOf<PortableExecutableReference>());
        }

        [TestCase(0)]
        [TestCase(PocNativeMetadataInputSession.ReceiptCost - 1)]
        [TestCase(PocNativeMetadataInputSession.ReceiptCost)]
        public void ReceiptFundingEdgeNeverSuppressesNativeFactory(int funding)
        {
            using var session = PocNativeMetadataInputSession.TryCreate(PocNativeMetadataInputSession.BootstrapCost + funding)!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            Assert.That(reference.FilePath, Is.EqualTo(typeof(object).Assembly.Location));
            Assert.That(session.IsRejected, Is.EqualTo(funding < PocNativeMetadataInputSession.ReceiptCost));
            Assert.That(session.HeaderReadCount, Is.Zero);
            Assert.That(session.Remaining, Is.EqualTo(funding == PocNativeMetadataInputSession.ReceiptCost ? 0 : funding));
            Assert.That(reference.GetType(), Is.EqualTo(MetadataReference.CreateFromFile(typeof(object).Assembly.Location).GetType()),
                "The session returns the ordinary private Roslyn file-factory reference, not a carrier replacement.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("/no-such-typespec-c3-reference.dll")]
        public void NativeFactoryExceptionsKeepNativeTypeMessageAndParameter(string? path)
        {
            var native = Assert.Catch(() => MetadataReference.CreateFromFile(path!))!;
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var observed = Assert.Catch(() => PocNativeMetadataInputSession.CreateReferenceFromFile(session, path!))!;
            Assert.That(observed.GetType(), Is.EqualTo(native.GetType()));
            Assert.That(observed.Message, Is.EqualTo(native.Message));
            Assert.That((observed as ArgumentException)?.ParamName, Is.EqualTo((native as ArgumentException)?.ParamName));
            Assert.That(session.IsRejected, Is.True);
            Assert.That(session.HeaderReadCount, Is.Zero);
            Assert.That(session.BeginPreparation(), Is.Null);
        }

        [Test]
        public void FactoryReferenceRemainsUsableAfterFileRemovalAndPublicCopyDisposal()
        {
            var path = Path.Combine(Path.GetTempPath(), "c3-native-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.Copy(typeof(object).Assembly.Location, path);
                using var session = PocNativeMetadataInputSession.TryCreate()!;
                var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, path);
                File.Delete(path);
                using (var copy = (AssemblyMetadata)reference.GetMetadata())
                {
                    foreach (var module in copy.GetModules()) { module.Dispose(); }
                }
                using var freshCopy = reference.GetMetadata();
                Assert.That(freshCopy, Is.InstanceOf<AssemblyMetadata>());
                session.Dispose();
                using var afterDetach = reference.GetMetadata();
                Assert.That(afterDetach, Is.InstanceOf<AssemblyMetadata>(), "Detachment never disposes factory images.");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void StoppedAndReentrantSessionsCannotRestartButStillRunNativeFactories()
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var access = session.BeginPreparation();
            Assert.That(access, Is.Not.Null);
            var remaining = session.Remaining;
            // A second acquisition loses the single bounded guard and poisons
            // only optional observation, without waiting or touching the ledger.
            Assert.That(session.BeginPreparation(), Is.Null);
            Assert.That(session.IsRejected, Is.True);
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            Assert.That(reference, Is.Not.Null);
            session.EndPreparation(access, null, null, null);
            session.Dispose(); session.Dispose();
            Assert.That(session.BeginPreparation(), Is.Null);
            Assert.That(session.Remaining, Is.EqualTo(remaining));
            Assert.That(session.TryReadExternalHeader(0, out _), Is.False);
        }

        [Test]
        public void ConcurrentAdmissionLosesOneGuardWithoutSpendingOrWaiting()
        {
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            var access = session.BeginPreparation();
            Assert.That(access, Is.Not.Null);
            var remaining = session.Remaining;
            var competing = System.Threading.Tasks.Task.Run(() => session.BeginPreparation());
            Assert.That(competing.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(competing.Result, Is.Null);
            session.EndPreparation(access, null, null, null);
            Assert.That(session.IsRejected, Is.True);
            Assert.That(session.Remaining, Is.EqualTo(remaining));
        }

        [Test]
        public void FactoryRecordsOnlyItsExactReturnedObjectAndSessionsHaveIndependentLedgers()
        {
            using var failed = PocNativeMetadataInputSession.TryCreate(PocNativeMetadataInputSession.BootstrapCost)!;
            using var session = PocNativeMetadataInputSession.TryCreate()!;
            _ = PocNativeMetadataInputSession.CreateReferenceFromFile(failed, typeof(object).Assembly.Location);
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var receipt = typeof(PocNativeMetadataInputSession).GetField("_receipts", flags)!.GetValue(session)!;
            Assert.That(receipt.GetType().GetField("Reference", flags)!.GetValue(receipt), Is.SameAs(reference));
            Assert.That(failed.IsRejected, Is.True);
            Assert.That(session.IsRejected, Is.False);
            Assert.That(session.Remaining, Is.EqualTo(PocNativeMetadataInputSession.SourceLimit -
                PocNativeMetadataInputSession.BootstrapCost - PocNativeMetadataInputSession.ReceiptCost));
        }

        [Test]
        public void NativeOptionalStateUsesOnlyExistingReferenceWidthSlots()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var fields = typeof(PreparedFieldTypes).GetFields(flags);
            // Preserve the original 76-field baseline and the native reference-slot
            // checks below. The explicitly approved development planner adds only
            // these six primitive counters and one request state, not arbitrary fields
            // or reference-width state. Their complete storage/cost proof remains open.
            (string Name, Type Type)[] developmentFields =
            [
                ("Trial2CandidateAttempts", typeof(int)),
                ("Trial2FirstQueries", typeof(int)),
                ("Trial2VisitedOccurrences", typeof(int)),
                ("Trial2InventoryRemaining", typeof(int)),
                ("Trial2RequestsRemaining", typeof(int)),
                ("Trial2FinalRemaining", typeof(int)),
                ("_pocRequestPending", typeof(int))
            ];
            foreach (var expected in developmentFields)
            {
                var field = fields.Single(candidate => candidate.Name == expected.Name);
                Assert.That(field.FieldType, Is.EqualTo(expected.Type), expected.Name);
            }
            var baselineFields = fields.Where(field => !developmentFields.Any(expected => expected.Name == field.Name)).ToArray();
            Assert.That(baselineFields, Has.Length.EqualTo(76));
            Assert.That(fields.Single(field => field.Name == "_pocLookup").FieldType, Is.EqualTo(typeof(object)));
            Assert.That(typeof(PreparedFieldTypes).GetNestedTypes(BindingFlags.NonPublic)
                .Where(type => type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .Any(field => field.FieldType == typeof(PocNativeMetadataInputSession)), Is.False,
                "The observing predicate must not enlarge an inactive resolver closure.");
            Assert.That(fields.Single(field => field.Name == "_pocCandidateSourceBudget").FieldType, Is.EqualTo(typeof(PocWorkBudget)));
            Assert.That(fields.Any(field => field.FieldType == typeof(PocNativeMetadataInputSession)), Is.False);
            Assert.That(typeof(CodeWriter).GetFields(flags).Any(field => field.FieldType == typeof(PocNativeMetadataInputSession)), Is.False);
            Assert.That(typeof(PocNativeMetadataExternalHeader).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .All(field => field.FieldType == typeof(int)), Is.True);
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    public class PocNativeMetadataCheckedInputTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
        private static int PrivateCounter(PocNativeMetadataInputSession session, string name)
        {
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session);
            return state is null ? 0 : (int)state.GetType().GetField(name, Fields)!.GetValue(state)!;
        }
        private static int Setup(int name = 3, int ns = 2) => PocNativeMetadataInputSession.BootstrapCost +
            PocNativeMetadataInputSession.CheckedSetupCost + PocNativeMetadataInputSession.CheckedRequestByteCost * (name + ns);

        private static (byte[] Bytes, PocNativeMetadataLayout Layout) Image(int module = 10, int suffix = 6,
            string name = "Abcdef", string ns = "Ns", uint? nameReference = null, uint? namespaceReference = null)
        {
            var width = module == 10 || module == 16 ? 2 : 4;
            var rowSize = 4 + 2 * width + suffix;
            var heap = Encoding.UTF8.GetBytes("\0" + name + "\0" + ns + "\0");
            var bytes = new byte[rowSize + heap.Length];
            bytes[0] = 255; bytes[1] = 255; bytes[2] = 255; bytes[3] = 255; // flags have no semantic filter.
            var nr = nameReference ?? 1;
            var nsr = namespaceReference ?? (uint)(2 + Encoding.UTF8.GetByteCount(name));
            for (var index = 0; index < width; index++)
            {
                bytes[4 + index] = (byte)(nr >> (8 * index));
                bytes[4 + width + index] = (byte)(nsr >> (8 * index));
            }
            for (var index = 0; index < heap.Length; index++) { bytes[rowSize + index] = heap[index]; }
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(bytes.Length, 1, MetadataKind.Ecma335,
                module, rowSize, 0, rowSize, heap.Length, out var layout), Is.True);
            return (bytes, layout);
        }

        [TestCase(10, 6)] [TestCase(10, 8)] [TestCase(10, 10)] [TestCase(10, 12)]
        [TestCase(12, 6)] [TestCase(12, 8)] [TestCase(12, 10)] [TestCase(12, 12)]
        [TestCase(16, 6)] [TestCase(16, 8)] [TestCase(16, 10)] [TestCase(16, 12)]
        [TestCase(18, 6)] [TestCase(18, 8)] [TestCase(18, 10)] [TestCase(18, 12)]
        public void NativeCheckedManagedBothWidthsAllSuffixesAreRawData(int module, int suffix)
        {
            var image = Image(module, suffix);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), Ascii("Ns"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.RowsVisited, Is.EqualTo(1));
                Assert.That(result.PossibleNamePrefixes, Is.EqualTo(1));
                Assert.That(result.PossibleNameAndNamespacePrefixes, Is.EqualTo(1));
                Assert.That(result.AmbiguousRows, Is.Zero);
                Assert.That(PrivateCounter(session, "FixedBytes"), Is.EqualTo(4 + 2 * image.Layout.StringReferenceWidth));
                Assert.That(PrivateCounter(session, "HeapBytes"), Is.EqualTo(5));
                Assert.That(session.CheckedRowsVisited, Is.Zero, "No progressive scalar diagnostic publication.");
                Assert.That(session.CheckedFixedByteReadCount, Is.Zero);
                Assert.That(session.CheckedHeapByteReadCount, Is.Zero);
                Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False, "Managed facade grants no publication authority.");
            });
        }

        [TestCase(0)] [TestCase(5)] [TestCase(7)] [TestCase(9)] [TestCase(11)] [TestCase(13)] [TestCase(100)]
        public void NativeCheckedRejectsUnsupportedTypeDefSuffix(int suffix)
        {
            var image = Image(suffix: suffix);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.False);
            Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
            Assert.That(session.CheckedFixedByteReadCount, Is.Zero);
            Assert.That(session.IsRejected, Is.True);
        }

        [TestCase(0u, true, 0)] [TestCase(1u, true, 1)] [TestCase(11u, false, 0)]
        [TestCase(0x80000000u, false, 0)] [TestCase(0xffffffffu, false, 0)]
        public void NativeCheckedUnsignedHeapReferencesNeverBecomeHandles(uint reference, bool accepted, int names)
        {
            var image = Image(12, nameReference: reference);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.EqualTo(accepted));
            Assert.That(result.PossibleNamePrefixes, Is.EqualTo(names));
            if (!accepted) { Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs))); }
        }

        [TestCase(0x80000000u)] [TestCase(0xffffffffu)] [TestCase(11u)]
        public void NativeCheckedInvalidNamespaceOffsetRejectsEvenWithWildcardAndNameNo(uint reference)
        {
            var image = Image(12, name: "Xbcdef", namespaceReference: reference);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out _), Is.False);
        }

        [TestCase("Abc", "Ns", true, 1, 1, 5)]
        [TestCase("Abcdef", "NsLong", true, 1, 1, 5)]
        [TestCase("Abc`1", "Ns", true, 1, 1, 5)]
        [TestCase("Ab", "Ns", true, 0, 0, 3)]
        [TestCase("X", "Ns", true, 0, 0, 1)]
        [TestCase("Abc", "Nx", true, 1, 0, 5)]
        [TestCase("Abé", "Ns", false, 0, 0, 0)]
        [TestCase("Abc", "Né", false, 0, 0, 0)]
        [TestCase("X", "Né", true, 0, 0, 1)]
        [TestCase("Xé", "Ns", true, 0, 0, 1)]
        public void NativeCheckedNeededBytesOnlyAndWholeRejectionAmbiguityPolicy(string name, string ns,
            bool accepted, int names, int namespaces, int loads)
        {
            var image = Image(name: name, ns: ns);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), Ascii("Ns"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.EqualTo(accepted));
            Assert.That(result.PossibleNamePrefixes, Is.EqualTo(names));
            Assert.That(result.PossibleNameAndNamespacePrefixes, Is.EqualTo(namespaces));
            if (accepted) { Assert.That(PrivateCounter(session, "HeapBytes"), Is.EqualTo(loads)); }
            else { Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs))); }
        }

        [TestCase(false)] [TestCase(true)]
        public void NativeCheckedNamespaceAbsentOrEmptyIsWildcardWithoutHeapReads(bool empty)
        {
            var image = Image(ns: "é");
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), empty ? [] : null)!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.True);
            Assert.That(result.PossibleNameAndNamespacePrefixes, Is.EqualTo(1));
            Assert.That(PrivateCounter(session, "HeapBytes"), Is.EqualTo(3));
        }

        [TestCase(1, false)] [TestCase(2, false)] [TestCase(3, true)]
        public void NativeCheckedNeededUnterminatedHeapEndRejectsButCompletePrefixDoesNotReadSuffix(int heapBytes, bool accepted)
        {
            const int row = 14;
            var bytes = new byte[row + heapBytes];
            bytes[4] = 0;
            for (var index = 0; index < heapBytes; index++) { bytes[row + index] = (byte)(65 + index); }
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(bytes.Length, 1, MetadataKind.Ecma335, 10,
                row, 0, row, heapBytes, out var layout), Is.True);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("ABC"))!;
            Assert.That(session.TryScanCheckedManagedForTest(bytes, 1, layout, out var result), Is.EqualTo(accepted));
            if (accepted) { Assert.That(result.PossibleNamePrefixes, Is.EqualTo(1)); }
            else { Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs))); }
        }

        [TestCase(-1)] [TestCase(int.MaxValue)]
        public void NativeCheckedNegativeAndProductMaxRowsRejectBeforeLoad(int rows)
        {
            var image = Image();
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, rows, image.Layout, out _), Is.False);
            Assert.That(session.CheckedFixedByteReadCount, Is.Zero);
        }

        [Test]
        public void NativeCheckedTruncatedArrayAndUnknownModuleRejectBeforeLoad()
        {
            var image = Image();
            using var shortSession = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(shortSession.TryScanCheckedManagedForTest(new byte[image.Bytes.Length - 1], 1, image.Layout, out _), Is.False);
            Assert.That(PocNativeMetadataLayout.TryCreate(image.Bytes.Length, 1, MetadataKind.Ecma335,
                14, 0, 14, image.Bytes.Length - 14, out var unknown), Is.True);
            using var unknownSession = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(unknownSession.TryScanCheckedManagedForTest(image.Bytes, 1, unknown, out _), Is.False);
            using var emptySession = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(emptySession.TryScanCheckedManagedForTest([], 0, default, out _), Is.False);
        }

        [Test]
        public void NativeCheckedZeroRowsIsCompleteEmptyRawObservation()
        {
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(1, 0, MetadataKind.Ecma335, 10, 14,
                0, 0, 1, out var layout), Is.True);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            Assert.That(session.TryScanCheckedManagedForTest([0], 0, layout, out var result), Is.True);
            Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
            Assert.That(session.CheckedHeapByteReadCount, Is.Zero);
        }

        [TestCase(0UL, 1, false)] [TestCase(1UL, 0, false)] [TestCase(1UL, -1, false)]
        [TestCase(1UL, int.MaxValue, true)] [TestCase(ulong.MaxValue, 1, false)]
        [TestCase(ulong.MaxValue - 1, 1, true)] [TestCase(ulong.MaxValue - 1, 2, false)]
        public void NativeCheckedNativeAddressWrapGateHasNoPointerInput(ulong address, int length, bool fits)
        {
            Assert.That(IntPtr.Size, Is.EqualTo(8));
            Assert.That(PocNativeMetadataInputSession.CheckedAddressFits((nuint)address, length), Is.EqualTo(fits));
        }

        [TestCase(0)] [TestCase(128)] [TestCase(255)]
        public void NativeCheckedInvalidOwnedAsciiRequestRejectsBeforeNativeAccess(int value)
        {
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs([(byte)value]), Is.Null);
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs([65], [(byte)value]), Is.Null);
        }

        [TestCase(0)] [TestCase(-1)] [TestCase(PocNativeMetadataInputSession.SourceLimit + 1)]
        public void NativeCheckedRequestSetupLimitGuards(int limit)
        {
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), limit: limit), Is.Null);
        }

        [Test]
        public void NativeCheckedSnapshotMutationAndSetupPrepayment()
        {
            var name = Ascii("Abc"); var ns = Ascii("Ns");
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(name, ns, Setup() - 1), Is.Null);
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(null!), Is.Null);
            Assert.That(PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs([]), Is.Null);
            using var exact = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(name, ns, Setup())!;
            Assert.That(exact.Remaining, Is.Zero);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(name, ns)!;
            name[0] = 255; ns[0] = 0;
            var image = Image();
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.True);
            Assert.That(result.PossibleNameAndNamespacePrefixes, Is.EqualTo(1), "Private validated snapshot, not caller-array currentness.");
        }

        [TestCase(0, 0, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost - 1, 0, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost - 1, 0, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost, 0, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost + PocNativeMetadataInputSession.CheckedHeapByteCost - 1, 0, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost + PocNativeMetadataInputSession.CheckedHeapByteCost, 1, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost + 5 * PocNativeMetadataInputSession.CheckedHeapByteCost - 1, 4, false)]
        [TestCase(PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedRowCost + 5 * PocNativeMetadataInputSession.CheckedHeapByteCost, 5, true)]
        public void NativeCheckedRowAndEachHeapByteArePrepaid(int funding, int heapLoads, bool accepted)
        {
            var image = Image();
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), Ascii("Ns"), Setup() + funding)!;
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.EqualTo(accepted));
            Assert.That(state.GetType().GetField("HeapBytes", Fields)!.GetValue(state), Is.EqualTo(heapLoads));
            if (!accepted)
            {
                Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
                Assert.That(typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session), Is.Null);
                Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            }
        }

        [TestCase("Rows")] [TestCase("FixedBytes")] [TestCase("HeapBytes")]
        public void NativeCheckedDiagnosticCounterBoundsRejectBeforeAnyLoad(string counter)
        {
            var image = Image();
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            state.GetType().GetField(counter, Fields)!.SetValue(state, int.MaxValue);
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.False);
            Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
        }

        [TestCase("null")] [TestCase("foreign")] [TestCase("thread")] [TestCase("nested")] [TestCase("dispose")]
        public void NativeCheckedBusyOwnerDefersStateCleanupAndNoPartialResults(string attack)
        {
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            using var other = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-busy", references: [reference]);
            var owner = new object(); var root = new object();
            var access = session.BeginPreparation()!; var otherAccess = other.BeginPreparation()!;
            session.Bind(access, owner, root, compilation);
            var field = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!;
            var retained = field.GetValue(session);
            var remaining = session.Remaining;
            var operation = typeof(PocNativeMetadataInputSession).GetField("_operation", Fields)!;
            if (attack == "nested") { operation.SetValue(session, 1); }
            try
            {
                switch (attack)
                {
                    case "null": session.EndPreparation(null, owner, root, compilation); break;
                    case "foreign": session.EndPreparation(otherAccess, owner, root, compilation); break;
                    case "thread": Assert.That(System.Threading.Tasks.Task.Run(() => session.EndPreparation(access, owner, root, compilation)).Wait(TimeSpan.FromSeconds(5)), Is.True); break;
                    case "nested": session.EndPreparation(access, owner, root, compilation); break;
                    case "dispose": session.Dispose(); break;
                }
                Assert.That(session.IsRejected, Is.True);
                Assert.That(field.GetValue(session), Is.SameAs(retained));
                Assert.That(session.Remaining, Is.EqualTo(remaining));
                Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            }
            finally
            {
                operation.SetValue(session, 0);
                session.EndPreparation(access, owner, root, compilation);
                other.EndPreparation(otherAccess, null, null, null);
            }
            Assert.That(field.GetValue(session), Is.Null);
            Assert.That(session.BeginPreparation(), Is.Null);
        }

        [Test]
        public void NativeCheckedNativeBudgetVetoKeepsOriginalBooleanAliasAndExceptionOrder()
        {
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-native", references: [reference]);
            var access = session.BeginPreparation()!; var owner = new object(); var root = new object();
            session.Bind(access, owner, root, compilation);
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, compilation.GetSpecialType(SpecialType.System_Object))!;
            Assert.That(predicate(reference.WithAliases(["private"])), Is.False);
            Assert.That(session.MetadataAccessCount, Is.Zero);
            Assert.That(predicate(reference), Is.True, "The original result is unchanged even when the paid row reservation rejects.");
            Assert.That(session.IsRejected, Is.True);
            Assert.That(session.HeaderReadCount, Is.EqualTo(1));
            Assert.That(session.LayoutReadCount, Is.EqualTo(1));
            Assert.That(session.CheckedFixedByteReadCount, Is.Zero);
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            Assert.That(predicate(reference), Is.True);
            Assert.Throws<NullReferenceException>(() => predicate(null!));
            Assert.That(PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location), Is.Not.Null);
        }

        [TestCase(0)] [TestCase(PocNativeMetadataInputSession.CheckedArrayHeaderCost + PocNativeMetadataInputSession.CheckedElementCost - 1)]
        [TestCase(PocNativeMetadataInputSession.CheckedArrayHeaderCost + PocNativeMetadataInputSession.CheckedElementCost)]
        public void NativeCheckedCellStorageIsPrepaidBeforeAllocation(int funding)
        {
            var before = Setup(3, 0) + PocNativeMetadataInputSession.ReceiptCost + 8 +
                PocNativeMetadataInputSession.SlotArrayHeaderCost + PocNativeMetadataInputSession.SlotCost +
                PocNativeMetadataInputSession.LayoutArrayHeaderCost + PocNativeMetadataInputSession.LayoutElementCost;
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), limit: before + funding)!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-storage", references: [reference]);
            var access = session.BeginPreparation()!; var owner = new object(); var root = new object();
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            session.Bind(access, owner, root, compilation);
            var exact = funding == PocNativeMetadataInputSession.CheckedArrayHeaderCost + PocNativeMetadataInputSession.CheckedElementCost;
            Assert.That(state.GetType().GetField("Cells", Fields)!.GetValue(state) is not null, Is.EqualTo(exact));
            Assert.That(session.Remaining, Is.EqualTo(exact ? 0 : funding));
            Assert.That(session.IsRejected, Is.True, "No funding for receipt association, even after an exact allocation debit.");
            session.EndPreparation(access, owner, root, compilation);
        }

        private static (PocNativeMetadataInputSession Session, PocNativeMetadataInputSession.PreparationAccess Access,
            object Owner, object Root, CSharpCompilation Compilation, Func<MetadataReference, bool> Predicate,
            PortableExecutableReference Reference) SmallNative()
        {
            var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("InputType"), Ascii("Microsoft.TypeSpec.Generator.Input"))!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(Input.InputType).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-small-native", references: [reference]);
            var access = session.BeginPreparation()!; var owner = new object(); var root = new object();
            session.Bind(access, owner, root, compilation);
            var target = compilation.GetTypeByMetadataName("Microsoft.TypeSpec.Generator.Input.InputType")!;
            Assert.That(target, Is.Not.Null);
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, target)!;
            Assert.That(predicate(reference), Is.True);
            Assert.That(session.IsRejected, Is.False);
            Assert.That(PrivateCounter(session, "Rows"), Is.GreaterThan(0));
            Assert.That(session.CheckedRowsVisited, Is.Zero);
            Assert.That(session.CheckedFixedByteReadCount, Is.Zero);
            Assert.That(session.CheckedHeapByteReadCount, Is.Zero);
            return (session, access, owner, root, compilation, predicate, reference);
        }

        [TestCase(0)] [TestCase(31)] [TestCase(32)] [TestCase(127)] [TestCase(128)]
        public void NativeCheckedSealPrepaymentAndNoPartialPublication(int funding)
        {
            var native = SmallNative();
            using var session = native.Session;
            Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            var budget = session.SourceBudget;
            typeof(PocWorkBudget).GetField("<Remaining>k__BackingField", Fields)!.SetValue(budget, funding);
            session.EndPreparation(native.Access, native.Owner, native.Root, native.Compilation);
            Assert.That(session.TryReadCheckedNameInputs(0, out var result), Is.EqualTo(funding == 128));
            if (funding == 128)
            {
                Assert.That(result.RowsVisited, Is.GreaterThan(0));
                Assert.That(result.PossibleNameAndNamespacePrefixes, Is.GreaterThan(0));
                Assert.That(session.Remaining, Is.Zero);
                var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
                foreach (var name in new[] { "Name", "Namespace", "RecordedOwner" })
                { Assert.That(state.GetType().GetField(name, Fields)!.GetValue(state), Is.Null); }
                session.Dispose();
                Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
                Assert.That(result.RowsVisited, Is.GreaterThan(0), "Already copied integers survive as data.");
            }
            else { Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs))); }
        }

        [TestCase(false)] [TestCase(true)]
        public void NativeCheckedCompleteSmallNativeScanKeepsFactoryRootAliveAndLateAccessRetires(bool disposeCopy)
        {
            var native = SmallNative();
            using var session = native.Session;
            if (disposeCopy)
            {
                using var copy = (AssemblyMetadata)native.Reference.GetMetadata();
                foreach (var module in copy.GetModules()) { module.Dispose(); }
            }
            Assert.That(native.Predicate(native.Reference), Is.True);
            Assert.That(session.HeaderReadCount, Is.EqualTo(1));
            session.EndPreparation(native.Access, native.Owner, native.Root, native.Compilation);
            Assert.That(session.TryReadCheckedNameInputs(0, out var result), Is.True);
            Assert.That(result.PossibleNameAndNamespacePrefixes, Is.GreaterThan(0));
            Assert.That(session.CheckedFixedByteReadCount, Is.EqualTo(result.RowsVisited * 8));
            Assert.That(native.Predicate(native.Reference), Is.True);
            Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            Assert.That(session.BeginPreparation(), Is.Null);
            TestContext.Progress.WriteLine($"CHECKED SMALL NATIVE: rows={result.RowsVisited}; names={result.PossibleNamePrefixes}; namespaces={result.PossibleNameAndNamespacePrefixes}");
        }

        [TestCase("_sourceDisposed")] [TestCase("_sharedSnapshotStale")]
        public void NativeCheckedRecordedRetirementGuardsManagedLoadsAndFacadeWithoutCallbacks(string flag)
        {
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            state.GetType().GetField("RecordedOwner", Fields)!.SetValue(state, owner);
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, session);
            typeof(PreparedFieldTypes).GetField("_valid", Fields)!.SetValue(owner, 1);
            typeof(PreparedFieldTypes).GetField(flag, Fields)!.SetValue(owner, flag == "_sourceDisposed" ? (object)true : 1);
            var image = Image();
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.False);
            Assert.That(state.GetType().GetField("FixedBytes", Fields)!.GetValue(state), Is.EqualTo(0));
            Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(0, out _), Is.False);
            typeof(PreparedFieldTypes).GetField(flag, Fields)!.SetValue(owner, flag == "_sourceDisposed" ? (object)false : 0);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(0, out _), Is.False, "No resurrection after recorded retirement.");
        }

        [TestCase(false)] [TestCase(true)]
        public void NativeCheckedEveryRowInOrderAndBadLaterRowDropsWholeResult(bool badSecond)
        {
            var one = Image();
            const int row = 14;
            var bytes = new byte[one.Bytes.Length + row];
            for (var index = 0; index < row; index++) { bytes[index] = one.Bytes[index]; bytes[row + index] = one.Bytes[index]; }
            bytes[row + 4] = badSecond ? (byte)255 : (byte)0;
            for (var index = row; index < one.Bytes.Length; index++) { bytes[row + index] = one.Bytes[index]; }
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(bytes.Length, 2, MetadataKind.Ecma335, 10,
                row, 0, 2 * row, one.Layout.StringHeapSize, out var layout), Is.True);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"), Ascii("Ns"))!;
            Assert.That(session.TryScanCheckedManagedForTest(bytes, 2, layout, out var result), Is.EqualTo(!badSecond));
            if (badSecond) { Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs))); }
            else
            {
                Assert.That(result.RowsVisited, Is.EqualTo(2));
                Assert.That(result.PossibleNamePrefixes, Is.EqualTo(1));
                Assert.That(result.PossibleNameAndNamespacePrefixes, Is.EqualTo(1));
            }
        }

        [TestCase("_sourceDisposed")] [TestCase("_sharedSnapshotStale")]
        public void NativeCheckedSealedFacadeRejectsRecordedRetirementAndIntegerCopiesSurvive(string flag)
        {
            var native = SmallNative();
            using var session = native.Session;
            session.EndPreparation(native.Access, native.Owner, native.Root, native.Compilation);
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, session);
            typeof(PreparedFieldTypes).GetField("_valid", Fields)!.SetValue(owner, 1);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(0, out var copy), Is.True);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(-1, out _), Is.False);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(1, out _), Is.False);
            typeof(PreparedFieldTypes).GetField(flag, Fields)!.SetValue(owner, flag == "_sourceDisposed" ? (object)true : 1);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(0, out var stopped), Is.False);
            Assert.That(stopped, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
            Assert.That(copy.RowsVisited, Is.GreaterThan(0));
            typeof(PreparedFieldTypes).GetField(flag, Fields)!.SetValue(owner, flag == "_sourceDisposed" ? (object)false : 0);
            Assert.That(owner.TryReadNativeMetadataCheckedNameInputs(0, out _), Is.False);
        }

        [Test]
        public void NativeCheckedDirectLoadCilUsesNoGenericByrefOrSpanHelper()
        {
            var type = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedByteSource", BindingFlags.NonPublic)!;
            Assert.That(type.IsNestedPrivate && type.IsByRefLike, Is.True);
            Assert.That(type.GetConstructors(Fields).Single(c => c.GetParameters().Any(p => p.ParameterType.IsPointer)).IsPrivate, Is.True);
            var codes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
            var read = type.GetMethod("ReadByte", Fields)!;
            var address = typeof(PocNativeMetadataInputSession).GetMethod("CheckedAddressFits", Fields | BindingFlags.Static)!;
            var pointerLoads = 0; var arrayLoads = 0; var calls = 0;
            foreach (var method in new[] { read, address })
            {
                var il = method.GetMethodBody()!.GetILAsByteArray()!;
                for (var position = 0; position < il.Length;)
                {
                    ushort value = il[position++];
                    if (value == 0xfe) { value = (ushort)(0xfe00 | il[position++]); }
                    var code = codes[value];
                    if (code == System.Reflection.Emit.OpCodes.Ldind_U1) { pointerLoads++; }
                    if (code == System.Reflection.Emit.OpCodes.Ldelem_U1) { arrayLoads++; }
                    var operand = code.OperandType;
                    if (operand == System.Reflection.Emit.OperandType.InlineMethod)
                    {
                        var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, position))!;
                        Assert.That(method, Is.SameAs(read), "Address arithmetic has NO getter/intrinsic/runtime callee.");
                        Assert.That(called.Name, Is.EqualTo("CheckedLive"));
                        calls++;
                    }
                    position += operand switch
                    {
                        System.Reflection.Emit.OperandType.InlineNone => 0,
                        System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or
                            System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                        System.Reflection.Emit.OperandType.InlineVar => 2,
                        System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                        System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
                        _ => 4
                    };
                }
            }
            Assert.That(pointerLoads, Is.EqualTo(1));
            Assert.That(arrayLoads, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1), "The only byte-source callee is the explicit recorded live-bit guard.");
            Assert.That(typeof(PocNativeMetadataInputSession).GetMethods(Fields).Any(m =>
                m.ReturnType.IsPointer || m.ReturnType.IsByRefLike || m.GetParameters().Any(p => p.ParameterType.IsPointer)), Is.False);
            TestContext.Progress.WriteLine($"CHECKED BYTE SOURCE CIL: ldind.u1={pointerLoads}; ldelem.u1={arrayLoads}; ReadByte calls={calls}; AddressFits calls=0");
        }

        // Structural checks deliberately avoid adding a production race callback.
        [TestCase("TryReadExternalLayout", typeof(PocNativeMetadataLayout))]
        [TestCase("TryReadCheckedNameInputs", typeof(PocNativeMetadataCheckedNameInputs))]
        public void NativeMetadataHistoricalOutputPublicationFollowsFinalRetirementGate(string reader, Type resultType)
        {
            var method = typeof(PocNativeMetadataInputSession).GetMethod(reader, Fields)!;
            var il = Instructions(method);
            var gate = Array.FindLastIndex(il, i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                method.Module.ResolveMethod(i.Token)!.Name == "get_IsRejected");
            var publication = Array.FindIndex(il, i => i.Code == System.Reflection.Emit.OpCodes.Stobj &&
                method.Module.ResolveType(i.Token) == resultType);
            Assert.That(gate, Is.GreaterThanOrEqualTo(0));
            Assert.That(publication, Is.GreaterThanOrEqualTo(0));
            Assert.That(gate, Is.LessThan(publication),
                "An observed final rejection must return false without publishing a non-default output.");
            Assert.That(il.Skip(gate + 1).Take(publication - gate - 1)
                .Any(i => i.Code.FlowControl == System.Reflection.Emit.FlowControl.Cond_Branch), Is.True,
                "The final retirement gate must branch before output publication.");
        }

        private static (int Offset, System.Reflection.Emit.OpCode Code, int Token)[] Instructions(MethodBase method)
        {
            var codes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
            var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
            var items = new System.Collections.Generic.List<(int, System.Reflection.Emit.OpCode, int)>();
            for (var index = 0; index < bytes.Length;)
            {
                var offset = index;
                ushort value = bytes[index++];
                if (value == 0xfe) { value = (ushort)(0xfe00 | bytes[index++]); }
                var code = codes[value];
                var size = code.OperandType switch
                {
                    System.Reflection.Emit.OperandType.InlineNone => 0,
                    System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or
                        System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                    System.Reflection.Emit.OperandType.InlineVar => 2,
                    System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                    System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, index),
                    _ => 4
                };
                items.Add((offset, code, code.OperandType == System.Reflection.Emit.OperandType.ShortInlineBrTarget ?
                    (sbyte)bytes[index] : code.OperandType == System.Reflection.Emit.OperandType.ShortInlineVar ? bytes[index] :
                    code.OperandType == System.Reflection.Emit.OperandType.InlineVar ? BitConverter.ToUInt16(bytes, index) :
                    size == 4 ? BitConverter.ToInt32(bytes, index) : 0));
                index += size;
            }
            return items.ToArray();
        }

        [TestCase("source", "InvalidateSourceFiles", "_sourceDisposed")]
        [TestCase("shared", "IsSnapshotCurrent", "_sharedSnapshotStale")]
        [TestCase("dispose", "Dispose", "_sharedSnapshotStale")]
        public void NativeCheckedFencePrecedesEveryRecordedFlagPublication(string path, string methodName, string flag)
        {
            var method = typeof(PreparedFieldTypes).GetMethod(methodName, Fields)!;
            var il = Instructions(method);
            var reject = Array.FindIndex(il, i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                method.Module.ResolveMethod(i.Token)!.Name == "RejectNativeMetadataInputs");
            var publication = Array.FindLastIndex(il, i => i.Code == System.Reflection.Emit.OpCodes.Ldflda &&
                method.Module.ResolveField(i.Token)!.Name == flag);
            Assert.That(reject, Is.GreaterThanOrEqualTo(0));
            Assert.That(reject, Is.LessThan(publication), $"{path}: reject the associated session BEFORE flag publication.");
            if (path == "shared")
            {
                Assert.That(Array.FindIndex(il, i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                    method.Module.ResolveMethod(i.Token)!.Name == "CheckSnapshotCurrent"), Is.LessThan(reject));
            }
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, session);
            var access = session.BeginPreparation()!;
            var remaining = session.Remaining;
            for (var repeat = 0; repeat < 2; repeat++)
            {
                var result = method.Invoke(owner, null);
                if (path == "shared") { Assert.That(result, Is.EqualTo(false)); }
                Assert.That(session.IsRejected, Is.True);
                Assert.That(typeof(PreparedFieldTypes).GetField(flag, Fields)!.GetValue(owner),
                    Is.EqualTo(flag == "_sourceDisposed" ? (object)true : 1));
                Assert.That(session.Remaining, Is.EqualTo(remaining), "Retirement never withdraws from the ledger.");
                Assert.That(typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session), Is.Not.Null,
                    "Busy owner retains paid state until its own end.");
            }
            session.EndPreparation(access, null, null, null);
        }

        [TestCase("exact", true)] [TestCase("wrong", false)] [TestCase("ordinary", false)]
        [TestCase("null", false)] [TestCase("unassociated", false)]
        public void NativeCheckedAssociationIsExactIdentityNotOwnerClass(string association, bool expected)
        {
            var helper = typeof(PreparedFieldTypes).GetMethod("IsNativeMetadataInputSession", Fields);
            Assert.That(helper, Is.Not.Null, "A callback-free exact private-union identity helper is required.");
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            using var wrong = PocNativeMetadataInputSession.TryCreate()!;
            object? union = association switch { "exact" => session, "wrong" => wrong, "ordinary" => new object(), _ => null };
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, union);
            Assert.That(helper!.Invoke(owner, [session]), Is.EqualTo(expected));
            Assert.That(helper.Invoke(owner, [wrong]), Is.EqualTo(association == "wrong"));
        }

        [Test]
        public void NativeCheckedCertifiedDecoderExistsOnlyBehindPrivateCaptureAdmission()
        {
            var source = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedByteSource", BindingFlags.NonPublic)!;
            var decode = source.GetMethod("DecodeFixedPrefix", Fields);
            var heap = source.GetMethod("MatchCertifiedPrefix", Fields);
            Assert.That(decode, Is.Not.Null, "Range-proved unrolled byte decoder is required.");
            Assert.That(heap, Is.Not.Null);
            var ns = source.GetMethod("MatchCertifiedNamespace", Fields)!;
            foreach (var method in new[] { decode!, heap!, ns })
            {
                Assert.That(method.IsPrivate, Is.True);
                var il = Instructions(method);
                foreach (var widened in il.Select((i, index) => (i, index)).Where(x =>
                    x.i.Code.Name!.StartsWith("ldind.") && x.i.Code != System.Reflection.Emit.OpCodes.Ldind_U1))
                {
                    Assert.That(widened.i.Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Ldind_I4));
                    var counterParameter = Array.FindIndex(method.GetParameters(), p => p.Name == "heapBytes") + 1;
                    Assert.That(counterParameter, Is.GreaterThan(0));
                    Assert.That(il[widened.index - 2].Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Ldarg_S));
                    Assert.That(il[widened.index - 2].Token, Is.EqualTo(counterParameter), "Only the int diagnostic byref, NEVER the native image.");
                    Assert.That(il[widened.index - 1].Code == System.Reflection.Emit.OpCodes.Dup ||
                        (il[widened.index - 1].Code == System.Reflection.Emit.OpCodes.Ldarg_S &&
                            il[widened.index - 1].Token == counterParameter), Is.True);
                    Assert.That(il[widened.index + 1].Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Ldc_I4_1));
                    Assert.That(il[widened.index + 2].Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Add));
                    Assert.That(il[widened.index + 3].Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Stind_I4));
                }
                var calls = il.Where(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                    .Select(i => method.Module.ResolveMethod(i.Token)!.Name).ToArray();
                Assert.That(calls.All(n => n is "get_IsRejected" or "Spend" or "get_StringHeapSize" or "get_StringHeapOffset" or
                    "get_StringReferenceWidth" or "get_TypeDefRowSize" or "get_TypeDefTableOffset" or
                    "MatchCertifiedPrefix" or "MatchCertifiedNamespace" or "CheckedLive" or ".ctor"),
                    Is.True, "Only selected byte helpers, full exit guard, result construction and immutable scalar getters.");
                foreach (var getter in new[] { "get_StringHeapSize", "get_StringHeapOffset", "get_StringReferenceWidth", "get_TypeDefRowSize", "get_TypeDefTableOffset" })
                {
                    var scalar = typeof(PocNativeMetadataLayout).GetMethod(getter, Fields)!;
                    var scalarCalls = Instructions(scalar).Where(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                        .Select(i => scalar.Module.ResolveMethod(i.Token)!.Name).ToArray();
                    Assert.That(scalarCalls.All(n => getter == "get_StringReferenceWidth" && n == "get_ModuleRowSize"), Is.True);
                    var moduleSize = typeof(PocNativeMetadataLayout).GetMethod("get_ModuleRowSize", Fields)!;
                    Assert.That(Instructions(moduleSize).Any(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod), Is.False);
                }
                Assert.That(calls.Count(n => n == "get_IsRejected"), Is.EqualTo(il.Count(i => i.Code == System.Reflection.Emit.OpCodes.Ldind_U1)),
                    "Each physical load has its own source poll; no row/word cached authorization.");
            }
            var capture = source.GetMethod("Capture", Fields | BindingFlags.Static)!;
            var captureCalls = Instructions(capture).Where(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                .Select(i => capture.Module.ResolveMethod(i.Token)!.Name).ToArray();
            Assert.That(captureCalls, Does.Contain("OwnsPreparation"));
            Assert.That(captureCalls, Does.Contain("IsNativeMetadataInputSession"));
            Assert.That(captureCalls, Does.Contain("get_NativeMetadataRecordedInputsAreCurrent"));
            var managed = typeof(PocNativeMetadataInputSession).GetMethod("TryScanCheckedManagedForTest", Fields)!;
            Assert.That(Instructions(managed).Where(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                .Select(i => managed.Module.ResolveMethod(i.Token)!.Name), Does.Not.Contain("IsNativeMetadataInputSession"));
        }

        [TestCase("DecodeFixedPrefix")] [TestCase("MatchCertifiedPrefix")] [TestCase("MatchCertifiedNamespace")]
        public void NativeCheckedEveryCertifiedByteIsDominatedByFreshPollAndObservedRejectCannotReachLoad(string name)
        {
            var source = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedByteSource", BindingFlags.NonPublic)!;
            var method = source.GetMethod(name, Fields)!;
            var il = Instructions(method);
            var offsets = il.Select((item, index) => (item.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
            int[] Next(int index)
            {
                var item = il[index];
                if (item.Code.FlowControl is System.Reflection.Emit.FlowControl.Return or System.Reflection.Emit.FlowControl.Throw) { return []; }
                var end = index + 1 < il.Length ? il[index + 1].Offset : method.GetMethodBody()!.GetILAsByteArray()!.Length;
                if (item.Code.FlowControl == System.Reflection.Emit.FlowControl.Branch) { return [offsets[end + item.Token]]; }
                if (item.Code.FlowControl == System.Reflection.Emit.FlowControl.Cond_Branch) { return [index + 1, offsets[end + item.Token]]; }
                return [index + 1];
            }
            int Local((int Offset, System.Reflection.Emit.OpCode Code, int Token) instruction) => instruction.Code.Name switch
            {
                "ldloc.0" or "stloc.0" => 0, "ldloc.1" or "stloc.1" => 1,
                "ldloc.2" or "stloc.2" => 2, "ldloc.3" or "stloc.3" => 3,
                "ldloc" or "stloc" or "ldloc.s" or "stloc.s" => instruction.Token, _ => -1
            };
            var counterLocals = il.Select((i, index) => (i, index)).Where(x =>
                x.i.Code == System.Reflection.Emit.OpCodes.Stfld &&
                method.Module.ResolveField(x.i.Token)!.Name is "FixedBytes" or "HeapBytes")
                .Select(x => Local(il[x.index - 1])).ToHashSet();
            if (name == "DecodeFixedPrefix")
            {
                Assert.That(counterLocals.Count, Is.EqualTo(2));
                Assert.That(method.GetMethodBody()!.ExceptionHandlingClauses.Any(c => c.Flags == ExceptionHandlingClauseOptions.Finally), Is.True);
                Assert.That(il.Count(i => i.Code == System.Reflection.Emit.OpCodes.Stfld), Is.EqualTo(3),
                    "Exactly three final counter writebacks; no session/state reattachment.");
            }
            var work = new System.Collections.Generic.Stack<(int Index, bool Fresh, bool Loaded)>();
            var seen = new System.Collections.Generic.HashSet<(int, bool, bool)>();
            work.Push((0, false, false));
            while (work.TryPop(out var entry))
            {
                if (!seen.Add(entry)) { continue; }
                var item = il[entry.Index]; var fresh = entry.Fresh; var loaded = entry.Loaded;
                if (item.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                    method.Module.ResolveMethod(item.Token)!.Name == "get_IsRejected") { fresh = true; }
                if (item.Code == System.Reflection.Emit.OpCodes.Ldind_U1)
                {
                    Assert.That(fresh, Is.True, $"{name} IL_{item.Offset:x4}: no cached row/word authorization.");
                    Assert.That(loaded, Is.False, "Previous successful byte must already have been counted.");
                    fresh = false; loaded = true;
                }
                var counterIncrement = entry.Index > 0 && il[entry.Index - 1].Code == System.Reflection.Emit.OpCodes.Add &&
                    (item.Code == System.Reflection.Emit.OpCodes.Stind_I4 ||
                        (item.Code.Name!.StartsWith("stloc") && counterLocals.Contains(Local(item))));
                if (counterIncrement)
                {
                    Assert.That(loaded, Is.True, "No count for a budget/currentness/range-refused load.");
                    Assert.That(il[entry.Index - 2].Code, Is.EqualTo(System.Reflection.Emit.OpCodes.Ldc_I4_1), "Exactly one count per successful byte.");
                    loaded = false;
                }
                if (item.Code.FlowControl == System.Reflection.Emit.FlowControl.Return)
                { Assert.That(loaded, Is.False, "Every exit preserves the last successful byte's partial count."); }
                foreach (var next in Next(entry.Index)) { work.Push((next, fresh, loaded)); }
            }
            // Each explicit `if (IsRejected) return false` has a brfalse gate.
            // Exhaustively follow its TRUE (fallthrough) CFG; it cannot reach a
            // physical image load. This is a structural proof, NOT a forced race.
            for (var index = 0; index < il.Length; index++)
            {
                var item = il[index];
                if (item.Code.OperandType != System.Reflection.Emit.OperandType.InlineMethod ||
                    method.Module.ResolveMethod(item.Token)!.Name != "get_IsRejected") { continue; }
                var branch = index + 1;
                while (il[branch].Code.FlowControl != System.Reflection.Emit.FlowControl.Cond_Branch) { branch++; }
                Assert.That(il[branch].Code == System.Reflection.Emit.OpCodes.Brfalse || il[branch].Code == System.Reflection.Emit.OpCodes.Brfalse_S, Is.True);
                var pending = new System.Collections.Generic.Stack<int>(); var visited = new System.Collections.Generic.HashSet<int>();
                pending.Push(branch + 1);
                while (pending.TryPop(out var current))
                {
                    if (!visited.Add(current)) { continue; }
                    Assert.That(il[current].Code, Is.Not.EqualTo(System.Reflection.Emit.OpCodes.Ldind_U1), "Observed rejection has no next byte load.");
                    foreach (var next in Next(current)) { pending.Push(next); }
                }
            }
        }

        [TestCase("exact")] [TestCase("wrong")] [TestCase("ordinary")]
        [TestCase("null")] [TestCase("object")] [TestCase("unassociated")]
        public void NativeCheckedNativeCaptureCertifiesOnlyExactBoundOwnerAndRetainsFallback(string association)
        {
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("InputType"), Ascii("Microsoft.TypeSpec.Generator.Input"))!;
            using var other = PocNativeMetadataInputSession.TryCreate()!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(Input.InputType).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-association", references: [reference]);
            var prepared = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            object? union = association switch
            {
                "exact" => session, "wrong" => other,
                "ordinary" => new PreparedFieldImportLookup(compilation,
                    new System.Collections.Generic.Dictionary<(string, string), TypeProvider[]>(), new System.Collections.Generic.HashSet<string>()),
                _ => null
            };
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(prepared, union);
            object owner = association is "null" or "object" ? new object() : prepared;
            var access = session.BeginPreparation()!; var root = new object();
            session.Bind(access, owner, root, compilation);
            if (association == "null")
            {
                var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
                state.GetType().GetField("RecordedOwner", Fields)!.SetValue(state, null);
            }
            var target = compilation.GetTypeByMetadataName("Microsoft.TypeSpec.Generator.Input.InputType")!;
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, target)!;
            var before = session.Remaining;
            Assert.That(predicate(reference), Is.True);
            Assert.That(session.IsRejected, Is.False);
            var rows = PrivateCounter(session, "Rows"); var heap = PrivateCounter(session, "HeapBytes");
            var rowCost = association == "exact" ? PocNativeMetadataInputSession.CheckedCertifiedRowCost : PocNativeMetadataInputSession.CheckedRowCost;
            var heapCost = association == "exact" ? PocNativeMetadataInputSession.CheckedCertifiedHeapByteCost : PocNativeMetadataInputSession.CheckedHeapByteCost;
            var captureDebit = before - session.Remaining;
            Assert.That(PrivateCounter(session, "FixedBytes"), Is.EqualTo(rows * (association == "exact" ? 4 : 8)),
                "Certified raw observations read both references, not the four discarded flag bytes; fallback still reads flags.");
            Assert.That(session.CheckedRowsVisited, Is.Zero);
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(session.TryReadCheckedNameInputs(0, out var copy), Is.True);
            Assert.That(copy.PossibleNameAndNamespacePrefixes, Is.GreaterThan(0));
            var namespaceEntryCost = (int?)typeof(PocNativeMetadataInputSession)
                .GetField("CheckedCertifiedNamespaceEntryCost", Fields | BindingFlags.Static)?.GetRawConstantValue() ?? 0;
            var namespaceEntries = association == "exact" && namespaceEntryCost != 0 ? copy.PossibleNamePrefixes : 0;
            var separatelyPaidBytes = association == "exact" && namespaceEntryCost != 0 ? heap - rows - namespaceEntries : heap;
            Assert.That(captureDebit, Is.EqualTo(16 + PocNativeMetadataInputSession.HeaderCost +
                PocNativeMetadataInputSession.LayoutReadCost + PocNativeMetadataInputSession.CheckedImageCost +
                PocNativeMetadataInputSession.CheckedCertificateCost + rows * rowCost + separatelyPaidBytes * heapCost +
                namespaceEntries * namespaceEntryCost));
            session.Dispose();
            Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            Assert.That(copy.RowsVisited, Is.EqualTo(rows));
            TestContext.Progress.WriteLine($"FENCE ASSOCIATION {association}: rows={rows}; fixed={PrivateCounter(session, "FixedBytes")}; heap={heap}; rowCost={rowCost}; heapCost={heapCost}");
        }

        [TestCase("_sourceDisposed")] [TestCase("_sharedSnapshotStale")]
        public void NativeCheckedInitialRetiredBitsWithTerminalZeroCannotCertifyOrLoad(string flag)
        {
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("InputType"))!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(Input.InputType).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-initial-retirement", references: [reference]);
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, session);
            typeof(PreparedFieldTypes).GetField(flag, Fields)!.SetValue(owner, flag == "_sourceDisposed" ? (object)true : 1);
            var access = session.BeginPreparation()!; var root = new object();
            session.Bind(access, owner, root, compilation);
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            Assert.That(session.IsRejected, Is.False, "Reflected initial bits must not confer terminal-only privilege.");
            var target = compilation.GetTypeByMetadataName("Microsoft.TypeSpec.Generator.Input.InputType")!;
            Assert.That(session.CreateOriginalMatchPredicate(access, compilation, target)!(reference), Is.True);
            Assert.That(session.IsRejected, Is.True);
            Assert.That(state.GetType().GetField("FixedBytes", Fields)!.GetValue(state), Is.EqualTo(0));
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
        }

        [TestCase(0)] [TestCase(88)]
        public void NativeCheckedEarlyNoAtLastHeapByteDoesNotRequireWholePrefixFit(int last)
        {
            var bytes = new byte[15]; bytes[14] = (byte)last;
            Assert.That(PocNativeMetadataLayout.TryCreateWithModule(15, 1, MetadataKind.Ecma335, 10, 14, 0, 14, 1, out var layout), Is.True);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("ABC"))!;
            Assert.That(session.TryScanCheckedManagedForTest(bytes, 1, layout, out var result), Is.True);
            Assert.That(result.RowsVisited, Is.EqualTo(1));
            Assert.That(result.PossibleNamePrefixes, Is.Zero);
            Assert.That(PrivateCounter(session, "HeapBytes"), Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void NativeCheckedRefRejectionAndNonAsciiKeepExactPrivatePartialCounters(bool nonAscii)
        {
            var image = nonAscii ? Image(12, name: "Abé") : Image(12, namespaceReference: uint.MaxValue);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.False);
            Assert.That(state.GetType().GetField("FixedBytes", Fields)!.GetValue(state), Is.EqualTo(12));
            Assert.That(state.GetType().GetField("Rows", Fields)!.GetValue(state), Is.EqualTo(nonAscii ? 1 : 0));
            Assert.That(state.GetType().GetField("HeapBytes", Fields)!.GetValue(state), Is.EqualTo(nonAscii ? 3 : 0));
            Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
        }

        [TestCase(10, true)] [TestCase(10, false)] [TestCase(12, true)] [TestCase(12, false)]
        public void NativeCheckedReferencesFallbackValidatesBothRefsBeforeHeapAndRetainsFlags(int module, bool badName)
        {
            var image = Image(module, name: "X", nameReference: badName ? 100u : null,
                namespaceReference: badName ? null : 100u);
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii("Abc"))!;
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            Assert.That(session.TryScanCheckedManagedForTest(image.Bytes, 1, image.Layout, out var result), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(state.GetType().GetField("FixedBytes", Fields)!.GetValue(state), Is.EqualTo(4 + 2 * image.Layout.StringReferenceWidth));
                Assert.That(state.GetType().GetField("Rows", Fields)!.GetValue(state), Is.EqualTo(0));
                Assert.That(state.GetType().GetField("HeapBytes", Fields)!.GetValue(state), Is.EqualTo(0));
                Assert.That(result, Is.EqualTo(default(PocNativeMetadataCheckedNameInputs)));
                Assert.That(typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session), Is.Null);
            });
        }

        [Test]
        public void NativeCheckedReferencesCertifiedHasNoDiscardedFlagLoadsOrUnsignedForwarders()
        {
            var source = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedByteSource", BindingFlags.NonPublic)!;
            var driver = source.GetMethod("DecodeFixedPrefix", Fields)!;
            var il = Instructions(driver);
            Assert.That(il.Count(i => i.Code == System.Reflection.Emit.OpCodes.Ldind_U1), Is.EqualTo(11),
                "Eight width4 ref loads, width2's two alternative namespace loads, and ONE funded name-first heap load. No four flags.");
            Assert.That(il.Where(i => i.Code.Name!.StartsWith("ldind.")).All(i => i.Code == System.Reflection.Emit.OpCodes.Ldind_U1), Is.True);
            var calls = il.Where(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                .Select(i => driver.Module.ResolveMethod(i.Token)!.Name).ToArray();
            Assert.That(calls, Does.Not.Contain("ReadUnsigned"));
            Assert.That(calls.Count(n => n == "CheckedLive"), Is.EqualTo(1), "Full post-check remains; no per-row historical flags guard.");
            var fallback = typeof(PocNativeMetadataInputSession).GetMethod("ScanChecked", Fields)!;
            Assert.That(Instructions(fallback).Count(i => i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                fallback.Module.ResolveMethod(i.Token)!.Name == "ReadUnsigned"), Is.EqualTo(3), "Legacy flags + BOTH references remain.");
        }

        [TestCase("rows-short", 0, 0, 0, false)]
        [TestCase("first-funded", 162, 648, 162, true)]
        [TestCase("tail-short", 1, 4, 1, false)]
        [TestCase("one-tail", 1, 4, 2, false)]
        [TestCase("namespace-short", 1, 4, 8, false)]
        // Module namespace byte is funded; the next '<'-name gets its
        // prepaid first byte, then refuses its own unpaid needed tail.
        [TestCase("namespace-first-funded", 2, 8, 10, false)]
        public void NativeCheckedReferencesFirstByteIsRowFundedAndNeededTailAndNamespaceWithdraw(string boundary,
            int expectedRows, int expectedFixed, int expectedHeap, bool accepted)
        {
            int Tariff(string field, int fallback) => (int?)typeof(PocNativeMetadataInputSession)
                .GetField(field, Fields | BindingFlags.Static)?.GetRawConstantValue() ?? fallback;
            var tail = Tariff("CheckedCertifiedHeapByteCost", 160);
            var nsEntry = Tariff("CheckedCertifiedNamespaceEntryCost", 144);
            var name = boundary == "first-funded" || boundary == "rows-short" ? "!" :
                boundary.StartsWith("namespace-", StringComparison.Ordinal) ? "<Module>" : "<Mod";
            using var session = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs(Ascii(name), Ascii("A"))!;
            var reference = PocNativeMetadataInputSession.CreateReferenceFromFile(session, typeof(Input.InputType).Assembly.Location);
            var compilation = CSharpCompilation.Create("checked-prepaid", references: [reference]);
            var owner = (PreparedFieldTypes)Activator.CreateInstance(typeof(PreparedFieldTypes), nonPublic: true)!;
            typeof(PreparedFieldTypes).GetField("_pocLookup", Fields)!.SetValue(owner, session);
            var access = session.BeginPreparation()!; var root = new object();
            session.Bind(access, owner, root, compilation);
            var state = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session)!;
            var target = compilation.GetTypeByMetadataName("Microsoft.TypeSpec.Generator.Input.InputType")!;
            var predicate = session.CreateOriginalMatchPredicate(access, compilation, target)!;
            var rowCost = Tariff("CheckedCertifiedRowCost", 576);
            var extra = boundary switch
            {
                "rows-short" => -1, "tail-short" => tail - 1, "one-tail" => tail,
                "namespace-short" => 7 * tail + nsEntry - 1, "namespace-first-funded" => 7 * tail + nsEntry, _ => 0
            };
            var funding = 16 + PocNativeMetadataInputSession.HeaderCost + PocNativeMetadataInputSession.LayoutReadCost +
                PocNativeMetadataInputSession.CheckedImageCost + PocNativeMetadataInputSession.CheckedCertificateCost + 162 * rowCost + extra;
            var budget = (PocWorkBudget)typeof(PocNativeMetadataInputSession).GetField("_budget", Fields)!.GetValue(session)!;
            Assert.That(budget.TrySpend(session.Remaining - funding), Is.True);
            Assert.That(predicate(reference), Is.True, "Native original match is not raw-scan authority.");
            int Count(string field) => (int)state.GetType().GetField(field, Fields)!.GetValue(state)!;
            TestContext.Progress.WriteLine($"REFERENCES FUNDING {boundary}: rows={Count("Rows")}; fixed={Count("FixedBytes")}; heap={Count("HeapBytes")}; rejected={session.IsRejected}; remaining={session.Remaining}");
            Assert.That(session.IsRejected, Is.EqualTo(!accepted));
            Assert.Multiple(() =>
            {
                Assert.That(Count("Rows"), Is.EqualTo(expectedRows));
                Assert.That(Count("FixedBytes"), Is.EqualTo(expectedFixed));
                Assert.That(Count("HeapBytes"), Is.EqualTo(expectedHeap));
                Assert.That(session.CheckedRowsVisited, Is.Zero);
                Assert.That(session.TryReadCheckedNameInputs(0, out _), Is.False);
            });
            session.EndPreparation(access, owner, root, compilation);
            Assert.That(typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!.GetValue(session), Is.Null,
                "An unfunded seal drops paid local state without resurrection.");
        }

        [Test]
        public void NativeCheckedReferencesPrepaidProtocolHasNoCallerCertificateOrPerRowWithdrawal()
        {
            var source = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedByteSource", BindingFlags.NonPublic)!;
            var driver = source.GetMethod("DecodeFixedPrefix", Fields)!;
            var tail = source.GetMethod("MatchCertifiedPrefix", Fields)!;
            var ns = source.GetMethod("MatchCertifiedNamespace", Fields);
            Assert.That(ns, Is.Not.Null, "Conditional namespace entry must pay before its first byte, independently of the name prepayment.");
            foreach (var method in new[] { driver, tail, ns! })
            {
                Assert.That(method.IsPrivate, Is.True);
                Assert.That(method.GetParameters().Any(p => p.ParameterType == typeof(bool)), Is.False,
                    "No caller-asserted prepaid/certified mode.");
                var il = Instructions(method);
                var spends = il.Select((i, index) => (i, index)).Where(x =>
                    x.i.Code.OperandType == System.Reflection.Emit.OperandType.InlineMethod &&
                    method.Module.ResolveMethod(x.i.Token)!.Name == "Spend").Select(x => x.index).ToArray();
                Assert.That(spends, Has.Length.EqualTo(1));
                Assert.That(spends[0], Is.LessThan(Array.FindIndex(il, i => i.Code == System.Reflection.Emit.OpCodes.Ldind_U1)),
                    "Funding precedes the first physical load, including the row reservation.");
            }
        }

        [Test]
        public void NativeCheckedPhysicalShapePrivateStateAndOrdinaryModes()
        {
            var stateType = typeof(PocNativeMetadataInputSession).GetNestedType("CheckedState", BindingFlags.NonPublic)!;
            Assert.That(stateType.IsNestedPrivate, Is.True);
            Assert.That(stateType.GetFields(Fields).Count(f => !f.FieldType.IsValueType), Is.EqualTo(4));
            Assert.That(stateType.GetFields(Fields).Count(f => f.FieldType == typeof(int)), Is.EqualTo(3));
            Assert.That(((16 + 4 * 8 + 3 * 4 + 7) / 8) * 8, Is.LessThanOrEqualTo(96));
            Assert.That(typeof(PocNativeMetadataCheckedNameInputs).GetFields(Fields), Has.Length.EqualTo(4));
            Assert.That(typeof(PocNativeMetadataCheckedNameInputs).GetFields(Fields).All(f => f.IsInitOnly && f.FieldType == typeof(int)), Is.True);
            Assert.That(typeof(PocNativeMetadataInputSession).GetFields(Fields).Any(f => f.FieldType.IsPointer || f.FieldType.IsByRefLike), Is.False);
            Assert.That(typeof(PreparedFieldTypes).GetFields(Fields).Any(f => f.FieldType == stateType || f.FieldType == typeof(PocNativeMetadataCheckedNameInputs)), Is.False);
            Assert.That(typeof(CodeWriter).GetFields(Fields).Any(f => f.FieldType == stateType || f.FieldType == typeof(PocNativeMetadataCheckedNameInputs)), Is.False);
            using var ordinary = PocNativeMetadataInputSession.TryCreate()!;
            using var layout = PocNativeMetadataInputSession.TryCreateWithLayout()!;
            var field = typeof(PocNativeMetadataInputSession).GetField("_checked", Fields)!;
            Assert.That(field.GetValue(ordinary), Is.Null);
            Assert.That(field.GetValue(layout), Is.Null);
            Assert.That(ordinary.TryReadCheckedNameInputs(0, out _), Is.False);
            Assert.That(layout.TryReadCheckedNameInputs(0, out _), Is.False);
            using var warm = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs([65])!;
            var before = GC.GetAllocatedBytesForCurrentThread();
            using var measured = PocNativeMetadataInputSession.TryCreateWithCheckedNameInputs([65])!;
            var access = measured.BeginPreparation();
            var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            measured.EndPreparation(access, null, null, null);
            // Includes a test caller array32, Session136, ledger40, access24,
            // private state64 and private name32. No marker objects.
            Assert.That(allocation, Is.LessThanOrEqualTo(32 + 200 + 96 + 32));
            TestContext.Progress.WriteLine($"CHECKED SETUP ALLOCATION including caller32: {allocation}");
        }
    }
}

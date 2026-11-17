// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection.Metadata;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal sealed partial class PocNativeMetadataInputSession
    {
        // Source operation bounds, NOT ingress credits. See the checked report
        // for selected-branch counts. Four storage visits pay zero-init, copy,
        // retention and retirement; align byte-array sizes up by seven bytes.
        internal const int CheckedSetupCost = 4 * (96 + 2 * 32) + 160;
        internal const int CheckedRequestByteCost = 40;
        internal const int CheckedArrayHeaderCost = 4 * 24 + 32;
        internal const int CheckedElementCost = 4 * 24 + 32;
        internal const int CheckedImageCost = 512;
        internal const int CheckedRowCost = 1152;
        internal const int CheckedHeapByteCost = 256;
        // Literal selected-body schedules in checked-references/TARIFF-PROOF.md.
        // Failed certificates and managed scans retain the full fallback tariffs.
        // Includes expanded native operation setup / row-reservation Spend /
        // final writebacks as well as minting and existing fence liability.
        // Fixed image512 + capture512 bounds BOTH certified and fallback setup.
        internal const int CheckedCertificateCost = 8 + 48 + 64 + 24 + 32 + 16 + 320;
        private const int CheckedCertifiedReferenceByteCost = 9 + 8 + 3;
        private const int CheckedCertifiedRowControlCost = 144; // Includes name entry + first byte, NO per-row Spend.
        internal const int CheckedCertifiedRowCost = 4 * CheckedCertifiedReferenceByteCost + CheckedCertifiedRowControlCost;
        internal const int CheckedCertifiedWideRowCost = 8 * CheckedCertifiedReferenceByteCost + CheckedCertifiedRowControlCost;
        internal const int CheckedCertifiedHeapByteCost = 64 + 64; // Needed tail only, including its own Spend.
        internal const int CheckedCertifiedNamespaceEntryCost = 64 + 96; // Entry + first byte, including its own Spend.
        internal const int CheckedSealCost = 64;
        internal const int CheckedSealElementCost = 32;

        private sealed class CheckedState
        {
            internal byte[]? Name;
            internal byte[]? Namespace;
            internal CheckedCell[]? Cells;
            internal PreparedFieldTypes? RecordedOwner;
            internal int Rows;
            internal int FixedBytes;
            internal int HeapBytes;
        }

        private struct CheckedCell
        {
            internal PocNativeMetadataCheckedNameInputs Result;
            internal bool Captured;
        }

        // Snapshot key, not atomic caller-array currentness or an occurrence ID.
        // No caller byte[], reader, pointer or compiler occurrence is retained.
        internal static PocNativeMetadataInputSession? TryCreateWithCheckedNameInputs(
            byte[] namePrefix, byte[]? namespacePrefix = null, int limit = SourceLimit)
        {
            if (namePrefix is null || namePrefix.Length == 0 || limit > SourceLimit) { return null; }
            var nameLength = namePrefix.Length;
            var namespaceLength = namespacePrefix is null ? 0 : namespacePrefix.Length;
            long setup = CheckedSetupCost + CheckedRequestByteCost * ((long)nameLength + namespaceLength);
            if (limit < BootstrapCost || setup > limit - BootstrapCost) { return null; }
            // Reserve ALL construction and snapshot work before any allocation.
            var session = new PocNativeMetadataInputSession(limit - BootstrapCost - (int)setup, collectLayout: true);
            var state = new CheckedState();
            session._checked = state;
            state.Name = new byte[nameLength];
            if (namespaceLength != 0) { state.Namespace = new byte[namespaceLength]; }
            for (var index = 0; index < nameLength; index++)
            {
                // Array length is immutable; the one loaded byte is validated
                // then copied, so racing mutations cannot corrupt the snapshot.
                var value = namePrefix[index];
                if (value == 0 || value > 127) { session.Dispose(); return null; }
                state.Name[index] = value;
            }
            for (var index = 0; index < namespaceLength; index++)
            {
                var value = namespacePrefix![index];
                if (value == 0 || value > 127) { session.Dispose(); return null; }
                state.Namespace![index] = value;
            }
            return session;
        }

        // Even diagnostics cannot expose usable progress before complete seal.
        // Tests inspect private paid state, not a production partial-count API.
        internal int CheckedRowsVisited => !IsRejected && System.Threading.Volatile.Read(ref _phase) == 2 ? _checked?.Rows ?? 0 : 0;
        internal int CheckedFixedByteReadCount => !IsRejected && System.Threading.Volatile.Read(ref _phase) == 2 ? _checked?.FixedBytes ?? 0 : 0;
        internal int CheckedHeapByteReadCount => !IsRejected && System.Threading.Volatile.Read(ref _phase) == 2 ? _checked?.HeapBytes ?? 0 : 0;

        private bool CheckedLive() => !IsRejected &&
            (_checked?.RecordedOwner is not { } owner || owner.NativeMetadataRecordedInputsAreCurrent);

        // Integer-only testable address/range/schema gate. No pointer inputs.
        internal static bool CheckedAddressFits(nuint address, int length) =>
            // Native all-ones by integer conversion, not UIntPtr.MaxValue/Size
            // getter binding or any runtime/type query.
            address != 0 && length > 0 && (nuint)length <= unchecked((nuint)(-1)) - address;

        private static bool CheckedShape(int length, int rows, PocNativeMetadataLayout layout)
        {
            var width = layout.StringReferenceWidth;
            var suffix = (long)layout.TypeDefRowSize - (4 + 2 * width);
            return length > 0 && rows >= 0 && layout.Kind == MetadataKind.Ecma335 &&
                (width == 2 || width == 4) && (suffix == 6 || suffix == 8 || suffix == 10 || suffix == 12) &&
                layout.TypeDefTableOffset >= 0 && layout.TypeDefTableOffset <= length &&
                (long)rows * layout.TypeDefRowSize <= (long)length - layout.TypeDefTableOffset &&
                layout.StringHeapOffset >= 0 && layout.StringHeapOffset <= length && layout.StringHeapSize > 0 &&
                (long)layout.StringHeapSize <= (long)length - layout.StringHeapOffset;
        }

        private bool CaptureCheckedInputs(PreparationAccess access, MetadataReader reader,
            PocNativeMetadataExternalHeader header, PocNativeMetadataLayout layout, int ordinal) =>
            CheckedByteSource.Capture(this, access, reader, header, layout, ordinal);

        // Private stack-only source, never stored, returned or passed to a host.
        // The managed facade below drives this SAME scanner without pointer input.
        private readonly ref struct CheckedByteSource
        {
            private readonly unsafe byte* _pointer;
            private readonly byte[]? _managed;
            private readonly int _length;

            private unsafe CheckedByteSource(byte* pointer, int length)
            { _pointer = pointer; _managed = null; _length = length; }

            internal unsafe CheckedByteSource(byte[] bytes)
            { _pointer = null; _managed = bytes; _length = bytes.Length; }

            internal int Length => _length;

            // The containing type is PRIVATE. This is the only pointer-origin
            // operation; its constructor is private even inside that boundary.
            internal static unsafe bool Capture(PocNativeMetadataInputSession session, PreparationAccess access,
                MetadataReader reader, PocNativeMetadataExternalHeader header, PocNativeMetadataLayout layout, int ordinal)
            {
                var state = session._checked;
                if (!session.OwnsPreparation(access) || session._operation != 1 || session._phase != 1 || !session.CheckedLive() ||
                    state?.Cells is not { } cells || (uint)ordinal >= (uint)cells.Length ||
                    !session.Spend(CheckedImageCost) || !CheckedShape(header.MetadataLength, header.TypeDef, layout) ||
                    !session.Spend(CheckedCertificateCost)) { return false; }
                // The ONLY privileged origin. The busy operation keeps state and
                // owner references stable; no flag VALUE is cached. Every legitimate
                // recorded-flag writer rejects this exact associated session first.
                // This is cooperative per-byte polling, NOT an atomic poll/load or
                // equivalence to all old staggered three-flag interleavings.
                var certified = System.Threading.Volatile.Read(ref session._busy) == 1 &&
                    state.RecordedOwner is { } owner && ReferenceEquals(owner, session._owner) &&
                    owner.IsNativeMetadataInputSession(session) && owner.NativeMetadataRecordedInputsAreCurrent && !session.IsRejected;
                // MetadataPointer is exactly Block.Pointer. Only the already
                // initialized privately-owned original-positive manifest enters.
                var pointer = reader.MetadataPointer;
                if (!session.CheckedLive() || !CheckedAddressFits((nuint)pointer, header.MetadataLength)) { return false; }
                var source = new CheckedByteSource(pointer, header.MetadataLength);
                PocNativeMetadataCheckedNameInputs result;
                if (!(certified ? source.DecodeFixedPrefix(session, state, header.TypeDef, layout, out result) :
                    session.ScanChecked(source, header.TypeDef, layout, out result)) || !session.CheckedLive()) { return false; }
                cells[ordinal] = new CheckedCell { Result = result, Captured = true };
                return true;
            }

            // ONLY Capture's exact operation-local certificate can enter this
            // fused native refs-only driver. Shape, whole table/heap extent and
            // pointer nonwrap have already been proved. Flags are never used by
            // these raw observations, so there are NO flag loads on this path.
            private unsafe bool DecodeFixedPrefix(PocNativeMetadataInputSession session, CheckedState state,
                int rows, PocNativeMetadataLayout layout, out PocNativeMetadataCheckedNameInputs result)
            {
                result = default;
                var name = state.Name!;
                var ns = state.Namespace;
                var width = layout.StringReferenceWidth;
                var stride = layout.TypeDefRowSize;
                var position = layout.TypeDefTableOffset;
                var heapOffset = layout.StringHeapOffset;
                var heapSize = layout.StringHeapSize;
                var fixedLimit = int.MaxValue - 2 * width;
                var heapLimit = int.MaxValue - name.Length - (long)(ns?.Length ?? 0);
                // CheckedShape proved nonnegative rows; even int.MaxValue*304
                // fits long. Spend preflights the narrowing/remaining cap.
                var rowWork = checked((long)rows * (width == 4 ? CheckedCertifiedWideRowCost : CheckedCertifiedRowCost));
                if (_managed is not null || !session.Spend(rowWork)) { return false; }
                // Every valid row must attempt name[0]. The reservation includes
                // its entry and first-byte work, without a per-row withdrawal.
                // A malformed/refused row leaves unused liability consumed.
                var rowCount = state.Rows;
                var fixedBytes = state.FixedBytes;
                var heapBytes = state.HeapBytes;
                var names = 0;
                var namespaces = 0;
                try
                {
                    for (var row = 0; row < rows; row++)
                    {
                        if (rowCount == int.MaxValue || fixedBytes > fixedLimit || heapBytes > heapLimit) { return false; }
                        // For every funded row, [position+4, position+4+2S)
                        // lies in its FULL validated TypeDef row, including suffix.
                        var p = _pointer + position;
                        if (session.IsRejected) { return false; }
                        uint nameReference = p[4]; fixedBytes++;
                        if (session.IsRejected) { return false; }
                        nameReference |= (uint)p[5] << 8; fixedBytes++;
                        uint namespaceReference;
                        if (width == 4)
                        {
                            if (session.IsRejected) { return false; }
                            nameReference |= (uint)p[6] << 16; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            nameReference |= (uint)p[7] << 24; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            namespaceReference = p[8]; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            namespaceReference |= (uint)p[9] << 8; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            namespaceReference |= (uint)p[10] << 16; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            namespaceReference |= (uint)p[11] << 24; fixedBytes++;
                        }
                        else
                        {
                            if (session.IsRejected) { return false; }
                            namespaceReference = p[6]; fixedBytes++;
                            if (session.IsRejected) { return false; }
                            namespaceReference |= (uint)p[7] << 8; fixedBytes++;
                        }
                        // Read BOTH complete references in order, THEN validate
                        // BOTH, including bad-name / nameNo / namespace wildcard.
                        if (nameReference >= (uint)heapSize || namespaceReference >= (uint)heapSize) { return false; }
                        rowCount++;
                        var start = _pointer + heapOffset + (int)nameReference;
                        var available = heapSize - (int)nameReference;
                        // Joint validation proves byte zero exists; no whole
                        // prefix-fit precondition. Only this funded name step
                        // omits Spend; every actual load still polls freshly.
                        if (session.IsRejected) { return false; }
                        var value = start[0]; heapBytes++;
                        if (value > 127) { return false; }
                        if (value != 0 && value == name[0])
                        {
                            if (!MatchCertifiedPrefix(session, start, available, name, ref heapBytes, out var possible)) { return false; }
                            if (possible)
                            {
                                names++;
                                if (ns is null) { namespaces++; }
                                else
                                {
                                    if (!MatchCertifiedNamespace(session, namespaceReference, heapOffset, heapSize, ns,
                                        ref heapBytes, out var nsPossible)) { return false; }
                                    if (nsPossible) { namespaces++; }
                                }
                            }
                        }
                        if (row + 1 < rows) { position += stride; }
                    }
                    if (!session.CheckedLive()) { return false; }
                    result = new PocNativeMetadataCheckedNameInputs(rows, names, namespaces, 0);
                    return true;
                }
                finally
                {
                    // Busy operation defers Drop/RecordedOwner clearing. Keep
                    // the strong paid state local; never reattach/recreate it.
                    // Every successful byte is counted locally before any exit.
                    state.Rows = rowCount;
                    state.FixedBytes = fixedBytes;
                    state.HeapBytes = heapBytes;
                }
            }

            // Only the two first-byte matchers below/above call this tail, at
            // index ONE. There is no caller-asserted prepaid mode or byte index.
            private unsafe bool MatchCertifiedPrefix(PocNativeMetadataInputSession session,
                byte* start, int available, byte[] prefix, ref int heapBytes, out bool possible)
            {
                possible = false;
                for (var index = 1; index < prefix.Length; index++)
                {
                    if (!session.Spend(CheckedCertifiedHeapByteCost) || index >= available) { return false; }
                    if (session.IsRejected) { return false; }
                    var value = start[index]; heapBytes++;
                    if (value > 127) { return false; }
                    if (value == 0 || value != prefix[index]) { return true; }
                }
                possible = true;
                return true;
            }

            private unsafe bool MatchCertifiedNamespace(PocNativeMetadataInputSession session, uint reference,
                int heapOffset, int heapSize, byte[] prefix, ref int heapBytes, out bool possible)
            {
                // Name's first-byte reservation NEVER covers this conditional
                // namespace entry. Pay entry+first byte before ANY such work.
                if (!session.Spend(CheckedCertifiedNamespaceEntryCost)) { possible = false; return false; }
                possible = false;
                var start = _pointer + heapOffset + (int)reference;
                var available = heapSize - (int)reference;
                if (session.IsRejected) { return false; }
                var value = start[0]; heapBytes++;
                if (value > 127) { return false; }
                if (value == 0 || value != prefix[0]) { return true; }
                return MatchCertifiedPrefix(session, start, available, prefix, ref heapBytes, out possible);
            }

            internal unsafe bool ReadByte(PocNativeMetadataInputSession session, int position, out byte value)
            {
                value = 0;
                // Bounds BEFORE address addition or either load; recorded bits
                // BEFORE EACH byte. No span/byref generic intrinsic or decoder.
                if (!session.CheckedLive() || (uint)position >= (uint)_length) { return false; }
                if (_managed is { } managed)
                {
                    if ((uint)position >= (uint)managed.Length) { return false; }
                    value = managed[position];
                }
                else { value = _pointer[position]; }
                return true;
            }
        }

        private bool ReadUnsigned(CheckedByteSource source, int position, int width, out uint value)
        {
            value = 0;
            // The validated row extent includes each fixed prefix. Each read
            // independently checks bounds/live bits; shifts are unsigned LE.
            for (var index = 0; index < width; index++)
            {
                if (!source.ReadByte(this, position + index, out var item)) { return false; }
                value |= (uint)item << (8 * index);
                _checked!.FixedBytes++;
            }
            return true;
        }

        private bool MatchPrefix(CheckedByteSource source, PocNativeMetadataLayout layout, uint reference,
            byte[] prefix, out bool possible)
        {
            possible = false;
            // Unknown offset rejects the WHOLE optional scan, never a binder
            // negative. Offset zero is valid and normally points to NUL.
            if (reference >= (uint)layout.StringHeapSize) { return false; }
            var start = (int)reference;
            for (var index = 0; index < prefix.Length; index++)
            {
                // Includes per-byte Spend/ledger guards, currentness, range,
                // load, request comparison, loop and diagnostic counter work.
                if (!Spend(CheckedHeapByteCost) || index >= layout.StringHeapSize - start) { return false; }
                var position = layout.StringHeapOffset + start + index;
                if (!source.ReadByte(this, position, out var value)) { return false; }
                _checked!.HeapBytes++;
                if (value > 127) { return false; } // One conservative policy: whole rejection.
                if (value == 0 || value != prefix[index]) { return true; }
            }
            possible = true; // No suffix, terminator, generic or nested-name scan.
            return true;
        }

        private bool ScanChecked(CheckedByteSource source, int rows, PocNativeMetadataLayout layout,
            out PocNativeMetadataCheckedNameInputs result)
        {
            result = default;
            var state = _checked;
            if (state?.Name is not { } name || !CheckedShape(source.Length, rows, layout) || !CheckedLive() ||
                !Spend((long)rows * CheckedRowCost)) { return false; }
            // Full fixed row cost is prepaid (including all fixed-prefix byte
            // guards, decoding, loops/counters). Heap comparisons pay separately.
            var width = layout.StringReferenceWidth;
            var visited = 0;
            var names = 0;
            var namespaces = 0;
            var position = layout.TypeDefTableOffset;
            for (var row = 0; row < rows; row++)
            {
                if (!CheckedLive() || visited >= rows || state.Rows == int.MaxValue ||
                    state.FixedBytes > int.MaxValue - 12 || state.HeapBytes > int.MaxValue - name.Length - (long)(state.Namespace?.Length ?? 0)) { return false; }
                if (!ReadUnsigned(source, position, 4, out _) ||
                    !ReadUnsigned(source, position + 4, width, out var nameReference) ||
                    !ReadUnsigned(source, position + 4 + width, width, out var namespaceReference)) { return false; }
                // Both offsets are validated even if namespace is a wildcard or
                // name mismatches; neither offset is forwarded to a StringHandle.
                if (nameReference >= (uint)layout.StringHeapSize || namespaceReference >= (uint)layout.StringHeapSize) { return false; }
                visited++;
                state.Rows++;
                if (!MatchPrefix(source, layout, nameReference, name, out var namePossible)) { return false; }
                if (namePossible)
                {
                    if (names >= visited) { return false; }
                    names++;
                    // Namespace guard is second, only for a possible name.
                    if (state.Namespace is not { } ns)
                    { namespaces++; }
                    else
                    {
                        if (!MatchPrefix(source, layout, namespaceReference, ns, out var nsPossible)) { return false; }
                        if (nsPossible) { namespaces++; }
                    }
                }
                // Complete validated long extent keeps this addition <=length;
                // do not step past the last row (including an int.MaxValue end).
                if (row + 1 < rows) { position += layout.TypeDefRowSize; }
            }
            if (!CheckedLive() || names > visited || namespaces > names) { return false; }
            result = new PocNativeMetadataCheckedNameInputs(visited, names, namespaces, 0);
            return true;
        }

        internal bool TryReadCheckedNameInputs(int ordinal, out PocNativeMetadataCheckedNameInputs result)
        {
            result = default;
            if (IsRejected || System.Threading.Volatile.Read(ref _phase) != 2) { return false; }
            var cells = _checked?.Cells;
            if (cells is null || (uint)ordinal >= (uint)cells.Length || !cells[ordinal].Captured) { return false; }
            var copy = cells[ordinal].Result;
            if (IsRejected) { return false; }
            result = copy;
            return true;
        }

        // Testing data only. No raw pointer, native reader/window, callback or
        // user collection can enter the production unsafe custody boundary.
        internal bool TryScanCheckedManagedForTest(byte[] bytes, int rows, PocNativeMetadataLayout layout,
            out PocNativeMetadataCheckedNameInputs result)
        {
            result = default;
            if (!TryEnter()) { return false; }
            try
            {
                if (_phase != 0 || _checked is null || bytes is null || !Spend(CheckedImageCost) ||
                    !ScanChecked(new CheckedByteSource(bytes), rows, layout, out var copy)) { Reject(); return false; }
                result = copy;
                return true;
            }
            finally { Exit(); }
        }
    }
}

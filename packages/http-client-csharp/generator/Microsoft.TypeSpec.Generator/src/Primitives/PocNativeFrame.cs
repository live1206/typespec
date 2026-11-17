// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Standalone mechanical adapter. No production writer uses it and no mask or cached
    // byte view grants authority. A future caller must independently supply genuine final
    // authority on EVERY use, including byte-cache hits.
    internal sealed class PocNativeFrame : IDisposable
    {
        private readonly object _owner;
        private readonly PocWorkBudget _ledger;
        private readonly PocFrameAlternative[] _uses;
        private readonly bool[] _required;
        private readonly ImmutableArray<PreparedNameAlternative> _legacy;
        private readonly CodeFile _qualifiedFallback;
        private readonly object _tombstone = new();
        private object? _tentative;
        private object? _final;
        private int _disposed;

        private PocNativeFrame(CodeFile native, object owner, PocFrameAlternative[] uses, bool[] required, PocWorkBudget ledger)
        {
            Native = native;
            _owner = owner;
            _uses = uses;
            _required = required;
            _ledger = ledger;
            _legacy = native.NativeLegacyAlternatives;
            // Prepaid before metadata validation: shared-invalid fallback never allocates late.
            _qualifiedFallback = native.QualifiedContent is null ? native :
                new CodeFile(native.QualifiedContent, native.Name, sourceConsumption: native.SourceConsumption);
        }

        internal CodeFile Native { get; }
        private int LegacyCount => _legacy.IsDefault ? 0 : _legacy.Length;
        internal long AttemptCost => PocFrameWorkReservations.AttemptCost(LegacyCount, _uses.Length, _required.Length,
            Math.Max(Native.Content.Length, Native.QualifiedContent?.Length ?? 0));

        // Factory/retain/selection/reserve are paid setup operations, NOT free ingress.
        // Their caller must serialize access to its EXISTING ledger (including ancestors).
        internal static bool TryCreate(CodeFile native, object owner, ImmutableArray<PocFrameAlternative> uses,
            ImmutableArray<bool> requiredSlots, PocWorkBudget ledger, out PocNativeFrame? frame)
        {
            frame = null;
            int u = uses.IsDefault ? 0 : uses.Length;
            int k = requiredSlots.IsDefault ? 0 : requiredSlots.Length;
            if (owner is null || !PocFrameWorkReservations.Withdraw(ledger, PocFrameWorkReservations.SetupCost(u, k))) { return false; }
            frame = new PocNativeFrame(native, owner, uses.IsDefault ? Array.Empty<PocFrameAlternative>() : uses.AsSpan().ToArray(),
                requiredSlots.IsDefault ? Array.Empty<bool>() : requiredSlots.AsSpan().ToArray(), ledger);
            return true;
        }

        internal bool TryRetainBase(CodeFile independentlySelected, bool field, bool body, PocWorkBudget ledger,
            out PocFrameSelectionBaseResult? result) => PocFrameSelectionBaseResult.TryCreate(this, independentlySelected, field, body, ledger, out result);

        internal bool TryCreateSelection(ImmutableArray<bool> enabled, PocWorkBudget ledger, out PocFrameSelection? selection) =>
            PocFrameSelection.TryCreate(this, enabled, ledger, out selection);

        internal bool TryReserve(PocWorkBudget ledger, out PocFrameWorkReservations? reservation) =>
            PocFrameWorkReservations.TryCreate(this, ledger, out reservation);

        internal int SlotCount => _required.Length;
        internal bool TryPay(PocWorkBudget ledger, long cost) => ReferenceEquals(ledger, _ledger) &&
            Volatile.Read(ref _disposed) == 0 && PocFrameWorkReservations.Withdraw(ledger, cost);

        private enum Validation { Valid, InvalidPoc, InvalidLegacy, UnsupportedBase }

        private sealed record View(PocFrameSelectionBaseResult Base, PocFrameSelection Selection, CodeFile File);

        // Fixed ingress/deny: only known fields, identity/scalar checks, one ticket exchange,
        // one phase-slot CAS attempt (no waits/retry/allocation). All work after a successful
        // claim or a ready-view observation is covered by the fully withdrawn ticket.
        internal CodeFile RenderMechanicalSelection(PocFrameSelectionBaseResult basis, PocFrameSelection selection,
            PocFramePhase phase, PocFrameWorkReservations? reservation)
        {
            CodeFile fallback = basis.File;
            if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(basis.Frame, this) || !ReferenceEquals(selection.Frame, this) ||
                (phase != PocFramePhase.Tentative && phase != PocFramePhase.Final) || reservation is null ||
                !ReferenceEquals(reservation.Frame, this) || !reservation.Take()) { return fallback; }
            ref object? slot = ref (phase == PocFramePhase.Tentative ? ref _tentative : ref _final);
            object? previous = Volatile.Read(ref slot);
            if (previous is View ready)
            {
                // No authority/freshness certificate is stored or returned by this cache.
                return Validate(basis, selection, out _, out _) == Validation.Valid && SameVector(ready, basis, selection) &&
                    ReferenceEquals(Volatile.Read(ref slot), ready) ? ready.File : fallback;
            }
            if (previous is not null || Interlocked.CompareExchange(ref slot, reservation, null) is not null) { return fallback; }

            var validation = Validate(basis, selection, out var offsets, out int length);
            if (validation != Validation.Valid || length == basis.File.Content.Length)
            {
                Interlocked.CompareExchange(ref slot, null, reservation);
                return validation == Validation.InvalidLegacy ? _qualifiedFallback : fallback;
            }

            CodeFile file;
            ref object? otherSlot = ref (phase == PocFramePhase.Final ? ref _tentative : ref _final);
            object? other = Volatile.Read(ref otherSlot);
            // No waiting/coalescing: deny while the other phase could still materialize.
            // Otherwise simultaneous equal vectors could retain duplicate byte strings.
            if (other is PocFrameWorkReservations || ReferenceEquals(other, _tombstone))
            {
                Interlocked.CompareExchange(ref slot, null, reservation);
                return fallback;
            }
            if (other is View otherView && SameVector(otherView, basis, selection))
            {
                file = otherView.File;
            }
            else
            {
                var state = new RenderState(this, basis.File.Content, selection, offsets!);
                string content = string.Create(length, state, static (destination, data) => data.CopyTo(destination));
                file = new CodeFile(content, Native.Name, sourceConsumption: Native.SourceConsumption);
            }
            var view = new View(basis, selection, file);
            // Exact unique claim only; a release or terminal tombstone cannot be overwritten.
            return ReferenceEquals(Interlocked.CompareExchange(ref slot, view, reservation), reservation) ? file : fallback;
        }

        private bool SameVector(View view, PocFrameSelectionBaseResult basis, PocFrameSelection selection)
        {
            if (!ReferenceEquals(view.Base, basis)) { return false; }
            if (ReferenceEquals(view.Selection, selection)) { return true; }
            for (int i = 0; i < _required.Length; i++)
            {
                if (view.Selection.IsEnabled(i) != selection.IsEnabled(i)) { return false; }
            }
            return true;
        }

        private Validation Validate(PocFrameSelectionBaseResult basis, PocFrameSelection selection, out int[]? offsets, out int length)
        {
            offsets = null;
            length = basis.File.Content.Length;
            string canonical = Native.QualifiedContent ?? Native.Content;
            int previousEnd = 0;
            long primaryLength = canonical.Length;
            long baseLength = canonical.Length;
            for (int i = 0; i < LegacyCount; i++)
            {
                var legacy = _legacy[i];
                if (!InRange(legacy.QualifiedOffset, legacy.QualifiedLength, canonical.Length) || legacy.QualifiedOffset < previousEnd ||
                    string.IsNullOrEmpty(legacy.ShortSpelling) || legacy.ShortSpelling.Length >= legacy.QualifiedLength ||
                    (legacy.Channel != PreparedNameChannel.Field && legacy.Channel != PreparedNameChannel.Body))
                { return Native.QualifiedContent is null ? Validation.UnsupportedBase : Validation.InvalidLegacy; }
                previousEnd = legacy.QualifiedOffset + legacy.QualifiedLength;
                int delta = legacy.ShortSpelling.Length - legacy.QualifiedLength;
                primaryLength += delta;
                if (Allowed(legacy, basis.Field, basis.Body)) { baseLength += delta; }
            }
            // Without a companion, legacy differences cannot be projected honestly.
            if (Native.QualifiedContent is null && LegacyCount != 0) { return Validation.UnsupportedBase; }
            if (primaryLength != Native.Content.Length || !MatchesLegacySelection(Native.Content, true, true, canonical))
            { return LegacyCount == 0 ? Validation.UnsupportedBase : Validation.InvalidLegacy; }
            if (baseLength != basis.File.Content.Length || !MatchesLegacySelection(basis.File.Content, basis.Field, basis.Body, canonical))
            { return Validation.UnsupportedBase; }
            if (selection.Count != _required.Length) { return Validation.InvalidPoc; }

            // Attempt-owned scratch only; nothing mutable is shared between phases/threads.
            var seen = new bool[_required.Length];
            offsets = new int[_uses.Length];
            int legacyIndex = 0;
            int qEnd = 0;
            int pEnd = 0;
            long primaryDelta = 0;
            long selectedDelta = 0;
            long resultLength = baseLength;
            for (int i = 0; i < _uses.Length; i++)
            {
                var use = _uses[i];
                if (!ReferenceEquals(use.Owner, _owner) || !use.Completed || use.NativeOrder != i ||
                    use.SlotId < 0 || use.SlotId >= seen.Length || seen[use.SlotId] ||
                    !InRange(use.QualifiedOffset, use.QualifiedLength, canonical.Length) ||
                    !InRange(use.PrimaryOffset, use.PrimaryLength, Native.Content.Length) ||
                    use.PrimaryLength != use.QualifiedLength || use.QualifiedOffset < qEnd || use.PrimaryOffset < pEnd ||
                    string.IsNullOrEmpty(use.ShortSpelling) || use.ShortSpelling.Length >= use.QualifiedLength)
                { return Validation.InvalidPoc; }
                seen[use.SlotId] = true;
                qEnd = use.QualifiedOffset + use.QualifiedLength;
                pEnd = use.PrimaryOffset + use.PrimaryLength;
                while (legacyIndex < LegacyCount)
                {
                    var legacy = _legacy[legacyIndex];
                    if (legacy.QualifiedOffset + legacy.QualifiedLength > use.QualifiedOffset) { break; }
                    int delta = legacy.ShortSpelling.Length - legacy.QualifiedLength;
                    primaryDelta += delta;
                    if (Allowed(legacy, basis.Field, basis.Body)) { selectedDelta += delta; }
                    legacyIndex++;
                }
                if (legacyIndex < LegacyCount && _legacy[legacyIndex].QualifiedOffset < qEnd) { return Validation.InvalidPoc; }
                if (use.PrimaryOffset != use.QualifiedOffset + primaryDelta) { return Validation.InvalidPoc; }
                long start = use.QualifiedOffset + selectedDelta;
                if (start < 0 || start > int.MaxValue || use.QualifiedLength > baseLength - start) { return Validation.InvalidPoc; }
                offsets[i] = (int)start;
                if (selection.IsEnabled(use.SlotId)) { resultLength += use.ShortSpelling.Length - use.QualifiedLength; }
            }
            for (int i = 0; i < seen.Length; i++)
            {
                if (_required[i] && !seen[i]) { return Validation.InvalidPoc; }
            }
            if (resultLength < 0 || resultLength > int.MaxValue) { return Validation.InvalidPoc; }
            length = (int)resultLength;
            return Validation.Valid;
        }

        private static bool InRange(int start, int count, int length) => start >= 0 && count > 0 && start <= length && count <= length - start;
        private static bool Allowed(PreparedNameAlternative legacy, bool field, bool body) => legacy.Channel == PreparedNameChannel.Field ? field : body;

        // Compare with the numerical legacy projection without constructing another string.
        // Both invocations together examine at most 2*max(primary, qualified) characters.
        private bool MatchesLegacySelection(string actual, bool field, bool body, string canonical)
        {
            int q = 0;
            int a = 0;
            for (int i = 0; i < LegacyCount; i++)
            {
                var legacy = _legacy[i];
                if (!Allowed(legacy, field, body)) { continue; }
                int count = legacy.QualifiedOffset - q;
                if (!Matches(actual, a, canonical.AsSpan(q, count))) { return false; }
                a += count;
                if (!Matches(actual, a, legacy.ShortSpelling.AsSpan())) { return false; }
                a += legacy.ShortSpelling.Length;
                q = legacy.QualifiedOffset + legacy.QualifiedLength;
            }
            return Matches(actual, a, canonical.AsSpan(q)) && actual.Length - a == canonical.Length - q;
        }

        private static bool Matches(string actual, int offset, ReadOnlySpan<char> expected) =>
            offset <= actual.Length && expected.Length <= actual.Length - offset && actual.AsSpan(offset, expected.Length).SequenceEqual(expected);

        private readonly record struct RenderState(PocNativeFrame Frame, string Base, PocFrameSelection Selection, int[] Offsets)
        {
            internal void CopyTo(Span<char> destination)
            {
                int source = 0;
                int target = 0;
                for (int i = 0; i < Frame._uses.Length; i++)
                {
                    var use = Frame._uses[i];
                    if (!Selection.IsEnabled(use.SlotId)) { continue; }
                    int count = Offsets[i] - source;
                    Base.AsSpan(source, count).CopyTo(destination.Slice(target));
                    target += count;
                    use.ShortSpelling.AsSpan().CopyTo(destination.Slice(target));
                    target += use.ShortSpelling.Length;
                    source = Offsets[i] + use.QualifiedLength;
                }
                Base.AsSpan(source).CopyTo(destination.Slice(target));
            }
        }

        // Only fixed atomic/reference work. Scratch is local to attempts; retained arrays
        // need no clear walk. Setup/attempt tariffs prepaid eventual GC/reference cleanup.
        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            Interlocked.Exchange(ref _tentative, _tombstone);
            Interlocked.Exchange(ref _final, _tombstone);
        }
    }
}

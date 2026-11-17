// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal sealed partial class PocNativeMetadataInputSession : IDisposable
    {
        internal const int SourceLimit = 1048576;

        // Conservative x64 object/cell bounds, including headers and alignment.
        // Four byte-visits cover allocation, initialization, retention and eventual
        // retirement. Work units additionally cover fixed control/field operations.
        // No exception/resource-formatting allowance is assumed by these tariffs.
        // The 160-byte session allowance also prepays the single empty access
        // object minted at successful Begin: session <=136 + access <=24 =160.
        // Session: header16 + nine references*8 + eleven ints*4 + bool1,
        // rounded to eight bytes. Optional layout storage is separately prepaid.
        internal const int BootstrapCost = 4 * (160 + 40) + 64;
        internal const int ReceiptCost = 4 * 56 + 24;
        internal const int PredicateCost = 4 * (48 + 64) + 32;
        internal const int SlotArrayHeaderCost = 4 * 24 + 16;
        internal const int SlotCost = 4 * 56 + 8;
        // Two iterator objects (80 each), two public module wrappers (24 each),
        // one nonowning ModuleMetadata copy (64); thirteen scalar reads, wrapper
        // CAS/adapters, iterator operations, commit and all fixed cleanup.
        internal const int HeaderCost = 4 * (2 * 80 + 2 * 24 + 64) + 128;
        internal const int LayoutArrayHeaderCost = 4 * 24 + 16;
        // Descriptor payload is six readonly 4-byte scalars (24), with no
        // reference fields or stored computed width; the original bound fits.
        // Four visits include zero-init, retention, scalar copy and retirement;
        // 16 additional operations cover per-cell setup/commit control.
        internal const int LayoutElementCost = 4 * 24 + 16;
        // Fixed non-null reader / Module / TypeDef / String branches only.
        // Original five getters + caller/range/commit <=300 source operations.
        // Module row size <=60 (guard2 + all53 table arms + field2 + call3).
        // Module guard/default/control <=32; added argument/forwarding/value
        // copies <=32. Total <=424, plus24 slack =448, reserved before getters.
        // Array/seal tariffs still fund the six-scalar storage/copies separately.
        // No row/byte traversal, exception work or ingress allowance is included.
        internal const int LayoutReadCost = 448;

        private readonly PocWorkBudget _budget;
        private Receipt? _receipts;
        private Compilation? _compilation;
        private object? _root;
        private object? _owner;
        private Slot[]? _slots;
        private PocNativeMetadataLayout[]? _layouts;
        private CheckedState? _checked;
        private readonly bool _collectLayout;
        private int _layoutReads;
        private int _receiptCount;
        private int _phase; // 0 factories, 1 preparation, 2 sealed
        private int _terminal;
        private int _busy;
        private int _nativeMatches;
        private int _headerReads;
        private int _moduleChecks;
        private int _metadataAccesses;
        private PreparationAccess? _access;
        private int _preparationThread;
        private int _operation;

        // Identity, not the public owner/root/compiler tuple, is the capability.
        // Its only constructor call is behind successful one-shot admission.
        // The canonical wrapper retains it in its private synchronous frame;
        // neither the prepared owner nor host callbacks receive it.
        internal sealed class PreparationAccess
        {
            private PreparationAccess() { }

            internal static PreparationAccess? Begin(PocNativeMetadataInputSession session)
            {
                if (!session.TryEnter()) { return null; }
                if (session._phase != 0)
                {
                    session.Reject();
                    session.Exit();
                    return null;
                }
                var access = new PreparationAccess();
                session._preparationThread = Environment.CurrentManagedThreadId;
                session._phase = 1;
                Volatile.Write(ref session._access, access);
                return access;
            }
        }

        private sealed class Receipt(Receipt? next)
        {
            internal readonly Receipt? Next = next;
            internal PortableExecutableReference? Reference;
            internal IAssemblySymbol? Assembly;
            internal int Ordinal = -1;
        }

        private struct Slot
        {
            internal PocNativeMetadataExternalHeader Header;
            internal bool Captured;
        }

        private PocNativeMetadataInputSession(int remaining, bool collectLayout = false)
        {
            _budget = new PocWorkBudget(remaining);
            _collectLayout = collectLayout;
        }

        internal static PocNativeMetadataInputSession? TryCreate(int limit = SourceLimit)
        {
            // Fixed ingress only. In particular zero/short/oversized budgets do
            // not allocate a session or ledger, nor acquire an arbitrary parent.
            if (limit < BootstrapCost || limit > SourceLimit) { return null; }
            return new PocNativeMetadataInputSession(limit - BootstrapCost);
        }

        internal static PocNativeMetadataInputSession? TryCreateWithLayout(int limit = SourceLimit)
        {
            if (limit < BootstrapCost || limit > SourceLimit) { return null; }
            return new PocNativeMetadataInputSession(limit - BootstrapCost, collectLayout: true);
        }

        internal int LayoutReadCount => _layoutReads;
        // Derived diagnostic for the fixed six-getter schedule, not extra state.
        internal int LayoutGetterReadCount => 6 * _layoutReads;
        internal int Remaining => _budget.Remaining;
        internal int NativeMatchCount => _nativeMatches;
        internal int HeaderReadCount => _headerReads;
        internal int ModuleMoveNextCount => _moduleChecks;
        internal int MetadataAccessCount => _metadataAccesses;
        internal bool IsRejected => Volatile.Read(ref _terminal) != 0;
        internal PocWorkBudget SourceBudget => _budget;

        // The ONLY provenance factory. There is deliberately no Adopt(reference),
        // caller boolean, filename comparison, custom factory, stream or docs API.
        // Even a null, stopped, concurrent or underfunded observer executes the
        // original factory exactly once, with its native result/exception intact.
        internal static PortableExecutableReference CreateReferenceFromFile(PocNativeMetadataInputSession? session, string path)
        {
            if (session is null) { return MetadataReference.CreateFromFile(path); }
            var entered = session.TryEnter();
            Receipt? receipt = null;
            var completed = false;
            try
            {
                if (entered && session._phase == 0 && session.Spend(ReceiptCost))
                {
                    receipt = new Receipt(session._receipts);
                    session._receipts = receipt;
                    session._receiptCount++;
                }
                else { session.Reject(); }
                var reference = MetadataReference.CreateFromFile(path);
                if (receipt is not null && !session.IsRejected) { receipt.Reference = reference; }
                completed = true;
                return reference;
            }
            finally
            {
                if (!completed) { session.Reject(); }
                if (entered) { session.Exit(); }
            }
        }

        // One nonblocking guard owns ALL ledger withdrawals, including factories.
        // Contention/reentry is a sticky optional veto, never a lock/retry loop.
        private bool TryEnter()
        {
            if (IsRejected) { return false; }
            if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
            {
                if (!IsRejected) { return true; }
                Exit();
                return false;
            }
            Reject();
            return false;
        }

        internal PreparationAccess? BeginPreparation() => PreparationAccess.Begin(this);

        private bool OwnsPreparation(PreparationAccess? access) => access is not null &&
            ReferenceEquals(access, Volatile.Read(ref _access)) &&
            _preparationThread == Environment.CurrentManagedThreadId && Volatile.Read(ref _busy) != 0;

        // Even a retained predicate cannot borrow the frame during another
        // operation/native callback. A loser never withdraws or releases a live
        // owner's guard. Dispose retires idle roots with one CAS; a busy owner
        // instead performs the already-paid detachment on its own Exit.
        private bool TryUsePreparation(PreparationAccess? access)
        {
            if (OwnsPreparation(access) && Interlocked.CompareExchange(ref _operation, 1, 0) == 0) { return true; }
            Dispose();
            return false;
        }

        internal void Bind(PreparationAccess? access, object owner, object root, Compilation compilation)
        {
            if (!TryUsePreparation(access)) { return; }
            try { BindCore(owner, root, compilation); }
            finally { Volatile.Write(ref _operation, 0); }
        }

        private void BindCore(object owner, object root, Compilation compilation)
        {
            if (IsRejected) { return; }
            if (_phase != 1 || _owner is not null) { Reject(); return; }
            _owner = owner;
            _root = root;
            _compilation = compilation;
            // ExternalReferences is an immutable auto-property. Neither References
            // nor DirectiveReferences is queried by this optional coverage pass.
            if (!Spend(8)) { return; }
            var references = compilation.ExternalReferences;
            long allocation = SlotArrayHeaderCost + (long)SlotCost * references.Length;
            if (!Spend(allocation)) { return; }
            _slots = new Slot[references.Length];
            if (_collectLayout)
            {
                // No growth: ExternalReferences bounds all initialization,
                // retention/copy/cleanup before allocating any layout cells.
                if (!Spend(LayoutArrayHeaderCost + (long)LayoutElementCost * references.Length)) { return; }
                _layouts = new PocNativeMetadataLayout[references.Length];
            }
            if (_checked is { } checkedState)
            {
                if (!Spend(CheckedArrayHeaderCost + (long)CheckedElementCost * references.Length)) { return; }
                checkedState.Cells = new CheckedCell[references.Length];
                checkedState.RecordedOwner = owner as Providers.PreparedFieldTypes;
            }
            for (var ordinal = 0; ordinal < references.Length; ordinal++)
            {
                // Pay the entire possible receipt walk before its first step.
                if (!Spend(8L + 8L * _receiptCount)) { return; }
                Receipt? found = null;
                for (var receipt = _receipts; receipt is not null; receipt = receipt.Next)
                {
                    if (ReferenceEquals(receipt.Reference, references[ordinal]))
                    {
                        if (found is not null || receipt.Ordinal != -1) { Reject(); return; }
                        found = receipt;
                    }
                }
                if (found is null) { Reject(); return; }
                found.Ordinal = ordinal;
            }
        }

        internal Func<MetadataReference, bool>? CreateOriginalMatchPredicate(PreparationAccess? access, Compilation compilation, INamedTypeSymbol target)
        {
            if (!TryUsePreparation(access)) { return null; }
            try
            {
                if (IsRejected || !ReferenceEquals(compilation, _compilation)) { Reject(); return null; }
                if (!Spend(PredicateCost)) { return null; }
                // Four references fit the existing <=48-byte context allowance;
                // the separate <=64-byte delegate and debit are unchanged.
                return new OriginalMatchPredicate(this, access!, compilation, target).Matches;
            }
            finally { Volatile.Write(ref _operation, 0); }
        }

        private sealed class OriginalMatchPredicate(PocNativeMetadataInputSession session, PreparationAccess access,
            Compilation compilation, INamedTypeSymbol target)
        {
            internal bool Matches(MetadataReference reference)
            {
                var observing = session.TryUsePreparation(access);
                try
                {
                    // Exactly the original alias short circuits, getter order and
                    // equality, once, INCLUDING retained/late/foreign invocation.
                    // Never catch/format any original native exception.
                    if (!(reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global"))) { return false; }
                    var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
                    var assembly = target.ContainingAssembly;
                    var matches = SymbolEqualityComparer.Default.Equals(symbol, assembly);
                    if (matches && observing) { session.ObserveOriginalMatch(access, reference, symbol); }
                    return matches;
                }
                finally { if (observing) { Volatile.Write(ref session._operation, 0); } }
            }
        }

        private void ObserveOriginalMatch(PreparationAccess access, MetadataReference reference, ISymbol? symbol)
        {
            if (!OwnsPreparation(access) || Volatile.Read(ref _operation) != 1) { Reject(); return; }
            if (IsRejected) { return; }
            if (_phase != 1 || _owner is null || _root is null || _slots is null ||
                reference is not PortableExecutableReference || symbol is not IAssemblySymbol assembly)
            { Reject(); return; }
            if (!Spend(8L + 8L * _receiptCount)) { return; }
            Receipt? found = null;
            for (var receipt = _receipts; receipt is not null; receipt = receipt.Next)
            {
                if (ReferenceEquals(reference, receipt.Reference)) { found = receipt; break; }
            }
            // Unknown external owners never reach Modules/GetMetadata, even if
            // their filename/type/metadata ID happens to match a private factory.
            if (found is null || found.Ordinal < 0) { Reject(); return; }
            _nativeMatches++;
            if (found.Assembly is not null)
            {
                if (!ReferenceEquals(found.Assembly, assembly)) { Reject(); }
                return;
            }
            if (!Spend(HeaderCost)) { return; }
            // The exact private factory root stays strongly alive through both
            // wrapper cleanup and capture. Public metadata views are nonowners.
            var keepAlive = found.Reference;
            try
            {
                using var modules = assembly.Modules.GetEnumerator();
                _moduleChecks++;
                if (!modules.MoveNext()) { Reject(); return; }
                var module = modules.Current;
                _moduleChecks++;
                if (modules.MoveNext()) { Reject(); return; }
                _metadataAccesses++;
                using var metadata = module.GetMetadata();
                if (metadata is null) { Reject(); return; }
                // Successful native PEAssembly construction initialized its
                // manifest reader. The single-module + positive original target
                // guard selects that manifest, not an arbitrary/cold reader.
                var reader = metadata.GetMetadataReader();
                var header = new PocNativeMetadataExternalHeader(reader.MetadataLength,
                    reader.GetTableRowCount(TableIndex.TypeDef), reader.GetTableRowCount(TableIndex.TypeRef),
                    reader.GetTableRowCount(TableIndex.ExportedType), reader.GetTableRowCount(TableIndex.AssemblyRef),
                    reader.GetTableRowCount(TableIndex.NestedClass), reader.GetTableRowCount(TableIndex.GenericParam),
                    reader.GetTableRowCount(TableIndex.CustomAttribute), reader.GetTableRowCount(TableIndex.MethodDef),
                    reader.GetTableRowCount(TableIndex.Field), reader.GetTableRowCount(TableIndex.MemberRef),
                    reader.GetTableRowCount(TableIndex.TypeSpec), reader.GetTableRowCount(TableIndex.MethodSpec));
                _headerReads++;
                if (IsRejected) { return; }
                if (_collectLayout)
                {
                    // Same original-positive, private-factory single module and
                    // exclusive operation; reserve BEFORE all six layout getters.
                    if (!Spend(LayoutReadCost)) { return; }
                    var kind = reader.MetadataKind;
                    var moduleRowSize = reader.GetTableRowSize(TableIndex.Module);
                    var rowSize = reader.GetTableRowSize(TableIndex.TypeDef);
                    var tableOffset = reader.GetTableMetadataOffset(TableIndex.TypeDef);
                    var stringSize = reader.GetHeapSize(HeapIndex.String);
                    var stringOffset = reader.GetHeapMetadataOffset(HeapIndex.String);
                    _layoutReads++;
                    if (!PocNativeMetadataLayout.TryCreateWithModule(header.MetadataLength, header.TypeDef,
                        kind, moduleRowSize, rowSize, tableOffset, stringOffset, stringSize, out var layout))
                    { Reject(); return; }
                    if (IsRejected) { return; }
                    _layouts![found.Ordinal] = layout;
                    if (_checked is not null && !CaptureCheckedInputs(access, reader, header, layout, found.Ordinal))
                    { Reject(); return; }
                }
                _slots[found.Ordinal] = new Slot { Header = header, Captured = true };
                found.Assembly = assembly;
            }
            finally { GC.KeepAlive(keepAlive); }
        }

        internal void EndPreparation(PreparationAccess? access, object? owner, object? root, Compilation? compilation)
        {
            // In particular, do NOT put this check inside the finally/Exit scope.
            // A foreign/reentrant end may reject but cannot retire a live frame.
            if (!TryUsePreparation(access)) { return; }
            try
            {
                if (IsRejected) { return; }
                if (_phase != 1 || Volatile.Read(ref _busy) == 0 || !ReferenceEquals(owner, _owner) || !ReferenceEquals(root, _root) ||
                    !ReferenceEquals(compilation, _compilation) || _slots is null)
                { Reject(); return; }
                if (!Spend(8L + 8L * _slots.Length)) { return; }
                if (_collectLayout)
                {
                    // Reserve extra layout seal/coverage work before the walk;
                    // no partial headers OR layouts publish on a short debit.
                    if (!Spend(8L + 8L * _slots.Length)) { return; }
                    if (_layouts is null || _layouts.Length != _slots.Length) { Reject(); return; }
                }
                if (_checked is { } checkedState)
                {
                    if (!Spend(CheckedSealCost + (long)CheckedSealElementCost * _slots.Length) || !CheckedLive() ||
                        checkedState.Cells is null || checkedState.Cells.Length != _slots.Length) { Reject(); return; }
                }
                for (var index = 0; index < _slots.Length; index++)
                {
                    if (!_slots[index].Captured || (_collectLayout && _layouts![index].TypeDefRowSize <= 0) ||
                        (_checked is not null && (!_checked.Cells![index].Captured || !CheckedLive()))) { Reject(); return; }
                }
                if (_checked is { } completedState)
                {
                    if (!CheckedLive()) { Reject(); return; }
                    completedState.RecordedOwner = null;
                    completedState.Name = null;
                    completedState.Namespace = null;
                }
                if (IsRejected) { return; }
                Volatile.Write(ref _phase, 2);
                // O(1) removal of native owners/symbols; retirement liability of
                // every receipt was reserved before allocating it. Never Dispose
                // native images. Only copied scalar slots survive publication.
                _receipts = null;
                _compilation = null;
                _root = null;
                _owner = null;
            }
            finally
            {
                // The actual owner still retires after a competing operation or
                // Dispose has already rejected optional observation.
                Volatile.Write(ref _access, null);
                _preparationThread = 0;
                Volatile.Write(ref _operation, 0);
                Exit();
            }
        }

        // The exact owner/root/compilation were checked once at seal. Subsequent
        // access is historical scalar-only; PreparedFieldTypes gates it through
        // its unique union slot and invalidation. Do not retain that owner here:
        // it in turn retains the compiler and would prolong native image lifetime.
        internal bool TryReadExternalHeader(int ordinal, out PocNativeMetadataExternalHeader header)
        {
            header = default;
            if (IsRejected || Volatile.Read(ref _phase) != 2) { return false; }
            var slots = _slots;
            if (slots is null || (uint)ordinal >= (uint)slots.Length) { return false; }
            header = slots[ordinal].Header;
            return !IsRejected;
        }

        // Historical extents only: no reader, handle, native owner or lease.
        internal bool TryReadExternalLayout(int ordinal, out PocNativeMetadataLayout layout)
        {
            layout = default;
            if (IsRejected || Volatile.Read(ref _phase) != 2) { return false; }
            var layouts = _layouts;
            if (layouts is null || (uint)ordinal >= (uint)layouts.Length) { return false; }
            var copy = layouts[ordinal];
            if (IsRejected) { return false; }
            layout = copy;
            return true;
        }

        private bool Spend(long cost)
        {
            if (!IsRejected && cost <= int.MaxValue && _budget.TrySpend((int)cost)) { return true; }
            Reject();
            return false;
        }

        private void Reject() => Interlocked.Exchange(ref _terminal, 1);

        private void Exit()
        {
            if (IsRejected) { Drop(); }
            Volatile.Write(ref _busy, 0);
            // Close the reject-between-check-and-release race with one CAS, not
            // a retry. Either this call or a concurrent owner retires the state.
            if (IsRejected && Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
            { Drop(); Volatile.Write(ref _busy, 0); }
        }

        private void Drop()
        {
            // Prepaid O(1) detachment, not storage funded by fixed ingress CPU.
            _receipts = null;
            _slots = null;
            _layouts = null;
            _checked = null;
            _compilation = null;
            _root = null;
            _owner = null;
        }

        public void Dispose()
        {
            Reject();
            // A concurrent owner performs cleanup on Exit; no concurrent ledger
            // access, traversal, or reference disposal is attempted here.
            if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0) { Drop(); Volatile.Write(ref _busy, 0); }
        }
    }
}

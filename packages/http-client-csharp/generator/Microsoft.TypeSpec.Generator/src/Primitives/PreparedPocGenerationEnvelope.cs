// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Separate POC accounting: neither stream identity nor these traversal
    // receipts establish grammar, lookup, lazy-header confinement or permission.
    // Never reads output text or invokes a producer/currentness callback.
    internal sealed class PreparedPocGenerationEnvelope
    {
        internal enum ReceiptState { Missing, Matched, Rejected }

        private readonly Dictionary<TypeProvider, FilePlan> _files = new(ReferenceEqualityComparer.Instance);
        private bool _rejected;
        private bool _streamCompleted;
        private int _next;
        internal int PlannedFileCount => _files.Count;
        internal bool HasUncoveredEmission => true;
        internal bool HasCompleteTraversalReceipts => !_rejected && _streamCompleted && _files.Count != 0 && _next == _files.Count;

        internal static PreparedPocGenerationEnvelope Capture(ImmutableArray<TypeProvider> expected,
            IReadOnlyDictionary<TypeProvider, PreparedSourceFile> sources, PocWorkBudget? budget = null)
        {
            var envelope = new PreparedPocGenerationEnvelope();
            if (expected.IsDefaultOrEmpty || expected.Length > 4096 ||
                sources is not Dictionary<TypeProvider, PreparedSourceFile> known || known.GetType() != typeof(Dictionary<TypeProvider, PreparedSourceFile>) ||
                !ReferenceEquals(known.Comparer, ReferenceEqualityComparer.Instance))
            { envelope._rejected = true; return envelope; }
            budget ??= new PocWorkBudget(262144);
            var paths = new HashSet<string>(System.StringComparer.Ordinal);
            System.Guid? generation = null;
            foreach (var owner in expected)
            {
                if (!budget.TrySpend() || owner is null ||
                    !budget.TrySpendReferenceLookup(envelope._files.Count) || envelope._files.ContainsKey(owner) ||
                    !budget.TrySpendReferenceLookup(known.Count) || !known.TryGetValue(owner, out var file) ||
                    file is null || !ReferenceEquals(owner, file.FileOwner) ||
                    file.FilePath is null || !TrySpendPathProbe(file.FilePath, paths.Count, budget) || !paths.Add(file.FilePath) ||
                    file.Generation == System.Guid.Empty || generation is { } capturedGeneration && capturedGeneration != file.Generation)
                { envelope._files.Clear(); envelope._rejected = true; return envelope; }
                generation ??= file.Generation;
                var consumed = ImmutableArray.CreateBuilder<SourceOccurrenceId>();
                var pending = new Stack<PreparedSourceOccurrence>();
                foreach (var root in file.Roots)
                {
                    if (!budget.TrySpend()) { envelope._files.Clear(); envelope._rejected = true; return envelope; }
                    if (!root.EmissionBoundary) { consumed.Add(root.Id); }
                    pending.Push(root);
                    while (pending.TryPop(out var occurrence))
                    {
                        if (!budget.TrySpend()) { envelope._files.Clear(); envelope._rejected = true; return envelope; }
                        if (occurrence.EmissionBoundary) { consumed.Add(occurrence.Id); }
                        if (occurrence.IsOpaque) { continue; }
                        if (!budget.TrySpend(occurrence.Children.Length))
                        { envelope._files.Clear(); envelope._rejected = true; return envelope; }
                        for (var index = occurrence.Children.Length - 1; index >= 0; index--) { pending.Push(occurrence.Children[index]); }
                    }
                }
                var constraint = PreparedPocTypeConstraint.TryCapture(owner, budget);
                var native = PreparedPocFileConsumption.TryCapture(file, constraint, budget, out var captureFailure);
                if (budget.Exhausted || !budget.TrySpendReferenceLookup(envelope._files.Count))
                { envelope._files.Clear(); envelope._rejected = true; return envelope; }
                envelope._files.Add(owner, new FilePlan(file, envelope._files.Count, consumed.ToImmutable(), constraint, native, captureFailure));
            }
            return envelope;
        }

        private static bool TrySpendPathProbe(string path, int count, PocWorkBudget budget)
        {
            // Pay hashing plus every possible same-length ordinal comparison
            // before HashSet.Add. Different lengths cannot cost more.
            long work = (path.Length + 1L) * (count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        internal FilePlan? GetFile(TypeProvider owner, PocWorkBudget? budget = null)
        {
            // A completed capture remains usable even if its capture budget was
            // exhausted. Fund each later identity probe independently instead.
            budget ??= new PocWorkBudget(262144);
            return owner is not null && budget.TrySpendReferenceLookup(_files.Count) && _files.TryGetValue(owner, out var plan) ? plan : null;
        }
        internal void Reject() { _rejected = true; }
        internal void CompleteStream()
        {
            if (_streamCompleted || _next != _files.Count || _files.Count == 0) { Reject(); return; }
            _streamCompleted = true;
        }

        // Invoked at the existing EndPocProviderWrite boundary. A duplicate,
        // foreign, reordered, missing or rejected receipt stays rejected; an
        // endpoint match after restoration cannot repair observed divergence.
        internal void ObserveWrite(TypeProvider owner, SourceConsumptionReceipt? receipt, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(262144);
            if (owner is null || !budget.TrySpendReferenceLookup(_files.Count) || !_files.TryGetValue(owner, out var plan))
            { Reject(); return; }
            if (plan.State != ReceiptState.Missing || plan.Index != _next)
            { plan.State = ReceiptState.Rejected; Reject(); return; }
            _next++;
            if (!plan.Matches(receipt, budget)) { plan.State = ReceiptState.Rejected; Reject(); return; }
            plan.State = ReceiptState.Matched;
        }

        internal sealed class FilePlan
        {
            internal PreparedSourceFile Source { get; }
            internal int Index { get; }
            internal ReceiptState State { get; set; }
            internal PreparedPocTypeConstraint? TypeConstraint { get; }
            private readonly PreparedPocFileConsumption? _native;
            private readonly string? _captureFailure;
            internal bool HasCompleteNativeConsumption => _native?.HasCompleted == true;
            internal string? NativeConsumptionFailure => _native is null ? "plan-unavailable:" + _captureFailure : _native.Failure;
            internal PreparedPocFileConsumption? StartNativeConsumption(PreparedFieldTypes preparation, TypeProviderWriter writer) =>
                _native?.Start(preparation, writer);
            private readonly ImmutableArray<SourceOccurrenceId> _consumed;

            internal FilePlan(PreparedSourceFile source, int index, ImmutableArray<SourceOccurrenceId> consumed,
                PreparedPocTypeConstraint? constraint, PreparedPocFileConsumption? native, string? captureFailure)
            { Source = source; Index = index; _consumed = consumed; TypeConstraint = constraint; _native = native; _captureFailure = captureFailure; }

            internal bool Matches(SourceConsumptionReceipt? receipt, PocWorkBudget budget)
            {
                if (!budget.TrySpend() || receipt is not { WasCurrentAtEndOfWrite: true } || receipt.Generation != Source.Generation ||
                    !TrySpendOrdinalComparison(receipt.File, Source.FilePath, budget) || receipt.File != Source.FilePath ||
                    receipt.RootCount != Source.Roots.Length || receipt.Consumed.IsDefault || receipt.Consumed.Length != _consumed.Length) { return false; }
                for (var index = 0; index < _consumed.Length; index++)
                {
                    var expected = _consumed[index];
                    var actual = receipt.Consumed[index];
                    if (!TrySpendOccurrenceComparison(expected, actual, budget) || expected != actual) { return false; }
                }
                return true;
            }

            private static bool TrySpendOccurrenceComparison(SourceOccurrenceId? expected, SourceOccurrenceId? actual, PocWorkBudget budget)
            {
                if (!budget.TrySpend()) { return false; }
                if (ReferenceEquals(expected, actual) || expected is null || actual is null) { return true; }
                return TrySpendOrdinalComparison(expected.File, actual.File, budget) &&
                    TrySpendOrdinalComparison(expected.MemberPath, actual.MemberPath, budget) &&
                    TrySpendOrdinalComparison(expected.RootRole, actual.RootRole, budget) &&
                    TrySpendOrdinalComparison(expected.EdgePath, actual.EdgePath, budget);
            }

            private static bool TrySpendOrdinalComparison(string? left, string? right, PocWorkBudget budget)
            {
                // String equality has a reference fast path; otherwise fund a
                // conservative complete comparison before invoking the operator.
                if (ReferenceEquals(left, right) || left is null || right is null) { return budget.TrySpend(); }
                long work = (long)left.Length + right.Length + 1;
                return work <= int.MaxValue && budget.TrySpend((int)work);
            }
        }
    }
}

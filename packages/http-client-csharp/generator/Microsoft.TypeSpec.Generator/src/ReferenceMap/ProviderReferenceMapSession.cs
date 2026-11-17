// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator
{
    internal sealed class ProviderReferenceMapSession : IDisposable
    {
        private bool _disposed;
        private readonly long _activityVersion;
        private readonly ProviderReferenceMapResult? _activityResult;
        private readonly bool _hasActivityWitness;
        private readonly OutputLibrary? _rootOutput;
        private readonly IReadOnlyList<TypeProvider>? _rootReference;
        private readonly long _rootRevision;
        private object? _generationOwner;
        private int _enumerationState;
        private int _ownedEnumerationState;
        private PreparedFieldTypes? _pocProviderStream;

        internal ProviderReferenceMapSession() { }

        internal ProviderReferenceMapSession(long activityVersion, ProviderReferenceMapResult? activityResult, bool uninterrupted,
            OutputLibrary? rootOutput, IReadOnlyList<TypeProvider> rootReference, long rootRevision)
        {
            _activityVersion = activityVersion;
            _activityResult = activityResult;
            _hasActivityWitness = uninterrupted;
            _rootOutput = rootOutput;
            _rootReference = rootReference;
            _rootRevision = rootRevision;
        }

        // A non-authorizing observation: this does not establish a serialized
        // host, an output/root phase, group closure, or absence of an operation
        // that was already in flight before capture.
        internal bool IsObservedAnalyzerActivityCurrent() => _hasActivityWitness && !_disposed &&
            _activityVersion != long.MaxValue && ProviderReferenceMapAnalyzer.ActivityVersion == _activityVersion &&
            ReferenceEquals(ProviderReferenceMapAnalyzer.LatestResult, _activityResult) &&
            ProviderReferenceMapAnalyzer.ActivityVersion == _activityVersion;

        // Root identity and observed analyzer entries only: array/list content,
        // earlier in-flight operations, group closure and name permission are
        // deliberately outside this fact.
        internal bool IsObservedReferenceMapRootCurrent(OutputLibrary output, IReadOnlyList<TypeProvider> roots) =>
            ReferenceEquals(_rootOutput, output) && ReferenceEquals(_rootReference, roots) && IsObservedAnalyzerActivityCurrent() &&
            output.IsMaterializedTypeProviderSnapshotCurrent(roots, _rootRevision) && IsObservedAnalyzerActivityCurrent();

        internal bool TryGetObservedReferenceMapRoot(out OutputLibrary? output, out IReadOnlyList<TypeProvider>? roots)
        {
            output = _rootOutput;
            roots = _rootReference;
            return output is not null && roots is not null && IsObservedReferenceMapRootCurrent(output, roots);
        }

        // The analyzer result reference is stable even if its removal set is
        // mutated. Capture and compare the concrete ordinal set without replaying
        // the potentially host-supplied write-policy delegate. Bound the set and
        // its strings before copying or hashing any of its entries.
        private const int MaxPocRemovalCandidates = 4096;
        private const int MaxPocRemovalNameLength = 4096;
        private const int MaxPocRemovalCharacters = 65536;

        private static bool IsBoundedRemovalSet(HashSet<string> names)
        {
            if (names.GetType() != typeof(HashSet<string>) || !ReferenceEquals(names.Comparer, StringComparer.Ordinal) ||
                names.Count > MaxPocRemovalCandidates) { return false; }
            var remaining = MaxPocRemovalCharacters;
            var count = 0;
            foreach (var name in names)
            {
                if (++count > MaxPocRemovalCandidates || name is null || name.Length > MaxPocRemovalNameLength || name.Length > remaining) { return false; }
                remaining -= name.Length;
            }
            return true;
        }

        // POC-only funding gate. Keep the native session's absolute-bound
        // observation unchanged, but never allocate/hash its immutable copy or
        // execute its final membership pass before reserving that repeated work.
        internal bool TryCaptureObservedRemovalCandidates(PocWorkBudget budget, out ImmutableHashSet<string>? snapshot)
        {
            snapshot = null;
            if (!IsObservedAnalyzerActivityCurrent() || _activityResult?.RemoveCandidates is not { } current ||
                current.GetType() != typeof(HashSet<string>) || !ReferenceEquals(current.Comparer, StringComparer.Ordinal)) { return false; }
            var count = current.Count;
            // Count excludes deleted entries; the enumerator can still scan
            // their slots after native ExceptWith. Public capacity bounds that
            // scan without private collection-layout inspection or compaction.
            var capacity = current.EnsureCapacity(0);
            if (count > MaxPocRemovalCandidates || capacity == int.MaxValue || !budget.TrySpend(capacity + 1)) { return false; }
            try
            {
                var seen = 0;
                var characters = 0;
                foreach (var name in current)
                {
                    if (++seen > count || name is null || name.Length > MaxPocRemovalNameLength ||
                        name.Length > MaxPocRemovalCharacters - characters || !budget.TrySpend(name.Length)) { return false; }
                    characters += name.Length;
                }
                if (seen != count || current.Count != count || current.EnsureCapacity(0) != capacity || !IsObservedAnalyzerActivityCurrent()) { return false; }
                // Immutable builder insertion and native Contains each require
                // hash/possible-chain/equal-hash character work. Include the
                // repeated bounded validation/enumeration and owned elements;
                // do not infer constant probes from an ordinal comparer.
                // The unchanged legacy helper enumerates this native set
                // twice more (absolute validation, then immutable insertion).
                long work = 2L * (count + 1) * (count + characters) + 2L * (capacity + characters) + count;
                if (work > int.MaxValue || !budget.TrySpend((int)work)) { return false; }
                return TryCaptureObservedRemovalCandidates(out snapshot);
            }
            catch (InvalidOperationException) { return false; }
        }

        internal bool TryCaptureObservedRemovalCandidates(out ImmutableHashSet<string>? snapshot)
        {
            snapshot = null;
            if (!IsObservedAnalyzerActivityCurrent() || _activityResult?.RemoveCandidates is not { } current) { return false; }
            try
            {
                if (!IsBoundedRemovalSet(current)) { return false; }
                var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
                var remaining = MaxPocRemovalCharacters;
                var count = 0;
                foreach (var name in current)
                {
                    if (++count > MaxPocRemovalCandidates || name is null || name.Length > MaxPocRemovalNameLength || name.Length > remaining) { return false; }
                    remaining -= name.Length;
                    builder.Add(name);
                }
                var captured = builder.ToImmutable();
                if (!IsObservedRemovalCandidatesCurrent(captured)) { return false; }
                snapshot = captured;
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        // This is a bounded point-in-time comparison, not an ABA/mutation
        // witness: callers requiring sticky freshness need separate ownership or
        // mutation evidence and must not treat a restored set as unchanged.
        internal bool IsObservedRemovalCandidatesCurrent(ImmutableHashSet<string> snapshot)
        {
            if (!IsObservedAnalyzerActivityCurrent() || _activityResult?.RemoveCandidates is not { } current ||
                current.GetType() != typeof(HashSet<string>) || !ReferenceEquals(current.Comparer, StringComparer.Ordinal) ||
                !ReferenceEquals(snapshot.KeyComparer, StringComparer.Ordinal) || snapshot.Count > MaxPocRemovalCandidates ||
                current.Count != snapshot.Count) { return false; }
            var remaining = MaxPocRemovalCharacters;
            foreach (var name in snapshot)
            {
                if (name is null || name.Length > MaxPocRemovalNameLength || name.Length > remaining) { return false; }
                remaining -= name.Length;
                if (!current.Contains(name)) { return false; }
            }
            return IsObservedAnalyzerActivityCurrent();
        }

        // The plan is non-authorizing; these hooks observe the existing iterator
        // without replaying it or invoking a user-supplied policy callback.
        internal bool TryAttachPocProviderStream(PreparedFieldTypes preparation) => IsObservedAnalyzerActivityCurrent() &&
            Interlocked.CompareExchange(ref _pocProviderStream, preparation, null) is null;

        internal void BeginPocProviderStream() => Volatile.Read(ref _pocProviderStream)?.BeginPocProviderStream();
        internal void ObservePocProviderYield(TypeProvider provider) => Volatile.Read(ref _pocProviderStream)?.ObservePocProviderYield(provider);
        internal void EndPocProviderStream(bool exhausted) => Volatile.Read(ref _pocProviderStream)?.EndPocProviderStream(exhausted);

        internal bool TryBindGenerationOwner(object owner, CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<TypeProvider> roots)
        {
            if (!CSharpGen.IsActiveGenerationOwner(owner, generator, output, roots, this) || !IsObservedReferenceMapRootCurrent(output, roots)) { return false; }
            return Interlocked.CompareExchange(ref _generationOwner, owner, null) is null;
        }

        internal bool IsOwnedGenerationPhaseCurrent(CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<TypeProvider> roots)
        {
            var owner = Volatile.Read(ref _generationOwner);
            return owner is not null && CSharpGen.IsActiveGenerationOwner(owner, generator, output, roots, this) &&
                IsObservedReferenceMapRootCurrent(output, roots) && CSharpGen.IsActiveGenerationOwner(owner, generator, output, roots, this);
        }

        internal void ObserveEnumerationRoot(OutputLibrary output, IReadOnlyList<TypeProvider> roots)
        {
            if (_rootOutput is null) { return; }
            if (Interlocked.CompareExchange(ref _enumerationState, 1, 0) != 0 || !IsObservedReferenceMapRootCurrent(output, roots))
            {
                Interlocked.Exchange(ref _enumerationState, -1);
                return;
            }
            if (Interlocked.CompareExchange(ref _enumerationState, 2, 1) != 1)
            {
                Interlocked.Exchange(ref _enumerationState, -1);
            }
        }

        internal bool IsObservedEnumerationRootCurrent(OutputLibrary output, IReadOnlyList<TypeProvider> roots) =>
            Volatile.Read(ref _enumerationState) == 2 && IsObservedReferenceMapRootCurrent(output, roots) &&
            Volatile.Read(ref _enumerationState) == 2;

        // The private owner is passed only by CSharpGen's actual iterator. A
        // caller-supplied root observation alone cannot claim this boundary.
        internal void ObserveOwnedEnumerationRoot(object owner, CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<TypeProvider> roots)
        {
            if (!ReferenceEquals(Volatile.Read(ref _generationOwner), owner) ||
                !CSharpGen.IsActiveGenerationOwner(owner, generator, output, roots, this) ||
                Interlocked.CompareExchange(ref _ownedEnumerationState, 1, 0) != 0 || !IsOwnedGenerationPhaseCurrent(generator, output, roots))
            {
                Interlocked.Exchange(ref _ownedEnumerationState, -1);
                return;
            }
            if (Interlocked.CompareExchange(ref _ownedEnumerationState, 2, 1) != 1)
            {
                Interlocked.Exchange(ref _ownedEnumerationState, -1);
            }
        }

        internal bool IsOwnedEnumerationRootCurrent(CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<TypeProvider> roots) =>
            Volatile.Read(ref _ownedEnumerationState) == 2 && IsOwnedGenerationPhaseCurrent(generator, output, roots) &&
            Volatile.Read(ref _ownedEnumerationState) == 2;

        public bool ShouldWriteProvider(TypeProvider provider) => ProviderReferenceMapAnalyzer.ShouldWriteProvider(provider);

        public void RestorePreWriteModelFactoryMethods() => ProviderReferenceMapAnalyzer.RestorePreWriteModelFactoryMethods();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Interlocked.Exchange(ref _pocProviderStream, null);
            ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
            _disposed = true;
        }
    }
}

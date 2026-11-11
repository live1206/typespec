// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Independent, preplanned traversal accounting. Opaque boundaries remain
    // opaque: completion is NOT grammar, binding, currentness or permission.
    internal sealed class PreparedPocFileConsumption
    {
        private sealed record Entry(object Producer, bool Opaque, int End);
        private sealed record Root(TypeProvider Owner, string Role, int Index, bool Opaque,
            bool PlannedConstraint, ImmutableArray<Entry> Entries, object? Producer = null);
        private readonly PreparedSourceFile _source;
        private readonly ImmutableArray<Root> _roots;
        private readonly PreparedPocTypeConstraint? _constraint;
        private PreparedFieldTypes? _preparation;
        private TypeProviderWriter? _writer;
        private Root? _active;
        private int _nextRoot, _nextNode, _opaqueDepth, _events;
        private bool _started, _failed, _completed, _rootObserved;
        private string? _failure;
        internal string? Failure => _failure ?? (_completed && _constraint is not null && !_constraint.HasObservedExpectedEmission ? "constraint-contract" : null);
        private void Fail(string reason) { _failure ??= reason; _failed = true; }

        private PreparedPocFileConsumption(PreparedSourceFile source, ImmutableArray<Root> roots, PreparedPocTypeConstraint? constraint)
        { _source = source; _roots = roots; _constraint = constraint; }

        internal static PreparedPocFileConsumption? TryCapture(PreparedSourceFile source,
            PreparedPocTypeConstraint? constraint, PocWorkBudget budget, out string? failure)
        {
            failure = "capture-unavailable";
            if (source.Roots.IsDefaultOrEmpty || !budget.TrySpend(source.Roots.Length)) { return null; }
            var roots = ImmutableArray.CreateBuilder<Root>();
            // A known lazy constraint has its own fixed producer contract. Insert
            // its root at the native header position, never discover it at write time.
            var insertion = -1;
            if (constraint?.ExpectsEmission == true && source.FileOwner.CachedWhereClause is null)
            {
                insertion = 1;
                while (insertion < source.Roots.Length && source.Roots[insertion].Id.MemberPath == source.Roots[0].Id.MemberPath &&
                    source.Roots[insertion].Id.RootRole == "type-attribute") { insertion++; }
            }
            TypeProvider? owner = null;
            for (var index = 0; index <= source.Roots.Length; index++)
            {
                if (index == insertion)
                {
                    if (!budget.TrySpend()) { return null; }
                    roots.Add(new Root(source.FileOwner, "type-constraint", 0, false, true, []));
                }
                if (index == source.Roots.Length) { break; }
                var root = source.Roots[index];
                if (root.Id.RootRole == "type-documentation") { owner = root.Producer as TypeProvider; }
                if (owner is null || index == 0 && !ReferenceEquals(owner, source.FileOwner)) { return null; }
                // Type attributes may have an opaque compiler-produced carrier in
                // the shared graph. Use only its separately recorded construction
                // elements here, and keep the original shared roots unchanged.
                if (root.Id.RootRole == "type-attribute") { continue; }
                var entries = new List<Entry>();
                if (!CaptureEntries(root, entries, budget, 0, source.DisableXmlDocs, out failure))
                { failure = $"{root.Id.RootRole}[{root.Id.RootIndex}]:{failure}"; return null; }
                roots.Add(new Root(owner, root.Id.RootRole, root.Id.RootIndex,
                    root.IsOpaque && !root.EmissionBoundary, false, entries.ToImmutableArray(),
                    root.EmissionBoundary || root.Id.RootRole == "type-documentation" ? null : root.Producer));
                if (root.Id.RootRole == "type-documentation")
                {
                    if (budget.Exhausted || budget.Remaining == 0) { return null; }
                    ImmutableArray<MethodBodyStatement> attributes;
                    if (owner.PocPostMapAttributeOperation is not null)
                    {
                        // The audited canonical call deliberately omits its
                        // global witness. Read only the live carrier owned by
                        // this exact current operation; never fall back to a
                        // stale witness when that projection is unavailable.
                        if (owner.CachedWriteAttributes is not { } carrier ||
                            !owner.TryCopyCurrentPocAttributeProjection(carrier, budget, out attributes)) { return null; }
                    }
                    else if (!ContractCollections.TryCopyPocConstruction(owner.CachedWriteAttributes, budget, out attributes)) { return null; }
                    for (var attributeIndex = 0; attributeIndex < attributes.Length; attributeIndex++)
                    {
                        if (attributes[attributeIndex] is not { } attribute) { return null; }
                        var attributeEntries = new List<Entry>();
                        if (!CapturePocEntries(attribute, true, attributeEntries, budget, 0, source.DisableXmlDocs, out failure)) { return null; }
                        roots.Add(new Root(owner, "type-attribute", attributeIndex, false, false, attributeEntries.ToImmutableArray()));
                    }
                }
            }
            failure = null;
            return new PreparedPocFileConsumption(source, roots.ToImmutable(), constraint);
        }

        private static bool CaptureEntries(PreparedSourceOccurrence occurrence, List<Entry> entries, PocWorkBudget budget, int depth, bool disableDocs, out string? failure)
        {
            failure = "capture-budget-or-depth";
            if (depth > 256 || !budget.TrySpend()) { return false; }
            // Signatures/parameters/initializers have no native Enter boundary.
            // Their shared opacity cannot absorb child writes. A POC-only capture
            // may now expand them from witnessed carriers before writing starts.
            if (depth > 0 && occurrence.IsOpaque && !occurrence.EmissionBoundary)
            { return CapturePocEntries(occurrence.Producer, false, entries, budget, depth, disableDocs, out failure); }
            var entry = -1;
            if (occurrence.EmissionBoundary)
            {
                entry = entries.Count;
                entries.Add(new Entry(occurrence.Producer, occurrence.IsOpaque, 0));
            }
            if (!occurrence.IsOpaque)
            {
                foreach (var child in occurrence.Children)
                { if (!CaptureEntries(child, entries, budget, depth + 1, disableDocs, out failure)) { return false; } }
            }
            if (entry >= 0) { entries[entry] = entries[entry] with { End = entries.Count }; }
            return true;
        }

        private static bool CapturePocEntries(object producer, bool boundary, List<Entry> entries, PocWorkBudget budget, int depth, bool disableDocs, out string? failure)
        {
            failure = "capture-budget-or-depth";
            if (depth > 256 || !budget.TrySpend()) { return false; }
            var shape = StructuredSourceShape.CaptureForPocSupplement(producer, disableDocs, budget);
            if (budget.Exhausted || shape.Opaque && !boundary)
            { failure = $"{producer.GetType().Name}:{shape.UnavailableReason}"; return false; }
            var entry = -1;
            if (boundary)
            {
                entry = entries.Count;
                entries.Add(new Entry(producer, shape.Opaque, 0));
            }
            if (!shape.Opaque)
            {
                foreach (var child in shape.Children)
                {
                    if (!CapturePocEntries(child.Producer, child.EmissionBoundary, entries, budget, depth + 1, disableDocs, out failure)) { return false; }
                }
            }
            if (entry >= 0) { entries[entry] = entries[entry] with { End = entries.Count }; }
            return true;
        }

        internal PreparedPocFileConsumption? Start(PreparedFieldTypes preparation, TypeProviderWriter writer)
        {
            if (_started || !preparation.OwnsPocSupplementalWrite(_source, writer)) { Fail("start-or-ownership"); return null; }
            _started = true; _preparation = preparation; _writer = writer;
            return this;
        }

        private bool ObserveBoundary()
        {
            if (_failed || _completed || ++_events > 262144 || _preparation is null || _writer is null ||
                !_preparation.OwnsPocSupplementalWrite(_source, _writer)) { Fail("boundary-or-ownership"); return false; }
            return true;
        }

        internal IDisposable BeginRoot(TypeProvider owner, string role, int index)
        {
            if (!ObserveBoundary() || _active is not null || _nextRoot >= _roots.Length)
            { Fail($"root-state:{role}[{index}]"); return PreparedSourceFile.EmptyScope.Instance; }
            var root = _roots[_nextRoot];
            if (!ReferenceEquals(root.Owner, owner) || root.Role != role || root.Index != index)
            { Fail($"root:{root.Role}[{root.Index}]/{root.Owner.GetType().Name}!={role}[{index}]/{owner.GetType().Name}"); return PreparedSourceFile.EmptyScope.Instance; }
            _nextRoot++; _active = root; _nextNode = 0; _opaqueDepth = root.Opaque ? 1 : 0;
            _rootObserved = root.Producer is null;
            return new Scope(() =>
            {
                if (!ReferenceEquals(_active, root) || !_rootObserved || _nextNode != root.Entries.Length) { Fail($"root-incomplete:{root.Role}[{root.Index}]:{_nextNode}/{root.Entries.Length}"); }
                _active = null; _opaqueDepth = 0;
            });
        }

        // Root roles alone do not identify a field/property/method/constructor.
        // Observe the object actually retained by the native writer after its
        // existing root-entry callbacks, including every repeated enum access.
        internal void ObserveRootProducer(object producer)
        {
            if (!ObserveBoundary() || _active is not { Producer: not null } root)
            { Fail("root-producer-state"); return; }
            if (!ReferenceEquals(root.Producer, producer))
            { Fail($"root-producer:{root.Role}[{root.Index}]"); return; }
            _rootObserved = true;
        }

        internal IDisposable Enter(object producer)
        {
            if (!ObserveBoundary()) { return PreparedSourceFile.EmptyScope.Instance; }
            if (_active is not { } root || _opaqueDepth > 0 || root.PlannedConstraint)
            { return PreparedSourceFile.EmptyScope.Instance; }
            if (_nextNode >= root.Entries.Length || !ReferenceEquals(root.Entries[_nextNode].Producer, producer))
            { Fail($"producer:{root.Role}[{root.Index}]:{_nextNode}:expected={(_nextNode < root.Entries.Length ? root.Entries[_nextNode].Producer.GetType().Name : "end")},actual={producer.GetType().Name}"); return PreparedSourceFile.EmptyScope.Instance; }
            var entry = root.Entries[_nextNode++];
            if (entry.Opaque) { _opaqueDepth++; }
            return new Scope(() =>
            {
                if (entry.Opaque) { _opaqueDepth--; }
                if (_nextNode != entry.End) { Fail($"subtree-incomplete:{root.Role}[{root.Index}]:{_nextNode}/{entry.End}"); }
            });
        }

        internal void Complete()
        {
            if (!ObserveBoundary() || _active is not null || _nextRoot != _roots.Length) { Fail($"file-incomplete:{_nextRoot}/{_roots.Length}"); }
            _completed = true;
        }

        internal bool HasCompleted => _started && _completed && !_failed &&
            (_constraint is null || _constraint.HasObservedExpectedEmission);

        private sealed class Scope(Action end) : IDisposable
        {
            private Action? _end = end;
            public void Dispose() { var end = _end; _end = null; end?.Invoke(); }
        }
    }
}

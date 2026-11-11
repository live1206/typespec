// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Partial, non-authorizing observations of audited opaque boundaries. This
    // is not a certificate for other nodes or the complete physical file.
    internal sealed class PreparedPocSupplementalFile
    {
        private readonly PreparedSourceFile _file;
        private readonly Dictionary<SourceOccurrenceId, Segment> _segments = [];
        private readonly Dictionary<SourceOccurrenceId, PreparedGeneratedDocumentationEnvelope> _documentation = [];
        private readonly List<PreparedPocFieldDeclaration> _safeFields = [];
        private readonly List<ParameterProvider> _safeParameters = [];
        private readonly List<PropertyProvider> _safeProperties = [];
        private int _started, _failed, _completed, _confirmed;
        private bool _unsafeOrUnavailableKnownRawNames, _unsafeOrUnavailableKnownRawOperators, _unsafeOrUnavailableKnownRawKeywords;
        private bool _unsafeOrUnavailableKnownInterpolatedText, _unsafeOrUnavailableKnownDirectiveConditions;
        private ImmutableArray<SourceOccurrenceId> _sharedConsumed;
        internal bool HasUnavailableDocumentation { get; private set; }
        private PreparedFieldTypes? _preparation;

        private PreparedPocSupplementalFile(PreparedSourceFile file, PocWorkBudget? inventoryBudget)
        {
            _file = file;
            inventoryBudget ??= new PocWorkBudget(65536);
            if (!inventoryBudget.TrySpend()) { Fail(); return; }
            var pending = new Stack<PreparedSourceOccurrence>();
            var pendingCapacity = 0;
            for (var index = file.Roots.Length - 1; index >= 0; index--)
            {
                if (!TryPush(pending, file.Roots[index], inventoryBudget, ref pendingCapacity)) { Fail(); return; }
            }
            var examinedBoundaries = 0;
            while (pending.TryPop(out var occurrence))
            {
                if (!inventoryBudget.TrySpend()) { Fail(); return; }
                for (var index = occurrence.Children.Length - 1; index >= 0; index--)
                {
                    if (!TryPush(pending, occurrence.Children[index], inventoryBudget, ref pendingCapacity)) { Fail(); return; }
                }
                if (!occurrence.IsOpaque && occurrence.Producer is (InvokeMethodExpression or MemberExpression or MethodSignatureBase or AttributeStatement or FieldProvider or ParameterProvider or PropertyProvider or DeclareLocalFunctionStatement))
                {
                    var shape = StructuredSourceShape.CaptureForPocSupplement(occurrence.Producer, file.DisableXmlDocs, inventoryBudget);
                    if (shape.Opaque || !shape.IntrinsicTokens) { _unsafeOrUnavailableKnownRawNames = true; }
                    else if (occurrence.Producer is FieldProvider field)
                    {
                        var declaration = PreparedPocFieldDeclaration.TryCapture(field, inventoryBudget);
                        if (declaration is null) { _unsafeOrUnavailableKnownRawNames = true; }
                        else if (!TryAppend(_safeFields, declaration, inventoryBudget)) { Fail(); return; }
                    }
                    else if (occurrence.Producer is ParameterProvider parameter)
                    { if (!TryAppend(_safeParameters, parameter, inventoryBudget)) { Fail(); return; } }
                    else if (occurrence.Producer is PropertyProvider property)
                    { if (!TryAppend(_safeProperties, property, inventoryBudget)) { Fail(); return; } }
                    if (inventoryBudget.Exhausted) { Fail(); return; }
                }
                else if (occurrence.IsOpaque && occurrence.Producer is (InvokeMethodExpression or MemberExpression or MethodSignatureBase or AttributeStatement or FieldProvider or ParameterProvider or PropertyProvider or DeclareLocalFunctionStatement) &&
                    !IsWitnessedBoundary(occurrence.Producer)) { _unsafeOrUnavailableKnownRawNames = true; }
                if (occurrence.Producer is BinaryOperatorExpression or UnaryOperatorExpression)
                {
                    if (occurrence.IsOpaque) { _unsafeOrUnavailableKnownRawOperators = true; }
                    else
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplement(occurrence.Producer, file.DisableXmlDocs, inventoryBudget);
                        if (shape.Opaque || !shape.IntrinsicTokens) { _unsafeOrUnavailableKnownRawOperators = true; }
                        if (inventoryBudget.Exhausted) { Fail(); return; }
                    }
                }
                if (occurrence.Producer is KeywordExpression)
                {
                    if (occurrence.IsOpaque) { _unsafeOrUnavailableKnownRawKeywords = true; }
                    else
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplement(occurrence.Producer, file.DisableXmlDocs, inventoryBudget);
                        if (shape.Opaque || !shape.IntrinsicTokens) { _unsafeOrUnavailableKnownRawKeywords = true; }
                        if (inventoryBudget.Exhausted) { Fail(); return; }
                    }
                }
                if (occurrence.Producer is FormattableStringExpression or IfElsePreprocessorStatement)
                {
                    var unavailable = occurrence.IsOpaque;
                    if (!unavailable)
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplement(occurrence.Producer, file.DisableXmlDocs, inventoryBudget);
                        unavailable = shape.Opaque || !shape.IntrinsicTokens;
                        if (inventoryBudget.Exhausted) { Fail(); return; }
                    }
                    if (unavailable)
                    {
                        if (occurrence.Producer is FormattableStringExpression) { _unsafeOrUnavailableKnownInterpolatedText = true; }
                        else { _unsafeOrUnavailableKnownDirectiveConditions = true; }
                    }
                }
                if (occurrence.IsOpaque && occurrence.Producer is XmlDocProvider docs)
                {
                    if (++examinedBoundaries > 256 || !TrySpendOccurrenceLookup(occurrence.Id, _documentation.Count, inventoryBudget)) { Fail(); return; }
                    if (!TryReserveInsertion(_documentation, inventoryBudget)) { Fail(); return; }
                    if (PreparedGeneratedDocumentationEnvelope.TryCaptureForFileWithBudget(docs, inventoryBudget) is { } envelope)
                    { _documentation.Add(occurrence.Id, envelope); }
                    else if (inventoryBudget.Exhausted) { Fail(); return; }
                    else { HasUnavailableDocumentation = true; }
                    continue;
                }
                if (!occurrence.IsOpaque || !occurrence.EmissionBoundary || !IsWitnessedBoundary(occurrence.Producer)) { continue; }
                if (++examinedBoundaries > 256 || !TrySpendOccurrenceLookup(occurrence.Id, _segments.Count, inventoryBudget)) { Fail(); return; }
                if (!TryReserveInsertion(_segments, inventoryBudget)) { Fail(); return; }
                if (Node.TryCapture(occurrence.Producer, true, inventoryBudget) is { } node)
                {
                    // Flattening and immutable boundary copies are bounded by
                    // the actual captured node count; pay before allocation.
                    long boundaryWork = 32L * node.SubtreeCount + 16;
                    if (boundaryWork > int.MaxValue || !inventoryBudget.TrySpend((int)boundaryWork)) { Fail(); return; }
                    var segment = new Segment(this, occurrence, node);
                    _segments.Add(occurrence.Id, segment);
                    var segmentCurrent = segment.IsCurrentForPoc(inventoryBudget);
                    if (inventoryBudget.Exhausted) { Fail(); return; }
                    if (!segmentCurrent || !node.CallAndMemberRawNamesAreSafe || !node.AttributeRawKeysAreSafe || !node.ParameterAndFieldRawNamesAreSafe)
                    { _unsafeOrUnavailableKnownRawNames = true; }
                    if (!segmentCurrent || !node.OperatorRawTokensAreSafe) { _unsafeOrUnavailableKnownRawOperators = true; }
                    if (!segmentCurrent || !node.KeywordRawTokensAreSafe) { _unsafeOrUnavailableKnownRawKeywords = true; }
                    if (!segmentCurrent || !node.InterpolatedRawTextIsSafe) { _unsafeOrUnavailableKnownInterpolatedText = true; }
                    if (!segmentCurrent || !node.DirectiveConditionsAreSafe) { _unsafeOrUnavailableKnownDirectiveConditions = true; }
                }
                else
                {
                    if (inventoryBudget.Exhausted) { Fail(); return; }
                    // A failed witnessed subtree can conceal any observed raw
                    // fragment family; no partial fact may appear safe.
                    _unsafeOrUnavailableKnownRawNames = true;
                    _unsafeOrUnavailableKnownRawOperators = true;
                    _unsafeOrUnavailableKnownRawKeywords = true;
                    _unsafeOrUnavailableKnownInterpolatedText = true;
                    _unsafeOrUnavailableKnownDirectiveConditions = true;
                }
            }
        }

        private static bool TryPush(Stack<PreparedSourceOccurrence> pending, PreparedSourceOccurrence node,
            PocWorkBudget budget, ref int capacity)
        {
            if (!budget.TrySpend()) { return false; }
            if (pending.Count == capacity)
            {
                long next = capacity == 0 ? 4 : 2L * capacity;
                if (next > int.MaxValue || !budget.TrySpend((int)next) || !budget.TrySpend(pending.Count)) { return false; }
                capacity = (int)next;
            }
            pending.Push(node);
            return true;
        }

        private static bool TryAppend<T>(List<T> list, T item, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return false; }
            if (list.Count == list.Capacity)
            {
                long next = list.Capacity == 0 ? 4 : 2L * list.Capacity;
                if (next > int.MaxValue || !budget.TrySpend((int)next) || !budget.TrySpend(list.Count)) { return false; }
            }
            list.Add(item);
            return true;
        }

        private static bool TryReserveInsertion<T>(Dictionary<SourceOccurrenceId, T> values, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return false; }
            if (values.Count < values.EnsureCapacity(0)) { return true; }
            // Both arrays, old-entry copies and bucket rethreading. For this
            // bounded (at most 256 entries) map the next prime is below 3(n+1).
            long growth = 6L * (values.Count + 1L) + 2L * values.Count + 2;
            return growth <= int.MaxValue && budget.TrySpend((int)growth);
        }

        internal static PreparedPocSupplementalFile Capture(PreparedSourceFile file, PocWorkBudget? inventoryBudget = null) => new(file, inventoryBudget);
        internal Segment? GetSegment(SourceOccurrenceId occurrence, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            return TrySpendOccurrenceLookup(occurrence, _segments.Count, budget) && _segments.TryGetValue(occurrence, out var segment) ? segment : null;
        }
        internal PreparedGeneratedDocumentationEnvelope? GetDocumentation(SourceOccurrenceId occurrence, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            return TrySpendOccurrenceLookup(occurrence, _documentation.Count, budget) && _documentation.TryGetValue(occurrence, out var envelope) ? envelope : null;
        }

        private static bool TrySpendOccurrenceLookup(SourceOccurrenceId occurrence, int count, PocWorkBudget budget)
        {
            if (occurrence is null) { return false; }
            // Record hashing reads all four string fields even for an identical
            // key reference. Also reserve every possible collision comparison.
            long keyWork = 5L + (occurrence.File?.Length ?? 0) + (occurrence.MemberPath?.Length ?? 0) +
                (occurrence.RootRole?.Length ?? 0) + (occurrence.EdgePath?.Length ?? 0);
            long work = keyWork * (count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        private static bool TrySpendOccurrenceComparison(SourceOccurrenceId? left, SourceOccurrenceId? right, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return false; }
            if (ReferenceEquals(left, right) || left is null || right is null) { return true; }
            return TrySpendStringComparison(left.File, right.File, budget) && TrySpendStringComparison(left.MemberPath, right.MemberPath, budget) &&
                TrySpendStringComparison(left.RootRole, right.RootRole, budget) && TrySpendStringComparison(left.EdgePath, right.EdgePath, budget);
        }

        private static bool TrySpendStringComparison(string? left, string? right, PocWorkBudget budget)
        {
            if (ReferenceEquals(left, right) || left is null || right is null || left.Length != right.Length) { return budget.TrySpend(); }
            long work = (long)left.Length + right.Length + 1;
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }
        internal bool IsFor(PreparedSourceFile file) => ReferenceEquals(_file, file);
        internal bool IsRejected => Volatile.Read(ref _failed) != 0;
        // Narrow, precomputed veto for known call/member/signature, field,
        // parameter and attribute-key branches.
        // Other raw producers, type headers and grammar remain separate.
        internal bool HasUnsafeOrUnavailableKnownRawNames
        {
            get { ObserveCurrent(); return _unsafeOrUnavailableKnownRawNames || IsRejected; }
        }
        // This is an operator-text veto only; operand boundaries and grammar
        // are not certified by the bounded string whitelist.
        internal bool HasUnsafeOrUnavailableKnownRawOperators
        {
            get { ObserveCurrent(); return _unsafeOrUnavailableKnownRawOperators || IsRejected; }
        }
        // A keyword-text veto is not a statement or expression grammar proof.
        internal bool HasUnsafeOrUnavailableKnownRawKeywords
        {
            get { ObserveCurrent(); return _unsafeOrUnavailableKnownRawKeywords || IsRejected; }
        }
        // These are partial raw-text vetoes. A safe directive condition still
        // remains a directive, and neither fact proves enclosing grammar.
        internal bool HasUnsafeOrUnavailableKnownInterpolatedText
        {
            get { ObserveCurrent(); return _unsafeOrUnavailableKnownInterpolatedText || IsRejected; }
        }
        internal bool HasUnsafeOrUnavailableKnownDirectiveConditions
        {
            get { ObserveCurrent(); return _unsafeOrUnavailableKnownDirectiveConditions || IsRejected; }
        }
        internal bool IsDocumentationConsumedExactlyOnce(SourceOccurrenceId occurrence, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            if (!budget.TrySpend() || !ObserveCurrent(budget)) { return false; }
            if (HasUnavailableDocumentation || Volatile.Read(ref _completed) != 1 || Volatile.Read(ref _confirmed) != 1 ||
                Volatile.Read(ref _failed) != 0 || _preparation?.IsObservedPocProviderWriteStreamComplete != true ||
                !TrySpendOccurrenceLookup(occurrence, _documentation.Count, budget) || !_documentation.TryGetValue(occurrence, out var envelope) ||
                !envelope.IsCurrentWithoutInvalidating(budget) || _sharedConsumed.IsDefault) { return false; }
            var matches = 0;
            foreach (var id in _sharedConsumed)
            {
                if (!TrySpendOccurrenceComparison(id, occurrence, budget)) { return false; }
                if (id == occurrence && ++matches > 1) { return false; }
            }
            return matches == 1;
        }

        private static bool IsWitnessedBoundary(object producer) => producer switch
        {
            InvokeMethodExpression call => ConstructedArgumentWitness.Read(call).HasValue,
            ScopedApi wrapper when ConstructedArgumentWitness.IsAuditedWrapper(wrapper) && wrapper.Original is InvokeMethodExpression call =>
                ConstructedArgumentWitness.Read(call).HasValue,
            _ => false
        };

        internal void ObserveCurrent()
        {
            var budget = new PocWorkBudget(65536);
            if (!ObserveCurrent(budget) && budget.Exhausted) { Fail(); }
        }

        private bool ObserveCurrent(PocWorkBudget budget)
        {
            if (Volatile.Read(ref _failed) != 0 || !budget.TrySpend()) { return false; }
            if (!_file.IsShapeCurrentWithoutInvalidating(budget)) { return RejectObservedMismatch(budget); }
            // Dictionary value views can be initialized lazily; pay before
            // accessing either view or starting its enumeration.
            if (!budget.TrySpend()) { return false; }
            foreach (var segment in _segments.Values)
            {
                if (!budget.TrySpend()) { return false; }
                if (!segment.IsCurrentForPoc(budget)) { return RejectObservedMismatch(budget); }
            }
            if (!budget.TrySpend()) { return false; }
            foreach (var envelope in _documentation.Values)
            {
                if (!budget.TrySpend()) { return false; }
                if (!envelope.IsCurrentWithoutInvalidating(budget)) { return RejectObservedMismatch(budget); }
            }
            foreach (var field in _safeFields)
            {
                if (!budget.TrySpend()) { return false; }
                if (!field.IsCurrentWithoutInvalidating(budget)) { return RejectObservedMismatch(budget); }
            }
            foreach (var parameter in _safeParameters)
            {
                if (!budget.TrySpend()) { return false; }
                var shape = StructuredSourceShape.CaptureForPocSupplementCurrent(parameter, _file.DisableXmlDocs, budget);
                if (shape.Opaque || !shape.IntrinsicTokens || budget.Exhausted) { return RejectObservedMismatch(budget); }
            }
            foreach (var property in _safeProperties)
            {
                if (!budget.TrySpend()) { return false; }
                var shape = StructuredSourceShape.CaptureForPocSupplementCurrent(property, _file.DisableXmlDocs, budget);
                if (shape.Opaque || !shape.IntrinsicTokens || budget.Exhausted) { return RejectObservedMismatch(budget); }
            }
            return true;
        }

        private bool RejectObservedMismatch(PocWorkBudget budget)
        {
            if (!budget.Exhausted) { Fail(); }
            return false;
        }

        private void Fail() => Interlocked.Exchange(ref _failed, 1);

        internal Cursor? Start(PreparedFieldTypes preparation, TypeProviderWriter writer)
        {
            if (!preparation.OwnsPocSupplementalWrite(_file, writer)) { return null; }
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) { Fail(); return null; }
            _preparation = preparation;
            ObserveCurrent();
            return new Cursor(this, preparation, writer);
        }

        internal void ConfirmWrite(PreparedFieldTypes preparation, TypeProviderWriter writer)
        {
            if (!ReferenceEquals(_preparation, preparation) || Volatile.Read(ref _completed) != 1 ||
                !preparation.OwnsPocSupplementalWrite(_file, writer)) { Fail(); return; }
            Interlocked.Exchange(ref _confirmed, 1);
        }

        internal sealed class Segment
        {
            private readonly PreparedPocSupplementalFile _owner;
            private readonly PreparedSourceOccurrence _occurrence;
            private readonly Node _node;
            private readonly ImmutableArray<Boundary> _boundaries;
            private int _stale;
            internal int Consumed;

            internal Segment(PreparedPocSupplementalFile owner, PreparedSourceOccurrence occurrence, Node node)
            {
                _owner = owner; _occurrence = occurrence; _node = node;
                var boundaries = new List<Boundary>();
                node.Flatten(boundaries);
                _boundaries = boundaries.ToImmutableArray();
            }
            internal SourceOccurrenceId Id => _occurrence.Id;
            // Only raw call/member name branches in this bounded subtree.
            // This does not certify other tokens, grammar, layout or binding.
            internal bool CallAndMemberRawNamesAreSafe => IsCurrent && _node.CallAndMemberRawNamesAreSafe;
            internal bool KnownRawNamesAreSafe => IsCurrent && _node.CallAndMemberRawNamesAreSafe && _node.AttributeRawKeysAreSafe && _node.ParameterAndFieldRawNamesAreSafe;
            internal bool KnownRawOperatorsAreSafe => IsCurrent && _node.OperatorRawTokensAreSafe;
            internal bool KnownRawKeywordsAreSafe => IsCurrent && _node.KeywordRawTokensAreSafe;
            internal bool KnownRawInterpolatedTextIsSafe => IsCurrent && _node.InterpolatedRawTextIsSafe;
            internal bool KnownDirectiveConditionsAreSafe => IsCurrent && _node.DirectiveConditionsAreSafe;
            // Structural directive syntax is a retention signal. Absence is
            // never inferred until all raw emission and surrounding grammar
            // have their own proof; unsafe text can itself contain directives.
            internal bool? ContainsDirectives => IsCurrent && _node.ContainsDirectives ? true : null;
            internal bool IsCurrent
            {
                get
                {
                    if (Volatile.Read(ref _stale) != 0) { return false; }
                    var current = _node.IsCurrent();
                    if (!current) { Interlocked.Exchange(ref _stale, 1); }
                    return current;
                }
            }
            internal bool IsCurrentForPoc(PocWorkBudget budget)
            {
                if (Volatile.Read(ref _stale) != 0 || !budget.TrySpend()) { return false; }
                var current = _node.IsCurrent(new PocWorkBudget(4096, budget));
                if (!current && !budget.Exhausted) { Interlocked.Exchange(ref _stale, 1); }
                return current;
            }

            internal bool WasConsumedExactlyOnce
            {
                get
                {
                    // A queried mismatch is an observed POC-only failure, even
                    // if the input is restored before a later receipt query.
                    _owner.ObserveCurrent();
                    return Volatile.Read(ref _owner._completed) == 1 && Volatile.Read(ref _owner._confirmed) == 1 &&
                        Volatile.Read(ref _owner._failed) == 0 && _owner._preparation?.IsObservedPocProviderWriteStreamComplete == true &&
                        Consumed == 1 && IsCurrent;
                }
            }
            internal bool MatchesOccurrence(PreparedSourceOccurrence occurrence) => ReferenceEquals(_occurrence, occurrence);
            internal ImmutableArray<Boundary> Boundaries => _boundaries;
        }

        internal sealed record Boundary(object Producer, int End);

        internal sealed class Node(object producer, bool boundary, StructuredSourceShape shape, ImmutableArray<Node> children, int subtreeCount)
        {
            internal int SubtreeCount => subtreeCount;

            // Preserve the existing standalone 4,096-credit capture profile.
            internal static Node? TryCapture(object producer, bool boundary, HashSet<object> ancestors, int depth) =>
                TryCaptureCore(producer, boundary, ancestors, depth, new PocWorkBudget(4096));

            internal static Node? TryCapture(object producer, bool boundary, PocWorkBudget parentBudget)
            {
                if (!parentBudget.TrySpend(2)) { return null; }
                var childBudget = new PocWorkBudget(4096, parentBudget);
                try { return TryCaptureCore(producer, boundary, new HashSet<object>(ReferenceEqualityComparer.Instance), 0, childBudget); }
                finally { childBudget.DetachParent(); }
            }

            private static Node? TryCaptureCore(object producer, bool boundary, HashSet<object> ancestors, int depth, PocWorkBudget budget)
            {
                if (depth >= 256 || !budget.TrySpend() ||
                    !budget.TrySpendReferenceLookup(ancestors.Count) ||
                    !budget.TrySpendReferenceLookup(ancestors.Count + 1) || !ancestors.Add(producer)) { return null; }
                try
                {
                    var shape = StructuredSourceShape.CaptureForPocSupplement(producer, disableXmlDocs: false, budget);
                    if (shape.Opaque || budget.Exhausted) { return null; }
                    if (!budget.TrySpend(shape.Children.Length + 2)) { return null; }
                    var children = ImmutableArray.CreateBuilder<Node>(shape.Children.Length);
                    var subtreeCount = 1;
                    foreach (var child in shape.Children)
                    {
                        if (TryCaptureCore(child.Producer, child.EmissionBoundary, ancestors, depth + 1, budget) is not { } captured) { return null; }
                        subtreeCount += captured.SubtreeCount;
                        children.Add(captured);
                    }
                    if (!budget.TrySpend()) { return null; }
                    return new Node(producer, boundary, shape, children.MoveToImmutable(), subtreeCount);
                }
                finally { ancestors.Remove(producer); }
            }

            internal bool CallAndMemberRawNamesAreSafe =>
                (producer is not InvokeMethodExpression and not MemberExpression || shape.IntrinsicTokens) &&
                children.All(child => child.CallAndMemberRawNamesAreSafe);

            internal bool AttributeRawKeysAreSafe =>
                (producer is not AttributeStatement || shape.IntrinsicTokens) &&
                children.All(child => child.AttributeRawKeysAreSafe);

            internal bool ParameterAndFieldRawNamesAreSafe =>
                (producer is not ParameterProvider and not FieldProvider and not PropertyProvider and not DeclareLocalFunctionStatement || shape.IntrinsicTokens) &&
                children.All(child => child.ParameterAndFieldRawNamesAreSafe);

            internal bool OperatorRawTokensAreSafe =>
                (producer is not BinaryOperatorExpression and not UnaryOperatorExpression || shape.IntrinsicTokens) &&
                children.All(child => child.OperatorRawTokensAreSafe);

            internal bool KeywordRawTokensAreSafe =>
                (producer is not KeywordExpression || shape.IntrinsicTokens) &&
                children.All(child => child.KeywordRawTokensAreSafe);

            internal bool InterpolatedRawTextIsSafe =>
                (producer is not FormattableStringExpression || shape.IntrinsicTokens) &&
                children.All(child => child.InterpolatedRawTextIsSafe);

            internal bool DirectiveConditionsAreSafe =>
                (producer is not IfElsePreprocessorStatement || shape.IntrinsicTokens) &&
                children.All(child => child.DirectiveConditionsAreSafe);

            // Structural presence only; false is not physical absence.
            internal bool ContainsDirectives => shape.HasDirectives || children.Any(child => child.ContainsDirectives);

            internal bool IsCurrent() => IsCurrent(new PocWorkBudget(4096));

            internal bool IsCurrent(PocWorkBudget budget)
            {
                if (!budget.TrySpend() || !shape.MatchesForPoc(StructuredSourceShape.CaptureForPocSupplementCurrent(producer, disableXmlDocs: false, budget), budget)) { return false; }
                foreach (var child in children) { if (!child.IsCurrent(budget)) { return false; } }
                return true;
            }

            internal void Flatten(List<Boundary> boundaries)
            {
                var index = -1;
                if (boundary) { index = boundaries.Count; boundaries.Add(new Boundary(producer, 0)); }
                foreach (var child in children) { child.Flatten(boundaries); }
                if (index >= 0) { boundaries[index] = new Boundary(producer, boundaries.Count); }
            }
        }

        internal sealed class Cursor(PreparedPocSupplementalFile owner, PreparedFieldTypes preparation, TypeProviderWriter writer)
        {
            private readonly Stack<Boundary> _stack = new();
            private Segment? _active;
            private int _next;

            internal IDisposable Enter(object producer, PreparedSourceOccurrence? sharedOccurrence)
            {
                if (Volatile.Read(ref owner._failed) != 0) { return PreparedSourceFile.EmptyScope.Instance; }
                if (_active is null)
                {
                    if (sharedOccurrence is null || !ReferenceEquals(sharedOccurrence.Producer, producer) ||
                        owner.GetSegment(sharedOccurrence.Id) is not { } segment) { return PreparedSourceFile.EmptyScope.Instance; }
                    if (!segment.MatchesOccurrence(sharedOccurrence) || segment.Consumed != 0 || !segment.IsCurrent)
                    { owner.Fail(); return PreparedSourceFile.EmptyScope.Instance; }
                    _active = segment; _next = 0;
                }
                if (sharedOccurrence is null || !_active.MatchesOccurrence(sharedOccurrence) || !_active.IsCurrent ||
                    !preparation.OwnsPocSupplementalWrite(owner._file, writer) || _next >= _active.Boundaries.Length ||
                    !ReferenceEquals(_active.Boundaries[_next].Producer, producer))
                { owner.Fail(); return PreparedSourceFile.EmptyScope.Instance; }
                var expected = _active.Boundaries[_next++];
                _stack.Push(expected);
                return new Scope(() => Exit(expected));
            }

            private void Exit(Boundary expected)
            {
                if (Volatile.Read(ref owner._failed) != 0) { return; }
                if (_active is null || _stack.Count == 0 || !ReferenceEquals(_stack.Peek(), expected) ||
                    _next != expected.End || !_active.IsCurrent || !preparation.OwnsPocSupplementalWrite(owner._file, writer))
                { owner.Fail(); return; }
                _stack.Pop();
                if (_stack.Count == 0)
                {
                    if (_next != _active.Boundaries.Length) { owner.Fail(); return; }
                    _active.Consumed++;
                    _active = null;
                }
            }

            internal void Complete(bool sharedCurrent, ImmutableArray<SourceOccurrenceId> sharedConsumed)
            {
                owner.ObserveCurrent();
                if (!sharedCurrent || Volatile.Read(ref owner._failed) != 0 || _active is not null || _stack.Count != 0 ||
                    owner._segments.Values.Any(segment => segment.Consumed != 1) ||
                    !preparation.OwnsPocSupplementalWrite(owner._file, writer))
                { owner.Fail(); return; }
                owner._sharedConsumed = sharedConsumed;
                if (Interlocked.CompareExchange(ref owner._completed, 1, 0) != 0) { owner.Fail(); }
            }
        }

        private sealed class Scope(Action exit) : IDisposable
        {
            private Action? _exit = exit;
            public void Dispose() => Interlocked.Exchange(ref _exit, null)?.Invoke();
        }
    }
}

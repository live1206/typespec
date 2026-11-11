// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Bounded capture of canonical native attribute IR, not a file/binding or
    // spelling certificate. It never formats, writes, or invokes a builder.
    internal sealed class PreparedPocAttributeEnvelope
    {
        private readonly IReadOnlyList<MethodBodyStatement> _carrier;
        private readonly TypeProvider? _nativeOwner;
        private readonly ImmutableArray<MethodBodyStatement> _attributes;
        private readonly ImmutableArray<Node> _nodes;
        private bool _stale;

        private PreparedPocAttributeEnvelope(IReadOnlyList<MethodBodyStatement> carrier,
            ImmutableArray<MethodBodyStatement> attributes, ImmutableArray<Node> nodes, TypeProvider? nativeOwner)
        { _carrier = carrier; _attributes = attributes; _nodes = nodes; _nativeOwner = nativeOwner; }

        internal IReadOnlyList<MethodBodyStatement> Carrier => _carrier;
        internal int Count => _attributes.Length;

        internal static PreparedPocAttributeEnvelope? TryCapture(IReadOnlyList<MethodBodyStatement>? attributes, PocWorkBudget budget, TypeProvider? nativeOwner = null)
        {
            if (attributes is null || !TryCopyCurrent(attributes, nativeOwner, budget, out var items)) { return null; }
            var nodes = ImmutableArray.CreateBuilder<Node>();
            var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var attribute in items)
            {
                if (!AttributeRoot(attribute, budget, ancestors, nodes, 0)) { return null; }
            }
            return new(attributes, items, nodes.ToImmutable(), nativeOwner);
        }

        private static bool AttributeRoot(MethodBodyStatement statement, PocWorkBudget budget, HashSet<object> ancestors,
            ImmutableArray<Node>.Builder nodes, int depth)
        {
            if (statement is null || depth > 32 || !budget.TrySpend()) { return false; }
            if (statement.GetType() == typeof(AttributeStatement)) { return CaptureNode(statement, budget, ancestors, nodes, depth); }
            if (statement.GetType() != typeof(SuppressionStatement)) { return false; }
            var suppression = (SuppressionStatement)statement;
            if (PreparedSuppressionEnvelope.TryCaptureForPoc(new[] { suppression }, budget) is not { IsCurrent: true } ||
                suppression.Inner is null) { return false; }
            // Check the inner root family as well as every actually represented
            // child. Suppression.WriteCore admits arbitrary statements natively;
            // this canonical attribute profile does not.
            return AttributeRoot(suppression.Inner, budget, ancestors, nodes, depth + 1) &&
                CaptureNode(suppression, budget, ancestors, nodes, depth);
        }

        private static bool CaptureNode(object producer, PocWorkBudget budget, HashSet<object> ancestors,
            ImmutableArray<Node>.Builder nodes, int depth)
        {
            // Reserve both identity probes before Add: a later Remove must
            // stay funded even when a descendant exhausts the shared budget.
            if (depth > 64 || !budget.TrySpendReferenceLookup(ancestors.Count) ||
                !budget.TrySpendReferenceLookup(ancestors.Count + 1) || !ancestors.Add(producer)) { return false; }
            try
            {
                // Admission is a current-state proof, not a construction receipt.
                var shape = StructuredSourceShape.CaptureForPocSupplementCurrent(producer, disableXmlDocs: true, budget);
                if (shape.Opaque || !shape.IntrinsicTokens || shape.HasMissingRequiredOperand) { return false; }
                nodes.Add(new(producer, shape));
                foreach (var child in shape.Children)
                {
                    if (!CaptureNode(child.Producer, budget, ancestors, nodes, depth + 1)) { return false; }
                }
                return true;
            }
            finally { ancestors.Remove(producer); }
        }

        private static bool TryCopyCurrent(IReadOnlyList<MethodBodyStatement> carrier, TypeProvider? nativeOwner, PocWorkBudget budget,
            out ImmutableArray<MethodBodyStatement> items)
        {
            if (ContractCollections.TryCopyForPoc(carrier, budget, out items)) { return true; }
            return !budget.Exhausted && nativeOwner is not null && nativeOwner.TryCopyCurrentPocAttributeProjection(carrier, budget, out items);
        }

        internal bool IsCurrent(PocWorkBudget budget)
        {
            if (_stale || !TryCopyCurrent(_carrier, _nativeOwner, budget, out var currentAttributes) || currentAttributes.Length != _attributes.Length)
            { _stale = true; return false; }
            for (var i = 0; i < _attributes.Length; i++)
            { if (!ReferenceEquals(currentAttributes[i], _attributes[i])) { _stale = true; return false; } }
            foreach (var node in _nodes)
            {
                if (!budget.TrySpend()) { _stale = true; return false; }
                var current = StructuredSourceShape.CaptureForPocSupplementCurrent(node.Producer, disableXmlDocs: true, budget);
                if (current.Opaque || !current.IntrinsicTokens || current.HasMissingRequiredOperand || !node.Shape.MatchesForPoc(current, budget))
                { _stale = true; return false; }
            }
            return true;
        }

        private sealed record Node(object Producer, StructuredSourceShape Shape);
    }
}

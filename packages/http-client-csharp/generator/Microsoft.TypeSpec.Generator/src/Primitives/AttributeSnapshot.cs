// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Snippets;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal sealed class AttributeSnapshot
    {
        private readonly AttributeStatement _attribute;
        private readonly DeclaredTypeSnapshot? _type;
        private readonly ImmutableArray<ValueExpression> _arguments;
        private readonly ImmutableArray<KeyValuePair<string, ValueExpression>> _named;
        private readonly ImmutableArray<(ScopedApi Wrapper, long Revision)> _wrappers;

        private AttributeSnapshot(AttributeStatement attribute, DeclaredTypeSnapshot? type, ImmutableArray<ValueExpression> arguments,
            ImmutableArray<KeyValuePair<string, ValueExpression>> named)
        {
            _attribute = attribute;
            _type = type;
            _arguments = arguments;
            _named = named;
            _wrappers = arguments.Concat(named.Select(n => n.Value)).OfType<ScopedApi>().Select(w => (w, w.DeclarationRevision)).ToImmutableArray();
        }

        internal static bool TryCapture(AttributeStatement attribute, out AttributeSnapshot? snapshot) => TryCaptureCore(attribute, null, out snapshot);
        internal static bool TryCaptureForPoc(AttributeStatement attribute, PocWorkBudget budget, out AttributeSnapshot? snapshot) => TryCaptureCore(attribute, budget, out snapshot);

        private static bool TryCaptureCore(AttributeStatement attribute, PocWorkBudget? budget, out AttributeSnapshot? snapshot)
        {
            snapshot = null;
            if (attribute.Data is not null)
            {
                // The additional POC check cannot replay unbounded source
                // attribute projection machinery or force its lazy inputs.
                if (budget is not null) { return false; }
                if (!attribute.IsOriginalDataProjectionCurrent())
                {
                    return false;
                }
                snapshot = new(attribute, null, [], []);
                return true;
            }
            if (attribute.CachedType is not { } type ||
                !(budget is not null ? DeclaredTypeSnapshot.TryCaptureForPoc(type, budget, out var typeSnapshot) : DeclaredTypeSnapshot.TryCapture(type, out typeSnapshot)) ||
                !(budget is not null ? ContractCollections.TryCopyForPoc(attribute.CachedArguments, budget, out var arguments) : ContractCollections.TryCopy(attribute.CachedArguments, out arguments)) ||
                !(budget is not null ? ContractCollections.TryCopyForPoc(attribute.CachedPositionalArguments, budget, out var named) : ContractCollections.TryCopy(attribute.CachedPositionalArguments, out named)) ||
                arguments.Concat(named.Select(n => n.Value)).Any(e => !TryGetLiteral(e, out _))) { return false; }
            snapshot = new(attribute, typeSnapshot, arguments, named);
            return true;
        }

        internal static bool IsKnownWrapper(ScopedApi wrapper) => wrapper.GetType() == typeof(ScopedApi) ||
            wrapper.GetType().IsGenericType && wrapper.GetType().GetGenericTypeDefinition() == typeof(ScopedApi<>);

        internal static bool TryGetLiteral(ValueExpression expression, out object? value)
        {
            var inner = expression is ScopedApi wrapper && IsKnownWrapper(wrapper) ? wrapper.Original : expression;
            value = (inner as LiteralExpression)?.Literal;
            return inner is LiteralExpression;
        }

        internal bool IsCurrent() => _attribute.Data is not null ? _attribute.IsOriginalDataProjectionCurrent() : _type!.IsCurrent() &&
            _wrappers.All(w => w.Wrapper.DeclarationRevision == w.Revision) &&
            ContractCollections.SameReferences(_attribute.CachedArguments, _arguments) &&
            ContractCollections.TryCopy(_attribute.CachedPositionalArguments, out var named) && named.Length == _named.Length &&
            named.Select((item, index) => item.Key == _named[index].Key && ReferenceEquals(item.Value, _named[index].Value)).All(same => same);

        internal bool IsCurrentForPoc(PocWorkBudget budget)
        {
            if (!budget.TrySpend() || _attribute.Data is not null || _type is null || !_type.IsCurrentForPoc(budget) ||
                !budget.TrySpend(_wrappers.Length) || _wrappers.Any(w => w.Wrapper.DeclarationRevision != w.Revision) ||
                !ContractCollections.SameReferencesForPoc(_attribute.CachedArguments, _arguments, budget) ||
                !ContractCollections.TryCopyForPoc(_attribute.CachedPositionalArguments, budget, out var named) || named.Length != _named.Length) { return false; }
            for (int i = 0; i < named.Length; i++)
            {
                // Equal text in a replacement carrier is still variable work.
                // Identical key references require no character traversal.
                if (!ReferenceEquals(named[i].Key, _named[i].Key) &&
                    !budget.TrySpend(named[i].Key?.Length ?? 0)) { return false; }
                if (named[i].Key != _named[i].Key || !ReferenceEquals(named[i].Value, _named[i].Value)) { return false; }
            }
            return true;
        }
    }
}

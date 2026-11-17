// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // A pre-write grammar contract for one header fragment, not whole-file or
    // spelling permission. The native writer still materializes its own object.
    internal sealed class PreparedPocTypeConstraint
    {
        private readonly TypeProvider _owner;
        private readonly long _ownerRevision;
        private readonly CSharpType? _key;
        private readonly DeclaredTypeSnapshot? _keySnapshot;
        private readonly WhereExpression? _initial;
        private WhereExpression? _observed;
        private KeywordExpression? _keyword;
        private long _keywordRevision;
        private bool _seen, _stale, _active, _completed;
        private int _nextNode;

        private PreparedPocTypeConstraint(TypeProvider owner, CSharpType? key, DeclaredTypeSnapshot? snapshot)
        {
            _owner = owner; _ownerRevision = owner.DeclarationRevision;
            _key = key; _keySnapshot = snapshot; _initial = owner.CachedWhereClause;
        }

        internal static PreparedPocTypeConstraint? TryCapture(TypeProvider owner, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return null; }
            if (owner is ChangeTrackingDictionaryDefinition dictionary)
            {
                var key = dictionary.ConstraintKeyType;
                if (!KeyHasKnownNativeSpelling(key) || !DeclaredTypeSnapshot.TryCaptureForPoc(key, budget, out var snapshot)) { return null; }
                var plan = new PreparedPocTypeConstraint(owner, key, snapshot);
                return plan.IsCurrent ? plan : null;
            }
            // Null is not memoized. Only the exact base implementation proves
            // absence without calling the getter; unknown overrides veto.
            var method = owner.GetType().GetMethod("BuildWhereClause", BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            return method?.DeclaringType == typeof(TypeProvider) && owner.CachedWhereClause is null
                ? new PreparedPocTypeConstraint(owner, null, null) : null;
        }

        private static bool KeyHasKnownNativeSpelling(CSharpType key) =>
            key.GetType() == typeof(CSharpType) && key.IsFrameworkType &&
            key.FrameworkType.GetType() == typeof(object).GetType() && key.FrameworkType.IsGenericParameter &&
            key.FrameworkType.Name == "TKey" && key.Name == "TKey" && !key.IsNullable;

        private bool Matches(WhereExpression? where)
        {
            if (_key is null) { return where is null; }
            return where is not null && ReferenceEquals(where.Type, _key) &&
                ContractCollections.TryCopyForPoc(where.Constraints, new PocWorkBudget(32), out var constraints) &&
                constraints.Length == 1 && constraints[0] is KeywordExpression { Keyword: "notnull", Expression: null };
        }

        internal bool IsCurrent
        {
            get
            {
                if (_stale) { return false; }
                var current = _owner.CachedWhereClause;
                if (_owner.DeclarationRevision != _ownerRevision ||
                    _key is not null && (!KeyHasKnownNativeSpelling(_key) || _keySnapshot?.IsCurrentForPoc(new PocWorkBudget(4096)) != true) ||
                    _initial is not null && !ReferenceEquals(_initial, current) ||
                    _seen && (!ReferenceEquals(_observed, current) || _keyword is not null && _keyword.DeclarationRevision != _keywordRevision) ||
                    current is not null && !Matches(current) || _key is null && current is not null)
                { _stale = true; return false; }
                return true;
            }
        }

        // Only the canonical object obtained at the normal native getter point.
        // This confirms or vetoes the preplanned shape; it discovers no choice.
        internal void ObserveNative(WhereExpression? where)
        {
            if (_seen || !IsCurrent || !ReferenceEquals(_owner.CachedWhereClause, where) || !Matches(where))
            { _stale = true; return; }
            _seen = true; _observed = where;
            if (where is not null && ContractCollections.TryCopyForPoc(where.Constraints, new PocWorkBudget(32), out var constraints))
            { _keyword = (KeywordExpression)constraints[0]; _keywordRevision = _keyword.DeclarationRevision; }
        }

        internal IDisposable BeginNativeRoot()
        {
            if (_active || _completed || !_seen || _key is null || !IsCurrent)
            { _stale = true; return PreparedSourceFile.EmptyScope.Instance; }
            _active = true;
            _nextNode = 0;
            return new NativeRoot(this);
        }

        internal void ObserveNativeProducer(object producer)
        {
            if (!_active) { return; }
            if (!IsCurrent || _nextNode > 1 ||
                !ReferenceEquals(producer, _nextNode == 0 ? _observed : _keyword)) { _stale = true; }
            _nextNode++;
        }

        private sealed class NativeRoot(PreparedPocTypeConstraint plan) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) { return; }
                _disposed = true;
                if (plan._nextNode != 2 || !plan.IsCurrent) { plan._stale = true; }
                else { plan._completed = true; }
                plan._active = false;
            }
        }

        internal bool ExpectsEmission => _key is not null;
        internal bool HasObservedExpectedEmission => _seen && (_key is null || _completed) && IsCurrent;
    }
}

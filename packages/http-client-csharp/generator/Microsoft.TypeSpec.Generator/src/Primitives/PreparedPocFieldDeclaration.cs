// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // POC-only currentness for a field declaration already certified by the
    // raw-name shape. A first-seen lazy value is not proof of first identity;
    // separate owner currentness remains mandatory. This does not establish
    // scope, grammar or permission.
    internal sealed class PreparedPocFieldDeclaration
    {
        private readonly FieldProvider _field;
        private readonly string _name;
        private CodeWriterDeclaration? _declaration;
        private readonly long _revision;
        private int _stale;

        private PreparedPocFieldDeclaration(FieldProvider field, string name, CodeWriterDeclaration? declaration, long revision)
        {
            _field = field; _name = name; _declaration = declaration; _revision = revision;
        }

        internal static PreparedPocFieldDeclaration? TryCapture(FieldProvider field, PocWorkBudget budget)
        {
            var name = field.Name;
            if (name is null || !budget.TrySpend(name.Length)) { return null; }
            var declaration = field.CachedDeclaration;
            if (declaration is null) { return new(field, name, null, 0); }
            var requested = declaration.RequestedName;
            if (requested is null || !budget.TrySpend(requested.Length) || !string.Equals(requested, name, StringComparison.Ordinal)) { return null; }
            return new(field, name, declaration, declaration.SourceRevision);
        }

        internal bool IsCurrent(PocWorkBudget budget) => IsCurrentCore(budget, invalidate: true);
        internal bool IsCurrentWithoutInvalidating(PocWorkBudget budget) => IsCurrentCore(budget, invalidate: false);

        private bool IsCurrentCore(PocWorkBudget budget, bool invalidate)
        {
            if (Volatile.Read(ref _stale) != 0) { return false; }
            var currentName = _field.Name;
            if (currentName is null || !budget.TrySpend(_name.Length) || !budget.TrySpend(currentName.Length) ||
                !string.Equals(currentName, _name, StringComparison.Ordinal)) { return Reject(invalidate); }
            var current = _field.CachedDeclaration;
            var observed = Volatile.Read(ref _declaration);
            if (observed is null)
            {
                // The native writer can construct this nonvirtual lazy value
                // once, using the exact field name and zero initial revision.
                if (current is null) { return true; }
                var requested = current.RequestedName;
                if (requested is null || !budget.TrySpend(requested.Length) || current.SourceRevision != 0 ||
                    !string.Equals(requested, _name, StringComparison.Ordinal)) { return Reject(invalidate); }
                // Complete and fund the second live declaration check before
                // publishing its identity. An underfunded point query must
                // not latch a partial observation for a later query.
                var finalRequested = current.RequestedName;
                if (finalRequested is null || !budget.TrySpend(finalRequested.Length) || current.SourceRevision != _revision ||
                    !string.Equals(finalRequested, _name, StringComparison.Ordinal)) { return Reject(invalidate); }
                observed = Interlocked.CompareExchange(ref _declaration, current, null) ?? current;
                return ReferenceEquals(current, observed) || Reject(invalidate);
            }
            if (!ReferenceEquals(current, observed)) { return Reject(invalidate); }
            var currentRequested = observed.RequestedName;
            if (currentRequested is null || !budget.TrySpend(currentRequested.Length) || observed.SourceRevision != _revision ||
                !string.Equals(currentRequested, _name, StringComparison.Ordinal)) { return Reject(invalidate); }
            return true;
        }

        private bool Reject(bool invalidate)
        {
            if (invalidate) { Interlocked.Exchange(ref _stale, 1); }
            return false;
        }
    }
}

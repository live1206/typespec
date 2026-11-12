// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal sealed class DeclaredTypeSnapshot
    {
        internal CSharpType Identity { get; }
        internal string Name { get; }
        internal string Namespace { get; }
        internal bool IsNullable { get; }
        internal bool IsValueType { get; }
        internal ImmutableArray<DeclaredTypeSnapshot> Arguments { get; }
        private readonly long _version;
        private readonly DeclaredTypeSnapshot? _declaring;

        private DeclaredTypeSnapshot(CSharpType type, ImmutableArray<DeclaredTypeSnapshot> arguments, DeclaredTypeSnapshot? declaring)
        {
            Identity = type;
            Name = type.Name;
            Namespace = type.Namespace;
            IsNullable = type.IsNullable;
            IsValueType = type.IsValueType;
            Arguments = arguments;
            _version = type.IdentityVersion;
            _declaring = declaring;
        }

        internal static bool TryCapture(CSharpType type, out DeclaredTypeSnapshot? snapshot) =>
            TryCapture(type, new HashSet<CSharpType>(ReferenceEqualityComparer.Instance), int.MaxValue, null, out snapshot);

        // The extra POC-only traversal is bounded in both depth and total work,
        // including repeated-child graphs that are not cycles.
        internal static bool TryCaptureForPoc(CSharpType type, out DeclaredTypeSnapshot? snapshot) =>
            TryCaptureForPoc(type, new PocWorkBudget(4096), out snapshot);
        internal static bool TryCaptureForPoc(CSharpType type, PocWorkBudget budget, out DeclaredTypeSnapshot? snapshot) =>
            TryCapture(type, null, 256, budget, out snapshot);

        private static bool TryCapture(CSharpType type, HashSet<CSharpType>? ancestors, int remainingDepth, PocWorkBudget? budget,
            out DeclaredTypeSnapshot? snapshot)
        {
            snapshot = null;
            if (remainingDepth <= 0) { budget?.Reject(); return false; }
            // The POC walk needs no hash-table cycle probe: depth and charged
            // per-occurrence work bound every path, and a cyclic path cannot
            // complete a snapshot. Preserve the legacy profile's early cycle
            // detection; its otherwise-unbounded traversal still needs it.
            if (budget is not null && !budget.TrySpend() || type is null ||
                budget is null && !ancestors!.Add(type) || type.IsLiteral || type.HasUnionMetadata ||
                !(budget is null ? ContractCollections.TryCopy(type.Arguments, out var arguments) : ContractCollections.TryCopyForPoc(type.Arguments, budget, out arguments)))
            {
                return false;
            }
            var children = ImmutableArray.CreateBuilder<DeclaredTypeSnapshot>();
            foreach (var argument in arguments)
            {
                if (!TryCapture(argument, ancestors, remainingDepth - 1, budget, out var child)) { return false; }
                children.Add(child!);
            }
            DeclaredTypeSnapshot? declaring = null;
            if (type.DeclaringType is { } parent && !TryCapture(parent, ancestors, remainingDepth - 1, budget, out declaring)) { return false; }
            if (budget is null) { ancestors!.Remove(type); }
            snapshot = new(type, children.ToImmutable(), declaring);
            return true;
        }

        // Prepay the existing shared check, including its expected-reference
        // projection, before it executes. This is not a richer POC snapshot.
        internal bool TryReserveSharedCurrentness(PocWorkBudget budget, int depth = 0)
        {
            if (depth >= 256) { budget.Reject(); return false; }
            if (!budget.TrySpend() || !budget.TrySpendStringComparison(Identity.Name, Name) ||
                !budget.TrySpendStringComparison(Identity.Namespace, Namespace) || !budget.TrySpend(Arguments.Length) ||
                !ContractCollections.ReserveSharedReferencesForPoc(Identity.Arguments, Arguments.Length, budget)) { return false; }
            foreach (var argument in Arguments) { if (!argument.TryReserveSharedCurrentness(budget, depth + 1)) { return false; } }
            return _declaring is null || _declaring.TryReserveSharedCurrentness(budget, depth + 1);
        }

        internal bool IsCurrent() => Identity.IdentityVersion == _version && Identity.Name == Name && Identity.Namespace == Namespace &&
            ContractCollections.SameReferences(Identity.Arguments, Arguments.Select(a => a.Identity).ToImmutableArray()) &&
            Arguments.All(a => a.IsCurrent()) && ReferenceEquals(Identity.DeclaringType, _declaring?.Identity) && (_declaring?.IsCurrent() ?? true);

        internal bool IsCurrentForPoc(PocWorkBudget budget, int depth = 0)
        {
            if (depth >= 256) { budget.Reject(); return false; }
            if (!budget.TrySpend() || Arguments.Length > budget.Remaining ||
                Identity.IdentityVersion != _version || Identity.Name != Name || Identity.Namespace != Namespace ||
                !ContractCollections.TryCopyForPoc(Identity.Arguments, budget, out var currentArguments) || currentArguments.Length != Arguments.Length) { return false; }
            for (int i = 0; i < currentArguments.Length; i++)
            {
                if (!ReferenceEquals(currentArguments[i], Arguments[i].Identity)) { return false; }
            }
            foreach (var argument in Arguments) { if (!argument.IsCurrentForPoc(budget, depth + 1)) { return false; } }
            return ReferenceEquals(Identity.DeclaringType, _declaring?.Identity) && (_declaring?.IsCurrentForPoc(budget, depth + 1) ?? true);
        }
    }
}

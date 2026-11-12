// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>Confinement and freshness of standard pragma data, without executing expressions.</summary>
    internal sealed class PreparedSuppressionEnvelope
    {
        private readonly List<Func<PocWorkBudget?, bool>> _checks = [];
        private int _stale;

        internal static PreparedSuppressionEnvelope? TryCapture(IReadOnlyList<SuppressionStatement> values)
            => TryCaptureCore(values, null);

        internal static PreparedSuppressionEnvelope? TryCaptureForPoc(IReadOnlyList<SuppressionStatement> values, PocWorkBudget budget)
            => TryCaptureCore(values, budget);

        private static PreparedSuppressionEnvelope? TryCaptureCore(IReadOnlyList<SuppressionStatement> values, PocWorkBudget? budget)
        {
            ImmutableArray<SuppressionStatement> snapshot;
            if (budget is null)
            {
                if (!ContractCollections.TryCopy(values, out snapshot)) { return null; }
            }
            else if (!ContractCollections.TryCopyForPoc(values, budget, out snapshot)) { return null; }
            var result = new PreparedSuppressionEnvelope();
            result._checks.Add(budget is null
                ? queryBudget => SourceCurrentnessChecks.References(values, snapshot, queryBudget)
                : queryBudget => ContractCollections.SameReferencesForPoc(values, snapshot, queryBudget ?? new PocWorkBudget(snapshot.Length)));
            foreach (var suppression in snapshot)
            {
                if (suppression is null || suppression.GetType() != typeof(SuppressionStatement) || !SingleLine(suppression.Justification, budget)) { return null; }
                ValueExpression code = suppression.Code;
                var depth = 0;
                while (code is ScopedApi wrapper && PreparedCallProofs.IsTransparentWrapper(wrapper))
                {
                    // The native pragma extracts a direct literal or ONE typed
                    // string wrapper only; other wrappers use record ToString.
                    // Keep the inherited shared profile unchanged.
                    if (budget is not null && (wrapper.GetType() != typeof(ScopedApi<string>) || wrapper.Original is not LiteralExpression)) { return null; }
                    if (depth++ >= 32 || (budget is not null && !budget.TrySpend())) { return null; }
                    var original = wrapper.Original;
                    var revision = wrapper.DeclarationRevision;
                    result._checks.Add(_ => wrapper.DeclarationRevision == revision && ReferenceEquals(wrapper.Original, original));
                    code = original;
                }
                // The pragma writer extracts ordinary strings raw. Other values
                // can execute ToString, formatting or arbitrary expression writers.
                if (code is not LiteralExpression { Literal: string text } || !SingleLine(text, budget)) { return null; }
            }
            return result;
        }

        internal bool IsCurrentWithoutInvalidating(PocWorkBudget? budget = null) => IsCurrentCore(invalidate: false, budget);
        internal bool IsCurrent => IsCurrentCore(invalidate: true);

        private bool IsCurrentCore(bool invalidate, PocWorkBudget? budget = null)
        {
            if (System.Threading.Volatile.Read(ref _stale) != 0 || !SourceCurrentnessChecks.Spend(budget)) { return false; }
            if (_checks.All(check => SourceCurrentnessChecks.Spend(budget) && check(budget))) { return true; }
            if (invalidate) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return false;
        }

        private static bool SingleLine(string value, PocWorkBudget? budget)
        {
            // Keep the shared legacy admission/exception path unchanged. The
            // additional POC path charges lengths BEFORE any character scan.
            if (budget is not null && (value is null || !budget.TrySpend(value.Length))) { return false; }
            return SingleLine(value);
        }

        private static bool SingleLine(string value) => value.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) < 0;
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>
    /// The POC's no-enclosing-call source case for a direct local declaration
    /// type. This is source-observation evidence only, not a name decision,
    /// import/lookup proof, whole-file confinement, or writer permission.
    /// Other statement shapes and enclosing calls remain unsupported here.
    /// </summary>
    internal sealed class PreparedOrdinaryTypeSourceObservation
    {
        private readonly PreparedSourceFile _file;
        private readonly PreparedSourceOccurrence[] _path;
        private int _stale;

        private PreparedOrdinaryTypeSourceObservation(PreparedSourceFile file, PreparedSourceOccurrence[] path)
        {
            _file = file;
            _path = path;
        }

        internal PreparedSourceOccurrence Occurrence => _path[^1];
        internal MethodProvider Method => (MethodProvider)_path[0].Producer;

        internal static PreparedOrdinaryTypeSourceObservation? TryCapture(PreparedSourceFile file, PreparedSourceOccurrence occurrence,
            PocWorkBudget? budget = null, bool pathKnownNotDirect = false)
        {
            budget ??= new PocWorkBudget(65536);
            if (budget.Exhausted || !budget.TrySpend() || occurrence.Producer is not DeclarationExpression declaration || occurrence.IsOpaque ||
                declaration.IsOut || declaration.Variable.GetType() != typeof(VariableExpression)) { return null; }
            // A captured occurrence path that cannot have the required five
            // producer shapes can never pass IsDirectDeclarationPath. Keep the
            // original initial guards (including their exception boundary)
            // before declining its otherwise redundant root/path search.
            if (pathKnownNotDirect) { return null; }
            PreparedSourceOccurrence? selectedRoot = null;
            foreach (var root in file.Roots)
            {
                if (!budget.TrySpend() || !budget.TrySpendStringComparison(root.Id.RootRole, "method")) { return null; }
                if (root.Id.RootRole != "method" || root.Id.RootIndex != occurrence.Id.RootIndex) { continue; }
                if (!budget.TrySpendStringComparison(root.Id.MemberPath, occurrence.Id.MemberPath)) { return null; }
                if (root.Id.MemberPath != occurrence.Id.MemberPath) { continue; }
                if (selectedRoot is not null) { return null; }
                selectedRoot = root;
            }
            if (selectedRoot is null || !budget.TrySpend()) { return null; }
            var path = new List<PreparedSourceOccurrence>();
            if (!FindPath(selectedRoot, occurrence, path, budget, 0) || !ReserveDirectPath(path, budget) ||
                !IsDirectDeclarationPath(file, path) || !budget.TrySpend(path.Count + 2)) { return null; }
            var result = new PreparedOrdinaryTypeSourceObservation(file, path.ToArray());
            return result.IsCurrentForPoc(budget) ? result : null;
        }

        internal bool IsCurrent => IsCurrentForPoc(new PocWorkBudget(65536));

        internal bool IsCurrentForPoc(PocWorkBudget budget)
        {
            if (Volatile.Read(ref _stale) != 0) { return false; }
            // Failure belongs to this additional POC observation. Keep every
            // full shared predicate, but never write shared stale flags here.
            if (budget.TrySpend() && _file.IsCurrentWithoutInvalidating(budget) && ReserveDirectPath(_path, budget) && IsDirectDeclarationPath(_file, _path)) { return true; }
            // An exhausted independent POC query has not observed a source
            // mismatch and must not poison later, adequately funded queries.
            if (!budget.Exhausted) { Interlocked.Exchange(ref _stale, 1); }
            return false;
        }

        private static bool ReserveDirectPath(IReadOnlyList<PreparedSourceOccurrence> path, PocWorkBudget budget)
        {
            if (!budget.TrySpend(path.Count)) { return false; }
            foreach (var node in path) { if (!budget.TrySpend(node.Children.Length)) { return false; } }
            return true;
        }

        private static bool FindPath(PreparedSourceOccurrence current, PreparedSourceOccurrence target,
            List<PreparedSourceOccurrence> path, PocWorkBudget budget, int depth)
        {
            if (depth >= 256) { budget.Reject(); return false; }
            // Pay for this node and worst-case List growth before adding it.
            if (!budget.TrySpend(path.Count + 2)) { return false; }
            path.Add(current);
            if (ReferenceEquals(current, target)) { return true; }
            foreach (var child in current.Children)
            {
                if (FindPath(child, target, path, budget, depth + 1)) { return true; }
                if (budget.Exhausted) { return false; }
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static bool IsDirectDeclarationPath(PreparedSourceFile file, IReadOnlyList<PreparedSourceOccurrence> path)
        {
            if (path.Count != 5 || path.Any(node => node.IsOpaque) ||
                path[0].Producer is not MethodProvider method || !ReferenceEquals(method.EnclosingType, file.FileOwner) ||
                path[1].Producer is not MethodBodyStatements block || block.GetType() != typeof(MethodBodyStatements) ||
                !ReferenceEquals(method.BodyStatements, block) ||
                path[2].Producer is not ExpressionStatement statement || statement.GetType() != typeof(ExpressionStatement) ||
                path[3].Producer is not AssignmentExpression assignment || assignment.UseNullCoalesce ||
                !ReferenceEquals(statement.Expression, assignment) ||
                path[4].Producer is not DeclarationExpression declaration || declaration.IsOut ||
                declaration.Variable.GetType() != typeof(VariableExpression) || !ReferenceEquals(assignment.Variable, declaration)) { return false; }

            // The declaration type is the assignment's target, not its value;
            // there is no enclosing invocation or lambda whose source could be
            // captured. The selected namespace prefix must independently be
            // proved single-line by the name plan.
            return ReferenceEquals(path[0].Children.SingleOrDefault(child => ReferenceEquals(child, path[1])), path[1]) &&
                ReferenceEquals(path[1].Children.SingleOrDefault(child => ReferenceEquals(child, path[2])), path[2]) &&
                ReferenceEquals(path[2].Children.SingleOrDefault(child => ReferenceEquals(child, path[3])), path[3]) &&
                ReferenceEquals(path[3].Children.SingleOrDefault(child => ReferenceEquals(child, path[4])), path[4]);
        }
    }
}

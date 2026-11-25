// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.TypeSpec.Generator
{
    // One disjoint partition for admitted caller captures, exact defaults and direct delegate targets.
    // Unsupported shapes keep their existing reduction policy.
    internal static class ScopedGeneratedReduction
    {
        internal static async Task<Document> ReduceAsync(Document document)
        {
            if (document.Project.DocumentIds.Count != 1 || await document.GetSyntaxRootAsync() is not { } root ||
                !root.HasAnnotation(Simplifier.Annotation))
            { return await Simplifier.ReduceAsync(document); }

            var callers = new List<InvocationExpressionSyntax>();
            var defaults = new List<EqualsValueClauseSyntax>();
            var delegates = new List<InvocationExpressionSyntax>();
            foreach (var node in root.DescendantNodes())
            {
                if (node is InvocationExpressionSyntax call)
                {
                    if (NestedCallerExpressionReduction.IsCandidate(call))
                    {
                        callers.Add(call);
                        if (callers.Count > 2) { return await Simplifier.ReduceAsync(document); }
                    }
                    else if (delegates.Count < 2 && DirectDelegateTargetReduction.IsCandidate(call))
                    { delegates.Add(call); }
                }
                else if (defaults.Count < 3 && node is EqualsValueClauseSyntax clause && GetExpectedDefault(clause) is not null)
                { defaults.Add(clause); }
            }
            bool hasDefaults = defaults.Count is 1 or 2;
            if (callers.Count == 0 && !hasDefaults && delegates.Count != 1 || await document.GetSemanticModelAsync() is not { } model ||
                !model.Compilation.SyntaxTrees.Take(2).SequenceEqual(new[] { model.SyntaxTree }))
            { return await Simplifier.ReduceAsync(document); }
            bool additionalAnnotations = (callers.Count == 2 || hasDefaults || delegates.Count == 1) &&
                root.GetAnnotatedNodesAndTokens(Simplifier.Annotation).Any(item => !ReferenceEquals(item.AsNode(), root));
            if (callers.Count == 2 && (callers[0].FullSpan.OverlapsWith(callers[1].FullSpan) || additionalAnnotations))
            { return await Simplifier.ReduceAsync(document); }

            var sites = new Dictionary<SyntaxNode, ReductionSite>(ReferenceEqualityComparer.Instance);
            foreach (var call in callers)
            {
                if (!NestedCallerExpressionReduction.TryBind(call, model, out string? entry, out string? target, out string? captured) ||
                    callers.Count == 2 && entry is null)
                { return await Simplifier.ReduceAsync(document); }
                sites.Add(call, new(new SyntaxAnnotation(), (node, finalModel) => node is InvocationExpressionSyntax finalCall &&
                    NestedCallerExpressionReduction.TryBind(finalCall, finalModel, out string? finalEntry, out string? finalTarget, out string? finalCapture) &&
                    entry == finalEntry && target == finalTarget && captured == finalCapture));
            }
            // Refusing optional defaults must not revoke an existing caller guard.
            if (!additionalAnnotations && hasDefaults)
            {
                var defaultSites = new List<(EqualsValueClauseSyntax Clause, ReductionSite Site)>(defaults.Count);
                foreach (var clause in defaults)
                {
                    if (!TryBindDefault(clause, model, out var identity)) { break; }
                    defaultSites.Add((clause, new(new SyntaxAnnotation(), (node, finalModel) => node is EqualsValueClauseSyntax finalClause &&
                        TryBindDefault(finalClause, finalModel, out var finalIdentity) && finalIdentity == identity)));
                }
                if (defaultSites.Count == defaults.Count)
                {
                    foreach (var (clause, site) in defaultSites) { sites.Add(clause, site); }
                }
            }
            // An unsupported delegate target does not revoke a caller or default guard.
            if (!additionalAnnotations && delegates.Count == 1 && DirectDelegateTargetReduction.TryBind(delegates[0], model, out var delegateIdentity))
            {
                sites.Add(delegates[0], new(new SyntaxAnnotation(), (node, finalModel) => node is InvocationExpressionSyntax finalCall &&
                    DirectDelegateTargetReduction.TryBind(finalCall, finalModel, out var finalIdentity) && delegateIdentity == finalIdentity));
            }
            if (sites.Count == 0) { return await Simplifier.ReduceAsync(document); }

            var protectedAncestors = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
            foreach (var site in sites.Keys)
            {
                foreach (var ancestor in site.AncestorsAndSelf()) { protectedAncestors.Add(ancestor); }
            }
            // A sibling of one site can contain another. Annotate only the
            // complement of all protected paths, and reduce that union once.
            var disjoint = sites.Keys.Concat(protectedAncestors.Where(node => !sites.ContainsKey(node))
                .SelectMany(node => node.ChildNodes().Where(child => !protectedAncestors.Contains(child))));
            var partitioned = root.ReplaceNodes(disjoint, (original, node) => sites.TryGetValue(original, out var site)
                ? node.WithAdditionalAnnotations(site.Tracking) : node.WithAdditionalAnnotations(Simplifier.Annotation))
                .WithoutAnnotations(Simplifier.Annotation);
            var reduced = await Simplifier.ReduceAsync(document.WithSyntaxRoot(partitioned));
            if (await reduced.GetSyntaxRootAsync() is not { } finalRoot || await reduced.GetSemanticModelAsync() is not { } finalModel)
            { throw new InvalidOperationException("Unable to verify the protected syntax during generated document reduction."); }
            foreach (var (original, site) in sites)
            {
                var tracked = finalRoot.GetAnnotatedNodes(site.Tracking).Take(2).ToArray();
                if (tracked.Length != 1 || !string.Equals(original.ToFullString(), tracked[0].ToFullString(), StringComparison.Ordinal) ||
                    !site.BindingMatches(tracked[0], finalModel))
                { throw new InvalidOperationException("Unable to verify the protected syntax during generated document reduction."); }
            }
            return reduced;
        }

        private static int? GetExpectedDefault(EqualsValueClauseSyntax clause)
        {
            if (clause.Parent is not ParameterSyntax { Type: PredefinedTypeSyntax type, Parent.Parent: MethodDeclarationSyntax } ||
                !type.Keyword.IsKind(SyntaxKind.IntKeyword) || clause.Value is not ParenthesizedExpressionSyntax grouped)
            { return null; }
            if (IsNumber(grouped.Expression, "1")) { return 1; }
            return grouped.Expression is BinaryExpressionSyntax { Right: ParenthesizedExpressionSyntax descendant } binary &&
                binary.IsKind(SyntaxKind.AddExpression) && IsNumber(binary.Left, "1") && IsNumber(descendant.Expression, "2") ? 3 : null;

            static bool IsNumber(ExpressionSyntax expression, string token) => expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Text == token;
        }

        private static bool TryBindDefault(EqualsValueClauseSyntax clause, SemanticModel model, out (string Method, int Ordinal, string Name, int Value) identity)
        {
            identity = default;
            if (clause.ContainsDiagnostics || clause.SyntaxTree != model.SyntaxTree || GetExpectedDefault(clause) is not { } expected ||
                model.GetDeclaredSymbol((ParameterSyntax)clause.Parent!) is not IParameterSymbol
                    { Type.SpecialType: SpecialType.System_Int32, RefKind: RefKind.None, IsOptional: true, HasExplicitDefaultValue: true,
                        ExplicitDefaultValue: int value, ContainingSymbol: IMethodSymbol method } parameter || value != expected ||
                method.GetDocumentationCommentId() is not { } methodId ||
                model.GetConstantValue(clause.Value) is not { HasValue: true, Value: int constant } || constant != value)
            { return false; }
            identity = (methodId, parameter.Ordinal, parameter.Name, value);
            return true;
        }

        private sealed record ReductionSite(SyntaxAnnotation Tracking, Func<SyntaxNode, SemanticModel, bool> BindingMatches);
    }
}

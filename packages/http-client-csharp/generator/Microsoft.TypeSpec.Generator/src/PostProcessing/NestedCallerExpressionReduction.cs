// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.TypeSpec.Generator
{
    // This profile protects up to two structured nested binary caller expressions in
    // a closed generated document. It is not a general CAE or formatter policy.
    internal static class NestedCallerExpressionReduction
    {
        internal static async Task<Document> ReduceAsync(Document document)
        {
            if (document.Project.DocumentIds.Count != 1 || await document.GetSyntaxRootAsync() is not { } root ||
                !root.HasAnnotation(Simplifier.Annotation))
            { return await Simplifier.ReduceAsync(document); }

            var candidates = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(call => TryGetShape(call, out _, out _, out _)).Take(3).ToArray();
            if (candidates.Length is 0 or > 2 || await document.GetSemanticModelAsync() is not { } model ||
                !model.Compilation.SyntaxTrees.Take(2).SequenceEqual(new[] { model.SyntaxTree }))
            { return await Simplifier.ReduceAsync(document); }
            if (candidates.Length == 2 && (candidates[0].FullSpan.OverlapsWith(candidates[1].FullSpan) ||
                root.GetAnnotatedNodesAndTokens(Simplifier.Annotation).Any(item => !ReferenceEquals(item.AsNode(), root))))
            { return await Simplifier.ReduceAsync(document); }

            var sites = new Dictionary<SyntaxNode, CaptureSite>(ReferenceEqualityComparer.Instance);
            var protectedAncestors = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
            foreach (var call in candidates)
            {
                // Do not filter an unaudited shape into the one-site profile.
                if (!TryBind(call, model, out string? entry, out string? target, out string? captured) ||
                    candidates.Length == 2 && entry is null)
                { return await Simplifier.ReduceAsync(document); }
                sites.Add(call, new(new SyntaxAnnotation(), entry, target, captured));
                foreach (var ancestor in call.AncestorsAndSelf()) { protectedAncestors.Add(ancestor); }
            }

            // The complement is shared: a sibling of one site can contain the
            // other. Annotate only roots outside the union of protected paths.
            var disjoint = sites.Keys.Concat(protectedAncestors.Where(node => !sites.ContainsKey(node))
                .SelectMany(node => node.ChildNodes().Where(child => !protectedAncestors.Contains(child))));
            var partitioned = root.ReplaceNodes(disjoint, (original, node) => sites.TryGetValue(original, out var site)
                ? node.WithAdditionalAnnotations(site.Tracking) : node.WithAdditionalAnnotations(Simplifier.Annotation))
                .WithoutAnnotations(Simplifier.Annotation);
            var reduced = await Simplifier.ReduceAsync(document.WithSyntaxRoot(partitioned));
            if (await reduced.GetSyntaxRootAsync() is not { } finalRoot || await reduced.GetSemanticModelAsync() is not { } finalModel)
            { throw new InvalidOperationException("Unable to preserve the implicit caller expression during generated document reduction."); }
            foreach (var (original, site) in sites)
            {
                var tracked = finalRoot.GetAnnotatedNodes(site.Tracking).Take(2).ToArray();
                if (tracked.Length != 1 || tracked[0] is not InvocationExpressionSyntax finalCall ||
                    !string.Equals(original.ToFullString(), finalCall.ToFullString(), StringComparison.Ordinal) ||
                    !TryBind(finalCall, finalModel, out string? finalEntry, out string? finalTarget, out string? finalCapture) ||
                    site.Entry != finalEntry || site.Target != finalTarget || site.Captured != finalCapture)
                { throw new InvalidOperationException("Unable to preserve the implicit caller expression during generated document reduction."); }
            }
            return reduced;
        }

        private static bool TryGetShape(InvocationExpressionSyntax call, out MethodDeclarationSyntax entry,
            out ParenthesizedLambdaExpressionSyntax lambda, out BinaryExpressionSyntax addition)
        {
            entry = null!;
            lambda = null!;
            addition = null!;
            if (call.Expression is not IdentifierNameSyntax || call.ArgumentList.Arguments.Count != 1 ||
                call.Parent is not ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax method } ||
                method.ParameterList.Parameters.Count != 2 || call.ArgumentList.Arguments[0] is not
                    { NameColon: null, Expression: ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0,
                        Block.Statements.Count: 1 } actualLambda } ||
                actualLambda.Block!.Statements[0] is not ReturnStatementSyntax { Expression: ParenthesizedExpressionSyntax
                    { Expression: ParenthesizedExpressionSyntax { Expression: BinaryExpressionSyntax
                        { Left: IdentifierNameSyntax, Right: IdentifierNameSyntax } binary } } })
            { return false; }
            entry = method;
            lambda = actualLambda;
            addition = binary;
            return true;
        }

        private static bool TryBind(InvocationExpressionSyntax call, SemanticModel model, out string? entryId, out string? targetId, out string? captured)
        {
            entryId = targetId = captured = null;
            if (!TryGetShape(call, out var method, out var lambda, out var addition) || call.ContainsDiagnostics ||
                call.SyntaxTree != model.SyntaxTree || model.GetDeclaredSymbol(method) is not IMethodSymbol entry || entry.Parameters.Length != 2 ||
                entry.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.Type.SpecialType != SpecialType.System_Int32) ||
                model.GetOperation(addition) is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Int32, LeftOperand: IParameterReferenceOperation left, RightOperand: IParameterReferenceOperation right } ||
                !SymbolEqualityComparer.Default.Equals(left.Parameter, entry.Parameters[0]) ||
                !SymbolEqualityComparer.Default.Equals(right.Parameter, entry.Parameters[1]) ||
                model.GetOperation(call) is not IInvocationOperation { TargetMethod: { IsStatic: false, MethodKind: MethodKind.Ordinary,
                    DeclaredAccessibility: Accessibility.Private, Arity: 0 } target, Arguments.Length: 2 } invocation ||
                !SymbolEqualityComparer.Default.Equals(entry.ContainingType, target.ContainingType) ||
                target.DeclaringSyntaxReferences.Length != 1 || target.DeclaringSyntaxReferences[0].SyntaxTree != model.SyntaxTree ||
                target.Parameters.Length != 2 || target.Parameters[1] is not { IsOptional: true, Type.SpecialType: SpecialType.System_String } textParameter)
            { return false; }

            var core = model.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            if (core?.GetTypeByMetadataName(typeof(CallerArgumentExpressionAttribute).FullName!) is not { } attributeType ||
                core.GetTypeByMetadataName("System.Func`1") is not { } func || target.Parameters[0].Type is not INamedTypeSymbol delegateType ||
                !SymbolEqualityComparer.Default.Equals(delegateType.OriginalDefinition, func) ||
                delegateType.TypeArguments is not [{ SpecialType: SpecialType.System_Int32 }])
            { return false; }
            var attributes = textParameter.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)).Take(2).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments is not [{ Value: string name }] || name != target.Parameters[0].Name ||
                !invocation.Arguments.Any(argument => argument is { IsImplicit: false, ArgumentKind: ArgumentKind.Explicit, Parameter.Ordinal: 0 } &&
                    argument.Syntax.Span == call.ArgumentList.Arguments[0].Span) ||
                invocation.Arguments.SingleOrDefault(argument => argument.Parameter?.Ordinal == 1) is not
                    { IsImplicit: true, ArgumentKind: ArgumentKind.DefaultValue, Value.ConstantValue: { HasValue: true, Value: string value } } ||
                !string.Equals(value, lambda.ToString(), StringComparison.Ordinal))
            { return false; }
            captured = value;
            entryId = entry.GetDocumentationCommentId();
            targetId = target.GetDocumentationCommentId();
            return targetId is not null;
        }

        private sealed record CaptureSite(SyntaxAnnotation Tracking, string? Entry, string? Target, string? Captured);
    }
}

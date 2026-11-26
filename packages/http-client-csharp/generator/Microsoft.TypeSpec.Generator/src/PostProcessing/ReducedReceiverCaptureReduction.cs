// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.TypeSpec.Generator
{
    // Only the closed two-file reduced receiver case. The ordinary single-document
    // reducer does not acquire authority over references to sibling documents.
    internal static class ReducedReceiverCaptureReduction
    {
        internal static async Task<(string Name, string Text)[]?> TryReduceAsync(Project project)
        {
            var documents = project.Documents.Take(3).ToArray();
            if (documents.Length != 2 || documents.Any(document => !GeneratedCodeWorkspace.IsGeneratedDocument(document)) ||
                string.Equals(documents[0].Name, documents[1].Name, StringComparison.OrdinalIgnoreCase))
            { return null; }

            // Preflight before member removal so unrelated two-document projects
            // do not enter the specialized pipeline.
            var roots = await Task.WhenAll(documents.Select(document => document.GetSyntaxRootAsync()));
            if (roots.Any(root => root is null || !root.HasAnnotation(Simplifier.Annotation) ||
                root.GetAnnotatedNodesAndTokens(Simplifier.Annotation).Any(item => !ReferenceEquals(item.AsNode(), root))) ||
                roots.SelectMany(root => Candidates(root!)).Take(2).Count() != 1)
            { return null; }

            var solution = project.Solution;
            var remover = new MemberRemoverRewriter();
            for (int i = 0; i < documents.Length; i++)
            {
                if (remover.Visit(roots[i]) is not { } rewritten) { return null; }
                solution = solution.WithDocumentSyntaxRoot(documents[i].Id, rewritten);
            }
            var coherent = solution.GetProject(project.Id)!;
            var trees = await Task.WhenAll(documents.Select(document => coherent.GetDocument(document.Id)!.GetSyntaxTreeAsync()));
            if (await coherent.GetCompilationAsync() is not CSharpCompilation before || trees.Any(tree => tree is null) ||
                before.SyntaxTrees.Count() != 2 || trees.Any(tree => !before.SyntaxTrees.Contains(tree!)) || HasErrors(before))
            { return null; }
            int caller = Candidates(await trees[0]!.GetRootAsync()).Any() ? 0 : 1;
            var calls = Candidates(await trees[caller]!.GetRootAsync()).Take(2).ToArray();
            if (calls.Length != 1 || Candidates(await trees[1 - caller]!.GetRootAsync()).Any() ||
                !TryBind(calls[0], before.GetSemanticModel(trees[caller]!), trees[1 - caller]!, out var original))
            { return null; }

            var call = calls[0];
            var root = await trees[caller]!.GetRootAsync();
            var siblings = new List<SyntaxNode>();
            for (SyntaxNode current = call; current.Parent is { } parent; current = parent)
            {
                siblings.AddRange(parent.ChildNodes().Where(child => !ReferenceEquals(child, current)));
            }
            var partitioned = root.ReplaceNodes(siblings, (_, node) => node.WithAdditionalAnnotations(Simplifier.Annotation))
                .WithoutAnnotations(Simplifier.Annotation);
            var reducedCaller = await Simplifier.ReduceAsync(coherent.GetDocument(documents[caller].Id)!.WithSyntaxRoot(partitioned));
            var reducedExtension = await Simplifier.ReduceAsync(coherent.GetDocument(documents[1 - caller].Id)!);
            var outputs = new (string Name, string Text)[2];
            outputs[caller] = (documents[caller].Name, (await reducedCaller.GetTextAsync()).ToString());
            outputs[1 - caller] = (documents[1 - caller].Name, (await reducedExtension.GetTextAsync()).ToString());

            // Independent document reductions see their original siblings. Check
            // the serialized pair together before yielding either exact string.
            var finalTrees = outputs.Select((output, i) => CSharpSyntaxTree.ParseText(output.Text,
                (CSharpParseOptions)trees[i]!.Options, trees[i]!.FilePath)).ToArray();
            var after = before.RemoveAllSyntaxTrees().AddSyntaxTrees(finalTrees);
            var finalCalls = Candidates(await finalTrees[caller].GetRootAsync()).Take(2).ToArray();
            if (HasErrors(after) || finalCalls.Length != 1 || Candidates(await finalTrees[1 - caller].GetRootAsync()).Any() ||
                !TryBind(finalCalls[0], after.GetSemanticModel(finalTrees[caller]), finalTrees[1 - caller], out var final) ||
                original != final || call.ToString() != finalCalls[0].ToString())
            { throw new InvalidOperationException("Unable to preserve the reduced extension caller expression during generated document reduction."); }
            return outputs;
        }

        private static bool HasErrors(Compilation compilation) => compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        private static IEnumerable<InvocationExpressionSyntax> Candidates(SyntaxNode root) => root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => TryGetShape(call, out _, out _, out _, out _));

        private static bool TryGetShape(InvocationExpressionSyntax call, out MethodDeclarationSyntax entry, out CastExpressionSyntax cast,
            out ParenthesizedLambdaExpressionSyntax lambda, out BinaryExpressionSyntax addition)
        {
            entry = null!;
            cast = null!;
            lambda = null!;
            addition = null!;
            if (call.Parent is not ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax { ParameterList.Parameters.Count: 2 } method } ||
                call.ArgumentList.Arguments.Count != 0 || call.Expression is not MemberAccessExpressionSyntax
                { Name: IdentifierNameSyntax, Expression: ParenthesizedExpressionSyntax { Expression: CastExpressionSyntax
                    { Expression: ParenthesizedExpressionSyntax { Expression: ParenthesizedLambdaExpressionSyntax
                        { ParameterList.Parameters.Count: 0, Block.Statements.Count: 1 } actualLambda } } actualCast } } ||
                actualLambda.Block!.Statements[0] is not ReturnStatementSyntax { Expression: ParenthesizedExpressionSyntax
                    { Expression: ParenthesizedExpressionSyntax { Expression: BinaryExpressionSyntax
                        { Left: IdentifierNameSyntax, Right: IdentifierNameSyntax } binary } } })
            { return false; }
            entry = method;
            cast = actualCast;
            lambda = actualLambda;
            addition = binary;
            return true;
        }

        private static bool TryBind(InvocationExpressionSyntax call, SemanticModel model, SyntaxTree extensionTree,
            out (string Entry, string Extension, string Capture) identity)
        {
            identity = default;
            var comparer = SymbolEqualityComparer.Default;
            if (!TryGetShape(call, out var method, out var cast, out var lambda, out var addition) ||
                model.GetDeclaredSymbol(method) is not IMethodSymbol { MethodKind: MethodKind.Ordinary, Arity: 0,
                    ReturnType.SpecialType: SpecialType.System_Object, Parameters: [var first, var second] } entry ||
                first.RefKind != RefKind.None || second.RefKind != RefKind.None ||
                first.Type.SpecialType != SpecialType.System_Int32 || second.Type.SpecialType != SpecialType.System_Int32 ||
                model.GetOperation(addition) is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Int32, LeftOperand: IParameterReferenceOperation left, RightOperand: IParameterReferenceOperation right } ||
                !comparer.Equals(first, left.Parameter) || !comparer.Equals(second, right.Parameter) ||
                model.GetSymbolInfo(call).Symbol is not IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } extension } ||
                extension.DeclaringSyntaxReferences is not [var declaration] || declaration.SyntaxTree != extensionTree ||
                !comparer.Equals(entry.ContainingNamespace, extension.ContainingNamespace) ||
                extension is not { Arity: 0, IsExtensionMethod: true, ReturnType.SpecialType: SpecialType.System_Object,
                    Parameters: [var receiverParameter, { IsOptional: true, Type.SpecialType: SpecialType.System_String } textParameter] } ||
                declaration.GetSyntax() is not MethodDeclarationSyntax { ExpressionBody.Expression: IdentifierNameSyntax returned } ||
                !comparer.Equals(model.Compilation.GetSemanticModel(extensionTree).GetSymbolInfo(returned).Symbol, textParameter))
            { return false; }

            var core = model.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            if (core.GetTypeByMetadataName("System.Func`1") is not { } func ||
                core.GetTypeByMetadataName(typeof(CallerArgumentExpressionAttribute).FullName!) is not { } attributeType ||
                model.GetTypeInfo(cast.Type).Type is not INamedTypeSymbol delegateType ||
                !comparer.Equals(delegateType.OriginalDefinition, func) || delegateType.TypeArguments is not [{ SpecialType: SpecialType.System_Int32 }] ||
                !comparer.Equals(delegateType, receiverParameter.Type) || !model.GetConversion(lambda).IsAnonymousFunction ||
                textParameter.GetAttributes() is not [{ ConstructorArguments: [{ Value: string name }] } attribute] ||
                !comparer.Equals(attribute.AttributeClass, attributeType) || name != receiverParameter.Name ||
                model.GetOperation(call) is not IInvocationOperation { Arguments.Length: 2 } invocation ||
                !comparer.Equals(invocation.TargetMethod, extension) ||
                invocation.Arguments.SingleOrDefault(argument => comparer.Equals(argument.Parameter, textParameter)) is not
                    { IsImplicit: true, ArgumentKind: ArgumentKind.DefaultValue, Value.ConstantValue: { HasValue: true, Value: string capture } } ||
                capture != cast.ToString() || entry.GetDocumentationCommentId() is not { } entryId || extension.GetDocumentationCommentId() is not { } extensionId)
            { return false; }
            identity = (entryId, extensionId, capture);
            return true;
        }
    }
}

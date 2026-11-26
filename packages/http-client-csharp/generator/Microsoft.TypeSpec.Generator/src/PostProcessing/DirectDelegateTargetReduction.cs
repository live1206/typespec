// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.TypeSpec.Generator
{
    // Exact direct delegate-formal receiver, not a general invocation policy.
    internal static class DirectDelegateTargetReduction
    {
        internal static bool IsCandidate(InvocationExpressionSyntax call) => TryGetShape(call, out _, out _);

        internal static bool TryBind(InvocationExpressionSyntax call, SemanticModel model, out (string Entry, string Formal, string Invoke) identity)
        {
            identity = default;
            if (!TryGetShape(call, out var method, out var identifier) || call.ContainsDiagnostics || call.SyntaxTree != model.SyntaxTree ||
                model.GetDeclaredSymbol(method) is not IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnType.SpecialType: SpecialType.System_Int32 } entry ||
                entry.Parameters is not [{ RefKind: RefKind.None, Type: INamedTypeSymbol delegateType } formal] ||
                !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, formal) ||
                model.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly?.GetTypeByMetadataName("System.Func`2") is not { } func ||
                !SymbolEqualityComparer.Default.Equals(delegateType.OriginalDefinition, func) ||
                delegateType.TypeArguments is not [{ SpecialType: SpecialType.System_Int32 }, { SpecialType: SpecialType.System_Int32 }] ||
                model.GetOperation(call) is not IInvocationOperation { TargetMethod: { MethodKind: MethodKind.DelegateInvoke,
                    ReturnType.SpecialType: SpecialType.System_Int32 } target, Arguments: [{ IsImplicit: false, ArgumentKind: ArgumentKind.Explicit,
                        Value: { Type.SpecialType: SpecialType.System_Int32, ConstantValue: { HasValue: true, Value: 1 } } } argument] } ||
                !SymbolEqualityComparer.Default.Equals(target, delegateType.DelegateInvokeMethod) ||
                target.Parameters is not [{ Type.SpecialType: SpecialType.System_Int32, RefKind: RefKind.None } parameter] ||
                !SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) || !argument.InConversion.IsIdentity ||
                entry.GetDocumentationCommentId() is not { } entryId || target.GetDocumentationCommentId() is not { } targetId)
            { return false; }
            identity = (entryId, formal.Name, targetId);
            return true;
        }

        private static bool TryGetShape(InvocationExpressionSyntax call, out MethodDeclarationSyntax method, out IdentifierNameSyntax identifier)
        {
            method = null!;
            identifier = null!;
            if (call.Parent is not ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax declaration } ||
                declaration.ParameterList.Parameters.Count != 1 ||
                call.Expression is not ParenthesizedExpressionSyntax { Expression: ParenthesizedExpressionSyntax { Expression: IdentifierNameSyntax formal } } ||
                call.ArgumentList.Arguments is not [{ NameColon: null, Expression: LiteralExpressionSyntax literal }] ||
                !literal.IsKind(SyntaxKind.NumericLiteralExpression) || literal.Token.Text != "1")
            { return false; }
            method = declaration;
            identifier = formal;
            return true;
        }
    }
}

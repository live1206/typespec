// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.TypeSpec.Generator
{
    // Exact bare right-grouped Int32 arrow body, not an arithmetic policy.
    internal static class RightAdditionReduction
    {
        internal static bool IsCandidate(BinaryExpressionSyntax expression) => TryGetShape(expression, out _);

        internal static bool TryBind(BinaryExpressionSyntax expression, SemanticModel model, out string? identity)
        {
            identity = null;
            if (!TryGetShape(expression, out var method) || expression.ContainsDiagnostics || expression.SyntaxTree != model.SyntaxTree ||
                model.GetDeclaredSymbol(method) is not IMethodSymbol { MethodKind: MethodKind.Ordinary, Arity: 0,
                    ReturnType.SpecialType: SpecialType.System_Int32, Parameters: [var first, var second, var third] } entry ||
                model.GetOperation(expression) is not IBinaryOperation { LeftOperand: IParameterReferenceOperation left,
                    RightOperand: IBinaryOperation { LeftOperand: IParameterReferenceOperation innerLeft, RightOperand: IParameterReferenceOperation innerRight } right } outer ||
                !IsPlainAddition(outer) || !IsPlainAddition(right) || !IsExactIntFormal(left, first) ||
                !IsExactIntFormal(innerLeft, second) || !IsExactIntFormal(innerRight, third))
            { return false; }
            identity = entry.GetDocumentationCommentId();
            return identity is not null;
        }

        private static bool IsPlainAddition(IBinaryOperation operation) => operation is
            { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, IsChecked: false, IsLifted: false, Type.SpecialType: SpecialType.System_Int32 };

        private static bool IsExactIntFormal(IParameterReferenceOperation reference, IParameterSymbol parameter) =>
            parameter is { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_Int32 } &&
            SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

        private static bool TryGetShape(BinaryExpressionSyntax expression, out MethodDeclarationSyntax method)
        {
            method = null!;
            if (expression.Parent is not ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax { ParameterList.Parameters.Count: 3 } entry } ||
                !expression.IsKind(SyntaxKind.AddExpression) || expression is not { Left: IdentifierNameSyntax,
                    Right: ParenthesizedExpressionSyntax { Expression: BinaryExpressionSyntax { Left: IdentifierNameSyntax, Right: IdentifierNameSyntax } right } } ||
                !right.IsKind(SyntaxKind.AddExpression))
            { return false; }
            method = entry;
            return true;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.TypeSpec.Generator
{
    // Exact safe Span<byte> initializer role, not a general stackalloc policy.
    internal static class StackAllocationReduction
    {
        internal static bool IsCandidate(ParenthesizedExpressionSyntax initializer) => TryGetShape(initializer, out _, out _, out _, out _, out _);

        internal static bool TryBind(ParenthesizedExpressionSyntax initializer, SemanticModel model, out (string Method, string Local, string Length) identity)
        {
            identity = default;
            if (!TryGetShape(initializer, out var method, out var variable, out var allocation, out var size, out var length) ||
                initializer.SyntaxTree != model.SyntaxTree || method.ContainsDiagnostics || model.Compilation.Options is not CSharpCompilationOptions { AllowUnsafe: false } ||
                model.GetDeclaredSymbol(method) is not IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnType.SpecialType: SpecialType.System_Int32 } entry ||
                model.GetDeclaredSymbol(variable) is not ILocalSymbol { Type: INamedTypeSymbol span } local ||
                model.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly?.GetTypeByMetadataName("System.Span`1") is not { } coreSpan ||
                !SymbolEqualityComparer.Default.Equals(span.OriginalDefinition, coreSpan) || span.TypeArguments is not [{ SpecialType: SpecialType.System_Byte }] ||
                model.GetConstantValue(size) is not { HasValue: true, Value: 8 } ||
                !model.GetConversion(initializer).IsIdentity || !model.GetConversion(allocation).IsIdentity ||
                !SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(initializer).ConvertedType, local.Type) ||
                !SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(allocation).ConvertedType, local.Type) ||
                model.GetOperation(length) is not IPropertyReferenceOperation { Property: { Name: "Length", IsStatic: false,
                    Type.SpecialType: SpecialType.System_Int32 } property, Instance: ILocalReferenceOperation receiver } ||
                !SymbolEqualityComparer.Default.Equals(property.ContainingType, span) || !SymbolEqualityComparer.Default.Equals(receiver.Local, local) ||
                entry.GetDocumentationCommentId() is not { } methodId || property.GetDocumentationCommentId() is not { } propertyId)
            { return false; }
            identity = (methodId, local.Name, propertyId);
            return true;
        }

        private static bool TryGetShape(ParenthesizedExpressionSyntax initializer, out MethodDeclarationSyntax method, out VariableDeclaratorSyntax variable,
            out StackAllocArrayCreationExpressionSyntax allocation, out LiteralExpressionSyntax size, out MemberAccessExpressionSyntax length)
        {
            method = null!;
            variable = null!;
            allocation = null!;
            size = null!;
            length = null!;
            if (initializer.Expression is not StackAllocArrayCreationExpressionSyntax { Initializer: null, Type: ArrayTypeSyntax
                { ElementType: PredefinedTypeSyntax element, RankSpecifiers: [{ Sizes: [LiteralExpressionSyntax literal] }] } } stack ||
                !element.Keyword.IsKind(SyntaxKind.ByteKeyword) || !literal.IsKind(SyntaxKind.NumericLiteralExpression) || literal.Token.Text != "8" ||
                initializer.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declaration } ||
                declaration.Identifier.ValueText != "value" || declaration.Parent is not VariableDeclarationSyntax { Variables.Count: 1,
                    Parent: LocalDeclarationStatementSyntax statement } || statement.UsingKeyword.RawKind != 0 || statement.Modifiers.Count != 0 ||
                statement.Parent is not BlockSyntax { Parent: MethodDeclarationSyntax { ParameterList.Parameters.Count: 0 } owner,
                    Statements: [LocalDeclarationStatementSyntax first, ReturnStatementSyntax { Expression: MemberAccessExpressionSyntax
                        { Expression: IdentifierNameSyntax reference, Name: IdentifierNameSyntax member } access }] } ||
                !ReferenceEquals(statement, first) || reference.Identifier.ValueText != "value" || member.Identifier.ValueText != "Length")
            { return false; }
            method = owner;
            variable = declaration;
            allocation = stack;
            size = literal;
            length = access;
            return true;
        }
    }
}

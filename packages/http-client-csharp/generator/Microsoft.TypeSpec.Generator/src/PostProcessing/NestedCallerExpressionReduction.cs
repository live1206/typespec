// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.TypeSpec.Generator
{
    // Exact shape and binding for the nested binary caller-expression profile.
    // This is not a general CAE or formatter policy.
    internal static class NestedCallerExpressionReduction
    {
        internal static bool IsCandidate(InvocationExpressionSyntax call) => TryGetShape(call, out _, out _, out _);

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

        internal static bool TryBind(InvocationExpressionSyntax call, SemanticModel model, out string? entryId, out string? targetId, out string? captured)
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
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // One native declaration assignment and return of that declaration. This
    // does not create a local binding service or extend the unused-local frame.
    internal readonly record struct PreparedUsedLocalInitializer(MethodBodyStatements Block, AssignmentExpression Assignment,
        DeclarationExpression Declaration, BinaryOperatorExpression Initializer)
    {
        internal static PreparedUsedLocalInitializer? TryGet(MethodProvider method, object body, ImmutableArray<ParameterProvider> parameters = default)
        {
            if (method.GetType() != typeof(MethodProvider) || body is not MethodBodyStatements block ||
                block.GetType() != typeof(MethodBodyStatements) || !ReferenceEquals(method.BodyStatements, block) ||
                !IsInt(method.Signature.ReturnType) || method.Signature.HasUnsupportedBaseContract ||
                parameters.IsDefault && !ContractCollections.TryCopy(method.Signature.Parameters, out parameters) || parameters.Length != 2 ||
                !HasCachedIntFormal(parameters[0]) || !HasCachedIntFormal(parameters[1])) { return null; }

            if (!ContractCollections.TryCopy(block.Statements, out var statements) || statements.Length != 2 ||
                statements[0] is not ExpressionStatement { Expression: AssignmentExpression assignment } first ||
                first.GetType() != typeof(ExpressionStatement) || assignment.UseNullCoalesce ||
                assignment.Variable is not DeclarationExpression { IsOut: false, IsUsing: false, IsConst: false } declaration ||
                declaration.Variable?.GetType() != typeof(VariableExpression) || !IsInt(declaration.Variable.Type) ||
                assignment.Value is not BinaryOperatorExpression { Operator: "+", AddParentheses: true } initializer ||
                initializer.Left?.GetType() != typeof(VariableExpression) || initializer.Right?.GetType() != typeof(VariableExpression) ||
                statements[1] is not ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: VariableExpression returned } keyword } last ||
                last.GetType() != typeof(ExpressionStatement) || keyword.GetType() != typeof(KeywordExpression) ||
                returned.GetType() != typeof(VariableExpression) || !IsInt(returned.Type) ||
                declaration.Variable.Declaration is not { } local || !ReferenceEquals(returned.Declaration, local)) { return null; }

            var leftFormal = parameters[0].CachedVariableExpression!.Declaration;
            var rightFormal = parameters[1].CachedVariableExpression!.Declaration;
            if (ReferenceEquals(leftFormal, rightFormal) || ReferenceEquals(local, leftFormal) || ReferenceEquals(local, rightFormal) ||
                !Identifier(local.RequestedName) || !Identifier(leftFormal.RequestedName) || !Identifier(rightFormal.RequestedName) ||
                local.RequestedName == leftFormal.RequestedName || local.RequestedName == rightFormal.RequestedName ||
                leftFormal.RequestedName == rightFormal.RequestedName) { return null; }
            return new(block, assignment, declaration, initializer);
        }

        internal bool Contains(object node) => ReferenceEquals(node, Assignment) || ReferenceEquals(node, Declaration) || ReferenceEquals(node, Initializer);

        private static bool HasCachedIntFormal(ParameterProvider? parameter) => parameter is not null &&
            parameter.HasCachedEmittedDeclarationState && parameter.CachedVariableExpression?.GetType() == typeof(VariableExpression) &&
            IsInt(parameter.Type) && !parameter.IsRef && !parameter.IsOut && !parameter.IsIn && !parameter.IsParams &&
            !parameter.HasUnsupportedParameterModifiers && !parameter.HasUnsupportedDefaultValue;

        private static bool Identifier(string name) => name.Length > 0 && (name[0] == '_' || char.IsAsciiLetter(name[0])) &&
            name.All(c => c == '_' || char.IsAsciiLetterOrDigit(c)) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;

        internal static bool IsInt(CSharpType? type) => type?.GetType() == typeof(CSharpType) &&
            type.IsFrameworkType && ReferenceEquals(type.FrameworkType, typeof(int)) && !type.IsNullable &&
            type.IsValueType && type.IsStruct && !type.IsLiteral && !type.HasUnionMetadata && type.DeclaringType is null &&
            type.Name == nameof(Int32) && type.Namespace == typeof(int).Namespace &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;
    }
}

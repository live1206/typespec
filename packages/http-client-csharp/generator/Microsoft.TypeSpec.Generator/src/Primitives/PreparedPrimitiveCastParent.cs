// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Exact native parent slots, not a cast precedence table.
    internal readonly record struct PreparedPrimitiveCastParent(object Root, CastExpression Cast,
        AssignmentExpression? Assignment = null, DeclarationExpression? Declaration = null, bool SeparateAssignment = false)
    {
        internal static PreparedPrimitiveCastParent? TryGet(MethodProvider method, object body, ImmutableArray<ParameterProvider> parameters = default)
        {
            if (method.GetType() != typeof(MethodProvider) || !ReferenceEquals((object?)method.BodyStatements ?? method.BodyExpression, body) ||
                body is not (MethodBodyStatements or BinaryOperatorExpression or TernaryConditionalExpression) ||
                method.Signature.HasUnsupportedBaseContract ||
                parameters.IsDefault && !ContractCollections.TryCopy(method.Signature.Parameters, out parameters) ||
                parameters.Length is not (1 or 2) || !parameters.All(HasCachedFormal)) { return null; }

            var result = method.Signature.ReturnType;
            if (body is TernaryConditionalExpression { Condition: CastExpression condition } ternary &&
                parameters.Length == 1 && IsBool(parameters[0].Type) && PreparedUsedLocalInitializer.IsInt(result) &&
                IsBool(condition.Type) && condition.Inner?.GetType() == typeof(VariableExpression) &&
                IsLiteral(ternary.Consequent, 1) && IsLiteral(ternary.Alternative, 2))
            { return new(ternary, condition); }
            if (!IsLong(result) || !parameters.All(parameter => PreparedUsedLocalInitializer.IsInt(parameter.Type))) { return null; }

            if (body is BinaryOperatorExpression { Operator: "+", Left: CastExpression left, Right: VariableExpression right } binary &&
                parameters.Length == 2 && IsWideningCast(left) && right.GetType() == typeof(VariableExpression))
            { return new(binary, left); }
            if (body is not MethodBodyStatements block || block.GetType() != typeof(MethodBodyStatements) || parameters.Length != 1 ||
                !ContractCollections.TryCopy(block.Statements, out var statements)) { return null; }
            if (statements.Length == 3) { return TryGetSeparateAssignment(block, statements, parameters[0]); }
            if (statements.Length != 2 || statements[0] is not ExpressionStatement { Expression: AssignmentExpression { UseNullCoalesce: false } assignment } first ||
                first.GetType() != typeof(ExpressionStatement) ||
                assignment.Variable is not DeclarationExpression { IsOut: false, IsUsing: false, IsConst: false } declaration ||
                declaration.Variable?.GetType() != typeof(VariableExpression) || !IsLong(declaration.Variable.Type) ||
                assignment.Value is not CastExpression initializer || !IsWideningCast(initializer) ||
                statements[1] is not ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: VariableExpression returned } keyword } last ||
                last.GetType() != typeof(ExpressionStatement) || keyword.GetType() != typeof(KeywordExpression) ||
                returned.GetType() != typeof(VariableExpression) || !IsLong(returned.Type) ||
                declaration.Variable.Declaration is not { } local || !ReferenceEquals(returned.Declaration, local)) { return null; }
            var formal = parameters[0].CachedVariableExpression!.Declaration;
            return !ReferenceEquals(local, formal) && Identifier(local.RequestedName) && Identifier(formal.RequestedName) &&
                local.RequestedName != formal.RequestedName ? new(block, initializer, assignment, declaration) : null;
        }

        private static PreparedPrimitiveCastParent? TryGetSeparateAssignment(MethodBodyStatements block, ImmutableArray<MethodBodyStatement> statements,
            ParameterProvider parameter)
        {
            if (statements[0] is not ExpressionStatement { Expression: DeclarationExpression { IsOut: false, IsUsing: false, IsConst: false } declaration } first ||
                first.GetType() != typeof(ExpressionStatement) || declaration.Variable?.GetType() != typeof(VariableExpression) ||
                !IsLong(declaration.Variable.Type) || declaration.Variable.Declaration is not { } local ||
                statements[1] is not ExpressionStatement { Expression: AssignmentExpression { UseNullCoalesce: false,
                    Variable: VariableExpression target, Value: CastExpression cast } assignment } second ||
                second.GetType() != typeof(ExpressionStatement) || target.GetType() != typeof(VariableExpression) ||
                !ReferenceEquals(target.Declaration, local) || !IsLong(target.Type) || !IsWideningCast(cast) ||
                statements[2] is not ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: VariableExpression returned } keyword } last ||
                last.GetType() != typeof(ExpressionStatement) || keyword.GetType() != typeof(KeywordExpression) ||
                returned.GetType() != typeof(VariableExpression) || !ReferenceEquals(returned.Declaration, local) || !IsLong(returned.Type)) { return null; }
            var formal = parameter.CachedVariableExpression!.Declaration;
            return !ReferenceEquals(local, formal) && Identifier(local.RequestedName) && Identifier(formal.RequestedName) &&
                local.RequestedName != formal.RequestedName ? new(block, cast, assignment, declaration, SeparateAssignment: true) : null;
        }

        internal bool Contains(object node) => ReferenceEquals(node, Root) || ReferenceEquals(node, Assignment) || ReferenceEquals(node, Declaration);

        internal static bool IsLong(CSharpType? type) => IsPrimitive(type, typeof(long));
        internal static bool IsBool(CSharpType? type) => IsPrimitive(type, typeof(bool));

        private static bool IsPrimitive(CSharpType? type, Type expected) => type?.GetType() == typeof(CSharpType) &&
            type.IsFrameworkType && ReferenceEquals(type.FrameworkType, expected) && type.IsValueType && type.IsStruct &&
            !type.IsNullable && !type.IsLiteral && !type.HasUnionMetadata && type.DeclaringType is null &&
            type.Name == expected.Name && type.Namespace == expected.Namespace &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;

        private static bool HasCachedFormal(ParameterProvider? parameter) => parameter is not null &&
            parameter.HasCachedEmittedDeclarationState && parameter.CachedVariableExpression?.GetType() == typeof(VariableExpression) &&
            !parameter.IsRef && !parameter.IsIn && !parameter.IsOut && !parameter.IsParams &&
            !parameter.HasUnsupportedParameterModifiers && !parameter.HasUnsupportedDefaultValue;

        private static bool IsWideningCast(CastExpression cast) => IsLong(cast.Type) && cast.Inner?.GetType() == typeof(VariableExpression);

        private static bool IsLiteral(ValueExpression expression, int expected) =>
            (expression is ScopedApi wrapper && PreparedCallProofs.IsTransparentWrapper(wrapper) ? wrapper.Original : expression)
                is LiteralExpression { Literal: int value } && value == expected;

        private static bool Identifier(string name) => name.Length > 0 && (name[0] == '_' || char.IsAsciiLetter(name[0])) &&
            name.All(c => c == '_' || char.IsAsciiLetterOrDigit(c)) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    }
}

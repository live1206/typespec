// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // A source-only frame may describe this exact right-child role. Shape and
    // signature alone do not prove the three emitted variable bindings.
    internal readonly record struct PreparedStringCoalesce(BinaryOperatorExpression Root, BinaryOperatorExpression Right)
    {
        internal static PreparedStringCoalesce? TryGet(MethodProvider method, object body, ImmutableArray<ParameterProvider> parameters = default)
        {
            if (method.GetType() != typeof(MethodProvider)) { return null; }
            var binary = body switch
            {
                BinaryOperatorExpression expression => expression,
                ExpressionStatement statement when statement.GetType() == typeof(ExpressionStatement) &&
                    statement.Expression is KeywordExpression { Keyword: "return", Expression: BinaryOperatorExpression returned } keyword &&
                    keyword.GetType() == typeof(KeywordExpression) => returned,
                _ => null
            };
            // Check cached native declarations before capturing the newly admitted region.
            if (binary is not { Operator: "??" } || !IsString(method.Signature.ReturnType) ||
                method.Signature.HasUnsupportedBaseContract ||
                parameters.IsDefault && !ContractCollections.TryCopy(method.Signature.Parameters, out parameters) || parameters.Length != 3 ||
                parameters.Any(parameter => parameter?.GetType() != typeof(ParameterProvider) ||
                    !parameter.HasCachedEmittedDeclarationState || !IsString(parameter.Type) ||
                    parameter.HasUnsupportedParameterModifiers || parameter.HasUnsupportedDefaultValue ||
                    parameter.IsRef || parameter.IsOut || parameter.IsIn || parameter.IsParams)) { return null; }
            return binary.Left?.GetType() == typeof(VariableExpression) &&
                binary.Right is BinaryOperatorExpression { Operator: "??", AddParentheses: true } right &&
                right.Left?.GetType() == typeof(VariableExpression) && right.Right?.GetType() == typeof(VariableExpression)
                ? new(binary, right) : null;
        }

        internal bool Contains(BinaryOperatorExpression binary) => ReferenceEquals(binary, Root) || ReferenceEquals(binary, Right);

        internal static bool IsString(CSharpType? type) => type?.GetType() == typeof(CSharpType) &&
            type.IsFrameworkType && ReferenceEquals(type.FrameworkType, typeof(string)) && !type.IsNullable &&
            !type.IsValueType && !type.IsStruct && !type.IsLiteral && !type.HasUnionMetadata && type.DeclaringType is null &&
            type.Name == nameof(String) && type.Namespace == typeof(string).Namespace &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;
    }
}

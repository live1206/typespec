// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal static class PreparedBinaryParentRoles
    {
        internal const int MaximumOccurrences = 128;
        internal const int MaximumDepth = 24;

        internal static bool TryGetNewRoot(MethodProvider method, object body, out BinaryOperatorExpression root)
        {
            root = null!;
            if (method.GetType() != typeof(MethodProvider)) { return false; }
            var binary = body switch
            {
                BinaryOperatorExpression expression => expression,
                ExpressionStatement statement when statement.GetType() == typeof(ExpressionStatement) &&
                    statement.Expression is KeywordExpression { Keyword: "return", Expression: BinaryOperatorExpression returned } keyword &&
                    keyword.GetType() == typeof(KeywordExpression) => returned,
                _ => null
            };
            if (binary is null || binary.Operator == "==" && binary.Left?.GetType() == typeof(VariableExpression) &&
                binary.Right?.GetType() == typeof(VariableExpression)) { return false; }
            if (!(binary.Operator == "==" && NativeType(method.Signature.ReturnType, typeof(bool))) &&
                !(binary.Operator is "+" or "-" && NativeType(method.Signature.ReturnType, typeof(int)))) { return false; }
            if (!ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) ||
                parameters.Any(parameter => parameter?.GetType() != typeof(ParameterProvider) ||
                    !NativeType(parameter.Type, typeof(int)) || parameter.IsRef || parameter.IsOut || parameter.IsIn || parameter.IsParams)) { return false; }
            int remaining = MaximumOccurrences - (ReferenceEquals(body, binary) ? 0 : 2);
            if (!KnownTree(binary, true, 0, ref remaining)) { return false; }
            root = binary;
            return true;
        }

        internal static bool IsBinaryOperator(string value) => value is "==" or "+" or "-" or "*";

        internal static bool OmitChild(string parent, string child, bool left) =>
            parent == "-" && left && child == "-" ||
            parent == "+" && !left && child == "*" ||
            parent == "==" && child == "+";

        private static bool KnownTree(ValueExpression? value, bool root, int depth, ref int remaining)
        {
            if (remaining-- <= 0 || depth >= MaximumDepth || value is null) { return false; }
            if (value.GetType() == typeof(VariableExpression)) { return true; }
            if (value is not BinaryOperatorExpression { AddParentheses: true } binary ||
                !(binary.Operator is "+" or "-" or "*" || root && binary.Operator == "==")) { return false; }
            return KnownTree(binary.Left, false, depth + 1, ref remaining) &&
                KnownTree(binary.Right, false, depth + 1, ref remaining);
        }

        private static bool NativeType(CSharpType? type, Type expected) => type?.GetType() == typeof(CSharpType) &&
            type.IsFrameworkType && ReferenceEquals(type.FrameworkType, expected) && !type.IsNullable &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // The written declaration and constraint establish ownership. Runtime
    // generic parameters below are only the native formatting descriptors.
    internal readonly record struct PreparedTypedDefaultCast(CastExpression Cast, TypedDefaultExpression Operand, WhereExpression Constraint)
    {
        internal static PreparedTypedDefaultCast? TryGet(MethodProvider method)
        {
            if (method.GetType() != typeof(MethodProvider) || method.BodyStatements is not null ||
                method.BodyExpression is not CastExpression { Inner: TypedDefaultExpression operand } cast ||
                !HasSignature(method.Signature, out var constraint) ||
                !ReferenceEquals(cast.Type, method.Signature.ReturnType) || !ReferenceEquals(operand.Type, cast.Type)) { return null; }
            return new(cast, operand, constraint);
        }

        internal static bool HasSignature(MethodSignature signature, out WhereExpression constraint)
        {
            constraint = null!;
            if (signature.Modifiers != MethodSignatureModifiers.Public || signature.NonDocumentComment is not null ||
                signature.HasUnsupportedBaseContract || signature.ExplicitInterface is not null || signature.OriginalSymbol is not null ||
                !ContractCollections.TryCopy(signature.Attributes, out var attributes) || !attributes.IsEmpty ||
                !ContractCollections.TryCopy(signature.Parameters, out var parameters) || !parameters.IsEmpty ||
                !ContractCollections.TryCopy(signature.GenericArguments, out var arguments) || arguments.Length != 1 ||
                !PreparedGeneratedGenericSignature.IsPureParameterSpelling(arguments[0]) ||
                !ContractCollections.TryCopy(signature.GenericParameterConstraints, out var constraints) || constraints.Length != 1 ||
                constraints[0] is not { } where || !ReferenceEquals(where.Type, arguments[0]) ||
                !ContractCollections.TryCopy(where.Constraints, out var kinds) ||
                kinds is not [KeywordExpression { Keyword: "struct", Expression: null }]) { return false; }
            if (signature.ReturnType is not { } nullable || nullable.GetType() != typeof(CSharpType) || !nullable.IsFrameworkType ||
                !ReferenceEquals(nullable.FrameworkType, arguments[0].FrameworkType) || nullable.Name != arguments[0].Name ||
                nullable.Namespace != arguments[0].Namespace || !nullable.IsNullable || nullable.IsValueType || nullable.IsStruct ||
                nullable.IsLiteral || nullable.HasUnionMetadata || nullable.DeclaringType is not null ||
                !ContractCollections.TryCopy(nullable.Arguments, out var typeArguments) || !typeArguments.IsEmpty) { return false; }
            // Avoid declaration/name collisions in the generated method itself.
            if (signature.Name == arguments[0].Name) { return false; }
            constraint = where;
            return true;
        }
    }
}

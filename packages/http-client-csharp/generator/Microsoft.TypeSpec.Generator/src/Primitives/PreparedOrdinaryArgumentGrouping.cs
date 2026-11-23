// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Source-only root and value shape. Neither this profile nor a declared
    // argument map establishes lookup or selects a generated declaration.
    internal readonly record struct PreparedOrdinaryArgumentGrouping(InvokeMethodExpression Call, IReadOnlyList<ValueExpression> OriginalList,
        ImmutableArray<ValueExpression> OrderedArguments, long InvocationRevision, bool HasCallerExpression)
    {
        internal int CandidateCount => HasCallerExpression ? 1 : OrderedArguments.Length;
        internal static PreparedOrdinaryArgumentGrouping? TryGet(MethodProvider method, object body, ImmutableArray<ParameterProvider> parameters = default)
        {
            if (method.GetType() != typeof(MethodProvider) || method.BodyStatements is not null || !ReferenceEquals(method.BodyExpression, body) ||
                body is not InvokeMethodExpression { InstanceReference: null, MethodSignature: null, MethodName: { } calleeName,
                    CallAsAsync: false, ExtensionType: null, TypeArguments: null, MethodProviderIntent: null, DeclaredContractIntent: null,
                    DelegateParameterIntent: null, IsReducedExtensionIntent: false } call ||
                method.Signature.Modifiers != MethodSignatureModifiers.Public ||
                !PreparedUsedLocalInitializer.IsInt(method.Signature.ReturnType) && !IsObject(method.Signature.ReturnType) ||
                method.Signature.HasUnsupportedBaseContract || parameters.IsDefault && !ContractCollections.TryCopy(method.Signature.Parameters, out parameters) ||
                parameters.Length is not (2 or 4) || !parameters.All(IsCachedIntFormal)) { return null; }
            // The unqualified call is in the exact arrow scope. A formal's
            // actual cached declaration can shadow the audited private member.
            if (parameters.Any(parameter => string.Equals(parameter.CachedVariableExpression!.Declaration.RequestedName,
                calleeName, StringComparison.Ordinal))) { return null; }
            bool hasCallerExpression = IsObject(method.Signature.ReturnType);
            var list = call.Arguments;
            if (!ContractCollections.TryCopy(list, out var arguments)) { return null; }
            if (hasCallerExpression
                ? parameters.Length != 2 || arguments.Length is not (1 or 2) || !IsAddition(arguments[0]) ||
                    arguments.Length == 2 && LiteralPayload(arguments[1]) is not { Literal: string }
                : arguments.Length != parameters.Length / 2 || arguments.Any(argument => !IsAddition(argument)) ||
                    arguments.Length == 2 && ReferenceEquals(arguments[0], arguments[1])) { return null; }
            // One original ordered grammar fact, shared by every child choice.
            return new(call, list, arguments, call.DeclarationRevision, hasCallerExpression);

            static bool IsAddition(ValueExpression argument) => argument is BinaryOperatorExpression { Operator: "+", AddParentheses: true } addition &&
                addition.Left?.GetType() == typeof(VariableExpression) && addition.Right?.GetType() == typeof(VariableExpression);
        }

        internal bool IsOriginalListCurrent => Call.DeclarationRevision == InvocationRevision && ReferenceEquals(Call.Arguments, OriginalList) &&
            ContractCollections.SameReferences(Call.Arguments, OrderedArguments);

        internal bool ContainsAddition(object node) => OrderedArguments.Take(CandidateCount).Any(argument => ReferenceEquals(argument, node));

        internal bool MatchesCallee(MethodProvider method)
        {
            if (method.GetType() != typeof(MethodProvider) || method.BodyStatements is not null ||
                method.Signature.Modifiers != MethodSignatureModifiers.Private ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters)) { return false; }
            if (HasCallerExpression)
            {
                return HasCallerExpressionSignature(method.Signature) && IsFormal(method.BodyExpression, parameters[1]);
            }
            if (!PreparedUsedLocalInitializer.IsInt(method.Signature.ReturnType) || parameters.Length != OrderedArguments.Length ||
                !parameters.All(IsCachedIntFormal)) { return false; }
            return parameters.Length == 1
                ? IsFormal(method.BodyExpression, parameters[0])
                : method.BodyExpression is BinaryOperatorExpression { Operator: "+", AddParentheses: false } addition &&
                    IsFormal(addition.Left, parameters[0]) && IsFormal(addition.Right, parameters[1]);

            static bool IsFormal(ValueExpression? expression, ParameterProvider parameter) => expression is VariableExpression variable &&
                variable.GetType() == typeof(VariableExpression) && ReferenceEquals(variable.Declaration, parameter.CachedVariableExpression!.Declaration);
        }

        internal static bool HasCallerExpressionSignature(MethodSignature signature)
        {
            if (!IsObject(signature.ReturnType) || signature.HasUnsupportedBaseContract || signature.NonDocumentComment is not null ||
                signature.ExplicitInterface is not null || signature.GenericArguments is not null || signature.GenericParameterConstraints is not null ||
                !ContractCollections.TryCopy(signature.Attributes, out var attributes) || !attributes.IsEmpty ||
                !ContractCollections.TryCopy(signature.Parameters, out var parameters) || parameters.Length != 2) { return false; }
            if (signature.Modifiers == MethodSignatureModifiers.Public) { return parameters.All(IsCachedIntFormal); }
            if (signature.Modifiers != MethodSignatureModifiers.Private || !HasCachedOrdinaryFormal(parameters[0]) || !HasCachedOrdinaryFormal(parameters[1]))
            { return false; }
            var value = parameters[0];
            var text = parameters[1];
            return IsObject(value.Type) && value.DefaultValue is null &&
                ContractCollections.TryCopy(value.Attributes, out var valueAttributes) && valueAttributes.IsEmpty &&
                PreparedStringCoalesce.IsString(text.Type) && LiteralPayload(text.DefaultValue) is { Literal: "" } &&
                ContractCollections.TryCopy(text.Attributes, out var observerAttributes) && observerAttributes.Length == 1 &&
                observerAttributes[0] is { Data: null, CachedType.IsFrameworkType: true } attribute &&
                ReferenceEquals(attribute.CachedType.FrameworkType, typeof(CallerArgumentExpressionAttribute)) &&
                ContractCollections.TryCopy(attribute.CachedArguments, out var arguments) && arguments.Length == 1 &&
                LiteralPayload(arguments[0]) is { Literal: string target } && target == value.Name &&
                ContractCollections.TryCopy(attribute.CachedPositionalArguments, out var named) && named.IsEmpty;
        }

        private static bool IsCachedIntFormal(ParameterProvider? parameter) => HasCachedOrdinaryFormal(parameter) &&
            PreparedUsedLocalInitializer.IsInt(parameter!.Type) && parameter.DefaultValue is null &&
            ContractCollections.TryCopy(parameter.Attributes, out var attributes) && attributes.IsEmpty;

        private static bool HasCachedOrdinaryFormal(ParameterProvider? parameter) => parameter is not null &&
            parameter.HasCachedEmittedDeclarationState && parameter.CachedVariableExpression?.GetType() == typeof(VariableExpression) &&
            !parameter.IsRef && !parameter.IsOut && !parameter.IsIn && !parameter.IsParams &&
            !parameter.HasUnsupportedParameterModifiers && !parameter.HasUnsupportedDefaultValue;

        private static bool IsObject(CSharpType? type) => type?.GetType() == typeof(CSharpType) && type.IsFrameworkType &&
            ReferenceEquals(type.FrameworkType, typeof(object)) && !type.IsNullable && !type.IsValueType && !type.IsStruct &&
            !type.IsLiteral && !type.HasUnionMetadata && type.DeclaringType is null && type.Name == nameof(Object) &&
            type.Namespace == typeof(object).Namespace && ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;

        private static LiteralExpression? LiteralPayload(ValueExpression? value) => value switch
        {
            LiteralExpression literal => literal,
            ScopedApi wrapper when PreparedCallProofs.IsTransparentWrapper(wrapper) => wrapper.Original as LiteralExpression,
            _ => null
        };

        internal static ITypeSymbol? ResolveAddition(ValueExpression? value, PreparedSourceOccurrence? occurrence, CSharpCompilation compilation)
        {
            if (value is not BinaryOperatorExpression { Operator: "+", AddParentheses: true,
                    Left: VariableExpression left, Right: VariableExpression right } binary ||
                occurrence is null || occurrence.IsOpaque || !ReferenceEquals(occurrence.Producer, binary) || !occurrence.IsCurrent() ||
                occurrence.Children.Length != 2 || !IsBoundOperand(occurrence.Children[0], left) ||
                !IsBoundOperand(occurrence.Children[1], right)) { return null; }
            var type = compilation.GetSpecialType(SpecialType.System_Int32);
            return type.TypeKind == TypeKind.Error ? null : type;

            static bool IsBoundOperand(PreparedSourceOccurrence node, VariableExpression variable) => !node.IsOpaque && node.Children.IsEmpty &&
                ReferenceEquals(node.Producer, variable) && PreparedUsedLocalInitializer.IsInt(node.ResolveParameterType(variable));
        }
    }
}

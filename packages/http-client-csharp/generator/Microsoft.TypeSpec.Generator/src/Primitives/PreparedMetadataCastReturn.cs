// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Four exact native arrow return forms. The compiler input snapshot can
    // certify their target symbols without binding or parsing generated text.
    internal readonly record struct PreparedMetadataCastReturn(CastExpression Cast, bool NeedsObjectFormal)
    {
        internal static PreparedMetadataCastReturn? TryGet(MethodProvider method, CSharpCompilation compilation, CSharpType ownerType)
        {
            if (method.GetType() != typeof(MethodProvider) || method.BodyStatements is not null ||
                method.BodyExpression is not CastExpression cast || !IsObject(method.Signature.ReturnType) ||
                method.Signature.HasUnsupportedBaseContract || method.Signature.OriginalSymbol is not null ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters)) { return null; }

            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            var core = objectType.ContainingAssembly;
            if (core is null || objectType.TypeKind == TypeKind.Error || !compilation.References.Any(reference =>
                (reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global")) &&
                SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), core))) { return null; }

            ITypeSymbol? target;
            bool boxing = false;
            bool needsFormal = false;
            if (parameters.IsEmpty && cast.Inner is KeywordExpression { Keyword: "null", Expression: null } &&
                ExactRuntime(cast.Type, typeof(Exception), "Exception", false, 0))
            {
                target = FindCoreType(compilation, core, ownerType, "System.Exception", 0);
            }
            else if (parameters.IsEmpty && cast.Inner is KeywordExpression { Keyword: "default", Expression: null } &&
                compilation.LanguageVersion >= LanguageVersion.CSharp7_1 && ExactRuntime(cast.Type, typeof(DateTimeOffset), "DateTimeOffset", true, 0))
            {
                target = FindCoreType(compilation, core, ownerType, "System.DateTimeOffset", 0);
                boxing = true;
            }
            else if (parameters.Length == 1 && parameters[0] is { } parameter && IsObject(parameter.Type) &&
                parameter.HasCachedEmittedDeclarationState && parameter.CachedVariableExpression?.GetType() == typeof(VariableExpression) &&
                parameter.OriginalSymbol is null && !parameter.HasUnsupportedParameterModifiers && !parameter.HasUnsupportedDefaultValue &&
                cast.Inner?.GetType() == typeof(VariableExpression))
            {
                needsFormal = true;
                if (ExactRuntime(cast.Type, typeof(IList<>), "IList", false, 1) &&
                    PreparedStringCoalesce.IsString(cast.Type.Arguments[0]) &&
                    FindCoreType(compilation, core, ownerType, "System.Collections.Generic.IList", 1) is { TypeKind: TypeKind.Interface } definition)
                {
                    var element = compilation.GetSpecialType(SpecialType.System_String);
                    if (element.TypeKind == TypeKind.Error || !SymbolEqualityComparer.Default.Equals(element.ContainingAssembly, core)) { return null; }
                    target = definition.Construct(element);
                }
                else if (ExactRuntime(cast.Type, typeof(Exception[]), "Exception[]", false, 0) &&
                    FindCoreType(compilation, core, ownerType, "System.Exception", 0) is { TypeKind: TypeKind.Class } element)
                {
                    target = compilation.CreateArrayTypeSymbol(element);
                }
                else { return null; }
            }
            else { return null; }
            if (target is null || target.TypeKind == TypeKind.Error) { return null; }
            var returned = compilation.ClassifyConversion(target, objectType);
            if (!returned.IsImplicit || returned.IsUserDefined ||
                (boxing ? !target.IsValueType || !returned.IsBoxing : !target.IsReferenceType || !returned.IsReference)) { return null; }
            if (needsFormal)
            {
                var operand = compilation.ClassifyConversion(objectType, target);
                if (!operand.IsExplicit || !operand.IsReference || operand.IsUserDefined) { return null; }
            }
            return new(cast, needsFormal);
        }

        internal static bool IsObject(CSharpType? type) => ExactRuntime(type, typeof(object), "Object", false, 0);

        private static bool ExactRuntime(CSharpType? type, Type expected, string name, bool valueType, int arity) =>
            type?.GetType() == typeof(CSharpType) && type.IsFrameworkType && ReferenceEquals(type.FrameworkType, expected) &&
            type.IsValueType == valueType && type.IsStruct == valueType && !type.IsNullable && !type.IsLiteral && !type.HasUnionMetadata &&
            type.DeclaringType is null && type.Name == name && type.Namespace == expected.Namespace &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.Length == arity;

        private static INamedTypeSymbol? FindCoreType(CSharpCompilation compilation, IAssemblySymbol core, CSharpType ownerType,
            string qualifiedName, int arity)
        {
            // These targets are spelled global:: by the native writer. Competing
            // source types or namespace prefixes must not supply a different cast.
            var ownerName = ownerType.Namespace + "." + ownerType.Name;
            if (qualifiedName == ownerName || qualifiedName.StartsWith(ownerName + ".", StringComparison.Ordinal) ||
                ownerType.Namespace == qualifiedName || ownerType.Namespace.StartsWith(qualifiedName + ".", StringComparison.Ordinal)) { return null; }
            var parts = qualifiedName.Split('.');
            var scope = compilation.GlobalNamespace;
            for (int index = 0; index < parts.Length - 1; index++)
            {
                var members = scope.GetMembers(parts[index]).Take(2).ToArray();
                if (members.Length != 1 || members[0] is not INamespaceSymbol next) { return null; }
                scope = next;
            }
            var candidates = scope.GetMembers(parts[^1]).Take(2).ToArray();
            var canonical = core.GetTypeByMetadataName(arity == 0 ? qualifiedName : qualifiedName + "`1");
            return candidates.Length == 1 && candidates[0] is INamedTypeSymbol candidate && candidate.Arity == arity &&
                candidate.TypeKind != TypeKind.Error && candidate.DeclaredAccessibility == Accessibility.Public &&
                SymbolEqualityComparer.Default.Equals(candidate, canonical) ? candidate : null;
        }
    }
}

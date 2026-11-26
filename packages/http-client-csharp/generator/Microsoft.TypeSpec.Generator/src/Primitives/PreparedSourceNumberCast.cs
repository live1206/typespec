// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // One native source-defined conversion and its direct return slots. The
    // captured input compilation, not the advertised cast type, selects it.
    internal readonly record struct PreparedSourceNumberCast(CastExpression Cast, bool IsStatement)
    {
        internal static PreparedSourceNumberCast? TryGet(MethodProvider method, CSharpCompilation compilation, CSharpType ownerType)
        {
            if (method.GetType() != typeof(MethodProvider) || method.Signature.OriginalSymbol is not null ||
                method.Signature.HasUnsupportedBaseContract || !PreparedMetadataCastReturn.IsObject(method.Signature.ReturnType) ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) || parameters.Length != 1 ||
                parameters[0] is not { } formal || !PreparedUsedLocalInitializer.IsInt(formal.Type) || !formal.HasCachedEmittedDeclarationState ||
                formal.OriginalSymbol is not null || formal.HasUnsupportedParameterModifiers || formal.HasUnsupportedDefaultValue ||
                compilation.Options.CheckOverflow) { return null; }

            bool isStatement = method.BodyStatements is not null;
            var cast = method.BodyStatements switch
            {
                null => method.BodyExpression as CastExpression,
                ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: CastExpression returned } } statement
                    when statement.GetType() == typeof(ExpressionStatement) => returned,
                _ => null
            };
            if (cast is null || cast.Inner?.GetType() != typeof(VariableExpression) || !IsNativeTarget(cast.Type) ||
                FindSourceTarget(compilation, ownerType) is not { } target ||
                compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) { return null; }

            var intType = compilation.GetSpecialType(SpecialType.System_Int32);
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            var core = objectType.ContainingAssembly;
            if (core is null || intType.TypeKind == TypeKind.Error || objectType.TypeKind == TypeKind.Error ||
                !SymbolEqualityComparer.Default.Equals(core, intType.ContainingAssembly) || !compilation.References.Any(reference =>
                    (reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global")) &&
                    SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), core))) { return null; }

            var conversion = compilation.ClassifyConversion(intType, target);
            if (!conversion.IsExplicit || !conversion.IsUserDefined || conversion.MethodSymbol is not
                { MethodKind: MethodKind.Conversion, MetadataName: "op_Explicit", DeclaredAccessibility: Accessibility.Public,
                    IsStatic: true, IsGenericMethod: false, ReturnsByRef: false, ReturnsByRefReadonly: false } operation ||
                !SymbolEqualityComparer.Default.Equals(operation.ContainingType, target) ||
                !SymbolEqualityComparer.Default.Equals(operation.ReturnType, target) || operation.Parameters.Length != 1 ||
                operation.Parameters[0].RefKind != RefKind.None || !SymbolEqualityComparer.Default.Equals(operation.Parameters[0].Type, intType) ||
                !HasCapturedDeclarations(operation, compilation)) { return null; }
            var returnedValue = compilation.ClassifyConversion(target, objectType);
            return returnedValue.IsImplicit && returnedValue.IsBoxing && !returnedValue.IsUserDefined ? new(cast, isStatement) : null;
        }

        private static bool IsNativeTarget(CSharpType? type) => type?.GetType() == typeof(CSharpType) && !type.IsFrameworkType &&
            type.Namespace == "Fixtures" && type.Name == "Number" && type.IsPublic && type.IsValueType && type.IsStruct && !type.IsEnum &&
            !type.IsNullable && !type.IsLiteral && !type.HasUnionMetadata && type.DeclaringType is null &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;

        private static INamedTypeSymbol? FindSourceTarget(CSharpCompilation compilation, CSharpType ownerType)
        {
            var ownerName = ownerType.Namespace.Length == 0 ? ownerType.Name : ownerType.Namespace + "." + ownerType.Name;
            if (ownerName is "Fixtures" or "Fixtures.Number" || ownerType.Namespace == "Fixtures.Number" ||
                ownerType.Namespace.StartsWith("Fixtures.Number.", StringComparison.Ordinal)) { return null; }
            var prefixes = compilation.GlobalNamespace.GetMembers("Fixtures").Take(2).ToArray();
            if (prefixes.Length != 1 || prefixes[0] is not INamespaceSymbol scope) { return null; }
            var names = scope.GetMembers("Number").Take(2).ToArray();
            return names.Length == 1 && names[0] is INamedTypeSymbol
                { TypeKind: TypeKind.Struct, Arity: 0, IsFileLocal: false, IsRefLikeType: false, DeclaredAccessibility: Accessibility.Public } target &&
                SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly) &&
                HasCapturedDeclarations(target, compilation) && target.GetMembers("op_CheckedExplicit").IsEmpty ? target : null;
        }

        private static bool HasCapturedDeclarations(ISymbol symbol, CSharpCompilation compilation) =>
            !symbol.DeclaringSyntaxReferences.IsDefaultOrEmpty && symbol.DeclaringSyntaxReferences.All(declaration => compilation.ContainsSyntaxTree(declaration.SyntaxTree));
    }
}

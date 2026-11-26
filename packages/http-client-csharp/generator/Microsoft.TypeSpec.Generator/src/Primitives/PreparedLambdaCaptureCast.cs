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
    // One original list and one nested identity cast. Capture presence still
    // comes from the actual selected callable, never from this shape alone.
    internal readonly record struct PreparedLambdaCaptureCast(ValueExpression Call,
        IReadOnlyList<ValueExpression> OriginalList, ImmutableArray<ValueExpression> Arguments, FuncExpression Lambda, CastExpression Cast,
        long Revision, long InvocationRevision)
    {
        internal static PreparedLambdaCaptureCast? TryGetConstructor(MethodProvider method)
        {
            return IsCaller(method) && PreparedMetadataCastReturn.IsObject(method.Signature.ReturnType) &&
                method.BodyExpression is NewInstanceExpression { Type: { } type, InitExpression: null, ConstructorIntent: null,
                    ConstructorProviderIntent: null } creation && IsHolder(type)
                ? TryGetArguments(creation, creation.Parameters) : null;
        }

        internal static PreparedLambdaCaptureCast? TryGetGeneratedMethod(MethodProvider method)
        {
            if (!IsCaller(method) || !HasGeneratedSignature(method.Signature) ||
                method.BodyExpression is not InvokeMethodExpression { MethodName: { } name, MethodSignature: null, InstanceReference: null,
                    CallAsAsync: false, TypeArguments: null, ExtensionType: null, DeclaredContractIntent: null,
                    MethodProviderIntent: null, DelegateParameterIntent: null, IsReducedExtensionIntent: false } invocation ||
                method.Signature.Parameters[0].CachedVariableExpression!.Declaration.RequestedName == name) { return null; }
            return TryGetArguments(invocation, invocation.Arguments);
        }

        private static bool IsCaller(MethodProvider method) => method.GetType() == typeof(MethodProvider) &&
            method.Signature.Modifiers == MethodSignatureModifiers.Public && method.Signature.OriginalSymbol is null &&
            !method.Signature.HasUnsupportedBaseContract && method.BodyStatements is null &&
            ContractCollections.TryCopy(method.Signature.Parameters, out var formals) && formals.Length == 1 && IsOrdinaryFormal(formals[0]) &&
            PreparedUsedLocalInitializer.IsInt(formals[0].Type);

        private static PreparedLambdaCaptureCast? TryGetArguments(ValueExpression call, IReadOnlyList<ValueExpression> list)
        {
            if (!ContractCollections.TryCopy(list, out var actuals) || actuals.Length is not (1 or 2) ||
                actuals.Length == 2 && !IsLiteralString(actuals[1]) ||
                actuals[0] is not FuncExpression { BlockBody: ExpressionStatement { Expression: KeywordExpression
                    { Keyword: "return", Expression: CastExpression cast } } statement } lambda ||
                statement.GetType() != typeof(ExpressionStatement) || !ContractCollections.TryCopy(lambda.Parameters, out var lambdaFormals) ||
                !lambdaFormals.IsEmpty || !PreparedUsedLocalInitializer.IsInt(cast.Type) || cast.Inner?.GetType() != typeof(VariableExpression)) { return null; }
            return new(call, list, actuals, lambda, cast, call.SourceRevision, (call as InvokeMethodExpression)?.DeclarationRevision ?? 0);
        }

        internal bool IsListCurrent => Call.SourceRevision == Revision && (Call switch
        {
            NewInstanceExpression creation => ReferenceEquals(creation.Parameters, OriginalList),
            InvokeMethodExpression invocation => invocation.DeclarationRevision == InvocationRevision && ReferenceEquals(invocation.Arguments, OriginalList),
            _ => false
        }) && ContractCollections.SameReferences(OriginalList, Arguments);

        internal static bool HasGeneratedSignature(MethodSignature signature)
        {
            if (!PreparedStringCoalesce.IsString(signature.ReturnType) || signature.OriginalSymbol is not null ||
                signature.HasUnsupportedBaseContract || signature.NonDocumentComment is not null || signature.ExplicitInterface is not null ||
                signature.GenericArguments is not null || signature.GenericParameterConstraints is not null ||
                !ContractCollections.TryCopy(signature.Attributes, out var attributes) || !attributes.IsEmpty ||
                !ContractCollections.TryCopy(signature.Parameters, out var formals)) { return false; }
            if (signature.Modifiers == MethodSignatureModifiers.Public)
            {
                return formals.Length == 1 && IsOrdinaryFormal(formals[0]) && PreparedUsedLocalInitializer.IsInt(formals[0].Type);
            }
            return signature.Modifiers == (MethodSignatureModifiers.Private | MethodSignatureModifiers.Static) && formals.Length == 2 &&
                IsOrdinaryFormal(formals[0]) && IsFuncInt(formals[0].Type) && HasCachedFormal(formals[1]) &&
                PreparedStringCoalesce.IsString(formals[1].Type) && formals[1].DefaultValue is KeywordExpression { Keyword: "null", Expression: null } &&
                ContractCollections.TryCopy(formals[1].Attributes, out var observers) && observers is [{ Data: null, CachedType: { IsFrameworkType: true } type } observer] &&
                ReferenceEquals(type.FrameworkType, typeof(CallerArgumentExpressionAttribute)) &&
                ContractCollections.TryCopy(observer.CachedPositionalArguments, out var named) && named.IsEmpty &&
                ContractCollections.TryCopy(observer.CachedArguments, out var arguments) && arguments.Length == 1 &&
                LiteralPayload(arguments[0]) is { Literal: string name } && name == formals[0].Name;
        }

        internal static bool MatchesGeneratedCallee(MethodProvider method) => method.GetType() == typeof(MethodProvider) &&
            method.BodyStatements is null && method.Signature.Modifiers == (MethodSignatureModifiers.Private | MethodSignatureModifiers.Static) &&
            HasGeneratedSignature(method.Signature) && method.BodyExpression is VariableExpression variable && variable.GetType() == typeof(VariableExpression) &&
            ReferenceEquals(variable.Declaration, method.Signature.Parameters[1].CachedVariableExpression!.Declaration);

        private static bool IsOrdinaryFormal(ParameterProvider? formal) => HasCachedFormal(formal) && formal!.DefaultValue is null &&
            ContractCollections.TryCopy(formal.Attributes, out var attributes) && attributes.IsEmpty;

        private static bool HasCachedFormal(ParameterProvider? formal) => formal is not null && formal.HasCachedEmittedDeclarationState &&
            formal.CachedVariableExpression?.GetType() == typeof(VariableExpression) && formal.OriginalSymbol is null &&
            !formal.IsRef && !formal.IsOut && !formal.IsIn && !formal.IsParams && !formal.HasUnsupportedParameterModifiers && !formal.HasUnsupportedDefaultValue;

        private static bool IsFuncInt(CSharpType? type) => type?.GetType() == typeof(CSharpType) && type.IsFrameworkType &&
            ReferenceEquals(type.FrameworkType, typeof(Func<>)) && type.Name == "Func" && type.Namespace == "System" && !type.IsNullable &&
            !type.IsLiteral && !type.HasUnionMetadata && !type.IsStruct && !type.IsValueType && type.DeclaringType is null &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.Length == 1 && PreparedUsedLocalInitializer.IsInt(arguments[0]);

        internal static INamedTypeSymbol? FindGlobalFunc(CSharpCompilation compilation, CSharpType ownerType)
        {
            var intType = compilation.GetSpecialType(SpecialType.System_Int32);
            var core = intType.ContainingAssembly;
            if (core is null || intType.TypeKind == TypeKind.Error ||
                !compilation.References.Any(reference => (reference.Properties.Aliases.IsDefaultOrEmpty || reference.Properties.Aliases.Contains("global")) &&
                    SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), core)) ||
                ownerType.Namespace == "System" && ownerType.Name == "Func" || ownerType.Namespace == "System.Func" ||
                ownerType.Namespace.StartsWith("System.Func.", StringComparison.Ordinal) ||
                compilation.GlobalNamespace.GetMembers("System").Take(2).ToArray() is not [INamespaceSymbol scope] ||
                scope.GetMembers("Func").OfType<INamedTypeSymbol>().Where(type => type.Arity == 1).Take(2).ToArray() is not [var func] ||
                !SymbolEqualityComparer.Default.Equals(func, core.GetTypeByMetadataName("System.Func`1"))) { return null; }
            var constructed = func.Construct(intType);
            return PreparedCallProofs.ResolveMetadataDelegateInvoke(constructed, compilation) is { Parameters.IsEmpty: true } invoke &&
                SymbolEqualityComparer.Default.Equals(invoke.ReturnType, intType) ? constructed : null;
        }

        private static bool IsLiteralString(ValueExpression value) => LiteralPayload(value) is { Literal: string };

        private static LiteralExpression? LiteralPayload(ValueExpression value) =>
            (value is ScopedApi wrapper && PreparedCallProofs.IsTransparentWrapper(wrapper) ? wrapper.Original : value) as LiteralExpression;

        private static bool IsHolder(CSharpType type) => type.GetType() == typeof(CSharpType) && !type.IsFrameworkType &&
            type.IsPublic && !type.IsStruct && !type.IsValueType && !type.IsNullable && !type.IsLiteral && !type.HasUnionMetadata &&
            type.DeclaringType is null && type.Namespace == "Sample" && type.Name == "Holder" &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;

        internal static INamedTypeSymbol? FindHolder(CSharpCompilation compilation, CSharpType ownerType)
        {
            var ownerName = ownerType.Namespace.Length == 0 ? ownerType.Name : ownerType.Namespace + "." + ownerType.Name;
            if (ownerName is "Sample" or "Sample.Holder" || ownerType.Namespace == "Sample.Holder" ||
                ownerType.Namespace.StartsWith("Sample.Holder.", StringComparison.Ordinal)) { return null; }
            var prefixes = compilation.GlobalNamespace.GetMembers("Sample").Take(2).ToArray();
            if (prefixes.Length != 1 || prefixes[0] is not INamespaceSymbol scope) { return null; }
            var names = scope.GetMembers("Holder").Take(2).ToArray();
            return names.Length == 1 && names[0] is INamedTypeSymbol
                { TypeKind: TypeKind.Class, Arity: 0, IsFileLocal: false, IsStatic: false, IsAbstract: false,
                    DeclaredAccessibility: Accessibility.Public, BaseType.SpecialType: SpecialType.System_Object } target &&
                SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly) && HasCapturedDeclarations(target, compilation)
                ? target : null;
        }

        internal static IMethodSymbol? FindConstructor(INamedTypeSymbol holder, CSharpCompilation compilation)
        {
            if (holder.InstanceConstructors is not [IMethodSymbol { DeclaredAccessibility: Accessibility.Public } constructor] ||
                !HasCapturedDeclarations(constructor, compilation) || constructor.Parameters.Length != 2) { return null; }
            var intType = compilation.GetSpecialType(SpecialType.System_Int32);
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            var core = objectType.ContainingAssembly;
            if (core is null || intType.TypeKind == TypeKind.Error || objectType.TypeKind == TypeKind.Error ||
                !SymbolEqualityComparer.Default.Equals(intType.ContainingAssembly, core) ||
                core.GetTypeByMetadataName("System.Func`1") is not { } func) { return null; }
            var lambda = constructor.Parameters[0];
            var text = constructor.Parameters[1];
            if (!SymbolEqualityComparer.Default.Equals(lambda.Type, func.Construct(intType)) || lambda.IsOptional ||
                lambda.RefKind != RefKind.None || lambda.IsParams || !lambda.GetAttributes().IsEmpty ||
                text.Type.SpecialType != SpecialType.System_String || text.RefKind != RefKind.None || text.IsParams ||
                !text.IsOptional || !text.HasExplicitDefaultValue || text.ExplicitDefaultValue is not null) { return null; }
            // Require exactly the actual source observer (the shared selected
            // contract also checks its canonical identity and correspondence).
            if (text.GetAttributes() is not [var observer] || observer.AttributeClass is not { } attribute ||
                !SymbolEqualityComparer.Default.Equals(attribute, core.GetTypeByMetadataName("System.Runtime.CompilerServices.CallerArgumentExpressionAttribute")) ||
                observer.ConstructorArguments is not [{ Value: string observed }] || observed != lambda.Name) { return null; }
            return constructor;
        }

        private static bool HasCapturedDeclarations(ISymbol symbol, Compilation compilation) =>
            !symbol.DeclaringSyntaxReferences.IsDefaultOrEmpty && symbol.DeclaringSyntaxReferences.All(declaration => compilation.ContainsSyntaxTree(declaration.SyntaxTree));
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Microsoft.TypeSpec.Generator.Input
{
    // Exact built-in registrations describe audited getter dispatch, never name
    // permission. Input checks may read only already-loaded data, not lazy builders.
    internal static class PocHeaderFinalizationBridge
    {
        internal readonly record struct ColdDescriptionInputs(string? Summary, string? Doc, int FallbackCharacters, string? FallbackName = null);
        internal sealed record Audit(Type XmlDocsBuilder, Type DescriptionBuilder, Type WarningBuilder,
            Func<object, InputNamespace?, bool> SafeInputs, Func<object, object?>? RequiredTypeOwner = null,
            Func<object, ColdDescriptionInputs>? DescriptionInputs = null)
        {
            // Exact built-in descriptions may also reference an immutable
            // static type identity. Core audits its ACTUAL current graph.
            internal object? AdditionalDescriptionType { get; init; }
            // Some native builders have a fixed-text singleton branch.
            internal Func<object, object?>? DescriptionTypeOwner { get; init; }
        }
        internal sealed record CachedAttributeAudit(Type Builder, Type BackCompatibilityBuilder, Type AnalysisPolicy,
            Func<object, bool>? SafeDependencies = null);
        // Facts about exact audited dispatch, and native observations only. Core
        // owns the recipe, currentness checks and budget; this bridge grants no
        // generation, name or Task03 permission.
        internal sealed record ContextRecipeAudit(Type[] ProviderTypes, Type[] MethodTypes,
            Func<Type, Func<int, bool>, bool> SpendDirectNegative,
            Func<Type, Func<int, bool>, bool> SpendHelperNegative,
            Func<object?> CachedNativeOutput);
        private static readonly ConditionalWeakTable<Type, ContextRecipeAudit> _contextRecipes = new();
        internal static void InstallContextRecipe(Type ownerType, ContextRecipeAudit audit) =>
            _contextRecipes.GetValue(ownerType, _ => audit);
        internal static ContextRecipeAudit? GetContextRecipe(object owner) =>
            _contextRecipes.TryGetValue(owner.GetType(), out var audit) ? audit : null;
        private static readonly ConditionalWeakTable<Type, CachedAttributeAudit> _attributeAudits = new();
        internal static void InstallCachedAttributes(Type ownerType, CachedAttributeAudit audit)
        { _attributeAudits.GetValue(ownerType, _ => audit); }
        internal static CachedAttributeAudit? GetCachedAttributes(object owner) =>
            _attributeAudits.TryGetValue(owner.GetType(), out var audit) ? audit : null;
        private static readonly ConditionalWeakTable<Type, Audit> _audits = new();
        private static readonly ConditionalWeakTable<Assembly, object> _assemblies = new();
        internal static void Install(Type ownerType, Audit audit)
        {
            _audits.GetValue(ownerType, _ => audit);
            _assemblies.GetValue(ownerType.Assembly, static _ => new object());
        }
        internal static bool IsRegisteredAssembly(Assembly assembly) => _assemblies.TryGetValue(assembly, out _);
        internal static Audit? Get(object owner) => _audits.TryGetValue(owner.GetType(), out var audit) ? audit : null;

        internal static bool SafeDescriptionText(string? text)
        {
            if (text is null) { return true; }
            if (text.Length > 4096 || text.Contains('<')) { return false; }
            foreach (var character in text)
            {
                if (char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\u0085')) { return false; }
            }
            return true;
        }

        internal static bool KnownEmpty<T>(IReadOnlyList<T>? items) => items is T[] { Length: 0 } ||
            items is List<T> list && items.GetType() == typeof(List<T>) && list.Count == 0;
    }
}

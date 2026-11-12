// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.TypeSpec.Generator.Providers
{
    /// <summary>
    /// The candidate-form ordering from the structural post-processing POC,
    /// computed from already-known name segments and aliases. Ranking is not
    /// lookup, source-observation evidence, or permission to write a name.
    /// </summary>
    internal static class PreparedPocNameForms
    {
        // Keep the POC's root ordering for equal-cost forms.
        internal enum RootKind { Ordinary, Alias, Global }
        internal readonly record struct Alias(string Name, bool NamespaceAlias, bool Preferred);
        internal readonly record struct Form(RootKind Root, int Start, string? AliasName, bool NamespaceAlias, int Cost, bool PreferredAlias);

        // Match the POC's Identifier renderer without reading emitted source.
        internal static string? RenderIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) { return null; }
            if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None)
            { return "@" + name; }
            return SyntaxFacts.IsValidIdentifier(name) ? name : null;
        }

        internal static ImmutableArray<Form> Rank(int partCount, ImmutableArray<Alias> aliases)
        {
            if (partCount <= 0 || partCount == int.MaxValue || aliases.IsDefault ||
                aliases.Any(static alias => string.IsNullOrEmpty(alias.Name))) { return []; }
            var forms = new List<Form>();
            for (var start = 0; start < partCount; start++)
            {
                forms.Add(new Form(RootKind.Ordinary, start, null, false, partCount - start, false));
            }
            forms.Add(new Form(RootKind.Global, 0, null, false, partCount + 1, false));
            foreach (var alias in aliases)
            {
                for (var start = 0; start <= partCount; start++)
                {
                    if (alias.NamespaceAlias && start == partCount) { continue; }
                    forms.Add(new Form(RootKind.Alias, start, alias.Name, alias.NamespaceAlias,
                        partCount - start + 1, alias.Preferred));
                }
            }
            return forms.OrderBy(static form => form.Cost).ThenBy(static form => form.PreferredAlias ? 0 : 1)
                .ThenBy(static form => form.Root).ThenBy(static form => form.AliasName, StringComparer.Ordinal)
                .ThenBy(static form => form.Start).ToImmutableArray();
        }
    }
}

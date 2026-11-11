// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Observed post-analysis inputs for an approved header operation. This is
    // deliberately not an owned-generation or spelling permission. The caller
    // must supply a complete bounded recipe before invoking a native builder.
    internal sealed class PreparedPocPostMapAttributeBoundary
    {
        private readonly CodeModelGenerator _generator;
        private readonly OutputLibrary _output;
        private readonly IReadOnlyList<TypeProvider> _rootCarrier;
        private readonly ImmutableArray<TypeProvider> _roots;
        private readonly ProviderReferenceMapSession _session;
        private readonly ImmutableHashSet<string> _removals;
        private bool _stale;

        private PreparedPocPostMapAttributeBoundary(CodeModelGenerator generator, OutputLibrary output,
            IReadOnlyList<TypeProvider> carrier, ImmutableArray<TypeProvider> roots, ProviderReferenceMapSession session,
            ImmutableHashSet<string> removals)
        {
            _generator = generator; _output = output; _rootCarrier = carrier;
            _roots = roots; _session = session; _removals = removals;
        }

        internal CodeModelGenerator Generator => _generator;
        internal OutputLibrary Output => _output;
        internal ImmutableArray<TypeProvider> Roots => _roots;

        internal static PreparedPocPostMapAttributeBoundary? TryCapture(CodeModelGenerator generator,
            OutputLibrary? output, IReadOnlyList<TypeProvider>? roots, ProviderReferenceMapSession? session,
            ImmutableHashSet<string>? removals, PocWorkBudget budget)
        {
            if (output is null || roots is null || session is null || removals is null ||
                !session.IsObservedReferenceMapRootCurrent(output, roots) ||
                !ContractCollections.TryCopyForPoc(roots, budget, out var elements)) { return null; }
            var result = new PreparedPocPostMapAttributeBoundary(generator, output, roots, elements, session, removals);
            return result.IsCurrent(budget) ? result : null;
        }

        internal bool IsCurrent(PocWorkBudget budget)
        {
            if (_stale) { return false; }
            try
            {
                if (!ReferenceEquals(CodeModelGenerator.Instance, _generator) ||
                    !_session.IsObservedReferenceMapRootCurrent(_output, _rootCarrier) ||
                    !ContractCollections.SameReferencesForPoc(_rootCarrier, _roots, budget) ||
                    !budget.TrySpend(_removals.Count)) { _stale = true; return false; }
                long characters = 0;
                foreach (var name in _removals)
                {
                    if (name is null || !budget.TrySpend(name.Length)) { _stale = true; return false; }
                    characters += name.Length;
                }
                // The session compares every snapshot name with a native
                // HashSet.Contains. Ordinal keys do not certify its bucket
                // topology: fund full possible chains AND equal-hash string
                // comparisons before the first membership probe.
                long collisionWork = (long)_removals.Count * (_removals.Count + characters);
                if (collisionWork > int.MaxValue || !budget.TrySpend((int)collisionWork) ||
                    !_session.IsObservedRemovalCandidatesCurrent(_removals)) { _stale = true; return false; }
                return true;
            }
            catch (InvalidOperationException) { _stale = true; return false; }
        }

        internal bool TrySpendNativeWriteLookup(CSharpType type, PocWorkBudget budget)
        {
            if (_stale || type is null || type.GetType() != typeof(CSharpType) || type.DeclaringType is not null ||
                type.Name is null || type.Namespace is null) { return false; }
            // Supported roots have no declaring type. Allow the namespace dot,
            // generic-arity marker and even the maximum int arity's digits.
            // One hash pass plus every possible chain/string-equality probe is
            // charged per occurrence, including future native ShouldWrite calls.
            long length = (long)type.Name.Length + type.Namespace.Length + 16;
            long work = (length + 1) * (_removals.Count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        internal bool TryShouldWrite(TypeProvider provider, CSharpType type, PocWorkBudget budget, out bool shouldWrite)
        {
            shouldWrite = false;
            if (!ReferenceEquals(provider.CachedType, type) || !TrySpendNativeWriteLookup(type, budget)) { return false; }
            shouldWrite = _session.ShouldWriteProvider(provider);
            return true;
        }
    }
}

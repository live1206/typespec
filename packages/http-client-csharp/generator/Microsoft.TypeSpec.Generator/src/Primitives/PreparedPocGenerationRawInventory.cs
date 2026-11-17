// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>
    /// Bounded observations over the already-planned physical provider stream.
    /// This is a raw-token veto inventory, not declaration confinement, a
    /// grammar proof, a binder, or authority to shorten a name.
    /// </summary>
    internal sealed class PreparedPocGenerationRawInventory
    {
        private readonly Dictionary<TypeProvider, FileObservation> _files = new(ReferenceEqualityComparer.Instance);
        private readonly int _writtenFileCount;

        private PreparedPocGenerationRawInventory(int writtenFileCount) { _writtenFileCount = writtenFileCount; }

        internal static PreparedPocGenerationRawInventory Capture(ImmutableArray<TypeProvider> expected,
            IReadOnlyDictionary<TypeProvider, PreparedSourceFile> sources, PocWorkBudget? budget = null)
        {
            var inventory = new PreparedPocGenerationRawInventory(expected.IsDefault ? 0 : expected.Length);
            if (expected.IsDefault || expected.Length > 4096 || sources is not Dictionary<TypeProvider, PreparedSourceFile> knownSources ||
                knownSources.GetType() != typeof(Dictionary<TypeProvider, PreparedSourceFile>) ||
                !ReferenceEquals(knownSources.Comparer, ReferenceEqualityComparer.Instance)) { return inventory; }
            budget ??= new PocWorkBudget(262144);
            var seen = new HashSet<TypeProvider>(ReferenceEqualityComparer.Instance);
            foreach (var owner in expected)
            {
                // Unknown, duplicate, missing or mismatched providers invalidate
                // the entire partial inventory; no positive file fact survives.
                if (owner is null || !budget.TrySpendReferenceLookup(seen.Count) || !seen.Add(owner) ||
                    !budget.TrySpendReferenceLookup(knownSources.Count) || !knownSources.TryGetValue(owner, out var file) ||
                    file is null || !ReferenceEquals(file.FileOwner, owner) ||
                    !budget.TrySpendReferenceLookup(inventory._files.Count) || !budget.TrySpend())
                { inventory._files.Clear(); return inventory; }
                inventory._files.Add(owner, new FileObservation(file));
            }
            foreach (var owner in expected)
            {
                if (!budget.TrySpendReferenceLookup(inventory._files.Count) || !inventory._files[owner].TryCapture(budget))
                { inventory._files.Clear(); break; }
            }
            return inventory;
        }

        internal int WrittenFileCount => _writtenFileCount;
        // Header emission and grammar composition are not covered yet, even if
        // every visible raw token in the shared graph happens to be known.
        internal bool HasUncoveredEmission => true;
        internal FileObservation? GetFile(TypeProvider owner) => owner is not null && _files.TryGetValue(owner, out var file) ? file : null;

        internal sealed class FileObservation(PreparedSourceFile file)
        {
            private bool _captured, _unsupportedVisibleRawName, _unsupportedVisibleRawFragment;
            private readonly List<PreparedPocFieldDeclaration> _safeFields = [];
            private readonly List<ParameterProvider> _safeParameters = [];
            private readonly List<PropertyProvider> _safeProperties = [];
            private int _stale;

            internal bool TryCapture(PocWorkBudget budget)
            {
                if (_captured) { return false; }
                foreach (var root in file.Roots)
                {
                    foreach (var occurrence in root.DescendantsAndSelf())
                    {
                        if (!budget.TrySpend()) { return false; }
                        if (occurrence.IsOpaque) { continue; }
                        var producer = occurrence.Producer;
                        var name = producer is InvokeMethodExpression or MemberExpression or MethodSignatureBase or AttributeStatement or FieldProvider or ParameterProvider or PropertyProvider or DeclareLocalFunctionStatement;
                        if (!name && producer is not (FormattableStringExpression or IfElsePreprocessorStatement or BinaryOperatorExpression or UnaryOperatorExpression or KeywordExpression)) { continue; }
                        var shape = StructuredSourceShape.CaptureForPocSupplement(producer, file.DisableXmlDocs, budget);
                        if (shape.Opaque || !shape.IntrinsicTokens)
                        {
                            if (name) { _unsupportedVisibleRawName = true; }
                            else { _unsupportedVisibleRawFragment = true; }
                        }
                        else if (producer is FieldProvider field)
                        {
                            var declaration = PreparedPocFieldDeclaration.TryCapture(field, budget);
                            if (declaration is null) { _unsupportedVisibleRawName = true; }
                            else { _safeFields.Add(declaration); }
                        }
                        else if (producer is ParameterProvider parameter) { _safeParameters.Add(parameter); }
                        else if (producer is PropertyProvider property) { _safeProperties.Add(property); }
                        if (budget.Exhausted) { return false; }
                    }
                }
                _captured = true;
                return true;
            }

            // Opaque ancestors and header/body grammar are deliberately not
            // represented by this narrow visible-name result.
            private bool IsCurrent()
            {
                if (!_captured || Volatile.Read(ref _stale) != 0) { return false; }
                if (file.IsShapeCurrentWithoutInvalidating())
                {
                    var budget = new PocWorkBudget(65536);
                    foreach (var field in _safeFields)
                    {
                        if (!field.IsCurrent(budget)) { Interlocked.Exchange(ref _stale, 1); return false; }
                    }
                    foreach (var parameter in _safeParameters)
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplementCurrent(parameter, file.DisableXmlDocs, budget);
                        if (shape.Opaque || !shape.IntrinsicTokens || budget.Exhausted)
                        { Interlocked.Exchange(ref _stale, 1); return false; }
                    }
                    foreach (var property in _safeProperties)
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplementCurrent(property, file.DisableXmlDocs, budget);
                        if (shape.Opaque || !shape.IntrinsicTokens || budget.Exhausted)
                        { Interlocked.Exchange(ref _stale, 1); return false; }
                    }
                    return true;
                }
                Interlocked.Exchange(ref _stale, 1);
                return false;
            }

            internal bool HasUnsupportedVisibleRawName => !IsCurrent() || _unsupportedVisibleRawName;
            internal bool HasUnsupportedVisibleRawFragment => !IsCurrent() || _unsupportedVisibleRawFragment;
        }
    }
}

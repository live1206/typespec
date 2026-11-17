// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator
{
    internal sealed class ProviderReferenceMapSession : IDisposable
    {
        private bool _disposed;

        public bool ShouldWriteProvider(TypeProvider provider) => ProviderReferenceMapAnalyzer.ShouldWriteProvider(provider);

        // The default write decision, using only the actual retained map and
        // cached type data. Unknown names/comparers fail closed without invoking
        // a provider, type formatter, or user equality comparer.
        internal bool TryGetCachedWriteDecision(CSharpType? type, out bool written)
        {
            written = false;
            if (_disposed || !TryGetCachedTypeName(type, 0, out var name)) { return false; }
            var removed = ProviderReferenceMapAnalyzer.LatestResult?.RemoveCandidates;
            if (removed is null) { written = true; return true; }
            if (!ReferenceEquals(removed.Comparer, StringComparer.Ordinal) &&
                !ReferenceEquals(removed.Comparer, EqualityComparer<string>.Default)) { return false; }
            written = !removed.Contains(name);
            return true;
        }

        private static bool TryGetCachedTypeName(CSharpType? type, int depth, out string name)
        {
            name = string.Empty;
            if (type is null || depth >= 32 || !ContractCollections.TryCopy(type.Arguments, out var arguments)) { return false; }
            var ownName = arguments.Length > 0 && !type.Name.Contains('`', StringComparison.Ordinal)
                ? $"{type.Name}`{arguments.Length}" : type.Name;
            if (type.DeclaringType is { } declaring)
            {
                if (!TryGetCachedTypeName(declaring, depth + 1, out var parent)) { return false; }
                name = $"{parent}.{ownName}";
            }
            else { name = string.IsNullOrEmpty(type.Namespace) ? ownName : $"{type.Namespace}.{ownName}"; }
            return true;
        }

        public void RestorePreWriteModelFactoryMethods() => ProviderReferenceMapAnalyzer.RestorePreWriteModelFactoryMethods();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
            _disposed = true;
        }
    }
}

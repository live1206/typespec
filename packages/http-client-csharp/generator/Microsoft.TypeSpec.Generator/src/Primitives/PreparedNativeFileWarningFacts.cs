// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Descriptive native-producer facts, not an edit permission or another lifetime.
    // In particular, capture never invokes DisabledFileWarnings or its builder.
    internal sealed class PreparedNativeFileWarningFacts
    {
        private readonly TypeProvider _owner;
        private IReadOnlyList<SuppressionStatement>? _root;
        private PreparedSuppressionEnvelope? _envelope;
        private bool _empty;
        private int _stale;

        private PreparedNativeFileWarningFacts(TypeProvider owner, IReadOnlyList<SuppressionStatement>? root,
            PreparedSuppressionEnvelope? envelope, bool empty)
        {
            _owner = owner;
            _root = root;
            _envelope = envelope;
            _empty = empty;
        }

        internal static PreparedNativeFileWarningFacts? TryCapture(TypeProvider owner)
        {
            var root = owner.CachedDisabledFileWarnings;
            var envelope = root is null ? null : PreparedSuppressionEnvelope.TryCapture(root);
            if (root is not null && envelope is null) { return null; }
            // With neither input source, the statically audited default builder
            // and GetTypeSuppressions(null) each produce no warnings. Do not run
            // either producer to discover this or warm the cache for a consumer.
            if (root is null && (owner.HasFileWarningInput ||
                owner.GetType().GetMethod("BuildDisabledFileWarnings", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null)?.DeclaringType != typeof(TypeProvider))) { return null; }
            bool empty = root is null || ContractCollections.TryCopy(root, out var values) && values.IsEmpty;
            return new(owner, root, envelope, empty);
        }

        internal bool IsCurrent()
        {
            if (System.Threading.Volatile.Read(ref _stale) != 0) { return false; }
            var current = _owner.CachedDisabledFileWarnings;
            bool valid;
            if (current is null)
            {
                valid = _root is null && !_owner.HasFileWarningInput;
            }
            else
            {
                // Observe actual canonical native materialization only. No getter
                // or builder invocation, and no fabricated empty carrier.
                if (_root is null)
                {
                    _root = current;
                    _envelope = PreparedSuppressionEnvelope.TryCapture(current);
                    _empty = ContractCollections.TryCopy(current, out var values) && values.IsEmpty;
                }
                valid = ReferenceEquals(current, _root) && _envelope?.IsCurrent == true;
            }
            if (!valid) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return valid;
        }

        internal bool IsCurrentAndEmptyFor(TypeProvider owner) => ReferenceEquals(owner, _owner) && IsCurrent() && _empty;
    }
}

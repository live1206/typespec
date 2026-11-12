// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Snippets;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Non-authorizing provenance for arguments constructed at audited built-in
    // sites. The caller must supply the same already-evaluated elements used to
    // construct the carrier; this class never enumerates the carrier itself.
    internal static class ConstructedArgumentWitness
    {
        internal const int MaximumArguments = ConstructedArgumentBridge.MaximumArguments;
        private sealed record Entry(IReadOnlyList<ValueExpression> Carrier, long Revision, ImmutableArray<ValueExpression> Arguments);
        private static readonly ConditionalWeakTable<InvokeMethodExpression, Entry> _entries = new();
        private sealed record WrapperToken(bool IsAudited);
        private static readonly ConditionalWeakTable<System.Type, WrapperToken> _wrappers = new();

        internal static void InstallBridge() => ConstructedArgumentBridge.Install(typeof(InvokeMethodExpression), new(
            static () => CodeModelGenerator.ObserveConstructedArguments,
            static (invocation, arguments) =>
            {
                // A bridge endpoint witnesses only this core's calls. Leave
                // mismatched arguments unavailable instead of casting.
                if (invocation is InvokeMethodExpression call && arguments is ValueExpression[] values)
                { Record(call, values); }
            },
            static type => _wrappers.GetValue(type, AuditWrapperType)));

        internal static bool IsAuditedWrapper(ScopedApi wrapper) =>
            _wrappers.TryGetValue(wrapper.GetType(), out var token) && token.IsAudited;

        private static WrapperToken AuditWrapperType(System.Type type)
        {
            if (!typeof(ScopedApi).IsAssignableFrom(type) || type == typeof(ScopedApi)) { return new(false); }
            for (var current = type; current != typeof(ScopedApi); current = current.BaseType!)
            {
                if (current is null || current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Any(method => method.Name is "WriteCore" or "IsEmptyExpression")) { return new(false); }
            }
            return new(true);
        }

        internal static void Record(InvokeMethodExpression invocation, ValueExpression[] ownedArguments)
        {
            if (ownedArguments.Length > MaximumArguments) { return; }
            var entry = new Entry(invocation.Arguments, invocation.DeclarationRevision, ImmutableArray.CreateRange(ownedArguments));
            // Never replace an earlier record, even if a caller accidentally
            // tries to re-register an invocation after it has changed.
            _entries.GetValue(invocation, _ => entry);
        }

        internal static ImmutableArray<ValueExpression>? Read(InvokeMethodExpression invocation)
        {
            if (!_entries.TryGetValue(invocation, out var entry) ||
                !ReferenceEquals(invocation.Arguments, entry.Carrier) || invocation.DeclarationRevision != entry.Revision)
            { return null; }
            return entry.Arguments;
        }
    }
}

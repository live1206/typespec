// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Construction provenance only. A copy from this sidecar does NOT establish
    // the carrier's current elements. Actual occurrence-owned consumption and
    // independent currentness are still required before any name permission.
    internal static class ConstructedCollectionWitness
    {
        private sealed class Entry(Type elementType, ImmutableArray<object?> elements)
        {
            internal Type ElementType { get; } = elementType;
            internal ImmutableArray<object?> Elements { get; } = elements;
            internal int Invalid;
        }
        private static readonly ConditionalWeakTable<object, Entry> _entries = new();

        internal static void InstallBridge()
        {
            var endpoint = new ConstructedCollectionBridge.Endpoint(static () => CodeModelGenerator.ObserveConstructedArguments, Record);
            foreach (var type in new[] { typeof(ValueExpression), typeof(WhereExpression), typeof(CSharpType),
                typeof(AttributeStatement), typeof(MethodBodyStatement), typeof(ParameterProvider) })
            { ConstructedCollectionBridge.Install(type, endpoint); }
        }

        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first) where T : class =>
            ConstructedCollectionBridge.Record(carrier, first);
        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second) where T : class =>
            ConstructedCollectionBridge.Record(carrier, first, second);
        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second, T third) where T : class =>
            ConstructedCollectionBridge.Record(carrier, first, second, third);
        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second, T third, T fourth) where T : class =>
            ConstructedCollectionBridge.Record(carrier, first, second, third, fourth);
        internal static IReadOnlyList<T> RecordOwned<T>(IReadOnlyList<T> carrier, T[] elements) where T : class =>
            ConstructedCollectionBridge.RecordOwned(carrier, elements);
        internal static IReadOnlyList<T> RecordOwned<T>(IReadOnlyList<T> carrier, List<T> elements) where T : class =>
            ConstructedCollectionBridge.RecordOwned(carrier, elements);

        private static void Record(object carrier, Type elementType, object?[] elements)
        {
            if (elements.Length > 4096) { return; }
            var proposed = new Entry(elementType, ImmutableArray.CreateRange(elements));
            var entry = _entries.GetValue(carrier, _ => proposed);
            if (entry.ElementType != elementType || entry.Elements.Length != elements.Length)
            { Interlocked.Exchange(ref entry.Invalid, 1); return; }
            for (var index = 0; index < elements.Length; index++)
            {
                if (!ReferenceEquals(entry.Elements[index], elements[index]))
                { Interlocked.Exchange(ref entry.Invalid, 1); return; }
            }
        }

        internal static bool TryCopyConstruction<T>(object? carrier, PocWorkBudget budget, out ImmutableArray<T> elements)
        {
            elements = [];
            if (carrier is null || !_entries.TryGetValue(carrier, out var entry) || Volatile.Read(ref entry.Invalid) != 0 ||
                !typeof(T).IsAssignableFrom(entry.ElementType) || !budget.TrySpend(entry.Elements.Length)) { return false; }
            var builder = ImmutableArray.CreateBuilder<T>(entry.Elements.Length);
            foreach (var value in entry.Elements)
            {
                if (value is not T typed) { return false; }
                builder.Add(typed);
            }
            elements = builder.MoveToImmutable();
            return Volatile.Read(ref entry.Invalid) == 0;
        }
    }
}

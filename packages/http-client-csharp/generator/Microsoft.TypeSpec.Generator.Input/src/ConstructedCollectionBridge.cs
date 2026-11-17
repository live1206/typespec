// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.TypeSpec.Generator.Input
{
    // Only audited construction sites may supply the already-evaluated elements
    // used to build this exact carrier. Never query the carrier. The caller keeps
    // its original representation and evaluation point; this is not currentness.
    // Invalid null native inputs pass through without an earlier bridge exception.
    internal static class ConstructedCollectionBridge
    {
        internal sealed record Endpoint(Func<bool> Enabled, Action<object, Type, object?[]> Record);
        private static readonly ConditionalWeakTable<Type, Endpoint> _endpoints = new();
        internal static void Install(Type elementType, Endpoint endpoint) => _endpoints.GetValue(elementType, _ => endpoint);
        internal static bool Enabled<T>() where T : class => _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled();

        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first) where T : class
        {
            if (carrier is not null && _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            { endpoint.Record(carrier, typeof(T), [first]); }
            return carrier!;
        }

        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second) where T : class
        {
            if (carrier is not null && _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            { endpoint.Record(carrier, typeof(T), [first, second]); }
            return carrier!;
        }

        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second, T third) where T : class
        {
            if (carrier is not null && _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            { endpoint.Record(carrier, typeof(T), [first, second, third]); }
            return carrier!;
        }

        internal static IReadOnlyList<T> Record<T>(IReadOnlyList<T> carrier, T first, T second, T third, T fourth) where T : class
        {
            if (carrier is not null && _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            { endpoint.Record(carrier, typeof(T), [first, second, third, fourth]); }
            return carrier!;
        }

        // The array/list is locally owned construction data, not an arbitrary
        // source enumerable. No copying or endpoint callback when disabled.
        internal static IReadOnlyList<T> RecordOwned<T>(IReadOnlyList<T> carrier, T[] elements) where T : class
        {
            if (carrier is not null && elements is not null && elements.Length <= 4096 && _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            {
                var snapshot = new object?[elements.Length];
                Array.Copy(elements, snapshot, elements.Length);
                endpoint.Record(carrier, typeof(T), snapshot);
            }
            return carrier!;
        }

        internal static IReadOnlyList<T> RecordOwned<T>(IReadOnlyList<T> carrier, List<T> elements) where T : class
        {
            if (carrier is not null && elements is not null && elements.GetType() == typeof(List<T>) && elements.Count <= 4096 &&
                _endpoints.TryGetValue(typeof(T), out var endpoint) && endpoint.Enabled())
            {
                var snapshot = new object?[elements.Count];
                for (var index = 0; index < snapshot.Length; index++) { snapshot[index] = elements[index]; }
                endpoint.Record(carrier, typeof(T), snapshot);
            }
            return carrier!;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.TypeSpec.Generator.ClientModel.Utilities
{
    // Native, already-occurring observations, not reflection probes or recipe
    // permission. The context's NAME predicate and the helper's IDENTITY
    // predicate deliberately have different tables/results (including for
    // structs/enums, which only the helper skips).
    internal static class PocContextInterfaceObservations
    {
        private const int MaxTypes = 4096;
        private const int MaxCost = 65536;
        private const int NativeBucketCount = 64;
        private static readonly Type s_runtimeType = typeof(object).GetType();
        internal sealed class NativeObservation(Type type, int cost, bool result)
        {
            internal readonly Type Type = type;
            internal readonly int Cost = cost;
            internal readonly bool Result = result;
            internal NativeObservation? Next;
        }

        internal sealed class Table
        {
            internal readonly object Sync = new();
            internal readonly Dictionary<Type, (int Cost, bool Result)> Direct = new(ReferenceEqualityComparer.Instance);
            internal readonly Dictionary<Type, (int Cost, bool Result)> Helper = new(ReferenceEqualityComparer.Instance);
            internal readonly NativeObservation?[] DirectNativeHead = new NativeObservation?[NativeBucketCount];
            internal readonly NativeObservation?[] DirectNativeTail = new NativeObservation?[NativeBucketCount];
            internal readonly NativeObservation?[] HelperNativeHead = new NativeObservation?[NativeBucketCount];
            internal readonly NativeObservation?[] HelperNativeTail = new NativeObservation?[NativeBucketCount];
            internal bool Invalid;
        }

        internal static void Record(Type type, int count, bool result) => Record(type, count, result, direct: false);
        internal static void RecordDirect(Type type, int count, long nameCharacters, bool result) =>
            Record(type, nameCharacters > MaxCost ? MaxCost + 1 : count + (int)nameCharacters, result, direct: true);

        private static void Record(Type type, int cost, bool result, bool direct)
        {
            if (type.GetType() != s_runtimeType || !ScmCodeModelGenerator.IsCurrentInstance) { return; }
            var generator = ScmCodeModelGenerator.Instance;
            var table = Volatile.Read(ref generator.PocContextInterfaceObservations);
            if (table is null)
            {
                var candidate = new Table();
                table = Interlocked.CompareExchange(ref generator.PocContextInterfaceObservations, candidate, null) ?? candidate;
            }
            lock (table.Sync)
            {
                if (table.Invalid) { return; }
                var values = direct ? table.Direct : table.Helper;
                if (cost < 0 || cost > MaxCost) { table.Invalid = true; return; }
                // A preflight funds a later native recheck using the current
                // count. Fixed identity buckets append new observations after
                // existing ones, so growth cannot lengthen an existing prefix.
                var heads = direct ? table.DirectNativeHead : table.HelperNativeHead;
                var tails = direct ? table.DirectNativeTail : table.HelperNativeTail;
                int bucket = RuntimeHelpers.GetHashCode(type) & (NativeBucketCount - 1);
                for (var observation = heads[bucket]; observation is not null; observation = observation.Next)
                {
                    if (!ReferenceEquals(observation.Type, type)) { continue; }
                    if (observation.Cost != cost || observation.Result != result) { table.Invalid = true; }
                    return;
                }
                if (table.Direct.Count + table.Helper.Count >= MaxTypes) { table.Invalid = true; return; }
                // Allocate before mutating the dictionary: a failed allocation
                // cannot leave an observed key absent from the native index.
                var added = new NativeObservation(type, cost, result);
                values.Add(type, (cost, result));
                var tail = tails[bucket];
                if (tail is null) { heads[bucket] = added; }
                else { tail.Next = added; }
                tails[bucket] = added;
            }
        }

        // Compatibility for the existing negative-observation controls. The
        // caller's generator must be the current observation owner.
        internal static bool TrySpendNegative(CodeModelGenerator generator, Type type, Func<int, bool> spend) =>
            ScmCodeModelGenerator.IsCurrentInstance && ReferenceEquals(generator, ScmCodeModelGenerator.Instance) &&
            TrySpend(type, spend, direct: false, reserveNativeRecord: false);
        internal static bool TrySpendDirectNegative(Type type, Func<int, bool> spend) => TrySpend(type, spend, direct: true, reserveNativeRecord: true);
        internal static bool TrySpendHelperNegative(Type type, Func<int, bool> spend) => TrySpend(type, spend, direct: false, reserveNativeRecord: true);
        private static bool TrySpend(Type type, Func<int, bool> spend, bool direct, bool reserveNativeRecord)
        {
            // No virtual Type metadata before the exact immutable runtime guard.
            if (type.GetType() != s_runtimeType || !ScmCodeModelGenerator.IsCurrentInstance || !spend(1)) { return false; }
            if (!direct && (type.IsEnum || type.IsValueType)) { return true; }
            var table = Volatile.Read(ref ScmCodeModelGenerator.Instance.PocContextInterfaceObservations);
            if (table is null) { return false; }
            lock (table.Sync)
            {
                var values = direct ? table.Direct : table.Helper;
                // The initial unit covers one inspected key (or an empty
                // table); prepay the rest of the possible collision chain.
                // Canonical-builder callers also reserve the current count
                // plus one for its later append-only native Record lookup
                // BEFORE native reflection. Later additions cannot increase
                // the observed entry's prefix. The compatibility observer is
                // a point read only, not a future-build reservation.
                return !table.Invalid && spend(Math.Max(0, values.Count - 1)) &&
                    (!reserveNativeRecord || spend(values.Count + 1)) &&
                    values.TryGetValue(type, out var observation) &&
                    !observation.Result && spend(observation.Cost);
            }
        }
    }
}

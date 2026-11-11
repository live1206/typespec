// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.TypeSpec.Generator.ClientModel.Utilities;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PocContextNativeRecheckBudgetTests
    {
        private sealed class NativeInterfaceProbe { }
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void LaterNativeRecheckRemainsFundedWhenOtherObservationsGrow(bool direct, bool sameNativeBucket)
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-recheck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var coreInstance = typeof(CodeModelGenerator).GetField("_instance", StaticFields)!;
            var scmInstance = typeof(ScmCodeModelGenerator).GetField("_instance", StaticFields)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), StaticFields)!;
            var oldCore = coreInstance.GetValue(null);
            var oldScm = scmInstance.GetValue(null);
            var oldPolicy = policy.GetValue(null);
            try
            {
                var configuration = Configuration.Load(directory,
                    "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = generator;
                var target = typeof(NativeInterfaceProbe);
                var directMethod = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                    .GetMethod("ImplementsIPersistableModel", StaticFields)!;
                bool Observe(Type type) => direct
                    ? (bool)directMethod.Invoke(null, new object?[] { new CSharpType(type), null })!
                    : ModelReaderWriterHelpers.ImplementsModelReaderWriter(type);
                Assert.That(target.GetInterfaces(), Is.Empty);
                Assert.That(Observe(target), Is.False);
                var ownerField = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", InstanceFields)!;
                var table = ownerField.GetValue(generator)!;
                var helper = (Dictionary<Type, (int Cost, bool Result)>)table.GetType()
                    .GetField(direct ? "Direct" : "Helper", InstanceFields)!.GetValue(table)!;
                Assert.That(helper.Count, Is.EqualTo(1));
                Assert.That(helper[target], Is.EqualTo((0, false)));

                // Test-only capacity control and actual runtime identity
                // collisions, following the original inner-collision control.
                int capacity = helper.EnsureCapacity(32);
                uint targetHash = (uint)RuntimeHelpers.GetHashCode(target);
                uint targetBucket = targetHash % (uint)capacity;
                uint targetNativeBucket = targetHash & 63;
                var assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("PocRecheckCollisions" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
                var module = assembly.DefineDynamicModule("Main");
                var later = new List<Type>();
                for (int i = 0; later.Count < 31 && i < 32768; i++)
                {
                    var type = module.DefineType("PocRecheck.T" + i, TypeAttributes.Public | TypeAttributes.Class).CreateType()!;
                    uint hash = (uint)RuntimeHelpers.GetHashCode(type);
                    if (sameNativeBucket ? (hash & 63) == targetNativeBucket : hash % (uint)capacity == targetBucket)
                    {
                        later.Add(type);
                    }
                }
                Assert.That(later.Count, Is.EqualTo(31));
                var budget = new PocWorkBudget(3);
                var debits = new List<int>();
                bool Spend(int units)
                {
                    bool success = budget.TrySpend(units);
                    if (success) { debits.Add(units); }
                    return success;
                }
                bool admitted = direct
                    ? PocContextInterfaceObservations.TrySpendDirectNegative(target, Spend)
                    : PocContextInterfaceObservations.TrySpendHelperNegative(target, Spend);
                Assert.That(admitted, Is.True,
                    "The actual first native negative must be admitted before unrelated observations grow.");
                Assert.That(budget.Remaining, Is.Zero);
                Assert.That(debits, Is.EqualTo(new[] { 1, 0, 2, 0 }));
                int futureRecordReservation = debits[2];

                Exception? error = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        foreach (var type in later)
                        {
                            if (Observe(type))
                            {
                                throw new InvalidOperationException("An emitted class unexpectedly implements a model interface.");
                            }
                        }
                    }
                    catch (Exception exception) { error = exception; }
                }) { IsBackground = true };
                worker.Start();
                try { Assert.That(worker.Join(TimeSpan.FromSeconds(10)), Is.True); }
                finally { worker.Join(); }
                Assert.That(error, Is.Null);
                Assert.That(CodeModelGenerator.Instance, Is.SameAs(generator));
                Assert.That(ownerField.GetValue(generator), Is.SameAs(table));
                Assert.That(helper.EnsureCapacity(0), Is.EqualTo(capacity));
                Assert.That(helper.Count, Is.EqualTo(32));
                Assert.That(helper[target], Is.EqualTo((0, false)));
                Assert.That((bool)table.GetType().GetField("Invalid", InstanceFields)!.GetValue(table)!, Is.False);

                int visited;
                var nativeHead = table.GetType().GetField(direct ? "DirectNativeHead" : "HelperNativeHead", InstanceFields);
                object? nativeIndex = nativeHead?.GetValue(table);
                object? nativeStart = nativeIndex is Array heads
                    ? heads.GetValue(RuntimeHelpers.GetHashCode(target) & (heads.Length - 1))
                    : nativeIndex;
                if (nativeStart is null)
                {
                    Assert.That(sameNativeBucket, Is.False, "Same-bucket controls require the stable native index.");
                    // Baseline Record searches the actual dictionary. Inspect
                    // the pinned runtime's real chain, not a timing proxy.
                    var buckets = (int[])helper.GetType().GetField("_buckets", InstanceFields)!.GetValue(helper)!;
                    var entries = (Array)helper.GetType().GetField("_entries", InstanceFields)!.GetValue(helper)!;
                    var entryType = entries.GetType().GetElementType()!;
                    var keyField = entryType.GetField("key")!;
                    var nextField = entryType.GetField("next")!;
                    int index = buckets[(int)targetBucket] - 1;
                    visited = 0;
                    bool found = false;
                    while (index >= 0)
                    {
                        visited++;
                        var entry = entries.GetValue(index)!;
                        if (ReferenceEquals(keyField.GetValue(entry), target)) { found = true; break; }
                        index = (int)nextField.GetValue(entry)!;
                    }
                    Assert.That(found, Is.True);
                    Assert.That(visited, Is.EqualTo(32), "The actual old native lookup must walk all colliding entries.");
                }
                else
                {
                    // Candidate Record searches the product-owned append-only
                    // native prefix, not the preflight dictionary.
                    object? entry = nativeStart;
                    visited = 0;
                    bool found = false;
                    while (entry is not null)
                    {
                        visited++;
                        var type = entry.GetType();
                        if (ReferenceEquals(type.GetField("Type", InstanceFields)!.GetValue(entry), target)) { found = true; break; }
                        entry = type.GetField("Next", InstanceFields)!.GetValue(entry);
                    }
                    Assert.That(found, Is.True);
                    Assert.That(visited, Is.EqualTo(1), "Later observations must not move an existing native entry.");
                    if (sameNativeBucket)
                    {
                        int bucketEntries = 0;
                        entry = nativeStart;
                        while (entry is not null)
                        {
                            bucketEntries++;
                            entry = entry.GetType().GetField("Next", InstanceFields)!.GetValue(entry);
                        }
                        Assert.That(bucketEntries, Is.EqualTo(32), "All real later native observations must share this fixed bucket.");
                    }
                }
                Assert.That(Observe(target), Is.False,
                    "The later operation must be the actual native predicate and duplicate record.");
                Assert.That(helper[target], Is.EqualTo((0, false)));
                Assert.That((bool)table.GetType().GetField("Invalid", InstanceFields)!.GetValue(table)!, Is.False);
                Assert.That(futureRecordReservation, Is.GreaterThanOrEqualTo(visited),
                    "The preflight's independent native-record debit must cover the later actual search after growth.");
            }
            finally
            {
                coreInstance.SetValue(null, oldCore);
                scmInstance.SetValue(null, oldScm);
                policy.SetValue(null, oldPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

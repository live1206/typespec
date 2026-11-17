// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.TypeSpec.Generator.ClientModel.Utilities;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PocContextInterfaceRegistryBudgetTests
    {
        private sealed class NativeInterfaceProbe { }
        private sealed class GenericNativeInterfaceProbe<T> { }
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;

        private static bool DrainWorkers(IEnumerable<Thread> threads, TimeSpan timeout)
        {
            bool timely = true;
            foreach (var thread in threads)
            {
                if (thread.Join(timeout)) { continue; }
                timely = false;
                // The enclosing test process has a timeout. Do not restore its
                // shared generator state while a started worker can still use it.
                thread.Join();
            }
            return timely;
        }

        [Test]
        public void TimedWorkerCleanupDoesNotRestoreWhileThreadIsRunning()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var worker = new Thread(() => { started.Set(); release.Wait(); }) { IsBackground = true };
            worker.Start();
            try
            {
                Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True);
                using var timer = new Timer(_ => release.Set(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
                DrainWorkers([worker], TimeSpan.FromMilliseconds(10));
                Assert.That(worker.IsAlive, Is.False,
                    "A timed-out worker must finish before the fixture restores generator globals.");
            }
            finally
            {
                release.Set();
                worker.Join();
            }
        }

        [Test]
        public void ConcurrentNativeObservationsPublishOneOwnerTable()
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-publication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var coreInstance = typeof(CodeModelGenerator).GetField("_instance", StaticFields)!;
            var scmInstance = typeof(ScmCodeModelGenerator).GetField("_instance", StaticFields)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), StaticFields)!;
            var oldCore = coreInstance.GetValue(null);
            var oldScm = scmInstance.GetValue(null);
            var oldPolicy = policy.GetValue(null);
            const int workers = 12;
            var threads = new List<Thread>();
            using var ready = new CountdownEvent(workers);
            using var start = new ManualResetEventSlim();
            var errors = new Exception?[workers];
            var results = new object?[workers];
            try
            {
                var configuration = Configuration.Load(directory,
                    "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = generator;
                var ownerField = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", InstanceFields)!;
                Assert.That(ownerField.GetValue(generator), Is.Null);
                for (int i = 0; i < workers; i++)
                {
                    var index = i;
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            ready.Signal();
                            start.Wait();
                            if (ModelReaderWriterHelpers.ImplementsModelReaderWriter(typeof(NativeInterfaceProbe)))
                            {
                                throw new InvalidOperationException("The native probe unexpectedly implements a model interface.");
                            }
                            results[index] = ownerField.GetValue(generator);
                        }
                        catch (Exception exception) { errors[index] = exception; }
                    }) { IsBackground = true };
                    thread.Start();
                    threads.Add(thread);
                }
                Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True);
                start.Set();
                foreach (var thread in threads) { Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True); }
                Assert.That(errors, Has.All.Null);
                var table = ownerField.GetValue(generator);
                Assert.That(table, Is.Not.Null);
                Assert.That(Array.TrueForAll(results, result => ReferenceEquals(result, table)), Is.True,
                    "Racing native observations must all use the one published generator-owned table.");
                var budget = new PocWorkBudget(3);
                Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(typeof(NativeInterfaceProbe), budget.TrySpend), Is.True);
                Assert.That(budget.Remaining, Is.Zero);
            }
            finally
            {
                start.Set();
                DrainWorkers(threads, TimeSpan.FromSeconds(10));
                coreInstance.SetValue(null, oldCore);
                scmInstance.SetValue(null, oldScm);
                policy.SetValue(null, oldPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void ConcurrentDistinctNativeObservationsRetainEveryResult()
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-distinct-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var coreInstance = typeof(CodeModelGenerator).GetField("_instance", StaticFields)!;
            var scmInstance = typeof(ScmCodeModelGenerator).GetField("_instance", StaticFields)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), StaticFields)!;
            var oldCore = coreInstance.GetValue(null);
            var oldScm = scmInstance.GetValue(null);
            var oldPolicy = policy.GetValue(null);
            Type[] types =
            [
                typeof(GenericNativeInterfaceProbe<int>), typeof(GenericNativeInterfaceProbe<long>),
                typeof(GenericNativeInterfaceProbe<byte>), typeof(GenericNativeInterfaceProbe<short>),
                typeof(GenericNativeInterfaceProbe<uint>), typeof(GenericNativeInterfaceProbe<ulong>),
                typeof(GenericNativeInterfaceProbe<ushort>), typeof(GenericNativeInterfaceProbe<char>),
                typeof(GenericNativeInterfaceProbe<float>), typeof(GenericNativeInterfaceProbe<double>),
                typeof(GenericNativeInterfaceProbe<decimal>), typeof(GenericNativeInterfaceProbe<string>)
            ];
            var threads = new List<Thread>();
            using var ready = new CountdownEvent(types.Length);
            using var start = new ManualResetEventSlim();
            var errors = new Exception?[types.Length];
            try
            {
                var configuration = Configuration.Load(directory,
                    "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = generator;
                var ownerField = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", InstanceFields)!;
                Assert.That(ownerField.GetValue(generator), Is.Null);
                for (int i = 0; i < types.Length; i++)
                {
                    var index = i;
                    Assert.That(types[index].GetInterfaces(), Is.Empty);
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            ready.Signal();
                            start.Wait();
                            if (ModelReaderWriterHelpers.ImplementsModelReaderWriter(types[index]))
                            {
                                throw new InvalidOperationException("The native probe unexpectedly implements a model interface.");
                            }
                        }
                        catch (Exception exception) { errors[index] = exception; }
                    }) { IsBackground = true };
                    thread.Start();
                    threads.Add(thread);
                }
                Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True);
                start.Set();
                Assert.That(DrainWorkers(threads, TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(errors, Has.All.Null);
                var table = ownerField.GetValue(generator);
                Assert.That(table, Is.Not.Null);
                var entries = (Dictionary<Type, (int Cost, bool Result)>)table!.GetType()
                    .GetField("Helper", InstanceFields)!.GetValue(table)!;
                Assert.That(entries.Count, Is.EqualTo(types.Length));
                foreach (var type in types)
                {
                    Assert.That(entries[type], Is.EqualTo((0, false)));
                    var budget = new PocWorkBudget(types.Length);
                    Assert.That(PocContextInterfaceObservations.TrySpendNegative(generator, type, budget.TrySpend), Is.True);
                    Assert.That(budget.Remaining, Is.Zero);
                }
                var observed = new HashSet<Type>(ReferenceEqualityComparer.Instance);
                void CheckChain(object? node)
                {
                    while (node is not null)
                    {
                        var nodeType = node.GetType();
                        Assert.That(observed.Add((Type)nodeType.GetField("Type", InstanceFields)!.GetValue(node)!), Is.True);
                        node = nodeType.GetField("Next", InstanceFields)!.GetValue(node);
                    }
                }
                object? heads = table.GetType().GetField("HelperNativeHead", InstanceFields)!.GetValue(table);
                if (heads is Array buckets)
                {
                    foreach (var head in buckets) { CheckChain(head); }
                }
                else { CheckChain(heads); }
                Assert.That(observed.SetEquals(types), Is.True, "Every native result must occur exactly once in the stable recheck index.");
            }
            finally
            {
                start.Set();
                DrainWorkers(threads, TimeSpan.FromSeconds(10));
                coreInstance.SetValue(null, oldCore);
                scmInstance.SetValue(null, oldScm);
                policy.SetValue(null, oldPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void NativeObservationRegistryLookupIsFundedForItsActualOwner()
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-observation-" + Guid.NewGuid().ToString("N"));
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
                var context = new GeneratorContext(configuration);
                var target = new ScmCodeModelGenerator(context);
                Assert.That(typeof(NativeInterfaceProbe).GetInterfaces(), Is.Empty);

                // Singleton manipulation here is test scheduling, as in the Foo
                // fixture. Every actual entry below is produced by the native
                // helper, not inserted into or synthesized in the registry.
                void SetCurrent(ScmCodeModelGenerator generator)
                {
                    scmInstance.SetValue(null, generator);
                    CodeModelGenerator.Instance = generator;
                }

                var registryField = typeof(PocContextInterfaceObservations).GetField("s_tables", StaticFields);
                if (registryField is null)
                {
                    // A local owner field has no process-wide bucket to walk.
                    // Check both native publication and isolation rather than
                    // silently skipping the regression after the repair.
                    var ownerField = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", InstanceFields);
                    Assert.That(ownerField, Is.Not.Null, "A noncreating observation must be owned by the exact generator.");
                    SetCurrent(target);
                    var cold = new PocWorkBudget(3);
                    Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(typeof(NativeInterfaceProbe), cold.TrySpend), Is.False);
                    Assert.That(cold.Remaining, Is.EqualTo(2));
                    Assert.That(ownerField!.GetValue(target), Is.Null);
                    Assert.That(ModelReaderWriterHelpers.ImplementsModelReaderWriter(typeof(NativeInterfaceProbe)), Is.False);
                    var targetTable = ownerField.GetValue(target);
                    Assert.That(targetTable, Is.Not.Null, "The native result publishes the target's table.");
                    var other = new ScmCodeModelGenerator(context);
                    SetCurrent(other);
                    Assert.That(ownerField.GetValue(other), Is.Null);
                    Assert.That(ModelReaderWriterHelpers.ImplementsModelReaderWriter(typeof(NativeInterfaceProbe)), Is.False);
                    Assert.That(ownerField.GetValue(other), Is.Not.Null);
                    Assert.That(ownerField.GetValue(other), Is.Not.SameAs(targetTable));
                    SetCurrent(target);
                    Assert.That(ownerField.GetValue(target), Is.SameAs(targetTable));
                    var budget = new PocWorkBudget(3);
                    Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(typeof(NativeInterfaceProbe), budget.TrySpend), Is.True);
                    Assert.That(budget.Remaining, Is.Zero, "The direct owner lookup and future native record must be funded.");
                    return;
                }

                var registry = registryField.GetValue(null)!;
                var registryLock = registry.GetType().GetField("_lock", InstanceFields)!.GetValue(registry)!;
                var containerField = registry.GetType().GetField("_container", InstanceFields)!;
                object initialContainer = containerField.GetValue(registry)!;
                var bucketsField = initialContainer.GetType().GetField("_buckets", InstanceFields)!;
                int initialCapacity = ((int[])bucketsField.GetValue(initialContainer)!).Length;
                int mask = checked(initialCapacity * 2 - 1);
                int targetBucket = RuntimeHelpers.GetHashCode(target) & mask;
                var prior = new List<ScmCodeModelGenerator>();
                // Match twice the present capacity: the same identities remain
                // colliding if these few genuine inserts grow the table once.
                for (int i = 0; prior.Count < 3 && i < Math.Max(32768, initialCapacity * 128); i++)
                {
                    var generator = new ScmCodeModelGenerator(context);
                    if ((RuntimeHelpers.GetHashCode(generator) & mask) == targetBucket) { prior.Add(generator); }
                }
                Assert.That(prior.Count, Is.EqualTo(3), "Choose real generator identities for the actual runtime bucket.");

                var threads = new List<Thread>();
                var started = new List<ManualResetEventSlim>();
                var errors = new Exception?[prior.Count];
                Monitor.Enter(registryLock);
                try
                {
                    for (int i = 0; i < prior.Count; i++)
                    {
                        SetCurrent(prior[i]);
                        var index = i;
                        var ready = new ManualResetEventSlim();
                        started.Add(ready);
                        var thread = new Thread(() =>
                        {
                            try
                            {
                                ready.Set();
                                if (ModelReaderWriterHelpers.ImplementsModelReaderWriter(typeof(NativeInterfaceProbe)))
                                {
                                    throw new InvalidOperationException("The native probe unexpectedly implements a model interface.");
                                }
                            }
                            catch (Exception exception) { errors[index] = exception; }
                        }) { IsBackground = true };
                        thread.Start();
                        threads.Add(thread);
                        Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True);
                        Assert.That(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                            TimeSpan.FromSeconds(10)), Is.True, "The earlier native call must reach the held registry lock.");
                        Assert.That(thread.IsAlive, Is.True);
                    }
                    SetCurrent(target);
                    // Monitor reentrancy permits this genuine observation to
                    // insert while the earlier captured keys remain blocked.
                    Assert.That(ModelReaderWriterHelpers.ImplementsModelReaderWriter(typeof(NativeInterfaceProbe)), Is.False);
                }
                finally
                {
                    Monitor.Exit(registryLock);
                    bool timely = DrainWorkers(threads, TimeSpan.FromSeconds(10));
                    foreach (var ready in started) { ready.Dispose(); }
                    Assert.That(timely, Is.True, "All native observations must complete promptly.");
                }
                Assert.That(errors, Has.All.Null, "All earlier native observations must complete.");
                SetCurrent(target);

                // Inspect the pinned runtime's actual table, without adding or
                // changing entries, and prove that the target really follows
                // the older calls in the bucket. No stopwatch approximation.
                object container = containerField.GetValue(registry)!;
                var buckets = (int[])bucketsField.GetValue(container)!;
                Assert.That(buckets.Length, Is.LessThanOrEqualTo(initialCapacity * 2));
                var entries = (Array)container.GetType().GetField("_entries", InstanceFields)!.GetValue(container)!;
                var next = entries.GetType().GetElementType()!.GetField("Next")!;
                var tryGetEntry = container.GetType().GetMethod("TryGetEntry", InstanceFields)!;
                int cursor = buckets[(RuntimeHelpers.GetHashCode(target) & int.MaxValue) & (buckets.Length - 1)];
                int visited = 0;
                var preceding = new HashSet<object>(ReferenceEqualityComparer.Instance);
                bool found = false;
                while (cursor >= 0)
                {
                    visited++;
                    object?[] arguments = [cursor, null, null];
                    Assert.That((bool)tryGetEntry.Invoke(container, arguments)!, Is.True);
                    if (ReferenceEquals(arguments[1], target)) { found = true; break; }
                    if (arguments[1] is not null) { preceding.Add(arguments[1]!); }
                    cursor = (int)next.GetValue(entries.GetValue(cursor))!;
                }
                Assert.That(found, Is.True, "The real target observation must exist.");
                Assert.That(prior.TrueForAll(preceding.Contains), Is.True, "All three real native entries must precede the target.");
                Assert.That(visited, Is.GreaterThanOrEqualTo(4));
                var funded = new PocWorkBudget(3);
                Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(typeof(NativeInterfaceProbe), funded.TrySpend), Is.True,
                    "The actual native negative is otherwise valid and its inner record is funded.");
                Assert.That(3 - funded.Remaining, Is.GreaterThanOrEqualTo(visited),
                    "The charged work must cover even the actual outer lookup before inner or future-record work.");
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

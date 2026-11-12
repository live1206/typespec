// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.TypeSpec.Generator.ClientModel.Utilities;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PocContextObservationCapacityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MixedNativeObservationsPreserveDuplicatesAtCapacityAndRejectOverflow(bool overflowDirect)
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-capacity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            const BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
            const BindingFlags inf = BindingFlags.Instance | BindingFlags.NonPublic;
            var core = typeof(CodeModelGenerator).GetField("_instance", sf)!;
            var scm = typeof(ScmCodeModelGenerator).GetField("_instance", sf)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), sf)!;
            var oldCore = core.GetValue(null);
            var oldScm = scm.GetValue(null);
            var oldPolicy = policy.GetValue(null);
            try
            {
                var configuration = Configuration.Load(directory,
                    "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = generator;
                var directMethod = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                    .GetMethod("ImplementsIPersistableModel", sf)!;
                bool Observe(Type type, bool direct) => direct
                    ? (bool)directMethod.Invoke(null, new object?[] { new CSharpType(type), null })!
                    : ModelReaderWriterHelpers.ImplementsModelReaderWriter(type);
                var assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("PocNativeCapacity" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
                var module = assembly.DefineDynamicModule("Main");
                var types = new Type[4097];
                for (int i = 0; i < types.Length; i++)
                {
                    types[i] = module.DefineType("PocCapacity.T" + i, TypeAttributes.Public | TypeAttributes.Class).CreateType()!;
                    if (i < 4096) { Assert.That(Observe(types[i], direct: (i & 1) != 0), Is.False); }
                }
                var table = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", inf)!.GetValue(generator)!;
                var helper = (Dictionary<Type, (int Cost, bool Result)>)table.GetType().GetField("Helper", inf)!.GetValue(table)!;
                var direct = (Dictionary<Type, (int Cost, bool Result)>)table.GetType().GetField("Direct", inf)!.GetValue(table)!;
                Assert.That(helper.Count, Is.EqualTo(2048));
                Assert.That(direct.Count, Is.EqualTo(2048));
                Assert.That((bool)table.GetType().GetField("Invalid", inf)!.GetValue(table)!, Is.False);
                Assert.That(helper[types[0]], Is.EqualTo((0, false)));
                Assert.That(direct[types[1]], Is.EqualTo((0, false)));

                int CountIndex(string field, Dictionary<Type, (int Cost, bool Result)> expected)
                {
                    var found = new HashSet<Type>(ReferenceEqualityComparer.Instance);
                    var heads = (Array)table.GetType().GetField(field, inf)!.GetValue(table)!;
                    foreach (var head in heads)
                    {
                        object? node = head;
                        while (node is not null)
                        {
                            var nodeType = node.GetType();
                            var type = (Type)nodeType.GetField("Type", inf)!.GetValue(node)!;
                            Assert.That(found.Add(type), Is.True);
                            Assert.That(expected.ContainsKey(type), Is.True);
                            node = nodeType.GetField("Next", inf)!.GetValue(node);
                        }
                    }
                    Assert.That(found.Count, Is.EqualTo(expected.Count));
                    return found.Count;
                }
                Assert.That(CountIndex("HelperNativeHead", helper), Is.EqualTo(2048));
                Assert.That(CountIndex("DirectNativeHead", direct), Is.EqualTo(2048));

                // Existing native observations must remain valid even though
                // the combined table has reached its cardinality limit.
                Assert.That(Observe(types[0], direct: false), Is.False);
                Assert.That(Observe(types[1], direct: true), Is.False);
                Assert.That((bool)table.GetType().GetField("Invalid", inf)!.GetValue(table)!, Is.False);
                // These are standalone observation-query budgets, not the
                // separately enforced 4,096-unit file-envelope limit.
                var helperBudget = new PocWorkBudget(4097);
                var directBudget = new PocWorkBudget(4097);
                Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(types[0], helperBudget.TrySpend), Is.True);
                Assert.That(PocContextInterfaceObservations.TrySpendDirectNegative(types[1], directBudget.TrySpend), Is.True);
                Assert.That(helperBudget.Remaining, Is.Zero);
                Assert.That(directBudget.Remaining, Is.Zero);
                Assert.That(CountIndex("HelperNativeHead", helper), Is.EqualTo(2048));
                Assert.That(CountIndex("DirectNativeHead", direct), Is.EqualTo(2048));

                // One more distinct native observation must invalidate both
                // routes without appending or partially changing either index.
                Assert.That(Observe(types[4096], direct: overflowDirect), Is.False);
                Assert.That((bool)table.GetType().GetField("Invalid", inf)!.GetValue(table)!, Is.True);
                Assert.That(helper.Count, Is.EqualTo(2048));
                Assert.That(direct.Count, Is.EqualTo(2048));
                Assert.That(CountIndex("HelperNativeHead", helper), Is.EqualTo(2048));
                Assert.That(CountIndex("DirectNativeHead", direct), Is.EqualTo(2048));
                Assert.That(Observe(types[0], direct: false), Is.False);
                Assert.That(Observe(types[1], direct: true), Is.False);
                Assert.That(PocContextInterfaceObservations.TrySpendHelperNegative(types[0], _ => true), Is.False);
                Assert.That(PocContextInterfaceObservations.TrySpendDirectNegative(types[1], _ => true), Is.False);
            }
            finally
            {
                core.SetValue(null, oldCore);
                scm.SetValue(null, oldScm);
                policy.SetValue(null, oldPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.ClientModel.Utilities;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PocContextNativeIndexScaleTests
    {
        [Test]
        public void NativeRecheckDoesNotScanUnrelatedIdentityBuckets()
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-index-" + Guid.NewGuid().ToString("N"));
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
                var assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("PocNativeIndexScale" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
                var module = assembly.DefineDynamicModule("Main");
                var types = new Type[4096];
                for (int i = 0; i < types.Length; i++)
                {
                    types[i] = module.DefineType("PocIndex.T" + i, TypeAttributes.Public | TypeAttributes.Class).CreateType()!;
                    Assert.That(ModelReaderWriterHelpers.ImplementsModelReaderWriter(types[i]), Is.False);
                }
                var target = types[^1];
                const int bucketCount = 64;
                int targetBucket = RuntimeHelpers.GetHashCode(target) & (bucketCount - 1);
                int targetBucketCount = 0;
                foreach (var type in types)
                {
                    if ((RuntimeHelpers.GetHashCode(type) & (bucketCount - 1)) == targetBucket) { targetBucketCount++; }
                }
                Assert.That(targetBucketCount, Is.GreaterThan(0));
                Assert.That(targetBucketCount, Is.LessThan(types.Length), "This actual runtime fixture must include another identity bucket.");
                var table = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations", inf)!.GetValue(generator)!;
                var helper = (Dictionary<Type, (int Cost, bool Result)>)table.GetType().GetField("Helper", inf)!.GetValue(table)!;
                Assert.That(helper.Count, Is.EqualTo(types.Length));
                Assert.That((bool)table.GetType().GetField("Invalid", inf)!.GetValue(table)!, Is.False);
                object? heads = table.GetType().GetField("HelperNativeHead", inf)!.GetValue(table);
                object? node = heads is Array buckets ? buckets.GetValue(targetBucket) : heads;
                if (heads is Array actualBuckets) { Assert.That(actualBuckets.Length, Is.EqualTo(bucketCount)); }
                int visited = 0;
                bool found = false;
                while (node is not null)
                {
                    visited++;
                    var nodeType = node.GetType();
                    if (ReferenceEquals(nodeType.GetField("Type", inf)!.GetValue(node), target)) { found = true; break; }
                    node = nodeType.GetField("Next", inf)!.GetValue(node);
                }
                Assert.That(found, Is.True);
                Assert.That(ModelReaderWriterHelpers.ImplementsModelReaderWriter(target), Is.False);
                Assert.That(visited, Is.EqualTo(targetBucketCount),
                    "An actual native recheck must not scan types in other fixed identity buckets.");
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

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Input;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Input.Tests
{
    [NonParallelizable]
    public class ConstructedArgumentBridgeTests
    {
        [Test]
        public void EndpointDoesNotRootCollectibleProducerType()
        {
            var weakType = InstallCollectibleEndpoint();
            for (int i = 0; i < 20 && weakType.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.That(weakType.IsAlive, Is.False, "A bridge endpoint must not pin a collectible producer type or its assembly.");
        }

        [Test]
        public void EndpointsAreIsolatedByExactProducerType()
        {
            for (int run = 0; run < 2; run++)
            {
                _firstCount = 0;
                _secondCount = 0;
                _firstWrapper = null;
                _secondWrapper = null;
                ConstructedArgumentBridge.Install(typeof(FirstProducer), new(static () => true, static (_, _) => _firstCount++, static type => _firstWrapper = type));
                ConstructedArgumentBridge.Install(typeof(SecondProducer), new(static () => true, static (_, _) => _secondCount++, static type => _secondWrapper = type));
                ConstructedArgumentBridge.Install(typeof(FirstProducer), new(() => throw new InvalidOperationException("Must not replace."), (_, _) => throw new InvalidOperationException("Must not replace."), static _ => throw new InvalidOperationException("Must not replace.")));
                var first = new FirstProducer();
                var second = new SecondProducer();
                var unknown = new object();
                var derived = new DerivedProducer();
                Assert.That(ConstructedArgumentBridge.Enabled(first), Is.True);
                Assert.That(ConstructedArgumentBridge.Enabled(second), Is.True);
                Assert.That(ConstructedArgumentBridge.Enabled(unknown), Is.False);
                Assert.That(ConstructedArgumentBridge.Enabled(derived), Is.False, "A derived type must not inherit another type's endpoint.");
                ConstructedArgumentBridge.Record(first, []);
                Assert.That(_firstCount, Is.EqualTo(1), $"first endpoint, run {run}");
                Assert.That(_secondCount, Is.Zero, $"second endpoint must not receive the first producer, run {run}");
                ConstructedArgumentBridge.Record(second, []);
                ConstructedArgumentBridge.Record(unknown, []);
                ConstructedArgumentBridge.Record(derived, []);
                ConstructedArgumentBridge.RegisterWrapperType(first, typeof(FirstWrapper));
                Assert.That(_firstWrapper, Is.SameAs(typeof(FirstWrapper)));
                Assert.That(_secondWrapper, Is.Null);
                ConstructedArgumentBridge.RegisterWrapperType(second, typeof(SecondWrapper));
                ConstructedArgumentBridge.RegisterWrapperType(unknown, typeof(FirstWrapper));
                ConstructedArgumentBridge.RegisterWrapperType(derived, typeof(FirstWrapper));
                Assert.That(_firstWrapper, Is.SameAs(typeof(FirstWrapper)));
                Assert.That(_secondWrapper, Is.SameAs(typeof(SecondWrapper)));
                Assert.That(_firstCount, Is.EqualTo(1), $"run {run}");
                Assert.That(_secondCount, Is.EqualTo(1), $"run {run}");
            }
        }

        private static int _firstCount;
        private static int _secondCount;
        private static Type? _firstWrapper, _secondWrapper;
        private sealed class FirstWrapper { }
        private sealed class SecondWrapper { }
        private class FirstProducer { }
        private sealed class DerivedProducer : FirstProducer { }
        private sealed class SecondProducer { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference InstallCollectibleEndpoint()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CollectibleBridgeProducer"), AssemblyBuilderAccess.RunAndCollect);
            var type = assembly.DefineDynamicModule("producer").DefineType("Producer").CreateType()!;
            ConstructedArgumentBridge.Install(type, new ConstructedArgumentBridge.Endpoint(() => type.Name.Length != 0, static (_, _) => { }, static _ => { }));
            return new WeakReference(type);
        }
    }
}

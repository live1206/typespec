// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class ConstructedArgumentWitnessTests
    {
        [Test]
        public void UnconfiguredMockGeneratorDisablesConstructionWitnessWithoutThrowing()
        {
            var instanceField = typeof(CodeModelGenerator).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previous = instanceField.GetValue(null);
            try
            {
                CodeModelGenerator.Instance = new UnconfiguredGenerator();
                Assert.DoesNotThrow(() => Assert.That(CodeModelGenerator.ObserveConstructedArguments, Is.False));
            }
            finally { instanceField.SetValue(null, previous); }
        }

        private sealed class UnconfiguredGenerator : CodeModelGenerator { }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using System.Threading.Tasks;
using Microsoft.TypeSpec.Generator.Tests.SourceInput;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocGenerationTestIsolationTests
    {
        [TestCase("reentrant")]
        [TestCase("forwarded")]
        [TestCase("build-host")]
        public async Task GenerationFixtureRestoresPriorGlobalState(string fixture)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var instance = typeof(CodeModelGenerator).GetField("_instance", flags)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), flags)!;
            var original = instance.GetValue(null);
            var originalPolicy = policy.GetValue(null);
            try
            {
                var prior = await MockHelpers.LoadMockGeneratorAsync(configuration: "{\"package-name\":\"PriorSample\"}");
                var priorPolicy = Configuration.UnreferencedTypesHandling;
                Assert.That(priorPolicy, Is.Not.EqualTo(Configuration.UnreferencedTypesHandlingOption.KeepAll));
                switch (fixture)
                {
                    case "reentrant": await new PreparedFieldTypeTests().ReentrantGeneratorSwitchCannotBindAnotherGeneratorsOutput(); break;
                    case "forwarded": await new PreparedFieldTypeTests().ForwardedOutputObservationDoesNotClaimExclusiveProvenance(); break;
                    case "build-host": await new BuildHostGenerationTests().PocProposalIsPreparedBeforeActualOutputProviderWrites(); break;
                }
                Assert.Multiple(() =>
                {
                    Assert.That(CodeModelGenerator.Instance, Is.SameAs(prior.Object));
                    Assert.That(Configuration.UnreferencedTypesHandling, Is.EqualTo(priorPolicy));
                });
            }
            finally
            {
                instance.SetValue(null, original);
                policy.SetValue(null, originalPolicy);
            }
        }
    }
}

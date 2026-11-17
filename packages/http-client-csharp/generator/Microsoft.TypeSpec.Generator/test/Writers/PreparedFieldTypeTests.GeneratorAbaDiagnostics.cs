// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void RestoredGeneratorInsideSourceCallbackIsOnlyAnObservationNotAnOwnershipProof()
        {
            using var host = new BodyOnlyPhysicalHost();
            var generator = host.Generator;
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = generator.OutputLibrary;
            output.TypeProviders = new List<TypeProvider> { owner };
            var other = new BodyOnlyHostGenerator(new GeneratorContext(generator.Configuration)) { SourceInputModel = generator.SourceInputModel };
            var switched = false;
            host.Session.CurrentnessCallback = OnFinalDirectSourceProof(() =>
            {
                switched = true;
                CodeModelGenerator.Instance = other;
                CodeModelGenerator.Instance = generator;
            });
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(generator, output, _ => true);
            host.Session.CurrentnessCallback = null;
            Assert.That(switched, Is.True);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.IsProspectiveGeneratorCurrent(), Is.True,
                "An observed current instance does not reveal a change-and-restore within the callback.");
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False, "This fieldless observation grants no shared or local permission.");
        }
    }
}

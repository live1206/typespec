// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class PreparedPocInventoryBudgetTests
    {
        [Test]
        public void SupplementalCurrentnessObservesActualWitnessedArrayElements()
        {
            MockHelpers.LoadMockGenerator();
            ValueExpression[] arguments = [new LiteralExpression(1)];
            var call = new InvokeMethodExpression(null, "SyntheticCall", arguments);
            ConstructedArgumentWitness.Record(call, arguments);
            var revision = call.DeclarationRevision;
            var node = PreparedPocSupplementalFile.Node.TryCapture(call, true, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.IsCurrent(), Is.True);
            arguments[0] = new LiteralExpression(2);
            Assert.That(ReferenceEquals(call.Arguments, arguments), Is.True);
            Assert.That(call.DeclarationRevision, Is.EqualTo(revision), "An in-place element edit does not touch the invocation revision.");
            using var writer = new CodeWriter();
            call.Write(writer);
            Assert.That(writer.ToString(false), Does.Contain("SyntheticCall(2)"), "The one native write observes the actual current element.");
            Assert.That(node.IsCurrent(), Is.False, "A construction witness cannot substitute for current carrier elements.");
        }

        [Test]
        public void RawInventoryZeroBudgetDoesNotAllocatePerOwnerObservations()
        {
            MockHelpers.LoadMockGenerator();
            var generation = Guid.NewGuid();
            var compilation = CSharpCompilation.Create("InventoryBudget");
            var owners = Enumerable.Range(0, 256).Select(index => (TypeProvider)new TestTypeProvider(name: "Owner" + index)).ToImmutableArray();
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance);
            foreach (var owner in owners)
            {
                sources.Add(owner, new PreparedSourceFile(generation, owner, owner.Name + ".cs", compilation, false));
            }
            // Warm fixed entrypoint state, not the measured owner observations.
            _ = PreparedPocGenerationRawInventory.Capture([], sources, new PocWorkBudget(0));
            var budget = new PocWorkBudget(0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var rejected = PreparedPocGenerationRawInventory.Capture(owners, sources, budget);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(rejected.GetFile(owners[0]), Is.Null);
            Assert.That(allocated, Is.LessThan(16384), "No owner-scaled dictionary/FileObservation/list copies before the first funded probe.");

            var compact = PreparedPocGenerationRawInventory.Capture([owners[0]],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [owners[0]] = sources[owners[0]] });
            Assert.That(compact.GetFile(owners[0]), Is.Not.Null);
            Assert.That(compact.HasUncoveredEmission, Is.True);
        }

        [Test]
        public void GenerationCapturePrepaysActualSourceLookupCollisionBucket()
        {
            MockHelpers.LoadMockGenerator();
            var owner = new TestTypeProvider(name: "Target");
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "Target.cs", CSharpCompilation.Create("CollisionBudget"), false);
            var compactSources = new Dictionary<TypeProvider, PreparedSourceFile>(17, ReferenceEqualityComparer.Instance) { [owner] = source };
            var compactBudget = new PocWorkBudget(262144);
            var compact = PreparedPocGenerationEnvelope.Capture([owner], compactSources, compactBudget);
            Assert.That(compact.PlannedFileCount, Is.EqualTo(1));
            var compactWork = 262144 - compactBudget.Remaining;

            var collisions = new Dictionary<TypeProvider, PreparedSourceFile>(17, ReferenceEqualityComparer.Instance) { [owner] = source };
            var capacity = collisions.EnsureCapacity(0);
            var bucket = unchecked((uint)RuntimeHelpers.GetHashCode(owner)) % (uint)capacity;
            for (var attempt = 0; attempt < capacity * 128 && collisions.Count < 12; attempt++)
            {
                var candidate = new TestTypeProvider(name: "Collision" + attempt);
                if (unchecked((uint)RuntimeHelpers.GetHashCode(candidate)) % (uint)capacity == bucket)
                {
                    collisions.Add(candidate, source);
                }
            }
            Assert.That(collisions.Count, Is.EqualTo(12), "Actual native identity hashes, no private bucket or seeded hash inspection.");
            Assert.That(collisions.EnsureCapacity(0), Is.EqualTo(capacity), "Keep the selected native bucket stable.");
            Assert.That(collisions.Keys.All(key => unchecked((uint)RuntimeHelpers.GetHashCode(key)) % (uint)capacity == bucket), Is.True);
            // Extra source values are deliberately not expected stream owners.
            // The matching key was inserted first, so native lookup traverses
            // the actual later same-bucket entries. No CPU-overrun assertion.
            var insufficient = PreparedPocGenerationEnvelope.Capture([owner], collisions, new PocWorkBudget(compactWork));
            Assert.That(insufficient.PlannedFileCount, Is.Zero, "Compact capture credits must not silently pay for a wider native source lookup.");
            Assert.That(insufficient.HasUncoveredEmission, Is.True);
            var funded = PreparedPocGenerationEnvelope.Capture([owner], collisions, new PocWorkBudget(262144));
            Assert.That(funded.PlannedFileCount, Is.EqualTo(1));
        }
    }
}

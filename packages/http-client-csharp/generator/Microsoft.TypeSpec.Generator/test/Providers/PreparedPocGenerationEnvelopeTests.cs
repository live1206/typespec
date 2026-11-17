// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocGenerationEnvelopeTests
    {
        // Synthetic traversal receipts exercise accounting, not native writing
        // or permission. Real-stream coverage separately uses native receipts.
        private static (TypeProvider Owner, PreparedSourceFile Source, SourceConsumptionReceipt Receipt) File(string name, Guid generation)
        {
            var owner = new TestTypeProvider(name: name);
            var source = new PreparedSourceFile(generation, owner, name + ".cs", CSharpCompilation.Create("Envelope"), false);
            return (owner, source, new SourceConsumptionReceipt(generation, source.FilePath, source.Roots.Length,
                source.Roots.Select(root => root.Id).ToImmutableArray(), true));
        }

        [Test]
        public void PointLookupUsesItsOwnFundedIdentityProbe()
        {
            var file = File("One", Guid.NewGuid());
            var envelope = PreparedPocGenerationEnvelope.Capture([file.Owner],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [file.Owner] = file.Source });
            Assert.That(envelope.GetFile(file.Owner), Is.Not.Null);
            Assert.That(envelope.GetFile(file.Owner, new PocWorkBudget(0)), Is.Null,
                "A point lookup cannot perform a hash/probe after its own budget is exhausted.");
            Assert.That(envelope.GetFile(file.Owner, new PocWorkBudget(2)), Is.Not.Null,
                "A single owner is supported by its separately funded compact lookup.");
        }

        [Test]
        public void ReceiptComparisonRequiresFundingBeforeDistinctEqualLongKeys()
        {
            var file = File(new string('L', 256), Guid.NewGuid());
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [file.Owner] = file.Source };
            var valid = file.Receipt with
            {
                File = new string(file.Receipt.File.ToCharArray()),
                Consumed = file.Receipt.Consumed.Select(id => id with
                {
                    File = new string(id.File.ToCharArray()),
                    MemberPath = new string(id.MemberPath.ToCharArray()),
                    RootRole = new string(id.RootRole.ToCharArray()),
                    EdgePath = new string(id.EdgePath.ToCharArray())
                }).ToImmutableArray()
            };
            Assert.That(ReferenceEquals(valid.File, file.Receipt.File), Is.False);
            Assert.That(valid.File, Is.EqualTo(file.Receipt.File));
            var limited = PreparedPocGenerationEnvelope.Capture([file.Owner], sources);
            limited.ObserveWrite(file.Owner, valid, new PocWorkBudget(2));
            Assert.That(limited.GetFile(file.Owner)!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Rejected),
                "Two credits cover the owner probe, not distinct-equal path/occurrence comparisons.");
            limited.ObserveWrite(file.Owner, file.Receipt);
            Assert.That(limited.HasCompleteTraversalReceipts, Is.False, "An observed exhausted receipt remains sticky.");
            var funded = PreparedPocGenerationEnvelope.Capture([file.Owner], sources);
            funded.ObserveWrite(file.Owner, valid);
            Assert.That(funded.GetFile(file.Owner)!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Matched));
            funded.CompleteStream();
            Assert.That(funded.HasCompleteTraversalReceipts, Is.True);
            Assert.That(funded.HasUncoveredEmission, Is.True, "A paid receipt is still not spelling authority.");
        }

        [Test]
        public void LastReceiptAloneDoesNotEstablishStreamExhaustion()
        {
            var first = File("One", Guid.NewGuid());
            var envelope = PreparedPocGenerationEnvelope.Capture([first.Owner],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [first.Owner] = first.Source });
            envelope.ObserveWrite(first.Owner, first.Receipt);
            Assert.That(envelope.GetFile(first.Owner)!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Matched));
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False,
                "Stopping immediately after the final expected write is not successful iterator exhaustion.");
        }

        [TestCase("generation")]
        [TestCase("path")]
        [TestCase("roots")]
        [TestCase("missing")]
        [TestCase("default-consumed")]
        [TestCase("missing-consumed")]
        [TestCase("foreign-consumed")]
        [TestCase("stale")]
        public void RejectedReceiptCannotBeRepairedByRestoration(string failure)
        {
            var (owner, source, receipt) = File("One", Guid.NewGuid());
            var envelope = PreparedPocGenerationEnvelope.Capture([owner], new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [owner] = source });
            var invalid = failure switch
            {
                "generation" => receipt with { Generation = Guid.NewGuid() },
                "path" => receipt with { File = "Other.cs" },
                "roots" => receipt with { RootCount = receipt.RootCount + 1 },
                "missing" => null,
                "default-consumed" => receipt with { Consumed = default },
                "missing-consumed" => receipt with { Consumed = [] },
                "foreign-consumed" => receipt with { Consumed = [File("Other", source.Generation).Receipt.Consumed[0]] },
                _ => receipt with { WasCurrentAtEndOfWrite = false }
            };
            envelope.ObserveWrite(owner, invalid);
            Assert.That(envelope.GetFile(owner)!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Rejected));
            envelope.ObserveWrite(owner, receipt);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
            Assert.That(envelope.HasUncoveredEmission, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AllFilesMustProvideOrderedReceipts(bool reverse)
        {
            var generation = Guid.NewGuid();
            var first = File("One", generation);
            var second = File("Two", generation);
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance)
            { [first.Owner] = first.Source, [second.Owner] = second.Source };
            var envelope = PreparedPocGenerationEnvelope.Capture([first.Owner, second.Owner], sources);
            Assert.That(envelope.PlannedFileCount, Is.EqualTo(2));
            envelope.ObserveWrite(reverse ? second.Owner : first.Owner, reverse ? second.Receipt : first.Receipt);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
            envelope.ObserveWrite(reverse ? first.Owner : second.Owner, reverse ? first.Receipt : second.Receipt);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
            envelope.CompleteStream();
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.EqualTo(!reverse));
            Assert.That(envelope.HasUncoveredEmission, Is.True, "Traversal is not grammar or a header certificate.");
            envelope.ObserveWrite(first.Owner, first.Receipt);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False, "Duplicate writes cannot repair completeness.");
        }

        [TestCase("early")]
        [TestCase("repeat")]
        [TestCase("late-yield")]
        public void StreamFailureRemainsRejected(string failure)
        {
            var first = File("One", Guid.NewGuid());
            var envelope = PreparedPocGenerationEnvelope.Capture([first.Owner],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [first.Owner] = first.Source });
            if (failure == "early") { envelope.CompleteStream(); }
            envelope.ObserveWrite(first.Owner, first.Receipt);
            envelope.CompleteStream();
            if (failure == "repeat") { envelope.CompleteStream(); }
            if (failure == "late-yield") { envelope.Reject(); }
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
        }

        [Test]
        public void NullPathRejectsWithoutCallingProducer()
        {
            var owner = new TestTypeProvider();
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, null!, CSharpCompilation.Create("Envelope"), false);
            var envelope = PreparedPocGenerationEnvelope.Capture([owner],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [owner] = source });
            Assert.That(envelope.PlannedFileCount, Is.Zero);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CollidingPathsAndMixedGenerationsAreRejected(bool pathCollision)
        {
            var first = File("One", Guid.NewGuid());
            var second = File(pathCollision ? "One" : "Two", pathCollision ? first.Source.Generation : Guid.NewGuid());
            var envelope = PreparedPocGenerationEnvelope.Capture([first.Owner, second.Owner],
                new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [first.Owner] = first.Source, [second.Owner] = second.Source });
            Assert.That(envelope.PlannedFileCount, Is.Zero);
            Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
        }

        private sealed class HostileComparer : IEqualityComparer<TypeProvider>
        {
            public bool Equals(TypeProvider? left, TypeProvider? right) => throw new InvalidOperationException("Unknown comparer must not execute.");
            public int GetHashCode(TypeProvider value) => throw new InvalidOperationException("Unknown comparer must not execute.");
        }

        [Test]
        public void UnknownMapAndComparerAreNotQueried()
        {
            var first = File("One", Guid.NewGuid());
            var known = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [first.Owner] = first.Source };
            foreach (var map in new IReadOnlyDictionary<TypeProvider, PreparedSourceFile>[]
            {
                new System.Collections.ObjectModel.ReadOnlyDictionary<TypeProvider, PreparedSourceFile>(known),
                new Dictionary<TypeProvider, PreparedSourceFile>(new HostileComparer())
            })
            {
                var envelope = PreparedPocGenerationEnvelope.Capture([first.Owner], map);
                Assert.That(envelope.PlannedFileCount, Is.Zero);
                Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
            }
        }

        [Test]
        public void UnknownDuplicateMissingOrOversizedUniverseHasNoPartialPlan()
        {
            var first = File("One", Guid.NewGuid());
            var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [first.Owner] = first.Source };
            foreach (var expected in new[] { default(ImmutableArray<TypeProvider>), ImmutableArray<TypeProvider>.Empty,
                ImmutableArray.Create(first.Owner, first.Owner), ImmutableArray.Create(first.Owner, new TestTypeProvider()),
                Enumerable.Repeat(first.Owner, 4097).ToImmutableArray() })
            {
                var envelope = PreparedPocGenerationEnvelope.Capture(expected, sources);
                Assert.That(envelope.PlannedFileCount, Is.Zero);
                Assert.That(envelope.HasCompleteTraversalReceipts, Is.False);
                Assert.That(envelope.HasUncoveredEmission, Is.True);
            }
        }
    }
}

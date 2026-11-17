// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.ReferenceMap
{
    [NonParallelizable]
    public class ProviderReferenceMapRemovalBudgetTests
    {
        [SetUp]
        public void SetUp() => ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();

        [TearDown]
        public void TearDown() => ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();

        [Test]
        public void BoundedRemovalSnapshotDetectsChangedContents()
        {
            TypeProvider[] roots = [new TestTypeProvider("Stable", ns: "Sample")];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
            candidates.Clear();
            candidates.Add("Sample.Removed");
            Assert.That(session.TryCaptureObservedRemovalCandidates(out var snapshot), Is.True);
            Assert.That(session.IsObservedRemovalCandidatesCurrent(snapshot!), Is.True);
            candidates.Clear();
            candidates.Add("Sample.Other");
            Assert.That(session.IsObservedRemovalCandidatesCurrent(snapshot!), Is.False);
        }

        [Test]
        public void RemovalCurrentnessBudgetCoversMembershipChainsNotOnlyCharacters()
        {
            TypeProvider[] roots = [new TestTypeProvider("Stable", ns: "Sample")];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var output = CodeModelGenerator.Instance.OutputLibrary;
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
            var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
            candidates.Clear();
            for (int i = 0; i < 64; i++) { candidates.Add($"R{i:D7}"); }
            Assert.That(session.TryCaptureObservedRemovalCandidates(out var snapshot), Is.True);
            var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, roots, session,
                snapshot, new PocWorkBudget(262144));
            Assert.That(boundary, Is.Not.Null);
            Assert.That(boundary!.TrySpendNativeWriteLookup(roots[0].CachedType!, new PocWorkBudget(1024)), Is.False,
                "Each future native ShouldWrite probe must fund its possible membership chain.");
            Assert.That(boundary.TryShouldWrite(roots[0], roots[0].CachedType!, new PocWorkBudget(4096), out var shouldWrite), Is.True);
            Assert.That(shouldWrite, Is.True, "A funded probe retains the actual native write policy.");
            // Funding 64 entries and their 512 characters cannot establish a
            // bound for 64 membership probes with uncertified bucket topology.
            // The profile must precharge possible chains, not assume a constant
            // HashSet lookup from an ordinal comparer or short strings.
            Assert.That(boundary!.IsCurrent(new PocWorkBudget(1024)), Is.False);
            Assert.That(boundary.IsCurrent(new PocWorkBudget(262144)), Is.False, "A budget veto remains sticky.");
        }

        [Test]
        public void PocRemovalSnapshotReservesBothHashPassesBeforeAllocation()
        {
            TypeProvider[] roots = [new TestTypeProvider("Stable", ns: "Sample")];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
            candidates.Clear();
            for (int i = 0; i < 64; i++) { candidates.Add($"R{i:D7}"); }
            Assert.That(session.TryCaptureObservedRemovalCandidates(out var native), Is.True);
            Assert.That(session.TryCaptureObservedRemovalCandidates(new PocWorkBudget(262144), out var funded), Is.True);
            Assert.That(funded!.SetEquals(native!), Is.True, "Funded observation preserves the native contents.");
            // Warm this exact early-veto path before the allocation observation.
            Assert.That(session.TryCaptureObservedRemovalCandidates(new PocWorkBudget(8192), out _), Is.False);
            var budget = new PocWorkBudget(8192);
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            var admitted = session.TryCaptureObservedRemovalCandidates(budget, out var unfunded);
            var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(admitted, Is.False);
            Assert.That(unfunded, Is.Null);
            Assert.That(allocated, Is.Zero, "Reject before creating the immutable builder/copy or hashing its names.");
            Assert.That(session.TryCaptureObservedRemovalCandidates(out _), Is.True, "Native absolute-bound behavior remains unchanged.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocRemovalSnapshotBoundsSparseCapacityBeforeEnumeration(bool retainOne)
        {
            TypeProvider[] roots = [new TestTypeProvider("Stable", ns: "Sample")];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
            candidates.Clear();
            for (int i = 0; i < 16384; i++) { candidates.Add($"R{i:D7}"); }
            // Remove, do not Clear: live Count no longer bounds the slots an
            // actual HashSet enumerator may scan, as after native ExceptWith.
            for (int i = retainOne ? 1 : 0; i < 16384; i++) { candidates.Remove($"R{i:D7}"); }
            Assert.That(candidates.Count, Is.EqualTo(retainOne ? 1 : 0));
            Assert.That(candidates.EnsureCapacity(0), Is.GreaterThan(8192));
            Assert.That(session.TryCaptureObservedRemovalCandidates(out var legacy), Is.True);
            Assert.That(session.TryCaptureObservedRemovalCandidates(new PocWorkBudget(8192), out var sparse), Is.False,
                "Bound actual public capacity before enumeration, not only the remaining live names.");
            Assert.That(sparse, Is.Null);
            candidates.TrimExcess();
            Assert.That(candidates.EnsureCapacity(0), Is.LessThan(8192));
            Assert.That(session.TryCaptureObservedRemovalCandidates(new PocWorkBudget(8192), out var compact), Is.True);
            Assert.That(compact!.SetEquals(legacy!), Is.True, "A compact set of the same native contents remains supported.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OversizedRemovalSetCannotProducePocSnapshot(bool tooManyNames)
        {
            TypeProvider[] roots = [new TestTypeProvider("Stable", ns: "Sample")];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
            candidates.Clear();
            if (tooManyNames)
            {
                for (int i = 0; i < 4097; i++) { candidates.Add($"Sample.Type{i}"); }
            }
            else
            {
                candidates.Add(new string('x', 65537));
            }
            Assert.That(session.TryCaptureObservedRemovalCandidates(out var snapshot), Is.False);
            Assert.That(snapshot, Is.Null);
        }
    }
}

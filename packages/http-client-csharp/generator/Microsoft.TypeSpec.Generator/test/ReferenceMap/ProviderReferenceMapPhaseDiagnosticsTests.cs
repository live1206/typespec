// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.ReferenceMap
{
    [NonParallelizable]
    public class ProviderReferenceMapPhaseDiagnosticsTests
    {
        [SetUp]
        public void SetUp() => ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();

        [TearDown]
        public void TearDown() => ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();

        [Test]
        public void NestedAnalysisDuringOuterAnalysisIsHiddenByTheFinalResultIdentity()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            object? nestedResult = null;
            var nestedCount = 0;
            var probe = new ReentrantDeclaringProvider(() =>
            {
                nestedCount++;
                ProviderReferenceMapAnalyzer.Analyze([stable]);
                nestedResult = ProviderReferenceMapAnalyzer.LatestResult;
            });
            TypeProvider[] roots = [stable, probe];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var output = CodeModelGenerator.Instance.OutputLibrary;
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(nestedCount, Is.EqualTo(1));
            Assert.That(nestedResult, Is.Not.Null);
            Assert.That(ProviderReferenceMapAnalyzer.LatestResult, Is.Not.Null.And.Not.SameAs(nestedResult),
                "The outer Analyze installs a different result after nested analysis has already happened.");
            Assert.That(output.TypeProviders, Is.SameAs(roots), "The root reference alone reveals no analyzer reentry.");
        }

        [Test]
        public void NestedSessionDisposalChangesAnExistingSessionsWriteFilter()
        {
            var unused = new TestTypeProvider("UnusedModel", ns: "Sample");
            TypeProvider[] roots = [unused];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var outer = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(outer.ShouldWriteProvider(unused), Is.False);
            using (var nested = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots))
            {
                Assert.That(nested.ShouldWriteProvider(unused), Is.False);
            }
            Assert.That(ProviderReferenceMapAnalyzer.LatestResult, Is.Null);
            Assert.That(outer.ShouldWriteProvider(unused), Is.True,
                "The session reads the shared analyzer result and reset defaults to writing the provider.");
            Assert.That(CodeModelGenerator.Instance.OutputLibrary.TypeProviders, Is.SameAs(roots));
        }

        [Test]
        public void RootReplacementDuringAnalysisIsInvisibleToActivityWitnessAlone()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            OutputLibrary? output = null;
            TypeProvider[]? roots = null;
            var replacements = 0;
            var probe = new ReentrantDeclaringProvider(() =>
            {
                replacements++;
                output!.TypeProviders = new System.Collections.Generic.List<TypeProvider>(roots!);
                output.TypeProviders = roots!;
            });
            roots = [stable, probe];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots!));
            output = CodeModelGenerator.Instance.OutputLibrary;
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            Assert.That(output.TryCaptureMaterializedTypeProviders(out var captured, out var revision), Is.True);
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            Assert.That(replacements, Is.EqualTo(1));
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True,
                "Replacing roots is not an analyzer entry and requires its own reference/revision witness.");
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            Assert.That(output.IsMaterializedTypeProviderSnapshotCurrent(captured!, revision), Is.False);
        }

        [Test]
        public void ObservedRootPhaseRejectsForeignOutputAndReferenceRestore()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            TypeProvider[] roots = [stable];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var output = CodeModelGenerator.Instance.OutputLibrary;
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
            Assert.That(session.IsObservedReferenceMapRootCurrent(output, roots), Is.True);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.False);
            var foreign = new OutputLibrary { TypeProviders = roots };
            Assert.That(session.IsObservedReferenceMapRootCurrent(foreign, roots), Is.False,
                "Matching root objects do not make a foreign output the analyzed output.");
            session.ObserveEnumerationRoot(output, roots);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.True);
            Assert.That(session.IsObservedEnumerationRootCurrent(foreign, roots), Is.False);
            output.TypeProviders = new System.Collections.Generic.List<TypeProvider>(roots);
            output.TypeProviders = roots;
            Assert.That(session.IsObservedReferenceMapRootCurrent(output, roots), Is.False);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.False);
        }

        [Test]
        public void ObservedRootPhaseRejectsRepeatedEnumeration()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            TypeProvider[] roots = [stable];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var output = CodeModelGenerator.Instance.OutputLibrary;
            _ = output.TypeProviders;
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
            session.ObserveEnumerationRoot(output, roots);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.True);
            session.ObserveEnumerationRoot(output, roots);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.False);
        }

        [Test]
        public void ObservedRootPhaseRejectsReplacementDuringReferenceMapAnalysis()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            OutputLibrary? output = null;
            TypeProvider[]? roots = null;
            var replacements = 0;
            var probe = new ReentrantDeclaringProvider(() =>
            {
                replacements++;
                output!.TypeProviders = new System.Collections.Generic.List<TypeProvider>(roots!);
                output.TypeProviders = roots!;
            });
            roots = [stable, probe];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots!));
            output = CodeModelGenerator.Instance.OutputLibrary;
            _ = output.TypeProviders;
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
            Assert.That(replacements, Is.EqualTo(1));
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True);
            Assert.That(session.IsObservedReferenceMapRootCurrent(output, roots), Is.False);
            session.ObserveEnumerationRoot(output, roots);
            Assert.That(session.IsObservedEnumerationRootCurrent(output, roots), Is.False);
        }

        [Test]
        public void ActivityWitnessRejectsNestedAnalysisDuringPreparation()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            var nestedCount = 0;
            var probe = new ReentrantDeclaringProvider(() => { nestedCount++; ProviderReferenceMapAnalyzer.Analyze([stable]); });
            TypeProvider[] roots = [stable, probe];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            Assert.That(nestedCount, Is.EqualTo(1));
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.False,
                "A fresh outer result cannot hide a nested analyzer entry.");
        }

        [TestCase("reset")]
        [TestCase("apply")]
        [TestCase("restore")]
        [TestCase("analyze")]
        public void ActivityWitnessRejectsLaterAnalyzerEntries(string activity)
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            TypeProvider[] roots = [stable];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True);
            _ = session.ShouldWriteProvider(stable);
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True, "A read-only filter call must not invalidate the witness.");
            switch (activity)
            {
                case "reset": ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility(); break;
                case "apply": ProviderReferenceMapAnalyzer.ApplyPreWriteAccessibility(roots); break;
                case "restore": ProviderReferenceMapAnalyzer.RestorePreWriteModelFactoryMethods(); break;
                case "analyze": ProviderReferenceMapAnalyzer.Analyze(roots); break;
                default: Assert.Fail("Unexpected analyzer activity."); break;
            }
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.False);
        }

        [Test]
        public void ActivityWitnessRejectsNestedSessionAndStaysStaleAfterDisposal()
        {
            var unused = new TestTypeProvider("UnusedModel", ns: "Sample");
            TypeProvider[] roots = [unused];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            using var outer = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            Assert.That(outer.IsObservedAnalyzerActivityCurrent(), Is.True);
            using (var nested = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots))
            {
                Assert.That(outer.IsObservedAnalyzerActivityCurrent(), Is.False);
            }
            Assert.That(outer.IsObservedAnalyzerActivityCurrent(), Is.False);
            Assert.That(outer.ShouldWriteProvider(unused), Is.True, "The ordinary outer session behavior is unchanged.");
        }

        [Test]
        public void DisposingWitnessedSessionInvalidatesItsActivityObservation()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            TypeProvider[] roots = [stable];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True);
            session.Dispose();
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.False);
            session.Dispose();
            Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparationPropagatesProviderExceptionsWithoutAReplacementSession(bool withWitness)
        {
            var expected = new InvalidOperationException("controlled provider callback failure");
            var provider = new ThrowingDeclaringProvider(expected);
            TypeProvider[] roots = [provider];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            provider.Armed = true;
            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                using var session = withWitness
                    ? ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots)
                    : ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            });
            Assert.That(actual, Is.SameAs(expected));
        }

        private sealed class ThrowingDeclaringProvider(Exception exception) : TestTypeProvider("Throwing", ns: "Sample")
        {
            internal bool Armed { get; set; }
            protected override TypeProvider? BuildDeclaringTypeProvider() => Armed ? throw exception : null;
        }

        [Test]
        public void ActivityWitnessDoesNotReplayProviderGettersOrChangeTheWriteFilter()
        {
            var ordinary = CapturePreparationBehavior(withWitness: false);
            var witnessed = CapturePreparationBehavior(withWitness: true);
            TestContext.Progress.WriteLine($"Reference-map preparation: ordinary={ordinary}, witnessed={witnessed}");
            Assert.That(witnessed, Is.EqualTo(ordinary));
        }

        private static (int DeclaringCalls, bool ShouldWrite) CapturePreparationBehavior(bool withWitness)
        {
            var provider = new CountingDeclaringProvider();
            TypeProvider[] roots = [provider];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            provider.ResetCount();
            using var session = withWitness
                ? ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots)
                : ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            var shouldWrite = session.ShouldWriteProvider(provider);
            if (withWitness) { Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.True); }
            return (provider.DeclaringCalls, shouldWrite);
        }

        private sealed class CountingDeclaringProvider : TestTypeProvider
        {
            internal CountingDeclaringProvider() : base("Counted", ns: "Sample") { }
            internal int DeclaringCalls { get; private set; }
            internal void ResetCount() => DeclaringCalls = 0;
            protected override TypeProvider? BuildDeclaringTypeProvider() { DeclaringCalls++; return null; }
        }

        [Test]
        public void ActivityWitnessFailsClosedAtSaturation()
        {
            var stable = new TestTypeProvider("Stable", ns: "Sample");
            TypeProvider[] roots = [stable];
            MockHelpers.LoadMockGenerator(createOutputLibrary: () => new TestOutputLibrary(roots));
            var field = typeof(ProviderReferenceMapAnalyzer).GetField("_activityVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var original = (long)field.GetValue(null)!;
            try
            {
                field.SetValue(null, long.MaxValue - 2);
                using var session = ProviderReferenceMapAnalyzer.PrepareForGenerationWithActivityWitness(roots);
                Assert.That(ProviderReferenceMapAnalyzer.ActivityVersion, Is.EqualTo(long.MaxValue));
                Assert.That(session.IsObservedAnalyzerActivityCurrent(), Is.False);
            }
            finally { field.SetValue(null, original); }
        }

        private sealed class ReentrantDeclaringProvider(Action onAnalyze) : TestTypeProvider("Probe", ns: "Sample")
        {
            private bool _triggered;

            protected override TypeProvider? BuildDeclaringTypeProvider()
            {
                if (!_triggered && new StackTrace().GetFrames().Any(frame =>
                    frame.GetMethod()?.DeclaringType == typeof(ProviderReferenceMapAnalyzer) && frame.GetMethod()?.Name == "Analyze"))
                {
                    _triggered = true;
                    onAnalyze();
                }
                return null;
            }
        }
    }
}

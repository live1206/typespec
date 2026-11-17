// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocNativeTypeCaptureReviewTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator(includeXmlDocs: true);

        [TestCase(false)]
        [TestCase(true)]
        public void NativeHeaderDisposalDoesNotPublishDetachedCapture(bool throws)
        {
            var baseline = FinalizeWithHeaderDisposal(false, throws);
            var captured = FinalizeWithHeaderDisposal(true, throws);

            Assert.That(baseline.Disposals, Is.EqualTo(1));
            Assert.That(captured.Disposals, Is.EqualTo(1));
            Assert.That(captured.CompareReads, Is.EqualTo(baseline.CompareReads));
            Assert.That(captured.Text, Is.EqualTo(baseline.Text));
            Assert.That(captured.Error, Is.EqualTo(baseline.Error));
            Assert.That(captured.Use, Is.Null,
                "Disposal during the actual native header must invalidate an unpublished detached capture.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyUncapturedDrainDoesNotConsumeSetup(bool header)
        {
            using var writer = new CodeWriter();
            Assert.That(writer.ToString(header), Is.Empty);
            var (type, declaration, id) = Inputs();
            var budget = new PocWorkBudget(PocNativeTypeCapture.SetupWork + 27);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 27), Is.True);
            Assert.That(budget.Remaining, Is.Zero);
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Not.Null);
        }

        [TestCase(0, false)]
        [TestCase(PocNativeTypeCapture.SetupWork + 26, false)]
        [TestCase(PocNativeTypeCapture.SetupWork + 27, true)]
        public void SetupCreditsAndFailedAttemptRemainOneShot(int credits, bool enabled)
        {
            using var writer = new CodeWriter();
            var (type, declaration, id) = Inputs();
            var budget = new PocWorkBudget(credits);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 27), Is.EqualTo(enabled));
            Assert.That(budget.Remaining, Is.EqualTo(enabled ? 0 : credits));
            var second = new PocWorkBudget(10000);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, second, 27), Is.False);
            Assert.That(second.Remaining, Is.EqualTo(10000));
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse is not null, Is.EqualTo(enabled));
        }

        [Test]
        public void NativePayloadPermanentlyDeniesSetupAfterDrain()
        {
            using var writer = new CodeWriter();
            writer.AppendRaw("native");
            Assert.That(writer.ToString(false), Is.EqualTo("native"));
            var (type, declaration, id) = Inputs();
            var budget = new PocWorkBudget(10000);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 27), Is.False);
            Assert.That(budget.Remaining, Is.EqualTo(10000));
        }

        [Test]
        public void PublishedSnapshotStaysVisibleAfterLaterDisposeUntilNextDrain()
        {
            using var writer = new CodeWriter();
            var (type, declaration, id) = Inputs();
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, new PocWorkBudget(2000), 27), Is.True);
            declaration.Write(writer);
            var content = writer.ToString(false);
            var use = writer.NativeTypeUse;
            Assert.That(use, Is.Not.Null);
            writer.Dispose();
            writer.Dispose();
            Assert.That(writer.NativeTypeUse, Is.SameAs(use));
            Assert.That(content.Substring(use!.PrimaryStart, use.PrimaryLength), Is.EqualTo("global::Native.Types.Foo"));
            Assert.That(writer.ToString(false), Is.Empty);
            Assert.That(writer.NativeTypeUse, Is.Null);
            Assert.That(use.EmittedName, Is.EqualTo("Foo"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnionKeepsOriginalLegacyListIdentityAndDrainOrder(bool capture)
        {
            using var writer = new CodeWriter();
            writer.EnableQualifiedFallback();
            var (type, declaration, id) = Inputs();
            if (capture) { Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, new PocWorkBudget(2000), 27), Is.True); }
            Assert.That(writer.TryAppendPreparedBodyName("B", "global::Other.B"), Is.True);
            var slot = typeof(CodeWriter).GetField("_nameAlternatives", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var state = slot.GetValue(writer);
            var list = state is PocNativeTypeCapture paid ? paid.Alternatives : state;
            Assert.That(writer.TryAppendPreparedBodyName("C", "global::Other.C"), Is.True);
            Assert.That(state is PocNativeTypeCapture owner ? owner.Alternatives : slot.GetValue(writer), Is.SameAs(list));
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("BCglobal::Native.Types.Foo value"));
            Assert.That(writer.QualifiedContent, Is.EqualTo("global::Other.Bglobal::Other.Cglobal::Native.Types.Foo value"));
            Assert.That(writer.NameAlternatives.Length, Is.EqualTo(2));
            Assert.That(writer.NameAlternatives[0].QualifiedOffset, Is.Zero);
            Assert.That(writer.NameAlternatives[1].QualifiedOffset, Is.EqualTo("global::Other.B".Length));
            Assert.That(writer.NativeTypeUse is not null, Is.EqualTo(capture));
            if (capture) { Assert.That(slot.GetValue(writer), Is.SameAs(state), "Keep the paid lifetime link through finalization."); }
            else { Assert.That(slot.GetValue(writer), Is.Null); }
            Assert.That(writer.ToString(false), Is.Empty);
            Assert.That(writer.NameAlternatives, Is.Empty);
            Assert.That(writer.NativeTypeUse, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReentrantEmptyHeaderDrainInvalidatesPendingPublication(bool companion)
        {
            string? baseline = null;
            int baselineReads = 0;
            foreach (bool capture in new[] { false, true })
            {
                using var writer = new CodeWriter();
                if (companion) { writer.EnableQualifiedFallback(); }
                var (type, declaration, id) = Inputs();
                if (capture) { Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, new PocWorkBudget(2000), 27), Is.True); }
                writer.UseNamespace("Header.Alpha");
                writer.UseNamespace("Header.Beta");
                declaration.Write(writer);
                var previous = CultureInfo.CurrentCulture;
                var culture = new HeaderDrainingCulture(writer);
                string content;
                try
                {
                    CultureInfo.CurrentCulture = culture;
                    content = writer.ToString(true);
                }
                finally { CultureInfo.CurrentCulture = previous; }
                Assert.That(culture.Drains, Is.EqualTo(1));
                Assert.That(culture.DrainedText, Is.Empty);
                Assert.That(writer.NativeTypeUse, Is.Null);
                if (!capture) { baseline = content; baselineReads = culture.Reads; }
                else
                {
                    Assert.That(content, Is.EqualTo(baseline));
                    Assert.That(culture.Reads, Is.EqualTo(baselineReads));
                }
                if (companion) { Assert.That(writer.QualifiedContent, Is.EqualTo(content)); }
            }
        }

        [Test]
        public void PaidStateAndSingleSnapshotHaveFixedMeasuredStorage()
        {
            var (type, declaration, id) = Inputs();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var capture = new PocNativeTypeCapture(declaration, type, id, 0, 0) { Completed = true };
            long stateBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            var snapshot = capture.Publish(0, 0, 0, false);
            long snapshotBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(stateBytes, Is.GreaterThan(0));
            Assert.That(snapshotBytes, Is.GreaterThan(0));
            Assert.That(stateBytes + snapshotBytes, Is.LessThanOrEqualTo(PocNativeTypeCapture.SetupWork));
            Assert.That(typeof(PocNativeTypeCapture).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Length, Is.EqualTo(28));
            Assert.That(typeof(PocNativeTypeCaptureUse).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Length, Is.EqualTo(18));
            TestContext.WriteLine($"Paid state: {stateBytes} bytes, 28 fields; single snapshot: {snapshotBytes} bytes, 18 fields; setup: {PocNativeTypeCapture.SetupWork} + retained characters.");
        }

        [Test]
        public void SetupAndDisposalRaceHasOneBoundedDebitAndNoResurrection()
        {
            var (type, declaration, id) = Inputs();
            for (int i = 0; i < 64; i++)
            {
                using var writer = new CodeWriter();
                var budget = new PocWorkBudget(2000);
                Parallel.Invoke(
                    () => writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 27),
                    writer.Dispose);
                Assert.That(budget.Remaining, Is.AnyOf(2000, 2000 - PocNativeTypeCapture.SetupWork - 27));
                Assert.That(writer.NativeTypeUse, Is.Null);
                Assert.That(writer.BeginNativeTypeCapture(declaration), Is.Null);
                Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 27), Is.False);
            }
        }

        [Test]
        public void CompetingPublicationClaimsAllocateOnlyOneSnapshot()
        {
            var (type, declaration, id) = Inputs();
            for (int i = 0; i < 64; i++)
            {
                // Synthetic completed-slot protocol input, not formatter evidence.
                var capture = new PocNativeTypeCapture(declaration, type, id, 0, 0) { Completed = true };
                PocNativeTypeCaptureUse? first = null, second = null;
                Parallel.Invoke(
                    () => first = capture.Publish(0, 0, 0, false),
                    () => second = capture.Publish(0, 0, 0, false));
                Assert.That((first is not null) ^ (second is not null), Is.True);
                Assert.That(capture.Published, Is.SameAs(first ?? second));
                capture.Release();
                Assert.That(capture.Published, Is.SameAs(first ?? second));
            }
        }

        [Test]
        public void PublicationAndDisposalRaceCannotPublishAfterInvalidation()
        {
            var (type, declaration, id) = Inputs();
            for (int i = 0; i < 128; i++)
            {
                var capture = new PocNativeTypeCapture(declaration, type, id, 0, 0) { Completed = true };
                PocNativeTypeCaptureUse? result = null;
                Parallel.Invoke(
                    () => result = capture.Publish(0, 0, 0, false),
                    capture.Release);
                Assert.That(capture.Published, Is.SameAs(result));
                Assert.That(capture.Publish(0, 0, 0, false), Is.Null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AlreadyClaimedOrInvalidatedPublicationDoesNotAllocate(bool invalidate)
        {
            var (type, declaration, id) = Inputs();
            var capture = new PocNativeTypeCapture(declaration, type, id, 0, 0) { Completed = true };
            // Hold the exact paid publishing token to exercise the losing path.
            var publication = typeof(PocNativeTypeCapture).GetField("_publication", BindingFlags.Instance | BindingFlags.NonPublic)!;
            publication.SetValue(capture, capture);
            if (invalidate) { capture.Release(); capture.Completed = true; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = capture.Publish(0, 0, 0, false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, Is.Null);
            Assert.That(allocated, Is.Zero, "Claim rejection must precede snapshot allocation.");
            Assert.That(capture.Published, Is.Null);
            Assert.That(publication.GetValue(capture), Is.SameAs(invalidate ? type : capture));
        }

        private static (CSharpType Type, DeclarationExpression Declaration, SourceOccurrenceId Id) Inputs()
        {
            var type = new CSharpType("Foo", "Native.Types", false, false, null, Array.Empty<CSharpType>(), true, false);
            return (type, new DeclarationExpression(type, "value"),
                new SourceOccurrenceId(Guid.NewGuid(), "Capture.Review.cs", "M", "body", 0, "local"));
        }

        private static (string? Text, Type? Error, PocNativeTypeCaptureUse? Use, int CompareReads, int Disposals)
            FinalizeWithHeaderDisposal(bool capture, bool throws)
        {
            var type = new CSharpType("Foo", "Native.Types", false, false, null, Array.Empty<CSharpType>(), true, false);
            var declaration = new DeclarationExpression(type, "value");
            using var writer = new CodeWriter();
            if (capture)
            {
                var id = new SourceOccurrenceId(Guid.NewGuid(), "Capture.Review.cs", "M", "body", 0, "local");
                Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, new PocWorkBudget(10000), 500), Is.True);
            }
            writer.UseNamespace("Header.Alpha");
            writer.UseNamespace("Header.Beta");
            declaration.Write(writer);

            var previous = CultureInfo.CurrentCulture;
            var culture = new HeaderDisposingCulture(writer, throws);
            string? text = null;
            Type? error = null;
            try
            {
                CultureInfo.CurrentCulture = culture;
                try { text = writer.ToString(true); }
                catch (Exception exception) { error = exception.GetType(); }
            }
            finally { CultureInfo.CurrentCulture = previous; }
            return (text, error, writer.NativeTypeUse, culture.Reads, culture.Disposals);
        }

        private sealed class HeaderDisposingCulture(CodeWriter writer, bool throws) : CultureInfo("en-US")
        {
            internal int Reads;
            internal int Disposals;
            public override CompareInfo CompareInfo
            {
                get
                {
                    Reads++;
                    if (Disposals == 0)
                    {
                        Disposals++;
                        writer.Dispose();
                        if (throws) { throw new HeaderProbeException(); }
                    }
                    return base.CompareInfo;
                }
            }
        }

        private sealed class HeaderDrainingCulture(CodeWriter writer) : CultureInfo("en-US")
        {
            internal int Reads;
            internal int Drains;
            internal string? DrainedText;
            public override CompareInfo CompareInfo
            {
                get
                {
                    Reads++;
                    if (Drains == 0)
                    {
                        Drains++;
                        DrainedText = writer.ToString(false);
                    }
                    return base.CompareInfo;
                }
            }
        }

        private sealed class HeaderProbeException : Exception { }
    }
}

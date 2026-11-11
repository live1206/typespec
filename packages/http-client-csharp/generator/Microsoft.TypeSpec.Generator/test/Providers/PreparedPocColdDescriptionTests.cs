// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Utilities;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocColdDescriptionTests
    {
        [TestCase("Plain text")]
        [TestCase("- item")]
        [TestCase("1. item")]
        [TestCase("*italic*")]
        [TestCase("**bold**")]
        [TestCase("***both***")]
        [TestCase("a\r\nb\rc\nd")]
        [TestCase("a\u0085b\u2028c\u2029d")]
        [TestCase("{brace} & \"quote\" 'apostrophe'")]
        [TestCase("- a\ntext\n2. b")]
        [TestCase("1.\u2028item")]
        public void AdmittedCounterFitsActualNativeNormalizedEnvelope(string text)
        {
            var budget = new PocWorkBudget(1048576);
            Assert.That(Reserve(text, null, 0, null, budget), Is.True);
            var description = DocHelpers.GetFormattableDescription(text, null)!;
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([description]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null,
                "A count-only admission must fit the actual native normalization and final envelope.");
            Assert.That(budget.Remaining, Is.LessThan(1048576));
        }

        [Test]
        public void ColdDescriptionReservesEveryPotentiallyContendedRegexRunner()
        {
            // Every line invokes the numbered-list check and all three
            // emphasis match enumerators. Another thread can acquire each
            // shared regex's cached runner between calls, so each acquisition
            // may create a fresh runner even within this one description.
            const int lineCount = 46;
            var text = string.Join("\n", Enumerable.Repeat("a", lineCount));
            Assert.That(text.Length, Is.EqualTo(91));
            var phase = new PocWorkBudget(1048576);
            Assert.That(Reserve(null, text, 0, null, phase), Is.True,
                "This previously admitted description must remain supported under the existing limits.");
            var description = DocHelpers.GetFormattableDescription(null, text)!;
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(
                new XmlDocProvider(new XmlDocSummaryStatement([description]))), Is.Not.Null);

            // Pinned runtime source: a cold numbered runner allocates at least
            // 92 object/array-slot units; each cold emphasis runner at least
            // 94. This is a conservative matching-only lower bound, not a
            // simulation of thread scheduling or total-work sufficiency.
            const int matchingLowerBound = lineCount * (92 + 3 * 94);
            var spent = 1048576 - phase.Remaining;
            var priorValidation = 2 * text.Length + text.Length + (2 * text.Length + 1);
            var additionalFinalCaptureAllowance = 4096;
            const int separatelyReservedInputRecheck = 1;
            var reservedForNativeAndCapture = spent - priorValidation - separatelyReservedInputRecheck + additionalFinalCaptureAllowance;
            Assert.That(reservedForNativeAndCapture, Is.GreaterThanOrEqualTo(matchingLowerBound),
                "Independent runner cache acquisitions must be funded before native execution, without assuming serialization or warm runners.");
        }

        [Test]
        public void ColdDescriptionReservesInterpreterFallbackRunnerStorage()
        {
            // Compiled regexes can use the interpreter when dynamic compilation
            // is unavailable. This source-derived contention bound does not
            // pretend that the current test process executed that fallback.
            const int lineCount = 46;
            var text = string.Join("\n", Enumerable.Repeat("a", lineCount));
            Assert.That(text.Length, Is.EqualTo(91));
            var budget = new PocWorkBudget(1048576);
            Assert.That(Reserve(null, text, 0, null, budget), Is.True);
            var description = DocHelpers.GetFormattableDescription(null, text)!;
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(
                new XmlDocProvider(new XmlDocSummaryStatement([description]))), Is.Not.Null);
            var spent = 1048576 - budget.Remaining;
            var validation = 5 * text.Length + 1;
            const int upper = 138;
            const int normalizedLines = 46;
            var converter = 32 * (text.Length + upper + normalizedLines + 1);
            var reservedForRunnerStorage = spent - validation - converter - 4096 - 1;
            const int matchingStorageLowerBound = lineCount * (100 + 126 + 126 + 254);
            Assert.That(reservedForRunnerStorage, Is.GreaterThanOrEqualTo(matchingStorageLowerBound),
                "Independent runner-storage credits must also cover the pinned interpreter fallback without borrowing other reservations.");
        }

        [TestCase("a", 1, 0, 0, 3, 1, 0)]
        [TestCase("*x*", 1, 1, 0, 13, 1, 0)]
        [TestCase("**x**", 1, 1, 0, 15, 1, 0)]
        [TestCase("***x***", 1, 1, 0, 17, 1, 0)]
        [TestCase("1. x", 1, 0, 1, 71, 1, 0)]
        [TestCase("- ", 1, 0, 1, 69, 1, 0)]
        [TestCase("a\r\n", 2, 0, 0, 6, 2, 0)]
        [TestCase(" \n", 2, 0, 0, 5, 2, 0)]
        [TestCase("a\u2028b", 1, 0, 0, 7, 2, 0)]
        [TestCase("a&>", 1, 0, 0, 5, 1, 2)]
        public void ColdDescriptionPrepaysRunnerStorageSeparately(string text, int nativeLines, int emphasizedLines,
            int items, int upper, int normalizedLines, int replacements)
        {
            var actualDocs = new XmlDocProvider(new XmlDocSummaryStatement([DocHelpers.GetFormattableDescription(null, text)!]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(actualDocs), Is.Not.Null);
            var budget = new PocWorkBudget(1048576);
            Assert.That(Reserve(null, text, 0, null, budget), Is.True);
            var compiledRunnerStorage = 383 * nativeLines + 97 * emphasizedLines + 104 * items + 2 * text.Length;
            var interpreterSupplement = 288 * nativeLines + 176 * emphasizedLines + 80 * items;
            var runnerStorage = compiledRunnerStorage + interpreterSupplement;
            var converter = 32 * (text.Length + upper + normalizedLines + 1);
            var validation = 5 * text.Length + 1;
            var replacementTriangle = replacements * (replacements + 1) / 2;
            var required = validation + converter + runnerStorage + 4096 + 1 + replacementTriangle;
            Assert.That(1048576 - budget.Remaining, Is.EqualTo(required),
                "Runner storage is additive; conversion, normalization, collision probes and recheck remain separately reserved.");
            Assert.That(Reserve(null, text, 0, null, new PocWorkBudget(required)), Is.True);
            Assert.That(Reserve(null, text, 0, null, new PocWorkBudget(required - 1)), Is.False);
        }

        [Test]
        public void ServiceClientNativeListDescriptionKeepsOriginalPositive()
        {
            // Exact description from the unchanged real-Foo input fixture.
            const string text = "Test that we can use @client decorators to customize client side code structure, such as:\n" +
                "1. have everything as default.\n" +
                "2. to rename client or operation group\n" +
                "3. one client can have more than one operations groups\n" +
                "4. split one interface into two clients\n" +
                "5. have two clients with operations come from different interfaces\n" +
                "6. have two clients with a hierarchy relation.";
            var description = DocHelpers.GetFormattableDescription(null, text)!;
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([description]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null,
                "The actual native summary is a bounded final-envelope positive.");
            Assert.That(Reserve(null, text, 0, null, new PocWorkBudget(1048576)), Is.True,
                "Preserve the original all-39 ServiceClient header without raising either 4096 limit.");
        }

        [Test]
        public void AdmittedListGroupCombinationsFitNativeEnvelope()
        {
            string[] parts = ["plain", "- a", "1. b", "", "*c*", "- d&x", "2. e>y", "- f\u2028g"];
            int admitted = 0;
            foreach (var first in parts)
            foreach (var second in parts)
            foreach (var third in parts)
            foreach (var separator in new[] { "\n", "\r\n" })
            {
                var text = first + separator + second + separator + third;
                if (!Reserve(null, text, 0, null, new PocWorkBudget(1048576))) { continue; }
                admitted++;
                var docs = new XmlDocProvider(new XmlDocSummaryStatement([DocHelpers.GetFormattableDescription(null, text)!]));
                Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null,
                    "Every admitted alternating/same-kind/empty/escaping/Unicode group combination must fit native output.");
            }
            Assert.That(admitted, Is.GreaterThan(0));
        }

        [TestCase("*unclosed")]
        [TestCase("**a* mismatch")]
        [TestCase("*a* and *b*")]
        [TestCase("******")]
        [TestCase("<see broken")]
        public void UnsupportedOrMalformedInputVetoesWithoutConversion(string text)
        {
            Assert.That(Reserve(text, null, 0, null, new PocWorkBudget(1048576)), Is.False);
        }

        [Test]
        public void NonemptyDocSelectsNativeValueAndBothRawScansAreFunded()
        {
            var summary = new string('x', 4096);
            const string doc = "short doc";
            var budget = new PocWorkBudget(1048576);
            Assert.That(Reserve(summary, doc, 0, null, budget), Is.True);
            Assert.That(DocHelpers.GetDescription(summary, doc), Is.EqualTo(doc));
            Assert.That(1048576 - budget.Remaining, Is.GreaterThanOrEqualTo(2 * (summary.Length + doc.Length)));
            var unfunded = new PocWorkBudget(summary.Length + doc.Length);
            Assert.That(Reserve(summary, doc, 0, null, unfunded), Is.False);
            Assert.That(unfunded.Exhausted, Is.True);
        }

        [TestCase(200, 1)]
        [TestCase(17, 17)]
        public void FinalHeaderReviewColdCounterReservesReplacementProbeChains(int count, int stride)
        {
            var text = Enumerable.Repeat('a', (count - 1) * stride + 1).ToArray();
            var positions = new System.Collections.Generic.Dictionary<int, string>();
            for (int i = 0; i < count; i++)
            {
                text[i * stride] = '&';
                positions.Add(i * stride, "&amp;");
            }
            var capacity = positions.EnsureCapacity(0);
            if (stride > 1)
            {
                Assert.That(capacity, Is.EqualTo(stride));
                Assert.That(positions.Keys.All(position => position % capacity == 0), Is.True,
                    "The native escape helper uses these exact integer positions and the same default dictionary growth.");
            }
            var input = new string(text);
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([DocHelpers.GetFormattableDescription(null, input)!]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null, "Retain the actual native escaped positive.");
            var budget = new PocWorkBudget(1048576);
            Assert.That(Reserve(input, null, 0, null, budget), Is.True);
            // Deliberately conservative for dense positions: this is an
            // explicit reservation test, not a claim that every set collides.
            var worstProbes = count * (count + 1) / 2;
            Assert.That(1048576 - budget.Remaining, Is.GreaterThanOrEqualTo(worstProbes));
            TestContext.WriteLine($"REPLACEMENT-PROBES count={count}; capacity={capacity}; work={1048576 - budget.Remaining}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalHeaderReviewWarmDescriptionReservesReplacementProbeChains(bool escaped)
        {
            var text = new string(escaped ? '&' : 'a', escaped ? 200 : 300);
            var description = System.Runtime.CompilerServices.FormattableStringFactory.Create("{0}", text);
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([description]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null, "Native normalization remains supported without early preparation.");
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(description) is not null, Is.EqualTo(!escaped),
                "A bounded warm preflight must fund replacement insertion chains before native normalization, without raising its 4096 limit.");
        }

        [TestCase("\u200B")]
        [TestCase("\u00AD")]
        [SetCulture("en-US")]
        public void FinalHeaderReviewColdCounterMatchesNativeCultureSensitiveBulletPrefix(string ignorable)
        {
            var line = "-" + ignorable + " ";
            Assert.That(line.StartsWith("- "), Is.True, "Establish actual native classification under this explicit culture.");
            var text = string.Concat(Enumerable.Repeat(line + "\n", 36));
            var description = DocHelpers.GetFormattableDescription(null, text)!;
            var nativeText = DocHelpers.GetDescription(null, text)!;
            Assert.That(nativeText, Does.Contain("<list"));
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([description]));
            TestContext.WriteLine($"NATIVE-CULTURE description={nativeText.Length}; fileEnvelope={PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs) is not null}");
            Assert.That(Reserve(text, null, 0, null, new PocWorkBudget(1048576)), Is.False,
                "A counter using ordinal prefix detection cannot certify this native list expansion.");
        }

        [TestCase("")]
        [TestCase("en-US")]
        [TestCase("tr-TR")]
        [TestCase("de-DE")]
        [TestCase("ja-JP")]
        [TestCase("ar-SA")]
        public void CulturePrefixMatchesNativeStringAndAdmittedNormalizedEnvelope(string cultureName)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                var culture = cultureName.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                string[] inputs = ["plain", "π 日本語 😀", "- item", "-\u200B item", "-\u00AD item", "\u200B- item",
                    "- \u200B", "1. item", "١. item", "plain\n- x\n2. y", "- a\r\ntext\r1. z", "-\u200B \n-\u200B ",
                    "**bold**", "{braces} & >", " \t- item", "1.\u2028item"];
                var admitted = 0;
                foreach (var input in inputs)
                {
                    var firstLine = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)[0].TrimStart();
                    Assert.That(culture.CompareInfo.IsPrefix(firstLine.AsSpan(), "- ".AsSpan(), CompareOptions.None),
                        Is.EqualTo(firstLine.StartsWith("- ")), "The span API must match the actual native string overload.");
                    if (!Reserve(null, input, 0, null, new PocWorkBudget(1048576))) { continue; }
                    admitted++;
                    var description = DocHelpers.GetFormattableDescription(null, input)!;
                    var docs = new XmlDocProvider(new XmlDocSummaryStatement([description]));
                    Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null,
                        "Admitted Unicode/culture/list/escaping inputs must still fit actual native normalization.");
                }
                Assert.That(admitted, Is.EqualTo(inputs.Length), "Preserve the small supported native cases, including ignorable bullet prefixes.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void CultureDependencyRechecksIdentityWithoutClaimingSerialization()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                var first = new CultureInfo("en-US");
                CultureInfo.CurrentCulture = first;
                Assert.That(ReserveWithCulture("plain", true, new PocWorkBudget(1048576), out var captured), Is.True);
                Assert.That(ReferenceEquals(captured, first), Is.True);
                Assert.That(PreparedPocColdDescription.IsCultureCurrent(captured), Is.True);
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Assert.That(PreparedPocColdDescription.IsCultureCurrent(captured), Is.False, "Same culture name is not the captured identity.");
                CultureInfo.CurrentCulture = first;
                Assert.That(PreparedPocColdDescription.IsCultureCurrent(captured), Is.True, "This is explicitly a point check, not an ABA or locking guarantee.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void CustomCultureIsRejectedBeforeVirtualDispatchAndUnfundedComparison()
        {
            var previous = CultureInfo.CurrentCulture;
            var culture = new ColdDescriptionCultureProbe();
            try
            {
                CultureInfo.CurrentCulture = culture;
                culture.Calls = 0; // Do not count the framework's culture setter.
                var unfunded = new PocWorkBudget(25); // plain: 2N validation + N scan + (2N+1) comparisons.
                Assert.That(ReserveWithCulture("plain", true, unfunded, out _), Is.False);
                Assert.That(unfunded.Exhausted, Is.True);
                Assert.That(culture.Calls, Is.Zero);
                var funded = new PocWorkBudget(1048576);
                Assert.That(ReserveWithCulture("plain", true, funded, out _), Is.False);
                Assert.That(funded.Exhausted, Is.False, "The exact-type guard, not lack of funding, rejects custom dispatch.");
                Assert.That(culture.Calls, Is.Zero);
                Assert.That(ReserveWithCulture("plain", false, new PocWorkBudget(1048576), out var warmCulture), Is.True);
                Assert.That(warmCulture, Is.Null, "A cached description does not run Markdown conversion or introduce a culture dependency.");
                Assert.That(culture.Calls, Is.Zero);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [TestCase("*italic*", "<i>italic</i>")]
        [TestCase("**bold**", "<b>bold</b>")]
        [TestCase("***both***", "<b><i>both</i></b>")]
        [TestCase("- *italic*", "<list type=\"bullet\"><item><description><i>italic</i></description></item></list>")]
        [TestCase("1. **bold**", "<list type=\"number\"><item><description><b>bold</b></description></item></list>")]
        [TestCase("- *first*\n- **second**", "<list type=\"bullet\"><item><description><i>first</i></description></item><item><description><b>second</b></description></item></list>")]
        [NonParallelizable]
        public void AdmittedNativeEmphasisDoesNotDispatchPoolListener(string text, string expected)
        {
            Assert.That(Reserve(null, text, 0, null, new PocWorkBudget(1048576)), Is.True);
            // Warm definition initialization and matching outside the observed
            // interval; the regression concerns replacement, not startup.
            Assert.That(DocHelpers.GetDescription(null, text), Is.EqualTo(expected));
            var control = new Regex(@"(?<!\*)\*([^*]+?)\*(?!\*)", RegexOptions.Compiled);
            Assert.That(control.Replace("*control*", "<i>$1</i>"), Is.EqualTo("<i>control</i>"));
            using var listener = new ReplacementPoolListener();
            listener.Arm();
            try { _ = control.Replace("*control*", "<i>$1</i>"); }
            finally { listener.Stop(); }
            Assert.That(listener.SegmentRents, Is.GreaterThan(0), "The installed runtime control must reach the enabled 256-element replacement-pool event.");
            listener.Arm();
            string? actual;
            try { actual = DocHelpers.GetDescription(null, text); }
            finally { listener.Stop(); }
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(listener.Rents, Is.Zero, "Admitted native emphasis must not execute a managed pool-listener callback through replacement assembly.");
        }

        private sealed class ReplacementPoolListener : EventListener
        {
            private int _thread;
            private int _rents;
            private int _segmentRents;
            internal int Rents => Volatile.Read(ref _rents);
            internal int SegmentRents => Volatile.Read(ref _segmentRents);
            internal void Arm()
            {
                _rents = 0;
                _segmentRents = 0;
                Volatile.Write(ref _thread, Environment.CurrentManagedThreadId);
            }
            internal void Stop() => Volatile.Write(ref _thread, 0);
            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
                { EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All); }
            }
            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                // No logging, formatting or allocation inside a pool callback.
                if (Volatile.Read(ref _thread) != Environment.CurrentManagedThreadId || eventData.EventId != 1) { return; }
                Interlocked.Increment(ref _rents);
                if (eventData.Payload is { Count: > 1 } payload && payload[1] is int size && size == 256)
                { Interlocked.Increment(ref _segmentRents); }
            }
        }

        private sealed class ColdDescriptionCultureProbe() : CultureInfo("en-US")
        {
            internal int Calls;
            public override string Name { get { Calls++; return base.Name; } }
            public override CompareInfo CompareInfo { get { Calls++; return base.CompareInfo; } }
        }

        private static bool ReserveWithCulture(string text, bool cold, PocWorkBudget budget, out CultureInfo? culture)
        {
            var method = typeof(PreparedPocColdDescription).GetMethod("TryReserveWithCulture", BindingFlags.NonPublic | BindingFlags.Static)!;
            var type = method.GetParameters()[0].ParameterType;
            var inputs = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object?[] { text, null, 0, null }, null)!;
            object?[] arguments = [inputs, cold, budget, null];
            var result = (bool)method.Invoke(null, arguments)!;
            culture = (CultureInfo?)arguments[3];
            return result;
        }

        // The Input bridge intentionally has no friend grant for this test
        // assembly. Exercise its internal fact record through the core entry
        // point's actual parameter type rather than widening that contract.
        private static bool Reserve(string? summary, string? doc, int fallback, string? name, PocWorkBudget budget)
        {
            var method = typeof(PreparedPocColdDescription).GetMethod("TryReserve", BindingFlags.NonPublic | BindingFlags.Static)!;
            var type = method.GetParameters()[0].ParameterType;
            var inputs = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object?[] { summary, doc, fallback, name }, null)!;
            return (bool)method.Invoke(null, new object[] { inputs, true, budget })!;
        }

        [Test]
        public void EmptyInputFallbackAndNearLimitCountsStayBounded()
        {
            Assert.That(Reserve(null, string.Empty, 20, "Foo", new PocWorkBudget(1048576)), Is.True);
            Assert.That(Reserve(null, null, 5000, "Foo", new PocWorkBudget(1048576)), Is.False);
            Assert.That(Reserve(new string('x', 300), null, 0, null, new PocWorkBudget(1048576)), Is.True);
            Assert.That(Reserve(new string('x', 340), null, 0, null, new PocWorkBudget(1048576)), Is.False);
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class PreparedPocHeaderFollowupRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DocumentationPreflightDoesNotDispatchCustomNumericCulture(bool throws)
        {
            MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(
                [FormattableStringFactory.Create("payload {0}", "value")]));
            var original = CultureInfo.CurrentCulture;
            var culture = new NumericCultureProbe(throws);
            Exception? nativeError = null;
            int nativeCalls;
            // Cached native XML writing does not rerun normalization, but its
            // composite formatter still parses indices. Check both native paths.
            using (var writer = new CodeWriter())
            {
                try
                {
                    CultureInfo.CurrentCulture = culture;
                    writer.WriteXmlDocsNoScope(docs);
                }
                catch (Exception error) { nativeError = error; }
                finally { CultureInfo.CurrentCulture = original; }
                nativeCalls = culture.Calls;
            }

            culture.Calls = 0;
            Exception? normalizationError = null;
            try
            {
                CultureInfo.CurrentCulture = culture;
                _ = new XmlDocSummaryStatement([FormattableStringFactory.Create("payload {0}", "value")]);
            }
            catch (Exception error) { normalizationError = error; }
            finally { CultureInfo.CurrentCulture = original; }
            var normalizationCalls = culture.Calls;

            culture.Calls = 0;
            Exception? preparationError = null;
            PreparedGeneratedDocumentationEnvelope? envelope = null;
            try
            {
                CultureInfo.CurrentCulture = culture;
                envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs);
            }
            catch (Exception error) { preparationError = error; }
            finally { CultureInfo.CurrentCulture = original; }
            var preparationCalls = culture.Calls;

            TestContext.Progress.WriteLine($"Numeric culture callbacks: native={nativeCalls}, normalization={normalizationCalls}, preflight={preparationCalls}");
            Assert.That(nativeCalls, Is.GreaterThan(0), "native composite formatting must reach the culture probe");
            Assert.That(nativeError is InvalidOperationException, Is.EqualTo(throws));
            Assert.That(normalizationCalls, Is.GreaterThan(0), "native cold normalization must actually reach the probe");
            Assert.That(normalizationError is InvalidOperationException, Is.EqualTo(throws));
            Assert.That(preparationError, Is.Null);
            Assert.That(preparationCalls, Is.Zero, "metadata-only file preflight must not invoke a foreign culture");
            Assert.That(envelope, Is.Not.Null, "numeric ASCII format indices need no culture dispatch");
        }

        [Test]
        public void DisposedHeaderLeaseDoesNotRetainGenerator()
        {
            var nativeControl = MakeGeneratorLease(false);
            var disposedLease = MakeGeneratorLease(true);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.That(nativeControl.Generator.IsAlive, Is.False, "the isolated generator must otherwise be collectible");
            Assert.That(disposedLease.Lease!.Count, Is.Zero);
            Assert.That(disposedLease.Generator.IsAlive, Is.False, "disposal must release transitive ownership, not just header entries");
            GC.KeepAlive(disposedLease.Lease);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference Generator, PreparedPocHeaderFinalization? Lease) MakeGeneratorLease(bool retainLease)
        {
            // No singleton assignment or configuration load: the only retained
            // strong edge in this control is the explicitly returned lease.
            var generator = new IsolatedGenerator();
            var lease = new PreparedPocHeaderFinalization(generator);
            lease.Dispose();
            return (new WeakReference(generator), retainLease ? lease : null);
        }

        private sealed class IsolatedGenerator : CodeModelGenerator { }

        private sealed class NumericCultureProbe(bool throws) : CultureInfo("en-US")
        {
            internal int Calls;
            public override NumberFormatInfo NumberFormat
            {
                get
                {
                    Calls++;
                    if (throws) { throw new InvalidOperationException("foreign numeric culture getter"); }
                    return base.NumberFormat;
                }
                set => base.NumberFormat = value;
            }

            public override object? GetFormat(Type? formatType)
            {
                Calls++;
                if (throws) { throw new InvalidOperationException("foreign numeric culture formatter"); }
                return base.GetFormat(formatType);
            }
        }
    }
}

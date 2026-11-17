// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    [NonParallelizable]
    public class PreparedPocHeaderFinalizationTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator(configuration: "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Header.Tests\"}");

        private sealed class UnknownHeaderOwner : TestTypeProvider
        {
            internal int Calls { get; private set; }
            protected override XmlDocProvider BuildXmlDocs() { Calls++; throw new InvalidOperationException("Must remain native."); }
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() { Calls++; throw new InvalidOperationException("Must remain native."); }
        }

        private static void SetCachedHeaderIdentity(TypeProvider owner, TypeSignatureModifiers modifiers)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Isolate dispatch/materialization from unrelated lazy type building.
            typeof(TypeProvider).GetField("_type", flags)!.SetValue(owner, new CSharpType(typeof(object)));
            typeof(TypeProvider).GetField("_declarationModifiers", flags)!.SetValue(owner, modifiers);
        }

        [Test]
        public void UnknownHeaderBuildersAreNotInvoked()
        {
            var owner = new UnknownHeaderOwner();
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Public | TypeSignatureModifiers.Class);
            var phase = new PreparedPocHeaderFinalization();
            phase.Materialize(owner, null);
            Assert.That(phase.Count, Is.Zero);
            Assert.That(owner.Calls, Is.Zero);
            Assert.That(owner.CachedXmlDocs, Is.Null);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
        }

        [Test]
        public void RawHeaderDescriptionCountsEachExpansionOccurrence()
        {
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(
                System.Runtime.CompilerServices.FormattableStringFactory.Create(string.Empty, Array.Empty<object>()))?.IsCurrent, Is.True);
            var text = new string('a', 200);
            var once = System.Runtime.CompilerServices.FormattableStringFactory.Create("{0}", text);
            var repeated = System.Runtime.CompilerServices.FormattableStringFactory.Create("{0}{0}", text);
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(once)?.IsCurrent, Is.True);
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(repeated), Is.Null,
                "Raw source length is not a bound on repeated normalization work.");
            var nested = System.Runtime.CompilerServices.FormattableStringFactory.Create("({0})", once);
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(nested)?.IsCurrent, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocSuppressionRejectsWrappersThatUseNativeRecordFormatting(bool nested)
        {
            ValueExpression literal = new LiteralExpression("CS0618");
            ValueExpression code = nested
                ? new Microsoft.TypeSpec.Generator.Snippets.ScopedApi<string>(new Microsoft.TypeSpec.Generator.Snippets.ScopedApi<string>(literal))
                : new Microsoft.TypeSpec.Generator.Snippets.ScopedApi(typeof(string), literal);
            SuppressionStatement[] clean = [new SuppressionStatement(null, literal, "fixture")];
            Assert.That(PreparedSuppressionEnvelope.TryCaptureForPoc(clean, new PocWorkBudget(4096)), Is.Not.Null);
            SuppressionStatement[] warnings = [new SuppressionStatement(null, code, "fixture")];
            var envelope = PreparedSuppressionEnvelope.TryCaptureForPoc(warnings, new PocWorkBudget(4096));
            Assert.That(envelope, Is.Null, "The native pragma extracts only a direct literal or ONE ScopedApi<string> around a literal.");
        }

        private static PreparedSuppressionEnvelope? CaptureObservedPocWarnings(System.Collections.Generic.IReadOnlyList<SuppressionStatement> warnings)
            => (PreparedSuppressionEnvelope?)typeof(PreparedFieldTypes).GetMethod("TryCapturePocFileWarnings", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [warnings]);

        [Test]
        public void PocWarningObservationBoundsCachedPayloadBeforeCopyAndScan()
        {
            SuppressionStatement[] warnings = [new SuppressionStatement(null, new LiteralExpression("CS0618"), new string('a', 300000))];
            Assert.That(CaptureObservedPocWarnings(warnings), Is.Null);
        }

        [Test]
        public void PocWarningObservationBoundsGrownListCurrentness()
        {
            var suppression = new SuppressionStatement(null, new LiteralExpression("CS0618"), "fixture");
            var values = new System.Collections.Generic.List<SuppressionStatement> { suppression };
            var envelope = CaptureObservedPocWarnings(values)!;
            Assert.That(envelope.IsCurrent, Is.True);
            for (var i = 0; i < 100000; i++) { values.Add(suppression); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var current = envelope.IsCurrent;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(current, Is.False);
            Assert.That(allocated, Is.LessThan(65536), "The actual POC observation helper must also refuse grown-list copies before allocation.");
        }

        [Test]
        public void PocSuppressionCurrentnessDoesNotCopyAnUnboundedGrownList()
        {
            var suppression = new SuppressionStatement(null, new LiteralExpression("CS0618"), "fixture");
            var values = new System.Collections.Generic.List<SuppressionStatement> { suppression };
            var envelope = PreparedSuppressionEnvelope.TryCaptureForPoc(values, new PocWorkBudget(4096))!;
            Assert.That(envelope.IsCurrent, Is.True);
            for (var i = 0; i < 100000; i++) { values.Add(suppression); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var current = envelope.IsCurrent;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(current, Is.False);
            Assert.That(allocated, Is.LessThan(65536), "A stale large list must be rejected before allocating a snapshot of its elements.");
        }

        [Test]
        public void OversizedCachedWarningIsRejectedBeforeSnapshotScanning()
        {
            var owner = new CodeGenTypeAttributeDefinition();
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
            var warnings = new[] { new SuppressionStatement(null, new LiteralExpression("CS0618"), new string('a', 300000)) };
            typeof(TypeProvider).GetField("_disabledFileWarnings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, warnings);
            var phase = new PreparedPocHeaderFinalization();
            phase.Materialize(owner, null);
            Assert.That(phase.Count, Is.Zero, "Bounded finalization must reject the oversized cached payload rather than scan it.");
            Assert.That(owner.CachedDisabledFileWarnings, Is.SameAs(warnings));
        }

        private sealed class LiteralWarningOwner(SuppressionStatement[] warnings) : TestTypeProvider
        {
            internal int Calls { get; private set; }
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() { Calls++; return warnings; }
        }

        [Test]
        public void WriterFreeLiteralWarningMergePreservesNativeFirstIdentityAndOnceOnlyBuilder()
        {
            var first = new SuppressionStatement(null, new LiteralExpression("CS0618"), "first");
            var duplicate = new SuppressionStatement(null, new LiteralExpression("CS0618"), "duplicate");
            var other = new SuppressionStatement(null, new LiteralExpression("CS0168"), "other");
            SuppressionStatement[] input = [first, duplicate, other];
            var owner = new LiteralWarningOwner(input);
            Assert.That(owner.TryFinalizePocFileWarnings(new PocWorkBudget(4096), out var warnings, out var envelope), Is.True);
            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(warnings[0], Is.SameAs(first));
            Assert.That(warnings[1], Is.SameAs(other));
            Assert.That(envelope!.IsCurrent, Is.True);
            Assert.That(owner.DisabledFileWarnings, Is.SameAs(warnings));
            Assert.That(owner.Calls, Is.EqualTo(1));
            var legacy = new LiteralWarningOwner(input);
            Assert.That(legacy.DisabledFileWarnings, Is.EqualTo(warnings));
            Assert.That(legacy.Calls, Is.EqualTo(1));
        }

        [Test]
        public void WriterFreeWrappedLiteralMergePreservesNativeIdentityAndVetoesPayloadMutation()
        {
            var code = Microsoft.TypeSpec.Generator.Snippets.Snippet.Literal("CS0618");
            var first = new SuppressionStatement(null, code, "first");
            var duplicate = new SuppressionStatement(null, Microsoft.TypeSpec.Generator.Snippets.Snippet.Literal("CS0618"), "duplicate");
            SuppressionStatement[] input = [first, duplicate];
            var owner = new LiteralWarningOwner(input);
            Assert.That(owner.TryFinalizePocFileWarnings(new PocWorkBudget(4096), out var warnings, out var envelope), Is.True);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Is.SameAs(first));
            Assert.That(owner.Calls, Is.EqualTo(1));
            Assert.That(envelope!.IsCurrent, Is.True);
            var legacy = new LiteralWarningOwner(input);
            Assert.That(legacy.DisabledFileWarnings, Is.EqualTo(warnings));
            code.Update(original: new LiteralExpression("CS0168"));
            Assert.That(envelope.IsCurrent, Is.False, "A retained transparent wrapper is not permanently trusted by its initial payload.");
            Assert.That(owner.DisabledFileWarnings, Is.SameAs(warnings));
            Assert.That(owner.Calls, Is.EqualTo(1));
        }

        [Test]
        public void WrappedUnknownWarningExpressionRemainsNativeWithoutEarlyFormatting()
        {
            var probe = new WarningWriterProbe();
            var code = new Microsoft.TypeSpec.Generator.Snippets.ScopedApi(typeof(string), probe);
            var owner = new LiteralWarningOwner([new SuppressionStatement(null, code, "fixture")]);
            Assert.That(owner.TryFinalizePocFileWarnings(new PocWorkBudget(4096), out _, out _), Is.False);
            Assert.That(probe.Calls, Is.Zero);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
            _ = owner.DisabledFileWarnings;
            Assert.That(probe.Calls, Is.EqualTo(1));
        }

        private sealed record WarningWriterProbe : ValueExpression
        {
            internal int Calls { get; private set; }
            internal override void WriteCore(CodeWriter writer) { Calls++; writer.AppendRaw("CS0618"); }
        }

        private sealed class AuditedWarningOwner(WarningWriterProbe probe) : TestTypeProvider
        {
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() =>
                [new SuppressionStatement(null, probe, "fixture")];
        }

        [Test]
        public void FinalizationDoesNotInvokeWarningDisplayWriter()
        {
            var probe = new WarningWriterProbe();
            var owner = new AuditedWarningOwner(probe);
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
            // A test-only registration isolates the warning merge path. The
            // unknown expression must be refused BEFORE it can execute a writer.
            var bridge = typeof(InputLibrary).Assembly.GetType("Microsoft.TypeSpec.Generator.Input.PocHeaderFinalizationBridge")!;
            var auditType = bridge.GetNestedType("Audit", BindingFlags.NonPublic)!;
            var audits = bridge.GetField("_audits", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var assemblies = bridge.GetField("_assemblies", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var assembly = typeof(AuditedWarningOwner).Assembly;
            var alreadyRegistered = (bool)bridge.GetMethod("IsRegisteredAssembly", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [assembly])!;
            var audit = auditType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6).Invoke([typeof(TypeProvider), typeof(TypeProvider), typeof(AuditedWarningOwner), new Func<object, InputNamespace?, bool>(static (_, _) => true), null, null]);
            bridge.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [typeof(AuditedWarningOwner), audit]);
            try
            {
                var phase = new PreparedPocHeaderFinalization();
                phase.Materialize(owner, null);
                Assert.That(probe.Calls, Is.Zero, "Warning merging must not call ToDisplayString or construct its writer in the finalization phase.");
                Assert.That(phase.Count, Is.Zero, "Unknown warning expression data remains a legacy native path.");
                Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
                _ = owner.DisabledFileWarnings;
                Assert.That(probe.Calls, Is.EqualTo(1), "The normal legacy getter still executes its native display formatter.");
            }
            finally
            {
                audits.GetType().GetMethod("Remove", [typeof(Type)])!.Invoke(audits, [typeof(AuditedWarningOwner)]);
                if (!alreadyRegistered) { assemblies.GetType().GetMethod("Remove", [typeof(System.Reflection.Assembly)])!.Invoke(assemblies, [assembly]); }
            }
        }

        [Test]
        public void FinalizedHeaderDoesNotIgnoreChangedDocumentationPolicy()
        {
            var owner = new CodeGenTypeAttributeDefinition();
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
            var phase = new PreparedPocHeaderFinalization();
            phase.Materialize(owner, null);
            Assert.That(phase.Get(owner)!.IsCurrent, Is.True);
            owner.PreserveXmlDocs();
            Assert.That(phase.Get(owner)!.IsCurrent, Is.False,
                "A finalized no-documentation header cannot remain current after documentation becomes requested.");
        }

        private sealed class UnknownCachedAttributeOwner : TestTypeProvider
        {
            internal int Calls { get; private set; }
            protected internal override bool ShouldAnalyzeAttributesInReferenceMap => false;
            protected override IReadOnlyList<MethodBodyStatement> BuildAttributes()
            {
                Calls++;
                return [new AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], [])];
            }
        }

        [Test]
        public void CachedUnknownAttributeBuilderRetainsLegacyRebuildingWithoutEarlyInvocation()
        {
            var owner = new UnknownCachedAttributeOwner();
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
            _ = owner.GetAttributesForWrite();
            var calls = owner.Calls;
            var bridge = typeof(InputLibrary).Assembly.GetType("Microsoft.TypeSpec.Generator.Input.PocHeaderFinalizationBridge")!;
            var auditType = bridge.GetNestedType("Audit", BindingFlags.NonPublic)!;
            var audits = bridge.GetField("_audits", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var assemblies = bridge.GetField("_assemblies", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var assembly = owner.GetType().Assembly;
            var alreadyRegistered = (bool)bridge.GetMethod("IsRegisteredAssembly", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [assembly])!;
            var audit = auditType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == 6)
                .Invoke([typeof(TypeProvider), typeof(TypeProvider), typeof(TypeProvider), new Func<object, InputNamespace?, bool>(static (_, _) => true), null, null]);
            bridge.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [owner.GetType(), audit]);
            try
            {
                var phase = new PreparedPocHeaderFinalization();
                phase.Materialize(owner, null);
                Assert.That(phase.Get(owner), Is.Not.Null);
                Assert.That(phase.Get(owner)!.Attributes, Is.Null);
                Assert.That(phase.Get(owner)!.CanUseCanonicalAttributes, Is.False);
                Assert.That(owner.Calls, Is.EqualTo(calls), "A cached unknown attribute builder must not run during finalization.");
                _ = owner.GetAttributesForWrite();
                Assert.That(owner.Calls, Is.EqualTo(calls + 1), "Its normal native rebuilding remains available.");
            }
            finally
            {
                audits.GetType().GetMethod("Remove", [typeof(Type)])!.Invoke(audits, [owner.GetType()]);
                if (!alreadyRegistered) { assemblies.GetType().GetMethod("Remove", [typeof(System.Reflection.Assembly)])!.Invoke(assemblies, [assembly]); }
            }
        }

        [Test]
        public void AuditedInternalHeaderMaterializesOnceWithoutTypeDocs()
        {
            var owner = new CodeGenTypeAttributeDefinition();
            SetCachedHeaderIdentity(owner, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
            var phase = new PreparedPocHeaderFinalization();
            phase.Materialize(owner, null);
            var warnings = owner.CachedDisabledFileWarnings;
            Assert.That(warnings, Is.Not.Null);
            Assert.That(owner.CachedXmlDocs, Is.Null);
            Assert.That(phase.Get(owner)!.IsCurrent, Is.True);
            phase.Materialize(owner, null);
            Assert.That(phase.Count, Is.EqualTo(1));
            Assert.That(owner.CachedDisabledFileWarnings, Is.SameAs(warnings));
            typeof(TypeProvider).GetField("_disabledFileWarnings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(owner, new SuppressionStatement[1]);
            Assert.That(phase.Get(owner)!.IsCurrent, Is.False);
        }
    }
}

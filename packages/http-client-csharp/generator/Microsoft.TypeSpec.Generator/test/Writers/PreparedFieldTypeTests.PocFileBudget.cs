// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void PocOwnerRecheckRejectsFieldsUnavailableBeforeLazyMaterialization()
        {
            var owner = new TestTypeProvider();
            _ = owner.Type;
            Assert.That(owner.CachedFields, Is.Null);
            var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
            Assert.That(snapshot.IsCurrent(), Is.True, "The shared baseline intentionally does not force the fields getter.");
            Assert.That(snapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False,
                "A POC observation cannot certify an unavailable field collection.");
            _ = owner.Fields;
            Assert.That(snapshot.IsCurrent(), Is.True, "The POC-only requirement must not change the shared snapshot.");
            Assert.That(snapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False);
        }

        [Test]
        public void PocOwnerRecheckRejectsUnknownFieldCarrierWithoutExecutingIt()
        {
            var owner = new TestTypeProvider();
            _ = owner.Type;
            typeof(TypeProvider).GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, new UnknownFieldCarrier());
            var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
            Assert.That(snapshot.IsCurrent(), Is.True, "The shared baseline still ignores unsupported field carriers.");
            Assert.DoesNotThrow(() => Assert.That(snapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False));
        }

        private sealed class UnknownFieldCarrier : IReadOnlyList<FieldProvider>
        {
            public int Count => throw new InvalidOperationException("Unknown field carrier must not execute.");
            public FieldProvider this[int index] => throw new InvalidOperationException("Unknown field carrier must not execute.");
            public IEnumerator<FieldProvider> GetEnumerator() => throw new InvalidOperationException("Unknown field carrier must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [TestCase("_fields", false)]
        [TestCase("_fields", true)]
        [TestCase("_properties", false)]
        [TestCase("_properties", true)]
        [TestCase("_methods", false)]
        [TestCase("_methods", true)]
        [TestCase("_constructors", false)]
        [TestCase("_constructors", true)]
        [TestCase("_nestedTypes", false)]
        [TestCase("_nestedTypes", true)]
        [TestCase("_serializationProviders", false)]
        [TestCase("_serializationProviders", true)]
        [TestCase("_attributes", false)]
        [TestCase("_attributes", true)]
        public void PocOwnerRequiresEveryCapturedCollection(string fieldName, bool unsupported)
        {
            var owner = new TestTypeProvider();
            owner.EnsureBuilt();
            _ = owner.Type;
            _ = owner.GetAttributesForWrite();
            object? replacement = unsupported ? fieldName switch
            {
                "_fields" => new UnknownCarrier<FieldProvider>(),
                "_properties" => new UnknownCarrier<PropertyProvider>(),
                "_methods" => new UnknownCarrier<MethodProvider>(),
                "_constructors" => new UnknownCarrier<ConstructorProvider>(),
                "_nestedTypes" or "_serializationProviders" => new UnknownCarrier<TypeProvider>(),
                "_attributes" => new UnknownCarrier<MethodBodyStatement>(),
                _ => throw new ArgumentOutOfRangeException(nameof(fieldName))
            } : null;
            typeof(TypeProvider).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, replacement);
            var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
            Assert.That(snapshot.IsCurrent(), Is.True, "Existing shared owner semantics remain unchanged.");
            Assert.DoesNotThrow(() => Assert.That(snapshot.IsCurrentForPoc(new PocWorkBudget(4096)), Is.False));
        }

        private sealed class UnknownCarrier<T> : IReadOnlyList<T>
        {
            public int Count => throw new InvalidOperationException("Unknown owner carrier must not execute.");
            public T this[int index] => throw new InvalidOperationException("Unknown owner carrier must not execute.");
            public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Unknown owner carrier must not execute.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void PocSupplementalDocumentationFundsOccurrenceKeyBeforeCaptureAndLookup()
        {
            var owner = new TestTypeProvider();
            var method = new MethodProvider(
                new MethodSignature("M", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, new XmlDocProvider());
            owner.Update(methods: [method]);
            owner.EnsureBuilt();
            _ = owner.Type;
            var compilation = CSharpCompilation.Create("PocOccurrenceKeyBudget");
            var generation = Guid.NewGuid();
            var compactFile = new PreparedSourceFile(generation, owner, "S.cs", compilation, disableXmlDocs: false);
            var longFile = new PreparedSourceFile(generation, owner, new string('L', 2048) + ".cs", compilation, disableXmlDocs: false);
            var compactBudget = new PocWorkBudget(65536);
            var compact = PreparedPocSupplementalFile.Capture(compactFile, compactBudget);
            Assert.That(compact.IsRejected, Is.False);
            var compactWork = 65536 - compactBudget.Remaining;
            var occurrence = compactFile.Roots.SelectMany(root => root.DescendantsAndSelf())
                .First(node => node.IsOpaque && node.Producer is XmlDocProvider);
            Assert.That(compact.GetDocumentation(occurrence.Id), Is.Not.Null, "A real captured documentation occurrence is required.");
            var limitedPoint = compact.GetDocumentation(occurrence.Id, new PocWorkBudget(1));
            var fundedPoint = compact.GetDocumentation(occurrence.Id, new PocWorkBudget(65536));
            var longCapture = PreparedPocSupplementalFile.Capture(longFile, new PocWorkBudget(compactWork));
            Assert.Multiple(() =>
            {
                Assert.That(limitedPoint, Is.Null, "A point lookup cannot hash a multi-character occurrence key with one credit.");
                Assert.That(fundedPoint, Is.Not.Null);
                Assert.That(longCapture.IsRejected, Is.True,
                    "Compact capture credits cannot fund a longer actual occurrence-key hash/possible equality chain.");
            });
            Assert.That(compactFile.IsCurrent(), Is.True);
            Assert.That(longFile.IsCurrent(), Is.True, "POC-only exhaustion must not invalidate shared traversal.");
            Assert.That(PreparedPocSupplementalFile.Capture(longFile).IsRejected, Is.False,
                "The actual bounded long path remains admitted under the unchanged budget.");
        }

        [Test]
        public void PocSupplementalDocumentationCaptureFundsActualSummaryWork()
        {
            static (PreparedSourceFile File, XmlDocProvider Docs) CreateFile(string text)
            {
                var owner = new TestTypeProvider();
                var docs = new XmlDocProvider(new XmlDocSummaryStatement([$"{text}"]));
                var method = new MethodProvider(
                    new MethodSignature("M", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                    new KeywordExpression("default", null), owner, docs);
                owner.Update(methods: [method]);
                owner.EnsureBuilt();
                _ = owner.Type;
                return (new PreparedSourceFile(Guid.NewGuid(), owner, "S.cs", CSharpCompilation.Create("PocNestedDocBudget"), disableXmlDocs: false), docs);
            }

            var compact = CreateFile("short");
            var longText = new string('x', 3000);
            var large = CreateFile(longText);
            Assert.That(large.Docs.Summary!.Lines.Single().GetArgument(0), Is.EqualTo(longText),
                "The actual normalized documentation argument must remain long.");
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(compact.Docs), Is.Not.Null);
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(large.Docs), Is.Not.Null);
            var compactFull = PreparedPocSupplementalFile.Capture(compact.File, new PocWorkBudget(65536));
            var fullBudget = new PocWorkBudget(65536);
            var largeFull = PreparedPocSupplementalFile.Capture(large.File, fullBudget);
            Assert.That(compactFull.IsRejected, Is.False);
            Assert.That(largeFull.IsRejected, Is.False, "A supported actual long documentation boundary must precede the low-credit contrast.");
            var occurrence = large.File.Roots.SelectMany(root => root.DescendantsAndSelf())
                .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, large.Docs));
            Assert.That(largeFull.GetDocumentation(occurrence.Id), Is.Not.Null);
            var limited = PreparedPocSupplementalFile.Capture(large.File, new PocWorkBudget(2000));
            Assert.That(limited.IsRejected, Is.True,
                "Two thousand inventory credits cannot fund the actual 3,000-character nested documentation capture.");
            var required = 65536 - fullBudget.Remaining;
            Assert.That(PreparedPocSupplementalFile.Capture(large.File, new PocWorkBudget(required)).IsRejected, Is.False);
            Assert.That(PreparedPocSupplementalFile.Capture(large.File, new PocWorkBudget(required - 1)).IsRejected, Is.True);
            Assert.That(PreparedPocSupplementalFile.Capture(large.File, new PocWorkBudget(0)).IsRejected, Is.True);
            Assert.That(large.File.IsCurrent(), Is.True, "POC inventory exhaustion must not stale the shared source file.");
        }

        [TestCase(4)]
        [TestCase(8)]
        public void PocSupplementalDocumentationCapturesShareOneInventoryBudget(int count)
        {
            var owner = new TestTypeProvider();
            var text = new string('x', 1000);
            var docs = Enumerable.Range(0, count)
                .Select(_ => new XmlDocProvider(new XmlDocSummaryStatement([$"{text}"]))).ToArray();
            foreach (var doc in docs) { Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(doc), Is.Not.Null); }
            var methods = docs.Select((doc, index) => new MethodProvider(
                new MethodSignature($"M{index}", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, doc)).ToArray();
            owner.Update(methods: methods);
            owner.EnsureBuilt();
            _ = owner.Type;
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "S.cs", CSharpCompilation.Create("PocAggregateDocBudget"), disableXmlDocs: false);
            var fullBudget = new PocWorkBudget(65536);
            var full = PreparedPocSupplementalFile.Capture(source, fullBudget);
            Assert.That(full.IsRejected, Is.False);
            Assert.That(full.HasUnavailableDocumentation, Is.False);
            foreach (var doc in docs)
            {
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, doc));
                Assert.That(full.GetDocumentation(occurrence.Id), Is.Not.Null);
            }
            Assert.That(PreparedPocSupplementalFile.Capture(source, new PocWorkBudget(2500)).IsRejected, Is.True,
                "One enclosing budget must pay every actual nested documentation capture.");
            var required = 65536 - fullBudget.Remaining;
            Assert.That(PreparedPocSupplementalFile.Capture(source, new PocWorkBudget(required)).IsRejected, Is.False);
            Assert.That(PreparedPocSupplementalFile.Capture(source, new PocWorkBudget(required - 1)).IsRejected, Is.True);
            Assert.That(source.IsCurrent(), Is.True);
        }

        [Test]
        public void PocSupplementalDocumentationRetainsLocalCaptureLimit()
        {
            var text = new string('x', 4060);
            var owner = new TestTypeProvider();
            var docs = new XmlDocProvider(new XmlDocSummaryStatement([$"{text}"]));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Null,
                "The native file-envelope limit must reject this actual long summary.");
            var method = new MethodProvider(
                new MethodSignature("M", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, docs);
            owner.Update(methods: [method]);
            owner.EnsureBuilt();
            _ = owner.Type;
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "S.cs", CSharpCompilation.Create("PocLocalDocLimit"), disableXmlDocs: false);
            var budget = new PocWorkBudget(65536);
            var supplemental = PreparedPocSupplementalFile.Capture(source, budget);
            Assert.That(supplemental.IsRejected, Is.False, "A child-local limit failure must not exhaust the enclosing inventory.");
            Assert.That(supplemental.HasUnavailableDocumentation, Is.True);
            Assert.That(budget.Exhausted, Is.False);
            Assert.That(source.IsCurrent(), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocSupplementalDocumentationChildLimitDistinguishesPointQueryAndVoidObserver(bool voidObserver)
        {
            var parameters = new List<XmlDocParamStatement>();
            var docs = new XmlDocProvider(parameters: parameters);
            var owner = new TestTypeProvider();
            var method = new MethodProvider(
                new MethodSignature("M", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, docs);
            owner.Update(methods: [method]);
            owner.EnsureBuilt();
            _ = owner.Type;
            _ = owner.GetAttributesForWrite();
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "S.cs", CSharpCompilation.Create("PocChildLimitRetry"), disableXmlDocs: false);
            var supplemental = PreparedPocSupplementalFile.Capture(source);
            var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, docs));
            var envelope = supplemental.GetDocumentation(occurrence.Id);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(supplemental.IsRejected, Is.False);
            Assert.That(envelope!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536), out _), Is.True);
            Assert.That(source.IsShapeCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True,
                "The actual supplemental shape must be current before the child-limit contrast.");
            Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(occurrence.Id, new PocWorkBudget(65536)), Is.False,
                "The compact fixture has not executed a native write.");
            Assert.That(supplemental.IsRejected, Is.False);

            var parameter = new ParameterProvider("arg", $"arg", typeof(int));
            var document = new XmlDocParamStatement(parameter);
            parameters.AddRange(Enumerable.Repeat(document, 4097));
            try
            {
                var envelopeBudget = new PocWorkBudget(65536);
                Assert.That(envelope.IsCurrentWithoutInvalidating(envelopeBudget, out var unavailable), Is.False);
                Assert.That(unavailable, Is.True, "The actual child precheck must exhaust its local limit.");
                Assert.That(envelopeBudget.Exhausted, Is.False, "Child exhaustion must not exhaust the parent.");
                Assert.That(source.IsCurrent(), Is.True, "The documentation boundary is opaque to the shared source proof.");
                Assert.That(source.IsShapeCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True,
                    "The actual supplemental shape must remain current before the documentation recheck.");
                if (voidObserver)
                {
                    _ = supplemental.HasUnsafeOrUnavailableKnownRawNames;
                    Assert.That(supplemental.IsRejected, Is.True, "A void observer must fail closed on unavailable coverage.");
                }
                else
                {
                    var queryBudget = new PocWorkBudget(65536);
                    Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(occurrence.Id, queryBudget), Is.False);
                    Assert.That(queryBudget.Exhausted, Is.False);
                    Assert.That(supplemental.IsRejected, Is.False, "A point query must not turn a child limit into a sticky mismatch.");
                }
            }
            finally { parameters.Clear(); }
            Assert.That(source.IsCurrent(), Is.True);
            Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(65536), out _), Is.True);
            Assert.That(supplemental.IsRejected, Is.EqualTo(voidObserver));
        }

        [Test]
        public void PocSupplementalDocumentationFundedMismatchRemainsSticky()
        {
            var parameters = new List<XmlDocParamStatement>();
            var docs = new XmlDocProvider(parameters: parameters);
            var owner = new TestTypeProvider();
            owner.Update(methods: [new MethodProvider(
                new MethodSignature("M", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, docs)]);
            owner.EnsureBuilt();
            _ = owner.Type;
            _ = owner.GetAttributesForWrite();
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "S.cs", CSharpCompilation.Create("PocFundedMismatch"), disableXmlDocs: false);
            var supplemental = PreparedPocSupplementalFile.Capture(source);
            var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, docs));
            var envelope = supplemental.GetDocumentation(occurrence.Id)!;
            Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(65536), out _), Is.True);
            Assert.That(source.IsShapeCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(occurrence.Id, new PocWorkBudget(65536)), Is.False);
            Assert.That(supplemental.IsRejected, Is.False);
            parameters.Add(new XmlDocParamStatement(new ParameterProvider("arg", $"arg", typeof(int))));
            var budget = new PocWorkBudget(65536);
            Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(65536), out var unavailable), Is.False);
            Assert.That(unavailable, Is.False, "A fully funded stable mismatch remains an observation.");
            Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(occurrence.Id, budget), Is.False);
            Assert.That(budget.Exhausted, Is.False);
            Assert.That(supplemental.IsRejected, Is.True);
            parameters.Clear();
            Assert.That(source.IsCurrent(), Is.True);
            Assert.That(supplemental.IsRejected, Is.True, "A witnessed mismatch remains sticky after restoration.");
        }

        [TestCase(8, false)]
        [TestCase(300, true)]
        public void PocSupplementalFileBoundsNumberOfObservedBoundaries(int count, bool rejected)
        {
            var owner = new TestTypeProvider();
            var methods = Enumerable.Range(0, count).Select(index => new MethodProvider(
                new MethodSignature($"M{index}", null, MethodSignatureModifiers.Public, null, null, Array.Empty<ParameterProvider>()),
                new KeywordExpression("default", null), owner, new XmlDocProvider())).ToArray();
            owner.Update(methods: methods);
            owner.EnsureBuilt();
            _ = owner.Type;
            var source = new PreparedSourceFile(Guid.NewGuid(), owner, "Generated.cs", CSharpCompilation.Create("PocBudget"), disableXmlDocs: false);
            var supplemental = PreparedPocSupplementalFile.Capture(source);
            Assert.That(supplemental.IsRejected, Is.EqualTo(rejected));
            Assert.That(source.IsCurrent(), Is.True, "A POC-only limit must not invalidate shared traversal.");
        }
    }
}

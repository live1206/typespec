// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(0, true)]
        [TestCase(14, false)]
        public void FinalHeaderReviewFileTagsFundRepeatedPlaceholderOccurrences(int depth, bool admitted)
        {
            Microsoft.TypeSpec.Generator.Tests.MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            FormattableString repeated = $"x";
            for (int i = 0; i < depth; i++)
            {
                // Composite XML formatting uses numeric argument indices, unlike
                // the separate body-interpolation hole traversal.
                repeated = FormattableStringFactory.Create("{0}{0}", repeated);
            }
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(Array.Empty<FormattableString>())
            {
                StartTag = FormattableStringFactory.Create("<summary>{0}", repeated)
            });
            using var writer = new CodeWriter();
            writer.WriteXmlDocsNoScope(docs);
            var native = writer.ToString(false);
            Assert.That(native.Count(c => c == 'x'), Is.EqualTo(1 << depth));
            Assert.That(native, Does.Contain("<summary>").And.Contain("</summary>"));
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs) is not null, Is.EqualTo(admitted));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalHeaderReviewNullableCrefChecksReconstructedRuntimeName(bool unsafeRuntimeName)
        {
            Microsoft.TypeSpec.Generator.Tests.MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FinalHeaderReview" + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.Run).DefineDynamicModule("types");
            var runtime = module.DefineType(unsafeRuntimeName ? "PocReview.Bad\nType" : "PocReview.GoodType", TypeAttributes.Public).CreateType()!;
            var type = new CSharpType(runtime, isNullable: true);
            if (unsafeRuntimeName) { type.Update(name: "SafeReviewName"); }
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0:C}", type)]));
            using var writer = new CodeWriter();
            writer.WriteXmlDocsNoScope(docs);
            var native = writer.ToString(false);
            if (unsafeRuntimeName)
            {
                Assert.That(native, Does.Not.Contain("SafeReviewName"));
                Assert.That(native, Does.Match(@"Bad\r?\n\s*Type"), "The native cref really reconstructs the runtime name.");
            }
            else { Assert.That(native, Does.Contain("GoodType")); }
            Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs) is not null, Is.EqualTo(!unsafeRuntimeName));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void FinalHeaderReviewRuntimeCacheFundsSparseSlots(int remaining, bool compact)
        {
            // No production removal API exists: this is explicitly an internal
            // accounting control, not a public-input reachability claim.
            var cache = (Dictionary<Type, CSharpType>)typeof(CSharpType).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var saved = cache.ToArray();
            var savedCapacity = cache.EnsureCapacity(0);
            try
            {
                var value = new CSharpType(typeof(int));
                var keys = Enumerable.Range(0, 8192).Select(i => typeof(Tuple<,,>).MakeGenericType(
                    typeof(int).MakeArrayType(1 + i % 32), typeof(byte).MakeArrayType(1 + i / 32 % 32),
                    typeof(short).MakeArrayType(1 + i / 1024))).ToArray();
                cache.Clear();
                foreach (var key in keys) { cache.Add(key, value); }
                foreach (var key in keys) { Assert.That(cache.Remove(key), Is.True); }
                if (remaining == 1) { cache.Add(typeof(int), value); }
                Assert.That(cache.Count, Is.EqualTo(remaining));
                Assert.That(cache.EnsureCapacity(0), Is.GreaterThanOrEqualTo(8192));
                if (compact) { cache.TrimExcess(); }
                var budget = new PocWorkBudget(4096);
                var captured = CSharpType.TryGetPocCachedRuntimeType(typeof(int), budget, out var actual);
                Assert.That(captured, Is.EqualTo(compact));
                if (compact) { Assert.That(ReferenceEquals(actual, remaining == 1 ? value : null), Is.True); }
            }
            finally
            {
                cache.Clear();
                cache.TrimExcess();
                cache.EnsureCapacity(savedCapacity);
                foreach (var pair in saved) { cache.Add(pair.Key, pair.Value); }
            }
        }

        [TestCase(8, false, true)]
        [TestCase(16384, false, true)]
        [TestCase(16384, true, false)]
        public void FinalHeaderReviewNamedMetadataKeyComparisonFundsCharacters(int length, bool replaceKey, bool current)
        {
            var key = new string('A', length);
            var value = Snippet.Literal(1);
            var named = new[] { new KeyValuePair<string, ValueExpression>(key, value) };
            var attribute = new AttributeStatement(typeof(ObsoleteAttribute), named);
            Assert.That(AttributeSnapshot.TryCaptureForPoc(attribute, new PocWorkBudget(4096), out var snapshot), Is.True);
            if (replaceKey)
            {
                var replacement = new string(key.ToCharArray());
                Assert.That(ReferenceEquals(key, replacement), Is.False);
                named[0] = new(replacement, value);
            }
            Assert.That(snapshot!.IsCurrentForPoc(new PocWorkBudget(4096)), Is.EqualTo(current));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void FinalHeaderReviewFileReconstructionObservesForeignBaseCache(bool cref, bool lateInsertion)
        {
            Microsoft.TypeSpec.Generator.Tests.MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            var cache = (Dictionary<Type, CSharpType>)typeof(CSharpType).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var runtime = new CSharpType(typeof(InvalidOperationException), isNullable: true);
            var docs = cref
                ? new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0:C}", runtime)]))
                : new XmlDocProvider(exceptions: new[] { new XmlDocExceptionStatement(typeof(InvalidOperationException), "failure", Array.Empty<ParameterProvider>()) });
            // Exception capture observes the existing target, while native
            // construction independently looks up its actual runtime base.
            _ = (CSharpType)typeof(InvalidOperationException);
            var baseType = typeof(InvalidOperationException).BaseType!;
            Assert.That(cache.ContainsKey(baseType), Is.True);
            var probe = new FinalHeaderReviewForeignBaseType(baseType);
            try
            {
                PreparedGeneratedDocumentationEnvelope? envelope = null;
                if (lateInsertion)
                {
                    envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs);
                    Assert.That(envelope, Is.Not.Null, "A supported reconstruction is the unchanged baseline.");
                }
                cache.Add(probe, cache[baseType]);
                probe.Calls = 0;
                if (!lateInsertion) { envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs); }
                var current = envelope?.IsCurrent ?? false;
                Assert.That(probe.Calls, Is.Zero, "Capture and currentness must not dispatch foreign equality.");
                using var writer = new CodeWriter();
                writer.WriteXmlDocsNoScope(docs);
                Assert.That(probe.Calls, Is.GreaterThan(0), "The injected key must intercept the actual native base lookup.");
                Assert.That(current, Is.False, "Formatter-specific construction and its current cache must be admitted, not only the old IR type.");
            }
            finally { cache.Remove(probe); }
        }

        private sealed class FinalHeaderReviewForeignBaseType(Type target) : TypeDelegator(target)
        {
            internal int Calls;
            public override int GetHashCode() => typeImpl.GetHashCode();
            public override bool Equals(object? other) { Calls++; return false; }
            public override bool Equals(Type? other) { Calls++; return false; }
        }

        [TestCase(1000, false)]
        [TestCase(65536, true)]
        public void FinalHeaderReviewFlattenedBodiesFundActualIdentityChains(int available, bool admitted)
        {
            var capacityProbe = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var capacity = capacityProbe.EnsureCapacity(32);
            var candidates = new List<(MethodBodyStatements Node, MethodBodyStatement[] Slot)>();
            for (int i = 0; candidates.Count < 32; i++)
            {
                var slot = new MethodBodyStatement[1];
                var node = new MethodBodyStatements(slot);
                if ((RuntimeHelpers.GetHashCode(node) & int.MaxValue) % capacity == 0) { candidates.Add((node, slot)); }
            }
            MethodBodyStatement child = new ExpressionStatement(Snippet.Literal(1));
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                candidates[i].Slot[0] = child;
                child = candidates[i].Node;
            }
            var expression = new TryExpression(child);
            var budget = new PocWorkBudget(available);
            var shape = StructuredSourceShape.CaptureForPocSupplement(expression, false, budget);
            Assert.That(!shape.Opaque && !budget.Exhausted, Is.EqualTo(admitted));
            if (admitted)
            {
                Assert.That(available - budget.Remaining, Is.GreaterThanOrEqualTo(32 * 32), "Actual identity buckets do not make variable probe chains free.");
                var role = "body[0]" + string.Concat(Enumerable.Repeat("/statement[0]", 32));
                Assert.That(shape.Children.Single().Role, Is.EqualTo(role), "Keep the full original role, even when intermediate path strings are not materialized.");
                Assert.That(available - budget.Remaining, Is.GreaterThanOrEqualTo(32 * 32 + role.Length), "Fund identity probes and the actual materialized leaf role.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalHeaderReviewFlattenedRolesMatchLegacyForBranchesAndMultiDigitIndices(bool grouped)
        {
            var statements = Enumerable.Range(0, 12).Select(i => (MethodBodyStatement)new ExpressionStatement(Snippet.Literal(i))).ToArray();
            var expression = grouped ? new TryExpression(new MethodBodyStatements(statements)) : new TryExpression(statements);
            var legacy = StructuredSourceShape.Capture(expression, false);
            var poc = StructuredSourceShape.CaptureForPocSupplement(expression, false, new PocWorkBudget(4096));
            Assert.That(poc.Opaque, Is.False);
            Assert.That(poc.Children.Select(child => child.Role), Is.EqualTo(legacy.Children.Select(child => child.Role)));
            Assert.That(poc.Children.Zip(legacy.Children, (left, right) => ReferenceEquals(left.Producer, right.Producer)).All(same => same), Is.True);
            Assert.That(poc.Children.Length, Is.EqualTo(12));
        }

        [Test]
        public void FinalHeaderReviewSupplementalAncestorProbeFundsActualIdentityChain()
        {
            var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var capacity = ancestors.EnsureCapacity(32);
            object? oldest = null;
            while (ancestors.Count < 32)
            {
                var value = new LiteralExpression(1);
                if ((RuntimeHelpers.GetHashCode(value) & int.MaxValue) % capacity != 0) { continue; }
                oldest ??= value;
                ancestors.Add(value);
            }
            var budget = new PocWorkBudget(1);
            var method = typeof(PreparedPocSupplementalFile.Node).GetMethod("TryCaptureCore", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.That(method.Invoke(null, new object[] { oldest!, false, ancestors, 0, budget }), Is.Null);
            Assert.That(budget.Exhausted, Is.True, "A seeded internal accounting control must fund its actual collision chain before probing.");
        }

        [TestCase("plain")]
        [TestCase("list")]
        [TestCase("dictionary")]
        public void FinalHeaderReviewCollectionClassificationDoesNotWarmNativeCaches(string kind)
        {
            var element = new CSharpType(typeof(string));
            var type = kind switch
            {
                "list" => new CSharpType(typeof(IList<>), arguments: new[] { element }, isNullable: false),
                "dictionary" => new CSharpType(typeof(IDictionary<,>), arguments: new[] { new CSharpType(typeof(int)), element }, isNullable: false),
                _ => element
            };
            var flags = new[] { "_isCollection", "_isDictionary", "_isReadOnlyDictionary", "_isReadWriteDictionary", "_isList", "_isReadOnlyList", "_isReadWriteList", "_isReadOnlyMemory" }
                .Select(name => typeof(CSharpType).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!).ToArray();
            Assert.That(flags.All(field => field.GetValue(type) is null), Is.True, "Establish genuinely cold native classifications.");
            // Targeted helper unit: the constructor runs its normal bookkeeping
            // initializers. Unused outer capture dependencies are deliberately
            // null; this is not a claim of a complete admitted generation.
            var empty = System.Collections.Immutable.ImmutableArray<MethodBodyStatement>.Empty;
            var constructor = typeof(PreparedPocContextAttributeRecipe).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var recipe = constructor.Invoke(new object?[] { new GeneratedCallableOwner("observe"), null, null, empty, empty, empty, null });
            var method = typeof(PreparedPocContextAttributeRecipe).GetMethod("InnermostCollection", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var arguments = new object?[] { type, new PocWorkBudget(4096), null };
            Assert.That(method.Invoke(recipe, arguments), Is.True);
            Assert.That(ReferenceEquals(arguments[2], element), Is.True);
            Assert.That(flags.All(field => field.GetValue(type) is null), Is.True, "A pure preflight read must not initialize the IR's native classification caches.");
            Assert.That(type.IsCollection, Is.EqualTo(kind != "plain"), "The native getter still retains its ordinary classification behavior.");
        }

        [TestCase(4096, true)]
        [TestCase(4097, false)]
        public void FinalHeaderReviewArgumentWitnessHasBoundedObservationCopy(int count, bool recorded)
        {
            var arguments = Enumerable.Repeat<ValueExpression>(Snippet.Literal(1), count).ToArray();
            var invocation = new InvokeMethodExpression(null, "Observe", (IReadOnlyList<ValueExpression>)arguments);
            ConstructedArgumentWitness.Record(invocation, arguments);
            Assert.That(ConstructedArgumentWitness.Read(invocation).HasValue, Is.EqualTo(recorded));
            Assert.That(ReferenceEquals(invocation.Arguments, arguments), Is.True, "Skipping a witness must not replace the native carrier.");
            Assert.That(invocation.Arguments.Count, Is.EqualTo(count));
        }
    }
}

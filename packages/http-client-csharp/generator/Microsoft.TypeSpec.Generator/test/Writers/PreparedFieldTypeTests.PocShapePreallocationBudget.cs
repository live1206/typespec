// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void PocKeywordShapeAvoidsUnusedListAllocations()
        {
            var keyword = new KeywordExpression("return", null);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(StructuredSourceShape.CaptureForPocRecheck(keyword, true, new PocWorkBudget(4096)), Is.Not.Null);
                Assert.That(StructuredSourceShape.CaptureForPocSupplementCurrent(keyword, true, new PocWorkBudget(4096)).Opaque, Is.False);
            }
            var budgets = new PocWorkBudget[64];
            var fullStorageBudgets = new PocWorkBudget[budgets.Length];
            for (int i = 0; i < budgets.Length; i++)
            {
                budgets[i] = new PocWorkBudget(4096);
                fullStorageBudgets[i] = new PocWorkBudget(4096);
            }
            bool allSupported = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in budgets)
            {
                var shape = StructuredSourceShape.CaptureForPocRecheck(keyword, true, budget);
                if (shape is null || shape.Opaque) { allSupported = false; }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            bool fullStorageSupported = true;
            before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in fullStorageBudgets)
            {
                if (StructuredSourceShape.CaptureForPocSupplementCurrent(keyword, true, budget).Opaque) { fullStorageSupported = false; }
            }
            long fullStorageAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var sampleBudget = new PocWorkBudget(4096);
            var sample = StructuredSourceShape.CaptureForPocRecheck(keyword, true, sampleBudget);
            Assert.That(allSupported, Is.True);
            Assert.That(fullStorageSupported, Is.True);
            Assert.That(allocated, Is.LessThan(fullStorageAllocated),
                "The actual keyword recheck must allocate less than the same keyword's full private storage profile.");
            Assert.That(sample, Is.Not.Null);
            var shared = StructuredSourceShape.Capture(keyword, true);
            Assert.That(shared.MatchesForPoc(sample!, new PocWorkBudget(4096)), Is.True);
            Assert.That(4096 - sampleBudget.Remaining, Is.InRange(11, 18),
                "The bounded keyword recheck includes its shape, storage and text work without three unused lists.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            foreach (var field in new[] { "_types", "_declarations", "_metadataAttributes" })
            {
                Assert.That(typeof(StructuredSourceShape).GetField(field, flags)!.GetValue(sample), Is.Null,
                    $"A keyword recheck never uses {field}.");
            }
            keyword.Update("throw", null);
            var changed = StructuredSourceShape.CaptureForPocRecheck(keyword, true, new PocWorkBudget(4096));
            Assert.That(changed, Is.Not.Null);
            Assert.That(shared.MatchesForPoc(changed!, new PocWorkBudget(4096)), Is.False,
                "The cheaper representation must still observe an actual mutation.");
        }

        [Test]
        public void PocCompactShapeProfilesMatchSharedCapture()
        {
            using var host = new BodyOnlyPhysicalHost();
            var hostile = new PocHostileBlockList();
            var signature = new MethodSignature("Call", null, MethodSignatureModifiers.Public, typeof(string), null, []);
            var nestedCall = new InvokeMethodExpression(null, signature, Array.Empty<ValueExpression>());
            var cases = new (string Name, object Producer, int Objects)[]
            {
                ("null literal", new LiteralExpression(null), 3),
                ("string literal", new LiteralExpression("text"), 3),
                ("opaque literal", new LiteralExpression(new object()), 3),
                ("keyword without operand", new KeywordExpression("return", null), 4),
                ("keyword with operand", new KeywordExpression("return", new LiteralExpression(1)), 4),
                ("keyword with signature child", new KeywordExpression("return", nestedCall), 4),
                ("expression statement", new ExpressionStatement(new LiteralExpression(2)), 4),
                ("missing expression", new ExpressionStatement(null!), 4),
                ("empty block", new MethodBodyStatements(Array.Empty<MethodBodyStatement>()), 4),
                ("nonempty block", new MethodBodyStatements([new ExpressionStatement(new LiteralExpression(3))]), 4),
                ("hostile block carrier", new MethodBodyStatements(hostile), 4),
                ("producer-owned block carrier", new MethodBodyStatements(ArgumentCarrierFactory.Create<MethodBodyStatement>([new ExpressionStatement(new LiteralExpression(3))])), 4),
                ("variable", new VariableExpression(typeof(string), "value"), 5),
                ("missing variable parts", new VariableExpression(null!, (CodeWriterDeclaration)null!), 5),
                ("derived statement fallback", new PocDerivedExpressionStatement(new LiteralExpression(4)), 7),
                ("derived block fallback", new PocDerivedBlock([new ExpressionStatement(new LiteralExpression(5))]), 7),
                ("derived variable fallback", new PocDerivedVariable(typeof(string), new CodeWriterDeclaration("value")), 7),
                ("nested signature child", nestedCall, 7)
            };
            foreach (var (name, producer, objects) in cases)
            {
                var shortBudget = new PocWorkBudget(objects - 1);
                Assert.That(StructuredSourceShape.CaptureForPocRecheck(producer, true, shortBudget), Is.Null, name);
                Assert.That(shortBudget.Exhausted, Is.True, name);
                Assert.That(StructuredSourceShape.CaptureForPocRecheck(producer, true, new PocWorkBudget(objects)), Is.Not.Null, name);
                var shared = StructuredSourceShape.Capture(producer, true);
                var current = StructuredSourceShape.CaptureForPocRecheck(producer, true, new PocWorkBudget(4096));
                Assert.That(current, Is.Not.Null, name);
                Assert.That(shared.MatchesForPoc(current!, new PocWorkBudget(4096)), Is.True, name);
                Assert.That(current!.MatchesForPoc(shared, new PocWorkBudget(4096)), Is.True, name);
            }
            Assert.That(hostile.Calls, Is.Zero, "Unknown block carriers must remain opaque without callbacks.");
            var missingKeyword = new KeywordExpression(null!, null);
            var missingShared = StructuredSourceShape.Capture(missingKeyword, true);
            var missingCurrent = StructuredSourceShape.CaptureForPocRecheck(missingKeyword, true, new PocWorkBudget(4096));
            Assert.That(missingCurrent, Is.Not.Null);
            Assert.That(missingCurrent!.Opaque, Is.True);
            Assert.That(missingShared.MatchesForPoc(missingCurrent, new PocWorkBudget(4096)), Is.False);
            var parent = new PocWorkBudget(3);
            var child = new PocWorkBudget(50, parent);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(new KeywordExpression("return", null), true, child), Is.Null);
            Assert.That(parent.Exhausted, Is.True);
            Assert.That(child.Exhausted, Is.True);
            Assert.That(parent.Remaining, Is.EqualTo(2), "Only the already-funded classification debit is charged.");
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(new KeywordExpression("return", null), true, new PocWorkBudget(4096)), Is.Not.Null);
            var variable = new VariableExpression(typeof(string), "before");
            var original = StructuredSourceShape.Capture(variable, true);
            variable.Update(name: "after");
            var changed = StructuredSourceShape.CaptureForPocRecheck(variable, true, new PocWorkBudget(4096));
            Assert.That(changed, Is.Not.Null);
            Assert.That(original.MatchesForPoc(changed!, new PocWorkBudget(4096)), Is.False,
                "The compact variable profile must still observe a real declaration mutation.");
        }

        private sealed class PocDerivedExpressionStatement(ValueExpression expression) : ExpressionStatement(expression) { }
        private sealed class PocDerivedBlock(MethodBodyStatement[] statements) : MethodBodyStatements(statements) { }
        private sealed record PocDerivedVariable(CSharpType Type, CodeWriterDeclaration Declaration) : VariableExpression(Type, Declaration);
        private sealed class PocHostileBlockList : IReadOnlyList<MethodBodyStatement>
        {
            internal int Calls { get; private set; }
            public int Count { get { Calls++; throw new InvalidOperationException(); } }
            public MethodBodyStatement this[int index] { get { Calls++; throw new InvalidOperationException(); } }
            public IEnumerator<MethodBodyStatement> GetEnumerator() { Calls++; throw new InvalidOperationException(); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void PocKeywordStopsBeforeUnfundedValueWork()
        {
            var keyword = new KeywordExpression("return", null);
            var shared = StructuredSourceShape.Capture(keyword, true);
            var funded = StructuredSourceShape.CaptureForPocRecheck(keyword, true, new PocWorkBudget(4096));
            Assert.That(funded, Is.Not.Null);
            Assert.That(shared.MatchesForPoc(funded!, new PocWorkBudget(4096)), Is.True);
            var exactObjects = new PocWorkBudget(4);
            var limited = StructuredSourceShape.CaptureForPocRecheck(keyword, true, exactObjects);
            Assert.That(limited, Is.Not.Null, "The exact shape-and-list object reservation remains available.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var values = (List<object?>)typeof(StructuredSourceShape).GetField("_values", flags)!.GetValue(limited)!;
            var references = (List<object?>)typeof(StructuredSourceShape).GetField("_references", flags)!.GetValue(limited)!;
            var children = (List<SourceChild>)typeof(StructuredSourceShape).GetField("_children", flags)!.GetValue(limited)!;
            Assert.That(exactObjects.Exhausted, Is.True);
            Assert.That(limited!.Opaque, Is.True);
            Assert.That(values.Count, Is.Zero, "No keyword values may be observed or appended without credits.");
            Assert.That(values.Capacity, Is.Zero);
            Assert.That(references.Capacity, Is.Zero);
            Assert.That(children.Capacity, Is.Zero);
            Assert.That(shared.MatchesForPoc(limited, new PocWorkBudget(4096)), Is.False);
            var oneShort = new PocWorkBudget(17);
            var shortShape = StructuredSourceShape.CaptureForPocRecheck(keyword, true, oneShort);
            Assert.That(shortShape, Is.Not.Null);
            Assert.That(oneShort.Exhausted, Is.True);
            Assert.That(shortShape!.Opaque, Is.True);
            var exact = new PocWorkBudget(18);
            var exactShape = StructuredSourceShape.CaptureForPocRecheck(keyword, true, exact);
            Assert.That(exactShape, Is.Not.Null);
            Assert.That(exact.Exhausted, Is.False);
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(shared.MatchesForPoc(exactShape!, new PocWorkBudget(4096)), Is.True);
            var withChild = new KeywordExpression("return", new LiteralExpression(null));
            var sharedWithChild = StructuredSourceShape.Capture(withChild, true);
            var childShort = new PocWorkBudget(18);
            var childShortShape = StructuredSourceShape.CaptureForPocRecheck(withChild, true, childShort);
            Assert.That(childShortShape, Is.Not.Null);
            Assert.That(childShort.Exhausted, Is.True, "The captured child still needs its immutable projection credit.");
            Assert.That(childShortShape!.Opaque, Is.True);
            var childExact = new PocWorkBudget(19);
            var childExactShape = StructuredSourceShape.CaptureForPocRecheck(withChild, true, childExact);
            Assert.That(childExactShape, Is.Not.Null);
            Assert.That(childExact.Exhausted, Is.False);
            Assert.That(childExact.Remaining, Is.Zero);
            Assert.That(sharedWithChild.MatchesForPoc(childExactShape!, new PocWorkBudget(4096)), Is.True);
        }

        [Test]
        public void PocLiteralStopsBeforeUnfundedValueWork()
        {
            var literal = new LiteralExpression(null);
            var shared = StructuredSourceShape.Capture(literal, true);
            var funded = StructuredSourceShape.CaptureForPocRecheck(literal, true, new PocWorkBudget(4096));
            Assert.That(funded, Is.Not.Null);
            Assert.That(shared.MatchesForPoc(funded!, new PocWorkBudget(4096)), Is.True,
                "The actual literal must remain supported with adequate credits.");
            var exactObjects = new PocWorkBudget(3);
            var limited = StructuredSourceShape.CaptureForPocRecheck(literal, true, exactObjects);
            Assert.That(limited, Is.Not.Null, "The exact shape-and-list object reservation remains available.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var values = (List<object?>)typeof(StructuredSourceShape).GetField("_values", flags)!.GetValue(limited)!;
            var references = (List<object?>)typeof(StructuredSourceShape).GetField("_references", flags)!.GetValue(limited)!;
            Assert.That(exactObjects.Exhausted, Is.True,
                "Object credits alone cannot fund literal value boxing and backing storage.");
            Assert.That(limited!.Opaque, Is.True);
            Assert.That(values.Count, Is.Zero, "No literal values may be observed or appended without credits.");
            Assert.That(values.Capacity, Is.Zero);
            Assert.That(references.Capacity, Is.Zero);
            Assert.That(shared.MatchesForPoc(limited, new PocWorkBudget(4096)), Is.False);
            var oneShort = new PocWorkBudget(6);
            var shortShape = StructuredSourceShape.CaptureForPocRecheck(literal, true, oneShort);
            Assert.That(shortShape, Is.Not.Null);
            Assert.That(oneShort.Exhausted, Is.True);
            Assert.That(shortShape!.Opaque, Is.True);
            var exact = new PocWorkBudget(7);
            var exactShape = StructuredSourceShape.CaptureForPocRecheck(literal, true, exact);
            Assert.That(exactShape, Is.Not.Null);
            Assert.That(exact.Exhausted, Is.False);
            Assert.That(exact.Remaining, Is.Zero);
            Assert.That(shared.MatchesForPoc(exactShape!, new PocWorkBudget(4096)), Is.True,
                "Exact object, backing-slot and boxing credits preserve the supported literal.");
            var opaqueLiteral = new LiteralExpression(new object());
            var opaqueShared = StructuredSourceShape.Capture(opaqueLiteral, true);
            var opaqueBudget = new PocWorkBudget(7);
            var opaqueCurrent = StructuredSourceShape.CaptureForPocRecheck(opaqueLiteral, true, opaqueBudget);
            Assert.That(opaqueCurrent, Is.Not.Null);
            Assert.That(opaqueBudget.Exhausted, Is.False);
            Assert.That(opaqueShared.MatchesForPoc(opaqueCurrent!, new PocWorkBudget(4096)), Is.True,
                "The reserved reference slot preserves the native opaque-literal profile.");
        }

        [Test]
        public void PocFileStopsBeforeUnfundedEntryAllocations()
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var keyword = new KeywordExpression("return", null);
            var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
            var method = new MethodProvider(signature, new MethodBodyStatements([new ExpressionStatement(keyword)]), owner);
            owner.Update(methods: [method]);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            Assert.That(file.IsCurrentWithoutInvalidating(new PocWorkBudget(4096)), Is.True);
            Assert.That(file.IsCurrent(), Is.True);
            var beforeQueries = proofCalls;
            for (int i = 0; i < 3; i++) { Assert.That(file.IsCurrentWithoutInvalidating(new PocWorkBudget(0)), Is.False); }
            var budgets = new PocWorkBudget[64];
            for (int i = 0; i < budgets.Length; i++) { budgets[i] = new PocWorkBudget(0); }
            bool allRejected = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in budgets)
            {
                if (file.IsCurrentWithoutInvalidating(budget)) { allRejected = false; }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allRejected, Is.True);
            Assert.That(budgets.All(budget => budget.Exhausted), Is.True);
            Assert.That(proofCalls, Is.EqualTo(beforeQueries));
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.IsCurrent(), Is.True);
            Assert.That(allocated, Is.LessThan(512),
                "A file query with no credits must stop before per-query allocations.");
        }

        [Test]
        public void PocOccurrenceStopsBeforeUnfundedEntryAllocations()
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var keyword = new KeywordExpression("return", null);
            var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
            var method = new MethodProvider(signature, new MethodBodyStatements([new ExpressionStatement(keyword)]), owner);
            owner.Update(methods: [method]);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, keyword));
            Assert.That(occurrence.IsCurrentWithoutInvalidating(new PocWorkBudget(4096)), Is.True);
            Assert.That(file.IsCurrent(), Is.True);
            var beforeQueries = proofCalls;
            for (int i = 0; i < 3; i++) { Assert.That(occurrence.IsCurrentWithoutInvalidating(new PocWorkBudget(0)), Is.False); }
            var budgets = new PocWorkBudget[64];
            for (int i = 0; i < budgets.Length; i++) { budgets[i] = new PocWorkBudget(0); }
            bool allRejected = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in budgets)
            {
                if (occurrence.IsCurrentWithoutInvalidating(budget)) { allRejected = false; }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allRejected, Is.True);
            Assert.That(budgets.All(budget => budget.Exhausted), Is.True);
            Assert.That(proofCalls, Is.EqualTo(beforeQueries));
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.IsCurrent(), Is.True);
            Assert.That(allocated, Is.LessThan(512),
                "A query with no credits must stop before per-occurrence allocations.");
        }

        [Test]
        public void PocOccurrenceStopsBeforeUnfundedShapeAllocations()
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var keyword = new KeywordExpression("return", null);
            var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
            var method = new MethodProvider(signature, new MethodBodyStatements([new ExpressionStatement(keyword)]), owner);
            owner.Update(methods: [method]);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, keyword));
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var occurrenceType = occurrence.GetType();
            foreach (var field in new[] { "_constructorFrame", "_parameterFrame", "_targetTypeSnapshot", "_methodReturnSnapshot" })
            {
                Assert.That(occurrenceType.GetField(field, flags)!.GetValue(occurrence), Is.Null,
                    $"The one-credit query must reach shape capture rather than stop at {field}.");
            }
            var generalShape = new AssignmentExpression(new DeclarationExpression(typeof(@class), "value"), keyword);
            var generalShort = new PocWorkBudget(6);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(generalShape, false, generalShort), Is.Null);
            Assert.That(generalShort.Exhausted, Is.True);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(generalShape, false, new PocWorkBudget(7)), Is.Not.Null,
                "The general profile still reserves its shape and all six lists before capture.");
            var shortReservation = new PocWorkBudget(3);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(keyword, false, shortReservation), Is.Null);
            Assert.That(shortReservation.Exhausted, Is.True);
            Assert.That(StructuredSourceShape.CaptureForPocRecheck(keyword, false, new PocWorkBudget(4)), Is.Not.Null,
                "An exact keyword object reservation may proceed to the separately funded shape work.");
            Assert.That(occurrence.IsCurrentWithoutInvalidating(new PocWorkBudget(4096)), Is.True,
                "The actual captured keyword occurrence must be supported with adequate credits.");
            Assert.That(file.IsCurrent(), Is.True);
            var beforeQueries = proofCalls;
            for (int i = 0; i < 3; i++) { Assert.That(occurrence.IsCurrentWithoutInvalidating(new PocWorkBudget(1)), Is.False); }
            var budgets = new PocWorkBudget[64];
            for (int i = 0; i < budgets.Length; i++) { budgets[i] = new PocWorkBudget(1); }
            bool allRejected = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in budgets)
            {
                if (occurrence.IsCurrentWithoutInvalidating(budget)) { allRejected = false; }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allRejected, Is.True);
            Assert.That(budgets.All(budget => budget.Exhausted), Is.True);
            Assert.That(proofCalls, Is.EqualTo(beforeQueries), "The POC check must not replay physical source callbacks.");
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.IsCurrent(), Is.True, "POC exhaustion must not stale the shared file.");
            Assert.That(allocated, Is.LessThan(4096),
                "A query with no credits after its occurrence entry cannot allocate an input-sized batch of shape objects.");
        }
    }
}

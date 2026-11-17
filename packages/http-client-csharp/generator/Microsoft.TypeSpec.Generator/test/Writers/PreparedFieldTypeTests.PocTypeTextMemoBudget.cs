// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void PocTypeTextMemoUsesIdentityAndFundsProbesAndPublication()
        {
            var memo = new PocTypeTextMemo();
            var first = new string('A', 32);
            var equalButDistinct = new string('A', 32);
            Assert.That(ReferenceEquals(first, equalButDistinct), Is.False);
            var shortProbe = new PocWorkBudget(3);
            Assert.That(memo.TryFind(first, false, shortProbe, out var found), Is.False);
            Assert.That(found, Is.False);
            Assert.That(shortProbe.Exhausted, Is.True);
            var exactProbe = new PocWorkBudget(4);
            Assert.That(memo.TryFind(first, false, exactProbe, out found), Is.True);
            Assert.That(found, Is.False);
            Assert.That(exactProbe.Remaining, Is.Zero);
            var failedPublish = new PocWorkBudget(0);
            Assert.That(memo.TryRemember(first, false, failedPublish), Is.False);
            Assert.That(failedPublish.Exhausted, Is.True);
            Assert.That(memo.TryFind(first, false, new PocWorkBudget(4), out found), Is.True);
            Assert.That(found, Is.False, "An unfunded publication cannot make a later query hit.");
            var publication = new PocWorkBudget(1);
            Assert.That(memo.TryRemember(first, false, publication), Is.True);
            Assert.That(publication.Remaining, Is.Zero);
            Assert.That(memo.TryFind(first, false, new PocWorkBudget(1), out found), Is.True);
            Assert.That(found, Is.True);
            Assert.That(memo.TryFind(equalButDistinct, false, new PocWorkBudget(4), out found), Is.True);
            Assert.That(found, Is.False, "Equal contents are not an identity witness.");
            Assert.That(memo.TryFind(first, true, new PocWorkBudget(4), out found), Is.True);
            Assert.That(found, Is.False, "Identifier and namespace facts are distinct.");
            var replacements = Enumerable.Range(0, 4).Select(index => new string((char)('B' + index), 32)).ToArray();
            foreach (var value in replacements) { Assert.That(memo.TryRemember(value, false, new PocWorkBudget(1)), Is.True); }
            Assert.That(memo.TryFind(first, false, new PocWorkBudget(4), out found), Is.True);
            Assert.That(found, Is.False, "The bounded cache may evict but must not return a false hit.");
            var parent = new PocWorkBudget(1);
            var child = new PocWorkBudget(10, parent);
            Assert.That(memo.TryFind(equalButDistinct, false, child, out found), Is.False);
            Assert.That(found, Is.False);
            Assert.That(parent.Exhausted, Is.True);
            Assert.That(child.Exhausted, Is.True);
            Assert.That(memo.TryFind(replacements[3], false, new PocWorkBudget(1), out found), Is.True);
            Assert.That(found, Is.True, "A separately funded query remains usable.");
        }

        [Test]
        public void PocTypeTextMemoRetainsLiveSyntaxChecksAcrossMutation()
        {
            using var host = new BodyOnlyPhysicalHost();
            var owner = new TestTypeProvider(name: "ValidIdentifier", ns: "Valid.Namespace");
            owner.EnsureBuilt();
            var type = owner.Type;
            var originalName = type.Name;
            var originalNamespace = type.Namespace;
            var variable = new VariableExpression(type, "value");
            var memo = new PocTypeTextMemo();
            StructuredSourceShape Capture()
            {
                var shape = StructuredSourceShape.CaptureForPocRecheck(variable, true, new PocWorkBudget(4096), memo);
                Assert.That(shape, Is.Not.Null);
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Assert.That(typeof(StructuredSourceShape).GetField("_typeTextMemo", flags)!.GetValue(shape), Is.Null,
                    "A captured shape must not retain the per-query memo.");
                return shape!;
            }
            Assert.That(Capture().IntrinsicTokens, Is.True);
            type.Update(name: "Invalid-Name");
            Assert.That(Capture().IntrinsicTokens, Is.False, "A new invalid name is not authorized by a previous valid string.");
            type.Update(name: originalName);
            Assert.That(ReferenceEquals(type.Name, originalName), Is.True);
            Assert.That(Capture().IntrinsicTokens, Is.True, "Reusing the same immutable name fact after restoration remains correct.");
            type.Update(@namespace: "Invalid.-Namespace");
            Assert.That(Capture().IntrinsicTokens, Is.False);
            type.Update(@namespace: originalNamespace);
            Assert.That(ReferenceEquals(type.Namespace, originalNamespace), Is.True);
            Assert.That(Capture().IntrinsicTokens, Is.True);
            type.Update(@namespace: "Valid..Namespace");
            Assert.That(Capture().IntrinsicTokens, Is.True, "Empty namespace segments retain the existing POC syntax behavior.");
            type.Update(name: "9Invalid");
            Assert.That(Capture().IntrinsicTokens, Is.False);
            type.Update(name: "Éclair");
            Assert.That(Capture().IntrinsicTokens, Is.False);
            type.Update(name: "class");
            Assert.That(Capture().IntrinsicTokens, Is.True, "This syntax check does not itself reject C# keywords.");
        }

        [Test]
        public void PocParameterFrameStopsBeforeUnfundedEntryAllocations()
        {
            using var host = new BodyOnlyPhysicalHost();
            var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
            var call = new InvokeMethodExpression(TypeReferenceExpression.FromType(typeof(PreparedCastCallProbe)),
                "ObserveObject", [new LiteralExpression(1)]);
            var owner = new ParameterValueOwner(call, new[] { parameter }, emptyDocs: true);
            owner.EnsureBuilt();
            _ = owner.Type;
            var method = owner.Methods.Single();
            var body = (object?)method.BodyStatements ?? method.BodyExpression!;
            var frame = PreparedMethodParameterFrame.TryCapture(method, body);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            for (int i = 0; i < 3; i++) { Assert.That(frame.IsCurrentWithoutInvalidating(new PocWorkBudget(0)), Is.False); }
            var budgets = new PocWorkBudget[64];
            for (int i = 0; i < budgets.Length; i++) { budgets[i] = new PocWorkBudget(0); }
            bool allRejected = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var budget in budgets)
            {
                if (frame.IsCurrentWithoutInvalidating(budget)) { allRejected = false; }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allRejected, Is.True);
            Assert.That(budgets.All(budget => budget.Exhausted), Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            Assert.That(frame.IsCurrent(), Is.True, "POC exhaustion must not stale the native frame.");
            Assert.That(allocated, Is.LessThan(512),
                "A parameter-frame query with no credits must stop before per-query allocations.");
        }

        [Test]
        public void PocFullSourceRepeatedTypeTextFitsExistingLocalBudget()
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var target = new TestTypeProvider(name: new string('T', 128), ns: "N." + new string('N', 128));
            target.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(target);
            var repeatedType = target.Type;
            var owner = new TestTypeProvider(name: "RepeatedTextOwner");
            var methods = Enumerable.Range(0, 8).Select(index =>
            {
                var parameter = new ParameterProvider("value" + index, $"value", repeatedType, validation: ParameterValidationType.None);
                var signature = new MethodSignature("Probe" + index, null, MethodSignatureModifiers.Private,
                    repeatedType, null, [parameter]);
                return new MethodProvider(signature, new MethodBodyStatements([new ExpressionStatement(new LiteralExpression(null))]),
                    owner, new XmlDocProvider());
            }).ToArray();
            owner.Update(methods: methods);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner, target }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
            Assert.That(file.IsCurrent(), Is.True);
            var beforeQueries = proofCalls;
            var full = new PocWorkBudget(65536);
            Assert.That(file.IsCurrentWithoutInvalidating(full), Is.True,
                "The actual repeated-type file must be supported with the existing full local limit.");
            var limited = new PocWorkBudget(8192);
            var limitedCurrent = file.IsCurrentWithoutInvalidating(limited);
            Assert.That(limitedCurrent, Is.True,
                "Repeated validation of the same immutable name and namespace should fit without skipping live source checks.");
            Assert.That(proofCalls, Is.EqualTo(beforeQueries), "The additional source query must not replay physical proof callbacks.");
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.IsCurrent(), Is.True, "A POC-only exhaustion must not stale the shared file.");
            var originalNamespace = repeatedType.Namespace;
            repeatedType.Update(@namespace: "Different.Namespace");
            Assert.That(file.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False,
                "A fresh query must observe a real type mutation even after earlier syntax-cache hits.");
            repeatedType.Update(@namespace: originalNamespace);
            Assert.That(file.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False,
                "Revisioned source mutation remains stale after restoration.");
        }
    }
}

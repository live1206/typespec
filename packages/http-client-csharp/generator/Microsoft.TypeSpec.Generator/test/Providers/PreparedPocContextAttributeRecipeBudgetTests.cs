// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocContextAttributeRecipeBudgetTests
    {
        [TestCase("cycle")]
        [TestCase("depth")]
        [TestCase("repeated")]
        public void PocTypeSnapshotKeepsCycleDepthAndExpansionVetoes(string shape)
        {
            var arguments = new CSharpType[shape == "cycle" ? 1 : 0];
            CSharpType root = new("T", "N", false, false, null, arguments, true, false);
            if (shape == "cycle") { arguments[0] = root; }
            else if (shape == "depth")
            {
                for (int i = 0; i < 260; i++) { root = new CSharpType("T", "N", false, false, root, Array.Empty<CSharpType>(), true, false); }
            }
            else
            {
                for (int i = 0; i < 20; i++) { root = new CSharpType("T", "N", false, false, null, new[] { root, root }, true, false); }
            }
            Assert.That(DeclaredTypeSnapshot.TryCaptureForPoc(root, new PocWorkBudget(4096), out var snapshot), Is.False);
            Assert.That(snapshot, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OriginalAttributeSnapshotBoundsSparseCapacityBeforeEnumeration(bool retainOne)
        {
            var type = new CSharpType(typeof(ObsoleteAttribute));
            var originals = new HashSet<MethodBodyStatement>();
            var elements = Enumerable.Range(0, 16384).Select(_ => new AttributeStatement(type, [], [])).ToArray();
            foreach (var item in elements) { originals.Add(item); }
            for (int i = retainOne ? 1 : 0; i < elements.Length; i++) { originals.Remove(elements[i]); }
            Assert.That(originals.Count, Is.EqualTo(retainOne ? 1 : 0));
            Assert.That(originals.EnsureCapacity(0), Is.GreaterThan(8192));
            var owner = new TestTypeProvider();
            typeof(TypeProvider).GetField("_attributes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, Array.Empty<MethodBodyStatement>());
            typeof(TypeProvider).GetField("_originalAttributes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, originals);
            var capture = typeof(PreparedPocContextAttributeRecipe).GetMethod("TryAttributeInputs", BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] sparse = [owner, new PocWorkBudget(4096), null, null, null];
            Assert.That((bool)capture.Invoke(null, sparse)!, Is.False,
                "A sparse original set must be rejected before scanning its deleted slots; live Count is not its enumeration bound.");
            originals.TrimExcess();
            object?[] compact = [owner, new PocWorkBudget(4096), null, null, null];
            Assert.That((bool)capture.Invoke(null, compact)!, Is.True, "The same compact native original contents remain admitted.");
        }

        [Test]
        public void VisitorHashBudgetIncludesPreflightAndNativeOriginalSetProbes()
        {
            const int originalCount = 64;
            const int visitorCount = 32;
            var originals = new HashSet<MethodBodyStatement>();
            var capacity = originals.EnsureCapacity(originalCount);
            var type = new CSharpType(typeof(ObsoleteAttribute));
            var collisions = new List<AttributeStatement>();
            for (var candidate = 0; candidate < 100000 && collisions.Count < originalCount + visitorCount; candidate++)
            {
                var attribute = new AttributeStatement(type, [], []);
                if ((uint)attribute.GetHashCode() % (uint)capacity == 0) { collisions.Add(attribute); }
            }
            Assert.That(collisions.Count, Is.EqualTo(originalCount + visitorCount), "The bounded fixture must obtain enough actual identity-hash bucket collisions.");
            foreach (var attribute in collisions.Take(originalCount)) { originals.Add(attribute); }
            Assert.That(originals.EnsureCapacity(0), Is.EqualTo(capacity), "All original keys must remain in the selected actual bucket.");
            var visitors = collisions.Skip(originalCount).Cast<MethodBodyStatement>().ToArray();
            Assert.That(visitors.All(value => !originals.Contains(value)), Is.True);
            var owner = new TestTypeProvider();
            typeof(TypeProvider).GetField("_attributes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, visitors);
            typeof(TypeProvider).GetField("_originalAttributes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, originals);
            // Enough for the old single original-probe reservation and copies,
            // but less than the actual two 32*64 collision-chain traversals,
            // even before native visitor-set construction is considered.
            var onePassBudget = 16 + 2 * visitorCount + originalCount + visitorCount * (originalCount + visitorCount + 4);
            Assert.That(onePassBudget, Is.LessThan(2 * visitorCount * originalCount));
            var capture = typeof(PreparedPocContextAttributeRecipe).GetMethod("TryAttributeInputs", BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] arguments = [owner, new PocWorkBudget(onePassBudget), null, null, null];
            Assert.That((bool)capture.Invoke(null, arguments)!, Is.False,
                "Reserve preflight hashing AND each future native pass before performing the corresponding probes.");
        }
    }
}

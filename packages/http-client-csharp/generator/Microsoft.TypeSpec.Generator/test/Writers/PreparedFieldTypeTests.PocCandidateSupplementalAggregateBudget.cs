// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public void PocCandidateSupplementalLocalExhaustionDoesNotVetoOtherFiles()
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var target = Target();
                var text = new string('x', 3000);
                var owners = new[] { new CandidateBudgetOwner("Large", target.Type), new CandidateBudgetOwner("Small", target.Type) };
                var docs = new XmlDocProvider[owners.Length][];
                for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
                {
                    var owner = owners[ownerIndex];
                    var declaration = new DeclarationExpression(typeof(@class), "value");
                    var methods = new List<MethodProvider>
                    {
                        new(new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []),
                            new MethodBodyStatements([new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate()]), owner)
                    };
                    var count = ownerIndex == 0 ? 24 : 1;
                    docs[ownerIndex] = new XmlDocProvider[count];
                    for (int docIndex = 0; docIndex < count; docIndex++)
                    {
                        var doc = new XmlDocProvider(new XmlDocSummaryStatement([$"{text}"]));
                        Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(doc), Is.Not.Null);
                        docs[ownerIndex][docIndex] = doc;
                        methods.Add(new MethodProvider(new MethodSignature("Documented" + docIndex, null, MethodSignatureModifiers.Public, null, null, []),
                            new KeywordExpression("default", null), owner, doc));
                    }
                    owner.Update(methods: methods.ToArray());
                    owner.EnsureBuilt();
                }
                target.EnsureBuilt();
                var roots = owners.Cast<TypeProvider>().Append(target).ToArray();
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                {
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
                }
                using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                Assert.That(prepared!.PocCandidateCount, Is.EqualTo(2));
                var files = owners.Select(owner => prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner))).ToArray();
                var large = prepared.GetPocSupplementalFile(files[0]);
                var small = prepared.GetPocSupplementalFile(files[1]);
                Assert.That(large, Is.Not.Null);
                Assert.That(large!.IsRejected, Is.True, "The independently supported long documents exceed only this file's inventory cap.");
                Assert.That(small, Is.Not.Null);
                Assert.That(small!.IsRejected, Is.False, "A different file remains supported.");
                var smallDoc = files[1].Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, docs[1][0]));
                Assert.That(small.GetDocumentation(smallDoc.Id), Is.Not.Null);
                var phaseField = typeof(PreparedFieldTypes).GetField("_pocCandidateSourceBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.That(((PocWorkBudget)phaseField.GetValue(prepared)!).Exhausted, Is.False);
                Assert.That(files.All(file => file.IsCurrent()), Is.True, "Local POC exhaustion does not stale shared source.");
            }
            finally { File.Delete(codeModel); }
        }

        [Test]
        public void PocCandidateSupplementalInventoriesSharePlanningWorkLimit()
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var proofCalls = 0;
                host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
                var target = Target();
                const int ownerCount = 40;
                const int docsPerOwner = 8;
                var text = new string('x', 3000);
                var owners = new CandidateBudgetOwner[ownerCount];
                var declarations = new DeclarationExpression[ownerCount];
                var docs = new XmlDocProvider[ownerCount, docsPerOwner];
                for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
                {
                    var owner = new CandidateBudgetOwner("Documented" + ownerIndex, target.Type);
                    var declaration = new DeclarationExpression(typeof(@class), "value");
                    declarations[ownerIndex] = declaration;
                    var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
                    var methods = new List<MethodProvider>
                    {
                        new(new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []),
                            new MethodBodyStatements([assignment.Terminate()]), owner)
                    };
                    for (int docIndex = 0; docIndex < docsPerOwner; docIndex++)
                    {
                        var doc = new XmlDocProvider(new XmlDocSummaryStatement([$"{text}"]));
                        docs[ownerIndex, docIndex] = doc;
                        methods.Add(new MethodProvider(
                            new MethodSignature("Documented" + docIndex, null, MethodSignatureModifiers.Public, null, null, []),
                            new KeywordExpression("default", null), owner, doc));
                    }
                    owner.Update(methods: methods.ToArray());
                    owner.EnsureBuilt();
                    owners[ownerIndex] = owner;
                }
                target.EnsureBuilt();
                var roots = owners.Cast<TypeProvider>().Append(target).ToArray();
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                {
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
                }
                using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var files = prepared!.SourceFiles.Where(file => owners.Any(owner => ReferenceEquals(owner, file.FileOwner))).ToArray();
                Assert.That(files.Length, Is.EqualTo(ownerCount));
                var firstFile = files.Single(file => ReferenceEquals(file.FileOwner, owners[0]));
                var firstOccurrence = firstFile.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Single(node => ReferenceEquals(node.Producer, declarations[0]));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(firstFile, firstOccurrence);
                var phaseField = typeof(PreparedFieldTypes).GetField("_pocCandidateSourceBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var phase = (PocWorkBudget)phaseField.GetValue(prepared)!;
                int candidates = prepared.PocCandidateCount;
                Assert.That(candidates, Is.LessThanOrEqualTo(ownerCount));
                if (candidates == 0)
                {
                    Assert.That(phase.Exhausted, Is.True, "Only actual planning exhaustion may veto this optional batch.");
                    var supplementalField = typeof(PreparedFieldTypes).GetField("_pocSupplementalFiles", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var captured = (Dictionary<PreparedSourceFile, PreparedPocSupplementalFile>)supplementalField.GetValue(prepared)!;
                    Assert.That(captured.Count, Is.InRange(1, ownerCount - 1),
                        "The real shared budget must exhaust during supplemental captures, not before them or after the whole batch.");
                    var completed = captured.First();
                    Assert.That(completed.Value.IsRejected, Is.False);
                    var ownerIndex = Array.FindIndex(owners, owner => ReferenceEquals(owner, completed.Key.FileOwner));
                    Assert.That(ownerIndex, Is.GreaterThanOrEqualTo(0));
                    var docOccurrence = completed.Key.Roots.SelectMany(root => root.DescendantsAndSelf())
                        .Single(node => node.IsOpaque && ReferenceEquals(node.Producer, docs[ownerIndex, 0]));
                    Assert.That(completed.Value.GetDocumentation(docOccurrence.Id), Is.Not.Null,
                        "At least one actual long-document capture completed before the phase veto.");
                    Assert.That(prepared.GetPocSupplementalFile(completed.Key), Is.Null,
                        "An incomplete batch must not expose even a completed per-file observation.");
                    Assert.That(candidate, Is.Null);
                    Assert.That(prepared.PocCandidateFilePaths, Is.Empty);
                }
                else
                {
                    Assert.That(candidates, Is.EqualTo(ownerCount), "The baseline must reach every real documented candidate file.");
                    Assert.That(candidate, Is.Not.Null);
                    Assert.That(candidate!.SimpleSpelling, Is.EqualTo("@class"));
                    Assert.That(prepared.PocCandidateFilePaths.Length, Is.EqualTo(ownerCount));
                    var beforeQueries = proofCalls;
                    for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
                    {
                        var file = files.Single(item => ReferenceEquals(item.FileOwner, owners[ownerIndex]));
                        var supplemental = prepared.GetPocSupplementalFile(file);
                        Assert.That(supplemental, Is.Not.Null);
                        Assert.That(supplemental!.IsRejected, Is.False);
                        Assert.That(supplemental.HasUnavailableDocumentation, Is.False);
                        var occurrences = file.Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
                        for (int docIndex = 0; docIndex < docsPerOwner; docIndex++)
                        {
                            var doc = docs[ownerIndex, docIndex];
                            Assert.That(doc.Summary!.Lines.Single().GetArgument(0), Is.EqualTo(text));
                            var occurrence = occurrences.Single(node => node.IsOpaque && ReferenceEquals(node.Producer, doc));
                            Assert.That(supplemental.GetDocumentation(occurrence.Id), Is.Not.Null);
                        }
                    }
                    Assert.That(proofCalls, Is.EqualTo(beforeQueries), "Point checks must not replay physical source callbacks.");
                }
                var content = new TypeProviderWriter(owners[0]) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(content, Does.Contain("value"));
                Assert.That(firstFile.IsInvalid, Is.False);
                long mandatoryCharacters = (long)candidates * docsPerOwner * text.Length;
                long charged = 1048576L - phase.Remaining;
                Assert.That(charged, Is.GreaterThanOrEqualTo(mandatoryCharacters),
                    "The planning subledger must include at least each actual supplemental documentation capture's characters.");
            }
            finally { File.Delete(codeModel); }
        }
    }
}

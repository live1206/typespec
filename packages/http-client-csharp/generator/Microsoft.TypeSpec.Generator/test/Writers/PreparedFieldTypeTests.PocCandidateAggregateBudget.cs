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
        private sealed class CandidateBudgetOwner(string name, CSharpType fieldType) : TypeProvider
        {
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => name + ".cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class;
            protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, fieldType, "_value", this)];
        }

        [Test]
        public void PocCandidateCaptureSharesOneGenerationWorkLimit()
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var proofCalls = 0;
                host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
                var target = Target();
                const int ownerCount = 50;
                const int localCount = 20;
                const int width = 400;
                var owners = new CandidateBudgetOwner[ownerCount];
                var methods = new MethodProvider[ownerCount];
                var declarations = new DeclarationExpression[ownerCount, localCount];
                for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
                {
                    var owner = new CandidateBudgetOwner("Clock" + ownerIndex, target.Type);
                    var statements = new MethodBodyStatement[width];
                    for (int i = 0; i < localCount; i++)
                    {
                        var declaration = new DeclarationExpression(typeof(@class), "value" + i);
                        declarations[ownerIndex, i] = declaration;
                        statements[i] = new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate();
                    }
                    for (int i = localCount; i < width; i++) { statements[i] = new ExpressionStatement(new KeywordExpression("return", null)); }
                    var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                    methods[ownerIndex] = new MethodProvider(signature, new MethodBodyStatements(statements), owner);
                    owner.Update(methods: [methods[ownerIndex]]);
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
                var ownerFiles = prepared!.SourceFiles.Where(file => owners.Any(owner => ReferenceEquals(owner, file.FileOwner))).ToArray();
                Assert.That(ownerFiles.Length, Is.EqualTo(ownerCount));
                var file = ownerFiles.Single(source => ReferenceEquals(source.FileOwner, owners[0]));
                Assert.That(file.IsInvalid, Is.False);
                Assert.That(file.HasStarted, Is.False);
                var nodes = file.Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
                var block = nodes.Single(node => ReferenceEquals(node.Producer, methods[0].BodyStatements));
                Assert.That(block.Children.Length, Is.EqualTo(width));
                var occurrence = nodes.Single(node => ReferenceEquals(node.Producer, declarations[0, 0]));
                var sampleBudget = new PocWorkBudget(65536);
                var beforeObservation = proofCalls;
                var sample = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, sampleBudget);
                Assert.That(sample, Is.Not.Null, "Each actual file must remain individually supported by the existing source limit.");
                Assert.That(65536 - sampleBudget.Remaining, Is.GreaterThanOrEqualTo(2 * width));
                Assert.That(proofCalls, Is.EqualTo(beforeObservation), "The diagnostic observation must not replay the physical host callback.");
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence);
                var ownerPaths = new HashSet<string>(ownerFiles.Select(source => source.FilePath), StringComparer.Ordinal);
                Assert.That(prepared.PocCandidateFilePaths.All(ownerPaths.Contains), Is.True);
                int candidates = prepared.PocCandidateCount;
                Assert.That(candidates, Is.LessThanOrEqualTo(ownerCount * localCount));
                object? partialHeader = null;
                if (candidates == 0)
                {
                    Assert.That(candidate, Is.Null);
                    Assert.That(prepared.PocCandidateFilePaths, Is.Empty);
                    var phase = typeof(PreparedFieldTypes).GetField("_pocCandidateSourceBudget", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(phase, Is.Not.Null, "An incomplete optional batch must have exhausted its actual capture budget.");
                    Assert.That(((PocWorkBudget)phase!.GetValue(prepared)!).Exhausted, Is.True);
                    var headers = (System.Collections.IDictionary)typeof(PreparedFieldTypes)
                        .GetField("_pocHeaders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared)!;
                    partialHeader = headers[owners[0]];
                    Assert.That(partialHeader, Is.Not.Null, "A real partial header exists before the batch is vetoed.");
                }
                else
                {
                    Assert.That(candidate, Is.Not.Null, "A retained first-file candidate must remain supported.");
                    Assert.That(candidate!.SimpleSpelling, Is.EqualTo("@class"));
                }

                // Each stored candidate necessarily passed TryCapture's direct
                // path reservation, its initial full-source currentness path,
                // and the later source.IsCurrent check. Finalization checks
                // the source again for every retained candidate. Each of these
                // four paths inspects that candidate's actual 400-child block.
                // Other scans, hashes, allocations and Roslyn lookups are excluded.
                long mandatoryBlockVisits = 4L * width * candidates;
                var content = new TypeProviderWriter(owners[0]) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(content, Does.Contain("value0"));
                Assert.That(content, Does.Contain("value19"));
                if (partialHeader is not null)
                {
                    var headerType = partialHeader.GetType();
                    const BindingFlags headerFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                    Assert.That((bool)headerType.GetField("WarningsSeen", headerFlags)!.GetValue(partialHeader)!, Is.False,
                        "A vetoed partial batch must not observe native warning output.");
                    Assert.That((bool)headerType.GetField("DocsSeen", headerFlags)!.GetValue(partialHeader)!, Is.False,
                        "A vetoed partial batch must not observe native documentation output.");
                }
                Assert.That(file.IsInvalid, Is.False, "A POC-only limit must not poison the shared source.");
                Assert.That(mandatoryBlockVisits, Is.LessThanOrEqualTo(1048576),
                    "A single generation-wide capture must fund even the mandatory block visits for its stored candidates.");
            }
            finally { File.Delete(codeModel); }
        }

        [Test]
        public void UnderfundedCandidateSourceQueryDoesNotInvalidateSupportedCandidate()
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var proofCalls = 0;
                host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
                var target = Target();
                var owner = new FieldOwner(target.Type);
                var declaration = new DeclarationExpression(typeof(@class), "value");
                var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
                var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                owner.Update(methods: [new MethodProvider(signature, new MethodBodyStatements([assignment.Terminate()]), owner)]);
                owner.EnsureBuilt(); target.EnsureBuilt();
                var roots = new TypeProvider[] { owner, target };
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                {
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
                }
                using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var file = prepared!.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence);
                Assert.That(candidate, Is.Not.Null, "The actual source and namespace candidate must be supported before the query.");
                Assert.That(candidate!.SourceObservation.IsCurrent, Is.True);
                var beforeQuery = proofCalls;
                var tooSmall = new PocWorkBudget(0);
                Assert.That(candidate.SourceObservation.IsCurrentForPoc(tooSmall), Is.False);
                Assert.That(tooSmall.Exhausted, Is.True);
                Assert.That(file.IsInvalid, Is.False);
                Assert.That(candidate.SourceObservation.IsCurrentForPoc(new PocWorkBudget(65536)), Is.True,
                    "A separate underfunded observation cannot make an unchanged supported source stale.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.SameAs(candidate));
                Assert.That(proofCalls, Is.EqualTo(beforeQuery), "The POC queries must not replay physical source callbacks.");
            }
            finally { File.Delete(codeModel); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnderfundedCandidateAuxiliaryQueryDoesNotInvalidateSupportedCandidate(bool relatedScope)
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var proofCalls = 0;
                host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
                var target = Target();
                var owner = new FieldOwner(target.Type);
                var declaration = new DeclarationExpression(typeof(@class), "value");
                var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
                var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                owner.Update(methods: [new MethodProvider(signature, new MethodBodyStatements([assignment.Terminate()]), owner)]);
                owner.EnsureBuilt(); target.EnsureBuilt();
                var roots = new TypeProvider[] { owner, target };
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                {
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
                }
                using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var file = prepared!.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence);
                Assert.That(candidate, Is.Not.Null, "A real compact candidate must be supported before either point query.");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var preparedType = typeof(PreparedFieldTypes);
                var method = preparedType.GetMethod(relatedScope ? "PocRelatedScopesAreCurrent" : "PocObservedInputsAreCurrent", flags)!;
                object[] Arguments(PocWorkBudget budget) => relatedScope ? [file, budget] : [budget];
                Assert.That(method.Invoke(prepared, Arguments(new PocWorkBudget(262144))), Is.True);
                var beforeQuery = proofCalls;
                int insufficient = 0;
                if (relatedScope)
                {
                    object Field(string name) => preparedType.GetField(name, flags)!.GetValue(prepared)!;
                    int Count(object value) => (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
                    var owners = (System.Collections.IDictionary)Field("_owners");
                    var state = owners[owner]!;
                    var parts = (Array)state.GetType().GetProperty("Parts")!.GetValue(state)!;
                    int invalidCount = Count(Field("_pocInvalidCandidateFiles"));
                    int unsupportedCount = Count(Field("_pocUnsupportedOwners"));
                    int scopeCount = Count(Field("_pocScopeNames"));
                    int fileCount = Count(Field("_sourceFiles"));
                    // Fund the initial owner lookup, rejection reservation,
                    // part count and scope lookup exactly, leaving no credits
                    // for the first actual scope snapshot check.
                    insufficient = owners.Count + 1 + parts.Length * (fileCount + invalidCount + parts.Length + 2)
                        + parts.Length + unsupportedCount + 1 + scopeCount + 1;
                }
                var shortBudget = new PocWorkBudget(insufficient);
                Assert.That(method.Invoke(prepared, Arguments(shortBudget)), Is.False);
                Assert.That(shortBudget.Exhausted, Is.True, "The query must fail due to budget, not a source mismatch.");
                Assert.That(file.IsInvalid, Is.False);
                Assert.That(method.Invoke(prepared, Arguments(new PocWorkBudget(262144))), Is.True,
                    "A separate underfunded query cannot invalidate unchanged POC evidence.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.SameAs(candidate));
                Assert.That(proofCalls, Is.EqualTo(beforeQuery), "The POC point checks must not replay physical source callbacks.");
            }
            finally { File.Delete(codeModel); }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void MalformedNestedDeclarationPreservesExistingPocExceptionBoundary(bool isOut, bool throwsDuringPreparation)
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var target = Target();
                var owner = new FieldOwner(target.Type);
                var valid = new DeclarationExpression(typeof(@class), "value");
                var malformed = new DeclarationExpression((VariableExpression)null!, isOut);
                var nested = new InvokeMethodExpression(null, "Consume", [malformed]);
                var statements = new MethodBodyStatement[]
                {
                    new AssignmentExpression(valid, new KeywordExpression("default", null)).Terminate(),
                    nested.Terminate()
                };
                var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                owner.Update(methods: [new MethodProvider(signature, new MethodBodyStatements(statements), owner)]);
                owner.EnsureBuilt(); target.EnsureBuilt();
                var roots = new TypeProvider[] { owner, target };
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                {
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
                }
                if (throwsDuringPreparation)
                {
                    Assert.Throws<NullReferenceException>(() => PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider),
                        "The original POC path observes the malformed non-out declaration before native writing.");
                }
                else
                {
                    using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                    Assert.That(prepared, Is.Not.Null, "An out declaration follows the original short-circuit path.");
                    Assert.Throws<NullReferenceException>(() => new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write(),
                        "The original native writer retains its malformed declaration exception.");
                }
            }
            finally { File.Delete(codeModel); }
        }
    }
}

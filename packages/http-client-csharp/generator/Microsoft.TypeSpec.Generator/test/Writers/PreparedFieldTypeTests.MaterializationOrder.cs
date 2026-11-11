// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        [TestCase(false)]
        [TestCase(true)]
        public void StandardWriterMaterializesHeaderInOriginalOrderAndOnlyOnce(bool xmlEnabled)
        {
            MockHelpers.LoadMockGenerator(configuration: "{\"disable-xml-docs\":" + (xmlEnabled ? "false" : "true") + ",\"package-name\":\"Sample.Namespace\"}");
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            Assert.That(events, Is.Empty);

            var file = new TypeProviderWriter(owner).Write();
            Assert.That(events, Is.EqualTo(new[] { "attributes", "warnings", "docs", "path" }));
            if (xmlEnabled)
            {
                Assert.That(file.Content, Does.Contain("/// <summary> Original documentation. </summary>"));
            }
            else
            {
                Assert.That(file.Content, Does.Not.Contain("Original documentation"));
            }
            Assert.That(owner.CachedXmlDocs, Is.Not.Null);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Not.Null);
        }

        [Test]
        public void PocHeaderVetoDoesNotReplayOrHoistLazyHeaderBuilders()
        {
            (string[] BeforeWrite, string[] AfterWrite, string Content) Run(bool capturePoc)
            {
                using var host = new BodyOnlyPhysicalHost();
                var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
                File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
                try
                {
                    var events = new List<string>();
                    var target = Target();
                    var owner = new PocHeaderOrderOwner(events, target.Type);
                    owner.EnsureBuilt();
                    var roots = new TypeProvider[] { owner, target };
                    var output = host.Generator.OutputLibrary;
                    output.TypeProviders = roots;
                    using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                    foreach (var provider in roots)
                    { ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type)); }
                    events.Clear();
                    using var prepared = capturePoc
                        ? PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider)
                        : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, referenceMap.ShouldWriteProvider);
                    Assert.That(prepared, Is.Not.Null);
                    if (capturePoc) { Assert.That(prepared!.PocCandidateCount, Is.GreaterThan(0)); }
                    var beforeWrite = events.ToArray();
                    var writer = host.Generator.GetWriter(owner);
                    writer.PreparedFieldTypes = prepared;
                    var file = writer.Write();
                    if (capturePoc)
                    {
                        var source = prepared!.SourceFiles.Single(candidate => ReferenceEquals(candidate.FileOwner, owner));
                        Assert.That(prepared.GetPocHeaderObservation(source), Is.True);
                    }
                    return (beforeWrite, events.ToArray(), file.Content);
                }
                finally { File.Delete(codeModel); }
            }

            var baseline = Run(capturePoc: false);
            var planned = Run(capturePoc: true);
            Assert.That(planned.BeforeWrite, Is.EqualTo(baseline.BeforeWrite), "POC planning must not hoist header callbacks.");
            Assert.That(planned.AfterWrite, Is.EqualTo(baseline.AfterWrite), "The standard writer must invoke the same callbacks in the same order.");
            Assert.That(planned.Content, Is.EqualTo(baseline.Content));
            Assert.That(planned.AfterWrite.Count(item => item == "docs"), Is.EqualTo(1));
            Assert.That(planned.AfterWrite.Count(item => item == "warnings"), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocHeaderVetoRejectsUnsafeWarningsWithoutChangingNativeWrite(bool nullJustification)
        {
            (string? Content, bool? Header, Exception? Failure) Run(bool capturePoc)
            {
                using var host = new BodyOnlyPhysicalHost();
                var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
                File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
                try
                {
                    var events = new List<string>();
                    var target = Target();
                    var malformedType = new CSharpType("Bad\nInjected", "Sample", false, false, null, [], true, false);
                    ValueExpression code = nullJustification ? new LiteralExpression("TEST") :
                        new Microsoft.TypeSpec.Generator.Snippets.ScopedApi(malformedType, new LiteralExpression("TEST"));
                    if (!nullJustification) { Assert.That(code.ToString(), Does.Contain("\n"), "The native fallback formatter must actually expose the unsafe newline."); }
                    var warning = new SuppressionStatement(null, code, nullJustification ? null! : "justification");
                    var owner = new PocHeaderOrderOwner(events, target.Type, [warning]);
                    owner.EnsureBuilt();
                    var roots = new TypeProvider[] { owner, target };
                    var output = host.Generator.OutputLibrary;
                    output.TypeProviders = roots;
                    using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                    foreach (var provider in roots)
                    { ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type)); }
                    using var prepared = capturePoc
                        ? PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider)
                        : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, referenceMap.ShouldWriteProvider);
                    Assert.That(prepared, Is.Not.Null);
                    if (capturePoc) { Assert.That(prepared!.PocCandidateCount, Is.GreaterThan(0)); }
                    var writer = host.Generator.GetWriter(owner);
                    writer.PreparedFieldTypes = prepared;
                    CodeFile? file = null;
                    Exception? failure = null;
                    try { file = writer.Write(); }
                    catch (Exception exception) { failure = exception; }
                    var source = prepared!.SourceFiles.Single(candidate => ReferenceEquals(candidate.FileOwner, owner));
                    return (file?.Content, prepared.GetPocHeaderObservation(source), failure);
                }
                finally { File.Delete(codeModel); }
            }

            var baseline = Run(capturePoc: false);
            var planned = Run(capturePoc: true);
            if (nullJustification)
            {
                Assert.That(baseline.Failure, Is.TypeOf<ArgumentNullException>(), "The native writer already rejects null justification.");
                Assert.That(planned.Failure, Is.TypeOf<ArgumentNullException>(), "POC observation must preserve the native exception type.");
                Assert.That(((ArgumentNullException)planned.Failure!).ParamName, Is.EqualTo(((ArgumentNullException)baseline.Failure!).ParamName));
            }
            else
            {
                Assert.That(baseline.Failure, Is.Null);
                Assert.That(planned.Failure, Is.Null);
                Assert.That(planned.Content, Is.EqualTo(baseline.Content));
            }
            Assert.That(planned.Header, Is.False, "Unsafe or unmodeled warning formatting must veto only the POC header fact.");
        }

        [Test]
        public void StandardWriterDoesNotMaterializeUnwrittenInternalTypeDocumentation()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: false);
            owner.EnsureBuilt();
            Assert.That(events, Is.Empty);

            var file = new TypeProviderWriter(owner).Write();
            Assert.That(events, Is.EqualTo(new[] { "attributes", "warnings", "path" }));
            Assert.That(file.Content, Does.Not.Contain("Original documentation"));
            Assert.That(owner.CachedXmlDocs, Is.Null);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void MaterializedHeaderPausePreservesSingleWriterAndCallbackOrder(bool isPublic, bool xmlEnabled)
        {
            MockHelpers.LoadMockGenerator(configuration: "{\"disable-xml-docs\":" + (xmlEnabled ? "false" : "true") + ",\"package-name\":\"Sample.Namespace\"}");
            var baselineEvents = new List<string>();
            var baseline = new MaterializationOrderOwner(baselineEvents, isPublic);
            baseline.EnsureBuilt();
            var expected = new TypeProviderWriter(baseline).Write();

            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic);
            owner.EnsureBuilt();
            var writer = new TypeProviderWriter(owner);
            XmlDocProvider? observed = null;
            var selected = false;
            var actual = WriteWithMaterializedHeader(writer, (current, writeDocs, docs) =>
            {
                events.Add("prepare");
                Assert.That(current, Is.SameAs(owner));
                selected = writeDocs;
                observed = docs;
                Assert.That(docs, Is.SameAs(owner.CachedXmlDocs));
            });
            Assert.That(actual.Content, Is.EqualTo(expected.Content));
            Assert.That(selected, Is.EqualTo(isPublic));
            Assert.That(observed is not null, Is.EqualTo(isPublic));
            Assert.That(events, Is.EqualTo(isPublic
                ? new[] { "attributes", "warnings", "docs", "prepare", "path" }
                : new[] { "attributes", "warnings", "prepare", "path" }));
        }

        [Test]
        public void MaterializedHeaderPauseDoesNotBypassCustomWriterOrReplay()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            var custom = new MaterializationCustomWriter(owner);
            Assert.Throws<System.Reflection.TargetInvocationException>(() => WriteWithMaterializedHeader(custom, (_, _, _) => events.Add("prepare")));
            Assert.That(events, Is.Empty);

            var writer = new TypeProviderWriter(owner);
            _ = WriteWithMaterializedHeader(writer, (_, _, _) => events.Add("prepare"));
            var count = events.Count;
            Assert.Throws<System.Reflection.TargetInvocationException>(() => WriteWithMaterializedHeader(writer, (_, _, _) => events.Add("replay")));
            Assert.That(events.Count, Is.EqualTo(count));
            Assert.That(events, Does.Not.Contain("replay"));
        }

        [Test]
        public void MaterializedHeaderPausePreservesPreparedFieldAndStickyFallback()
        {
            var target = Target("Other");
            var owner = new FieldOwner(target.Type);
            using var prepared = PreparedFieldTypes.Create([owner, target], ReferenceCompilation(), _ => true);
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            var callbacks = 0;
            var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
            var file = WriteWithMaterializedHeader(writer, (_, _, _) => callbacks++);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(file.Content, Does.Contain("private Widget _value;"));
            Assert.That(file.QualifiedContent, Does.Contain("private global::Other.Widget _value;"));
            Assert.That(prepared.CanCommit(), Is.True);
            var originalNested = owner.NestedTypes.ToArray();
            owner.Update(nestedTypes: [new Shadow(owner)]);
            Assert.That(prepared.CanCommit(), Is.False);
            owner.Update(nestedTypes: originalNested);
            Assert.That(prepared.CanCommit(), Is.False);
            Assert.That(file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true).Content, Is.EqualTo(file.QualifiedContent));
        }

        [Test]
        public void FieldlessSourceOnlySessionDoesNotCurrentlyHaveSharedCommitSnapshot()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            Assert.That(owner.Fields, Is.Empty);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.SourceFiles, Has.Length.EqualTo(1));
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False);
        }

        [Test]
        public void FieldlessProspectiveSessionOwnsSharedLifetimeBeforePause()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            Assert.That(owner.Fields, Is.Empty);
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.SourceFiles, Has.Length.EqualTo(1));
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False, "A dormant fieldless lifetime grants no shared commit.");
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True);
            Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
            var callbacks = 0;
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var output = WriteWithMaterializedHeader(writer, (_, _, _) => callbacks++);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(writer.PreparedFieldTypes, Is.SameAs(prepared));
            Assert.That(output.PreparedBodyNameCount, Is.Zero, "A prospective lifetime alone grants no name permission.");
            Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False);
            Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
            var originalNested = owner.NestedTypes.ToArray();
            owner.Update(nestedTypes: [new Shadow(owner)]);
            Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.False);
            owner.Update(nestedTypes: originalNested);
            Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.False);
        }

        [Test]
        public void FieldlessProspectiveSessionDetectsUnobservedOwnerChangeAndRestore()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
            var originalNested = owner.NestedTypes.ToArray();
            owner.Update(nestedTypes: [new Shadow(owner)]);
            owner.Update(nestedTypes: originalNested);
            Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.False,
                "A prospective owner revision must remain stale even if its visible members are restored before observation.");
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False, "The dormant lifetime must not claim a shared commit.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MethodCollectionChangeDoesNotInvalidateIndependentPreparedField(bool prospective)
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(string), null, Array.Empty<ParameterProvider>());
            var first = new MethodProvider(signature, new LiteralExpression("first"), owner);
            var replacement = new MethodProvider(signature, new LiteralExpression("replacement"), owner);
            owner.Update(methods: [first]);
            var methods = (MethodProvider[])owner.Methods;
            using var prepared = prospective
                ? CreateProspectiveForGeneration(host.Generator, [owner, target])
                : PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner, target }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            methods[0] = replacement;
            Assert.That(prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner)).IsCurrent(), Is.False);
            Assert.That(prepared.CanCommit(), Is.True, "A body/source-only change with the same scope name must not veto an independent field.");
            if (prospective)
            {
                Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.False);
                Assert.That(prepared.CanCommit(), Is.True, "Prospective invalidation must stay separate from the field channel.");
            }
            var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(output.Content, Does.Contain("private Widget _value;"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocPlanningPreservesExistingFieldlessBodyPermission(bool capturePoc)
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var owner = new OriginalTypeOfHostOwner(false);
                owner.EnsureBuilt();
                FinalizeGeneratedInstanceOwner(owner);
                var roots = new TypeProvider[] { owner };
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(owner.Type));
                using var prepared = capturePoc
                    ? PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider)
                    : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                Assert.That(prepared!.PocCandidateCount, Is.Zero, "The existing body permission is fieldless and is not a POC field-owner candidate.");
                var writer = host.Generator.GetWriter(owner);
                writer.PreparedFieldTypes = prepared;
                var file = writer.Write();
                Assert.That(file.PreparedBodyNameCount, Is.GreaterThan(0));
                Assert.That(prepared.CanCommitBodyNames(file), Is.True);
                Assert.That(file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true).Content, Does.Contain("return Pass(typeof(Exception));"));
            }
            finally { File.Delete(codeModel); }
        }

        [Test]
        public void PocOnlyObservedVetoKeepsIndependentSameWriteBodyChoice()
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            var roots = new TypeProvider[] { owner };
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, roots, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var source = prepared!.SourceFiles.Single();
            // Test-only standalone POC observation is prepared before the one
            // normal writer. The fieldless fixture has no production POC slot.
            var supplemental = PreparedPocSupplementalFile.Capture(source);
            supplemental.ObserveCurrent();
            Assert.That(supplemental.IsRejected, Is.False, "The control must begin with a current POC-only observation.");
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var file = writer.Write();
            Assert.That(file.PreparedBodyNameCount, Is.GreaterThan(0));
            Assert.That(prepared.CanCommitBodyNames(file), Is.True);
            supplemental.ObserveCurrent();
            Assert.That(supplemental.IsRejected, Is.False, "The one normal write must not reject the control.");
            var statements = (MethodBodyStatement[])((MethodBodyStatements)owner.M.BodyStatements!).Statements;
            var original = statements[0];
            try
            {
                statements[0] = new ExpressionStatement(new KeywordExpression("default", null));
                supplemental.ObserveCurrent();
                Assert.That(supplemental.IsRejected, Is.True);
            }
            finally { statements[0] = original; }
            Assert.That(source.IsCurrent(), Is.True, "A POC-only observation must not invalidate shared traversal.");
            Assert.That(prepared.CanCommitBodyNames(file), Is.True, "The independent body choice remains current.");
            Assert.That(file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true).Content, Does.Contain("return Pass(typeof(Exception));"));
        }

        [Test]
        public void ProspectiveSessionKeepsDeepBodyInvalidationSeparateFromSharedSnapshot()
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Not.Null);
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var output = WriteWithMaterializedHeader(writer, (_, _, _) => { });
            Assert.That(output.PreparedBodyNameCount, Is.GreaterThan(0));
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.True);
            Assert.That(prepared.CanCommitBodyNames(output), Is.True);
            var arguments = (ValueExpression[])owner.Call.Arguments;
            var original = arguments.Single();
            arguments[0] = new TypeOfExpression(typeof(Uri));
            try { Assert.That(prepared.CanCommitBodyNames(output), Is.False); }
            finally { arguments[0] = original; }
            Assert.That(prepared.CanCommitBodyNames(output), Is.False);
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.True,
                "A deep body-only failure must not invalidate an otherwise current shared snapshot.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RootChangeDuringCreationDoesNotVetoExistingBodyNames(bool prospective, bool mutateList)
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            var roots = new List<TypeProvider> { owner };
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = roots;
            var changed = false;
            Func<TypeProvider, bool> shouldWrite = _ =>
            {
                if (!changed)
                {
                    changed = true;
                    if (mutateList)
                    {
                        roots.Add(Target("Other"));
                        roots.RemoveAt(roots.Count - 1);
                    }
                    else
                    {
                        output.TypeProviders = new List<TypeProvider>(roots);
                        output.TypeProviders = roots;
                    }
                }
                return true;
            };
            using var prepared = prospective
                ? PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, shouldWrite)
                : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, shouldWrite);
            Assert.That(changed, Is.True);
            Assert.That(prepared, Is.Not.Null);
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var file = WriteWithMaterializedHeader(writer, (_, _, _) => { });
            Assert.That(file.PreparedBodyNameCount, Is.GreaterThan(0));
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.True);
            Assert.That(prepared.CanCommitBodyNames(file), Is.True);
            Assert.That(file.QualifiedContent, Does.Contain("return Pass(typeof(global::System.Exception));"));
            Assert.That(file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true).Content, Does.Contain("return Pass(typeof(Exception));"));
            if (prospective)
            {
                Assert.That(mutateList ? prepared.AreProspectiveRootMembersCurrent() : prepared.IsProspectiveRootReferenceCurrent(), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalSourceProofRootChangeDoesNotVetoExistingBodyNames(bool mutateList)
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            var roots = new List<TypeProvider> { owner };
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = roots;
            var mutated = false;
            host.Session.CurrentnessCallback = OnFinalDirectSourceProof(() =>
            {
                mutated = true;
                if (mutateList)
                {
                    roots.Add(Target("Other"));
                    roots.RemoveAt(roots.Count - 1);
                }
                else
                {
                    output.TypeProviders = new List<TypeProvider>(roots);
                    output.TypeProviders = roots;
                }
            });
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, _ => true);
            host.Session.CurrentnessCallback = null;
            Assert.That(mutated, Is.True);
            Assert.That(prepared, Is.Not.Null);
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var file = WriteWithMaterializedHeader(writer, (_, _, _) => { });
            Assert.That(file.PreparedBodyNameCount, Is.GreaterThan(0));
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.True);
            Assert.That(prepared.CanCommitBodyNames(file), Is.True);
            Assert.That(file.QualifiedContent, Does.Contain("return Pass(typeof(global::System.Exception));"));
            Assert.That(file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true).Content, Does.Contain("return Pass(typeof(Exception));"));
            Assert.That(mutateList ? prepared.AreProspectiveRootMembersCurrent() : prepared.IsProspectiveRootReferenceCurrent(), Is.False);
        }

        [Test]
        public void FieldlessProspectiveSessionRejectsCompetingAttachment()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            using var first = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.IsProspectiveRootReferenceCurrent(), Is.True);
            using var second = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(second, Is.Null, "A disposed competing session is not an admitted prospective lifetime.");
            Assert.That(first.IsProspectiveRootReferenceCurrent(), Is.True);
            Assert.That(first.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
            Assert.That(first.CanCommitSharedSnapshot(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FieldlessProspectiveSessionRejectsMissingOrStaleSourceProof(bool stale)
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            host.Generator.SourceInputModel = new Microsoft.TypeSpec.Generator.SourceInput.SourceInputModel(host.Snapshot.Compilation, null);
            if (stale)
            {
                System.IO.File.AppendAllText(host.SourcePath, "\n// Changed physical compiler input.\n");
                host.Generator.SourceInputModel.PreEmissionSourceInputs =
                    Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof.TryCreate(host.Session, host.DirectoryPath);
                Assert.That(host.Generator.SourceInputModel.PreEmissionSourceInputs, Is.Null);
            }
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Null);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void FieldlessProspectiveSessionRejectsDisabledPolicyAndCustomWriter(bool enabled, bool customWriter)
        {
            using var host = new BodyOnlyPhysicalHost(enabled, customWriter);
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Null);
        }

        // Diagnostic access to the non-authorizing snapshot core, without
        // fabricating a CSharpGen-owned generation phase in a unit fixture.
        private static PreparedFieldTypes? CreateUnownedProspectiveSnapshot(CodeModelGenerator generator, OutputLibrary output,
            Func<TypeProvider, bool> shouldWrite)
        {
            var core = typeof(PreparedFieldTypes).GetMethod("CreateForGenerationCore", BindingFlags.NonPublic | BindingFlags.Static)!;
            return (PreparedFieldTypes?)core.Invoke(null, new object?[] { generator, null, shouldWrite, true, output, false });
        }

        private static PreparedFieldTypes? CreateProspectiveForGeneration(CodeModelGenerator generator, TypeProvider[] owners,
            Func<TypeProvider, bool>? shouldWrite = null)
        {
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "A prospective group must use the existing complete-source preparation lifetime.");
            var output = generator.OutputLibrary;
            if (!output.TryCaptureMaterializedTypeProviders(out var current, out _) || current!.Count != owners.Length ||
                current.Where((owner, index) => !ReferenceEquals(owner, owners[index])).Any())
            {
                output.TypeProviders = owners;
            }
            return (PreparedFieldTypes?)method!.Invoke(null, new object[] { generator, output, shouldWrite ?? (static _ => true) });
        }

        [Test]
        public void FieldlessProspectiveLifetimeDoesNotReplayWritePolicyWithoutDecision()
        {
            var baseline = GenerateWithPolicyCounting(prospective: false);
            var candidate = GenerateWithPolicyCounting(prospective: true);
            TestContext.Progress.WriteLine($"Fieldless write-policy calls: original={baseline.Calls}, prospective={candidate.Calls}");
            Assert.That(candidate.Content, Is.EqualTo(baseline.Content));
            Assert.That(candidate.Calls, Is.EqualTo(baseline.Calls),
                "An unused prospective lifetime must not add write-policy callbacks.");
        }

        private static (int Calls, string Content) GenerateWithPolicyCounting(bool prospective)
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var calls = 0;
            Func<TypeProvider, bool> shouldWrite = _ => { calls++; return true; };
            using var prepared = prospective
                ? CreateProspectiveForGeneration(host.Generator, [owner], shouldWrite)
                : PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, shouldWrite);
            Assert.That(prepared, Is.Not.Null);
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var output = WriteWithMaterializedHeader(writer, (_, _, _) => { });
            Assert.That(output.PreparedBodyNameCount, Is.Zero);
            _ = prepared!.CanCommit();
            return (calls, output.Content);
        }

        [Test]
        public void MaterializedHeaderPausePreservesPreparedBodyAndStickyFallback()
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var callbacks = 0;
            var writer = host.Generator.GetWriter(owner);
            writer.PreparedFieldTypes = prepared;
            var output = WriteWithMaterializedHeader(writer, (_, _, _) => callbacks++);
            var qualified = output.QualifiedContent ?? throw new InvalidOperationException("Missing same-pass qualified companion.");
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(output.PreparedBodyNameCount, Is.GreaterThan(0));
            Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(prepared!.CanCommitBodyNames(output), Is.True);
            var raw = AssertOriginalTypeOfNative(output.Content, host.Snapshot, false);
            var full = AssertOriginalTypeOfNative(qualified, host.Snapshot, false);
            Assert.That(raw.SeedType, Is.EqualTo("Exception"));
            Assert.That(raw.OperandType, Is.EqualTo("Exception"));
            Assert.That(full.SeedType, Is.EqualTo("global::System.Exception"));
            Assert.That(full.OperandType, Is.EqualTo("global::System.Exception"));
            Assert.That(raw.Diagnostics, Is.EqualTo(full.Diagnostics));
            Assert.That(raw.EmitDiagnostics, Is.EqualTo(full.EmitDiagnostics));
            Assert.That(raw.Runtime, Is.EqualTo(full.Runtime));

            var arguments = (ValueExpression[])owner.Call.Arguments;
            var original = arguments.Single();
            arguments[0] = new TypeOfExpression(typeof(Uri));
            try { Assert.That(prepared.CanCommitBodyNames(output), Is.False); }
            finally { arguments[0] = original; }
            Assert.That(prepared.CanCommitBodyNames(output), Is.False);
            Assert.That(output.SelectPreparedNames(prepared.CanCommit(), bodyAllowed: false).Content, Is.EqualTo(qualified));
        }

        [Test]
        public void MaterializedHeaderPauseRetainsLateImportAndSelectsQualifiedFallback()
        {
            var target = Target("Other");
            var owner = new FieldOwner(target.Type);
            var field = new FieldProvider(FieldModifiers.Private, target.Type, "_value", owner,
                initializationValue: new MaterializationLateImport());
            owner.Update(fields: [field]);
            var compilation = ReferenceCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Competing { public class Widget {} }"));
            using var prepared = PreparedFieldTypes.Create([owner, target], compilation, _ => true);
            Assert.That(prepared.CanWriteSimpleName(owner, field), Is.True);
            var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
            var file = WriteWithMaterializedHeader(writer, (_, _, _) => { });
            Assert.That(file.Content, Does.Contain("private Widget _value"));
            Assert.That(file.QualifiedContent, Does.Contain("private global::Other.Widget _value"));
            Assert.That(prepared.CanCommit(), Is.False, "The attached session must observe the later conflicting import.");
            var selected = file.SelectPreparedNames(fieldAllowed: false, bodyAllowed: true);
            var source = CSharpSyntaxTree.ParseText("namespace Other { public class Widget {} }");
            Assert.That(compilation.AddSyntaxTrees(source, CSharpSyntaxTree.ParseText(file.Content)).GetDiagnostics()
                .Any(diagnostic => diagnostic.Id == "CS0104"), Is.True);
            Assert.That(compilation.AddSyntaxTrees(source, CSharpSyntaxTree.ParseText(selected.Content)).GetDiagnostics()
                .Any(diagnostic => diagnostic.Id == "CS0104"), Is.False);
        }

        [Test]
        public void MaterializedHeaderPauseSessionSetterWinsBeforeStart()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            using var prepared = PreparedFieldTypes.Create(Array.Empty<TypeProvider>(), ReferenceCompilation(), _ => true);
            var writer = new TypeProviderWriter(owner);
            var syncField = typeof(TypeProviderWriter).GetField("_headerSessionSync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(syncField, Is.Not.Null);
            var sync = syncField!.GetValue(writer)!;
            using var started = new ManualResetEventSlim();
            Task<CodeFile> task;
            lock (sync)
            {
                task = Task.Run(() =>
                {
                    started.Set();
                    return WriteWithMaterializedHeader(writer, (_, _, _) => Assert.That(writer.PreparedFieldTypes, Is.SameAs(prepared)));
                });
                Assert.That(started.Wait(TimeSpan.FromSeconds(30)), Is.True);
                writer.PreparedFieldTypes = prepared;
            }
            Assert.That(task.GetAwaiter().GetResult(), Is.Not.Null);
        }

        [Test]
        public void MaterializedHeaderPauseStartWinsBeforeConcurrentSessionSetter()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            using var prepared = PreparedFieldTypes.Create(Array.Empty<TypeProvider>(), ReferenceCompilation(), _ => true);
            var writer = new TypeProviderWriter(owner);
            using var paused = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var task = Task.Run(() => WriteWithMaterializedHeader(writer, (_, _, _) =>
            {
                paused.Set();
                Assert.That(resume.Wait(TimeSpan.FromSeconds(30)), Is.True);
            }));
            try
            {
                Assert.That(paused.Wait(TimeSpan.FromSeconds(30)), Is.True);
                Assert.Throws<InvalidOperationException>(() => writer.PreparedFieldTypes = prepared);
            }
            finally { resume.Set(); }
            Assert.That(task.GetAwaiter().GetResult(), Is.Not.Null);
            Assert.That(writer.PreparedFieldTypes, Is.Null);
        }

        [Test]
        public void MaterializedHeaderPauseCannotAttachSessionAfterPrefix()
        {
            var target = Target();
            var owner = new FieldOwner(target.Type);
            using var prepared = PreparedFieldTypes.Create([owner, target], ReferenceCompilation(), _ => true);
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            var writer = new TypeProviderWriter(owner);
            var rejected = false;
            var file = WriteWithMaterializedHeader(writer, (_, _, _) =>
            {
                try { writer.PreparedFieldTypes = prepared; }
                catch (InvalidOperationException) { rejected = true; }
            });
            Assert.That(rejected, Is.True);
            Assert.That(file.QualifiedContent, Is.Null);
            Assert.That(file.Content, Does.Contain("private global::Sample.Widget _value;"));
            Assert.That(prepared.CanCommit(), Is.True);
        }

        [Test]
        public void MaterializedHeaderPausePreservesNonemptyWarningScopes()
        {
            var baseline = new TypeProviderWriter(new MaterializationWarningOwner()).Write();
            var owner = new MaterializationWarningOwner();
            var callbacks = 0;
            var writer = new TypeProviderWriter(owner);
            var file = WriteWithMaterializedHeader(writer, (current, writeDocs, docs) =>
            {
                callbacks++;
                Assert.That(current, Is.SameAs(owner));
                Assert.That(owner.CachedDisabledFileWarnings, Has.Count.EqualTo(1));
                Assert.That(writeDocs, Is.True);
                Assert.That(docs, Is.SameAs(owner.CachedXmlDocs));
            });
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(file.Content, Is.EqualTo(baseline.Content));
            Assert.That(file.Content, Does.Contain("#pragma warning disable CS0618"));
            Assert.That(file.Content, Does.Contain("#pragma warning restore CS0618"));
        }

        private sealed class MaterializationWarningOwner : TypeProvider
        {
            protected override string BuildName() => "WarningOwner";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "WarningOwner.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() =>
                [new SuppressionStatement(null, new LiteralExpression("CS0618"), "controlled")];
        }

        [Test]
        public void MaterializedHeaderPauseRejectsSessionReplacementBeforeLateImport()
        {
            var target = Target("Other");
            var owner = new FieldOwner(target.Type);
            var field = new FieldProvider(FieldModifiers.Private, target.Type, "_value", owner,
                initializationValue: new MaterializationLateImport());
            owner.Update(fields: [field]);
            var compilation = ReferenceCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Competing { public class Widget {} }"));
            using var initial = PreparedFieldTypes.Create([owner, target], compilation, _ => true);
            Assert.That(initial.CanWriteSimpleName(owner, field), Is.True);
            PreparedFieldTypes? replacement = null;
            var rejected = false;
            try
            {
                var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = initial };
                var file = WriteWithMaterializedHeader(writer, (_, _, _) =>
                {
                    initial.Dispose();
                    replacement = PreparedFieldTypes.Create([owner, target], compilation, _ => true);
                    Assert.That(replacement.CanWriteSimpleName(owner, field), Is.True);
                    try { writer.PreparedFieldTypes = replacement; }
                    catch (InvalidOperationException) { rejected = true; }
                });
                var replacementCurrent = replacement!.CanCommit();
                var selected = file.SelectPreparedNames(replacementCurrent, bodyAllowed: true);
                var native = compilation.AddSyntaxTrees(
                    CSharpSyntaxTree.ParseText("namespace Other { public class Widget {} }"),
                    CSharpSyntaxTree.ParseText(selected.Content));
                var ambiguous = native.GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS0104");
                var rawShort = file.Content.Contains("private Widget _value", StringComparison.Ordinal);
                TestContext.Progress.WriteLine($"Session replacement: rejected={rejected}, replacementCurrent={replacementCurrent}, rawShort={rawShort}, ambiguous={ambiguous}");
                Assert.That(rejected, Is.True, "The writer's initially attached preparation cannot be replaced during its pass.");
                Assert.That(file.Content, Does.Contain("private global::Other.Widget _value"));
                Assert.That(ambiguous, Is.False);
            }
            finally { replacement?.Dispose(); }
        }

        private sealed record MaterializationLateImport : ValueExpression
        {
            internal override void WriteCore(CodeWriter writer)
            {
                writer.UseNamespace("Competing");
                writer.AppendRaw("default");
            }
        }

        [Test]
        public void MaterializedHeaderPauseCannotReplayAfterExceptionOrThroughOrdinaryWrite()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            var writer = new TypeProviderWriter(owner);
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => WriteWithMaterializedHeader(writer, (_, _, _) =>
            {
                events.Add("prepare");
                throw new InvalidOperationException("stop at preparation");
            }));
            Assert.That(error!.InnerException?.Message, Is.EqualTo("stop at preparation"));
            Assert.That(events, Is.EqualTo(new[] { "attributes", "warnings", "docs", "prepare" }));
            Assert.Throws<InvalidOperationException>(() => writer.Write());
            Assert.Throws<System.Reflection.TargetInvocationException>(() => WriteWithMaterializedHeader(writer, (_, _, _) => events.Add("replay")));
            Assert.That(events, Does.Not.Contain("replay"));
        }

        [Test]
        public void OrdinaryWriterCanStillBeCalledMoreThanOnceButCannotEnterMaterializedPathAfterward()
        {
            var events = new List<string>();
            var owner = new MaterializationOrderOwner(events, isPublic: true);
            owner.EnsureBuilt();
            var writer = new TypeProviderWriter(owner);
            var first = writer.Write();
            var second = writer.Write();
            Assert.That(second.Content, Is.EqualTo(first.Content));
            Assert.That(events, Is.EqualTo(new[] { "attributes", "warnings", "docs", "path", "path" }));
            Assert.Throws<System.Reflection.TargetInvocationException>(() => WriteWithMaterializedHeader(writer, (_, _, _) => events.Add("prepare")));
            Assert.That(events, Does.Not.Contain("prepare"));
        }

        private static CodeFile WriteWithMaterializedHeader(TypeProviderWriter writer, Action<TypeProvider, bool, XmlDocProvider?> prepare)
        {
            var method = typeof(TypeProviderWriter).GetMethod("WriteWithMaterializedHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, "A single-use standard-writer materialization boundary is required.");
            return (CodeFile)method!.Invoke(writer, new object[] { prepare })!;
        }

        private sealed class MaterializationCustomWriter(TypeProvider owner) : TypeProviderWriter(owner) { }

        [Test]
        public void StandardWriterEarlierFileCanChangeLaterLazyDocumentation()
        {
            MockHelpers.LoadMockGenerator(configuration: "{\"disable-xml-docs\":false,\"package-name\":\"Sample.Namespace\"}");
            var value = "before";
            var events = new List<string>();
            var earlier = new MaterializationDependencyOwner("Earlier", events, () => { value = "after"; return "earlier"; });
            var later = new MaterializationDependencyOwner("Later", events, () => value);
            earlier.EnsureBuilt();
            later.EnsureBuilt();
            Assert.That(later.CachedXmlDocs, Is.Null);

            _ = new TypeProviderWriter(earlier).Write();
            var output = new TypeProviderWriter(later).Write();
            Assert.That(events, Is.EqualTo(new[] { "Earlier", "Later" }));
            Assert.That(output.Content, Does.Contain("/// <summary> after. </summary>"));
        }

        [Test]
        public void StandardWriterRelatedPartialCanAffectLaterPartialDocumentation()
        {
            MockHelpers.LoadMockGenerator(configuration: "{\"disable-xml-docs\":false,\"package-name\":\"Sample.Namespace\"}");
            var value = "before";
            var events = new List<string>();
            var first = new MaterializationPartialOrderOwner("Foo.Part1.cs", events, () => { value = "after"; return "first"; });
            var second = new MaterializationPartialOrderOwner("Foo.Part2.cs", events, () => value, first);
            first.EnsureBuilt();
            second.EnsureBuilt();
            Assert.That(second.PartialDeclarationOwner, Is.SameAs(first));
            Assert.That(first.PartialDeclarationParts, Does.Contain(second));
            Assert.That(second.CachedXmlDocs, Is.Null);

            _ = new TypeProviderWriter(first).Write();
            var output = new TypeProviderWriter(second).Write();
            Assert.That(events, Is.EqualTo(new[] { "Foo.Part1.cs", "Foo.Part2.cs" }));
            Assert.That(output.Content, Does.Contain("/// <summary> after. </summary>"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalizedHeadersDoNotHoistLaterUnknownDocumentation(bool relatedPartial)
        {
            (string First, string Second, string[] Events) Run(bool finalized)
            {
                using var host = new BodyOnlyPhysicalHost();
                var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
                File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
                try
                {
                    var value = "before";
                    var events = new List<string>();
                    TypeProvider first;
                    TypeProvider second;
                    if (relatedPartial)
                    {
                        var root = new MaterializationPartialOrderOwner("Foo.Part1.cs", events, () => { value = "after"; return "first"; });
                        first = root;
                        second = new MaterializationPartialOrderOwner("Foo.Part2.cs", events, () => value, root);
                        Assert.That(second.PartialDeclarationOwner, Is.SameAs(first));
                        Assert.That(first.PartialDeclarationParts, Does.Contain(second));
                    }
                    else
                    {
                        first = new MaterializationDependencyOwner("Earlier", events, () => { value = "after"; return "earlier"; });
                        second = new MaterializationDependencyOwner("Later", events, () => value);
                    }
                    first.EnsureBuilt();
                    second.EnsureBuilt();
                    var roots = new[] { first, second };
                    var output = host.Generator.OutputLibrary;
                    output.TypeProviders = roots;
                    using var map = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                    foreach (var provider in roots)
                    { ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type)); }
                    Assert.That(events, Is.Empty);
                    Assert.That(value, Is.EqualTo("before"));
                    var firstDocs = first.CachedXmlDocs;
                    var secondDocs = second.CachedXmlDocs;
                    var firstWarnings = first.CachedDisabledFileWarnings;
                    var secondWarnings = second.CachedDisabledFileWarnings;
                    var firstAttributes = first.CachedWriteAttributes;
                    var secondAttributes = second.CachedWriteAttributes;
                    var firstOrigin = first.PocNativeAttributeCacheOrigin;
                    var secondOrigin = second.PocNativeAttributeCacheOrigin;
                    Assert.That(firstDocs, Is.Null);
                    Assert.That(secondDocs, Is.Null);
                    using var preparedHeaders = finalized ? new PreparedPocHeaderFinalization(host.Generator) : null;
                    using var prepared = finalized
                        ? PreparedFieldTypes.CreateForGenerationWithFinalizedPocHeaders(host.Generator, roots, map.ShouldWriteProvider, preparedHeaders!)
                        : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, map.ShouldWriteProvider);
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(events, Is.Empty, "Unknown documentation builders must remain cold through preparation.");
                    Assert.That(value, Is.EqualTo("before"));
                    Assert.That(ReferenceEquals(first.CachedXmlDocs, firstDocs), Is.True);
                    Assert.That(ReferenceEquals(second.CachedXmlDocs, secondDocs), Is.True);
                    Assert.That(ReferenceEquals(first.CachedDisabledFileWarnings, firstWarnings), Is.True);
                    Assert.That(ReferenceEquals(second.CachedDisabledFileWarnings, secondWarnings), Is.True);
                    Assert.That(ReferenceEquals(first.CachedWriteAttributes, firstAttributes), Is.True);
                    Assert.That(ReferenceEquals(second.CachedWriteAttributes, secondAttributes), Is.True);
                    Assert.That(ReferenceEquals(first.PocNativeAttributeCacheOrigin, firstOrigin), Is.True);
                    Assert.That(ReferenceEquals(second.PocNativeAttributeCacheOrigin, secondOrigin), Is.True);
                    if (finalized)
                    {
                        Assert.That(prepared!.FinalizedPocHeaders, Is.Not.Null);
                        Assert.That(prepared.FinalizedPocHeaders!.Get(first), Is.Null);
                        Assert.That(prepared.FinalizedPocHeaders.Get(second), Is.Null);
                    }
                    var firstWriter = host.Generator.GetWriter(first);
                    firstWriter.PreparedFieldTypes = prepared;
                    firstWriter.PreparedPocHeaders = preparedHeaders;
                    var firstText = firstWriter.Write().Content;
                    Assert.That(value, Is.EqualTo("after"), "The first native write, not setup, changes the later input.");
                    var secondWriter = host.Generator.GetWriter(second);
                    secondWriter.PreparedFieldTypes = prepared;
                    secondWriter.PreparedPocHeaders = preparedHeaders;
                    var secondText = secondWriter.Write().Content;
                    return (firstText, secondText, events.ToArray());
                }
                finally { File.Delete(codeModel); }
            }

            var native = Run(finalized: false);
            var candidate = Run(finalized: true);
            Assert.That(native.Events, Is.EqualTo(relatedPartial ? new[] { "Foo.Part1.cs", "Foo.Part2.cs" } : new[] { "Earlier", "Later" }));
            Assert.That(native.Second, Does.Contain("/// <summary> after. </summary>"));
            Assert.That(candidate.Events, Is.EqualTo(native.Events));
            Assert.That(candidate.First, Is.EqualTo(native.First));
            Assert.That(candidate.Second, Is.EqualTo(native.Second));
        }

        private sealed class MaterializationPartialOrderOwner : TypeProvider
        {
            private readonly string _path;
            private readonly List<string> _events;
            private readonly Func<string> _description;

            public MaterializationPartialOrderOwner(string path, List<string> events, Func<string> description, TypeProvider? root = null)
            {
                _path = path;
                _events = events;
                _description = description;
                if (root is not null) { RegisterPartialDeclarationOwner(root); }
            }

            protected override string BuildName() => "Foo";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => _path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Partial | TypeSignatureModifiers.Class;
            protected override XmlDocProvider BuildXmlDocs()
            {
                _events.Add(_path);
                return new XmlDocProvider(summary: new XmlDocSummaryStatement([$"{_description()}"]));
            }
        }

        private sealed class MaterializationDependencyOwner(string name, List<string> events, Func<string> description) : TypeProvider
        {
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => name + ".cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override XmlDocProvider BuildXmlDocs()
            {
                events.Add(name);
                return new XmlDocProvider(summary: new XmlDocSummaryStatement([$"{description()}"]));
            }
        }

        private sealed class PocHeaderOrderOwner(List<string> events, CSharpType fieldType, SuppressionStatement[]? fileWarnings = null) : TypeProvider
        {
            protected override string BuildName() => "PocHeaderOrder";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() { events.Add("path"); return "PocHeaderOrder.cs"; }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override IReadOnlyList<MethodBodyStatement> BuildAttributes() { events.Add("attributes"); return []; }
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() { events.Add("warnings"); return fileWarnings ?? []; }
            protected override XmlDocProvider BuildXmlDocs()
            {
                events.Add("docs");
                return new XmlDocProvider(summary: new XmlDocSummaryStatement([$"Original documentation."]));
            }
            protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, fieldType, "_value", this)];
            protected internal override MethodProvider[] BuildMethods()
            {
                var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "value");
                var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
                return [new MethodProvider(signature, new MethodBodyStatements([assignment.Terminate()]), this)];
            }
        }

        private sealed class MaterializationOrderOwner(List<string> events, bool isPublic) : TypeProvider
        {
            protected override string BuildName() => "MaterializationOrder";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() { events.Add("path"); return "MaterializationOrder.cs"; }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() =>
                TypeSignatureModifiers.Class | (isPublic ? TypeSignatureModifiers.Public : TypeSignatureModifiers.Internal);
            protected override IReadOnlyList<MethodBodyStatement> BuildAttributes() { events.Add("attributes"); return []; }
            protected internal override SuppressionStatement[] BuildDisabledFileWarnings() { events.Add("warnings"); return []; }
            protected override XmlDocProvider BuildXmlDocs()
            {
                events.Add("docs");
                return new XmlDocProvider(summary: new XmlDocSummaryStatement([$"Original documentation."]));
            }
        }
    }
}

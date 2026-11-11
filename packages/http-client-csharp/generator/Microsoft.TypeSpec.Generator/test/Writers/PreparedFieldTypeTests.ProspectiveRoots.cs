// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void OutputLibraryMaterializesRootsOnceAndRetriesBuilderFailure()
        {
            var owner = Target();
            var calls = 0;
            var output = new CallbackOutputLibrary(() =>
            {
                calls++;
                if (calls == 1) { throw new InvalidOperationException("first builder failure"); }
                return [owner];
            });
            Assert.Throws<InvalidOperationException>(() => _ = output.TypeProviders);
            var roots = output.TypeProviders;
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            Assert.That(calls, Is.EqualTo(2));
            var replacement = new List<TypeProvider> { owner };
            output.TypeProviders = replacement;
            Assert.That(output.TypeProviders, Is.SameAs(replacement));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void OutputLibraryRootBuilderRunsWithoutHoldingWitnessLockAndKeepsOriginalAssignmentOrder()
        {
            var built = Target();
            var injected = Target("Other");
            CallbackOutputLibrary? output = null;
            var calls = 0;
            output = new CallbackOutputLibrary(() =>
            {
                calls++;
                var setter = System.Threading.Tasks.Task.Run(() => output!.TypeProviders = new List<TypeProvider> { injected });
                Assert.That(setter.Wait(TimeSpan.FromSeconds(30)), Is.True, "The virtual builder must not run while holding the root witness lock.");
                return [built];
            });
            var roots = output.TypeProviders;
            Assert.That(roots[0], Is.SameAs(built), "The builder result still replaces a setter that ran while the builder was active.");
            Assert.That(output.TypeProviders, Is.SameAs(roots));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ProspectiveFactoryDoesNotMaterializeAnUnknownOutputBuilder()
        {
            using var host = new BodyOnlyPhysicalHost();
            var calls = 0;
            var output = new CallbackOutputLibrary(() => { calls++; throw new InvalidOperationException("must not execute"); });
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(prepared, Is.Null);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void OutputLibraryRootWitnessFailsClosedAtRevisionSaturation()
        {
            var output = new OutputLibrary();
            var roots = new List<TypeProvider> { Target() };
            output.TypeProviders = roots;
            var revision = typeof(OutputLibrary).GetField("_typeProviderRevision", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(revision, Is.Not.Null);
            revision!.SetValue(output, long.MaxValue - 1);
            Assert.That(output.TryCaptureMaterializedTypeProviders(out var captured, out _), Is.True);
            Assert.That(captured, Is.SameAs(roots));
            output.TypeProviders = roots;
            Assert.That(output.TryCaptureMaterializedTypeProviders(out _, out _), Is.False);
            output.TypeProviders = new List<TypeProvider>(roots);
            Assert.That(output.TryCaptureMaterializedTypeProviders(out _, out _), Is.False);
        }

        private sealed class CallbackOutputLibrary(Func<TypeProvider[]> build) : OutputLibrary
        {
            protected override TypeProvider[] BuildTypeProviders() => build();
        }

        [Test]
        public void DormantProspectiveCreationDoesNotReplayLazyProviderGetters()
        {
            var baseline = CaptureFieldlessGetterCounts(prospective: false);
            var candidate = CaptureFieldlessGetterCounts(prospective: true);
            TestContext.Progress.WriteLine($"Fieldless getter calls: original={baseline}, prospective={candidate}");
            Assert.That(candidate, Is.EqualTo(baseline), "An unused prospective lifetime must not replay lazy provider getters.");
        }

        private static (int Path, int Declaring) CaptureFieldlessGetterCounts(bool prospective)
        {
            using var host = new BodyOnlyPhysicalHost();
            var owner = new FieldlessGetterCountingOwner();
            owner.EnsureBuilt();
            _ = owner.Type;
            var pathBefore = owner.PathCalls;
            var declaringBefore = owner.DeclaringCalls;
            using var prepared = prospective
                ? CreateProspectiveForGeneration(host.Generator, [owner])
                : PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            return (owner.PathCalls - pathBefore, owner.DeclaringCalls - declaringBefore);
        }

        private sealed class FieldlessGetterCountingOwner : TypeProvider
        {
            internal int PathCalls { get; private set; }
            internal int DeclaringCalls { get; private set; }
            protected override string BuildName() => "GetterOwner";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() { PathCalls++; return "GetterOwner.cs"; }
            protected override TypeProvider? BuildDeclaringTypeProvider() { DeclaringCalls++; return null; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProspectiveRootReplacementDoesNotVetoExistingIndependentField(bool prospective)
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var output = host.Generator.OutputLibrary;
            var roots = new List<TypeProvider> { owner, target };
            output.TypeProviders = roots;
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = prospective
                ? (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) })
                : PreparedFieldTypes.CreateForGeneration(host.Generator, roots, _ => true);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            output.TypeProviders = new List<TypeProvider> { owner, target };
            Assert.That(prepared.CanCommit(), Is.True,
                "A prospective-only root witness must not veto an existing independent field channel.");
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(written.Content, Does.Contain("private Widget _value;"));
            Assert.That(written.QualifiedContent, Does.Contain("private global::Sample.Widget _value;"));
            Assert.That(written.SelectPreparedNames(prepared.CanCommit(), false).Content, Does.Contain("private Widget _value;"));
            if (prospective)
            {
                Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.False);
                Assert.That(prepared.CanCommit(), Is.True);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RootChangeDuringCreationDoesNotVetoExistingIndependentField(bool prospective, bool mutateList)
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var roots = new List<TypeProvider> { owner, target };
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
            Assert.That(prepared, Is.Not.Null, "A prospective-only stale fact must not erase the existing field channel.");
            Assert.That(prepared!.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.CanCommit(), Is.True);
            Assert.That(written.SelectPreparedNames(prepared.CanCommit(), false).Content, Does.Contain("private Widget _value;"));
            if (prospective)
            {
                Assert.That(mutateList ? prepared.AreProspectiveRootMembersCurrent() : prepared.IsProspectiveRootReferenceCurrent(), Is.False);
            }
        }

        [Test]
        public void SelfConsistentForeignPreparationCannotRepresentActiveGeneration()
        {
            using var host = new BodyOnlyPhysicalHost();
            const string config = "{\"enable-pre-emission-simplification\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = MockHelpers.LoadMockGenerator(outputPath: host.DirectoryPath, configuration: config);
            var actualOutput = new TestOutputLibrary(Target("Actual"));
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                mock.Object.TypeFactory, actualOutput, provideSnapshot: false);
            generator.SourceInputModel = new SourceInputModel(host.Snapshot.Compilation, null) { PreEmissionSourceInputs = host.Snapshot };
            CodeModelGenerator.Instance = generator;
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var foreign = new TestOutputLibrary(owner, target);
            var roots = foreign.TypeProviders;
            using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(foreign, roots);
            Assert.That(referenceMap.TryBindGenerationOwner(new object(), generator, foreign, roots), Is.False,
                "An arbitrary object cannot impersonate CSharpGen's private owner.");
            using var prepared = CreateUnownedProspectiveSnapshot(generator, foreign, referenceMap.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var method = typeof(CSharpGen).GetMethod("GetWrittenProviders", BindingFlags.NonPublic | BindingFlags.Static)!;
            var stream = (IEnumerable<TypeProvider>)method.Invoke(null, new object?[] { foreign, referenceMap, null })!;
            using var enumerator = stream.GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(prepared!.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False,
                "A self-consistent foreign output and manually driven iterator are not an active CSharpGen generation.");
        }

        [Test]
        public void CallerObservationCannotImpersonateOwnedIterator()
        {
            using var host = new BodyOnlyPhysicalHost();
            const string config = "{\"enable-pre-emission-simplification\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = MockHelpers.LoadMockGenerator(outputPath: host.DirectoryPath, configuration: config);
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var output = new TestOutputLibrary(owner, target);
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                mock.Object.TypeFactory, output, provideSnapshot: false);
            generator.SourceInputModel = new SourceInputModel(host.Snapshot.Compilation, null) { PreEmissionSourceInputs = host.Snapshot };
            CodeModelGenerator.Instance = generator;
            var roots = output.TypeProviders;
            using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
            // Construct a private token only in this test to isolate iterator
            // observation from ownership; production creates it in CSharpGen.
            var tokenType = typeof(CSharpGen).GetNestedType("GenerationOwner", BindingFlags.NonPublic)!;
            using var token = (IDisposable)Activator.CreateInstance(tokenType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { generator, output, roots }, null)!;
            tokenType.GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(token, new object[] { referenceMap });
            Assert.That(referenceMap.IsOwnedGenerationPhaseCurrent(generator, output, roots), Is.True);
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(generator, output, referenceMap.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null, "A genuine owner token admits the prospective factory without changing output getters.");
            referenceMap.ObserveEnumerationRoot(output, roots); // Not the iterator's observation.
            Assert.That(referenceMap.IsObservedEnumerationRootCurrent(output, roots), Is.True);
            Assert.That(prepared!.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False,
                "A caller-supplied observation is not evidence that the CSharpGen iterator began.");
            referenceMap.ObserveOwnedEnumerationRoot(new object(), generator, output, roots);
            Assert.That(prepared.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False,
                "An arbitrary object cannot impersonate the private iterator owner.");
        }

        [Test]
        public void ProspectiveFactoryDoesNotInvokeCovariantOutputOverride()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = new CovariantOutputLibrary { TypeProviders = new List<TypeProvider> { owner } };
            var generator = new CovariantOutputGenerator(new GeneratorContext(host.Generator.Configuration), output)
            {
                SourceInputModel = host.Generator.SourceInputModel
            };
            CodeModelGenerator.Instance = generator;
            Assert.That(PreparedFieldTypes.IsPolicyEnabled(generator), Is.True);
            var properties = generator.GetType().GetProperties().Where(property => property.Name == nameof(CodeModelGenerator.OutputLibrary)).ToArray();
            TestContext.Progress.WriteLine("Covariant output properties: " + string.Join(", ", properties.Select(property => $"{property.DeclaringType?.Name}:{property.PropertyType.Name}/getter={property.GetMethod?.DeclaringType?.Name}")));
            var audit = typeof(PreparedFieldTypes).GetMethod("HasKnownDirectBaseOutput", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.That(audit.Invoke(null, new object[] { generator, output }), Is.EqualTo(false));
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(generator, output, _ => true);
            Assert.That(generator.GetterCalls, Is.Zero, "An unknown covariant virtual output getter must not run during admission.");
            Assert.That(prepared, Is.Null);
            Assert.That(((CodeModelGenerator)generator).OutputLibrary, Is.SameAs(output));
            Assert.That(generator.GetterCalls, Is.EqualTo(1), "The control must dispatch through the covariant override.");
        }

        [Test]
        public void ProspectiveFactoryRejectsNonCurrentGeneratorWithItsBaseOutput()
        {
            using var host = new BodyOnlyPhysicalHost();
            var original = host.Generator;
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = original.OutputLibrary;
            output.TypeProviders = new List<TypeProvider> { owner };
            var other = new BodyOnlyHostGenerator(new GeneratorContext(original.Configuration)) { SourceInputModel = original.SourceInputModel };
            CodeModelGenerator.Instance = other;
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(original, output, _ => true);
            Assert.That(prepared, Is.Null, "A non-current generator cannot establish this generation's prospective lifetime.");
        }

        private sealed class CovariantOutputLibrary : OutputLibrary { }

        private sealed class CovariantOutputGenerator(GeneratorContext context, CovariantOutputLibrary output) : CodeModelGenerator(context)
        {
            internal int GetterCalls { get; private set; }
            public override CovariantOutputLibrary OutputLibrary { get { GetterCalls++; return output; } }
        }

        [Test]
        public void ProspectiveFactoryRejectsForeignOutputWithoutGenerationPhaseBinding()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var actual = host.Generator.OutputLibrary;
            actual.TypeProviders = new List<TypeProvider> { owner };
            var foreign = new OutputLibrary { TypeProviders = new List<TypeProvider> { owner } };
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, foreign, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(prepared, Is.Null, "A foreign output cannot establish the active generation's root phase.");
        }

        [Test]
        public void ProspectiveLifetimeDetectsCanonicalOutputRootListReplacement()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = host.Generator.OutputLibrary;
            var roots = new List<TypeProvider> { owner };
            output.TypeProviders = roots;
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.False, "A dormant fieldless lifetime does not grant a shared commit.");
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
            output.TypeProviders = new List<TypeProvider> { owner };
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.False,
                "Replacing the canonical output root collection must invalidate the prospective root witness even when the old list is unchanged.");
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.False);
            output.TypeProviders = roots;
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.False);
        }

        [Test]
        public void ProspectiveLifetimeRejectsCanonicalRootReplacementDuringCreation()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = host.Generator.OutputLibrary;
            var roots = new List<TypeProvider> { owner };
            output.TypeProviders = roots;
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            var changed = false;
            Func<TypeProvider, bool> shouldWrite = _ =>
            {
                if (!changed)
                {
                    changed = true;
                    output.TypeProviders = new List<TypeProvider> { owner };
                    output.TypeProviders = roots;
                }
                return true;
            };
            using var rejected = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, shouldWrite });
            Assert.That(changed, Is.True);
            Assert.That(rejected, Is.Null, "A changed canonical root cannot admit a prospective session even if restored during creation.");
            using var next = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(next, Is.Not.Null, "Rejected creation must release any provider attachment.");
        }

        [Test]
        public void ProspectiveRootWitnessDetectsChangeInsideSharedValidationCallback()
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = Target();
            var owner = new FieldOwner(target.Type);
            var output = host.Generator.OutputLibrary;
            var roots = new List<TypeProvider> { owner, target };
            output.TypeProviders = roots;
            var armed = false;
            var changed = false;
            Func<TypeProvider, bool> shouldWrite = _ =>
            {
                if (armed && !changed)
                {
                    changed = true;
                    output.TypeProviders = new List<TypeProvider> { owner, target };
                    output.TypeProviders = roots;
                }
                return true;
            };
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, shouldWrite });
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.IsProspectiveRootReferenceCurrent(), Is.True);
            armed = true;
            Assert.That(prepared.CanCommitSharedSnapshot(), Is.True, "Existing shared validation remains separate from the prospective root witness.");
            Assert.That(changed, Is.True);
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.False);
            Assert.That(prepared.CanCommit(), Is.True, "The independently eligible field remains committable.");
        }

        [Test]
        public void ProspectiveLifetimeDetectsUnobservedCanonicalRootReplacementAndRestore()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = host.Generator.OutputLibrary;
            var roots = new List<TypeProvider> { owner };
            output.TypeProviders = roots;
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.False);
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
            output.TypeProviders = new List<TypeProvider> { owner };
            output.TypeProviders = roots;
            Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.False,
                "A canonical root replacement must remain stale even if restored before observation.");
        }

        [Test]
        public void ProspectiveRootListDetectsMutationAndRestoreBeforeObservation()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var roots = new List<TypeProvider> { owner };
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = roots;
            using var prepared = CreateProspectiveForGeneration(host.Generator, [owner]);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.HasProspectiveListVersionWitness, Is.True);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True);
            roots.Add(Target());
            roots.RemoveAt(roots.Count - 1);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.False,
                "A canonical List root changed and restored between observations cannot establish current group membership.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void FinalSourceProofRootChangeCannotAuthorizeProspectiveCreation(bool withField, bool mutateList)
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = Target();
            TypeProvider owner = withField ? new FieldOwner(target.Type) : CreateBodyOnlyHostOwner().Owner;
            var roots = withField ? new List<TypeProvider> { owner, target } : new List<TypeProvider> { owner };
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
            Assert.That(mutated, Is.True, "Mutate only within the final direct physical-source proof callback.");
            if (!withField)
            {
                Assert.That(prepared, Is.Null, "A fieldless stale prospective lifetime must be rejected during creation.");
                using var next = PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, _ => true);
                Assert.That(next, Is.Not.Null, "Rejected creation must release its attachments.");
                return;
            }
            Assert.That(prepared, Is.Not.Null, "The independently eligible field must survive a prospective-only failure.");
            Assert.That(prepared!.CanCommit(), Is.True);
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
            Assert.That(mutateList ? prepared.AreProspectiveRootMembersCurrent() : prepared.IsProspectiveRootReferenceCurrent(), Is.False);
        }

        private static Func<bool> OnFinalDirectSourceProof(Action mutate)
        {
            var directProofCalls = 0;
            return () =>
            {
                var frames = new System.Diagnostics.StackTrace().GetFrames();
                var proofFrame = Array.FindIndex(frames, frame => frame.GetMethod()?.DeclaringType == typeof(global::Microsoft.TypeSpec.Generator.SourceInput.CompleteSourceInputSnapshot) &&
                    frame.GetMethod()?.Name == "IsCurrentFor");
                if (proofFrame >= 0 && proofFrame + 1 < frames.Length &&
                    frames[proofFrame + 1].GetMethod()?.Name == "CreateForGenerationCore" && ++directProofCalls == 2)
                {
                    mutate();
                }
                return true;
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalSourceProofGeneratorSwitchCannotAuthorizeProspectiveCreation(bool withField)
        {
            using var host = new BodyOnlyPhysicalHost();
            var generator = host.Generator;
            var target = Target();
            TypeProvider owner = withField ? new FieldOwner(target.Type) : CreateBodyOnlyHostOwner().Owner;
            var roots = withField ? new List<TypeProvider> { owner, target } : new List<TypeProvider> { owner };
            var output = generator.OutputLibrary;
            output.TypeProviders = roots;
            var other = new BodyOnlyHostGenerator(new GeneratorContext(generator.Configuration)) { SourceInputModel = generator.SourceInputModel };
            var switched = false;
            host.Session.CurrentnessCallback = OnFinalDirectSourceProof(() => { switched = true; CodeModelGenerator.Instance = other; });
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(generator, output, _ => true);
            host.Session.CurrentnessCallback = null;
            Assert.That(switched, Is.True);
            Assert.That(CodeModelGenerator.Instance, Is.SameAs(other));
            if (!withField)
            {
                Assert.That(prepared, Is.Null, "A fieldless lifetime cannot survive an observed generator change during creation.");
                CodeModelGenerator.Instance = generator;
                using var next = PreparedFieldTypes.CreateProspectiveForGeneration(generator, output, _ => true);
                Assert.That(next, Is.Not.Null, "Rejected creation must release its provider attachments.");
                return;
            }
            Assert.That(prepared, Is.Not.Null, "A prospective-only generator change must not erase the existing field channel.");
            Assert.That(prepared!.IsProspectiveGeneratorCurrent(), Is.False);
            Assert.That(prepared.CanCommit(), Is.True, "Prospective staleness must not veto an existing field while the other generator is current.");
            CodeModelGenerator.Instance = generator;
            Assert.That(prepared.IsProspectiveGeneratorCurrent(), Is.False, "Observed generator staleness must remain sticky after restore.");
            Assert.That(prepared.CanCommit(), Is.True);
            Assert.That(prepared.CanWriteSimpleName(owner, owner.Fields[0]), Is.True);
        }

        [Test]
        public void ProspectiveCreationRejectsUnobservedListMutationAndReleasesAttachment()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var roots = new List<TypeProvider> { owner };
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = roots;
            var changed = false;
            Func<TypeProvider, bool> shouldWrite = _ =>
            {
                if (!changed)
                {
                    changed = true;
                    roots.Add(Target());
                    roots.RemoveAt(roots.Count - 1);
                }
                return true;
            };
            using var rejected = PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, shouldWrite);
            Assert.That(changed, Is.True);
            Assert.That(rejected, Is.Null);
            using var next = PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, _ => true);
            Assert.That(next, Is.Not.Null, "Rejected creation must release attached providers.");
        }

        [Test]
        public void ProspectiveArrayRootsDoNotClaimListVersionWitness()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = new TypeProvider[] { owner };
            using var prepared = PreparedFieldTypes.CreateProspectiveForGeneration(host.Generator, output, _ => true);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.HasProspectiveListVersionWitness, Is.False,
                "An array cannot supply this list-version witness merely by matching at an observation.");
        }

        [Test]
        public void ProspectiveLifetimeRetainsCanonicalListRootIdentityAndDetectsMutation()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var roots = new List<TypeProvider> { owner };
            var output = host.Generator.OutputLibrary;
            output.TypeProviders = roots;
            Assert.That(output.TryCaptureMaterializedTypeProviders(out var captured, out _), Is.True);
            Assert.That(captured, Is.SameAs(roots), "The canonical list must not be copied into a detached array.");
            var method = typeof(PreparedFieldTypes).GetMethod("CreateProspectiveForGeneration", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.GetParameters()[1].ParameterType.IsInstanceOfType(output), Is.True);
            using var prepared = (PreparedFieldTypes?)method.Invoke(null, new object[] { host.Generator, output, (Func<TypeProvider, bool>)(_ => true) });
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.CanCommitSharedSnapshot(), Is.False);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True);
            var added = Target();
            roots.Add(added);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.False);
            roots.RemoveAt(roots.Count - 1);
            Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.False, "Observed root-list staleness must remain sticky.");
        }
    }
}

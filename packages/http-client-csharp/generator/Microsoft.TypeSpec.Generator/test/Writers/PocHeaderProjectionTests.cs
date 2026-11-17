// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocHeaderProjectionTests
    {
        [SetUp]
        public async Task SetUp()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(Compilation()));
            CodeModelGenerator.Instance = new ProbeGenerator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = mock.Object.SourceInputModel };
        }
        private sealed class ProbeGenerator(GeneratorContext context) : CodeModelGenerator(context) { }
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("PocHeaderProjection",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        [TestCase(false)]
        [TestCase(true)]
        public void VirtualAttributePolicyCannotBorrowCachedEmptyInventory(bool decision)
        {
            var owner = new PolicyOwner(decision);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            Assert.That(owner.PolicyReads, Is.GreaterThan(0), "The native writer actually consumes this policy");
            Assert.That(baseline, Does.Contain("return (x == y);"));
            Assert.That(ContractCollections.TryCopy(owner.CachedWriteAttributes, out var cached) && cached.IsEmpty, Is.True,
                "The fixture must reach native header admission rather than fail on an opaque cache");
            int reads = owner.PolicyReads;
            int builds = owner.AttributeBuilds;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(owner.PolicyReads, Is.EqualTo(reads), "Capture must inspect dispatch rather than invoke policy");
            Assert.That(owner.AttributeBuilds, Is.EqualTo(builds), "Capture must not execute a builder");
            Assert.That(prepared.PocGroupingCount, Is.Zero, "Cached empty attributes are not native dispatch authority");
        }

        [Test]
        public void MatchingDefaultPolicyAnchorActuallyAdmitsGrouping()
        {
            var owner = new Owner();
            string baseline = new TypeProviderWriter(owner).Write().Content;
            Assert.That(baseline, Does.Contain("return (x == y);"));
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
        }

        [Test]
        public void CachedBuilderOverrideUsesDefaultPolicyWithoutReplay()
        {
            var owner = new Owner();
            string baseline = new TypeProviderWriter(owner).Write().Content;
            int builds = owner.AttributeBuilds;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            Assert.That(file.QualifiedContent, Is.EqualTo(baseline));
            Assert.That(owner.AttributeBuilds, Is.EqualTo(builds));
        }

        [Test]
        public void EmissionCoreDoesNotGrantOriginalPinnedPathContract()
        {
            var owner = new Owner();
            _ = new TypeProviderWriter(owner).Write();
            Assert.That(owner.PinnedRelativeFilePath, Is.Null);
            Assert.That(PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner), Is.True);
            Assert.That(PreparedGeneratedTypeHeader.TryCapture(owner), Is.Null);
            owner.Update(relativeFilePath: "Clock.cs");
            var header = PreparedGeneratedTypeHeader.TryCapture(owner);
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.IsCurrent, Is.True);
            owner.Update(relativeFilePath: "Bad\nHeader.cs");
            Assert.That(PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner), Is.True);
            Assert.That(PreparedGeneratedTypeHeader.TryCapture(owner), Is.Null);
            Assert.That(header.IsCurrent, Is.False);
            owner.Update(relativeFilePath: "Clock.cs");
            Assert.That(header.IsCurrent, Is.False);
        }

        [Test]
        public void MetadataBaseKeepsItsSeparateOriginalProof()
        {
            var owner = new MetadataOwner();
            _ = new TypeProviderWriter(owner).Write();
            owner.Update(relativeFilePath: "Clock.cs");
            Assert.That(PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner), Is.False);
            Assert.That(PreparedGeneratedTypeHeader.TryCapture(owner), Is.Null);
            var header = PreparedGeneratedTypeHeader.TryCaptureMetadataBase(owner);
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.IsCurrent, Is.True);
            owner.Type.BaseType!.Namespace = "Changed";
            Assert.That(header.IsCurrent, Is.False);
            owner.Type.BaseType.Namespace = "System";
            Assert.That(header.IsCurrent, Is.False);
        }

        [Test]
        public void AttributeMutationAndRestorationAfterUseKeepNativeFallbackSticky()
        {
            var owner = new Owner();
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            owner.Update(attributes: new[] { new AttributeStatement(typeof(ObsoleteAttribute)) });
            Assert.That(prepared.CanCommitPocGrouping(file), Is.False);
            owner.Update(attributes: Array.Empty<AttributeStatement>());
            Assert.That(prepared.CanCommitPocGrouping(file), Is.False);
            Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(baseline));
        }

        private sealed class MetadataOwner : Owner
        {
            protected override CSharpType? BuildBaseType() => typeof(Exception);
        }

        private class Owner : TypeProvider
        {
            internal int AttributeBuilds { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override IReadOnlyList<MethodBodyStatement> BuildAttributes()
            {
                AttributeBuilds++;
                return Array.Empty<MethodBodyStatement>();
            }
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]),
                    Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }

        private sealed class PolicyOwner(bool decision) : Owner
        {
            internal int PolicyReads { get; private set; }
            protected internal override IReadOnlyList<MethodBodyStatement> BuildAttributesForBackCompatibility(IReadOnlyList<MethodBodyStatement> attributes)
                => Array.Empty<MethodBodyStatement>();
            protected internal override bool ShouldAnalyzeAttributesInReferenceMap
            {
                get { PolicyReads++; return decision; }
            }
        }
    }
}

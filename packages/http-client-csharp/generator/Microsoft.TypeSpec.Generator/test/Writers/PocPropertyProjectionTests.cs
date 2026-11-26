// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    internal class PocPropertyProjectionTests
    {
        [SetUp]
        public async Task SetUp()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(Compilation()));
            CodeModelGenerator.Instance = new ProbeGenerator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = mock.Object.SourceInputModel };
        }
        private sealed class ProbeGenerator(GeneratorContext context) : CodeModelGenerator(context) { }
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("PocPropertyProjection",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        [Test]
        public void RawPropertyInterfaceTypeMustNotReceiveGroupingPermission()
        {
            var owner = new Owner();
            owner.Properties[0].Update(explicitInterface: new CSharpType("I\n// raw type input", "Injected", false, false,
                null, Array.Empty<CSharpType>(), false, false));
            // Match the finalized/native-baseline state of the admitted seeded
            // anchor; cold source caches conservatively refuse every choice.
            string baseline = new TypeProviderWriter(owner).Write().Content;
            TestContext.WriteLine(baseline);
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.Zero, "Unknown emitted type text must retain, even when snapshots are current");
        }

        [TestCase("ref")]
        [TestCase("modifiers")]
        [TestCase("name")]
        [TestCase("setter")]
        [TestCase("attributes")]
        [TestCase("type")]
        [TestCase("owner")]
        public void ConsumedPropertyInputsOutsideProjectionRetain(string input)
        {
            var owner = new Owner();
            var property = owner.Properties[0];
            switch (input)
            {
                case "ref": property.Update(isRef: true); break;
                case "modifiers": property.Update(modifiers: MethodSignatureModifiers.Public | MethodSignatureModifiers.Virtual); break;
                case "name": property.Update(name: "Other"); break;
                case "setter": property.Update(body: new ExpressionPropertyBody(Default, Default)); break;
                case "attributes": property.Update(attributes: [new AttributeStatement(typeof(ObsoleteAttribute))]); break;
                case "type": property.Update(type: typeof(TimeSpan)); break;
                case "owner": property.Update(enclosingType: new Owner()); break;
            }
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.Zero);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(file.Content, Is.EqualTo(baseline));
        }

        [Test]
        public void NonEmittedBackingFieldHintDoesNotBecomeArbitraryPermissionVeto()
        {
            var owner = new Owner();
            owner.Properties[0].BackingField = new FieldProvider(FieldModifiers.Private,
                new CSharpType("Not\nEmitted", "Raw", false, false, null, Array.Empty<CSharpType>(), false, false), "_hint", owner);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            Assert.That(baseline, Does.Not.Contain("Not\nEmitted"));
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            Assert.That(file.QualifiedContent, Is.EqualTo(baseline));
        }

        [Test]
        public void LatePropertyProjectionMutationRevokesAlreadyUsedChoiceWithoutReplay()
        {
            var owner = new Owner();
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            int visits = 0;
            prepared.PocNativeWriteObserver = _ => visits++;
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            int written = visits;
            owner.Properties[0].Update(isRef: true);
            Assert.That(prepared.CanCommitPocGrouping(file), Is.False);
            Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(baseline));
            Assert.That(visits, Is.EqualTo(written));
        }

        private sealed class Owner : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override PropertyProvider[] BuildProperties() =>
                [new PropertyProvider(null, MethodSignatureModifiers.Public, typeof(DateTimeOffset), "Seed", new ExpressionPropertyBody(Default), this)];
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]),
                    Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
    }
}

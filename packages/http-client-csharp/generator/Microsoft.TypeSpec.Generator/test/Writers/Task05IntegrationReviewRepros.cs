// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class Task05IntegrationReviewRepros
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DiscoveryMustNotCallUnknownAttributeCount(bool parameter)
        {
            var compilation = await Setup();
            var attributes = new UnknownAttributes();
            var owner = new Owner(attributes, parameter, false);
            _ = new TypeProviderWriter(owner).Write();
            TestContext.WriteLine($"parameter={parameter}; nativeCountReads={attributes.Reads}");
            if (!parameter) { Assert.That(attributes.Reads, Is.Zero, "Native method-attribute enumeration does not call Count"); }
            attributes.Reads = 0;
            attributes.Throw = true;
            Assert.DoesNotThrow(() =>
            {
                using var prepared = PreparedFieldTypes.CreatePocGrouping(new TypeProvider[] { owner }, compilation, _ => true);
                Assert.That(prepared.PocGroupingCount, Is.Zero);
            });
            Assert.That(attributes.Reads, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BodylessMethodMustNotIntroduceUserCallbackInsideFinalSeal(bool mutateEarlierRoot)
        {
            var compilation = await Setup();
            var owner = new Owner(null, false, true);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            using var prepared = PreparedFieldTypes.CreatePocGrouping(new TypeProvider[] { owner }, compilation, _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1), "Establish actual shortening plan");
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(file);
            TestContext.WriteLine($"uses={prepared.PocGroupingUses}; physical={file.PreparedGroupingCount}; allowed={allowed}; receiptCurrent={file.SourceConsumption?.WasCurrentAtEndOfWrite}; unusedCached={owner.Unused.CachedVariableExpression is not null}");
            Assert.That(allowed, Is.True, "Review counterexample must reach successful callbackful commit before arming seal hook");
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            if (mutateEarlierRoot) { culture.OnRead = count => { if (count == 3) { owner.Right.Update(type: typeof(long)); } }; }
            bool sealedChoice;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                sealedChoice = prepared.SealPocGroupingWithoutCallbacks(file);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            TestContext.WriteLine($"seal={sealedChoice}; cultureReads={culture.Reads}");
            if (mutateEarlierRoot)
            {
                Assert.That(sealedChoice, Is.False, "A callback changed the previously checked grouping root during the final seal");
            }
            else { Assert.That(culture.Reads, Is.Zero, "Final seal cannot invoke ambient user code"); }
            Assert.That(culture.Reads, Is.Zero);
            Assert.That(sealedChoice, Is.False);
            Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(baseline));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LaterFileWithColdParameterCannotCallbackWhileSealingEarlierGroupingFile(bool reverseCaptureOrder)
        {
            var compilation = await Setup();
            var first = new Owner(null, false, false);
            var later = new Owner(null, false, true, "Later");
            string baseline = new TypeProviderWriter(first).Write().Content;
            _ = new TypeProviderWriter(later).Write();
            Assert.That(later.Unused.CachedVariableExpression, Is.Null);
            using var prepared = PreparedFieldTypes.CreatePocGrouping(reverseCaptureOrder ? new TypeProvider[] { later, first } : new TypeProvider[] { first, later }, compilation, _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(2));
            var firstFile = new TypeProviderWriter(first) { PreparedFieldTypes = prepared }.Write();
            _ = new TypeProviderWriter(later) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.CanCommitPocGrouping(firstFile), Is.True);
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture { OnRead = _ => first.Right.Update(type: typeof(long)) };
            bool selected;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                selected = prepared.SealPocGroupingWithoutCallbacks(firstFile);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(selected, Is.False);
            Assert.That(culture.Reads, Is.Zero);
            Assert.That(prepared.SelectPocGrouping(firstFile).Content, Is.EqualTo(baseline));
        }

        [Test]
        public async Task CanonicalCarrierMaterializationEnablesDataOnlySealWithoutPoisoningSource()
        {
            var compilation = await Setup();
            var owner = new Owner(null, false, true);
            _ = new TypeProviderWriter(owner).Write();
            using var prepared = PreparedFieldTypes.CreatePocGrouping(new TypeProvider[] { owner }, compilation, _ => true);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            Assert.That(prepared.SealPocGroupingWithoutCallbacks(file), Is.False);
            // Caller/native materialization, never performed by preparation or seal.
            _ = owner.Unused.AsVariable();
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            bool selected;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                selected = prepared.SealPocGroupingWithoutCallbacks(file);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(selected, Is.True);
            Assert.That(culture.Reads, Is.Zero);
            Assert.That(prepared.SelectPocGrouping(file).Content, Does.Contain("return x == y;"));
        }

        [Test]
        public async Task WrittenUnusedFormalRetainsPositiveGroupingAndPureSeal()
        {
            var compilation = await Setup();
            var owner = new Owner(null, false, false, unusedOnWritten: true);
            _ = owner.Methods;
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            _ = new TypeProviderWriter(owner).Write();
            Assert.That(owner.Unused.CachedVariableExpression, Is.Not.Null, "Only actual native writing materializes the unused formal");
            using var prepared = PreparedFieldTypes.CreatePocGrouping(new TypeProvider[] { owner }, compilation, _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            bool selected;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                selected = prepared.SealPocGroupingWithoutCallbacks(file);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(selected, Is.True);
            Assert.That(culture.Reads, Is.Zero);
            Assert.That(prepared.SelectPocGrouping(file).Content, Does.Contain("return x == y;"));
        }

        [Test]
        public async Task OpaqueSignatureStillGuardsUnmaterializedFormalWhenNewSourceFrameDeclines()
        {
            var compilation = await Setup();
            var owner = new Owner(null, false, false, unusedOnWritten: true);
            var method = System.Linq.Enumerable.Single(owner.Methods);
            var attributes = new UnknownAttributes { Throw = true };
            method.Signature.Update(attributes: attributes);
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, method.BodyExpression!), Is.Null);
            var node = PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "method", 0, "$"),
                method, false, compilation, true);
            Assert.That(System.Linq.Enumerable.Any(node.DescendantsAndSelf(), child => ReferenceEquals(child.Producer, owner.Unused)), Is.False,
                "Opaque signature hides parameter children; retain the owned cold-formal veto even when its binary source frame declines");
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            Assert.That(node.HasCachedParameterEmittedStates, Is.False);
            Assert.That(attributes.Reads, Is.Zero);
            _ = owner.Unused.AsVariable();
            Assert.That(node.HasCachedParameterEmittedStates, Is.True);
        }

        [Test]
        public async Task IndexerFormalIsGuardedWithoutRequiringParameterChildOccurrence()
        {
            var compilation = await Setup();
            var owner = new Owner(null, false, false);
            var formal = new ParameterProvider("unused_parameter", $"", typeof(int));
            var property = new IndexPropertyProvider($"", MethodSignatureModifiers.Public, typeof(int), formal,
                new ExpressionPropertyBody(Default), owner);
            var node = PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "property", 0, "$"),
                property, false, compilation, true);
            Assert.That(System.Linq.Enumerable.Any(node.DescendantsAndSelf(), child => ReferenceEquals(child.Producer, formal)), Is.False);
            Assert.That(formal.CachedVariableExpression, Is.Null);
            Assert.That(node.HasCachedParameterEmittedStates, Is.False);
            _ = formal.AsVariable();
            Assert.That(node.HasCachedParameterEmittedStates, Is.True);
        }

        private static async Task<CSharpCompilation> Setup()
        {
            var compilation = CSharpCompilation.Create("Review", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            return compilation;
        }
        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class ObservingCulture() : CultureInfo("en-US")
        {
            internal bool Armed;
            internal int Reads;
            internal Action<int>? OnRead;
            public override TextInfo TextInfo { get { if (Armed) { Reads++; OnRead?.Invoke(Reads); } return base.TextInfo; } }
        }
        private sealed class UnknownAttributes : IReadOnlyList<AttributeStatement>
        {
            internal int Reads;
            internal bool Throw;
            public int Count { get { Reads++; if (Throw) { throw new InvalidOperationException("review attribute Count"); } return 0; } }
            public AttributeStatement this[int index] => throw new IndexOutOfRangeException();
            public IEnumerator<AttributeStatement> GetEnumerator() => ((IEnumerable<AttributeStatement>)Array.Empty<AttributeStatement>()).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class Owner(UnknownAttributes? attributes, bool parameter, bool bodyless, string name = "Clock", bool unusedOnWritten = false) : TypeProvider
        {
            internal ParameterProvider Unused { get; } = new("unused_parameter", $"", typeof(int));
            internal VariableExpression Right = null!;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => name + ".cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                if (parameter && attributes is not null) { x.Update(attributes: attributes); }
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    unusedOnWritten ? new ParameterProvider[] { x, y, Unused } : new ParameterProvider[] { x, y });
                if (!parameter && attributes is not null) { signature.Update(attributes: attributes); }
                Right = y;
                var m = new MethodProvider(signature, Return(new BinaryOperatorExpression("==", x, Right)), this, xmlDocProvider: XmlDocProvider.Empty);
                if (!bodyless) { return [m]; }
                var hidden = new MethodSignature("Unwritten", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { Unused });
                return [m, new MethodProvider(hidden, (ValueExpression)null!, this, xmlDocProvider: XmlDocProvider.Empty)];
            }
        }
    }
}

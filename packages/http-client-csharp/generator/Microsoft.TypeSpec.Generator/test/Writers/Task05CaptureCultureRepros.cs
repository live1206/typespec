// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    internal class Task05CaptureCultureRepros
    {
        [Test]
        public async Task OpaqueSignatureCaptureDoesNotInvokeColdParameterCultureCode()
        {
            var compilation = CSharpCompilation.Create("CaptureCulture", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            var owner = new Owner();
            var method = owner.Methods.Single();
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            Assert.That(owner.SignatureAttributes.Reads, Is.Zero);
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            PreparedSourceOccurrence? node = null;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                node = PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "method", 0, "$"), method,
                    false, compilation, true);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.DescendantsAndSelf().Any(child => ReferenceEquals(child.Producer, owner.Unused)), Is.False,
                "Opaque signature has no parameter children");
            TestContext.WriteLine($"captureCultureReads={culture.Reads}; attributeCountReads={owner.SignatureAttributes.Reads}; unusedCached={owner.Unused.CachedVariableExpression is not null}");
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            Assert.That(owner.SignatureAttributes.Reads, Is.Zero);
            Assert.That(culture.Reads, Is.Zero, "Conservative opaque discovery must not newly invoke ambient user code");
        }
        [Test]
        public async Task KnownSignatureCaptureDoesNotAddAThirdParameterSpellingRead()
        {
            var compilation = CSharpCompilation.Create("KnownCaptureCulture", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            var owner = new Owner(opaqueSignature: false);
            var method = owner.Methods.Single();
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture { ThrowAt = 3 };
            Exception? caught = null;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                try
                {
                    _ = PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "method", 0, "$"), method,
                        false, compilation, true);
                }
                catch (Exception ex) { caught = ex; }
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            TestContext.WriteLine($"knownSignatureCaptureReads={culture.Reads}; sentinel={caught is not null}; unusedCached={owner.Unused.CachedVariableExpression is not null}");
            Assert.That(caught, Is.Null, "Do not add a parameter-spelling observation beyond original signature traversal");
            Assert.That(culture.Reads, Is.EqualTo(2));
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
        }

        [Test]
        public async Task SourceBinaryFrameWaitsForNativeSpellingWithoutForcingIt()
        {
            var compilation = CSharpCompilation.Create("SourceFrame", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            var owner = new Owner(opaqueSignature: false);
            var method = owner.Methods.Single();
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, method.BodyExpression!), Is.Null);
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            // Explicit existing frame capture keeps its observation contract.
            Assert.That(PreparedMethodParameterFrame.TryCapture(method, method.BodyExpression!), Is.Not.Null);
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            _ = owner.Unused.AsVariable();
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, method.BodyExpression!), Is.Not.Null);
        }

        [Test]
        public async Task InheritedOpaqueNonbinaryFrameStillCarriesColdFormalAvailability()
        {
            var compilation = CSharpCompilation.Create("InheritedFrame", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            var owner = new Owner();
            var flag = new ParameterProvider("flag", $"", typeof(bool));
            var body = flag.AsVariable();
            var signature = new MethodSignature("Identity", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { flag, owner.Unused });
            signature.Update(attributes: owner.SignatureAttributes);
            var method = new MethodProvider(signature, body, owner, xmlDocProvider: XmlDocProvider.Empty);
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, body), Is.Not.Null, "Inherited nonbinary capture is not narrowed");
            var node = PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "method", 0, "$"), method,
                false, compilation, true);
            Assert.That(node.DescendantsAndSelf().Any(child => ReferenceEquals(child.Producer, owner.Unused)), Is.False);
            var bodyNode = node.DescendantsAndSelf().Single(child => ReferenceEquals(child.Producer, body));
            Assert.That(bodyNode.ResolveParameterType(body), Is.EqualTo(new CSharpType(typeof(bool))), "A genuine retained frame resolves the formal");
            Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            Assert.That(bodyNode.HasCachedParameterEmittedStates, Is.False, "Frame-held cold formal, not opaque-signature sibling, supplies this veto");
            _ = owner.Unused.AsVariable();
            Assert.That(bodyNode.HasCachedParameterEmittedStates, Is.True);
        }

        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class ObservingCulture() : CultureInfo("en-US")
        {
            internal bool Armed;
            internal int Reads;
            internal int ThrowAt = int.MaxValue;
            public override TextInfo TextInfo
            {
                get
                {
                    if (Armed)
                    {
                        Reads++;
                        if (Reads == ThrowAt) { throw new InvalidOperationException("capture culture sentinel"); }
                    }
                    return base.TextInfo;
                }
            }
        }
        private sealed class UnknownAttributes : IReadOnlyList<AttributeStatement>
        {
            internal int Reads;
            public int Count { get { Reads++; return 0; } }
            public AttributeStatement this[int index] => throw new IndexOutOfRangeException();
            public IEnumerator<AttributeStatement> GetEnumerator() => ((IEnumerable<AttributeStatement>)Array.Empty<AttributeStatement>()).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class Owner(bool opaqueSignature = true) : TypeProvider
        {
            internal ParameterProvider Unused { get; } = new("unused_parameter", $"", typeof(int));
            internal UnknownAttributes SignatureAttributes { get; } = new();
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y, Unused });
                if (opaqueSignature) { signature.Update(attributes: SignatureAttributes); }
                return [new MethodProvider(signature, new BinaryOperatorExpression("==", x, y), this, xmlDocProvider: XmlDocProvider.Empty)];
            }
        }
    }
}

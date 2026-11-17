// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocFrameCultureCallbackTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RepeatedNewBinaryFrameChecksCanObserveAmbientUserCode(bool materializeUnused)
        {
            var compilation = CSharpCompilation.Create("CultureFrame", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration))
            {
                SourceInputModel = new SourceInputModel(compilation, null)
            };
            var owner = new Owner();
            var method = owner.Methods.Single();
            if (materializeUnused) { _ = owner.Unused.AsVariable(); }
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            bool first = false;
            int afterFirst = 0;
            SentinelException? caught = null;
            try
            {
                CultureInfo.CurrentCulture = culture;
                var frame = PreparedMethodParameterFrame.TryCapture(method, (object?)method.BodyStatements ?? method.BodyExpression!);
                Assert.That(frame, Is.Not.Null, "Actual new binary equality frame, not a stand-in predicate");
                Assert.That(owner.Unused.CachedVariableExpression is not null, Is.EqualTo(materializeUnused));
                culture.Armed = true;
                first = frame!.IsCurrent();
                afterFirst = culture.Reads;
                culture.ThrowAt = culture.Reads + 1;
                try { _ = frame.IsCurrent(); }
                catch (SentinelException ex) { caught = ex; }
            }
            finally
            {
                culture.Armed = false;
                CultureInfo.CurrentCulture = saved;
            }
            Assert.That(first, Is.True);
            if (materializeUnused)
            {
                Assert.That(afterFirst, Is.Zero);
                Assert.That(caught, Is.Null);
            }
            else
            {
                Assert.That(afterFirst, Is.GreaterThan(0));
                Assert.That(caught, Is.Not.Null, "A second check must not silently reuse the earlier successful result");
                Assert.That(caught!.StackTrace, Does.Contain("PreparedMethodParameterFrame"));
                Assert.That(caught.StackTrace, Does.Contain("ToIdentifierName"));
                Assert.That(owner.Unused.CachedVariableExpression, Is.Null);
            }
            TestContext.Out.WriteLine($"materialized={materializeUnused}; first={first}; firstReads={afterFirst}; totalReads={culture.Reads}; sentinel={caught is not null}");
        }

        [Test]
        public async Task EarlierBodyMismatchDoesNotSuppressParameterSpellingCallback()
        {
            var owner = await NewOwner();
            var method = owner.Methods.Single();
            var frame = PreparedMethodParameterFrame.TryCapture(method, method.BodyExpression!)!;
            owner.Left.Update(type: typeof(int)); // Alias revision, not the formal's cached carrier revision.
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture { ThrowAt = 1 };
            SentinelException? caught = null;
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                try { _ = frame.IsCurrent(); } catch (SentinelException ex) { caught = ex; }
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(caught, Is.Not.Null);
            Assert.That(culture.Reads, Is.EqualTo(1));
        }

        [Test]
        public async Task CaptureTimeParameterCallbackCannotReplaceOriginalRegionFacts()
        {
            var owner = await NewOwner();
            var method = owner.Methods.Single();
            var saved = CultureInfo.CurrentCulture;
            var culture = new ObservingCulture();
            PreparedMethodParameterFrame? frame;
            culture.OnRead = count => { if (count == 1) owner.Left.Update(type: typeof(int)); };
            try
            {
                CultureInfo.CurrentCulture = culture; culture.Armed = true;
                frame = PreparedMethodParameterFrame.TryCapture(method, method.BodyExpression!);
            }
            finally { culture.Armed = false; CultureInfo.CurrentCulture = saved; }
            Assert.That(culture.Reads, Is.GreaterThan(0));
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.IsCurrent(), Is.False, "Region snapshot predates parameter spelling effects");
        }

        private static async Task<Owner> NewOwner()
        {
            var compilation = CSharpCompilation.Create("CultureFrame", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            return new Owner();
        }

        private sealed class SentinelException : Exception { }
        private sealed class ObservingCulture() : CultureInfo("en-US")
        {
            internal bool Armed;
            internal int Reads;
            internal int ThrowAt = int.MaxValue;
            internal Action<int>? OnRead;
            public override TextInfo TextInfo
            {
                get
                {
                    if (Armed)
                    {
                        Reads++;
                        OnRead?.Invoke(Reads);
                        if (Reads == ThrowAt) throw new SentinelException();
                    }
                    return base.TextInfo;
                }
            }
        }
        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class Owner : TypeProvider
        {
            internal ParameterProvider Unused { get; } = new("unused_parameter", $"", typeof(int));
            internal VariableExpression Left = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Left = new VariableExpression(typeof(int), x.AsVariable().Declaration);
                var binary = new BinaryOperatorExpression("==", Left, y);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    new ParameterProvider[] { x, y, Unused }), binary, this, xmlDocProvider: XmlDocProvider.Empty)];
            }
        }
    }
}

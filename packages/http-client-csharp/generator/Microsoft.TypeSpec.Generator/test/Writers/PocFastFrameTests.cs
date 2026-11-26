// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
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
    internal class PocFastFrameTests
    {
        [TestCase("left-type")]
        [TestCase("right-type")]
        [TestCase("left-name")]
        [TestCase("declaration")]
        [TestCase("keyword")]
        [TestCase("statement")]
        [TestCase("signature")]
        [TestCase("parameter")]
        [TestCase("parameter-order")]
        [TestCase("signature-attributes")]
        [TestCase("parameter-attributes")]
        public async Task OriginalFullFrameObservesAndLatchesTransientChanges(string mutation)
        {
            await Setup();
            var owner = new Owner(false);
            var method = owner.Methods.Single();
            var frame = PreparedMethodParameterFrame.TryCapture(method, method.BodyStatements!);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.IsCurrent(), Is.True);
            Action restore;
            switch (mutation)
            {
                case "left-type": owner.Left.Update(type: typeof(long)); restore = () => owner.Left.Update(type: typeof(int)); break;
                case "right-type": owner.Right.Update(type: typeof(long)); restore = () => owner.Right.Update(type: typeof(int)); break;
                case "left-name": owner.Left.Update(name: "changed"); restore = () => owner.Left.Update(name: "x"); break;
                case "declaration": owner.Left.Declaration.Update(name: "changed"); restore = () => owner.Left.Declaration.Update(name: "x"); break;
                case "keyword": owner.Keyword.Update("throw", owner.Binary); restore = () => owner.Keyword.Update("return", owner.Binary); break;
                case "statement": owner.Statement.Update(Literal(1)); restore = () => owner.Statement.Update(owner.Keyword); break;
                case "signature": method.Signature.Update(name: "Changed"); restore = () => method.Signature.Update(name: "M"); break;
                case "parameter": owner.X.Update(type: typeof(long)); restore = () => owner.X.Update(type: typeof(int)); break;
                case "parameter-order": (owner.Parameters[0], owner.Parameters[1]) = (owner.Parameters[1], owner.Parameters[0]); restore = () => (owner.Parameters[0], owner.Parameters[1]) = (owner.Parameters[1], owner.Parameters[0]); break;
                case "signature-attributes": owner.SignatureAttributes.Add(new AttributeStatement(typeof(ObsoleteAttribute))); restore = () => owner.SignatureAttributes.Clear(); break;
                default: owner.ParameterAttributes.Add(new AttributeStatement(typeof(ObsoleteAttribute))); restore = () => owner.ParameterAttributes.Clear(); break;
            }
            Assert.That(frame.IsCurrent(), Is.False, "Observe the bad state before restoration");
            restore();
            Assert.That(frame.IsCurrent(), Is.False, "Restoration does not clear observed invalidation");
            Assert.That(PreparedMethodParameterFrame.TryCapture(method, method.BodyStatements!)!.IsCurrent(), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StableOriginalReturnAndArrowFramesKeepParameterBinding(bool arrow)
        {
            await Setup();
            var owner = new Owner(arrow);
            var method = owner.Methods.Single();
            var frame = PreparedMethodParameterFrame.TryCapture(method, (object?)method.BodyStatements ?? method.BodyExpression!);
            Assert.That(frame, Is.Not.Null);
            for (int i = 0; i < 10; i++) { Assert.That(frame!.IsCurrent(), Is.True); }
            Assert.That(frame!.Resolve(owner.Left), Is.SameAs(owner.X.Type));
            Assert.That(frame.Resolve(owner.Right), Is.SameAs(owner.Y.Type));
        }

        [Test]
        public async Task UnknownAttributeCarrierIsNotReadByFrameDiscovery()
        {
            await Setup();
            var owner = new Owner(true);
            var method = owner.Methods.Single();
            var attributes = new ThrowingAttributes();
            method.Signature.Update(attributes: attributes);
            Assert.DoesNotThrow(() => PreparedMethodParameterFrame.TryCapture(method, method.BodyExpression!));
            Assert.That(attributes.Reads, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FiniteBodyHasDirectCapabilityAndUnsupportedHintKeepsFullFallback(bool arrow)
        {
            await Setup();
            var owner = new Owner(arrow);
            var method = owner.Methods.Single();
            var body = (object?)method.BodyStatements ?? method.BodyExpression!;
            var frame = PreparedMethodParameterFrame.TryCapture(method, body)!;
            Assert.That(frame.HasDirectEqualityRegion, Is.True);
            Assert.That(frame.IsCurrent(), Is.True);
            owner.Left.Update(type: typeof(string));
            Assert.That(frame.IsCurrent(), Is.False);
            var fallback = PreparedMethodParameterFrame.TryCapture(method, body)!;
            Assert.That(fallback.HasDirectEqualityRegion, Is.False);
            Assert.That(fallback.IsCurrent(), Is.True);
            Assert.That(fallback.Resolve(owner.Left), Is.SameAs(owner.X.Type));
        }

        [Test]
        public async Task InPlaceHintTypeMutationCannotPassDirectRegion()
        {
            await Setup();
            var owner = new Owner(true);
            var method = owner.Methods.Single();
            var hint = new CSharpType(typeof(int));
            owner.Left.Update(type: hint);
            var frame = PreparedMethodParameterFrame.TryCapture(method, method.BodyExpression!)!;
            Assert.That(frame.HasDirectEqualityRegion, Is.True);
            Assert.That(frame.IsCurrent(), Is.True);
            hint.Update(name: "Changed");
            Assert.That(frame.IsCurrent(), Is.False);
            hint.Update(name: "Int32");
            Assert.That(frame.IsCurrent(), Is.False);
        }

        private sealed class ThrowingAttributes : IReadOnlyList<AttributeStatement>
        {
            internal int Reads;
            public int Count { get { Reads++; throw new InvalidOperationException("arbitrary Count"); } }
            public AttributeStatement this[int index] => throw new InvalidOperationException("arbitrary indexer");
            public IEnumerator<AttributeStatement> GetEnumerator() => throw new InvalidOperationException("arbitrary enumeration");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private static async Task Setup()
        {
            var compilation = CSharpCompilation.Create("FastFrames", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
        }
        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class Owner(bool arrow) : TypeProvider
        {
            internal ParameterProvider X { get; } = new("x", $"", typeof(int));
            internal ParameterProvider Y { get; } = new("y", $"", typeof(int));
            internal VariableExpression Left = null!;
            internal VariableExpression Right = null!;
            internal BinaryOperatorExpression Binary = null!;
            internal KeywordExpression Keyword = null!;
            internal ExpressionStatement Statement = null!;
            internal ParameterProvider[] Parameters = null!;
            internal List<AttributeStatement> SignatureAttributes { get; } = [];
            internal List<AttributeStatement> ParameterAttributes { get; } = [];
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                X.Update(attributes: ParameterAttributes);
                Parameters = [X, Y];
                Left = X; Right = Y;
                Binary = new("==", Left, Right);
                Keyword = new("return", Binary);
                Statement = new(Keyword);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, Parameters);
                signature.Update(attributes: SignatureAttributes);
                return [arrow ? new MethodProvider(signature, Binary, this) : new MethodProvider(signature, Statement, this)];
            }
        }
    }
}

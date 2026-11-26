// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
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
namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class NullBinaryCaptureComparison
    {
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public async Task CompareNullOperandCaptureAndNativeOutcome(bool nullLeft, bool capture)
        {
            var compilation = CSharpCompilation.Create("NullOperand", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(compilation));
            CodeModelGenerator.Instance = new Generator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = new SourceInputModel(compilation, null) };
            var owner = new Owner(nullLeft);
            var method = owner.Methods.Single();
            if (capture)
            {
                Assert.DoesNotThrow(() => PreparedSourceOccurrence.Capture(new SourceOccurrenceId(Guid.NewGuid(), "Clock.cs", "type", "method", 0, "$"), method,
                    false, compilation, true), "Unknown malformed operands must not add preparation observations");
            }
            else
            {
                Assert.Throws<NullReferenceException>(() => new TypeProviderWriter(owner).Write(),
                    "Preserve the native malformed-operand failure rather than supporting malformed emission");
            }
        }
        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class Owner(bool nullLeft) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var binary = new BinaryOperatorExpression("==", nullLeft ? (ValueExpression)null! : (ValueExpression)x, nullLeft ? (ValueExpression)y : (ValueExpression)null!);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y }), binary, this, xmlDocProvider: XmlDocProvider.Empty)];
            }
        }
    }
}

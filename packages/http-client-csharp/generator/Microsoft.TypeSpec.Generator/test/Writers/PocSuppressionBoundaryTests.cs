// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocSuppressionBoundaryTests
    {
        [TestCase("none")]
        [TestCase("plain")]
        [TestCase("nested")]
        [TestCase("direct")]
        [TestCase("null")]
        public async Task SuppressionsPreserveNativeObservationAndFailureBoundary(string mode)
        {
            var compilation = CSharpCompilation.Create("Task05SuppressionBoundary",
                references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Owner();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-suppression-boundary", compilation, ["controlled-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            ValueExpression code = new LiteralExpression("CS0618");
            if (mode == "plain" || mode == "nested")
            {
                code = new ScopedApi(new CSharpType("Payload\n#error TASK05_SUPPRESSION_RAW\n//", "Probe",
                    false, false, null, [], true, false), code);
            }
            if (mode == "nested" || mode == "direct")
            {
                code = new ScopedApi<string>(code);
            }
            owner.ProcessTypeForBackCompatibility();
            if (mode != "none")
            {
                owner.Methods[0].Suppressions = new SuppressionStatement[]
                {
                    new SuppressionStatement(null, code, mode == "null" ? null! : "fixture")
                };
            }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(new TypeProvider[] { owner });
            PreparedFieldTypes? prepared = null;
            Assert.DoesNotThrow(() => prepared = PreparedFieldTypes.CreateForGeneration(generator,
                new TypeProvider[] { owner }, references.ShouldWriteProvider));
            Assert.That(prepared, Is.Not.Null);
            using (prepared)
            {
                if (mode == "null")
                {
                    Assert.That(prepared!.PocGroupingCount, Is.Zero);
                    Assert.Throws<ArgumentNullException>(() => new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write());
                    return;
                }
                Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1), "Preserve the existing preparation admission independently of native traversal");
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(file.SourceConsumption, Is.Not.Null);
                if (mode == "none")
                {
                    Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
                    Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
                    Assert.That(prepared.SealPocGroupingWithoutCallbacks(file), Is.True);
                    Assert.That(file.Content, Does.Contain("return x == y;"));
                    Assert.That(file.QualifiedContent, Does.Contain("return (x == y);"));
                    return;
                }
                Assert.That(prepared.PocGroupingUses, Is.Zero);
                Assert.That(file.SourceConsumption!.WasCurrentAtEndOfWrite, Is.False);
                Assert.That(file.PreparedGroupingCount, Is.Zero);
                Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
                Assert.That(file.Content, Does.Contain("return (x == y);"));
                Assert.That(file.SelectPreparedForms(true, true, true).Content, Is.EqualTo(file.Content));
                if (mode == "plain" || mode == "nested")
                {
                    Assert.That(file.Content, Does.Contain("\n#error TASK05_SUPPRESSION_RAW"));
                }
            }
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Owner : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    new ParameterProvider[] { x, y });
                return [new MethodProvider(signature, Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
    }
}

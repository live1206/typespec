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
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocNormalGenerationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalSliceUsesNormalPreparationWithoutDiscoveryWrite(bool arrow)
        {
            var compilation = CSharpCompilation.Create("Task05Normal",
                references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Owner(arrow);
            var generator = new NormalGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            int sourceChecks = 0;
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory, "task05-normal",
                    compilation, ["controlled-normal-input-set"], () => { sourceChecks++; return true; })
            };
            CodeModelGenerator.Instance = generator;
            owner.ProcessTypeForBackCompatibility();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(new TypeProvider[] { owner });
            int writePolicyChecks = 0;
            bool ShouldWrite(TypeProvider provider) { writePolicyChecks++; return references.ShouldWriteProvider(provider); }
            TestContext.WriteLine($"NORMAL-CACHES attrs={owner.CachedWriteAttributes is not null}; warnings={owner.CachedDisabledFileWarnings is not null}; interfaces={owner.CachedImplements is not null}; custom={owner.IsCustomCodeViewMaterialized}; header={PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner)}; map={generator.TypeFactory.CSharpTypeMap.Count}");
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, new TypeProvider[] { owner }, ShouldWrite);
            Assert.That(prepared, Is.Not.Null, "Real policy and complete-source input gate must pass");
            TestContext.WriteLine($"NORMAL-PREP plans={prepared!.PocGroupingCount}; sourceChecks={sourceChecks}; writePolicyChecks={writePolicyChecks}");
            Assert.That(owner.Fields, Is.Empty);
            Assert.That(owner.Properties, Is.Empty);
            Assert.That(owner.CachedDisabledFileWarnings, Is.Null, "Capture must not materialize the native warning getter");
            Assert.That(PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner), Is.False,
                "The original no-witness header contract remains strict");
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1), "Normal source preparation must actually admit the no-type-seed slice");
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(owner.CachedDisabledFileWarnings, Is.Not.Null, "Only actual native writing materializes warnings");
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            int sourceBeforeSeal = sourceChecks;
            int policyBeforeSeal = writePolicyChecks;
            Assert.That(prepared.SealPocGroupingWithoutCallbacks(file), Is.True);
            Assert.That(sourceChecks, Is.EqualTo(sourceBeforeSeal), "Final data seal must not call the source host");
            Assert.That(writePolicyChecks, Is.EqualTo(policyBeforeSeal), "Final data seal must not call the write policy");
            Assert.That(file.Content, Does.Contain(arrow ? "=> x == y;" : "return x == y;"));
            Assert.That(file.QualifiedContent, Does.Contain(arrow ? "=> (x == y);" : "return (x == y);"));
            TestContext.WriteLine($"NORMAL-END sourceChecks={sourceChecks}; writePolicyChecks={writePolicyChecks}");
        }

        private sealed class NormalGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
            // Deliberately do not override TypeFactory or GetWriter.
        }

        private sealed class Owner(bool arrow) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y });
                var binary = new BinaryOperatorExpression("==", x, y);
                return [arrow ? new MethodProvider(signature, binary, this) : new MethodProvider(signature, Return(binary), this)];
            }
        }
    }
}

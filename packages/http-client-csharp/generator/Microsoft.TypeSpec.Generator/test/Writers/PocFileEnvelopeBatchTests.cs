// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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
    internal class PocFileEnvelopeBatchTests
    {
        [Test]
        public async Task EnvelopeIsFileScopedWhileEveryOccurrenceStillParticipates()
        {
            int one = await Measure(1, false);
            int eight = await Measure(8, false);
            Assert.That(one, Is.GreaterThan(0));
            Assert.That(eight, Is.EqualTo(one), "Retained full file envelope is not re-invoked per occurrence");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task EveryOccurrenceParticipatesInTheFinalFileSeal(int index)
        {
            _ = await Measure(8, true, index);
        }

        [TestCase("full")]
        [TestCase("final")]
        [TestCase("node")]
        [TestCase("count")]
        [TestCase("receipt")]
        [TestCase("file")]
        [TestCase("policy")]
        public async Task NonFirstProvenanceAndPublicationBoundariesFailClosed(string tamper)
        {
            _ = await Measure(8, false, tamper: tamper);
        }

        private sealed class IdentityRewriter : LibraryRewriter { }

        private static async Task<int> Measure(int methods, bool mutateLast, int mutationIndex = 7, string? tamper = null)
        {
            var compilation = CSharpCompilation.Create("Batch", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var generator = new Generator(new GeneratorContext(mock.Object.Configuration));
            int sourceChecks = 0;
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory, "batch",
                    compilation, ["controlled-batch-inputs"], () => { sourceChecks++; return true; })
            };
            CodeModelGenerator.Instance = generator;
            var owner = new Owner(methods);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(methods));
            int writes = 0;
            prepared.PocNativeWriteObserver = node => { if (node is BinaryOperatorExpression) writes++; };
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(file.PreparedGroupingCount, Is.EqualTo(methods));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(methods));
            Assert.That(writes, Is.EqualTo(methods));
            if (tamper is not null)
            {
                var output = file;
                if (tamper is "full" or "final" or "node")
                {
                    var dictionary = (Dictionary<SourceOccurrenceId, (PreparedSourceFile File, PreparedSourceOccurrence Node, Func<bool> Envelope, Func<bool> FinalData)>)
                        typeof(PreparedFieldTypes).GetField("_pocGrouping", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared)!;
                    var id = dictionary.Keys.Last();
                    var old = dictionary[id];
                    dictionary[id] = tamper switch
                    {
                        "full" => (old.File, old.Node, () => old.Envelope(), old.FinalData),
                        "final" => (old.File, old.Node, old.Envelope, () => old.FinalData()),
                        _ => (old.File, dictionary.Values.First().Node, old.Envelope, old.FinalData)
                    };
                }
                if (tamper is "count" or "receipt" or "file")
                {
                    var receipt = file.SourceConsumption!;
                    if (tamper == "receipt")
                    {
                        var missing = prepared.SourceFiles.SelectMany(f => f.Roots).SelectMany(r => r.DescendantsAndSelf())
                            .Last(node => node.Producer is BinaryOperatorExpression).Id;
                        var consumed = receipt.Consumed.Remove(missing);
                        Assert.That(consumed.Length, Is.EqualTo(receipt.Consumed.Length - 1));
                        receipt = receipt with { Consumed = consumed };
                    }
                    output = new CodeFile(file.Content, tamper == "file" ? "Foreign.cs" : file.Name, file.QualifiedContent,
                        receipt, ImmutableArray<PreparedNameAlternative>.Empty, tamper == "count" ? methods - 1 : methods);
                }
                if (tamper == "policy") { generator.AddRewriter(new IdentityRewriter()); }
                Assert.That(prepared.CanCommitPocGrouping(output), Is.False);
                Assert.That(prepared.SealPocGroupingWithoutCallbacks(output), Is.False);
                Assert.That(prepared.SelectPocGrouping(output).Content, Is.EqualTo(baseline));
                Assert.That(writes, Is.EqualTo(methods));
                return 0;
            }
            int before = sourceChecks;
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            int fullChecks = sourceChecks - before;
            before = sourceChecks;
            Assert.That(prepared.SealPocGroupingWithoutCallbacks(file), Is.True);
            Assert.That(sourceChecks, Is.EqualTo(before));
            if (mutateLast)
            {
                owner.Rights[mutationIndex].Update(type: typeof(long));
                Assert.That(prepared.SealPocGroupingWithoutCallbacks(file), Is.False);
                Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(baseline));
                Assert.That(writes, Is.EqualTo(methods));
            }
            return fullChecks;
        }

        private sealed class Generator(GeneratorContext context) : CodeModelGenerator(context) { }
        private sealed class Owner(int methods) : TypeProvider
        {
            internal List<VariableExpression> Rights { get; } = [];
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var result = new List<MethodProvider>();
                for (int i = 0; i < methods; i++)
                {
                    var x = new ParameterProvider("x", $"", typeof(int));
                    var y = new ParameterProvider("y", $"", typeof(int));
                    VariableExpression right = y;
                    Rights.Add(right);
                    result.Add(new MethodProvider(new MethodSignature("M" + i, null, MethodSignatureModifiers.Public, typeof(bool), null,
                        new ParameterProvider[] { x, y }), Return(new BinaryOperatorExpression("==", x, right)), this));
                }
                return result.ToArray();
            }
        }
    }
}

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
    internal class PocGroupingLateFallbackTests
    {
        [SetUp]
        public async Task SetUp()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(Compilation()));
            CodeModelGenerator.Instance = new ProbeGenerator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = mock.Object.SourceInputModel };
        }

        private sealed class ProbeGenerator(GeneratorContext context) : CodeModelGenerator(context) { }
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("PocGroupingLate",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        [TestCase("before")]
        [TestCase("during")]
        [TestCase("after")]
        public void ChangedChildRevisionRetainsNativeCompanionWithoutReplay(string phase)
        {
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            VariableExpression left = x;
            VariableExpression right = y;
            var binary = new BinaryOperatorExpression("==", left, right);
            var owner = new Owner(binary, [x, y]);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            int hookCalls = 0;
            var visits = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            prepared.PocNativeWriteObserver = node => { visits.TryGetValue(node, out int count); visits[node] = count + 1; };
            if (phase == "before") { right.Update(type: typeof(long)); }
            if (phase == "during") { prepared.PocAfterOpening = () => { hookCalls++; right.Update(type: typeof(long)); }; }
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            if (phase == "after")
            {
                Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
                right.Update(type: typeof(long));
            }
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(phase == "before" ? 0 : 1));
            Assert.That(hookCalls, Is.EqualTo(phase == "during" ? 1 : 0));
            Assert.That(visits[binary], Is.EqualTo(1));
            Assert.That(visits[left], Is.EqualTo(1));
            Assert.That(visits[right], Is.EqualTo(1));
            if (phase != "before")
            {
                Assert.That(file.Content, Does.Contain("return x == y;"));
                Assert.That(prepared.CanCommitPocGrouping(file), Is.False);
            }
            var selected = prepared.SelectPocGrouping(file);
            Assert.That(selected.Content, Is.EqualTo(baseline));
            Assert.That(selected.Content, Does.Contain("return (x == y);"));
            Assert.That(binary.AddParentheses, Is.True);
            Assert.That(visits[binary], Is.EqualTo(1), "Selection cannot replay the native writer");
        }

        [Test]
        public void LateTypeFactorySuppressionMapChangeRejectsUsedChoice()
        {
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            var binary = new BinaryOperatorExpression("==", x, y);
            var owner = new Owner(binary, [x, y]);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(prepared.CanCommitPocGrouping(file), Is.True);
            CodeModelGenerator.Instance.TypeFactory.CSharpTypeMap[typeof(int)] = owner;
            Assert.That(prepared.CanCommitPocGrouping(file), Is.False);
            Assert.That(prepared.SelectPocGrouping(file).Content, Is.EqualTo(baseline));
        }

        [Test]
        public void IndirectParameterFieldSuppressionCannotBypassEmissionContract()
        {
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            var owner = new Owner(new BinaryOperatorExpression("==", x, y), [x, y]);
            var associated = new FieldProvider(FieldModifiers.Private, typeof(int), "_associated", owner);
            associated.Update(suppressions: [new SuppressionStatement(null,
                new LiteralExpression("CS0168\n// indirect marker"), "native indirect route")]);
            x.Update(field: associated);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            Assert.That(baseline, Does.Contain("indirect marker"));
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.Zero);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(file.Content, Is.EqualTo(baseline));
        }

        [Test]
        public void UnsupportedExtraRootRetainsInsteadOfAssumingLocalAncestryIsEnough()
        {
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            var owner = new Owner(new BinaryOperatorExpression("==", x, y), [x, y]);
            owner.Update(fields: [new FieldProvider(FieldModifiers.Private, typeof(int), "_extra", owner)]);
            string baseline = new TypeProviderWriter(owner).Write().Content;
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.Zero);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(file.Content, Is.EqualTo(baseline));
        }

        private sealed class Owner(ValueExpression body, ParameterProvider[] parameters) : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods() =>
                [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, parameters), Return(body), this)];
        }
    }
}

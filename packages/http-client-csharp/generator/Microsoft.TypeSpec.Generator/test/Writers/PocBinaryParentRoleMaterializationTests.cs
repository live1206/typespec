// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
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
    internal class PocBinaryParentRoleMaterializationTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public Task ExtraValidationDoesNotAdvanceLazyCollectionMaterialization(bool opaque, bool withDirect) =>
            CheckMaterialization(opaque, withDirect, serialization: false);

        [TestCase(false)]
        [TestCase(true)]
        public Task ExtraValidationDoesNotAdvanceLazySerializationMaterialization(bool withDirect) =>
            CheckMaterialization(opaque: true, withDirect, serialization: true);

        private static async Task CheckMaterialization(bool opaque, bool withDirect, bool serialization)
        {
            var compilation = CSharpCompilation.Create("Task05Materialization",
                references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var clock = new Clock();
            var control = new Control();
            if (opaque)
            {
                if (serialization) { clock.Update(serializations: clock.LazyNested); }
                else { clock.Update(nestedTypes: clock.LazyNested); }
            }
            var output = withDirect ? new TestOutputLibrary(control, clock) : new TestOutputLibrary(clock);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-first-materialization", compilation, ["controlled-materialization-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(clock.PathInitialized, Is.True, "The path dependency must already be materialized before the added validation reads");
            Assert.That(clock.PathInitializations, Is.EqualTo(1));
            Assert.That(clock.LazyNested.Loaded, Is.EqualTo(opaque && serialization),
                "Normal reference analysis initializes serialization Count, but enumerates nested types without requiring their Count data");
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            bool unavailableNestedFacts = opaque && !serialization;
            int expected = (withDirect ? 1 : 0) + (unavailableNestedFacts ? 0 : 2);
            int plans = prepared!.PocGroupingCount;
            if (withDirect)
            {
                var direct = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                Assert.That(direct.Content, Does.Contain("return x == y;"));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(1), "Preserve the original direct family even when the new family is unavailable");
            }
            bool? countLoadedAtFirstLeaf = null;
            int innerVisits = 0;
            int leafVisits = 0;
            prepared.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, clock.Inner)) { innerVisits++; }
                if (ReferenceEquals(node, clock.Leaf))
                {
                    leafVisits++;
                    countLoadedAtFirstLeaf = clock.LazyNested.Loaded;
                }
            };
            var file = new TypeProviderWriter(clock) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(file) && prepared.SealPocGroupingWithoutCallbacks(file);
            var selected = file.SelectPreparedForms(true, true, allowed);
            Assert.That(innerVisits, Is.EqualTo(1));
            Assert.That(leafVisits, Is.EqualTo(1));
            Assert.That(clock.MethodBuilds, Is.EqualTo(1));
            Assert.That(clock.PathInitializations, Is.EqualTo(1), "Repeated path queries must reuse the already-initialized dependency");
            TestContext.WriteLine($"FIRST-MATERIALIZATION opaque={opaque}; serialization={serialization}; direct={withDirect}; plans={plans}; uses={prepared.PocGroupingUses}; countAtFirstLeaf={countLoadedAtFirstLeaf}; countAfter={clock.LazyNested.Loaded}; pathBuilds={clock.PathInitializations}; methodBuilds={clock.MethodBuilds}");
            Assert.Multiple(() =>
            {
                Assert.That(plans, Is.EqualTo(expected));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
                Assert.That(selected.Content, Does.Contain(unavailableNestedFacts ? "return ((x + y) == z);" : "return x + y == z;"));
                Assert.That(countLoadedAtFirstLeaf, Is.EqualTo(opaque && (serialization || withDirect)),
                    "Without an old grouping use, native nested-type writing must remain the first consumer of opaque Count data");
            });
            bool expectedLoaded = opaque;
            Assert.That(clock.LazyNested.Loaded, Is.EqualTo(expectedLoaded));
            Assert.That(clock.LazyNested.Materializations, Is.EqualTo(expectedLoaded ? 1 : 0));
            Assert.That(clock.Outer!.AddParentheses && clock.Inner!.AddParentheses, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExtraValidationDoesNotRebuildColdUnwrittenRootInsideMethod(bool withDirect)
        {
            var compilation = CSharpCompilation.Create("Task05UnwrittenMaterialization",
                references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var clock = new Clock();
            var hidden = new Hidden();
            var control = new Control();
            var output = withDirect ? new TestOutputLibrary(control, clock, hidden) : new TestOutputLibrary(clock, hidden);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-unwritten-materialization", compilation, ["controlled-unwritten-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            bool ShouldWrite(TypeProvider provider)
            {
                _ = provider.Type.Name;
                return !ReferenceEquals(provider, hidden);
            }
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, ShouldWrite);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(hidden.CachedType, Is.Not.Null);
            int buildsBefore = hidden.NameBuilds;
            if (withDirect)
            {
                var direct = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                Assert.That(direct.Content, Does.Contain("return x == y;"));
                Assert.That(prepared!.PocGroupingUses, Is.EqualTo(1));
            }
            int innerVisits = 0;
            bool? typeCachedAtFirstLeaf = null;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, clock.Inner))
                {
                    innerVisits++;
                    hidden.Reset();
                    Assert.That(hidden.CachedType, Is.Null, "A supported public reset must genuinely make this omitted root cold");
                }
                if (ReferenceEquals(node, clock.Leaf)) { typeCachedAtFirstLeaf = hidden.CachedType is not null; }
            };
            var file = new TypeProviderWriter(clock) { PreparedFieldTypes = prepared }.Write();
            TestContext.WriteLine($"UNWRITTEN-MATERIALIZATION direct={withDirect}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; visits={innerVisits}; typeAtFirstLeaf={typeCachedAtFirstLeaf}; typeAfter={hidden.CachedType is not null}; buildsBefore={buildsBefore}; buildsAfter={hidden.NameBuilds}");
            Assert.That(innerVisits, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(typeCachedAtFirstLeaf, Is.False,
                    "A new child permission query must not rebuild an unwritten root's cold Type inside the method");
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(withDirect ? 1 : 0));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(withDirect ? 1 : 0));
                Assert.That(file.Content, Does.Contain("return ((x + y) == z);"));
            });
            Assert.That(hidden.CachedType is not null, Is.EqualTo(withDirect),
                "Preserve the inherited old-direct snapshot query after the method, without introducing one when there were no old candidates");
            if (!withDirect) { Assert.That(hidden.NameBuilds, Is.EqualTo(buildsBefore)); }
            Assert.That(clock.Outer!.AddParentheses && clock.Inner!.AddParentheses, Is.True);
        }

        private sealed class Hidden : TypeProvider
        {
            internal int NameBuilds;
            protected override string BuildName() { NameBuilds++; return "Hidden"; }
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Hidden.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Clock : TypeProvider
        {
            private readonly Lazy<string> _path;
            internal Clock()
            {
                _path = new Lazy<string>(() => { PathInitializations++; return "Clock.cs"; });
                LazyNested = new LazyNestedTypes();
            }
            internal int PathInitializations;
            internal int MethodBuilds;
            internal bool PathInitialized => _path.IsValueCreated;
            internal LazyNestedTypes LazyNested { get; }
            internal BinaryOperatorExpression? Outer { get; private set; }
            internal BinaryOperatorExpression? Inner { get; private set; }
            internal VariableExpression? Leaf { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => _path.Value;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                MethodBuilds++;
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Leaf = x;
                Inner = new BinaryOperatorExpression("+", Leaf, y);
                Outer = new BinaryOperatorExpression("==", Inner, z);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public,
                    typeof(bool), null, new ParameterProvider[] { x, y, z }), Return(Outer), this)];
            }
        }

        private sealed class LazyNestedTypes : IReadOnlyList<TypeProvider>
        {
            private readonly Lazy<int> _count;
            internal LazyNestedTypes() { _count = new Lazy<int>(() => { Materializations++; return 0; }); }
            internal int Materializations;
            internal bool Loaded => _count.IsValueCreated;
            public int Count => _count.Value;
            public TypeProvider this[int index] => throw new IndexOutOfRangeException();
            public IEnumerator<TypeProvider> GetEnumerator() => ((IEnumerable<TypeProvider>)Array.Empty<TypeProvider>()).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class Control : TypeProvider
        {
            protected override string BuildName() => "Control";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Control.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public,
                    typeof(bool), null, new ParameterProvider[] { x, y }), Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
    }
}

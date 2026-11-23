// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
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
    internal class PocIfConditionPhaseDependencyTests
    {
        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task NewIfChecksDoNotFirstLoadAnotherColdDependency(string mode, bool withDirect)
        {
            var compilation = CSharpCompilation.Create("IfPhaseDependency", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var clock = new Clock(mode);
            var control = new Control();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                withDirect ? new TestOutputLibrary(control, clock) : new TestOutputLibrary(clock));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "if-phase-dependency", compilation, ["controlled-if-phase-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            if (mode == "serialization") { clock.Update(serializations: new SerializationCarrier(clock)); }
            var roots = generator.OutputLibrary.TypeProviders;
            clock.Phase = "backcompat";
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            clock.Phase = "reference-map";
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(clock.Early.IsValueCreated, Is.True);
            Assert.That(clock.Late.IsValueCreated, Is.False);
            Assert.That(clock.CachedNestedTypes, Is.TypeOf<TypeProvider[]>());
            clock.Phase = "prepare";
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            if (withDirect)
            {
                clock.Phase = "write-old-control";
                var controlFile = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                Assert.That(controlFile.Content, Does.Contain("return x == y;"));
                Assert.That(controlFile.PreparedGroupingCount, Is.EqualTo(1), "A genuine old-family positive, not a direct-shaped expression inside the new file");
            }
            bool? lateAtLeaf = null;
            int conditions = 0;
            int leaves = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, clock.Condition))
                {
                    conditions++;
                    clock.Phase = "condition-entry";
                    Assert.That(clock.Late.IsValueCreated, Is.False);
                    clock.Armed = true;
                }
                if (ReferenceEquals(node, clock.Leaf))
                {
                    leaves++;
                    clock.Phase = "first-leaf";
                    lateAtLeaf = clock.Late.IsValueCreated;
                    clock.Armed = false;
                }
            };
            clock.Phase = "write-clock";
            var raw = new TypeProviderWriter(clock) { PreparedFieldTypes = prepared }.Write();
            clock.Phase = "selection";
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(withDirect ? 2 : 1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withDirect ? 2 : 1));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(selected.Content, Does.Contain("if (x == y)"));
            Assert.That(conditions, Is.EqualTo(1));
            Assert.That(leaves, Is.EqualTo(1));
            Assert.That(lateAtLeaf, Is.False, "A warmed getter does not prove a different dependency is warm");
            TestContext.WriteLine($"IF-PHASE mode={mode}; oldControl={withDirect}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; conditions={conditions}; leaves={leaves}; lateAtLeaf={lateAtLeaf}; earlyFirst={clock.EarlyFirst}; lateFirst={clock.LateFirst ?? "never"}");
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Clock : TypeProvider
        {
            private readonly string _mode;
            internal readonly Lazy<int> Early;
            internal readonly Lazy<int> Late;
            internal string Phase = "construct";
            internal bool Armed;
            internal string? EarlyFirst;
            internal string? LateFirst;
            internal BinaryOperatorExpression Condition = null!;
            internal VariableExpression Leaf = null!;
            internal Clock(string mode)
            {
                _mode = mode;
                Early = new Lazy<int>(() => { EarlyFirst = Phase + ":" + Stack(); return 0; });
                Late = new Lazy<int>(() => { LateFirst = Phase + ":" + Stack(); return 0; });
            }
            private static string Stack() => string.Join(">", new StackTrace().GetFrames().Take(15).Select(frame => frame.GetMethod()?.Name));
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                if (_mode == "path") { _ = Early.Value; if (Armed) { _ = Late.Value; } }
                return "Clock.cs";
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Leaf = x;
                Condition = new BinaryOperatorExpression("==", x, y);
                var conditional = new IfStatement(Condition);
                conditional.Add(Return(Literal(true)));
                var body = new MethodBodyStatements([conditional, Return(Literal(false))]);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]), body, this)];
            }
        }
        private sealed class SerializationCarrier(Clock owner) : IReadOnlyList<TypeProvider>
        {
            public int Count { get { _ = owner.Early.Value; if (owner.Armed) { _ = owner.Late.Value; } return 0; } }
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
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]), Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
    }
}

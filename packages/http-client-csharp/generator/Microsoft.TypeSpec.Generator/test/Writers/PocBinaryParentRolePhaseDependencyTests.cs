// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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
    internal class PocBinaryParentRolePhaseDependencyTests
    {
        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task ExtraReadsDoNotFirstLoadSecondColdDependency(string mode, bool withDirect)
        {
            var compilation = CSharpCompilation.Create("PhaseDependency", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}", compilation: () => Task.FromResult<Compilation>(compilation));
            var clock = new Clock(mode);
            var control = new Control();
            var output = withDirect ? new TestOutputLibrary(control, clock) : new TestOutputLibrary(clock);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null) { PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory, "phase-dependency", compilation, ["controlled"], () => true) };
            CodeModelGenerator.Instance = generator;
            if (mode == "serialization")
            { clock.Update(serializations: clock.Serializations); }
            var roots = generator.OutputLibrary.TypeProviders;
            clock.Phase = "backcompat";
            foreach (var root in roots)
            { root.ProcessTypeForBackCompatibility(); }
            clock.Phase = "reference-map";
            using var refs = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(clock.Early.IsValueCreated, Is.True, "The original dependency must genuinely be warm before preparation");
            Assert.That(clock.Late.IsValueCreated, Is.False, "The second dependency must genuinely be cold");
            Assert.That(clock.CachedNestedTypes, Is.TypeOf<TypeProvider[]>());
            clock.Phase = "prepare";
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, refs.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            int plans = prepared!.PocGroupingCount;
            if (withDirect)
            { clock.Phase = "write-control"; var f = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write(); Assert.That(f.Content, Does.Contain("return x == y;")); Assert.That(prepared.PocGroupingUses, Is.EqualTo(1)); }
            bool? loadedAtLeaf = null;
            int inner = 0;
            int leaf = 0;
            prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, clock.Inner)) { inner++; clock.Phase = "inner-entry"; Assert.That(clock.Late.IsValueCreated, Is.False); clock.Armed = true; } if (ReferenceEquals(node, clock.Leaf)) { leaf++; clock.Phase = "first-leaf"; loadedAtLeaf = clock.Late.IsValueCreated; clock.Armed = false; } };
            clock.Phase = "write-clock";
            var raw = new TypeProviderWriter(clock) { PreparedFieldTypes = prepared }.Write();
            clock.Phase = "selection";
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            var selected = raw.SelectPreparedForms(true, true, allowed);
            TestContext.WriteLine("PHASE-DEPENDENCY " + JsonSerializer.Serialize(new { mode, withDirect, plans, uses = prepared.PocGroupingUses, inner, leaf, loadedAtLeaf, lateAfter = clock.Late.IsValueCreated, clock.EarlyFirst, clock.LateFirst, allowed, shortForm = selected.Content.Contains("return x + y == z;"), nativeFlags = clock.Outer!.AddParentheses && clock.Inner!.AddParentheses }));
            Assert.That(inner, Is.EqualTo(1));
            Assert.That(leaf, Is.EqualTo(1));
            Assert.That(plans, Is.EqualTo(withDirect ? 3 : 2));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(withDirect ? 3 : 2));
            Assert.That(selected.Content, Does.Contain("return x + y == z;"));
            Assert.That(loadedAtLeaf, Is.False, "A repeated getter must not first materialize a different cold dependency between inner native entry and first leaf");
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context) { public override InputLibrary InputLibrary => input; public override OutputLibrary OutputLibrary => output; }
        private sealed class Clock : TypeProvider
        {
            private readonly string _mode; internal readonly Lazy<int> Early; internal readonly Lazy<int> Late; internal readonly SerializationCarrier Serializations; internal string Phase = "construct"; internal bool Armed; internal string? EarlyFirst, LateFirst; internal BinaryOperatorExpression? Inner, Outer; internal VariableExpression? Leaf;
            internal Clock(string mode) { _mode = mode; Early = new Lazy<int>(() => { EarlyFirst = Phase + ":" + Stack(); return 0; }); Late = new Lazy<int>(() => { LateFirst = Phase + ":" + Stack(); return 0; }); Serializations = new SerializationCarrier(this); }
            internal static string Stack() => string.Join(">", new StackTrace().GetFrames().Take(15).Select(f => f.GetMethod()?.Name));
            protected override string BuildName() => "Clock"; protected override string BuildNamespace() => "Sample"; protected override string BuildRelativeFilePath() { if (_mode == "path") { _ = Early.Value; if (Armed) { _ = Late.Value; } } return "Clock.cs"; }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods() { var x = new ParameterProvider("x", $"", typeof(int)); var y = new ParameterProvider("y", $"", typeof(int)); var z = new ParameterProvider("z", $"", typeof(int)); Leaf = x; Inner = new BinaryOperatorExpression("+", Leaf, y); Outer = new BinaryOperatorExpression("==", Inner, z); return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y, z }), Return(Outer), this)]; }
        }
        private sealed class SerializationCarrier(Clock owner) : IReadOnlyList<TypeProvider>
        {
            public int Count { get { _ = owner.Early.Value; if (owner.Armed) { _ = owner.Late.Value; } return 0; } }
            public TypeProvider this[int index] => throw new IndexOutOfRangeException(); public IEnumerator<TypeProvider> GetEnumerator() => ((IEnumerable<TypeProvider>)Array.Empty<TypeProvider>()).GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class Control : TypeProvider
        {
            protected override string BuildName() => "Control"; protected override string BuildNamespace() => "Sample"; protected override string BuildRelativeFilePath() => "Control.cs"; protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods() { var x = new ParameterProvider("x", $"", typeof(int)); var y = new ParameterProvider("y", $"", typeof(int)); return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y }), Return(new BinaryOperatorExpression("==", x, y)), this)]; }
        }
    }
}

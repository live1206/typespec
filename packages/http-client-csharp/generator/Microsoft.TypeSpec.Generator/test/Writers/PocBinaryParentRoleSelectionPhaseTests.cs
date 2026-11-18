// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
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
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocBinaryParentRoleSelectionPhaseTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NewGroupingDoesNotFirstLoadSelectionDependency(bool pinned)
        {
            const string mode = "second-lazy";
            string directory = Path.Combine(Path.GetTempPath(), "task05-selection-callback", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory, configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}", compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
                var clock = new Clock(mode);
                if (pinned)
                { clock.Update(relativeFilePath: "Clock.cs"); }
                var widget = new Widget();
                var field = new Consumer(widget.Type);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(clock, field, widget));
                CodeModelGenerator.Instance = generator;
                int uses = 0;
                bool decisionSeen = false;
                bool? decision = null;
                var selected = new Dictionary<string, CodeFile>();
                Exception? failure = null;
                var pipeline = new CSharpGen
                {
                    PocRawFileObserved = (prepared, provider, file) => { if (ReferenceEquals(provider, clock)) { uses = prepared!.PocGroupingUses; TestContext.WriteLine($"SELECTION-RAW mode={mode}; pinned={pinned}; plans={prepared.PocGroupingCount}; uses={uses}; short={file.Content.Contains("return x + y == z;")}"); } },
                    PocGroupingDecisionObserved = (file, allowed) => { if (file.Name == "Clock.cs") { decisionSeen = true; decision = allowed; Assert.That(clock.Late.IsValueCreated, Is.False); clock.Armed = true; } },
                    PocSelectedFileObserved = file => { clock.Armed = false; selected.Add(file.Name, file); }
                };
                try
                { await pipeline.ExecuteAsync(); }
                catch (Exception e) { failure = e; }
                finally { clock.Armed = false; }
                TestContext.WriteLine($"SELECTION mode={mode}; pinned={pinned}; uses={uses}; decisionSeen={decisionSeen}; decision={decision}; callbackCalls={clock.Calls}; mutations={clock.Mutations}; failure={failure?.GetType().Name ?? "none"}; selected={selected.Count}; fieldShort={(selected.TryGetValue("Consumer.cs", out var consumer) ? consumer.Content.Contains("private Widget _value;") : false)}");
                Assert.That(decisionSeen, Is.True, "Both versions must reach the same final-selection observation boundary");
                if (pinned && uses > 0)
                { Assert.That(uses, Is.EqualTo(2)); Assert.That(selected["Clock.cs"].Content, Does.Contain("return x + y == z;")); }
                TestContext.WriteLine($"SELECTION-PHASE pinned={pinned}; lateLoaded={clock.Late.IsValueCreated}; first={clock.LateFirst ?? "never"}; uses={uses}; decisionSeen={decisionSeen}; fieldShort={selected["Consumer.cs"].Content.Contains("private Widget _value;")}");
                if (!pinned)
                { Assert.That(clock.Early.IsValueCreated, Is.True); }
                Assert.That(failure, Is.Null);
                Assert.That(uses, Is.EqualTo(2));
                Assert.That(selected["Clock.cs"].Content, Does.Contain("return x + y == z;"));
                Assert.That(clock.Late.IsValueCreated, Is.False, "Added final-selection checks must not first load a second cold dependency after the same decision observer");
                Assert.That(failure, Is.Null);
                Assert.That(selected["Consumer.cs"].Content, Does.Contain("private Widget _value;"), "Both baselines need an actual valid independent field-name positive");
            }
            finally { Directory.Delete(directory, true); }
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input; public override OutputLibrary OutputLibrary => output;
            public override SourceInputModel SourceInputModel { get => base.SourceInputModel; internal set { base.SourceInputModel = value; if (value.Customization is CSharpCompilation c) { value.PreEmissionSourceInputs = new CompleteSourceInputSnapshot(Configuration.OutputDirectory, "selection-callback", c, ["controlled"], () => true); } } }
        }
        private sealed class Clock : TypeProvider
        {
            internal readonly Lazy<int> Early = new(() => 0); internal readonly Lazy<IReadOnlyList<string>> Late; internal string? LateFirst;
            internal Clock(string mode) { Late = new Lazy<IReadOnlyList<string>>(() => { LateFirst = string.Join(">", new StackTrace().GetFrames().Take(15).Select(f => f.GetMethod()?.Name)); return new[] { "deferred-path-metadata" }; }); }
            internal bool Armed; internal int Calls; internal int Mutations = 0; internal VariableExpression? Leaf;
            protected override string BuildName() => "Clock"; protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() { _ = Early.Value; if (Armed) { Calls++; _ = Late.Value; } return "Clock.cs"; }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods() { var x = new ParameterProvider("x", $"", typeof(int)); var y = new ParameterProvider("y", $"", typeof(int)); var z = new ParameterProvider("z", $"", typeof(int)); Leaf = x; var expression = new BinaryOperatorExpression("==", new BinaryOperatorExpression("+", Leaf, y), z); return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y, z }), Return(expression), this)]; }
        }
        private sealed class Widget : TypeProvider
        { protected override string BuildName() => "Widget"; protected override string BuildNamespace() => "Sample"; protected override string BuildRelativeFilePath() => "Widget.cs"; protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class; }
        private sealed class Consumer(CSharpType target) : TypeProvider
        { protected override string BuildName() => "Consumer"; protected override string BuildNamespace() => "Sample"; protected override string BuildRelativeFilePath() => "Consumer.cs"; protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class; protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, target, "_value", this)]; }
    }
}

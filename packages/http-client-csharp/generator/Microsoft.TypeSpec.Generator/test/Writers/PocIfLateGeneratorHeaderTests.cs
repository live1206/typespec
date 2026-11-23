// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocIfLateGeneratorHeaderTests
    {
        // The paired accepted-BASE copy changes ONLY this feature-count oracle.
        private const int ExpectedPreparedUses = 1;

        [TestCase("stable")]
        [TestCase("foreign-safe")]
        [TestCase("foreign-hidden")]
        [TestCase("same-configuration")]
        public async Task TruthfulFinalSourceCallbackCannotLendAnotherGeneratorsHeader(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-late-generator-header", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            var state = new SwapState(mode);
            try
            {
                var originalMock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration("original-safe"),
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("OriginalHeaderInputs")));
                var alternateMock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: Configuration(mode == "foreign-hidden" ? "foreign\u2028#line hidden\u2028" : "alternate-safe"),
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("AlternateHeaderInputs")));
                var clock = new Clock(state);
                var input = originalMock.Object.InputLibrary;
                var output = new TestOutputLibrary(clock);
                state.Original = new NativeGenerator(new GeneratorContext(originalMock.Object.Configuration), input, output);
                state.Alternate = new NativeGenerator(new GeneratorContext(mode == "same-configuration"
                    ? originalMock.Object.Configuration : alternateMock.Object.Configuration), input, output);
                CodeModelGenerator.Instance = state.Original;
                Assert.That(PreparedSourceFile.HasCommentOnlyLicenseHeader(state.Original.LicenseHeader), Is.True);
                Assert.That(PreparedSourceFile.HasCommentOnlyLicenseHeader(state.Alternate.LicenseHeader), Is.EqualTo(mode != "foreign-hidden"));
                var session = new Session(original, source, state);
                CodeFile? raw = null;
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                int uses = -1;
                int visits = 0;
                bool? allowed = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(ExpectedPreparedUses));
                        proof = state.Original.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null, "Real physical source/reference authority is required");
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, clock.Condition)) { visits++; state.Armed = true; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(clock));
                        raw = file;
                        uses = prepared!.PocGroupingUses;
                        Assert.That(uses, Is.EqualTo(ExpectedPreparedUses), "An actual feature use precedes the final source callback");
                        Assert.That(file.PreparedGroupingCount, Is.EqualTo(ExpectedPreparedUses));
                    },
                    PocGroupingDecisionObserved = (_, value) => allowed = value,
                    PocSelectedFileObserved = file => selected = file
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, session);
                Assert.That(raw, Is.Not.Null);
                Assert.That(selected, Is.Not.Null);
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(uses, Is.EqualTo(ExpectedPreparedUses));
                Assert.That(state.Swaps, Is.EqualTo(mode == "stable" ? 0 : 1));
                Assert.That(state.Restores, Is.EqualTo(mode == "stable" ? 0 : 1));
                Assert.That(CodeModelGenerator.Instance, Is.SameAs(state.Original));
                Assert.That(proof!.IsCurrentFor(original), Is.True, "The callback truthfully preserves actual compiler inputs");
                Assert.That(result.UsedCapturedResidualInputs, Is.True);
                Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
                var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
                var conditional = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
                bool hidden = tree.GetLineVisibility(conditional.Condition.SpanStart) == LineVisibility.Hidden;
                Assert.That(hidden, Is.EqualTo(mode == "foreign-hidden"), "Inspect actual written source visibility, not a header surrogate");
                bool expectShort = mode == "stable" && ExpectedPreparedUses > 0;
                TestContext.WriteLine($"LATE-GENERATOR mode={mode}; plansUses={uses}; swaps={state.Swaps}; restores={state.Restores}; allowed={allowed}; selected={conditional.Condition.NormalizeWhitespace()}; hidden={hidden}; capturedResult={result.UsedCapturedResidualInputs}; swapStack={state.SwapStack}; restoreStack={state.RestoreStack}");
                var compilation = CSharpCompilation.Create("LateHeaderExecuted", [tree],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                var physical = Path.Combine(isolated, selected.Name);
                Assert.That(File.Exists(physical), Is.True);
                var final = File.ReadAllText(physical);
                var finalTree = CSharpSyntaxTree.ParseText(final, path: selected.Name);
                var finalCondition = finalTree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
                Assert.That(finalTree.GetLineVisibility(finalCondition.SpanStart) == LineVisibility.Hidden, Is.EqualTo(hidden));
                PreparedParenthesesTests.AssertOperations(raw!.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(selected.Content, final);
                var finalCompilation = CSharpCompilation.Create("LateHeaderFinalExecuted", [finalTree],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(finalCompilation, "Sample.Clock", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                TestContext.WriteLine($"LATE-GENERATOR-PHYSICAL mode={mode}; selectedRuntime=true,false; finalRuntime=true,false; finalCondition={finalCondition.NormalizeWhitespace()}; finalHidden={hidden}; operationsPreserved=true");
                Assert.That(conditional.Condition is ParenthesizedExpressionSyntax, Is.EqualTo(!expectShort),
                    "A truthful callback may not lend another generator/configuration and restore it after header emission");
            }
            finally
            {
                if (state.Original is not null) { CodeModelGenerator.Instance = state.Original; }
                Directory.Delete(sandbox, recursive: true);
            }
        }

        [TestCase("stable")]
        [TestCase("foreign-safe")]
        [TestCase("foreign-hidden")]
        [TestCase("same-configuration")]
        [TestCase("provider-hidden")]
        public async Task MixedLegacySnapshotCallbackCannotLendAnotherGeneratorsHeader(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-late-generator-header-mixed", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            var state = new SwapState(mode, mixedTrailingSnapshot: true);
            bool hiddenHeader = mode is "foreign-hidden" or "provider-hidden";
            try
            {
                var originalMock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration("original-safe"),
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("OriginalMixedHeaderInputs")));
                var alternateMock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: Configuration(hiddenHeader ? "foreign\u2028#line hidden\u2028" : "alternate-safe"),
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("AlternateMixedHeaderInputs")));
                var control = new Control();
                var clock = new Clock(state);
                var input = originalMock.Object.InputLibrary;
                var output = new TestOutputLibrary(control, clock);
                state.Original = new NativeGenerator(new GeneratorContext(originalMock.Object.Configuration), input, output);
                state.Alternate = new NativeGenerator(new GeneratorContext(mode == "same-configuration"
                    ? originalMock.Object.Configuration : alternateMock.Object.Configuration), input, output);
                CodeModelGenerator.Instance = state.Original;
                Assert.That(PreparedSourceFile.HasCommentOnlyLicenseHeader(state.Original.LicenseHeader), Is.True);
                Assert.That(PreparedSourceFile.HasCommentOnlyLicenseHeader(state.Alternate.LicenseHeader), Is.EqualTo(!hiddenHeader));
                var session = new Session(original, source, state);
                CodeFile? controlRaw = null;
                CodeFile? clockRaw = null;
                CodeFile? controlSelected = null;
                CodeFile? clockSelected = null;
                CompleteSourceInputSnapshot? proof = null;
                int usesAtControl = -1;
                int usesAtClock = -1;
                int visits = 0;
                var decisions = new List<bool>();
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1 + ExpectedPreparedUses));
                        proof = state.Original.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null, "Real physical source/reference authority is required");
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, clock.Condition)) { visits++; state.Armed = true; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        if (ReferenceEquals(provider, control))
                        {
                            controlRaw = file;
                            usesAtControl = prepared!.PocGroupingUses;
                            Assert.That(usesAtControl, Is.EqualTo(1), "A genuine old direct-equality use precedes the new file");
                            Assert.That(file.PreparedGroupingCount, Is.EqualTo(1));
                        }
                        else if (ReferenceEquals(provider, clock))
                        {
                            clockRaw = file;
                            usesAtClock = prepared!.PocGroupingUses;
                            Assert.That(usesAtClock, Is.EqualTo(1 + ExpectedPreparedUses));
                            Assert.That(file.PreparedGroupingCount, Is.EqualTo(ExpectedPreparedUses));
                        }
                    },
                    PocGroupingDecisionObserved = (_, value) => decisions.Add(value),
                    PocSelectedFileObserved = file =>
                    {
                        if (file.Name == "Control.cs") { controlSelected = file; }
                        else if (file.Name == "Clock.cs") { clockSelected = file; }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, session);
                Assert.That(controlRaw, Is.Not.Null);
                Assert.That(clockRaw, Is.Not.Null);
                Assert.That(controlSelected, Is.Not.Null);
                Assert.That(clockSelected, Is.Not.Null);
                Assert.That(controlRaw!.Content, Does.Contain("return x == y;"));
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(usesAtControl, Is.EqualTo(1));
                Assert.That(usesAtClock, Is.EqualTo(1 + ExpectedPreparedUses));
                Assert.That(state.NestedCheckCalls, Is.EqualTo(2), "Mixed legacy/new WriteNestedTypes runs two CheckSnapshotCurrent callbacks after ObserveSourceSnapshot");
                Assert.That(state.Swaps, Is.EqualTo(mode == "stable" ? 0 : 1));
                Assert.That(state.Restores, Is.EqualTo(mode == "stable" ? 0 : 1));
                if (mode != "stable")
                {
                    Assert.That(state.SwapStack, Does.Contain("CheckSnapshotCurrent"));
                    Assert.That(state.SwapStack, Does.Not.Contain("ObserveSourceSnapshot"));
                    if (mode == "provider-hidden")
                    {
                        Assert.That(state.SwapStack, Does.Contain("BuildRelativeFilePath"));
                        Assert.That(state.RestoreStack, Does.Not.Contain("CheckSnapshotCurrent"));
                    }
                }
                Assert.That(CodeModelGenerator.Instance, Is.SameAs(state.Original));
                Assert.That(proof!.IsCurrentFor(original), Is.True, "The callback truthfully preserves actual compiler inputs");
                Assert.That(result.UsedCapturedResidualInputs, Is.True);
                Assert.That(clockSelected!.Name, Is.EqualTo("Clock.cs"));
                var tree = CSharpSyntaxTree.ParseText(clockSelected.Content, path: clockSelected.Name);
                var conditional = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
                bool hidden = tree.GetLineVisibility(conditional.Condition.SpanStart) == LineVisibility.Hidden;
                Assert.That(hidden, Is.EqualTo(hiddenHeader), "Inspect actual written source visibility, not a header surrogate");
                var compilation = CSharpCompilation.Create("LateHeaderMixedExecuted",
                    [CSharpSyntaxTree.ParseText(controlSelected!.Content, path: controlSelected.Name), tree],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Control", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                var physical = Path.Combine(isolated, clockSelected.Name);
                var controlPhysical = Path.Combine(isolated, controlSelected.Name);
                Assert.That(File.Exists(physical), Is.True);
                Assert.That(File.Exists(controlPhysical), Is.True);
                var final = File.ReadAllText(physical);
                var controlFinal = File.ReadAllText(controlPhysical);
                var finalTree = CSharpSyntaxTree.ParseText(final, path: clockSelected.Name);
                var finalCondition = finalTree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
                Assert.That(finalTree.GetLineVisibility(finalCondition.SpanStart) == LineVisibility.Hidden, Is.EqualTo(hidden));
                PreparedParenthesesTests.AssertOperations(clockRaw!.Content, clockSelected.Content);
                PreparedParenthesesTests.AssertOperations(clockSelected.Content, final);
                PreparedParenthesesTests.AssertOperations(controlRaw.Content, controlSelected.Content);
                PreparedParenthesesTests.AssertOperations(controlSelected.Content, controlFinal);
                var finalCompilation = CSharpCompilation.Create("LateHeaderMixedFinalExecuted",
                    [CSharpSyntaxTree.ParseText(controlFinal, path: controlSelected.Name), finalTree],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(finalCompilation, "Sample.Control", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                Assert.That(PreparedGeneratedRuntime.Invoke(finalCompilation, "Sample.Clock", "M", [[1, 1], [1, 2]]), Is.EqualTo(new object?[] { true, false }));
                bool expectShort = mode == "stable" && ExpectedPreparedUses > 0;
                TestContext.WriteLine($"LATE-GENERATOR-MIXED mode={mode}; controlUses={usesAtControl}; clockUses={usesAtClock}; nestedChecks={state.NestedCheckCalls}; swaps={state.Swaps}; restores={state.Restores}; decisions={string.Join(",", decisions)}; selected={conditional.Condition.NormalizeWhitespace()}; final={finalCondition.NormalizeWhitespace()}; hidden={hidden}; capturedResult={result.UsedCapturedResidualInputs}; swapStack={state.SwapStack}; restoreStack={state.RestoreStack}");
                Assert.That(conditional.Condition is ParenthesizedExpressionSyntax, Is.EqualTo(!expectShort),
                    "A truthful mixed-legacy trailing callback may not lend another generator/configuration and restore it after header emission");
            }
            finally
            {
                if (state.Original is not null) { CodeModelGenerator.Instance = state.Original; }
                Directory.Delete(sandbox, recursive: true);
            }
        }

        private static string Configuration(string header) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["package-name"] = "Sample", ["enable-pre-emission-simplification"] = true,
            ["disable-xml-docs"] = true, ["unreferenced-types-handling"] = "keepAll",
            ["license"] = new { name = "controlled", company = "controlled", link = "", header, description = "controlled" }
        });
        private sealed class SwapState(string mode, bool mixedTrailingSnapshot = false)
        {
            internal readonly string Mode = mode;
            internal readonly bool MixedTrailingSnapshot = mixedTrailingSnapshot;
            internal NativeGenerator Original = null!;
            internal NativeGenerator Alternate = null!;
            internal bool Armed;
            internal int NestedCheckCalls;
            internal int Swaps;
            internal int Restores;
            internal string? SwapStack;
            internal string? RestoreStack;
            internal bool Check()
            {
                var frames = new StackTrace().GetFrames().Select(frame => frame.GetMethod()?.Name).ToArray();
                if (Armed && frames.Contains("WriteNestedTypes"))
                {
                    if (!MixedTrailingSnapshot && Mode != "stable" && Swaps == 0)
                    {
                        SwapStack = string.Join(">", frames.Take(22));
                        Swaps++;
                        CodeModelGenerator.Instance = Alternate;
                    }
                    else if (MixedTrailingSnapshot && frames.Contains("CheckSnapshotCurrent"))
                    {
                        NestedCheckCalls++;
                        if (Mode is not ("stable" or "provider-hidden") && NestedCheckCalls == 2 && Swaps == 0)
                        {
                            SwapStack = string.Join(">", frames.Take(22));
                            Swaps++;
                            CodeModelGenerator.Instance = Alternate;
                        }
                    }
                }
                return true; // No provider mutation, changed input hash, or emitted extra declaration.
            }
            internal static string Stack() => string.Join(">", new StackTrace().GetFrames().Take(22).Select(frame => frame.GetMethod()?.Name));
        }
        private sealed class Session(string directory, string source, SwapState state) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "late-generator-header", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => state.Check();
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }
        private sealed class Clock(SwapState state) : TypeProvider
        {
            internal BinaryOperatorExpression Condition = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                string? stack = state.Mode == "provider-hidden" && state.Armed ? SwapState.Stack() : null;
                bool inNestedValidation = stack is not null && stack.Contains("CheckSnapshotCurrent", StringComparison.Ordinal) &&
                    stack.Contains("WriteNestedTypes", StringComparison.Ordinal);
                if (inNestedValidation && state.NestedCheckCalls == 1 && state.Swaps == 0)
                {
                    state.SwapStack = stack;
                    state.Swaps++;
                    CodeModelGenerator.Instance = state.Alternate;
                }
                else if (!inNestedValidation && state.Swaps > 0 && state.Restores == 0 && ReferenceEquals(CodeModelGenerator.Instance, state.Alternate))
                {
                    state.RestoreStack = stack ?? SwapState.Stack();
                    state.Restores++;
                    CodeModelGenerator.Instance = state.Original;
                }
                return "Clock.cs";
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Condition = new BinaryOperatorExpression("==", x, y);
                var conditional = new IfStatement(Condition);
                conditional.Add(Return(Literal(true)));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]),
                    new MethodBodyStatements([conditional, Return(Literal(false))]), this)];
            }
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
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]),
                    Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
    }
}

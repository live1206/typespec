// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocIfConditionRoleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalWrappedIfRetainsItsRequiredPair(bool selectOmission)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner("==", true, true, wrapped: true);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => true);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new IfOwner("==", true, true, wrapped: true);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            List<object> visits = [];
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Conditional) || ReferenceEquals(node, owner.WrappedCondition) || ReferenceEquals(node, owner.Condition) ||
                    ReferenceEquals(node, owner.Condition.Left) || ReferenceEquals(node, owner.Condition.Right)) { visits.Add(node); }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = raw.SelectPreparedForms(false, false, false);
            var selected = raw.SelectPreparedForms(true, true, selectOmission && prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(native.Name, Is.EqualTo(qualified.Name));
            Assert.That(native.Name, Is.EqualTo(selected.Name));
            Assert.That(native.Content, Is.EqualTo(qualified.Content));
            if (!selectOmission) { Assert.That(native.Content, Is.EqualTo(selected.Content)); }
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(owner.Condition.AddParentheses, Is.True);
            object[] expectedVisits = [owner.Conditional, owner.WrappedCondition!, owner.Condition, owner.Condition.Left, owner.Condition.Right];
            Assert.That(visits, Has.Count.EqualTo(expectedVisits.Length));
            for (int i = 0; i < expectedVisits.Length; i++) { Assert.That(visits[i], Is.SameAs(expectedVisits[i])); }

            var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var conditional = (IfStatementSyntax)method.Body!.Statements[0];
            Assert.That(conditional.ToString(), Is.EqualTo("if (((x == y))) return true;"));
            Assert.That(conditional.Else, Is.Null);
            Assert.That(method.Body.Statements[1].ToString(), Is.EqualTo("return false;"));
            var equality = conditional.Condition.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
            var model = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            var formals = model.GetDeclaredSymbol(method)!.Parameters;
            var operation = (IBinaryOperation)model.GetOperation(equality)!;
            Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.Equals));
            Assert.That(operation.OperatorMethod, Is.Null);
            Assert.That(operation.LeftOperand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(operation.RightOperand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(equality.Left).Symbol, formals[0]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(equality.Right).Symbol, formals[1]), Is.True);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = [[1, 1], [1, 2], [int.MinValue, int.MaxValue]];
            Assert.That(Execute(native, arguments), Is.EqualTo(new object?[] { true, false, false }));
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
            var selectedIf = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name).GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
            Assert.That(selectedIf.OpenParenToken.IsKind(SyntaxKind.OpenParenToken) && !selectedIf.OpenParenToken.IsMissing, Is.True);
            Assert.That(selectedIf.CloseParenToken.IsKind(SyntaxKind.CloseParenToken) && !selectedIf.CloseParenToken.IsMissing, Is.True);
            Assert.That(selectedIf.ToString(), Is.EqualTo(selectOmission ? "if (x == y) return true;" : conditional.ToString()));
            if (selectOmission)
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(2));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(2));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(2));
            }
        }

        [TestCase("restored")]
        [TestCase("non-inline")]
        [TestCase("noBraces")]
        [TestCase("falseBranch")]
        [TestCase("opaqueStatements")]
        [TestCase("newline")]
        [TestCase("reversed")]
        [TestCase("hidden")]
        public async Task WrappedIfUnsupportedOrRestoredEvidenceRetains(string mode)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: WrappedConfiguration(enabled: true, hidden: mode == "hidden"),
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner("==", true, mode != "non-inline", kind: mode, wrapped: true);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => true);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new IfOwner("==", true, mode != "non-inline", kind: mode, wrapped: true);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            bool restore = mode == "restored";
            if (restore) { Assert.That(prepared, Is.Not.Null); }
            Assert.That(prepared?.PocGroupingCount ?? 0, Is.EqualTo(restore ? 2 : 0));
            int openings = 0;
            if (prepared is not null)
            {
                prepared.PocAfterOpening = () =>
                {
                    openings++;
                    var declaration = ((VariableExpression)owner.Condition.Right).Declaration;
                    string name = declaration.RequestedName;
                    declaration.Update("other");
                    declaration.Update(name);
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(openings, Is.EqualTo(restore ? 1 : 0));
            Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                Assert.That(ActualIf(raw).ToString(), Is.EqualTo("if ((x == y)) return true;"));
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            }
            bool allowed = prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw);
            if (restore) { Assert.That(allowed, Is.False); }
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            if (mode == "hidden")
            {
                var conditional = ActualIf(selected);
                Assert.That(conditional.SyntaxTree.GetLineVisibility(conditional.Condition.SpanStart), Is.EqualTo(LineVisibility.Hidden));
                Assert.That(conditional.ToString(), Is.EqualTo("if (((x == y))) return true;"));
            }
            if (mode == "newline") { Assert.That(selected.Content, Does.Contain("y\n")); }
            object?[][] arguments = [[1, 1], [1, 2]];
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
        }

        [TestCase("stable")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task WrappedIfUsesActualCompilerOutputAndFinalAuthority(string mode)
        {
            var sandbox = Directory.CreateTempSubdirectory("task05-wrapped-if-");
            string source = Path.Combine(sandbox.FullName, "Marker.cs");
            string nativeSource = Path.Combine(sandbox.FullName, "NativeMarker.cs");
            const string input = "namespace Inputs { internal class Marker {} }\n";
            File.WriteAllText(source, input);
            File.WriteAllText(nativeSource, input);
            try
            {
                var actual = await WriteWrappedIfOutput(sandbox.FullName, source, mode);
                var native = await WriteWrappedIfOutput(sandbox.FullName, nativeSource, "native");
                Assert.That(ActualIf(native.Selected).ToString(), Is.EqualTo("if (((x == y))) return true;"));
                Assert.That(ActualIf(actual.Selected).ToString(), Is.EqualTo(mode == "source-expiry" ? "if (((x == y))) return true;" : "if (x == y) return true;"));
                if (mode == "source-expiry") { Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content)); }
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, actual.Selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                foreach (var file in new[] { native.Selected, actual.Selected, native.Final, actual.Final })
                { Assert.That(Execute(file, [[1, 1], [1, 2], [int.MinValue, int.MaxValue]]), Is.EqualTo(new object?[] { true, false, false })); }
            }
            finally { sandbox.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteWrappedIfOutput(string directory, string source, string mode)
        {
            bool enabled = mode != "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, mode + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, mode + "-isolated")).FullName;
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: WrappedConfiguration(enabled),
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            var owner = new IfOwner("==", true, true, wrapped: true);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            int wrapperVisits = 0;
            int binaryVisits = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (!enabled) { Assert.That(prepared, Is.Null); return; }
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
                    Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                    prepared.PocNativeWriteObserver = node =>
                    {
                        if (ReferenceEquals(node, owner.WrappedCondition)) { wrapperVisits++; }
                        if (ReferenceEquals(node, owner.Condition)) { binaryVisits++; }
                    };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(enabled ? 2 : 0));
                    Assert.That(ActualIf(file).ToString(), Is.EqualTo(enabled ? "if (x == y) return true;" : "if (((x == y))) return true;"));
                },
                PocGroupingDecisionObserved = (_, allowed) =>
                {
                    Assert.That(allowed, Is.True);
                    if (mode == "source-expiry") { File.AppendAllText(source, "// expired before selection\n"); }
                },
                PocSelectedFileObserved = file =>
                {
                    selected = file;
                    if (mode == "result-expiry") { File.AppendAllText(source, "// expired after selection\n"); }
                }
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new WrappedIfSession(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"), "False means the caller discards isolated output");
            Assert.That(wrapperVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(binaryVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static IfStatementSyntax ActualIf(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<IfStatementSyntax>().Single();

        private static string WrappedConfiguration(bool enabled, bool hidden = false) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["package-name"] = "Sample", ["enable-pre-emission-simplification"] = enabled,
            ["disable-xml-docs"] = true, ["unreferenced-types-handling"] = "keepAll",
            ["license"] = new { name = "controlled", company = "controlled", link = "", header = hidden ? "foreign\u2028#line hidden\u2028" : "controlled", description = "controlled" }
        });

        // The equality positive maps to original M2
        // StructuralParenthesesNonReassociatingCasesKeepOperations(condition).
        // Inline/layout variants and the two refusal shapes are native-IR bridges.
        [TestCase("==", true, false)]
        [TestCase("==", true, true)]
        [TestCase("!=", true, false)]
        [TestCase("!=", true, true)]
        [TestCase("==", false, false)]
        [TestCase("==", false, true)]
        public async Task NativeIfConditionPreservesRequiredDelimiters(string operation, bool grouping, bool inline)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner(operation, grouping, inline);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-if-condition", compilation, ["controlled-if-condition-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new IfOwner(operation, grouping, inline);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var visits = new List<object>();
            prepared!.PocNativeWriteObserver = visits.Add;
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written);
            var selected = written.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = [[1, 1], [1, 2], [int.MinValue, int.MaxValue]];
            var expectedValues = operation == "==" ? new object?[] { true, false, false } : [false, true, true];
            Assert.That(Execute(native, arguments), Is.EqualTo(expectedValues));
            Assert.That(Execute(selected, arguments), Is.EqualTo(expectedValues));
            int expected = operation == "==" && grouping ? 1 : 0;
            string expectedCondition = $"x {operation} y";
            if (grouping && expected == 0) { expectedCondition = "(" + expectedCondition + ")"; }
            var syntax = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name).GetRoot();
            var conditional = syntax.DescendantNodes().OfType<IfStatementSyntax>().Single();
            TestContext.WriteLine($"IF-ROLE op={operation}; grouping={grouping}; inline={inline}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; physical={written.PreparedGroupingCount}; condition={conditional.Condition.NormalizeWhitespace()}; binaryVisits={visits.OfType<BinaryOperatorExpression>().Count()}; receipt={written.SourceConsumption?.WasCurrentAtEndOfWrite}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expected), "Exact condition-role preparation, not legacy final shortening");
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
                Assert.That(written.PreparedGroupingCount, Is.EqualTo(expected));
                Assert.That(conditional.Condition.NormalizeWhitespace().ToFullString(), Is.EqualTo(expectedCondition));
                Assert.That(conditional.OpenParenToken.IsKind(SyntaxKind.OpenParenToken), Is.True);
                Assert.That(conditional.CloseParenToken.IsKind(SyntaxKind.CloseParenToken), Is.True);
                Assert.That(visits.OfType<BinaryOperatorExpression>().Count(), Is.EqualTo(1), "One native binary visit");
                Assert.That(owner.Condition.AddParentheses, Is.EqualTo(grouping), "Never mutate the shared flag");
                Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            });
        }

        [TestCase("hintAlias", false)]
        [TestCase("hintAlias", true)]
        [TestCase("noBraces", false)]
        [TestCase("noBraces", true)]
        [TestCase("unsupportedBase", false)]
        [TestCase("unsupportedBase", true)]
        [TestCase("unsupportedModifiers", false)]
        [TestCase("unsupportedModifiers", true)]
        [TestCase("unsupportedDefault", false)]
        [TestCase("unsupportedDefault", true)]
        [TestCase("refFormal", false)]
        [TestCase("refFormal", true)]
        [TestCase("nullableFormal", false)]
        [TestCase("nullableFormal", true)]
        [TestCase("longFormal", false)]
        [TestCase("longFormal", true)]
        [TestCase("extraFormal", false)]
        [TestCase("extraFormal", true)]
        [TestCase("swappedReturns", false)]
        [TestCase("swappedReturns", true)]
        [TestCase("falseBranch", false)]
        [TestCase("falseBranch", true)]
        [TestCase("extraBody", false)]
        [TestCase("extraBody", true)]
        [TestCase("extraStatement", false)]
        [TestCase("extraStatement", true)]
        [TestCase("returnExpression", false)]
        [TestCase("returnExpression", true)]
        [TestCase("literalOperand", false)]
        [TestCase("literalOperand", true)]
        [TestCase("derivedStatements", false)]
        [TestCase("derivedStatements", true)]
        [TestCase("opaqueStatements", false)]
        [TestCase("opaqueStatements", true)]
        public async Task IfProfilesRequireNativeOwnedFormalsAndClosedBodies(string kind, bool inline)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner("==", true, inline, kind);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => true);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new IfOwner("==", true, inline, kind);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var visits = new List<object>();
            prepared!.PocNativeWriteObserver = visits.Add;
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            var selected = raw.SelectPreparedForms(true, true, allowed);
            int expected = kind is "hintAlias" or "noBraces" ? 1 : 0;
            Assert.That(prepared.PocGroupingCount, Is.EqualTo(expected));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            if (expected == 0) { Assert.That(selected.Content, Is.EqualTo(native.Content)); }
            var conditional = CSharpSyntaxTree.ParseText(selected.Content).GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
            Assert.That(conditional.Condition is ParenthesizedExpressionSyntax, Is.EqualTo(expected == 0));
            Assert.That(conditional.OpenParenToken.IsKind(SyntaxKind.OpenParenToken) && conditional.CloseParenToken.IsKind(SyntaxKind.CloseParenToken), Is.True);
            Assert.That(visits.OfType<BinaryOperatorExpression>().Count(), Is.EqualTo(kind == "returnExpression" ? 2 : 1));
            Assert.That(owner.Condition.AddParentheses, Is.True);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = kind == "extraFormal" ? [[1, 1, 0], [1, 2, 1], [int.MinValue, int.MaxValue, -1]]
                : kind == "longFormal" ? [[1L, 1L], [1L, 2L], [long.MinValue, long.MaxValue]]
                : [[1, 1], [1, 2], [int.MinValue, int.MaxValue]];
            object?[] values = kind == "falseBranch" ? [false, false, false] : kind == "swappedReturns" ? [false, true, true]
                : kind == "literalOperand" ? [true, true, false] : [true, false, false];
            Assert.That(Execute(native, arguments), Is.EqualTo(values));
            Assert.That(Execute(selected, arguments), Is.EqualTo(values));
            TestContext.WriteLine($"IF-PROFILE kind={kind}; inline={inline}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; condition={conditional.Condition.NormalizeWhitespace()}; binaryVisits={visits.OfType<BinaryOperatorExpression>().Count()}");
        }

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("before", false)]
        [TestCase("before", true)]
        [TestCase("opening", false)]
        [TestCase("opening", true)]
        [TestCase("after", false)]
        [TestCase("after", true)]
        [TestCase("host", false)]
        [TestCase("host", true)]
        [TestCase("restore", false)]
        [TestCase("restore", true)]
        [TestCase("formal", false)]
        [TestCase("formal", true)]
        [TestCase("if-body", false)]
        [TestCase("if-body", true)]
        [TestCase("if-layout", false)]
        [TestCase("if-layout", true)]
        [TestCase("if-condition", false)]
        [TestCase("if-condition", true)]
        public async Task ChangedIfFactsUseTheActualSamePassNativeCompanion(string mode, bool inline)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner("==", true, inline);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            int openings = 0;
            int mutations = 0;
            void Mutate()
            {
                if (mode == "formal") { owner.Parameter.Update(type: typeof(long)); }
                else if (mode == "if-body") { owner.Conditional.Update(body: ReturnBoolean(false)); }
                else if (mode == "if-layout") { owner.Conditional.Update(inline: !inline); }
                else if (mode == "if-condition")
                { owner.Conditional.Update(condition: new BinaryOperatorExpression("!=", owner.Condition.Left, owner.Condition.Right)); }
                else
                {
                    owner.Leaf.Update(type: typeof(long));
                    if (mode == "restore") { owner.Leaf.Update(type: typeof(int)); }
                }
                mutations++;
            }
            generator.SourceInputModel = ControlledInputs(generator, compilation, () =>
            {
                if (mode == "host" && openings > 0 && mutations == 0) { Mutate(); }
                return true;
            });
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            // Body and layout can change before their native writes. Compare to
            // those actual writes, not to an unrelated old body/layout spelling.
            var nativeOwner = new IfOwner("==", true, mode == "if-layout" ? !inline : inline,
                mode == "if-body" ? "falseBranch" : "ordinary");
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
            var visits = new List<object>();
            prepared.PocNativeWriteObserver = visits.Add;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                if (mode is "opening" or "restore" or "formal" or "if-body" or "if-layout" or "if-condition") { Mutate(); }
            };
            if (mode == "before") { Mutate(); }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            if (mode == "after")
            {
                Assert.That(prepared.CanCommitPocGrouping(raw), Is.True, "A real positive precedes late expiry");
                Mutate();
            }
            int expected = mode == "before" ? 0 : 1;
            Assert.That(openings, Is.EqualTo(expected));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            if (expected > 0) { Assert.That(allowed, Is.EqualTo(mode == "stable")); }
            Assert.That(mutations, Is.EqualTo(mode == "stable" ? 0 : 1));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            var conditional = CSharpSyntaxTree.ParseText(selected.Content).GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
            Assert.That(conditional.Condition is ParenthesizedExpressionSyntax, Is.EqualTo(mode != "stable"));
            if (mode != "stable")
            {
                Assert.That(selected.Content, Is.EqualTo(raw.QualifiedContent));
                Assert.That(selected.Content, Is.EqualTo(native.Content));
            }
            Assert.That(visits.OfType<BinaryOperatorExpression>().Count(), Is.EqualTo(1));
            Assert.That(owner.Condition.AddParentheses, Is.True);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            Assert.That(Execute(selected, [[1, 1], [1, 2]]), Is.EqualTo(Execute(native, [[1, 1], [1, 2]])));
            TestContext.WriteLine($"IF-MUTATION mode={mode}; inline={inline}; openings={openings}; uses={prepared.PocGroupingUses}; mutations={mutations}; condition={conditional.Condition.NormalizeWhitespace()}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IfConditionRequiresActualFormalDeclarationCorrespondence(bool inline)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new IfOwner("==", true, inline, "unbound");
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation, () => true);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(((VariableExpression)owner.Condition.Left).Declaration, Is.Not.SameAs(owner.Leaf.Declaration));
            Assert.That(prepared!.PocGroupingCount, Is.Zero);
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            // An unowned native declaration can receive a different emitted name;
            // this is an admission test, not a compilation/binding-parity claim.
        }

        [TestCase("required-delimiters")]
        [TestCase("operator")]
        public void IfOperationOracleRejectsForcedGrammarOrBindingCorruption(string kind)
        {
            const string before = "public class Clock { public bool M(int x, int y) { if ((x == y)) return true; return false; } }";
            string after = kind == "operator"
                ? before.Replace("x == y", "x != y", StringComparison.Ordinal)
                : "public class Clock { public bool M(int x, int y) { if x == y return true; return false; } }";
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(before, after));
        }

        private static SourceInputModel ControlledInputs(NativeGenerator generator, CSharpCompilation compilation, Func<bool> current) => new(compilation, null)
        {
            PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                "task05-if-guards", compilation, ["controlled-if-guard-inputs"], current)
        };

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("Task05IfCondition",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", arguments);

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class WrappedIfSession(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-wrapped-if-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class IfOwner(string operation, bool grouping, bool inline, string kind = "ordinary", string name = "Clock", bool wrapped = false) : TypeProvider
        {
            internal BinaryOperatorExpression Condition { get; private set; } = null!;
            internal ParenthesizedExpression? WrappedCondition { get; private set; }
            internal IfStatement Conditional { get; private set; } = null!;
            internal VariableExpression Leaf { get; private set; } = null!;
            internal ParameterProvider Parameter { get; private set; } = null!;
            internal MethodProvider Method { get; private set; } = null!;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => name + ".cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                CSharpType parameterType = kind == "longFormal" ? typeof(long) : kind == "nullableFormal" ? typeof(int?) : typeof(int);
                Parameter = new ParameterProvider("x", $"", parameterType, isRef: kind == "refFormal")
                {
                    HasUnsupportedParameterModifiers = kind == "unsupportedModifiers",
                    HasUnsupportedDefaultValue = kind == "unsupportedDefault"
                };
                var y = new ParameterProvider("y", $"", parameterType);
                Leaf = Parameter;
                if (kind == "hintAlias") { Leaf.Update(type: typeof(bool)); }
                ValueExpression left = kind == "unbound" ? new VariableExpression(typeof(int), "x") : Leaf;
                ValueExpression right = kind == "literalOperand" ? new LiteralExpression(1) : y;
                if (kind == "newline") { ((VariableExpression)y).Declaration.Update("y\n"); }
                if (kind == "reversed") { (left, right) = (right, left); }
                Condition = new BinaryOperatorExpression(operation, left, right, grouping);
                if (wrapped) { WrappedCondition = new ParenthesizedExpression(Condition); }
                Conditional = new IfStatement((ValueExpression?)WrappedCondition ?? Condition, inline: inline, addBraces: kind != "noBraces");
                if (kind == "extraBody") { Conditional.Add(new SingleLineCommentStatement("native body comment")); }
                Conditional.Add(kind == "returnExpression" ? new ExpressionStatement(new KeywordExpression("return", Condition)) : ReturnBoolean(kind is not "falseBranch" and not "swappedReturns"));
                MethodBodyStatement[] statements = kind == "extraStatement"
                    ? [Conditional, new SingleLineCommentStatement("native outer comment"), ReturnBoolean(false)]
                    : [Conditional, ReturnBoolean(kind == "swappedReturns")];
                MethodBodyStatement body = kind switch
                {
                    "derivedStatements" => new DerivedStatements(statements),
                    "opaqueStatements" => new MethodBodyStatements(new OpaqueStatements(statements)),
                    _ => new MethodBodyStatements(statements)
                };
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    kind == "extraFormal" ? [Parameter, y, new ParameterProvider("other", $"", typeof(int))] : [Parameter, y])
                {
                    HasUnsupportedBaseContract = kind == "unsupportedBase"
                };
                Method = new MethodProvider(signature, body, this);
                return [Method];
            }
        }

        private static ExpressionStatement ReturnBoolean(bool value) => new(new KeywordExpression("return", new LiteralExpression(value)));
        private sealed class DerivedStatements(IReadOnlyList<MethodBodyStatement> statements) : MethodBodyStatements(statements) { }
        private sealed class OpaqueStatements(MethodBodyStatement[] statements) : IReadOnlyList<MethodBodyStatement>
        {
            public int Count => statements.Length;
            public MethodBodyStatement this[int index] => statements[index];
            public IEnumerator<MethodBodyStatement> GetEnumerator() => ((IEnumerable<MethodBodyStatement>)statements).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

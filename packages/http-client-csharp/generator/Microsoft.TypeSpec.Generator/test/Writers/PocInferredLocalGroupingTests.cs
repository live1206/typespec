// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocInferredLocalGroupingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OriginalInferredLocalDistinguishesContextualVarFromAnActualAlias(bool alias)
        {
            const string original = "namespace Sample { public class Clock { public int M(int x, int y) { var value = ((x + y)); return value; } } }";
            const string bare = "namespace Sample { public class Clock { public int M(int x, int y) { var value = x + y; return value; } } }";
            var baseline = CSharpSyntaxTree.ParseText(original, path: "Clock.cs");
            var selected = CSharpSyntaxTree.ParseText(bare, path: baseline.FilePath);
            var model = Compilation();
            if (alias)
            {
                model = model.AddSyntaxTrees(CSharpSyntaxTree.ParseText("global using var = global::System.Int64;", path: "GlobalAliases.cs"));
            }
            var baselineContext = model.AddSyntaxTrees(baseline);
            var selectedContext = model.AddSyntaxTrees(selected);
            Assert.That(baseline.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single().ToString(), Is.EqualTo("var value = ((x + y));"));
            Assert.That(selected.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single().ToString(), Is.EqualTo("var value = x + y;"));
            AssertActualLocalBinding(baselineContext, baseline, alias);
            AssertActualLocalBinding(selectedContext, selected, alias);
            if (!alias)
            {
                PreparedParenthesesTests.AssertOperations(original, bare);
                Assert.That(Execute(baselineContext), Is.EqualTo(new object?[] { 7, int.MinValue, int.MaxValue }));
                Assert.That(Execute(selectedContext), Is.EqualTo(Execute(baselineContext)));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeInferredLocalWritesTheExactStatementAndKeepsItsActualDeclaration(bool alias)
        {
            var compilation = Compilation();
            if (alias)
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("global using var = global::System.Int64;", path: "GlobalAliases.cs"));
            }
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-inferred-local", compilation, ["inferred-local"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock();
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            List<object> visits = [];
            if (alias)
            {
                Assert.That(prepared, Is.Null, "Existing input policy refuses global using directives");
            }
            else
            {
                Assert.That(prepared, Is.Not.Null);
                prepared!.PocNativeWriteObserver = node =>
                {
                    if (ReferenceEquals(node, owner.Declaration) || ReferenceEquals(node, owner.Declaration.Variable) ||
                        ReferenceEquals(node, owner.Wrapper) || ReferenceEquals(node, owner.Binary) ||
                        ReferenceEquals(node, owner.Binary.Left) || ReferenceEquals(node, owner.Binary.Right)) { visits.Add(node); }
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
            Assert.That(raw.PreparedGroupingCount, Is.Zero);
            if (!alias) { Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True); }
            Assert.That(native.Name, Is.EqualTo(selected.Name));
            Assert.That(native.Name, Is.EqualTo(qualified.Name));
            Assert.That(native.Content, Is.EqualTo(selected.Content));
            Assert.That(native.Content, Is.EqualTo(qualified.Content));
            object[] expected = [owner.Declaration, owner.Declaration.Variable, owner.Wrapper, owner.Binary, owner.Binary.Left, owner.Binary.Right, owner.Declaration.Variable];
            if (!alias)
            {
                Assert.That(visits, Has.Count.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++) { Assert.That(visits[i], Is.SameAs(expected[i])); }
            }
            Assert.That(owner.Declaration.Variable.Type.FrameworkType, Is.EqualTo(typeof(int)), "The carrier is not a fabricated var type");
            var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            Assert.That(method.Body!.Statements.Select(statement => statement.ToString()), Is.EqualTo(new[] { "var value = ((x + y));", "return value;" }));
            var emitted = compilation.AddSyntaxTrees(tree);
            AssertActualLocalBinding(emitted, tree, alias);
            if (!alias) { Assert.That(Execute(emitted), Is.EqualTo(new object?[] { 7, int.MinValue, int.MaxValue })); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeInferredLocalHasAnActualCoreOnlyCompilerInputCase(bool enableOmission)
        {
            var directory = Directory.CreateTempSubdirectory("task05-inferred-local-compiler-");
            try
            {
                var original = Directory.CreateDirectory(Path.Combine(directory.FullName, "original"));
                var isolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "isolated"));
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original.FullName,
                    // Keep the actual compiler input proof active for both cases;
                    // the disabled control selects the qualified view below.
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(Compilation()));
                var owner = new Clock();
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CodeFile? qualified = null;
                int plans = -1;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        plans = prepared!.PocGroupingCount;
                        var proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        Assert.That(proof!.Compilation.SyntaxTrees, Is.Empty);
                        var compilation = (CSharpCompilation)proof.Compilation;
                        var reference = compilation.References.Single();
                        Assert.That(reference.Properties.Aliases.IsDefaultOrEmpty, Is.True);
                        var core = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
                        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
                        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
                        Assert.That(SymbolEqualityComparer.Default.Equals(core, objectType.ContainingAssembly), Is.True);
                        Assert.That(SymbolEqualityComparer.Default.Equals(core, intType.ContainingAssembly), Is.True);
                        Assert.That(core.Modules, Has.Exactly(1).Items);
                        Assert.That(core.GetForwardedTypes(), Is.Empty);
                        Assert.That(core.GlobalNamespace.GetMembers("Sample"), Is.Empty);
                        Assert.That(core.GlobalNamespace.GetMembers("var"), Is.Empty);
                        Assert.That(objectType.GetMembers("var"), Is.Empty);
                    },
                    PocRawFileObserved = (prepared, _, file) =>
                    {
                        qualified = file.SelectPreparedForms(false, false, false);
                        TestContext.WriteLine($"VAR-WRITE plans={plans}; uses={prepared?.PocGroupingUses}; physical={file.PreparedGroupingCount}; receipt={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                    },
                    PocGroupingDecisionObserved = (_, allowed) => TestContext.WriteLine($"VAR-DECISION allowed={allowed}"),
                    PocSelectedFileObserved = file => selected = file
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated.FullName, new CoreOnlySession(original.FullName));
                Assert.That(result.UsedCapturedResidualInputs, Is.True);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
                Assert.That(qualified, Is.Not.Null);
                Assert.That(qualified!.Name, Is.EqualTo(selected.Name));
                var retainedTree = CSharpSyntaxTree.ParseText(qualified.Content, path: qualified.Name);
                Assert.That(retainedTree.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single().ToString(),
                    Is.EqualTo("var value = ((x + y));"));
                var selectedTree = enableOmission ? CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name) : retainedTree;
                Assert.That(selectedTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>(), Is.Empty);
                var parsed = Compilation().AddSyntaxTrees(selectedTree);
                AssertActualLocalBinding(parsed, selectedTree, alias: false);
                Assert.That(Execute(parsed), Is.EqualTo(new object?[] { 7, int.MinValue, int.MaxValue }));
                var final = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(isolated.FullName, selected.Name)), path: selected.Name);
                var finalCompilation = Compilation().AddSyntaxTrees(final);
                AssertActualLocalBinding(finalCompilation, final, alias: false);
                Assert.That(Execute(finalCompilation), Is.EqualTo(Execute(parsed)));
                Assert.That(selectedTree.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single().ToString(),
                    Is.EqualTo(enableOmission ? "var value = x + y;" : "var value = ((x + y));"));
                if (enableOmission)
                {
                    Assert.That(plans, Is.EqualTo(2));
                    var nativeFinal = await WriteIndependentNativeFinal(directory.FullName, selected.Name);
                    var nativeCompilation = Compilation().AddSyntaxTrees(nativeFinal);
                    AssertActualLocalBinding(nativeCompilation, nativeFinal, alias: false);
                    PreparedParenthesesTests.AssertOperations(nativeFinal.ToString(), final.ToString());
                    Assert.That(Execute(finalCompilation), Is.EqualTo(Execute(nativeCompilation)));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<SyntaxTree> WriteIndependentNativeFinal(string directory, string expectedName, bool widening = false)
        {
            var original = Directory.CreateDirectory(Path.Combine(directory, "native-original"));
            var isolated = Directory.CreateDirectory(Path.Combine(directory, "native-isolated"));
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original.FullName,
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":false,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            TypeProvider owner = widening ? new WideningClock() : new Clock();
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeFile? selected = null;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared => Assert.That(prepared, Is.Null),
                PocSelectedFileObserved = file => selected = file
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated.FullName, new CoreOnlySession(original.FullName));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "This disabled-policy run supplies a native comparison, not captured-input authority");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo(expectedName));
            var retained = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            Assert.That(retained.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single().ToString(),
                Is.EqualTo(widening ? "var result = ((long)value);" : "var value = ((x + y));"));
            return CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(isolated.FullName, selected.Name)), path: selected.Name);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalInferredWideningCastKeepsRequiredTypeDelimiters(bool selectOmission)
        {
            var directory = Directory.CreateTempSubdirectory("task05-inferred-widening-");
            try
            {
                string original = Directory.CreateDirectory(Path.Combine(directory.FullName, "original")).FullName;
                string isolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "isolated")).FullName;
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(Compilation()));
                var owner = new WideningClock();
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CodeFile? written = null;
                int plans = -1;
                int uses = -1;
                List<object> visits = [];
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        plans = prepared!.PocGroupingCount;
                        var proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        var compilation = (CSharpCompilation)proof!.Compilation;
                        Assert.That(compilation.SyntaxTrees, Is.Empty);
                        var core = compilation.GetAssemblyOrModuleSymbol(compilation.References.Single());
                        Assert.That(core, Is.Not.Null);
                        Assert.That(SymbolEqualityComparer.Default.Equals(core, compilation.GetSpecialType(SpecialType.System_Int64).ContainingAssembly), Is.True);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Declaration) || ReferenceEquals(node, owner.Declaration?.Variable) ||
                                ReferenceEquals(node, owner.Cast) || ReferenceEquals(node, owner.Cast?.Inner)) { visits.Add(node); }
                        };
                    },
                    PocRawFileObserved = (prepared, _, file) => { written = file; uses = prepared!.PocGroupingUses; },
                    PocSelectedFileObserved = file => selected = file
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new CoreOnlySession(original));
                Assert.That(result.UsedCapturedResidualInputs, Is.True);
                Assert.That(written?.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(selected?.Name, Is.EqualTo("Clock.cs"));
                var qualified = written!.SelectPreparedForms(false, false, false);
                object[] order = [owner.Declaration!, owner.Declaration!.Variable, owner.Cast!, owner.Cast!.Inner, owner.Declaration.Variable];
                Assert.That(visits, Has.Count.EqualTo(order.Length));
                for (int i = 0; i < order.Length; i++) { Assert.That(visits[i], Is.SameAs(order[i])); }
                const string retained = "var result = ((long)value);";
                const string shortened = "var result = (long)value;";
                Assert.That(AssertWideningBinding(qualified), Is.EqualTo(retained));
                var output = selectOmission ? selected! : qualified;
                PreparedParenthesesTests.AssertOperations(qualified.Content, output.Content);
                Assert.That(AssertWideningBinding(output), Is.EqualTo(selectOmission ? shortened : retained));
                if (selectOmission)
                {
                    Assert.That(plans, Is.EqualTo(1));
                    Assert.That(uses, Is.EqualTo(1));
                    Assert.That(written.PreparedGroupingCount, Is.EqualTo(1));
                    var nativeFinal = await WriteIndependentNativeFinal(directory.FullName, selected!.Name, widening: true);
                    var actualFinal = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(isolated, selected.Name)), path: selected.Name);
                    PreparedParenthesesTests.AssertOperations(nativeFinal.ToString(), actualFinal.ToString());
                    AssertWideningBinding(new CodeFile(nativeFinal.ToString(), nativeFinal.FilePath));
                    AssertWideningBinding(new CodeFile(actualFinal.ToString(), actualFinal.FilePath));
                }
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("source-type")]
        [TestCase("extra-reference")]
        [TestCase("different-local-carrier")]
        [TestCase("restored")]
        [TestCase("before-selection")]
        [TestCase("after-selection")]
        public async Task InferredWideningCastHonorsClosedInputsAndFallback(string mode)
        {
            var directory = Directory.CreateTempSubdirectory("task05-inferred-widening-veto-");
            try
            {
                string original = Directory.CreateDirectory(Path.Combine(directory.FullName, "original")).FullName;
                string isolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "isolated")).FullName;
                bool competing = mode == "source-type";
                bool extraReference = mode == "extra-reference";
                bool expired = mode is "before-selection" or "after-selection";
                string? source = competing ? Path.Combine(directory.FullName, "CompetingType.cs") : null;
                const string competingSource = "namespace Sample; public struct var { public static implicit operator var(long value) => default; public static implicit operator long(var value) => 23L; }";
                if (source is not null) { File.WriteAllText(source, competingSource); }
                string core = typeof(object).Assembly.Location;
                if (expired)
                {
                    string copy = Path.Combine(directory.FullName, Path.GetFileName(core));
                    File.Copy(core, copy);
                    core = copy;
                }
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(Compilation()));
                var owner = new WideningClock(mode == "different-local-carrier");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CodeFile? raw = null;
                int uses = -1;
                int expectedPlans = competing || extraReference || mode == "different-local-carrier" ? 0 : 1;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(expectedPlans));
                        if (mode == "restored")
                        {
                            prepared.PocAfterOpening = () =>
                            {
                                var declaration = ((VariableExpression)owner.Cast!.Inner).Declaration;
                                string name = declaration.RequestedName;
                                declaration.Update("other");
                                declaration.Update(name);
                            };
                        }
                    },
                    PocRawFileObserved = (prepared, _, file) => { raw = file; uses = prepared!.PocGroupingUses; },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        if (mode == "before-selection")
                        {
                            Assert.That(allowed, Is.True);
                            using var append = File.Open(core, FileMode.Append);
                            append.WriteByte(0);
                        }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "after-selection")
                        {
                            using var append = File.Open(core, FileMode.Append);
                            append.WriteByte(0);
                        }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new CoreOnlySession(original, core, source, extraReference));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(!expired));
                Assert.That(uses, Is.EqualTo(expectedPlans));
                Assert.That(raw, Is.Not.Null);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
                if (mode == "restored") { Assert.That(raw!.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False); }
                if (mode != "after-selection") { Assert.That(selected.Content, Is.EqualTo(raw!.SelectPreparedForms(false, false, false).Content)); }
                string expected = mode == "after-selection" ? "var result = (long)value;" : "var result = ((long)value);";
                if (competing)
                {
                    var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
                    var local = tree.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
                    Assert.That(local.ToString(), Is.EqualTo(expected));
                    var compilation = Compilation().AddSyntaxTrees(tree, CSharpSyntaxTree.ParseText(competingSource, path: source!));
                    var symbol = (ILocalSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(local.Declaration.Variables.Single())!;
                    Assert.That(symbol.Type.ToDisplayString(), Is.EqualTo("Sample.var"));
                    Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [int.MinValue], [int.MaxValue]), Is.EqualTo(new object?[] { 23L, 23L }));
                }
                else { Assert.That(AssertWideningBinding(selected), Is.EqualTo(expected)); }
                Assert.That(File.Exists(Path.Combine(isolated, selected.Name)), Is.True);
            }
            finally { directory.Delete(recursive: true); }
        }

        private static string AssertWideningBinding(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var statement = (LocalDeclarationStatementSyntax)method.Body!.Statements[0];
            var local = (ILocalSymbol)model.GetDeclaredSymbol(statement.Declaration.Variables.Single())!;
            var cast = statement.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var conversion = (IConversionOperation)model.GetOperation(cast)!;
            Assert.That(conversion.Conversion.IsNumeric && !conversion.Conversion.IsUserDefined, Is.True);
            Assert.That(conversion.Operand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(conversion.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int64));
            Assert.That(model.GetAliasInfo((IdentifierNameSyntax)statement.Declaration.Type), Is.Null);
            Assert.That(local.Type.SpecialType, Is.EqualTo(SpecialType.System_Int64));
            var returned = (ReturnStatementSyntax)method.Body.Statements[1];
            Assert.That(returned.ToString(), Is.EqualTo("return result;"));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(returned.Expression!).Symbol, local), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(cast.Expression).Symbol, model.GetDeclaredSymbol(method)!.Parameters.Single()), Is.True);
            Assert.That(cast.Type.ToString(), Is.EqualTo("long"));
            Assert.That(cast.OpenParenToken.IsMissing || cast.CloseParenToken.IsMissing, Is.False);
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [int.MinValue], [17], [int.MaxValue]),
                Is.EqualTo(new object?[] { (long)int.MinValue, 17L, (long)int.MaxValue }));
            return statement.ToString();
        }

        [TestCase("source-type")]
        [TestCase("second-reference")]
        [TestCase("restored")]
        [TestCase("import-veto")]
        [TestCase("before-selection")]
        [TestCase("after-selection")]
        public async Task InferredLocalRetainsOrDiscardsWhenItsEvidenceIsUnavailable(string mode)
        {
            var directory = Directory.CreateTempSubdirectory("task05-inferred-local-guards-");
            try
            {
                string original = Directory.CreateDirectory(Path.Combine(directory.FullName, "original")).FullName;
                string isolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "isolated")).FullName;
                bool competing = mode == "source-type";
                bool extraReference = mode == "second-reference";
                bool expired = mode is "before-selection" or "after-selection";
                string? source = competing ? Path.Combine(directory.FullName, "CompetingType.cs") : null;
                const string competingSource = "namespace Sample; public struct var { public static implicit operator var(int value) => default; public static implicit operator int(var value) => 23; }";
                if (source is not null) { File.WriteAllText(source, competingSource); }
                string core = typeof(object).Assembly.Location;
                if (expired)
                {
                    string copy = Path.Combine(directory.FullName, Path.GetFileName(core));
                    File.Copy(core, copy);
                    core = copy;
                }
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(Compilation()));
                var owner = new Clock();
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                CodeFile? selected = null;
                CodeFile? raw = null;
                int uses = -1;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(competing || extraReference ? 0 : 2));
                        if (mode == "restored")
                        {
                            prepared.PocAfterOpening = () =>
                            {
                                var declaration = ((VariableExpression)owner.Binary.Right).Declaration;
                                string name = declaration.RequestedName;
                                declaration.Update("other");
                                declaration.Update(name);
                            };
                        }
                    },
                    PocRawFileObserved = (prepared, _, file) =>
                    {
                        raw = file;
                        uses = prepared!.PocGroupingUses;
                        if (mode == "import-veto")
                        {
                            // Isolate the fail-closed observation path; the exact
                            // admitted producer itself cannot author an import.
                            prepared.ObserveNativePocImports(prepared.GetSourceFile(owner));
                        }
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        if (mode == "before-selection")
                        {
                            Assert.That(allowed, Is.True);
                            using var append = File.Open(core, FileMode.Append);
                            append.WriteByte(0);
                        }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "after-selection")
                        {
                            using var append = File.Open(core, FileMode.Append);
                            append.WriteByte(0);
                        }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new CoreOnlySession(original, core, source, extraReference));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(!expired));
                Assert.That(uses, Is.EqualTo(competing || extraReference ? 0 : mode == "restored" ? 1 : 2));
                Assert.That(raw, Is.Not.Null);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
                if (mode == "restored") { Assert.That(raw!.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False); }
                if (mode != "after-selection") { Assert.That(selected.Content, Is.EqualTo(raw!.SelectPreparedForms(false, false, false).Content)); }
                var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
                var local = tree.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(local.ToString(), Is.EqualTo(mode == "after-selection" ? "var value = x + y;" : "var value = ((x + y));"));
                var compilation = Compilation().AddSyntaxTrees(tree);
                if (competing)
                {
                    compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(competingSource, path: source!));
                    var symbol = (ILocalSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(local.Declaration.Variables.Single())!;
                    Assert.That(symbol.Type.ToDisplayString(), Is.EqualTo("Sample.var"));
                    Assert.That(Execute(compilation), Is.EqualTo(new object?[] { 23, 23, 23 }));
                }
                else
                {
                    AssertActualLocalBinding(compilation, tree, alias: false);
                    Assert.That(Execute(compilation), Is.EqualTo(new object?[] { 7, int.MinValue, int.MaxValue }));
                }
                Assert.That(File.Exists(Path.Combine(isolated, selected.Name)), Is.True);
            }
            finally { directory.Delete(recursive: true); }
        }

        private static void AssertActualLocalBinding(CSharpCompilation compilation, SyntaxTree tree, bool alias)
        {
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var statement = method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
            var syntax = statement.Declaration.Variables.Single();
            var local = (ILocalSymbol)model.GetDeclaredSymbol(syntax)!;
            var actualAlias = model.GetAliasInfo((IdentifierNameSyntax)statement.Declaration.Type);
            Assert.That(local.Type.SpecialType, Is.EqualTo(alias ? SpecialType.System_Int64 : SpecialType.System_Int32));
            if (alias)
            {
                Assert.That((actualAlias?.Target as ITypeSymbol)?.SpecialType, Is.EqualTo(SpecialType.System_Int64));
                Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id),
                    Is.EqualTo(new[] { "CS0266" }));
            }
            else
            {
                Assert.That(actualAlias, Is.Null);
                Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            }
            var returned = method.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(returned.Expression!).Symbol, local), Is.True);
            var binary = syntax.Initializer!.Value.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Single();
            var bound = (IBinaryOperation)model.GetOperation(binary)!;
            Assert.That(bound.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
            Assert.That(bound.OperatorMethod, Is.Null);
            Assert.That(bound.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            var formals = model.GetDeclaredSymbol(method)!.Parameters;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Left).Symbol, formals[0]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Right).Symbol, formals[1]), Is.True);
        }

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("OriginalInferredLocal",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CSharpCompilation compilation) => PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [3, 4], [int.MaxValue, 1], [int.MinValue, -1]);

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class CoreOnlySession : ITrustedCompilerBuildSession
        {
            public CoreOnlySession(string directory, string? core = null, string? source = null, bool extraReference = false)
            {
                core ??= typeof(object).Assembly.Location;
                static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                var first = new CapturedCompilerReference(core, Hash(core), [], false);
                string extra = typeof(Enumerable).Assembly.Location;
                Capture = new CompilerBuildInputCapture(directory, "inferred-local-core-only", "Debug|net10.0", "12.0", "Actual", [],
                    source is null ? [] : [new CapturedCompilerSource(source, Hash(source))],
                    extraReference ? [first, new CapturedCompilerReference(extra, Hash(extra), [], false)] : [first], false, false,
                    new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            }
            public CompilerBuildInputCapture Capture { get; }
            public bool IsCurrent() => true;
        }

        private sealed class WideningClock(bool differentLocalCarrier = false) : TypeProvider
        {
            internal InferredLocalDeclarationExpression? Declaration { get; private set; }
            internal CastExpression? Cast { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var value = new ParameterProvider("value", $"", typeof(int));
                var local = new VariableExpression(typeof(long), "result");
                Declaration = new InferredLocalDeclarationExpression(local);
                Cast = new CastExpression(value, typeof(long));
                var returned = differentLocalCarrier ? new VariableExpression(typeof(long), local.Declaration) : local;
                var body = new MethodBodyStatements([new AssignmentExpression(Declaration, Cast).Terminate(), Return(returned)]);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(long), null, [value]);
                return [new MethodProvider(signature, body, this)];
            }
        }

        private sealed class Clock : TypeProvider
        {
            internal InferredLocalDeclarationExpression Declaration { get; private set; } = null!;
            internal ParenthesizedExpression Wrapper { get; private set; } = null!;
            internal BinaryOperatorExpression Binary { get; private set; } = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var local = new VariableExpression(typeof(int), "value");
                Declaration = new InferredLocalDeclarationExpression(local);
                Binary = new BinaryOperatorExpression("+", x, y);
                Wrapper = new ParenthesizedExpression(Binary);
                var body = new MethodBodyStatements([new AssignmentExpression(Declaration, Wrapper).Terminate(), Return(local)]);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [x, y]);
                return [new MethodProvider(signature, body, this)];
            }
        }
    }
}

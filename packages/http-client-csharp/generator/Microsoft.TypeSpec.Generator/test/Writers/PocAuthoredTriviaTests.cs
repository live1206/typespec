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
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocAuthoredTriviaTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
        [TestCase("\n")]
        [TestCase("\r\n")]
        public void OriginalBinaryReturnTriviaSurvivesOnlyRemovingItsPairs(string ending)
        {
            string original = "return ( /* before */ (" + ending + "x + y" + ending + ") /* after */ );";
            string expected = "return  /* before */ " + ending + "x + y" + ending + " /* after */ ;";
            string source = Source(original);
            string shortened = Source(expected);
            var before = CSharpSyntaxTree.ParseText(source);
            var after = CSharpSyntaxTree.ParseText(shortened);
            var originalReturn = before.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            var outer = (ParenthesizedExpressionSyntax)originalReturn.Expression!;
            var inner = (ParenthesizedExpressionSyntax)outer.Expression;
            Assert.That(inner.Expression, Is.TypeOf<BinaryExpressionSyntax>());
            Assert.That(outer.OpenParenToken.TrailingTrivia.ToFullString(), Is.EqualTo(" /* before */ "));
            Assert.That(inner.CloseParenToken.TrailingTrivia.ToFullString(), Is.EqualTo(" /* after */ "));
            int[] delimiters = [outer.OpenParenToken.SpanStart, outer.CloseParenToken.SpanStart,
                inner.OpenParenToken.SpanStart, inner.CloseParenToken.SpanStart];
            string withoutPairs = new(source.Where((_, position) => !delimiters.Contains(position)).ToArray());
            Assert.That(withoutPairs, Is.EqualTo(shortened), "Only the four parser-identified delimiter bytes differ");
            var selectedReturn = after.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            Assert.That(selectedReturn.Expression, Is.TypeOf<BinaryExpressionSyntax>());
            Assert.That(Comments(after), Is.EqualTo(Comments(before)));
            Assert.That(Endings(after), Is.EqualTo(new[] { ending, ending }));
            Assert.That(Endings(after), Is.EqualTo(Endings(before)));
            PreparedParenthesesTests.AssertOperations(source, shortened);
            Assert.That(Execute(before), Is.EqualTo(new object?[] { 7, int.MaxValue, int.MinValue }));
            Assert.That(Execute(after), Is.EqualTo(Execute(before)));
        }

        [TestCase("\n", false)]
        [TestCase("\r\n", false)]
        [TestCase("\n", true)]
        [TestCase("\r\n", true)]
        public async Task NativeBinaryTriviaEmitsTheExactAuthoredBoundaries(string ending, bool selectOmission)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(ending == "\r\n");
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-authored-binary-trivia", compilation, ["authored-binary-trivia"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(ending == "\r\n");
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            List<object> visits = [];
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Outer) || ReferenceEquals(node, owner.Outer.Operand) ||
                    ReferenceEquals(node, owner.Binary) || ReferenceEquals(node, owner.Binary.Left) || ReferenceEquals(node, owner.Binary.Right))
                { visits.Add(node); }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = raw.SelectPreparedForms(false, false, false);
            var selected = raw.SelectPreparedForms(true, true,
                selectOmission && prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Content, Is.EqualTo(native.Content));
            if (!selectOmission) { Assert.That(selected.Content, Is.EqualTo(native.Content)); }
            Assert.That(visits, Has.Count.EqualTo(5));
            object[] expectedVisits = [owner.Outer, owner.Outer.Operand, owner.Binary, owner.Binary.Left, owner.Binary.Right];
            for (int i = 0; i < expectedVisits.Length; i++) { Assert.That(visits[i], Is.SameAs(expectedVisits[i])); }
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);

            var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var returned = method.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
            Assert.That(returned.ToString(), Is.EqualTo("return ( /* before */ (" + ending + "x + y" + ending + ") /* after */ );"));
            var outer = (ParenthesizedExpressionSyntax)returned.Expression!;
            var inner = (ParenthesizedExpressionSyntax)outer.Expression;
            var binary = (BinaryExpressionSyntax)inner.Expression;
            var model = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            var methodSymbol = model.GetDeclaredSymbol(method)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Left).Symbol, methodSymbol.Parameters[0]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Right).Symbol, methodSymbol.Parameters[1]), Is.True);
            Assert.That(model.GetTypeInfo(binary).Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(Execute(tree), Is.EqualTo(new object?[] { 7, int.MaxValue, int.MinValue }));
            var selectedTree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            Assert.That(Comments(selectedTree), Is.EqualTo(Comments(tree)));
            Assert.That(Endings(selectedTree), Is.EqualTo(Endings(tree)));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            Assert.That(Execute(selectedTree), Is.EqualTo(Execute(tree)));
            string expected = selectOmission ? "return  /* before */ " + ending + "x + y" + ending + " /* after */ ;" : returned.ToString();
            Assert.That(selectedTree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().Single().ToString(), Is.EqualTo(expected));
            if (selectOmission)
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(2));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(2));
            }
        }

        [TestCase("restored", false)]
        [TestCase("restored", true)]
        [TestCase("arrow", false)]
        [TestCase("newline-formal", true)]
        public async Task UnknownOrRestoredTriviaBoundariesRetainNative(string mode, bool crLf)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(crLf, mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-authored-binary-trivia-veto", compilation, ["authored-binary-trivia"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(crLf, mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            bool restore = mode == "restored";
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(restore ? 2 : 0));
            int openings = 0;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                var declaration = ((VariableExpression)owner.Binary.Right).Declaration;
                var previous = declaration.RequestedName;
                declaration.Update("newName");
                declaration.Update(previous);
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(openings, Is.EqualTo(restore ? 1 : 0));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                string ending = crLf ? "\r\n" : "\n";
                Assert.That(Returned(raw), Is.EqualTo("return  /* before */ (" + ending + "x + y" + ending + ") /* after */ ;"));
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            }
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            Assert.That(allowed, Is.EqualTo(!restore));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            Assert.That(Execute(tree), Is.EqualTo(new object?[] { 7, int.MaxValue, int.MinValue }));
            if (mode == "newline-formal") { Assert.That(Returned(selected), Does.Contain("x + y\n")); }
        }

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("before", false)]
        [TestCase("before", true)]
        [TestCase("after", false)]
        [TestCase("after", true)]
        public async Task CompilerTriviaUsesActualFilesAndFinalAuthority(string mode, bool crLf)
        {
            var directory = Directory.CreateTempSubdirectory("task05-authored-trivia-");
            var original = Directory.CreateDirectory(Path.Combine(directory.FullName, "original"));
            var isolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "isolated"));
            var nativeOriginal = Directory.CreateDirectory(Path.Combine(directory.FullName, "native-original"));
            var nativeIsolated = Directory.CreateDirectory(Path.Combine(directory.FullName, "native-isolated"));
            string source = Path.Combine(directory.FullName, "Marker.cs");
            string nativeSource = Path.Combine(directory.FullName, "NativeMarker.cs");
            const string marker = "namespace Inputs { internal class Marker {} }\n";
            File.WriteAllText(source, marker);
            File.WriteAllText(nativeSource, marker);
            try
            {
                string ending = crLf ? "\r\n" : "\n";
                string retained = "return ( /* before */ (" + ending + "x + y" + ending + ") /* after */ );";
                string shortened = "return  /* before */ " + ending + "x + y" + ending + " /* after */ ;";
                var owner = new Clock(crLf);
                CodeModelGenerator.Instance = await CompilerGenerator(original.FullName, owner, enabled: true);
                CodeFile? selected = null;
                var pipeline = new CSharpGen
                {
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        Assert.That(prepared?.PocGroupingUses, Is.EqualTo(2));
                        Assert.That(Returned(file), Is.EqualTo(shortened));
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        Assert.That(allowed, Is.True);
                        if (mode == "before") { File.AppendAllText(source, "// expired before selection\n"); }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "after") { File.AppendAllText(source, "// expired after selection\n"); }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated.FullName, new Session(original.FullName, source));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"), "False requires discarding isolated output");
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
                Assert.That(Returned(selected), Is.EqualTo(mode == "before" ? retained : shortened));
                string actual = File.ReadAllText(Path.Combine(isolated.FullName, selected.Name));

                CodeModelGenerator.Instance = await CompilerGenerator(nativeOriginal.FullName, new Clock(crLf), enabled: false);
                CodeFile? nativeSelected = null;
                var nativePipeline = new CSharpGen { PocSelectedFileObserved = file => nativeSelected = file };
                var nativeResult = await nativePipeline.ExecuteForCompilerBuildAsync(nativeIsolated.FullName, new Session(nativeOriginal.FullName, nativeSource));
                Assert.That(nativeResult.UsedCapturedResidualInputs, Is.False);
                Assert.That(nativeSelected, Is.Not.Null);
                Assert.That(Returned(nativeSelected!), Is.EqualTo(retained));
                string nativeFinal = File.ReadAllText(Path.Combine(nativeIsolated.FullName, nativeSelected!.Name));
                PreparedParenthesesTests.AssertOperations(nativeFinal, actual);
                var actualTree = CSharpSyntaxTree.ParseText(actual, path: selected.Name);
                var nativeTree = CSharpSyntaxTree.ParseText(nativeFinal, path: nativeSelected.Name);
                Assert.That(Comments(actualTree), Is.EqualTo(Comments(nativeTree)));
                Assert.That(Endings(actualTree), Is.EqualTo(Endings(nativeTree)));
                Assert.That(Execute(actualTree), Is.EqualTo(Execute(nativeTree)));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<NativeGenerator> CompilerGenerator(string directory, Clock owner, bool enabled)
        {
            var config = enabled ? Configuration : Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory, configuration: config,
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            return new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
        }

        private static string Returned(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<ReturnStatementSyntax>().Single().ToString();

        private static string Source(string statement) => "namespace Sample { public class Clock { public int M(int x, int y) { " + statement + " } } }";

        private static string[] Comments(SyntaxTree tree) => tree.GetRoot().DescendantTrivia()
            .Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            .Select(trivia => trivia.RawKind + ":" + trivia.ToFullString()).ToArray();

        private static string[] Endings(SyntaxTree tree) => tree.GetRoot().DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)).Select(trivia => trivia.ToFullString()).ToArray();

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("AuthoredTriviaOracle",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(SyntaxTree tree) => PreparedGeneratedRuntime.Invoke(Compilation().AddSyntaxTrees(tree),
            "Sample.Clock", "M", [3, 4], [int.MinValue, -1], [int.MaxValue, 1]);

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-authored-trivia-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(bool crLf, string mode = "statement") : TypeProvider
        {
            internal AuthoredBinaryTriviaExpression Outer { get; private set; } = null!;
            internal BinaryOperatorExpression Binary { get; private set; } = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Binary = new BinaryOperatorExpression("+", x, y, AddParentheses: false);
                Outer = new AuthoredBinaryTriviaExpression(new AuthoredBinaryLineExpression(Binary, crLf, "after"), "before");
                if (mode == "newline-formal") { y.AsVariable().Declaration.Update("y\n"); }
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [x, y]);
                return [mode == "arrow" ? new MethodProvider(signature, Outer, this) : new MethodProvider(signature, Return(Outer), this)];
            }
        }
    }
}

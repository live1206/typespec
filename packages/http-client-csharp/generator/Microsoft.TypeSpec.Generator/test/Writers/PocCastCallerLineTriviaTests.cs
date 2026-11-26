// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    internal class PocCastCallerLineTriviaTests
    {
        [TestCase("\n")]
        [TestCase("\r\n")]
        public void OriginalCastChainPreservesTheActualOmittedCallerLine(string ending)
        {
            string source = "class Clock{public int M(int value){var result=(/*a*/(" + ending + "(int)value" + ending +
                ")/*b*/);return result+Line();}static int Line([System.Runtime.CompilerServices.CallerLineNumber]int line=0)=>line;}";
            var original = CSharpSyntaxTree.ParseText(source, path: "Clock.cs");
            var originalLocal = original.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            var outer = (ParenthesizedExpressionSyntax)originalLocal.Initializer!.Value;
            var inner = (ParenthesizedExpressionSyntax)outer.Expression;
            Assert.That(inner.Expression, Is.TypeOf<CastExpressionSyntax>());
            Assert.That(outer.OpenParenToken.TrailingTrivia.ToFullString(), Is.EqualTo("/*a*/"));
            Assert.That(inner.CloseParenToken.TrailingTrivia.ToFullString(), Is.EqualTo("/*b*/"));
            int[] delimiters = [outer.OpenParenToken.SpanStart, outer.CloseParenToken.SpanStart,
                inner.OpenParenToken.SpanStart, inner.CloseParenToken.SpanStart];
            string edited = new(source.Where((_, position) => !delimiters.Contains(position)).ToArray());
            Assert.That(edited, Does.Contain("result=/*a*/"));
            var shortened = CSharpSyntaxTree.ParseText(edited, path: original.FilePath);
            Assert.That(shortened.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single().Initializer!.Value,
                Is.TypeOf<CastExpressionSyntax>());
            Assert.That(SignificantTrivia(shortened), Is.EqualTo(SignificantTrivia(original)));
            Assert.That(Endings(original), Is.EqualTo(new[] { ending, ending }));
            Assert.That(Endings(shortened), Is.EqualTo(Endings(original)));
            PreparedParenthesesTests.AssertOperations(source, edited);
            AssertBindingAndRuntime(original);
            AssertBindingAndRuntime(shortened);
        }

        [Test]
        public async Task NativeTypedCastInitializerRetainsBeforeTheActualCallerLine()
        {
            var directory = Directory.CreateTempSubdirectory("task05-cast-caller-line-");
            string marker = Path.Combine(directory.FullName, "Marker.cs");
            File.WriteAllText(marker, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var candidate = await WriteCompilerCase(directory.FullName, marker, enabled: true);
                var native = await WriteCompilerCase(directory.FullName, marker, enabled: false);
                Assert.That(candidate.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(candidate.Selected.Content, Is.EqualTo(native.Selected.Content));
                var candidateTree = Tree(candidate.Selected);
                var local = candidateTree.GetRoot().DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(local.NormalizeWhitespace().ToFullString(), Is.EqualTo("int result = ((int)value);"));
                Assert.That(GeneratedCallerLine(candidateTree), Is.EqualTo(GeneratedCallerLine(Tree(native.Selected))));

                // Downstream legacy reduction is not the prepared choice.
                // Compare native and candidate at the actual physical final stage.
                Assert.That(candidate.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, candidate.Final.Content);
                Assert.That(GeneratedCallerLine(Tree(candidate.Final)), Is.EqualTo(GeneratedCallerLine(Tree(native.Final))));
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        public async Task NativeCompactCastInitializerOwnsItsTriviaAndCallerLine(string ending)
        {
            var directory = Directory.CreateTempSubdirectory("task05-compact-cast-caller-line-");
            try
            {
                string marker = Path.Combine(directory.FullName, "Marker.cs");
                File.WriteAllText(marker, "namespace Inputs { internal class Marker {} }\n");
                var actual = await WriteCompilerCase(directory.FullName, marker, enabled: true, compactEnding: ending);
                var native = await WriteCompilerCase(directory.FullName, marker, enabled: false, compactEnding: ending);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                int callerLine = GeneratedCallerLine(Tree(actual.Selected));
                Assert.That(callerLine, Is.EqualTo(GeneratedCallerLine(Tree(native.Selected))));
                string expected = "var result=(/*a*/(" + ending + "(int)value" + ending + ")/*b*/);return result+Line();";
                var tree = Tree(actual.Selected);
                var body = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "M").Body!;
                string statementText = tree.GetText().ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    body.Statements[0].SpanStart, body.Statements[1].Span.End));
                Assert.That(statementText, Is.EqualTo(expected));
                Assert.That(body.Statements[0].GetLocation().GetLineSpan().StartLinePosition.Line + 3, Is.EqualTo(callerLine));
                AssertBindingAndRuntime(tree, callerLine, declaringType: "Sample.Clock");
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                Assert.That(GeneratedCallerLine(Tree(actual.Final)), Is.EqualTo(callerLine));
                Assert.That(GeneratedCallerLine(Tree(native.Final)), Is.EqualTo(callerLine));
                Assert.That(Endings(Tree(actual.Final)), Is.EqualTo(Endings(tree)));
                Assert.That(SignificantTrivia(Tree(actual.Final)), Is.EqualTo(SignificantTrivia(tree)));
                Assert.That(Endings(Tree(native.Final)), Is.EqualTo(Endings(Tree(native.Selected))));
                Assert.That(SignificantTrivia(Tree(native.Final)), Is.EqualTo(SignificantTrivia(Tree(native.Selected))));
                AssertFinalAfterIdentityCastReduction(actual.Selected, actual.Final);
                AssertFinalAfterIdentityCastReduction(native.Selected, native.Final);
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        public async Task ClosedCompactCastOmitsOnlyItsAuthoredPairs(string ending)
        {
            var directory = Directory.CreateTempSubdirectory("task05-compact-cast-pairs-");
            try
            {
                var actual = await WriteCompilerCase(directory.FullName, marker: null, enabled: true, compactEnding: ending, observePairs: true);
                var native = await WriteCompilerCase(directory.FullName, marker: null, enabled: false, compactEnding: ending);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                var nativeSelected = Tree(native.Selected);
                var actualSelected = Tree(actual.Selected);
                var actualFinal = Tree(actual.Final);
                var nativeFinal = Tree(native.Final);
                foreach (var tree in new[] { nativeSelected, actualSelected })
                {
                    var root = (CompilationUnitSyntax)tree.GetRoot();
                    Assert.That(root.Usings.Select(directive => directive.ToString()),
                        Is.EqualTo(new[] { "using System.Runtime.CompilerServices;" }));
                }
                int callerLine = GeneratedCallerLine(nativeSelected);
                foreach (var tree in new[] { actualSelected, actualFinal, nativeFinal })
                {
                    Assert.That(GeneratedCallerLine(tree), Is.EqualTo(callerLine));
                    Assert.That(Endings(tree), Is.EqualTo(Endings(nativeSelected)));
                    Assert.That(SignificantTrivia(tree), Is.EqualTo(SignificantTrivia(nativeSelected)));
                }
                AssertBindingAndRuntime(actualSelected, callerLine, declaringType: "Sample.Clock");
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, actual.Selected.Content);
                AssertFinalAfterIdentityCastReduction(actual.Selected, actual.Final);
                AssertFinalAfterIdentityCastReduction(native.Selected, native.Final);

                string retained = "var result=(/*a*/(" + ending + "(int)value" + ending + ")/*b*/);return result+Line();";
                string shortened = "var result=/*a*/" + ending + "(int)value" + ending + "/*b*/;return result+Line();";
                Assert.That(native.Selected.Content, Does.Contain(retained));
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content.Replace(retained, shortened, StringComparison.Ordinal)));
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("\n", "restore")]
        [TestCase("\r\n", "restore")]
        [TestCase("\r\n", "unexpected-import")]
        [TestCase("\r\n", "other-default")]
        [TestCase("\r\n", "before-selection")]
        [TestCase("\r\n", "after-selection")]
        public async Task CompactCastRetainsOrRequiresDiscardWhenItsProofExpires(string ending, string mode)
        {
            var directory = Directory.CreateTempSubdirectory("task05-compact-cast-pair-guards-");
            try
            {
                var actual = await WriteCompilerCase(directory.FullName, marker: null, enabled: true, compactEnding: ending,
                    observePairs: mode != "other-default", mode: mode);
                var native = await WriteCompilerCase(directory.FullName, marker: null, enabled: false, compactEnding: ending,
                    mode: mode == "other-default" ? mode : null);
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                if (mode == "after-selection")
                {
                    Assert.That(actual.Selected.Content, Does.Contain("var result=/*a*/" + ending + "(int)value" + ending + "/*b*/;"));
                    return; // Compiler result above must require discard, regardless of written isolated files.
                }
                Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content));
                var selected = Tree(native.Selected);
                var final = Tree(actual.Final);
                Assert.That(GeneratedCallerLine(final), Is.EqualTo(GeneratedCallerLine(selected)));
                Assert.That(SignificantTrivia(final), Is.EqualTo(SignificantTrivia(selected)));
                Assert.That(Endings(final), Is.EqualTo(Endings(selected)));
                AssertFinalAfterIdentityCastReduction(actual.Selected, actual.Final);
            }
            finally { directory.Delete(recursive: true); }
        }

        [TestCase("null-initializer", "AuthoredCompactCastLocalStatement.WriteCore")]
        [TestCase("null-line-wrapper", "AuthoredCompactCastTriviaExpression.WriteCore")]
        [TestCase("null-cast", "CodeWriter.WriteAuthoredIntCastAtColumnZero")]
        [TestCase("null-operand", "AuthoredBareIntCastExpression.WriteCore")]
        [TestCase("null-declaration", "AuthoredCompactCastLocalStatement.WriteCore")]
        [TestCase("null-local", "InferredLocalDeclarationExpression.WriteCore")]
        public void MalformedCompactCastKeepsItsNativeWriteFailure(string mode, string writer)
        {
            var directory = Directory.CreateTempSubdirectory("task05-malformed-compact-");
            try
            {
                var native = Assert.ThrowsAsync<NullReferenceException>(() => WriteCompilerCase(directory.FullName, marker: null,
                    enabled: false, compactEnding: "\n", mode: mode));
                Assert.That(native!.StackTrace, Does.Contain(writer));
                var prepared = Assert.ThrowsAsync<NullReferenceException>(() => WriteCompilerCase(directory.FullName, marker: null,
                    enabled: true, compactEnding: "\n", mode: mode));
                Assert.That(prepared!.StackTrace, Does.Contain(writer));
            }
            finally { directory.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerCase(string directory, string? marker, bool enabled,
            string? compactEnding = null, bool observePairs = false, string? mode = null)
        {
            string original = Directory.CreateDirectory(Path.Combine(directory, enabled ? "prepared-original" : "native-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, enabled ? "prepared-isolated" : "native-isolated")).FullName;
            string config = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":" + (enabled ? "true" : "false") +
                ",\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeCallerLineInputs")));
            var owner = new Clock(compactEnding, mode == "other-default" ? 1 : 0, mode);
            bool expire = mode is "before-selection" or "after-selection";
            string? copiedCore = expire ? Path.Combine(directory, "captured-core.dll") : null;
            if (copiedCore is not null) { File.Copy(typeof(object).Assembly.Location, copiedCore); }
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeFile? selected = null;
            int captures = 0;
            int castVisits = 0;
            int operandVisits = 0;
            int mutationCount = 0;
            void ExpireReference()
            {
                using var append = File.Open(copiedCore!, FileMode.Append);
                append.WriteByte(0);
                mutationCount++;
            }
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (prepared is null) { return; }
                    captures++;
                    Assert.That(prepared.PocGroupingCount, Is.EqualTo(observePairs ? 2 : 0));
                    if (compactEnding is not null && mode is not ("null-initializer" or "null-line-wrapper"))
                    {
                        var authoredLine = prepared.GetSourceFile(owner)!.Roots.SelectMany(root => root.DescendantsAndSelf())
                            .Single(node => node.Producer is AuthoredCompactCastLineExpression);
                        Assert.That(authoredLine.IsOpaque, Is.False);
                        Assert.That(authoredLine.IntrinsicSource.LineEndings, Is.EqualTo(SourceFactState.Unavailable));
                        Assert.That(authoredLine.IntrinsicSource.IntrinsicLineEnding, Is.Null);
                    }
                    if (mode == "restore")
                    {
                        prepared.PocAfterOpening = () =>
                        {
                            var declaration = owner.CastOperand!.Declaration;
                            string previous = declaration.RequestedName;
                            declaration.Update("other");
                            declaration.Update(previous);
                            mutationCount++;
                        };
                    }
                    prepared.PocNativeWriteObserver = node =>
                    {
                        if (ReferenceEquals(node, owner.CastNode)) { castVisits++; }
                        if (ReferenceEquals(node, owner.CastOperand)) { operandVisits++; }
                    };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    if (!observePairs)
                    {
                        Assert.That(prepared?.PocGroupingUses ?? 0, Is.Zero);
                        Assert.That(file.PreparedGroupingCount, Is.Zero);
                    }
                    if (enabled && compactEnding is not null)
                    {
                        Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(mode != "restore"));
                        string fallback = file.SelectPreparedForms(false, false, false).Content;
                        if (observePairs)
                        {
                            int uses = mode == "restore" ? 1 : 2;
                            Assert.That(prepared!.PocGroupingUses, Is.EqualTo(uses));
                            Assert.That(file.PreparedGroupingCount, Is.EqualTo(uses));
                            if (mode == "unexpected-import")
                            {
                                // Exercise the file-local observation without changing the emitted test inputs.
                                prepared.ObserveCompactCallerLineImports(prepared.GetSourceFile(owner), ["System.Runtime.CompilerServices", "System"]);
                                mutationCount++;
                            }
                            Assert.That(fallback, Does.Contain("var result=(/*a*/(" + compactEnding + "(int)value" + compactEnding + ")/*b*/);"));
                        }
                        else { Assert.That(fallback, Is.EqualTo(file.Content)); }
                    }
                },
                PocGroupingDecisionObserved = (_, allowed) =>
                {
                    if (mode == "before-selection")
                    {
                        Assert.That(allowed, Is.True);
                        ExpireReference();
                    }
                },
                PocSelectedFileObserved = file =>
                {
                    selected = file;
                    if (mode == "after-selection") { ExpireReference(); }
                }
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, marker, copiedCore));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(enabled && !expire));
            Assert.That(mutationCount, Is.EqualTo(mode is "restore" or "unexpected-import" or "before-selection" or "after-selection" ? 1 : 0));
            Assert.That(captures, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(castVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(operandVisits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static void AssertFinalAfterIdentityCastReduction(CodeFile selected, CodeFile final)
        {
            var tree = Tree(selected);
            var root = tree.GetRoot();
            var cast = root.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var compilation = CSharpCompilation.Create("CompactIdentityCastWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var operation = (IConversionOperation)compilation.GetSemanticModel(tree).GetOperation(cast)!;
            Assert.That(operation.Conversion.IsIdentity && !operation.Conversion.IsUserDefined, Is.True);
            Assert.That(Tree(final).GetRoot().DescendantNodes().OfType<CastExpressionSyntax>(), Is.Empty);
            // The independent final pipeline removes the identity cast. Compare
            // against a selected-source oracle that makes just that edit; the
            // operation comparison still checks the actual caller-line constant.
            var expected = root.ReplaceNode(cast, cast.Expression.WithTriviaFrom(cast));
            PreparedParenthesesTests.AssertOperations(expected.ToFullString(), final.Content);
        }

        private static SyntaxTree Tree(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name);

        private static int GeneratedCallerLine(SyntaxTree tree)
        {
            var compilation = CSharpCompilation.Create("NativeCallerLineWitness", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Line");
            var symbol = model.GetDeclaredSymbol(declaration)!;
            var attribute = symbol.Parameters.Single().GetAttributes().Single();
            var canonical = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly.GetTypeByMetadataName(typeof(CallerLineNumberAttribute).FullName!);
            Assert.That(canonical, Is.Not.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, canonical), Is.True);
            var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(operation.TargetMethod, symbol), Is.True);
            Assert.That(call.ArgumentList.Arguments, Is.Empty);
            var argument = operation.Arguments.Single();
            Assert.That(argument.IsImplicit, Is.True);
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            int physicalLine = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Assert.That(argument.Value.ConstantValue.HasValue, Is.True);
            Assert.That(argument.Value.ConstantValue.Value, Is.EqualTo(physicalLine));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [0], [17]),
                Is.EqualTo(new object?[] { physicalLine, 17 + physicalLine }));
            return physicalLine;
        }

        private static void AssertBindingAndRuntime(SyntaxTree tree, int expectedLine = 3, string declaringType = "Clock")
        {
            var compilation = CSharpCompilation.Create("OriginalCallerLineTrivia", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "M");
            var lineMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Line");
            var lineSymbol = model.GetDeclaredSymbol(lineMethod)!;
            Assert.That(lineSymbol.Parameters.Single().GetAttributes().Single().AttributeClass?.ToDisplayString(),
                Is.EqualTo("System.Runtime.CompilerServices.CallerLineNumberAttribute"));
            var cast = method.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            Assert.That(((IConversionOperation)model.GetOperation(cast)!).Conversion.IsIdentity, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(cast.Expression).Symbol,
                model.GetDeclaredSymbol(method)!.Parameters.Single()), Is.True);
            var local = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            var sum = (BinaryExpressionSyntax)method.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(local), model.GetSymbolInfo(sum.Left).Symbol), Is.True);
            var call = (InvocationExpressionSyntax)sum.Right;
            var operation = (IInvocationOperation)model.GetOperation(call)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(operation.TargetMethod, lineSymbol), Is.True);
            var argument = operation.Arguments.Single();
            Assert.That(argument.IsImplicit, Is.True);
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            int physicalLine = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Assert.That(physicalLine, Is.EqualTo(expectedLine));
            Assert.That(argument.Value.ConstantValue.HasValue, Is.True);
            Assert.That(argument.Value.ConstantValue.Value, Is.EqualTo(physicalLine));
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, declaringType, "M", [0], [17], [int.MaxValue]),
                Is.EqualTo(new object?[] { expectedLine, 17 + expectedLine, unchecked(int.MaxValue + expectedLine) }));
        }

        private static string[] SignificantTrivia(SyntaxTree tree) => tree.GetRoot().DescendantTrivia()
            .Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            .Select(trivia => trivia.RawKind + ":" + trivia.ToFullString()).ToArray();

        private static string[] Endings(SyntaxTree tree) => tree.GetRoot().DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            .Select(trivia => trivia.ToFullString()).ToArray();

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Session(string directory, string? source, string? core = null) : ITrustedCompilerBuildSession
        {
            private readonly string? _sourceHash = source is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = core ?? typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core ?? typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "native-cast-caller-line-output", "Debug|net10.0", "12.0", "Actual", [],
                source is null ? [] : [new CapturedCompilerSource(source, _sourceHash!)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(string? compactEnding, int lineDefault, string? mode) : TypeProvider
        {
            internal ValueExpression? CastNode { get; private set; }
            internal VariableExpression? CastOperand { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var value = new ParameterProvider("value", $"", typeof(int));
                var line = new ParameterProvider("line", $"", typeof(int), defaultValue: Literal(lineDefault),
                    attributes: new AttributeStatement[] { new(typeof(CallerLineNumberAttribute), Array.Empty<ValueExpression>()) });
                var local = new VariableExpression(typeof(int), "result");
                VariableExpression operand = value;
                CastOperand = operand;
                var call = new InvokeMethodExpression(null, "Line", []);
                MethodBodyStatement body;
                if (compactEnding is null)
                {
                    var declaration = new DeclarationExpression(local);
                    var cast = new CastExpression(operand, typeof(int));
                    CastNode = cast;
                    var total = new BinaryOperatorExpression("+", local, call, AddParentheses: false);
                    body = new MethodBodyStatements([new AssignmentExpression(declaration, cast).Terminate(), Return(total)]);
                }
                else
                {
                    var cast = new AuthoredBareIntCastExpression(mode == "null-operand" ? null! : operand);
                    CastNode = cast;
                    var inner = new AuthoredCompactCastLineExpression(mode == "null-cast" ? null! : cast, compactEnding == "\r\n", 'b');
                    var outer = new AuthoredCompactCastTriviaExpression(mode == "null-line-wrapper" ? null! : inner, 'a');
                    var declaration = new InferredLocalDeclarationExpression(mode == "null-local" ? null! : local);
                    body = new AuthoredCompactCastLocalStatement(mode == "null-declaration" ? null! : declaration,
                        mode == "null-initializer" ? null! : outer, new AuthoredCompactLocalAdditionExpression(local, call));
                }
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [value]), body, this),
                    new MethodProvider(new MethodSignature("Line", null, MethodSignatureModifiers.Private | MethodSignatureModifiers.Static, typeof(int), null, [line]), (VariableExpression)line, this)];
            }
        }
    }
}

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
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocTypedDefaultTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [Test]
        public async Task LegacyNullableGenericSpellingDoesNotEmitTypedDefault()
        {
            await MockHelpers.LoadMockGeneratorAsync(configuration: "{\"package-name\":\"Sample\",\"disable-xml-docs\":true}");
            var owner = new Clock();
            owner.ProcessTypeForBackCompatibility();
            var file = new TypeProviderWriter(owner).Write();
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = CSharpCompilation.Create("LegacyTypedDefault", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            TestContext.WriteLine(syntax.ToString());
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            Assert.That(syntax.ReturnType.ToString(), Is.EqualTo("T?"));
            Assert.That(syntax.ConstraintClauses.Single().NormalizeWhitespace().ToString(), Is.EqualTo("where T : struct"));
            var model = compilation.GetSemanticModel(tree);
            var declared = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
            var parameter = declared.TypeParameters.Single();
            Assert.That(parameter.HasValueTypeConstraint, Is.True);
            var cast = syntax.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            Assert.That(cast.Type.ToString(), Is.EqualTo("T?"));
            Assert.That(cast.Expression.IsKind(SyntaxKind.NullLiteralExpression), Is.True);
            Assert.That(syntax.DescendantNodes().OfType<DefaultExpressionSyntax>(), Is.Empty);
            foreach (var type in new[] { declared.ReturnType, model.GetTypeInfo(cast.Type).Type })
            {
                Assert.That(type, Is.InstanceOf<INamedTypeSymbol>());
                var nullable = (INamedTypeSymbol)type!;
                Assert.That(nullable.OriginalDefinition.SpecialType, Is.EqualTo(SpecialType.System_Nullable_T));
                Assert.That(SymbolEqualityComparer.Default.Equals(nullable.TypeArguments.Single(), parameter), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequiredTypedDefaultDelimitersAreIndependentOfTheCastPair(bool selectGrouping)
        {
            var inputs = CSharpCompilation.Create("TypedDefaultInputs", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(inputs));
            var owner = new Clock(typed: true);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(inputs, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "typed-default", inputs, ["typed-default"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(typed: true);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var visits = new List<object>();
            prepared!.PocNativeWriteObserver = visits.Add;
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = written.SelectPreparedForms(false, false, false);
            var selected = written.SelectPreparedForms(false, false,
                selectGrouping && prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written));

            foreach (var file in new[] { native, qualified, selected })
            {
                Assert.That(file.Name, Is.EqualTo(native.Name));
                var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
                var compilation = inputs.AddSyntaxTrees(tree);
                Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
                var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                var model = compilation.GetSemanticModel(tree);
                var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
                var parameter = method.TypeParameters.Single();
                Assert.That(parameter.HasValueTypeConstraint, Is.True);
                var cast = syntax.DescendantNodes().OfType<CastExpressionSyntax>().Single();
                Assert.That(cast.Expression, Is.InstanceOf<DefaultExpressionSyntax>());
                var operand = (DefaultExpressionSyntax)cast.Expression;
                Assert.That(operand.Type.ToString(), Is.EqualTo("T?"));
                Assert.That(((IConversionOperation)model.GetOperation(cast)!).Conversion.IsIdentity, Is.True);
                foreach (var type in new[] { method.ReturnType, model.GetTypeInfo(cast.Type).Type, model.GetTypeInfo(operand.Type).Type })
                {
                    Assert.That(type, Is.InstanceOf<INamedTypeSymbol>());
                    var nullable = (INamedTypeSymbol)type!;
                    Assert.That(nullable.OriginalDefinition.SpecialType, Is.EqualTo(SpecialType.System_Nullable_T));
                    Assert.That(SymbolEqualityComparer.Default.Equals(nullable.TypeArguments.Single(), parameter), Is.True);
                }
                const string harness = "public class Probe { public object Run() => new Sample.Clock().M<int>(); public object RunGuid() => new Sample.Clock().M<System.Guid>(); }";
                var runnable = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(harness));
                Assert.That(PreparedGeneratedRuntime.Invoke(runnable, "Probe", "Run", [[]]).Single(), Is.Null);
                Assert.That(PreparedGeneratedRuntime.Invoke(runnable, "Probe", "RunGuid", [[]]).Single(), Is.Null);
            }
            Assert.That(Body(native), Is.EqualTo("((T?)default(T?))"));
            Assert.That(Body(qualified), Is.EqualTo("((T?)default(T?))"));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            var actual = visits.Where(node => ReferenceEquals(node, owner.Cast) || ReferenceEquals(node, owner.Cast!.Inner)).ToArray();
            Assert.That(actual.Length, Is.EqualTo(2));
            Assert.That(actual[0], Is.SameAs(owner.Cast));
            Assert.That(actual[1], Is.SameAs(owner.Cast!.Inner));
            Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            TestContext.WriteLine($"TYPED-DEFAULT select={selectGrouping}; planned={prepared.PocGroupingCount}; used={written.PreparedGroupingCount}; selected={Body(selected)}");
            Assert.That(Body(selected), Is.EqualTo(selectGrouping ? "(T?)default(T?)" : "((T?)default(T?))"));
            if (selectGrouping) { Assert.That(written.PreparedGroupingCount, Is.EqualTo(1)); }
        }

        [TestCase("foreign-parameter")]
        [TestCase("restored-constraint")]
        public async Task TypedDefaultRequiresCurrentOwnedParameterAndConstraint(string defect)
        {
            var inputs = CSharpCompilation.Create("TypedDefaultGuard", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(inputs));
            var owner = new Clock(typed: true, defect: defect);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(inputs, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "typed-default-guard", inputs, ["typed-default-guard"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            bool restore = defect == "restored-constraint";
            int callbacks = 0;
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                prepared.PocAfterOpening = () =>
                {
                    owner.Constraint.Update("class", null);
                    owner.Constraint.Update("struct", null);
                    callbacks++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(false, false,
                prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(callbacks, Is.EqualTo(restore ? 1 : 0));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(restore ? 1 : 0));
            if (restore) { Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False); }
            Assert.That(Body(selected), Is.EqualTo("((T?)default(T?))"));
            var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            Assert.That(inputs.AddSyntaxTrees(tree).GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        }

        [TestCase("stable")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task CompilerTypedDefaultUsesActualSelectedFilesAndResult(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-typed-default-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var native = await NativeFinalControl(sandbox, source);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("TypedDefaultCompilerInputs")));
                var owner = new Clock(typed: true, path: "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                int uses = 0;
                int castVisits = 0;
                int operandVisits = 0;
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                            if (ReferenceEquals(node, owner.Cast!.Inner)) { operandVisits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(Body(file), Is.EqualTo("(T?)default(T?)"));
                    },
                    PocGroupingDecisionObserved = (_, allowed) =>
                    {
                        Assert.That(allowed, Is.True);
                        if (mode == "source-expiry") { File.AppendAllText(source, "// changed before selection\n"); }
                    },
                    PocSelectedFileObserved = file =>
                    {
                        selected = file;
                        if (mode == "result-expiry") { File.AppendAllText(source, "// changed after selection\n"); }
                    }
                };
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(castVisits, Is.EqualTo(1));
                Assert.That(operandVisits, Is.EqualTo(1));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(Body(selected), Is.EqualTo(mode == "source-expiry" ? "((T?)default(T?))" : "(T?)default(T?)"));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires discard even after short selection");
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, final.Content);
                const string harness = "public class Probe { public object Run() => new Sample.Clock().M<System.Guid>(); }";
                var runnable = CSharpCompilation.Create("TypedDefaultFinal", [CSharpSyntaxTree.ParseText(final.Content, path: final.Name), CSharpSyntaxTree.ParseText(harness)],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(runnable, "Probe", "Run", [[]]).Single(), Is.Null);
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> NativeFinalControl(string sandbox, string source)
        {
            var original = Path.Combine(sandbox, "native-original");
            var isolated = Path.Combine(sandbox, "native-isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            var config = Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeTypedDefaultInputs")));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(new Clock(typed: true, path: "alias/Clock.cs")));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            var pipeline = new CSharpGen { PocSelectedFileObserved = file => selected = file };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "This disabled control is only for stage-aligned comparison");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
            Assert.That(Body(selected), Is.EqualTo("((T?)default(T?))"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static string Body(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression.ToString();

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Template<T> { }
        private sealed class Foreign<T> { }

        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "typed-default-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(bool typed = false, string path = "Clock.cs", string? defect = null) : TypeProvider
        {
            internal CastExpression? Cast;
            internal KeywordExpression Constraint = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                CSharpType parameter = typeof(Template<>).GetGenericArguments()[0];
                CSharpType formatting = defect == "foreign-parameter" ? typeof(Foreign<>).GetGenericArguments()[0] : parameter;
                CSharpType nullable = formatting.WithNullable(true);
                var where = Where.Struct(parameter);
                Constraint = (KeywordExpression)where.Constraints.Single();
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, nullable, null, [],
                    GenericArguments: new[] { parameter }, GenericParameterConstraints: new[] { where });
                ValueExpression body = typed ? (Cast = new CastExpression(new TypedDefaultExpression(nullable), nullable)) : DefaultOf(nullable);
                return [new MethodProvider(signature, body, this)];
            }
        }
    }
}

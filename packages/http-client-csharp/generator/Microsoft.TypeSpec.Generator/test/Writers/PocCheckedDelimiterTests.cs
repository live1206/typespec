// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
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
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocCheckedDelimiterTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequiredCheckedDelimitersAreIndependentOfTheCastPair(bool selectGrouping)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "checked-delimiter", compilation, ["checked-delimiter"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock();
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
                var expression = BoundCheckedExpression(file);
                Assert.That(expression.OpenParenToken.IsMissing, Is.False);
                Assert.That(expression.CloseParenToken.IsMissing, Is.False);
                Assert.That(Execute(file, [[(long)int.MinValue], [(long)int.MaxValue]]), Is.EqualTo(new object?[] { int.MinValue, int.MaxValue }));
                Assert.That(Assert.Throws<TargetInvocationException>(() => Execute(file, [[(long)int.MaxValue + 1]]))!.InnerException,
                    Is.InstanceOf<OverflowException>());
            }
            Assert.That(BoundCheckedExpression(native).ToString(), Is.EqualTo("checked(((int)value))"));
            Assert.That(BoundCheckedExpression(qualified).ToString(), Is.EqualTo("checked(((int)value))"));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            var observed = visits.Where(node => ReferenceEquals(node, owner.Checked) || ReferenceEquals(node, owner.Cast) || ReferenceEquals(node, owner.Cast.Inner)).ToArray();
            Assert.That(observed.Length, Is.EqualTo(3));
            Assert.That(observed[0], Is.SameAs(owner.Checked));
            Assert.That(observed[1], Is.SameAs(owner.Cast));
            Assert.That(observed[2], Is.SameAs(owner.Cast.Inner));
            Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);

            // Representation-only baseline retains the cast pair, so only the
            // enabled case should fail before grouping admission is added.
            TestContext.WriteLine($"CHECKED-DELIMITERS select={selectGrouping}; planned={prepared.PocGroupingCount}; used={written.PreparedGroupingCount}; selected={BoundCheckedExpression(selected)}");
            Assert.That(BoundCheckedExpression(selected).ToString(), Is.EqualTo(selectGrouping ? "checked((int)value)" : "checked(((int)value))"));
            if (selectGrouping) { Assert.That(written.PreparedGroupingCount, Is.EqualTo(1)); }
        }

        [TestCase("stable")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task CompilerCheckedCastUsesActualSelectedFilesAndResult(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-checked-delimiter-compiler", Guid.NewGuid().ToString("N"));
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
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("CheckedDelimiterCompilerInputs")));
                var owner = new Clock("alias/Clock.cs");
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
                        Assert.That(BoundCheckedExpression(file).Expression, Is.InstanceOf<CastExpressionSyntax>());
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
                Assert.That(BoundCheckedExpression(selected).Expression is ParenthesizedExpressionSyntax, Is.EqualTo(mode == "source-expiry"));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires discard even after short selection");
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, final.Content);
                object?[][] values = [[(long)int.MaxValue], [(long)int.MinValue]];
                Assert.That(Execute(selected, values), Is.EqualTo(Execute(native.Selected, values)));
                Assert.That(Execute(final, values), Is.EqualTo(Execute(native.Final, values)));
                _ = BoundCheckedExpression(selected);
                _ = BoundCheckedExpression(final);
                Assert.That(Assert.Throws<TargetInvocationException>(() => Execute(final, [[(long)int.MaxValue + 1]]))!.InnerException,
                    Is.InstanceOf<OverflowException>());
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
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeCheckedDelimiterInputs")));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(new Clock("alias/Clock.cs")));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            var pipeline = new CSharpGen { PocSelectedFileObserved = file => selected = file };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "This disabled control is only for stage-aligned comparison");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
            Assert.That(BoundCheckedExpression(selected).Expression, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static CheckedExpressionSyntax BoundCheckedExpression(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var expression = tree.GetRoot().DescendantNodes().OfType<CheckedExpressionSyntax>().Single();
            var cast = expression.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var operation = (IConversionOperation)compilation.GetSemanticModel(tree).GetOperation(cast)!;
            Assert.That(operation.IsChecked, Is.True);
            Assert.That(operation.Operand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int64));
            Assert.That(operation.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            return expression;
        }

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("CheckedDelimiters",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] values) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", values);

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
            public CompilerBuildInputCapture Capture => new(directory, "checked-delimiter-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(string path = "Clock.cs") : TypeProvider
        {
            internal CheckedExpression Checked = null!;
            internal CastExpression Cast = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var value = new ParameterProvider("value", $"", typeof(long));
                Cast = new CastExpression(value, typeof(int));
                Checked = new CheckedExpression(Cast);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [value]), Checked, this)];
            }
        }
    }
}

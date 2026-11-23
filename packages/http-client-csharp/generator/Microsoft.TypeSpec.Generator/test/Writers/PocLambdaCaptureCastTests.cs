// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    internal class PocLambdaCaptureCastTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
        private const string HolderSource =  """
            namespace Sample
            {
                public sealed class Holder
                {
                    public readonly string Text;
                    public Holder(System.Func<int> f, [System.Runtime.CompilerServices.CallerArgumentExpression("f")] string text = null)
                    { Text = text; }
                }
            }
            """;

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeLambdaCastRespectsActualSelectedCallerCapture(bool constructor, bool explicitText)
        {
            var compilation = Compilation(constructor);
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(constructor, explicitText);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-lambda-capture-cast", compilation, ["lambda-capture-cast"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(constructor, explicitText);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            if (!constructor)
            {
                var formals = owner.Methods.Single(method => method.Signature.Name == "Capture").Signature.Parameters;
                TestContext.WriteLine($"LAMBDA-CAST-CACHED delegate={formals[0].HasCachedEmittedDeclarationState}; text={formals[1].HasCachedEmittedDeclarationState}");
            }
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int casts = 0;
            int operands = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { casts++; }
                if (ReferenceEquals(node, owner.Cast.Inner)) { operands++; }
            };
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = written.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written));
            var qualified = written.SelectPreparedForms(false, false, false);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            string proofSource = constructor ? "\n" + HolderSource : "";
            PreparedParenthesesTests.AssertOperations(native.Content + proofSource, selected.Content + proofSource);
            PreparedParenthesesTests.AssertOperations(native.Content + proofSource, qualified.Content + proofSource);
            var expected = Actual(native, constructor, explicitText);
            var actual = Actual(selected, constructor, explicitText);
            var fallback = Actual(qualified, constructor, explicitText);
            Assert.That(actual.Text, Is.EqualTo(expected.Text));
            Assert.That(fallback.Text, Is.EqualTo(expected.Text));
            Assert.That(expected.Cast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(fallback.Cast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(Execute(selected, constructor), Is.EqualTo(new[] { expected.Text, expected.Text }));
            string changed = native.Content.Replace("((int)value)", "(int)value", StringComparison.Ordinal);
            Assert.That(changed, Is.Not.EqualTo(native.Content));
            var knownEdited = Actual(new CodeFile(changed, native.Name), constructor, explicitText);
            Assert.That(knownEdited.Text == expected.Text, Is.EqualTo(explicitText), "Actual nested lambda capture observes the cast pair only if text is omitted");
            Assert.That(Execute(new CodeFile(changed, native.Name), constructor), Is.EqualTo(new[] { knownEdited.Text, knownEdited.Text }));
            Assert.That(casts, Is.EqualTo(1));
            Assert.That(operands, Is.EqualTo(1));
            Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            TestContext.WriteLine($"LAMBDA-CAST ctor={constructor}; explicit={explicitText}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; selectedParent={actual.Cast.Parent?.Kind()}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(explicitText ? 1 : 0));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(explicitText ? 1 : 0));
                Assert.That(written.PreparedGroupingCount, Is.EqualTo(explicitText ? 1 : 0));
                Assert.That(actual.Cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!explicitText));
            });
        }

        [Test]
        public async Task MissingCachedDelegateFormalTypeDoesNotReplaceTheNativeFailure()
        {
            var owner = new Clock(false, true);
            var generator = await ControlledGenerator(false, null, owner);
            var roots = generator.OutputLibrary.TypeProviders;
            var nativeOwner = new Clock(false, true);
            nativeOwner.ProcessTypeForBackCompatibility();
            var nativeFormal = nativeOwner.Methods.Single(method => method.Signature.Name == "Capture").Signature.Parameters[0];
            nativeFormal.Type = null!;
            var nativeError = Assert.Catch(() => new TypeProviderWriter(nativeOwner).Write());
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            var formal = owner.Methods.Single(method => method.Signature.Name == "Capture").Signature.Parameters[0];
            Assert.That(formal.HasCachedEmittedDeclarationState, Is.True);
            formal.Type = null!;
            Assert.That(formal.HasCachedEmittedDeclarationState, Is.True);
            string stage = "prepare";
            int plans = -1;
            int visits = 0;
            var preparedError = Assert.Catch(() =>
            {
                using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
                plans = prepared?.PocGroupingCount ?? 0;
                if (prepared is not null)
                {
                    prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Cast)) { visits++; } };
                }
                stage = "write";
                new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            });
            TestContext.WriteLine($"LAMBDA-NULL-FORMAL native={nativeError!.GetType().Name} at {nativeError.TargetSite}; actual={preparedError!.GetType().Name} at {preparedError.TargetSite}; stage={stage}; plans={plans}; visits={visits}");
            Assert.That(stage, Is.EqualTo("write"));
            Assert.That(plans, Is.Zero);
            Assert.That(preparedError!.GetType(), Is.EqualTo(nativeError!.GetType()));
            if (nativeError is ArgumentNullException expected)
            { Assert.That(((ArgumentNullException)preparedError).ParamName, Is.EqualTo(expected.ParamName)); }
        }

        [TestCase(false, "different-delegate")]
        [TestCase(true, "different-delegate")]
        [TestCase(false, "competing")]
        [TestCase(true, "competing")]
        public async Task UncertifiedLambdaCallableRetainsNativeCast(bool constructor, string defect)
        {
            var owner = new Clock(constructor, true, defect: defect);
            var generator = await ControlledGenerator(constructor, defect, owner);
            var roots = generator.OutputLibrary.TypeProviders;
            var nativeOwner = new Clock(constructor, true, defect: defect);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
            Assert.That(raw.PreparedGroupingCount, Is.Zero);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            string extra = constructor ? "\n" + HolderFor(defect) : "";
            PreparedParenthesesTests.AssertOperations(native.Content + extra, selected.Content + extra);
            Assert.That(Execute(selected, constructor, defect), Is.EqualTo(new[] { "manual", "manual" }));
            Assert.That(CastSyntax(selected).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredCapturedFormalExpiresTheLambdaCastChoice(bool constructor)
        {
            var owner = new Clock(constructor, true);
            var generator = await ControlledGenerator(constructor, null, owner);
            var roots = generator.OutputLibrary.TypeProviders;
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
            int restores = 0;
            int calls = 0;
            prepared.PocAfterOpening = () =>
            {
                var declaration = ((VariableExpression)owner.Cast.Inner).Declaration;
                var name = declaration.RequestedName;
                declaration.Update("changed");
                declaration.Update(name);
                restores++;
            };
            prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Cast)) { calls++; } };
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = written.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written));
            Assert.That(restores, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(written.PreparedGroupingCount, Is.EqualTo(1));
            Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            Assert.That(Actual(selected, constructor, true).Cast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(Execute(selected, constructor), Is.EqualTo(new[] { "manual", "manual" }));
        }

        [TestCase(false, "stable")]
        [TestCase(true, "stable")]
        [TestCase(true, "source-expiry")]
        [TestCase(false, "result-expiry")]
        public async Task CompilerLambdaCastUsesActualSelectedFilesAndResult(bool constructor, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-lambda-cast-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, constructor ? "Holder.cs" : "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, constructor ? HolderSource : "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var native = await NativeFinalControl(constructor, sandbox, source);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(Compilation(constructor)));
                var owner = new Clock(constructor, true, "alias/Clock.cs");
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
                        Assert.That(proof!.Compilation.SyntaxTrees.Any(tree => tree.FilePath == source), Is.True);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                            if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(Actual(file, constructor, true).Cast.Parent, Is.Not.InstanceOf<ParenthesizedExpressionSyntax>());
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
                Assert.That(Actual(selected, constructor, true).Cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(mode == "source-expiry"));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires the trusted caller to discard isolated output");
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                string extra = constructor ? "\n" + HolderSource : "";
                PreparedParenthesesTests.AssertOperations(native.Selected.Content + extra, selected.Content + extra);
                PreparedParenthesesTests.AssertOperations(native.Final.Content + extra, final.Content + extra);
                Assert.That(Execute(selected, constructor), Is.EqualTo(Execute(native.Selected, constructor)));
                Assert.That(Execute(final, constructor), Is.EqualTo(Execute(native.Final, constructor)));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> NativeFinalControl(bool constructor, string sandbox, string source)
        {
            var original = Path.Combine(sandbox, "native-original");
            var isolated = Path.Combine(sandbox, "native-isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            var config = Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(Compilation(constructor)));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(new Clock(constructor, true, "alias/Clock.cs")));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            var pipeline = new CSharpGen { PocSelectedFileObserved = file => selected = file };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "The disabled control is only for stage-aligned comparison");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
            Assert.That(Actual(selected, constructor, true).Cast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        [TestCase(true, "path", false)]
        [TestCase(true, "path", true)]
        [TestCase(true, "serialization", false)]
        [TestCase(true, "serialization", true)]
        [TestCase(false, "path", false)]
        [TestCase(false, "serialization", false)]
        public async Task LambdaCastDoesNotForceASecondColdDependency(bool constructor, string dependency, bool withLegacy)
        {
            var early = new Lazy<int>(() => 0);
            var late = new Lazy<int>(() => 0);
            bool armed = false;
            void Observe()
            {
                _ = early.Value;
                if (armed) { _ = late.Value; }
            }
            var owner = new Clock(constructor, true, observePath: dependency == "path" ? Observe : null);
            if (dependency == "serialization") { owner.Update(serializations: new SerializationCarrier(Observe)); }
            var control = new DirectControl();
            var generator = await ControlledGenerator(constructor, null, withLegacy ? [control, owner] : [owner]);
            var roots = generator.OutputLibrary.TypeProviders;
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(early.IsValueCreated, Is.True);
            Assert.That(late.IsValueCreated, Is.False);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            if (withLegacy)
            {
                var old = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                var selectedOld = old.SelectPreparedForms(true, true, prepared!.CanCommitPocGrouping(old) && prepared.SealPocGroupingWithoutCallbacks(old));
                Assert.That(old.PreparedGroupingCount, Is.EqualTo(1));
                Assert.That(selectedOld.Content, Does.Contain("return x == y;"));
            }
            int visits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { armed = true; }
                if (ReferenceEquals(node, owner.Cast.Inner))
                {
                    visits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            bool shorten = !withLegacy && (constructor || dependency == "path");
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(shorten ? 1 : 0));
            Assert.That(Actual(selected, constructor, true).Cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
            Assert.That(visits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
        }

        private static async Task<NativeGenerator> ControlledGenerator(bool constructor, string? defect, params TypeProvider[] owners)
        {
            var compilation = Compilation(constructor, defect);
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owners));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "lambda-cast-guard", compilation, ["lambda-cast-guard"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            foreach (var root in generator.OutputLibrary.TypeProviders) { root.ProcessTypeForBackCompatibility(); }
            return generator;
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<CastExpressionSyntax>().Single();

        private static (CastExpressionSyntax Cast, string Text) Actual(CodeFile file, bool constructor, bool explicitText)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation(constructor).AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);
            var cast = root.DescendantNodes().OfType<CastExpressionSyntax>().Single();
            Assert.That(model.GetOperation(cast), Is.InstanceOf<IConversionOperation>());
            var conversion = (IConversionOperation)model.GetOperation(cast)!;
            Assert.That(conversion.Conversion.IsIdentity, Is.True);
            Assert.That(conversion.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(cast.Ancestors().OfType<ParenthesizedLambdaExpressionSyntax>().Count(), Is.EqualTo(1));
            ImmutableArray<IArgumentOperation> arguments;
            IMethodSymbol target;
            if (constructor)
            {
                var actual = (IObjectCreationOperation)model.GetOperation(root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single())!;
                Assert.That(actual.Constructor?.ContainingType.Name, Is.EqualTo("Holder"));
                target = actual.Constructor!;
                Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
                var asObject = compilation.ClassifyConversion(target.ContainingType, compilation.GetSpecialType(SpecialType.System_Object));
                Assert.That(asObject.IsImplicit && asObject.IsReference && !asObject.IsBoxing, Is.True);
                arguments = actual.Arguments;
            }
            else
            {
                var actual = (IInvocationOperation)model.GetOperation(root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
                Assert.That(actual.TargetMethod.Name, Is.EqualTo("Capture"));
                target = actual.TargetMethod;
                Assert.That(target.IsStatic, Is.True);
                Assert.That(target.ContainingType.Name, Is.EqualTo("Clock"));
                arguments = actual.Arguments;
            }
            Assert.That(target.Parameters.Length, Is.EqualTo(2));
            var delegateFormal = target.Parameters[0];
            Assert.That(delegateFormal.Name, Is.EqualTo("f"));
            Assert.That(delegateFormal.Type, Is.InstanceOf<INamedTypeSymbol>());
            var func = (INamedTypeSymbol)delegateFormal.Type;
            Assert.That(func.OriginalDefinition.MetadataName, Is.EqualTo("Func`1"));
            Assert.That(func.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(func.ContainingAssembly,
                compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly), Is.True);
            var observer = target.Parameters[1].GetAttributes().Single();
            Assert.That(observer.AttributeClass?.Name, Is.EqualTo("CallerArgumentExpressionAttribute"));
            Assert.That(observer.ConstructorArguments.Single().Value, Is.EqualTo("f"));
            var text = arguments.Single(arg => arg.Parameter?.Name == "text");
            Assert.That(text.IsImplicit, Is.EqualTo(!explicitText));
            Assert.That(text.Value.ConstantValue.HasValue, Is.True);
            var value = (string)text.Value.ConstantValue.Value!;
            if (explicitText) { Assert.That(value, Is.EqualTo("manual")); }
            return (cast, value);
        }

        private static string HolderFor(string? defect) => defect switch
        {
            "different-delegate" => HolderSource.Replace("System.Func<int>", "System.Func<long>", StringComparison.Ordinal),
            "competing" => HolderSource.Replace("public Holder(System.Func<int>",
                "public Holder(System.Action f) { Text = \"other\"; }\npublic Holder(System.Func<int>", StringComparison.Ordinal),
            _ => HolderSource
        };
        private static CSharpCompilation Compilation(bool constructor, string? defect = null) => CSharpCompilation.Create("NativeLambdaCast",
            constructor ? [CSharpSyntaxTree.ParseText(HolderFor(defect), path: "Holder.cs")] : [],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static string[] Execute(CodeFile file, bool constructor, string? defect = null) => PreparedGeneratedRuntime.Invoke(
            Compilation(constructor, defect).AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", [[int.MinValue], [int.MaxValue]])
            .Select(value => value is string direct ? direct : (string)value!.GetType().GetField("Text")!.GetValue(value)!).ToArray();
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
            public CompilerBuildInputCapture Capture => new(directory, "lambda-cast-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }
        private sealed class SerializationCarrier(Action observe) : IReadOnlyList<TypeProvider>
        {
            public int Count { get { observe(); return 0; } }
            public TypeProvider this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
            public IEnumerator<TypeProvider> GetEnumerator() => ((IEnumerable<TypeProvider>)Array.Empty<TypeProvider>()).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class DirectControl : TypeProvider
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
        private sealed class Clock(bool constructor, bool explicitText, string path = "Clock.cs", Action? observePath = null, string? defect = null) : TypeProvider
        {
            internal CastExpression Cast = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                observePath?.Invoke();
                return path;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var value = new ParameterProvider("value", $"", typeof(int));
                Cast = new CastExpression(value, typeof(int));
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(Cast));
                ValueExpression[] actuals = explicitText ? [lambda, Literal("manual")] : [lambda];
                CSharpType result = constructor ? typeof(object) : typeof(string);
                ValueExpression body = constructor
                    ? new NewInstanceExpression(new CSharpType("Holder", "Sample", false, false, null, [], true, false), actuals)
                    : new InvokeMethodExpression(null, "Capture", actuals);
                var entry = new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, [value]), body, this);
                if (constructor) { return [entry]; }
                var f = new ParameterProvider("f", $"", defect == "different-delegate" ? typeof(Func<long>) : typeof(Func<int>));
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: new KeywordExpression("null", null),
                    attributes: new AttributeStatement[] { new(typeof(CallerArgumentExpressionAttribute), Literal("f")) });
                var signature = new MethodSignature("Capture", null, MethodSignatureModifiers.Private | MethodSignatureModifiers.Static, typeof(string), null, [f, text]);
                var callee = new MethodProvider(signature, (VariableExpression)text, this);
                if (defect != "competing") { return [entry, callee]; }
                var action = new ParameterProvider("action", $"", typeof(Action));
                var overload = new MethodSignature("Capture", null, MethodSignatureModifiers.Private | MethodSignatureModifiers.Static, typeof(string), null, [action]);
                return [entry, callee, new MethodProvider(overload, (ValueExpression)Literal("other"), this)];
            }
        }
    }
}

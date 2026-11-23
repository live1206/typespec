// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
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
    internal class PocArgumentRoleTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [Test]
        public Task RetainedOriginalWithoutCaeDoesNotAuthorizeOmittedNativeArgument() =>
            NativeArgumentGroupingPreservesTheActualCallAndCapturedText("retained-unobserved");

        [TestCase("ordinary")]
        [TestCase("siblings")]
        [TestCase("explicit")]
        [TestCase("omitted")]
        [TestCase("disabled")]
        public async Task NativeArgumentGroupingPreservesTheActualCallAndCapturedText(string mode)
        {
            var compilation = Compilation();
            if (mode == "retained-unobserved")
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                    "namespace PriorTypes { internal class PriorCapture { private object Capture(object value, string text = \"\") => text; } }", path: "Prior.cs"));
            }
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode, originalCompilation: mode == "retained-unobserved" ? compilation : null);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-argument-role", compilation, ["task05-argument-role"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode, originalCompilation: mode == "retained-unobserved" ? compilation : null);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            if (mode is "explicit" or "omitted" or "retained-unobserved")
            {
                var textFormals = owner.Methods.Single(method => method.Signature.Name == "Capture").Signature.Parameters;
                Assert.That(textFormals.All(parameter => parameter.HasCachedEmittedDeclarationState), Is.True,
                    "Both actual callee formal spellings are already available before preparation");
                if (mode == "retained-unobserved")
                {
                    var callee = owner.Methods.Single(method => method.Signature.Name == "Capture");
                    var contract = DeclaredCallableContract.FromSignature(callee.Signature, compilation);
                    Assert.That(contract.IsAvailable && contract.IsCurrent(), Is.True);
                    Assert.That(contract.Parameters[1].CallerExpressionTargets.IsEmpty, Is.True);
                    var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
                    var nativeModel = Compilation().AddSyntaxTrees(tree).GetSemanticModel(tree);
                    var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Capture");
                    var actualSymbol = (IMethodSymbol)nativeModel.GetDeclaredSymbol(declaration)!;
                    Assert.That(actualSymbol.Parameters[1].GetAttributes().Single().AttributeClass?.Name, Is.EqualTo("CallerArgumentExpressionAttribute"));
                    TestContext.WriteLine("ARG-RETAINED-CAE actual emitted attribute=True; original observer=False; original contract current=True");
                }
            }
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, referenceMap.ShouldWriteProvider);
            var caller = owner.Methods.Single(method => method.Signature.Name == "M");
            Assert.That(PreparedMethodParameterFrame.TryCapture(caller, caller.BodyExpression!), Is.Null,
                "The explicit parameter frame does not admit argument arithmetic");
            var visited = new List<BinaryOperatorExpression>();
            prepared!.PocNativeWriteObserver = node =>
            {
                if (node is BinaryOperatorExpression binary && owner.Arguments.Any(arg => ReferenceEquals(arg, binary))) { visited.Add(binary); }
            };
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = written.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] values = mode == "siblings" ? [[1, 2, 3, 4], [-4, 2, 0, 8]] : [[1, 2], [-4, 2]];
            Assert.That(Execute(selected, values), Is.EqualTo(Execute(native, values)));
            if (mode is "explicit" or "omitted" or "retained-unobserved")
            {
                var edited = new CodeFile(native.Content.Replace("(x + y)", "x + y", StringComparison.Ordinal), native.Name);
                var nativeConstant = CaptureConstant(native);
                var editedConstant = CaptureConstant(edited);
                var actualNative = Execute(native, values);
                var actualEdited = Execute(edited, values);
                TestContext.WriteLine($"ARG-CAE mode={mode}; nativeConstant={nativeConstant}; editedConstant={editedConstant}; nativeRuntime={actualNative[0]}; editedRuntime={actualEdited[0]}");
                Assert.That(CaptureConstant(selected), Is.EqualTo(nativeConstant));
                AssertActualBoxing(native);
                AssertActualBoxing(selected);
                Assert.That(actualNative, Is.EqualTo(new object?[] { nativeConstant, nativeConstant }));
                Assert.That(actualEdited, Is.EqualTo(new object?[] { editedConstant, editedConstant }));
                // Roslyn strips this outer argument pair from implicit CAE text.
                // Omitted text is still outside the omission policy: assert the actual
                // native pair separately from this compiled constant below.
                string expectedText = mode == "explicit" ? "manual" : "x + y";
                Assert.That(nativeConstant, Is.EqualTo(expectedText));
                Assert.That(editedConstant, Is.EqualTo(expectedText));
            }
            Assert.That(visited.Count, Is.EqualTo(owner.Arguments.Length));
            Assert.That(owner.Arguments.All(arg => visited.Count(node => ReferenceEquals(node, arg)) == 1), Is.True);
            Assert.That(owner.Arguments.All(arg => arg.AddParentheses == (mode != "disabled")), Is.True);
            var receipt = written.SourceConsumption;
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt!.WasCurrentAtEndOfWrite, Is.True, "The candidate must have a current writer receipt");
            string expression = ActualCall(selected);
            string expected = mode switch
            {
                "siblings" => "Echo(a + b, c + d)",
                "explicit" => "Capture(x + y, \"manual\")",
                "omitted" or "retained-unobserved" => "Capture((x + y))",
                _ => "Echo(x + y)"
            };
            int count = mode switch { "siblings" => 2, "ordinary" or "explicit" => 1, _ => 0 };
            TestContext.WriteLine($"ARG-ROLE mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; actual={expression}; visits={visited.Count}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(count));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(count));
                Assert.That(written.PreparedGroupingCount, Is.EqualTo(count));
                Assert.That(expression, Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task CallerFormalShadowsTheUnqualifiedGeneratedCallee()
        {
            var owner = new Clock("shadow");
            var generator = await ControlledGenerator(owner);
            var roots = generator.OutputLibrary.TypeProviders;
            var nativeOwner = new Clock("shadow");
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int visits = 0;
            prepared!.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Arguments[0])) { visits++; } };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(visits, Is.EqualTo(1));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var symbol = compilation.GetSemanticModel(tree).GetSymbolInfo(call.Expression).Symbol;
            var nativeErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id).ToArray();
            var selectedErrors = Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name))
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id).ToArray();
            TestContext.WriteLine($"ARG-SHADOW symbol={symbol?.Kind}; nativeErrors={string.Join(",", nativeErrors)}; selectedErrors={string.Join(",", selectedErrors)}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; selected={ActualCall(selected)}");
            Assert.That(symbol, Is.InstanceOf<IParameterSymbol>());
            Assert.That(nativeErrors, Is.Not.Empty, "The original native call is shadowed, not a valid bound invocation of the private member");
            Assert.That(selectedErrors, Is.EqualTo(nativeErrors));
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.Zero);
                Assert.That(prepared.PocGroupingUses, Is.Zero);
                Assert.That(raw.PreparedGroupingCount, Is.Zero);
                Assert.That(ActualCall(selected), Is.EqualTo("x((x + y))"));
            });
        }

        [TestCase("relational")]
        [TestCase("opaque-list")]
        [TestCase("competing")]
        public async Task UnknownOrCompetingArgumentListsRetainNativeSpelling(string mode)
        {
            var owner = new Clock(mode);
            var generator = await ControlledGenerator(owner);
            var roots = generator.OutputLibrary.TypeProviders;
            var nativeOwner = new Clock(mode);
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
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            if (mode == "relational")
            {
                Assert.That(ActualCall(selected), Is.EqualTo("Echo((a < b), (c + d))"));
                Assert.That(Execute(selected, [[1, 2, 3, 4]]), Is.EqualTo(new object?[] { 7 }));
            }
            else
            {
                Assert.That(ActualCall(selected), Is.EqualTo("Echo((x + y))"));
                Assert.That(Execute(selected, [[1, 2]]), Is.EqualTo(new object?[] { 3 }));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredOriginalListInvalidatesAllSiblingChoices(bool afterOpening)
        {
            var owner = new Clock("siblings");
            var generator = await ControlledGenerator(owner);
            var roots = generator.OutputLibrary.TypeProviders;
            var nativeOwner = new Clock("siblings");
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
            int mutations = 0;
            int visits = 0;
            void Restore()
            {
                var original = owner.Call.Arguments;
                owner.Call.Update(arguments: original.Reverse().ToArray());
                owner.Call.Update(arguments: original);
                mutations++;
            }
            prepared.PocNativeWriteObserver = node =>
            {
                if (!afterOpening && ReferenceEquals(node, owner.Call)) { Restore(); }
                if (owner.Arguments.Any(arg => ReferenceEquals(arg, node))) { visits++; }
            };
            if (afterOpening) { prepared.PocAfterOpening = Restore; }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(mutations, Is.EqualTo(1));
            Assert.That(visits, Is.EqualTo(2));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(afterOpening ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            Assert.That(ActualCall(selected), Is.EqualTo("Echo((a + b), (c + d))"));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            Assert.That(Execute(selected, [[1, 2, 3, 4]]), Is.EqualTo(new object?[] { 10 }));
        }

        [TestCase("siblings", "stable")]
        [TestCase("explicit", "stable")]
        [TestCase("siblings", "source-expiry")]
        [TestCase("explicit", "result-expiry")]
        public async Task CompilerArgumentPipelineUsesActualSelectedFilesAndResult(string shape, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-argument-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("ArgumentCompilerInputs")));
                var owner = new Clock(shape, path: "alias/Clock.cs");
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                bool sibling = shape == "siblings";
                int expectedUses = sibling ? 2 : 1;
                string shortened = sibling ? "Echo(a + b, c + d)" : "Capture(x + y, \"manual\")";
                CodeFile? selected = null;
                CompleteSourceInputSnapshot? proof = null;
                int visits = 0;
                int uses = 0;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(expectedUses));
                        proof = generator.CompilerBuildContext?.SourceProof;
                        Assert.That(proof, Is.Not.Null);
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (owner.Arguments.Any(arg => ReferenceEquals(arg, node))) { visits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(ActualCall(file), Is.EqualTo(shortened));
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
                Assert.That(uses, Is.EqualTo(expectedUses));
                Assert.That(visits, Is.EqualTo(expectedUses));
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
                Assert.That(ActualCall(selected), Is.EqualTo(mode == "source-expiry" ? "Echo((a + b), (c + d))" : shortened));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires discard of isolated output even after short selection");
                var nativeOwner = new Clock(shape, path: "alias/Clock.cs");
                nativeOwner.ProcessTypeForBackCompatibility();
                var native = new TypeProviderWriter(nativeOwner).Write();
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Content, final.Content);
                object?[][] args = sibling ? [[1, 2, 3, 4], [-4, 2, 0, 8]] : [[1, 2], [-4, 2]];
                Assert.That(Execute(selected, args), Is.EqualTo(Execute(native, args)));
                Assert.That(Execute(final, args), Is.EqualTo(Execute(native, args)));
                if (!sibling) { AssertActualBoxing(selected); AssertActualBoxing(final); }
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task ArgumentProofDoesNotForceASecondColdDependency(string dependency, bool withLegacy)
        {
            var early = new Lazy<int>(() => 0);
            var late = new Lazy<int>(() => 0);
            bool armed = false;
            void Observe()
            {
                _ = early.Value;
                if (armed) { _ = late.Value; }
            }
            var owner = new Clock("ordinary", observePath: dependency == "path" ? Observe : null);
            if (dependency == "serialization") { owner.Update(serializations: new SerializationCarrier(Observe)); }
            var control = new DirectControl();
            var generator = await ControlledGenerator(withLegacy ? [control, owner] : [owner]);
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
                if (ReferenceEquals(node, owner.Call)) { armed = true; }
                if (ReferenceEquals(node, owner.Arguments[0]))
                {
                    visits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            // Actual path builders are supported. An unowned serialization
            // carrier or an extra written root remains out of this call proof.
            bool shorten = dependency == "path" && !withLegacy;
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(shorten ? 1 : 0));
            Assert.That(ActualCall(selected), Is.EqualTo(shorten ? "Echo(x + y)" : "Echo((x + y))"));
            Assert.That(visits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
        }

        private static async Task<NativeGenerator> ControlledGenerator(params TypeProvider[] owners)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owners));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "argument-guard", compilation, ["argument-guard"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            foreach (var root in generator.OutputLibrary.TypeProviders) { root.ProcessTypeForBackCompatibility(); }
            return generator;
        }

        private static string CaptureConstant(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var model = Compilation().AddSyntaxTrees(tree).GetSemanticModel(tree);
            var operation = (IInvocationOperation)model.GetOperation(call)!;
            var constant = operation.Arguments.Single(argument => argument.Parameter?.Name == "text").Value.ConstantValue;
            Assert.That(constant.HasValue, Is.True);
            return (string)constant.Value!;
        }
        private static void AssertActualBoxing(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
            Assert.That(operation.TargetMethod.Name, Is.EqualTo("Capture"));
            var argument = operation.Arguments.Single(value => value.Parameter?.Ordinal == 0);
            Assert.That(argument.Value, Is.InstanceOf<IConversionOperation>());
            var conversion = (IConversionOperation)argument.Value;
            Assert.That(conversion.IsImplicit, Is.True);
            Assert.That(conversion.Operand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(conversion.Type?.SpecialType, Is.EqualTo(SpecialType.System_Object));
            Assert.That(compilation.ClassifyConversion(conversion.Operand.Type!, conversion.Type!).IsBoxing, Is.True);
        }
        private static string ActualCall(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "M")
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single().NormalizeWhitespace().ToFullString();
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("NativeArgumentRole",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        private static object?[] Execute(CodeFile file, object?[][] values) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", values);
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
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
        private sealed class OpaqueArguments(ValueExpression[] arguments) : IReadOnlyList<ValueExpression>
        {
            public int Count => arguments.Length;
            public ValueExpression this[int index] => arguments[index];
            public IEnumerator<ValueExpression> GetEnumerator() => ((IEnumerable<ValueExpression>)arguments).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "argument-captured", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }
        private sealed class Clock(string mode, string path = "Clock.cs", Action? observePath = null, Compilation? originalCompilation = null) : TypeProvider
        {
            internal BinaryOperatorExpression[] Arguments = [];
            internal InvokeMethodExpression Call = null!;
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
                bool sibling = mode is "siblings" or "relational";
                bool capture = mode is "explicit" or "omitted" or "retained-unobserved";
                var names = sibling ? new[] { "a", "b", "c", "d" } : ["x", "y"];
                var formals = names.Select(name => new ParameterProvider(name, $"", typeof(int))).ToArray();
                Arguments = sibling
                    ? [new BinaryOperatorExpression(mode == "relational" ? "<" : "+", formals[0], formals[1]), new BinaryOperatorExpression("+", formals[2], formals[3])]
                    : [new BinaryOperatorExpression("+", formals[0], formals[1], mode != "disabled")];
                var calleeName = capture ? "Capture" : mode == "shadow" ? "x" : "Echo";
                ValueExpression[] actuals = mode == "explicit" ? [Arguments[0], Literal("manual")] : Arguments;
                Call = new InvokeMethodExpression(null, calleeName, mode == "opaque-list" ? new OpaqueArguments(actuals) : actuals);
                CSharpType result = capture ? typeof(object) : typeof(int);
                var entry = new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, formals), Call, this);
                if (capture)
                {
                    var value = new ParameterProvider("value", $"", typeof(object));
                    var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                        attributes: new AttributeStatement[] { new(typeof(CallerArgumentExpressionAttribute), Literal("value")) });
                    var signature = new MethodSignature(calleeName, null, MethodSignatureModifiers.Private, result, null, [value, text]);
                    if (originalCompilation is not null)
                    {
                        var prior = originalCompilation.GetTypeByMetadataName("PriorTypes.PriorCapture")!.GetMembers("Capture").OfType<IMethodSymbol>().Single();
                        signature.RetainOriginalSymbol(prior, originalCompilation);
                        value.RetainOriginalSymbol(prior.Parameters[0], originalCompilation);
                        text.RetainOriginalSymbol(prior.Parameters[1], originalCompilation);
                    }
                    return [entry, new MethodProvider(signature, (VariableExpression)text, this)];
                }
                else
                {
                    var left = new ParameterProvider("left", $"", mode == "relational" ? typeof(bool) : typeof(int));
                    var right = new ParameterProvider("right", $"", typeof(int));
                    var signature = new MethodSignature(calleeName, null, MethodSignatureModifiers.Private, result, null, sibling ? [left, right] : [left]);
                    ValueExpression body = mode == "relational" ? (VariableExpression)right :
                        sibling ? new BinaryOperatorExpression("+", left, right, AddParentheses: false) : (VariableExpression)left;
                    var callee = new MethodProvider(signature, body, this);
                    if (mode != "competing") { return [entry, callee]; }
                    var alternative = new ParameterProvider("other", $"", typeof(long));
                    var overload = new MethodSignature(calleeName, null, MethodSignatureModifiers.Private, result, null, [alternative]);
                    return [entry, callee, new MethodProvider(overload, (ValueExpression)Literal(-100), this)];
                }
            }
        }
    }
}

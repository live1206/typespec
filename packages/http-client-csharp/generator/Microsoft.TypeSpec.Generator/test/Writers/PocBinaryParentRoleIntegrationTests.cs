// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocBinaryParentRoleIntegrationTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task OriginalBareArithmeticRootUsesOnlyItsChildPair(bool multiply, bool selectOmission)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration);
            var owner = new BareArithmeticOwner(multiply);
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(Compilation("BareArithmeticInputs", []), null);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new BareArithmeticOwner(multiply);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            List<object> visits = [];
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Root) || ReferenceEquals(node, owner.Child) ||
                    Array.Exists(owner.Parameters, parameter => ReferenceEquals(node, parameter))) { visits.Add(node); }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = raw.SelectPreparedForms(false, false, false);
            var selected = raw.SelectPreparedForms(true, true, selectOmission && prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(native.Name, Is.EqualTo(selected.Name));
            Assert.That(native.Name, Is.EqualTo(qualified.Name));
            Assert.That(qualified.Content, Is.EqualTo(native.Content));
            if (!selectOmission) { Assert.That(selected.Content, Is.EqualTo(native.Content)); }
            object[] expectedVisits = multiply ? [owner.Root, owner.Parameters[0], owner.Child, owner.Parameters[1], owner.Parameters[2]] :
                [owner.Root, owner.Child, owner.Parameters[0], owner.Parameters[1], owner.Parameters[2]];
            Assert.That(visits, Has.Count.EqualTo(expectedVisits.Length));
            for (int i = 0; i < expectedVisits.Length; i++) { Assert.That(visits[i], Is.SameAs(expectedVisits[i])); }
            Assert.That(owner.Root.AddParentheses, Is.False);
            Assert.That(owner.Child.AddParentheses, Is.True);

            var tree = CSharpSyntaxTree.ParseText(native.Content, path: native.Name);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            string retained = multiply ? "x + (y * z)" : "(x - y) - z";
            Assert.That(method.ExpressionBody!.Expression.NormalizeWhitespace().ToFullString(), Is.EqualTo(retained));
            var model = Compilation("BareArithmeticBinding", [tree]).GetSemanticModel(tree);
            var parameters = model.GetDeclaredSymbol(method)!.Parameters;
            var leaves = method.ExpressionBody.Expression.DescendantNodes().OfType<IdentifierNameSyntax>().ToArray();
            Assert.That(leaves, Has.Length.EqualTo(3));
            for (int i = 0; i < leaves.Length; i++)
            { Assert.That(SymbolEqualityComparer.Default.Equals(parameters[i], model.GetSymbolInfo(leaves[i]).Symbol), Is.True); }
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = multiply ? [[10, 2, 3], [int.MaxValue, 2, 2]] : [[10, 7, 3], [int.MinValue, 1, 1]];
            object?[] expected = multiply ? [16, int.MinValue + 3] : [0, int.MaxValue - 1];
            Assert.That(Execute(native, arguments), Is.EqualTo(expected));
            Assert.That(Execute(selected, arguments), Is.EqualTo(expected));
            var wrong = new CodeFile("namespace Sample; public class Clock { public int M(int x, int y, int z) => " +
                (multiply ? "(x + y) * z" : "x - (y - z)") + "; }", native.Name);
            Assert.That(Execute(wrong, [arguments[0]]).Single(), Is.EqualTo(multiply ? 36 : 6));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, wrong.Content));
            string selectedExpression = PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace().ToFullString();
            Assert.That(selectedExpression, Is.EqualTo(selectOmission ? multiply ? "x + y * z" : "x - y - z" : retained));
            if (selectOmission)
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(1));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(1));
            }
        }

        [TestCase(false, "restored")]
        [TestCase(true, "restored")]
        [TestCase(false, "other-side")]
        [TestCase(true, "other-side")]
        [TestCase(false, "bare-child")]
        [TestCase(true, "alias-carrier")]
        [TestCase(true, "newline")]
        public async Task BareArithmeticUnknownOrRestoredChildrenUseNative(bool multiply, string mode)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration);
            var owner = new BareArithmeticOwner(multiply, mode);
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(Compilation("BareArithmeticVeto", []), null);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new BareArithmeticOwner(multiply, mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            bool restore = mode == "restored";
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(restore ? 1 : 0));
            int openings = 0;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                var declaration = owner.Parameters[1].Declaration;
                string name = declaration.RequestedName;
                declaration.Update("other");
                declaration.Update(name);
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(openings, Is.EqualTo(restore ? 1 : 0));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
                Assert.That(raw.Content, Does.Contain(multiply ? "=> x + y * z;" : "=> x - y - z;"));
            }
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            Assert.That(allowed, Is.EqualTo(!restore));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            if (mode == "other-side") { Assert.That(selected.Content, Does.Contain(multiply ? "=> (x * y) + z;" : "=> x - (y - z);")); }
            if (mode == "newline") { Assert.That(selected.Content, Does.Contain("y\n")); }
            Assert.That(Execute(selected, [[10, 2, 3]]), Is.EqualTo(Execute(native, [[10, 2, 3]])));
        }

        [TestCase("stable")]
        [TestCase("child")]
        [TestCase("host")]
        public async Task ActualPipelineSelectsNestedGroupingIndependentlyFromFieldNames(string mode)
        {
            string directory = Path.Combine(Path.GetTempPath(), "task05-binary-parent-pipeline", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
                var owner = new NestedOwner(false, false, false);
                var widget = new Widget();
                var consumer = new Consumer(widget.Type);
                var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                    new TestOutputLibrary(owner, consumer, widget));
                CodeModelGenerator.Instance = generator;
                var selected = new Dictionary<string, CodeFile>();
                var visits = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                int uses = 0;
                int openings = 0;
                int mutations = 0;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
                        prepared.PocNativeWriteObserver = node =>
                        {
                            if (ReferenceEquals(node, owner.Outer) || ReferenceEquals(node, owner.Inner))
                            {
                                visits[node] = visits.GetValueOrDefault(node) + 1;
                            }
                        };
                        prepared.PocAfterOpening = () =>
                        {
                            openings++;
                            if (mode == "host") { generator.SourceCurrent = false; mutations++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        if (!ReferenceEquals(provider, owner)) { return; }
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Content, Does.Contain("return (x + y * z);"));
                        Assert.That(file.QualifiedContent, Does.Contain("return (x + (y * z));"));
                        if (mode == "child") { owner.Leaf!.Update(type: typeof(long)); mutations++; }
                    },
                    PocSelectedFileObserved = file => selected.Add(file.Name, file)
                };
                await pipeline.ExecuteAsync();
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(openings, Is.EqualTo(1));
                Assert.That(mutations, Is.EqualTo(mode == "stable" ? 0 : 1));
                Assert.That(visits[owner.Outer!], Is.EqualTo(1));
                Assert.That(visits[owner.Inner!], Is.EqualTo(1));
                Assert.That(owner.Outer!.AddParentheses && owner.Inner!.AddParentheses, Is.True);
                Assert.That(selected["Clock.cs"].Content,
                    Does.Contain(mode == "stable" ? "return (x + y * z);" : "return (x + (y * z));"));
                Assert.That(selected["Consumer.cs"].Content,
                    Does.Contain(mode == "host" ? "private global::Sample.Widget _value;" : "private Widget _value;"));
                object?[][] arguments = [[10, 7, 3], [-10, 7, 3], [0, 0, 9]];
                var selectedCompilation = Compilation("SelectedBinaryParents", selected.Values.Select(file =>
                    CSharpSyntaxTree.ParseText(file.Content, path: file.Name)));
                var runtime = PreparedGeneratedRuntime.Invoke(selectedCompilation, "Sample.Clock", "M", arguments);
                Assert.That(runtime, Is.EqualTo(new object?[] { 31, 11, 0 }));
                Assert.That(File.Exists(Path.Combine(directory, "Clock.cs")), Is.True);
                var published = Compilation("PublishedBinaryParents", Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)));
                Assert.That(PreparedGeneratedRuntime.Invoke(published, "Sample.Clock", "M", arguments), Is.EqualTo(runtime));
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ObservedArithmeticLambdaPreservesActualNativeCapturedText(bool arrow)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration);
            var owner = new NestedOwner(false, arrow, true);
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(Compilation("ObservedCustomization", []), null);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new NestedOwner(false, arrow, true);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared!.PocGroupingCount, Is.Zero);
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(file.Content, Does.Contain("(x + (y * z))"));
            string captured = CapturedText(native.Content);
            Assert.That(captured, Does.Contain("(x + (y * z))"));
            Assert.That(CapturedText(file.Content), Is.EqualTo(captured));
            PreparedParenthesesTests.AssertOperations(native.Content, file.Content);
            Assert.That(Execute(file, [[10, 7, 3]]).Single(), Is.EqualTo(captured));
            Assert.That(owner.Outer!.AddParentheses && owner.Inner!.AddParentheses, Is.True);
            string unsafeText = native.Content.Replace("(y * z)", "y * z", StringComparison.Ordinal);
            Assert.That(unsafeText, Is.Not.EqualTo(native.Content));
            Assert.That(CapturedText(unsafeText), Is.Not.EqualTo(captured));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
        }

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("before", false)]
        [TestCase("before", true)]
        [TestCase("nested-opening", false)]
        [TestCase("nested-opening", true)]
        [TestCase("after", false)]
        [TestCase("after", true)]
        [TestCase("host", false)]
        [TestCase("host", true)]
        public async Task NestedChangesUseSamePassNativeCompanionWithoutReplay(string mode, bool arrow)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration);
            var owner = new NestedOwner(true, arrow, false);
            var generator = new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(Compilation("MutationCustomization", []), null);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new NestedOwner(true, arrow, false);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
            int openings = 0;
            int mutations = 0;
            void Mutate()
            {
                owner.Leaf!.Update(type: typeof(long));
                mutations++;
            }
            prepared.PocAfterOpening = () =>
            {
                openings++;
                if (mode == "nested-opening" && openings == 2) { Mutate(); }
            };
            if (mode == "host")
            {
                generator.OnSourceCheck = () =>
                {
                    if (openings > 0 && mutations == 0) { Mutate(); }
                    return true;
                };
            }
            var visits = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            prepared.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Outer) || ReferenceEquals(node, owner.Inner))
                {
                    visits[node] = visits.GetValueOrDefault(node) + 1;
                }
            };
            if (mode == "before") { Mutate(); }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            if (mode == "after")
            {
                Assert.That(prepared.CanCommitPocGrouping(raw), Is.True);
                Mutate();
            }
            int expected = mode == "before" ? 0 : mode == "host" ? 1 : 2;
            Assert.That(openings, Is.EqualTo(expected));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
            Assert.That(mutations, Is.EqualTo(mode == "stable" ? 0 : 1));
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            if (expected > 0) { Assert.That(allowed, Is.EqualTo(mode == "stable")); }
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Content, Does.Contain(mode == "stable"
                ? arrow ? "=> x + y == z;" : "return x + y == z;"
                : arrow ? "=> ((x + y) == z);" : "return ((x + y) == z);"));
            if (mode != "stable")
            {
                Assert.That(selected.Content, Is.EqualTo(raw.QualifiedContent));
                Assert.That(selected.Content, Is.EqualTo(native.Content));
            }
            Assert.That(visits[owner.Outer!], Is.EqualTo(1));
            Assert.That(visits[owner.Inner!], Is.EqualTo(1));
            Assert.That(owner.Outer!.AddParentheses && owner.Inner!.AddParentheses, Is.True);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            Assert.That(Execute(selected, [[10, 7, 3], [2, 1, 3]]), Is.EqualTo(Execute(native, [[10, 7, 3], [2, 1, 3]])));
            Assert.That(visits[owner.Outer!], Is.EqualTo(1));
            Assert.That(visits[owner.Inner!], Is.EqualTo(1));
        }

        private static string CapturedText(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = Compilation("CapturedBinaryParents", [tree]);
            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            Assert.That(syntax.ArgumentList.Arguments.Count, Is.EqualTo(1));
            var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
            var argument = operation.Arguments.Single(value => value.Parameter!.Name == "text");
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            return (string)argument.Value.ConstantValue.Value!;
        }

        private static CSharpCompilation Compilation(string name, IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(name,
            trees, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation("ExecutedBinaryParents", [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)]), "Sample.Clock", "M", arguments);

        private sealed class PipelineGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            internal bool SourceCurrent { get; set; } = true;
            internal Func<bool>? OnSourceCheck { get; set; }
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
            public override SourceInputModel SourceInputModel
            {
                get => base.SourceInputModel;
                internal set
                {
                    base.SourceInputModel = value;
                    if (value.Customization is CSharpCompilation compilation)
                    {
                        value.PreEmissionSourceInputs = new CompleteSourceInputSnapshot(Configuration.OutputDirectory,
                            "task05-parent-role-pipeline", compilation, ["controlled-parent-role-pipeline-inputs"],
                            () => OnSourceCheck?.Invoke() ?? SourceCurrent);
                    }
                }
            }
        }

        internal sealed class BareArithmeticOwner(bool multiply, string mode = "ordinary") : TypeProvider
        {
            internal BinaryOperatorExpression Root { get; private set; } = null!;
            internal BinaryOperatorExpression Child { get; private set; } = null!;
            internal VariableExpression[] Parameters { get; private set; } = [];
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Parameters = [x, y, z];
                var first = mode == "alias-carrier" ? new VariableExpression(typeof(int), Parameters[0].Declaration) : Parameters[0];
                if (mode == "newline") { Parameters[1].Declaration.Update("y\n"); }
                bool leftGrouped = multiply == (mode == "other-side");
                Child = leftGrouped ? new BinaryOperatorExpression(multiply ? "*" : "-", first, Parameters[1], mode != "bare-child") :
                    new BinaryOperatorExpression(multiply ? "*" : "-", Parameters[1], Parameters[2], mode != "bare-child");
                Root = leftGrouped ? new BinaryOperatorExpression(multiply ? "+" : "-", Child, Parameters[2], AddParentheses: false) :
                    new BinaryOperatorExpression(multiply ? "+" : "-", first, Child, AddParentheses: false);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(int), null, [x, y, z]), Root, this)];
            }
        }

        private sealed class NestedOwner(bool equality, bool arrow, bool captured) : TypeProvider
        {
            internal BinaryOperatorExpression? Outer { get; private set; }
            internal BinaryOperatorExpression? Inner { get; private set; }
            internal VariableExpression? Leaf { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Leaf = equality ? x : z;
                Inner = equality ? new BinaryOperatorExpression("+", Leaf, y) : new BinaryOperatorExpression("*", y, Leaf);
                Outer = equality ? new BinaryOperatorExpression("==", Inner, z) : new BinaryOperatorExpression("+", x, Inner);
                CSharpType result = captured ? typeof(object) : equality ? typeof(bool) : typeof(int);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null,
                    new ParameterProvider[] { x, y, z });
                if (!captured)
                {
                    return [arrow ? new MethodProvider(signature, Outer, this) : new MethodProvider(signature, Return(Outer), this)];
                }
                var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(), Return(Outer));
                var capture = new InvokeMethodExpression(null, "Capture", new ValueExpression[] { lambda });
                var text = new ParameterProvider("text", $"", typeof(string), defaultValue: Literal(""),
                    attributes: new AttributeStatement[] { new AttributeStatement(typeof(CallerArgumentExpressionAttribute), Literal("value")) });
                var captureSignature = new MethodSignature("Capture", null, MethodSignatureModifiers.Private, typeof(object), null,
                    new ParameterProvider[] { new ParameterProvider("value", $"", typeof(Func<int>)), text });
                return [arrow ? new MethodProvider(signature, capture, this) : new MethodProvider(signature, Return(capture), this),
                    new MethodProvider(captureSignature, (VariableExpression)text, this)];
            }
        }

        private sealed class Widget : TypeProvider
        {
            protected override string BuildName() => "Widget";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Widget.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class Consumer(CSharpType target) : TypeProvider
        {
            protected override string BuildName() => "Consumer";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Consumer.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, target, "_value", this)];
        }
    }
}

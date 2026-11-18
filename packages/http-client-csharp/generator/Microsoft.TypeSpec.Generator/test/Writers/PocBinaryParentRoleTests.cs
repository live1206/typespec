// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocBinaryParentRoleTests
    {
        [TestCase("direct", false)]
        [TestCase("direct", true)]
        [TestCase("leftSub", false)]
        [TestCase("leftSub", true)]
        [TestCase("higherPrecedence", false)]
        [TestCase("higherPrecedence", true)]
        [TestCase("equalityLeft", false)]
        [TestCase("equalityLeft", true)]
        [TestCase("hintAlias", false)]
        [TestCase("hintAlias", true)]
        [TestCase("equalityRight", false)]
        [TestCase("equalityRight", true)]
        [TestCase("leftChain", false)]
        [TestCase("leftChain", true)]
        [TestCase("sharedEdges", false)]
        [TestCase("sharedEdges", true)]
        [TestCase("leftMultiplication", false)]
        [TestCase("leftMultiplication", true)]
        [TestCase("disabledChild", false)]
        [TestCase("disabledChild", true)]
        [TestCase("rightSub", false)]
        [TestCase("rightSub", true)]
        [TestCase("rightAdd", false)]
        [TestCase("rightAdd", true)]
        public async Task NativeBinaryChildRolesPreserveOperations(string kind, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new BinaryOwner(kind, arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            var observations = new List<string>();
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-binary-parent-roles", compilation, ["controlled-parent-role-inputs"],
                    () => { observations.Add("host"); return true; })
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new BinaryOwner(kind, arrow);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            var flags = owner.Expressions.Select(expression => expression.AddParentheses).ToArray();
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            int beforeWrite = observations.Count;
            var visited = new List<object>();
            prepared!.PocNativeWriteObserver = node => { visited.Add(node); observations.Add(node.GetType().Name); };
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written);
            var selected = written.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            var arguments = new object?[][]
            {
                Inputs(kind, 10, 7, 3, 6),
                Inputs(kind, int.MaxValue, 1, 1, -1),
                Inputs(kind, -10, -7, -3, 6)
            };
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
            int expectedUses = kind switch
            {
                "rightSub" or "rightAdd" or "leftMultiplication" or "disabledChild" => 0,
                "equalityLeft" or "equalityRight" or "hintAlias" or "leftChain" or "sharedEdges" => 2,
                _ => 1
            };
            int expectedBinaryVisits = kind == "sharedEdges" ? 5 : kind == "direct" ? 1 : kind == "leftChain" ? 3 : 2;
            string expected = kind switch
            {
                "direct" => "x == y",
                "leftSub" => "(x - y - z)",
                "higherPrecedence" => "(x + y * z)",
                "equalityLeft" => "x + y == z",
                "equalityRight" => "x == y + z",
                "hintAlias" => "x + y == z",
                "leftChain" => "(x - y - z - w)",
                "sharedEdges" => "(x - y - z) == (z - (x - y))",
                "leftMultiplication" => "((x * y) + z)",
                "disabledChild" => "(x + y == z)",
                "rightSub" => "(x - (y - z))",
                _ => "(x + (y + z))"
            };
            TestContext.WriteLine($"PARENT-ROLE kind={kind}; arrow={arrow}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; physical={written.PreparedGroupingCount}; binaryVisits={visited.OfType<BinaryOperatorExpression>().Count()}; receipt={written.SourceConsumption?.WasCurrentAtEndOfWrite}; hostCallsBeforeWrite={beforeWrite}; hostCallsTotal={observations.Count(s => s == "host")}");
            TestContext.WriteLine("PARENT-ORDER " + string.Join(",", observations));
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expectedUses));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUses));
                Assert.That(written.PreparedGroupingCount, Is.EqualTo(expectedUses));
                Assert.That(visited.OfType<BinaryOperatorExpression>().Count(), Is.EqualTo(expectedBinaryVisits));
                Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace().ToFullString(), Is.EqualTo(expected));
                Assert.That(owner.Expressions.Select(expression => expression.AddParentheses), Is.EqualTo(flags));
                Assert.That(owner.Expressions.Count(expression => !expression.AddParentheses), Is.EqualTo(kind == "disabledChild" ? 1 : 0));
            });
        }

        [Test]
        public async Task RightSubtractionOracleRejectsReassociation()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            var nativeOwner = new BinaryOwner("rightSub", false);
            CodeModelGenerator.Instance = new NativeGenerator(new GeneratorContext(mock.Object.Configuration),
                mock.Object.InputLibrary, new TestOutputLibrary(nativeOwner))
            {
                SourceInputModel = new SourceInputModel(Compilation(), null)
            };
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            string unsafeText = native.Content.Replace("(x - (y - z))", "(x - y - z)", StringComparison.Ordinal);
            Assert.That(unsafeText, Is.Not.EqualTo(native.Content));
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(native.Content, unsafeText));
            var unsafeFile = new CodeFile(unsafeText, native.Name);
            Assert.That(Execute(native, [new object?[] { 10, 7, 3 }]).Single(), Is.EqualTo(6));
            Assert.That(Execute(unsafeFile, [new object?[] { 10, 7, 3 }]).Single(), Is.EqualTo(0));
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public async Task NewRecognitionCountsSharedOccurrencesAndReturnFraming(bool arrow, bool expected)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            CodeModelGenerator.Instance = mock.Object;
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            ValueExpression SharedTree(ValueExpression leaf, int depth)
            {
                if (depth == 0) { return leaf; }
                var child = SharedTree(leaf, depth - 1);
                return new BinaryOperatorExpression("-", child, child);
            }
            var binary = new BinaryOperatorExpression("==", SharedTree(x, 5), SharedTree(y, 5));
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                new ParameterProvider[] { x, y });
            var owner = new BinaryOwner("direct", arrow);
            var method = arrow ? new MethodProvider(signature, binary, owner) : new MethodProvider(signature, Return(binary), owner);
            object body = (object?)method.BodyStatements ?? method.BodyExpression!;
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(method, body, out _), Is.EqualTo(expected));
            var larger = new BinaryOperatorExpression("==", SharedTree(x, 6), y);
            var largeMethod = new MethodProvider(signature, larger, owner);
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(largeMethod, larger, out _), Is.False);
        }

        [TestCase(22, true)]
        [TestCase(23, false)]
        public async Task NewRecognitionHonorsItsLocalDepthBoundary(int depth, bool expected)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            CodeModelGenerator.Instance = mock.Object;
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            ValueExpression chain = x;
            for (int i = 0; i < depth; i++) { chain = new BinaryOperatorExpression("-", chain, y); }
            var binary = new BinaryOperatorExpression("==", chain, y);
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                new ParameterProvider[] { x, y });
            var method = new MethodProvider(signature, binary, new BinaryOwner("direct", true));
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(method, binary, out _), Is.EqualTo(expected));
        }

        [Test]
        public async Task NewSourceFrameDoesNotMaterializeColdFormalDeclarations()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            CodeModelGenerator.Instance = mock.Object;
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            var binary = new BinaryOperatorExpression("==",
                new BinaryOperatorExpression("+", new VariableExpression(typeof(int), "x"), new VariableExpression(typeof(int), "y")),
                new VariableExpression(typeof(int), "x"));
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                new ParameterProvider[] { x, y });
            var method = new MethodProvider(signature, binary, new BinaryOwner("direct", true), xmlDocProvider: XmlDocProvider.Empty);
            Assert.That(x.HasCachedEmittedDeclarationState || y.HasCachedEmittedDeclarationState, Is.False);
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(method, binary, out _), Is.True);
            Assert.That(x.HasCachedEmittedDeclarationState || y.HasCachedEmittedDeclarationState, Is.False);
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, binary), Is.Null);
            Assert.That(x.HasCachedEmittedDeclarationState || y.HasCachedEmittedDeclarationState, Is.False);
            Assert.That(PreparedMethodParameterFrame.TryCapture(method, binary), Is.Null,
                "The explicit snapshot API must not gain the source-only grammar");
        }

        [TestCase("null")]
        [TestCase("unknown")]
        [TestCase("formal-type")]
        [TestCase("carrier")]
        public async Task NewRecognitionRefusesUnavailableFactsWithoutWriting(string kind)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            CodeModelGenerator.Instance = mock.Object;
            CSharpType parameterType = kind == "formal-type" ? typeof(double) : typeof(int);
            var x = new ParameterProvider("x", $"", parameterType);
            var y = new ParameterProvider("y", $"", parameterType);
            ValueExpression left = x;
            ValueExpression? operand = kind == "null" ? null : kind == "unknown" ? new ForbiddenExpression() : y;
            var binary = new BinaryOperatorExpression("==", new BinaryOperatorExpression("+", left, operand!), y);
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                new ParameterProvider[] { x, y });
            var method = new MethodProvider(signature, binary, new BinaryOwner("direct", true), xmlDocProvider: XmlDocProvider.Empty);
            var carrier = new UnreadableParameters();
            if (kind == "carrier") { signature.Parameters = carrier; }
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(method, binary, out _), Is.False);
            Assert.That(PreparedMethodParameterFrame.TryCaptureForSource(method, binary), Is.Null);
            Assert.That(carrier.Reads, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NewBinaryLeavesRequireUniqueActualFormalCorrespondence(bool duplicate)
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync();
            CodeModelGenerator.Instance = mock.Object;
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            VariableExpression actualX = x;
            VariableExpression actualY = y;
            var unowned = new VariableExpression(typeof(int), "x");
            var binary = new BinaryOperatorExpression("==", new BinaryOperatorExpression("+", duplicate ? actualX : unowned, actualY), actualY);
            var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                duplicate ? new ParameterProvider[] { x, x, y } : new ParameterProvider[] { x, y });
            var method = new MethodProvider(signature, binary, new BinaryOwner("direct", true), xmlDocProvider: XmlDocProvider.Empty);
            Assert.That(PreparedBinaryParentRoles.TryGetNewRoot(method, binary, out _), Is.True);
            var frame = PreparedMethodParameterFrame.TryCaptureForSource(method, binary);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Resolve(duplicate ? actualX : unowned), Is.Null,
                "A matching name or advertised type does not establish emitted-parameter ownership");
        }

        private sealed record ForbiddenExpression : ValueExpression
        {
            internal override void WriteCore(CodeWriter writer) => throw new InvalidOperationException("Unknown node must not be written to discover grouping");
        }

        private sealed class UnreadableParameters : IReadOnlyList<ParameterProvider>
        {
            internal int Reads;
            public int Count { get { Reads++; throw new InvalidOperationException("Unknown Count"); } }
            public ParameterProvider this[int index] { get { Reads++; throw new InvalidOperationException("Unknown indexer"); } }
            public IEnumerator<ParameterProvider> GetEnumerator() { Reads++; throw new InvalidOperationException("Unknown enumeration"); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static object?[] Inputs(string kind, int x, int y, int z, int w) => kind == "direct" ? [x, y] : kind == "leftChain" ? [x, y, z, w] : [x, y, z];

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("Task05BinaryParents",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", arguments);

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class BinaryOwner(string kind, bool arrow) : TypeProvider
        {
            internal List<BinaryOperatorExpression> Expressions { get; } = [];
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var parameters = (kind == "direct" ? new[] { "x", "y" } : kind == "leftChain" ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" })
                    .Select(name => new ParameterProvider(name, $"", typeof(int))).ToArray();
                ValueExpression x = parameters[0];
                ValueExpression y = parameters[1];
                if (kind == "hintAlias") { ((VariableExpression)x).Update(type: typeof(bool)); }
                ValueExpression z = parameters.Length > 2 ? parameters[2] : ValueExpression.Empty;
                BinaryOperatorExpression Binary(string op, ValueExpression left, ValueExpression right, bool parentheses = true)
                {
                    var binary = new BinaryOperatorExpression(op, left, right, parentheses);
                    Expressions.Add(binary);
                    return binary;
                }
                BinaryOperatorExpression SharedEdges()
                {
                    var shared = Binary("-", x, y);
                    return Binary("==", Binary("-", shared, z), Binary("-", z, shared));
                }
                var body = kind switch
                {
                    "direct" => Binary("==", x, y),
                    "leftSub" => Binary("-", Binary("-", x, y), z),
                    "higherPrecedence" => Binary("+", x, Binary("*", y, z)),
                    "equalityLeft" => Binary("==", Binary("+", x, y), z),
                    "equalityRight" => Binary("==", x, Binary("+", y, z)),
                    "hintAlias" => Binary("==", Binary("+", x, y), z),
                    "leftChain" => Binary("-", Binary("-", Binary("-", x, y), z), parameters[3]),
                    "sharedEdges" => SharedEdges(),
                    "leftMultiplication" => Binary("+", Binary("*", x, y), z),
                    "disabledChild" => Binary("==", Binary("+", x, y, false), z),
                    "rightSub" => Binary("-", x, Binary("-", y, z)),
                    _ => Binary("+", x, Binary("+", y, z))
                };
                CSharpType result = kind is "direct" or "equalityLeft" or "equalityRight" or "hintAlias" or "sharedEdges" or "disabledChild" ? typeof(bool) : typeof(int);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, result, null, parameters);
                return [arrow ? new MethodProvider(signature, body, this) : new MethodProvider(signature, Return(body), this)];
            }
        }
    }
}

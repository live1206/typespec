// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Isolated falsification fixture; not a new accepted production syntax family.
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class PocGroupingCounterexampleTests
    {
        [SetUp]
        public async Task SetUp()
        {
            var mock = await MockHelpers.LoadMockGeneratorAsync(compilation: () => Task.FromResult<Compilation>(Compilation()));
            CodeModelGenerator.Instance = new ProbeGenerator(new GeneratorContext(mock.Object.Configuration)) { SourceInputModel = mock.Object.SourceInputModel };
        }

        private sealed class ProbeGenerator(GeneratorContext context) : CodeModelGenerator(context) { }

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("PocGroupingCounterexample",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        [Test]
        public void RawSignatureTextCannotHideAnEnclosingCallerExpressionCapture()
        {
            var owner = new Owner();
            string baseline = new TypeProviderWriter(owner).Write().Content;
            string before = CaptureText(baseline);
            using var prepared = PreparedFieldTypes.CreatePocGrouping([owner], Compilation(), _ => true);
            Assert.That(prepared.PocGroupingCount, Is.Zero, "Raw signature source cannot receive grouping permission");
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            Assert.That(file.Content, Is.EqualTo(baseline), "No admitted edit of the observed source");
            string after = CaptureText(file.Content); // both outputs must compile
            TestContext.WriteLine("NATIVE:\n" + baseline);
            TestContext.WriteLine("PREPARED:\n" + file.Content);
            TestContext.WriteLine("NATIVE CAPTURE:\n" + before);
            TestContext.WriteLine("PREPARED CAPTURE:\n" + after);
            Assert.That(after, Is.EqualTo(before), "An admitted grouping edit must not change an omitted CAE argument");
        }

        private static string CaptureText(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = Compilation().AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "Capture" });
            var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
            var text = operation.Arguments.Single(a => a.Parameter!.Name == "text");
            Assert.That(text.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
            return (string)text.Value.ConstantValue.Value!;
        }

        private sealed class Owner : TypeProvider
        {
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                // NonDocumentComment is native raw text, explicitly marked as
                // unknown intrinsic tokens/layout by StructuredSourceShape.
                // The literal candidate incorrectly whitelists all signatures.
                const string open = "\nprivate static object _captured = Capture(() => {\n";
                const string close = "\nreturn M(1, 2);\n});\n" +
                    "private static object Capture(global::System.Func<bool> value, " +
                    "[global::System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string text = \"\") => text;\n";
                return [Method("M", 0, open), Method("Tail", MethodSignatureModifiers.Public, close)];
            }

            private MethodProvider Method(string name, MethodSignatureModifiers modifiers, string comment)
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var binary = new BinaryOperatorExpression("==", x, y);
                var signature = new MethodSignature(name, null, modifiers, typeof(bool), null, [x, y], NonDocumentComment: comment);
                return new MethodProvider(signature, Return(binary), this);
            }
        }
    }
}

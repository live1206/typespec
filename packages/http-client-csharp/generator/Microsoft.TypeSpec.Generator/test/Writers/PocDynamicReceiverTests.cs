// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    public class PocDynamicReceiverTests
    {
        [Test]
        public void OriginalDynamicFormalBindsAndDispatchesTheActualArgument()
        {
            const string original = "namespace Sample; public class Clock { public object M(dynamic receiver, int x, int y) => receiver.Use((x + y)); }";
            AssertActualDynamicDispatch(original, "receiver.Use((x + y))");
            const string objectCounterpart = "namespace Sample; public class Clock { public object M(object receiver, int x, int y) => receiver.Use((x + y)); }";
            var compilation = Compile(objectCounterpart);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id),
                Does.Contain("CS1061"), "An object formal is not equivalent to dynamic dispatch");
        }

        private static CSharpCompilation Compile(string source, string path = "Clock.cs") => CSharpCompilation.Create("DynamicReceiverWitness",
            [CSharpSyntaxTree.ParseText(source, path: path)],
            new[] { typeof(object).Assembly.Location, typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location,
                typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location, PathToRuntimeFacade() }
                .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static string PathToRuntimeFacade() => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");

        private static void AssertActualDynamicDispatch(string source, string expected, string path = "Clock.cs")
        {
            var compilation = Compile(source, path);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "M");
            var formal = model.GetDeclaredSymbol(method)!.Parameters;
            Assert.That(formal[0].Type.TypeKind, Is.EqualTo(TypeKind.Dynamic));
            var call = (InvocationExpressionSyntax)method.ExpressionBody!.Expression;
            Assert.That(call.ToString(), Is.EqualTo(expected));
            var receiver = ((MemberAccessExpressionSyntax)call.Expression).Expression;
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(receiver).Symbol, formal[0]), Is.True);
            var operation = model.GetOperation(call);
            Assert.That(operation, Is.InstanceOf<IDynamicInvocationOperation>());
            var binary = call.ArgumentList.Arguments.Single().Expression.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Single();
            var addition = (IBinaryOperation)model.GetOperation(binary)!;
            Assert.That(addition.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
            Assert.That(addition.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Left).Symbol, formal[1]), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(binary.Right).Symbol, formal[2]), Is.True);
            var witness = new ReceiverWitness();
            Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", [witness, 3, 4]).Single(), Is.EqualTo("int:7"));
            Assert.That(witness.Invocations, Is.EqualTo(1));
        }

        public sealed class ReceiverWitness
        {
            internal int Invocations { get; private set; }
            public string Use(int value) { Invocations++; return "int:" + value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            public string Use(object value) { Invocations++; return "object"; }
        }
    }
}

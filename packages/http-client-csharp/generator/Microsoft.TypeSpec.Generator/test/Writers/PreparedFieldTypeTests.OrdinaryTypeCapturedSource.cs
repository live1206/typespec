// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void PocCapturedLambdaRetainsActualQualifiedLocalTypeAndRuntimeText()
        {
            const string support = "namespace Sample { public static class PocCaptureProbe { " +
                "public static string Capture(System.Action make, [System.Runtime.CompilerServices.CallerArgumentExpression(\"make\")] string text = \"\") => text; } }";
            using var host = new BodyOnlyPhysicalHost(customizationSource: support);
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(MemoryStream), "seed", isUsing: true);
            var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
            var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(),
                new MethodBodyStatements(new MethodBodyStatement[] { assignment.Terminate() }));
            var probe = new CSharpType("PocCaptureProbe", "Sample", false, false, null, Array.Empty<CSharpType>(), true, false);
            var call = new InvokeMethodExpression(TypeReferenceExpression.FromType(probe), "Capture", new ValueExpression[] { lambda });
            owner.Methods.Single().Update(bodyStatements: new MethodBodyStatements(new MethodBodyStatement[]
            {
                new ExpressionStatement(new KeywordExpression("return", call))
            }));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var source = prepared!.SourceFiles.Single();
            var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(source, occurrence), Is.Null,
                "The enclosing caller-expression capture is not the POC's no-enclosing-call case.");
            var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(file.Content, Does.Contain("global::System.IO.MemoryStream"));
            var tree = CSharpSyntaxTree.ParseText(file.Content, host.Snapshot.ParseOptions);
            var compilation = host.Snapshot.Compilation.AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            image.Position = 0;
            var context = new AssemblyLoadContext("poc-captured-local-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var assembly = context.LoadFromStream(image);
                var method = assembly.GetType("Sample.RawTypeOfPermissionOwner")!.GetMethod("M")!;
                var observed = (string)method.Invoke(null, null)!;
                Assert.That(observed, Does.Contain("global::System.IO.MemoryStream"));
            }
            finally { context.Unload(); }
        }
    }
}

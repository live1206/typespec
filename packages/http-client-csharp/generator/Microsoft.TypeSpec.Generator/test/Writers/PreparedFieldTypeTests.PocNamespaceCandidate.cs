// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PocBareNamespaceCandidateUsesCanonicalTargetAndRetainsSourceShadow(bool shadow)
        {
            using var host = new BodyOnlyPhysicalHost();
            var compilation = shadow
                ? host.Snapshot.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Sample { public class Uri {} }"))
                : host.Snapshot.Compilation;
            var target = new CSharpType(typeof(Uri));
            var symbol = compilation.GetTypeByMetadataName(typeof(Uri).FullName!);
            Assert.That(symbol, Is.Not.Null);
            var lookup = new PreparedFieldImportLookup(compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            var supported = lookup.TryPreparePocBareNamespaceCandidate("Sample", target, symbol!, out var form, out _);
            Assert.That(supported, Is.EqualTo(!shadow));
            if (!shadow)
            {
                Assert.That(form.Root, Is.EqualTo(PreparedPocNameForms.RootKind.Ordinary));
                Assert.That(form.Start, Is.EqualTo(1));
                var wrong = compilation.GetTypeByMetadataName(typeof(Exception).FullName!);
                Assert.That(wrong, Is.Not.Null);
                Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", target, wrong!, out _, out _), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocBareNamespaceCandidateRejectsGeneratedTargetSideCollision(bool namespacePrefix)
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = new CSharpType(typeof(Uri));
            var symbol = host.Snapshot.Compilation.GetTypeByMetadataName(typeof(Uri).FullName!);
            Assert.That(symbol, Is.Not.Null);
            var generated = new Dictionary<(string Namespace, string Name), TypeProvider[]>();
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            if (namespacePrefix) { namespaces.Add("System.Uri"); }
            else
            {
                var shadow = new TestTypeProvider("Uri", ns: "System");
                shadow.EnsureBuilt();
                _ = shadow.Type;
                generated[("System", "Uri")] = [shadow];
            }
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation, generated, namespaces);
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", target, symbol!, out _, out _), Is.False,
                "A generated type or namespace at the metadata target cannot be treated as that target.");
        }

        [Test]
        public void PocBareNamespaceCandidateRejectsInaccessibleMetadataType()
        {
            using var host = new BodyOnlyPhysicalHost();
            var runtime = typeof(PocInternalRuntimeTarget);
            var target = new CSharpType(runtime);
            var symbol = host.Snapshot.Compilation.GetTypeByMetadataName(runtime.FullName!);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(host.Snapshot.Compilation.IsSymbolAccessibleWithin(symbol!, host.Snapshot.Compilation.Assembly), Is.False);
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", target, symbol!, out _, out _), Is.False);
        }

        [Test]
        public void PocBareNamespaceCandidateRejectsExternAliasOnlyReference()
        {
            using var host = new BodyOnlyPhysicalHost();
            var runtime = typeof(PocPublicRuntimeTarget);
            var reference = host.Snapshot.Compilation.References.OfType<PortableExecutableReference>()
                .Single(item => item.FilePath == runtime.Assembly.Location);
            var aliased = reference.WithAliases(ImmutableArray.Create("only"));
            var compilation = host.Snapshot.Compilation.ReplaceReference(reference, aliased);
            var assembly = compilation.GetAssemblyOrModuleSymbol(aliased) as IAssemblySymbol;
            var symbol = assembly?.GetTypeByMetadataName(runtime.FullName!);
            Assert.That(symbol, Is.Not.Null);
            var lookup = new PreparedFieldImportLookup(compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", new CSharpType(runtime), symbol!, out _, out _), Is.False);
        }

        [Test]
        public void PocBareNamespaceCandidateRejectsLiteralAndUnionDescriptors()
        {
            using var host = new BodyOnlyPhysicalHost();
            var literal = CSharpType.FromLiteral(typeof(Uri), "not-a-plain-type");
            var uriSymbol = host.Snapshot.Compilation.GetTypeByMetadataName(typeof(Uri).FullName!);
            Assert.That(uriSymbol, Is.Not.Null);
            var union = CSharpType.FromUnion(new CSharpType[] { typeof(Uri), typeof(Exception) });
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", literal, uriSymbol!, out _, out _), Is.False);
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", union, uriSymbol!, out _, out _), Is.False);
        }

        [Test]
        public void PocBareNamespaceCandidateEscapesKeywordIdentifier()
        {
            using var host = new BodyOnlyPhysicalHost();
            var runtime = typeof(@class);
            var symbol = host.Snapshot.Compilation.GetTypeByMetadataName(runtime.FullName!);
            Assert.That(symbol, Is.Not.Null);
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", new CSharpType(runtime), symbol!, out var candidate, out _), Is.True);
            var spelling = candidate.GetType().GetProperty("Spelling", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(spelling, Is.Not.Null, "A POC bare-name candidate must carry its safely escaped spelling.");
            Assert.That(spelling!.GetValue(candidate), Is.EqualTo("@class"));
            var tree = CSharpSyntaxTree.ParseText($"using {runtime.Namespace}; public class PocConsumer {{ public {spelling.GetValue(candidate)} M() => null; }}");
            var compilation = host.Snapshot.Compilation.AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var type = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ReturnType;
            Assert.That(SymbolEqualityComparer.Default.Equals(compilation.GetSemanticModel(tree).GetTypeInfo(type).Type, symbol), Is.True);
        }

        [Test]
        public void PocKeywordCandidateKeepsExactNativeQualifiedFallback()
        {
            using var host = new BodyOnlyPhysicalHost();
            var codeModel = Path.Combine(host.DirectoryPath, "tspCodeModel.json");
            File.WriteAllText(codeModel, "{\"name\":\"Sample\"}");
            try
            {
                var target = Target();
                var owner = new FieldOwner(target.Type);
                var declaration = new DeclarationExpression(typeof(@class), "value");
                var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
                var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Private, null, null, []);
                owner.Update(methods: [new MethodProvider(signature, new MethodBodyStatements([assignment.Terminate()]), owner)]);
                owner.EnsureBuilt(); target.EnsureBuilt();
                var roots = new TypeProvider[] { owner, target };
                var output = host.Generator.OutputLibrary;
                output.TypeProviders = roots;
                using var referenceMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                foreach (var provider in roots)
                { ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type)); }
                using var prepared = PreparedFieldTypes.CreateForGenerationWithPocPlans(host.Generator, roots, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var file = prepared!.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence);
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.SimpleSpelling, Is.EqualTo("@class"));
                using var writer = new CodeWriter();
                writer.Append($"{declaration.Variable.Type}");
                Assert.That(candidate.QualifiedSpelling, Is.EqualTo(writer.ToString(false)),
                    "The precomputed fallback must match the native formatter exactly, even when the candidate escapes a keyword.");
            }
            finally { File.Delete(codeModel); }
        }

        [Test]
        public void PocBareNamespaceCandidateDoesNotForceUnmaterializedGeneratedCompetitors()
        {
            using var host = new BodyOnlyPhysicalHost();
            var target = new CSharpType(typeof(Uri));
            var symbol = host.Snapshot.Compilation.GetTypeByMetadataName(typeof(Uri).FullName!);
            Assert.That(symbol, Is.Not.Null);
            var unmaterialized = new CountingGeneratedCompetitor();
            var generated = new Dictionary<(string Namespace, string Name), TypeProvider[]> { [("Other", "Uri")] = [unmaterialized] };
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation, generated, new HashSet<string>(StringComparer.Ordinal));
            var admitted = lookup.TryPreparePocBareNamespaceCandidate("Sample", target, symbol!, out _, out _);
            TestContext.Progress.WriteLine($"Unmaterialized competitor: admitted={admitted}, getter-calls={unmaterialized.GetterCalls}");
            Assert.That(admitted, Is.False);
            Assert.That(unmaterialized.GetterCalls, Is.Zero, "Preparing a candidate must not force a generated provider getter.");
        }

        private sealed class CountingGeneratedCompetitor : TypeProvider
        {
            internal int GetterCalls { get; private set; }
            protected override string BuildName() { GetterCalls++; return "Uri"; }
            protected override string BuildNamespace() { GetterCalls++; return "Other"; }
            protected override string BuildRelativeFilePath() => "Other/Uri.cs";
        }

        [Test]
        public void PocBareNamespaceCandidateDoesNotInvokeUnknownRuntimeTypePredicates()
        {
            using var host = new BodyOnlyPhysicalHost();
            var wrapped = new CountingTypeDelegator(typeof(Uri));
            var target = new CSharpType(wrapped);
            wrapped.Reset();
            var symbol = host.Snapshot.Compilation.GetTypeByMetadataName(typeof(Uri).FullName!);
            Assert.That(symbol, Is.Not.Null);
            var lookup = new PreparedFieldImportLookup(host.Snapshot.Compilation,
                new Dictionary<(string Namespace, string Name), TypeProvider[]>(), new HashSet<string>(StringComparer.Ordinal));
            Assert.That(lookup.TryPreparePocBareNamespaceCandidate("Sample", target, symbol!, out _, out _), Is.False);
            Assert.That(wrapped.GetterCalls, Is.Zero, "Unknown runtime Type dispatch must not run while preparing a candidate.");
        }

        private sealed class CountingTypeDelegator(Type wrapped) : TypeDelegator(wrapped)
        {
            internal int GetterCalls { get; private set; }
            internal void Reset() => GetterCalls = 0;
            public override string Name { get { GetterCalls++; return base.Name; } }
            public override string? Namespace { get { GetterCalls++; return base.Namespace; } }
            public override bool IsGenericType { get { GetterCalls++; return base.IsGenericType; } }
            public override bool IsGenericParameter { get { GetterCalls++; return base.IsGenericParameter; } }
        }
    }

    internal sealed class PocInternalRuntimeTarget { }
    public sealed class PocPublicRuntimeTarget { }
    public sealed class @class { }
}

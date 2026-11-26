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
    internal class PocQualifiedMemberGroupingTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";
        private const string QualifiedMember = "global::System.DateTimeOffset.Now";
        private const string Wrapped = "((" + QualifiedMember + "))";

        [Test]
        public void OriginalRawQualifiedMemberHasNoNameShorteningAuthority()
        {
            var original = new CodeFile("class Clock { object M() => " + Wrapped + "; }", "Clock.cs");
            var expected = new CodeFile("class Clock { object M() => " + QualifiedMember + "; }", "Clock.cs");
            Assert.That(Expression(original), Is.EqualTo(Wrapped));
            Assert.That(Expression(expected), Is.EqualTo(QualifiedMember));
            Assert.That(original.SelectPreparedForms(true, true, true).Content, Is.EqualTo(original.Content));
            AssertCanonicalBoxing(original);
            AssertCanonicalBoxing(expected);
            PreparedParenthesesTests.AssertOperations(original.Content, expected.Content);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OriginalWrappedQualifiedMemberSelectsOnlyGrouping(bool selectOmission)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-qualified-member", compilation, ["qualified-member"], () => true)
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
            List<object> visits = [];
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Outer) || ReferenceEquals(node, owner.Inner) || ReferenceEquals(node, owner.Member) ||
                    ReferenceEquals(node, owner.Member.Inner)) { visits.Add(node); }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var qualified = raw.SelectPreparedForms(false, false, false);
            var selected = raw.SelectPreparedForms(true, true, selectOmission && prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(native.Name, Is.EqualTo(selected.Name));
            Assert.That(native.Name, Is.EqualTo(qualified.Name));
            Assert.That(native.Content, Is.EqualTo(qualified.Content));
            if (!selectOmission) { Assert.That(native.Content, Is.EqualTo(selected.Content)); }
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(raw.PreparedBodyNameCount, Is.Zero);
            object[] expectedVisits = [owner.Outer, owner.Inner, owner.Member, owner.Member.Inner!];
            Assert.That(visits, Has.Count.EqualTo(expectedVisits.Length));
            for (int i = 0; i < expectedVisits.Length; i++) { Assert.That(visits[i], Is.SameAs(expectedVisits[i])); }
            Assert.That(Expression(native), Is.EqualTo(Wrapped));
            AssertCanonicalBoxing(native);
            AssertCanonicalBoxing(selected);
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            AssertBoxedRuntime(native);
            AssertBoxedRuntime(selected);
            Assert.That(Expression(selected), Is.EqualTo(selectOmission ? QualifiedMember : Wrapped));
            if (selectOmission)
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(2));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(2));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(2));
                var unknownNames = new CodeFile(raw.Content, raw.Name, qualified.Content, raw.SourceConsumption, preparedGroupingCount: 2);
                Assert.That(unknownNames.SelectPreparedForms(true, true, true).Content, Is.EqualTo(native.Content));
            }
        }

        [TestCase("restored")]
        [TestCase("other-member")]
        [TestCase("other-receiver")]
        public async Task QualifiedMemberUnknownOrRestoredEvidenceRetains(string mode)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = ControlledInputs(generator, compilation);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            bool restore = mode == "restored";
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(restore ? 2 : 0));
            int openings = 0;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                owner.Member.Update(memberName: nameof(DateTimeOffset.UtcNow));
                owner.Member.Update(memberName: nameof(DateTimeOffset.Now));
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            Assert.That(openings, Is.EqualTo(restore ? 1 : 0));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(restore ? 1 : 0));
            if (restore)
            {
                Assert.That(Expression(raw), Is.EqualTo("(" + QualifiedMember + ")"));
                Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False);
            }
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            Assert.That(allowed, Is.EqualTo(!restore));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            Assert.That(Expression(selected), Is.EqualTo(mode switch
            {
                "other-member" => "((global::System.DateTimeOffset.UtcNow))",
                "other-receiver" => "((global::System.DateTime.Now))",
                _ => Wrapped
            }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task QualifiedMemberRetainsWithActualEmittedSourceCompetitor(bool loadedSource)
        {
            var compilation = Compilation();
            if (loadedSource)
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                    "namespace System { public class DateTimeOffset { public static int Now => 23; } }", path: "Competitor.cs"));
            }
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration, compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock();
            var competitor = new Competitor();
            var output = loadedSource ? new TestOutputLibrary(owner) : new TestOutputLibrary(owner, competitor);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = ControlledInputs(generator, compilation);
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock();
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
            Assert.That(Expression(selected), Is.EqualTo(Wrapped));
            var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            if (!loadedSource)
            {
                var competitorFile = new TypeProviderWriter(competitor).Write();
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(competitorFile.Content, path: competitorFile.Name));
            }
            var emitted = compilation.AddSyntaxTrees(tree);
            Assert.That(emitted.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var member = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single(node => node.Name.Identifier.ValueText == "Now");
            var property = ((IPropertyReferenceOperation)emitted.GetSemanticModel(tree).GetOperation(member)!).Property;
            Assert.That(SymbolEqualityComparer.Default.Equals(property.ContainingAssembly, emitted.Assembly), Is.True);
            Assert.That(property.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(PreparedGeneratedRuntime.Invoke(emitted, "Sample.Clock", "M", [Array.Empty<object?>()]).Single(), Is.EqualTo(23));
        }

        [TestCase("stable")]
        [TestCase("source-expiry")]
        [TestCase("result-expiry")]
        public async Task QualifiedMemberUsesActualCompilerOutputAndFinalAuthority(string mode)
        {
            var sandbox = Directory.CreateTempSubdirectory("task05-qualified-member-");
            string source = Path.Combine(sandbox.FullName, "Marker.cs");
            string nativeSource = Path.Combine(sandbox.FullName, "NativeMarker.cs");
            const string input = "namespace Inputs { internal class Marker {} }\n";
            File.WriteAllText(source, input);
            File.WriteAllText(nativeSource, input);
            try
            {
                var actual = await WriteCompilerOutput(sandbox.FullName, source, mode);
                var native = await WriteCompilerOutput(sandbox.FullName, nativeSource, "native");
                Assert.That(Expression(native.Selected), Is.EqualTo(Wrapped));
                Assert.That(Expression(actual.Selected), Is.EqualTo(mode == "source-expiry" ? Wrapped : QualifiedMember));
                if (mode == "source-expiry") { Assert.That(actual.Selected.Content, Is.EqualTo(native.Selected.Content)); }
                Assert.That(actual.Selected.Name, Is.EqualTo(native.Selected.Name));
                Assert.That(actual.Final.Name, Is.EqualTo(native.Final.Name));
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, actual.Selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, actual.Final.Content);
                foreach (var file in new[] { native.Selected, actual.Selected, native.Final, actual.Final })
                {
                    AssertCanonicalBoxing(file);
                    AssertBoxedRuntime(file);
                }
            }
            finally { sandbox.Delete(recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> WriteCompilerOutput(string directory, string source, string mode)
        {
            bool enabled = mode != "native";
            string original = Directory.CreateDirectory(Path.Combine(directory, mode + "-original")).FullName;
            string isolated = Directory.CreateDirectory(Path.Combine(directory, mode + "-isolated")).FullName;
            string config = enabled ? Configuration : Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(Compilation()));
            var owner = new Clock();
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            int visits = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    if (!enabled) { Assert.That(prepared, Is.Null); return; }
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(prepared!.PocGroupingCount, Is.EqualTo(2));
                    Assert.That(generator.CompilerBuildContext?.SourceProof, Is.Not.Null);
                    prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Member)) { visits++; } };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    Assert.That(provider, Is.SameAs(owner));
                    Assert.That(prepared?.PocGroupingUses ?? 0, Is.EqualTo(enabled ? 2 : 0));
                    Assert.That(file.PreparedBodyNameCount, Is.Zero);
                    Assert.That(Expression(file), Is.EqualTo(enabled ? QualifiedMember : Wrapped));
                },
                PocGroupingDecisionObserved = (_, allowed) =>
                {
                    Assert.That(allowed, Is.True);
                    if (mode == "source-expiry") { File.AppendAllText(source, "// expired before selection\n"); }
                },
                PocSelectedFileObserved = file =>
                {
                    selected = file;
                    if (mode == "result-expiry") { File.AppendAllText(source, "// expired after selection\n"); }
                }
            };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"), "False means the caller discards isolated output");
            Assert.That(visits, Is.EqualTo(enabled ? 1 : 0));
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("Clock.cs"));
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        private static SourceInputModel ControlledInputs(NativeGenerator generator, CSharpCompilation compilation) => new(compilation, null)
        {
            PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                "task05-qualified-member-guards", compilation, ["qualified-member"], () => true)
        };

        private static string Expression(CodeFile file) => PreparedParenthesesTests.MethodExpression(file.Content).NormalizeWhitespace().ToFullString();

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("QualifiedMember",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static void AssertCanonicalBoxing(CodeFile file)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var expression = method.ExpressionBody!.Expression;
            var member = expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Single(node => node.Name.Identifier.ValueText == "Now");
            var model = compilation.GetSemanticModel(tree);
            var property = ((IPropertyReferenceOperation)model.GetOperation(member)!).Property;
            var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            var canonical = core.GetTypeByMetadataName("System.DateTimeOffset")!;
            Assert.That(SymbolEqualityComparer.Default.Equals(property.ContainingType, canonical), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(property.Type, canonical), Is.True);
            Assert.That(property.IsStatic && property.GetMethod is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }, Is.True);
            Assert.That(model.GetConversion(expression).IsBoxing, Is.True);
            Assert.That(model.GetTypeInfo(expression).ConvertedType?.SpecialType, Is.EqualTo(SpecialType.System_Object));
        }

        private static void AssertBoxedRuntime(CodeFile file) => Assert.That(PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", [Array.Empty<object?>()]).Single(), Is.TypeOf<DateTimeOffset>());

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Competitor : TypeProvider
        {
            protected override string BuildName() => "DateTimeOffset";
            protected override string BuildNamespace() => "System";
            protected override string BuildRelativeFilePath() => "DateTimeOffset.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override PropertyProvider[] BuildProperties() =>
                [new PropertyProvider(null, MethodSignatureModifiers.Public | MethodSignatureModifiers.Static, typeof(int), "Now", new ExpressionPropertyBody(Literal(23)), this)];
        }

        private sealed class Session(string directory, string source) : ITrustedCompilerBuildSession
        {
            private readonly string _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string _core = typeof(object).Assembly.Location;
            private readonly string _coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            public CompilerBuildInputCapture Capture => new(directory, "actual-qualified-member-output", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, _sourceHash)], [new CapturedCompilerReference(_core, _coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent() => true;
        }

        private sealed class Clock(string mode = "ordinary") : TypeProvider
        {
            internal ParenthesizedExpression Outer { get; private set; } = null!;
            internal ParenthesizedExpression Inner { get; private set; } = null!;
            internal MemberExpression Member { get; private set; } = null!;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                Member = new MemberExpression(Static(mode == "other-receiver" ? typeof(DateTime) : typeof(DateTimeOffset)),
                    mode == "other-member" ? nameof(DateTimeOffset.UtcNow) : nameof(DateTimeOffset.Now));
                Inner = new ParenthesizedExpression(Member);
                Outer = new ParenthesizedExpression(Inner);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null, []);
                return [new MethodProvider(signature, Outer, this)];
            }
        }
    }
}

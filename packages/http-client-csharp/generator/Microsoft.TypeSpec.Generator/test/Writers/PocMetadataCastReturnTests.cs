// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    internal class PocMetadataCastReturnTests
    {
        private const string Configuration = "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}";

        [TestCase("exception-null")]
        [TestCase("datetime-default")]
        [TestCase("list-reference")]
        [TestCase("array-reference")]
        [TestCase("array-receiver")]
        [TestCase("unboxing")]
        [TestCase("primitive-control")]
        [TestCase("exception-default")]
        [TestCase("datetime-unbox")]
        [TestCase("list-other-element")]
        [TestCase("other-array")]
        [TestCase("restored-target")]
        public async Task NativeMetadataCastReturnKeepsActualTypeAndConversion(string mode)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock(mode);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-metadata-cast-return", compilation, ["metadata-cast-return"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock(mode);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            int castVisits = 0;
            int operandVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast)) { castVisits++; }
                if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
            };
            bool restored = mode == "restored-target";
            int restores = 0;
            if (restored)
            {
                prepared.PocAfterOpening = () =>
                {
                    var type = owner.Cast.Type;
                    string name = type.Name;
                    type.Update(name: "Changed");
                    type.Update(name: name);
                    restores++;
                };
            }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            var qualified = raw.SelectPreparedForms(false, false, false);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(qualified.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            PreparedParenthesesTests.AssertOperations(native.Content, qualified.Content);
            var (originalCast, originalConversion) = ActualCast(native, mode);
            var (cast, conversion) = ActualCast(selected, mode);
            var (qualifiedCast, _) = ActualCast(qualified, mode);
            Assert.That(originalCast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(qualifiedCast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(cast.Type.NormalizeWhitespace().ToFullString(), Is.EqualTo(originalCast.Type.NormalizeWhitespace().ToFullString()),
                "Native spelling is a separate decision, not changed by grouping");
            Assert.That(cast.Expression.NormalizeWhitespace().ToFullString(), Is.EqualTo(originalCast.Expression.NormalizeWhitespace().ToFullString()));
            Assert.That(conversion.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Is.EqualTo(originalConversion.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            Assert.That(conversion.OperatorMethod, Is.Null);
            object?[][] values = mode switch
            {
                "exception-null" or "datetime-default" or "exception-default" or "restored-target" => [[]],
                "datetime-unbox" => [[DateTimeOffset.MinValue], [DateTimeOffset.UnixEpoch]],
                "list-other-element" => [[new List<object> { new object() }], [null]],
                "other-array" => [[new[] { new InvalidOperationException() }], [null]],
                "list-reference" => [[new List<string> { "value" }], [new[] { "array" }], [null]],
                "array-reference" => [[new Exception[] { new InvalidOperationException() }], [Array.Empty<Exception>()], [null]],
                "array-receiver" => [[new Exception[] { new InvalidOperationException() }], [Array.Empty<Exception>()]],
                _ => [[int.MinValue], [0], [int.MaxValue]]
            };
            Assert.That(Execute(selected, values), Is.EqualTo(Execute(native, values)));
            if (mode is "array-reference" or "list-reference")
            {
                object?[][] unsuitable = [[new object()]];
                Assert.That(Assert.Throws<TargetInvocationException>(() => Execute(native, unsuitable))!.InnerException,
                    Is.InstanceOf<InvalidCastException>());
                Assert.That(Assert.Throws<TargetInvocationException>(() => Execute(selected, unsuitable))!.InnerException,
                    Is.InstanceOf<InvalidCastException>());
            }
            Assert.That(castVisits, Is.EqualTo(1));
            Assert.That(operandVisits, Is.EqualTo(1));
            Assert.That(restores, Is.EqualTo(restored ? 1 : 0));
            Assert.That(raw.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!restored));
            bool shorten = mode is "exception-null" or "datetime-default" or "list-reference" or "array-reference" or "primitive-control";
            int expectedUse = shorten || restored ? 1 : 0;
            TestContext.WriteLine($"METADATA-CAST mode={mode}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; type={cast.Type}; parent={cast.Parent?.Kind()}; visits={castVisits}/{operandVisits}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expectedUse));
                Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expectedUse));
                Assert.That(cast.Parent is ParenthesizedExpressionSyntax, Is.EqualTo(!shorten));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ActualSourceOrGeneratedOwnerCanCompeteWithGlobalTarget(bool loadedSource)
        {
            var compilation = Compilation();
            if (loadedSource)
            {
                compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace System { public sealed class Exception {} }", path: "Competitor.cs"));
            }
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new Clock("exception-null", collidingOwner: !loadedSource);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "metadata-competition", compilation, ["metadata-competition"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new Clock("exception-null", collidingOwner: !loadedSource);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared?.CanCommitPocGrouping(raw) == true && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(selected.Content, Is.EqualTo(native.Content));
            var tree = CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name);
            var emitted = compilation.AddSyntaxTrees(tree);
            Assert.That(emitted.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var cast = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var symbol = emitted.GetSemanticModel(tree).GetTypeInfo(cast.Type).Type;
            Assert.That(symbol?.Name, Is.EqualTo("Exception"));
            Assert.That(SymbolEqualityComparer.Default.Equals(symbol!.ContainingAssembly, emitted.Assembly), Is.True,
                "The actual emitted type syntax binds to a source competitor, not the intended corelib target");
            Assert.That(cast.Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            Assert.That(raw.PreparedGroupingCount, Is.Zero);
            Assert.That(prepared?.PocGroupingCount ?? 0, Is.Zero);
        }

        [TestCase("datetime-default", "stable")]
        [TestCase("list-reference", "stable")]
        [TestCase("exception-null", "source-expiry")]
        [TestCase("array-reference", "result-expiry")]
        public async Task CompilerMetadataReturnUsesActualSelectedFilesAndResult(string role, string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-metadata-return-compiler", Guid.NewGuid().ToString("N"));
            var original = Path.Combine(sandbox, "original");
            var isolated = Path.Combine(sandbox, "isolated");
            var source = Path.Combine(sandbox, "Marker.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Inputs { internal class Marker {} }\n");
            try
            {
                var native = await NativeFinalControl(role, sandbox, source);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: Configuration,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("MetadataReturnCompilerInputs")));
                var owner = new Clock(role, "alias/Clock.cs");
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
                            if (ReferenceEquals(node, owner.Cast.Inner)) { operandVisits++; }
                        };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(provider, Is.SameAs(owner));
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo("alias/Clock.cs"));
                        Assert.That(CastSyntax(file).Parent, Is.Not.InstanceOf<ParenthesizedExpressionSyntax>());
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
                Assert.That(CastSyntax(selected).Parent is ParenthesizedExpressionSyntax, Is.EqualTo(mode == "source-expiry"));
                Assert.That(proof!.IsCurrentFor(original), Is.EqualTo(mode == "stable"));
                Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(mode == "stable"),
                    "False requires discard even after short selection");
                var final = new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name);
                PreparedParenthesesTests.AssertOperations(native.Selected.Content, selected.Content);
                PreparedParenthesesTests.AssertOperations(native.Final.Content, final.Content);
                object?[][] values = role switch
                {
                    "list-reference" => [[new List<string> { "a" }], [null]],
                    "array-reference" => [[new Exception[] { new InvalidOperationException() }], [null]],
                    _ => [[]]
                };
                Assert.That(Execute(selected, values), Is.EqualTo(Execute(native.Selected, values)));
                Assert.That(Execute(final, values), Is.EqualTo(Execute(native.Final, values)));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        private static async Task<(CodeFile Selected, CodeFile Final)> NativeFinalControl(string role, string sandbox, string source)
        {
            var original = Path.Combine(sandbox, "native-original");
            var isolated = Path.Combine(sandbox, "native-isolated");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            var config = Configuration.Replace("\"enable-pre-emission-simplification\":true", "\"enable-pre-emission-simplification\":false", StringComparison.Ordinal);
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original, configuration: config,
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeMetadataReturnInputs")));
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(new Clock(role, "alias/Clock.cs")));
            CodeModelGenerator.Instance = generator;
            CodeFile? selected = null;
            var pipeline = new CSharpGen { PocSelectedFileObserved = file => selected = file };
            var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, new Session(original, source));
            Assert.That(result.UsedCapturedResidualInputs, Is.False, "This disabled control is only for stage-aligned comparison");
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.Name, Is.EqualTo("alias/Clock.cs"));
            Assert.That(CastSyntax(selected).Parent, Is.InstanceOf<ParenthesizedExpressionSyntax>());
            return (selected, new CodeFile(File.ReadAllText(Path.Combine(isolated, selected.Name)), selected.Name));
        }

        [TestCase("path", false)]
        [TestCase("path", true)]
        [TestCase("serialization", false)]
        [TestCase("serialization", true)]
        public async Task MetadataCastDoesNotFirstLoadASecondPhaseDependency(string dependency, bool withLegacy)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(configuration: Configuration,
                compilation: () => Task.FromResult<Compilation>(compilation));
            var early = new Lazy<int>(() => 0);
            var late = new Lazy<int>(() => 0);
            bool armed = false;
            void ObserveDependency()
            {
                _ = early.Value;
                if (armed) { _ = late.Value; }
            }
            var owner = new Clock("exception-null", observePath: dependency == "path" ? ObserveDependency : null);
            var control = new DirectControl();
            var output = withLegacy ? new TestOutputLibrary(control, owner) : new TestOutputLibrary(owner);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "metadata-cast-cold", compilation, ["metadata-cast-cold"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            if (dependency == "serialization") { owner.Update(serializations: new SerializationCarrier(ObserveDependency)); }
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            Assert.That(early.IsValueCreated, Is.True);
            Assert.That(late.IsValueCreated, Is.False);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            if (withLegacy)
            {
                var old = new TypeProviderWriter(control) { PreparedFieldTypes = prepared }.Write();
                Assert.That(old.PreparedGroupingCount, Is.EqualTo(1));
                Assert.That(old.Content, Does.Contain("return x == y;"));
            }
            int castVisits = 0;
            int leftVisits = 0;
            prepared!.PocNativeWriteObserver = node =>
            {
                if (ReferenceEquals(node, owner.Cast))
                {
                    castVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = true;
                }
                if (ReferenceEquals(node, owner.Cast.Inner))
                {
                    leftVisits++;
                    Assert.That(late.IsValueCreated, Is.False);
                    armed = false;
                }
            };
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            var selected = raw.SelectPreparedForms(true, true, prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw));
            Assert.That(castVisits, Is.EqualTo(1));
            Assert.That(leftVisits, Is.EqualTo(1));
            Assert.That(late.IsValueCreated, Is.False);
            // The metadata proof is intentionally single-owner; the separate
            // legacy control keeps its own choice in the mixed case.
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(withLegacy ? 0 : 1));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(1));
            Assert.That(CastSyntax(selected).Parent is ParenthesizedExpressionSyntax, Is.EqualTo(withLegacy));
        }

        private static CastExpressionSyntax CastSyntax(CodeFile file) => CSharpSyntaxTree.ParseText(file.Content, path: file.Name).GetRoot()
            .DescendantNodes().OfType<CastExpressionSyntax>().Single();

        private static (CastExpressionSyntax Cast, IConversionOperation Conversion) ActualCast(CodeFile file, string mode)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, path: file.Name);
            var compilation = Compilation().AddSyntaxTrees(tree);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var cast = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var operation = model.GetOperation(cast);
            var target = model.GetTypeInfo(cast.Type).Type;
            TestContext.WriteLine($"METADATA-CAST-ORACLE mode={mode}; operation={operation?.Kind}; target={target?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
            Assert.That(operation, Is.InstanceOf<IConversionOperation>());
            Assert.That(target, Is.Not.Null);
            var runtimeObject = compilation.GetSpecialType(SpecialType.System_Object);
            if (mode is "exception-null" or "datetime-default" or "list-reference" or "array-reference")
            {
                var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                Assert.That(model.GetTypeInfo(method.ExpressionBody!.Expression).ConvertedType?.SpecialType, Is.EqualTo(SpecialType.System_Object));
                var returnConversion = compilation.ClassifyConversion(target!, runtimeObject);
                Assert.That(returnConversion.IsImplicit, Is.True);
                if (mode == "datetime-default") { Assert.That(returnConversion.IsBoxing, Is.True); }
                else { Assert.That(returnConversion.IsReference, Is.True); }
            }
            if (mode == "list-reference")
            {
                Assert.That(target, Is.InstanceOf<INamedTypeSymbol>());
                var list = (INamedTypeSymbol)target!;
                Assert.That(list.OriginalDefinition.MetadataName, Is.EqualTo("IList`1"));
                Assert.That(list.OriginalDefinition.ContainingNamespace.ToDisplayString(), Is.EqualTo("System.Collections.Generic"));
                Assert.That(SymbolEqualityComparer.Default.Equals(list.ContainingAssembly, runtimeObject.ContainingAssembly), Is.True);
                Assert.That(list.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_String));
            }
            if (mode is "array-reference" or "array-receiver")
            {
                Assert.That(target, Is.InstanceOf<IArrayTypeSymbol>());
                var array = (IArrayTypeSymbol)target!;
                Assert.That(array.IsSZArray, Is.True);
                Assert.That(array.ElementType.Name, Is.EqualTo("Exception"));
                Assert.That(SymbolEqualityComparer.Default.Equals(array.ElementType.ContainingAssembly, runtimeObject.ContainingAssembly), Is.True);
            }
            if (mode is "array-reference" or "list-reference")
            {
                var inputConversion = compilation.ClassifyConversion(runtimeObject, target!);
                Assert.That(inputConversion.IsExplicit && inputConversion.IsReference && !inputConversion.IsUserDefined, Is.True);
            }
            return (cast, (IConversionOperation)operation!);
        }
        private static CSharpCompilation Compilation() => CSharpCompilation.Create("NativeMetadataCastReturn",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
            public CompilerBuildInputCapture Capture => new(directory, "metadata-return-captured", "Debug|net10.0", "12.0", "Actual", [],
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
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, [x, y]), Return(new BinaryOperatorExpression("==", x, y)), this)];
            }
        }
        private sealed class Clock(string mode, string path = "Clock.cs", Action? observePath = null, bool collidingOwner = false) : TypeProvider
        {
            internal CastExpression Cast = null!;
            protected override string BuildName() => collidingOwner ? "Exception" : "Clock";
            protected override string BuildNamespace() => collidingOwner ? "System" : "Sample";
            protected override string BuildRelativeFilePath()
            {
                observePath?.Invoke();
                return path;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var formal = new ParameterProvider("value", $"", mode == "primitive-control" ? typeof(int) : typeof(object));
                ValueExpression operand = mode switch
                {
                    "exception-null" or "restored-target" => new KeywordExpression("null", null),
                    "datetime-default" or "exception-default" => new KeywordExpression("default", null),
                    _ => (VariableExpression)formal
                };
                CSharpType target = mode switch
                {
                    "exception-null" or "exception-default" or "restored-target" => new CSharpType(typeof(Exception)),
                    "datetime-default" or "datetime-unbox" => typeof(DateTimeOffset),
                    "list-reference" => typeof(IList<string>),
                    "list-other-element" => typeof(IList<object>),
                    "other-array" => typeof(InvalidOperationException[]),
                    "array-reference" or "array-receiver" => typeof(Exception[]),
                    _ => typeof(int)
                };
                Cast = new CastExpression(operand, target);
                var returns = mode is "array-receiver" or "primitive-control" ? typeof(int) : typeof(object);
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public, returns, null,
                    operand is KeywordExpression ? [] : [formal]);
                ValueExpression body = mode == "array-receiver" ? new MemberExpression(Cast, "Length") : Cast;
                return [new MethodProvider(signature, body, this)];
            }
        }
    }
}

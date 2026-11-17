// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
    internal class PocPublicationTests
    {
        [TestCase("stable")]
        [TestCase("child")]
        [TestCase("source")]
        [TestCase("policy")]
        [TestCase("late-permission")]
        [TestCase("late-proof-identity")]
        [TestCase("late-write-policy")]
        [TestCase("late-output-roots")]
        [TestCase("late-source-model")]
        [TestCase("unbacked-source-model")]
        [TestCase("late-field")]
        [TestCase("late-field-path")]
        [TestCase("late-field-host")]
        [TestCase("late-field-unknown-list")]
        [TestCase("late-field-null-builder")]
        public async Task ActualPipelinePublishesPerFileChoicesBeforeLegacyProcessing(string mode)
        {
            string directory = Path.Combine(Path.GetTempPath(), "task05-publication", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
                var group = new GroupOwner();
                var target = new Widget();
                FieldOwner field = mode == "late-field-null-builder" ? new NullScopeFieldOwner(target.Type) : new FieldOwner(target.Type);
                var mutableImplements = new List<CSharpType>();
                var unknownInterfaces = new UnknownEmptyInterfaces();
                int interfaceReadsAtArm = 0;
                if (mode == "late-field-unknown-list") { field.Update(implements: unknownInterfaces); }
                else if (mode.StartsWith("late-field", StringComparison.Ordinal)) { field.Update(implements: mutableImplements); }
                var output = new TestOutputLibrary(group, field, target);
                PipelineGenerator generator = mode == "unbacked-source-model"
                    ? new UnbackedSourceModelGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output)
                    : new PipelineGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
                CodeModelGenerator.Instance = generator;
                var raw = new Dictionary<string, CodeFile>();
                var selected = new Dictionary<string, CodeFile>();
                int binaryWrites = 0;
                int actualUses = 0;
                int lateMutations = 0;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        prepared!.PocNativeWriteObserver = node => { if (ReferenceEquals(node, group.Binary)) binaryWrites++; };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        raw.Add(file.Name, file);
                        if (!ReferenceEquals(provider, group)) return;
                        Assert.That(file.Content, Does.Contain("return x == y;"));
                        Assert.That(file.QualifiedContent, Does.Contain("return (x == y);"));
                        actualUses = prepared!.PocGroupingUses;
                        if (mode == "child") group.Right!.Update(type: typeof(long));
                        if (mode == "source") generator.SourceCurrent = false;
                        if (mode == "policy") generator.AddRewriter(new IdentityRewriter());
                    },
                    PocGroupingDecisionObserved = (file, allowed) =>
                    {
                        if (mode is "late-proof-identity" or "late-write-policy" or "late-output-roots" or "late-source-model" && file.Name == "Clock.cs")
                        {
                            Assert.That(allowed, Is.True);
                            int sharedPaths = 0;
                            void AtSharedPath()
                            {
                                if (++sharedPaths < 2) { field.OnSharedPathCheck = AtSharedPath; return; }
                                generator.OnNextSourceCheck = () =>
                                {
                                    if (mode == "late-proof-identity")
                                    {
                                        Assert.That(generator.SourceInputModel.PreEmissionSourceInputs, Is.Not.Null);
                                        generator.SourceInputModel.PreEmissionSourceInputs = null;
                                    }
                                    else if (mode == "late-write-policy")
                                    {
                                        Assert.That(ProviderReferenceMapAnalyzer.ShouldWriteProvider(field), Is.True);
                                        Assert.That(ProviderReferenceMapAnalyzer.LatestResult, Is.Not.Null);
                                        ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Add(field.Type.FullyQualifiedName);
                                        Assert.That(ProviderReferenceMapAnalyzer.ShouldWriteProvider(field), Is.False);
                                    }
                                    else if (mode == "late-output-roots")
                                    {
                                        var retained = generator.OutputLibrary.TypeProviders;
                                        generator.OutputLibrary.TypeProviders = new TypeProvider[] { target };
                                        Assert.That(generator.OutputLibrary.TypeProviders, Is.Not.SameAs(retained));
                                        Assert.That(retained.Contains(group), Is.True);
                                        Assert.That(retained.Contains(field), Is.True);
                                    }
                                    else
                                    {
                                        var retained = generator.SourceInputModel;
                                        var proof = retained.PreEmissionSourceInputs;
                                        generator.SourceInputModel = new SourceInputModel((CSharpCompilation)retained.Customization!, null);
                                        Assert.That(generator.SourceInputModel, Is.Not.SameAs(retained));
                                        Assert.That(retained.PreEmissionSourceInputs, Is.SameAs(proof));
                                    }
                                    lateMutations++;
                                };
                            }
                            field.OnSharedPathCheck = AtSharedPath;
                            return;
                        }
                        if (mode.StartsWith("late-field", StringComparison.Ordinal) && file.Name == "Clock.cs")
                        {
                            Assert.That(allowed, Is.True);
                            void MutateField()
                            {
                                Assert.That(field.HasSimpleFieldTypeScope, Is.True);
                                mutableImplements.Add(new CSharpType(typeof(IDisposable)));
                                Assert.That(field.HasSimpleFieldTypeScope, Is.False);
                                lateMutations++;
                            }
                            if (mode == "late-field-unknown-list")
                            {
                                interfaceReadsAtArm = unknownInterfaces.Reads;
                                unknownInterfaces.Armed = true;
                                lateMutations++;
                            }
                            else if (mode == "late-field-null-builder")
                            {
                                ((NullScopeFieldOwner)field).Armed = true;
                                lateMutations++;
                            }
                            else if (mode == "late-field") { MutateField(); }
                            else if (mode == "late-field-path") { field.OnSharedPathCheck = MutateField; }
                            else { field.OnSharedPathCheck = () => generator.OnNextSourceCheck = MutateField; }
                            return;
                        }
                        if (mode != "late-permission" || file.Name != "Clock.cs") return;
                        Assert.That(allowed, Is.True, "The per-file decision must succeed before the later callback mutates it");
                        generator.OnNextSourceCheck = () => { lateMutations++; group.Right!.Update(type: typeof(long)); };
                    },
                    PocSelectedFileObserved = file => selected.Add(file.Name, file)
                };
                await pipeline.ExecuteAsync();
                if (mode == "late-field-unknown-list") { Assert.That(unknownInterfaces.Reads, Is.EqualTo(interfaceReadsAtArm)); }
                Assert.That(actualUses, Is.EqualTo(1));
                Assert.That(binaryWrites, Is.EqualTo(1));
                Assert.That(lateMutations, Is.EqualTo(mode is "late-permission" or "late-proof-identity" or "late-write-policy" or "late-output-roots" or "late-source-model" || mode.StartsWith("late-field", StringComparison.Ordinal) ? 1 : 0));
                Assert.That(selected["Clock.cs"].Content,
                    Does.Contain(mode == "stable" || mode.StartsWith("late-field", StringComparison.Ordinal) ? "return x == y;" : "return (x == y);"),
                    "Assert actual selected raw form, not a result cleaned up by legacy processing");
                if (mode != "policy")
                {
                    Assert.That(raw["Consumer.cs"].Content,
                        Does.Contain(mode == "source" ? "private global::Sample.Widget _value;" : "private Widget _value;"));
                }
                Assert.That(selected["Consumer.cs"].Content,
                    Does.Contain(mode is "source" or "policy" or "late-proof-identity" or "late-write-policy" or "late-output-roots" or "late-source-model" or "unbacked-source-model" || mode.StartsWith("late-field", StringComparison.Ordinal) ? "private global::Sample.Widget _value;" : "private Widget _value;"),
                    "An unrelated name-only file must retain its independent approved decision");
                var selectedTrees = selected.Values.Select(file => CSharpSyntaxTree.ParseText(file.Content, path: file.Name)).ToArray();
                var selectedCompilation = CSharpCompilation.Create("SelectedBeforeLegacy", selectedTrees,
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(selectedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
                    "Captured selected pre-legacy files must compile independently of workspace cleanup");
                object?[][] runtimeArguments = [[0, 0], [1, 2], [-1, -1], [int.MinValue, int.MaxValue]];
                var selectedRuntime = PreparedGeneratedRuntime.Invoke(selectedCompilation, "Sample.Clock", "M", runtimeArguments);
                Assert.That(selectedRuntime, Is.EqualTo(new object?[] { true, false, true, false }));
                Assert.That(File.Exists(Path.Combine(directory, "Clock.cs")), Is.True);
                var trees = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))).ToArray();
                var compilation = CSharpCompilation.Create("Published", trees,
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
                Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample.Clock", "M", runtimeArguments), Is.EqualTo(selectedRuntime),
                    "Workspace/legacy publication must not change the pre-legacy selected program's runtime behavior");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        [Test]
        public void DisjointSelectorDoesNotInferUnsupportedCompoundPermission()
        {
            var none = ImmutableArray<PreparedNameAlternative>.Empty;
            var group = new CodeFile("short", "X.cs", "native", nameAlternatives: none, preparedGroupingCount: 1);
            Assert.That(group.SelectPreparedForms(false, false, true).Content, Is.EqualTo("short"));
            Assert.That(group.SelectPreparedForms(true, true, false).Content, Is.EqualTo("native"));
            var names = ImmutableArray.Create(new PreparedNameAlternative(0, 6, "n", PreparedNameChannel.Field));
            var compound = new CodeFile("short", "X.cs", "native", nameAlternatives: names, preparedGroupingCount: 1);
            Assert.That(compound.SelectPreparedForms(true, true, true).Content, Is.EqualTo("native"));
            var unknown = new CodeFile("short", "X.cs", "native", preparedGroupingCount: 1);
            Assert.That(unknown.SelectPreparedForms(true, true, true).Content, Is.EqualTo("native"));
            var nameOnly = new CodeFile("n", "X.cs", "native", nameAlternatives: names);
            Assert.That(nameOnly.SelectPreparedForms(true, true, false).Content, Is.EqualTo("n"));
        }

        private sealed class IdentityRewriter : LibraryRewriter { }

        private class PipelineGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            internal bool SourceCurrent { get; set; } = true;
            internal Action? OnNextSourceCheck { get; set; }
            private bool CheckSource()
            {
                var callback = OnNextSourceCheck;
                OnNextSourceCheck = null;
                callback?.Invoke();
                return SourceCurrent;
            }
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
                            "task05-pipeline", compilation, ["controlled-pipeline-inputs"], CheckSource);
                    }
                }
            }
        }

        // Refuse the new permission when the virtual getter does not expose the
        // model installed in the native base slot. No virtual call is made at seal.
        private sealed class UnbackedSourceModelGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output)
            : PipelineGenerator(context, input, output)
        {
            private SourceInputModel? _view;
            public override SourceInputModel SourceInputModel
            {
                get => _view ?? base.SourceInputModel;
                internal set
                {
                    base.SourceInputModel = value;
                    _view = new SourceInputModel((CSharpCompilation)value.Customization!, null)
                    {
                        PreEmissionSourceInputs = value.PreEmissionSourceInputs
                    };
                }
            }
        }

        private sealed class GroupOwner : TypeProvider
        {
            internal VariableExpression? Right { get; private set; }
            internal BinaryOperatorExpression? Binary { get; private set; }
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Right = y;
                Binary = new BinaryOperatorExpression("==", x, Right);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y }), Return(Binary), this)];
            }
        }

        private sealed class Widget : TypeProvider
        {
            protected override string BuildName() => "Widget";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Widget.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class UnknownEmptyInterfaces : IReadOnlyList<CSharpType>
        {
            internal bool Armed;
            internal int Reads;
            public int Count
            {
                get
                {
                    if (Armed) { throw new InvalidOperationException("Late interface Count must not be invoked"); }
                    Reads++;
                    return 0;
                }
            }
            public CSharpType this[int index] => throw new IndexOutOfRangeException();
            public IEnumerator<CSharpType> GetEnumerator() => ((IEnumerable<CSharpType>)Array.Empty<CSharpType>()).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class NullScopeFieldOwner(CSharpType target) : FieldOwner(target)
        {
            internal bool Armed;
            protected override WhereExpression? BuildWhereClause()
            {
                if (Armed) { throw new InvalidOperationException("Late null-returning scope builder must not be invoked"); }
                return null;
            }
        }

        private class FieldOwner(CSharpType target) : TypeProvider
        {
            internal Action? OnSharedPathCheck;
            protected override string BuildName() => "Consumer";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                if (OnSharedPathCheck is { } callback && new System.Diagnostics.StackTrace().GetFrames()
                    .Any(frame => frame.GetMethod()?.Name == "CanCommitSharedSnapshot"))
                {
                    OnSharedPathCheck = null;
                    callback();
                }
                return "Consumer.cs";
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, target, "_value", this)];
        }
    }
}

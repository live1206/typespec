// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
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
    internal class PocBinaryParentRoleNativeEvidenceTests
    {
        [TestCase("stable")]
        [TestCase("later-path")]
        [TestCase("later-serialization")]
        [TestCase("written-name")]
        [TestCase("extra-native-file")]
        [TestCase("operand")]
        [TestCase("formal")]
        [TestCase("source")]
        [TestCase("legacy-observes-path")]
        [TestCase("same-file-direct")]
        [TestCase("same-file-direct-later-path")]
        [TestCase("path-restore")]
        [TestCase("operand-restore")]
        [TestCase("two-grouping-files")]
        [TestCase("two-grouping-source")]
        [TestCase("two-grouping-host")]
        [TestCase("two-grouping-method")]
        [TestCase("two-grouping-header")]
        public async Task SelectionUsesActualNativeFilesAndRelevantCurrentSource(string mode)
        {
            var directory = Path.Combine(Path.GetTempPath(), "task05-native-evidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("NativeEvidence")));
                var clock = new Clock(mode.StartsWith("same-file-direct", StringComparison.Ordinal));
                var widget = new Widget();
                var consumer = new Consumer(widget.Type);
                var control = new Control();
                var otherClock = new Clock(false, "Other");
                bool twoFiles = mode.StartsWith("two-grouping-", StringComparison.Ordinal);
                bool serialization = mode == "later-serialization";
                if (serialization)
                { clock.Update(serializations: clock.Serializations); }
                var output = twoFiles ? new TestOutputLibrary(clock, otherClock) : serialization ? new TestOutputLibrary(clock) : mode == "legacy-observes-path"
                    ? new TestOutputLibrary(control, clock, consumer, widget) : new TestOutputLibrary(clock, consumer, widget);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
                CodeModelGenerator.Instance = generator;
                PreparedFieldTypes? prepared = null;
                CodeFile? rawClock = null;
                int uses = 0;
                int otherUses = 0;
                int mutations = 0;
                int innerVisits = 0;
                bool decisionSeen = false;
                bool allowed = false;
                var selected = new Dictionary<string, CodeFile>();
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = value =>
                    {
                        prepared = value;
                        Assert.That(value, Is.Not.Null);
                        if (mode == "written-name")
                        { clock.PathValue = "Actual.cs"; mutations++; }
                        value!.PocNativeWriteObserver = node =>
                        {
                            if (!ReferenceEquals(node, clock.Inner))
                            { return; }
                            innerVisits++;
                            if (mode == "two-grouping-host")
                            {
                                int hostCalls = 0;
                                generator.OnSourceCheck = () =>
                                {
                                    // First is the existing cursor observation;
                                    // second is the new use's host observation.
                                    if (++hostCalls != 2)
                                    { return; }
                                    generator.OnSourceCheck = null;
                                    clock.Leaf!.Update(type: typeof(long));
                                    mutations++;
                                };
                            }
                        };
                    },
                    PocRawFileObserved = (value, provider, file) =>
                    {
                        if (ReferenceEquals(provider, otherClock))
                        { otherUses = value!.PocGroupingUses; }
                        if (!ReferenceEquals(provider, clock))
                        { return; }
                        rawClock = file;
                        uses = value!.PocGroupingUses;
                        TestContext.WriteLine($"NATIVE-EVIDENCE mode={mode}; plans={value.PocGroupingCount}; uses={uses}; rawName={file.Name}; short={file.Content.Contains("return x + y == z;")}");
                        Assert.That(file.Content, Does.Contain(mode == "two-grouping-host"
                            ? "return (x + y) == z;" : "return x + y == z;"));
                        if (mode is "later-path" or "legacy-observes-path" or "same-file-direct-later-path")
                        { clock.PathValue = "GetterOnlyLater.cs"; mutations++; }
                        if (mode == "path-restore")
                        {
                            clock.PathValue = "TransientGetterOnly.cs";
                            Assert.That(value.CanCommit(), Is.False, "Observe real legacy path expiry before restoring only the getter's backing value");
                            clock.PathValue = "Clock.cs";
                            mutations++;
                        }
                        if (mode == "extra-native-file")
                        {
                            // A genuine extra native write, absent from the staged
                            // output inventory: not a later getter-only value.
                            _ = new TypeProviderWriter(new Extra()) { PreparedFieldTypes = value }.Write();
                            mutations++;
                        }
                    },
                    PocGroupingDecisionObserved = (file, value) =>
                    {
                        if (file.Name != (mode == "written-name" ? "Actual.cs" : "Clock.cs"))
                        { return; }
                        decisionSeen = true;
                        allowed = value;
                        if (mode == "later-serialization")
                        { clock.Serializations.Items.Add(new Extra()); mutations++; }
                        if (mode == "operand")
                        { clock.Leaf!.Update(type: typeof(long)); mutations++; }
                        if (mode == "operand-restore")
                        {
                            clock.Leaf!.Update(type: typeof(long));
                            clock.Leaf.Update(type: typeof(int));
                            mutations++;
                        }
                        if (mode == "formal")
                        { clock.Formal!.Update(type: typeof(long)); mutations++; }
                        if (mode is "source" or "two-grouping-source")
                        { generator.SourceCurrent = false; mutations++; }
                        if (mode == "two-grouping-files")
                        { clock.Leaf!.Update(type: typeof(long)); mutations++; }
                        if (mode == "two-grouping-header")
                        {
                            clock.Update(modifiers: TypeSignatureModifiers.Public | TypeSignatureModifiers.Struct);
                            mutations++;
                        }
                        if (mode == "two-grouping-method")
                        {
                            clock.Method!.Update(bodyStatements: Return(new BinaryOperatorExpression("==", clock.Leaf!, clock.Leaf!)));
                            mutations++;
                        }
                    },
                    PocSelectedFileObserved = file => selected.Add(file.Name, file)
                };
                await pipeline.ExecuteAsync();
                Assert.That(prepared, Is.Not.Null);
                Assert.That(rawClock, Is.Not.Null);
                Assert.That(decisionSeen, Is.True);
                Assert.That(innerVisits, Is.EqualTo(1));
                int expectedUses = mode == "two-grouping-host" ? 1 :
                    mode == "legacy-observes-path" || mode.StartsWith("same-file-direct", StringComparison.Ordinal) ? 3 : 2;
                Assert.That(uses, Is.EqualTo(expectedUses));
                Assert.That(mutations, Is.EqualTo(mode is "stable" or "same-file-direct" ? 0 : 1));
                string name = mode == "written-name" ? "Actual.cs" : "Clock.cs";
                Assert.That(rawClock!.Name, Is.EqualTo(name));
                Assert.That(selected.Keys, Is.EquivalentTo(twoFiles ? new[] { name, "Other.cs" } : serialization ? new[] { name } : mode == "legacy-observes-path"
                    ? new[] { "Control.cs", name, "Consumer.cs", "Widget.cs" } : new[] { name, "Consumer.cs", "Widget.cs" }));
                bool shortForm = mode is "stable" or "later-path" or "later-serialization" or "legacy-observes-path" or
                    "same-file-direct" or "same-file-direct-later-path" or "path-restore";
                Assert.That(selected[name].Content, Does.Contain(shortForm ? "return x + y == z;" : "return ((x + y) == z);"));
                Assert.That(allowed, Is.EqualTo(mode is not "written-name" and not "extra-native-file" and not "two-grouping-host"),
                    "Later relevant mutations must be caught after the first callbackful decision, not mistaken for unused permission");
                if (mode is "later-path" or "legacy-observes-path" or "same-file-direct-later-path")
                {
                    Assert.That(clock.PathValue, Is.EqualTo("GetterOnlyLater.cs"));
                    Assert.That(selected.ContainsKey("GetterOnlyLater.cs"), Is.False);
                }
                if (serialization)
                { Assert.That(clock.Serializations.Items.Count, Is.EqualTo(1)); }
                if (mode == "legacy-observes-path")
                {
                    Assert.That(selected["Control.cs"].Content, Does.Contain("return (x == y);"),
                        "The original direct family must retain its live global freshness behavior");
                }
                if (mode.StartsWith("same-file-direct", StringComparison.Ordinal))
                {
                    Assert.That(selected[name].Content, Does.Contain("return x == y;"),
                        "This direct-shaped occurrence shares a newly admitted file, not baseline legacy whole-file admission");
                }
                if (twoFiles)
                {
                    Assert.That(otherUses, Is.EqualTo(mode == "two-grouping-host" ? 3 : 4),
                        "The unexpired other file must have two real native grouping uses even when the first file declines its child");
                    Assert.That(selected["Other.cs"].Content, Does.Contain(mode is "two-grouping-source" or "two-grouping-header"
                        ? "return ((x + y) == z);" : "return x + y == z;"),
                        "A file-local operand expiry must not poison another grouping file; shared source expiry must veto both");
                }
                if (!serialization && !twoFiles)
                {
                    bool fieldShort = mode is "stable" or "same-file-direct" or "operand" or "operand-restore" or "formal" or "extra-native-file";
                    Assert.That(selected["Consumer.cs"].Content, Does.Contain(fieldShort
                        ? "private Widget _value;" : "private global::Sample.Widget _value;"));
                }
                Assert.That(clock.Outer!.AddParentheses && clock.Inner!.AddParentheses, Is.True);
                PreparedParenthesesTests.AssertOperations(rawClock.QualifiedContent!, selected[name].Content);
                object?[][] arguments = [[1, 2, 3], [-1, 2, 1], [0, 0, 1]];
                var actual = Compile("SelectedNativeEvidence", selected.Values.Select(file =>
                    CSharpSyntaxTree.ParseText(file.Content, path: file.Name)));
                var runtime = PreparedGeneratedRuntime.Invoke(actual, "Sample.Clock", "M", arguments);
                Assert.That(runtime, Is.EqualTo(new object?[] { true, true, false }));
                var published = Compile("PublishedNativeEvidence", Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)));
                Assert.That(PreparedGeneratedRuntime.Invoke(published, "Sample.Clock", "M", arguments), Is.EqualTo(runtime));
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        private static CSharpCompilation Compile(string name, IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(name,
            trees, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            internal bool SourceCurrent = true;
            internal Action? OnSourceCheck { get; set; }
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
                            "native-evidence", compilation, ["controlled-native-evidence-inputs"], () =>
                            {
                                OnSourceCheck?.Invoke();
                                return SourceCurrent;
                            });
                    }
                }
            }
        }

        private sealed class Clock(bool direct, string name = "Clock") : TypeProvider
        {
            internal string PathValue = name + ".cs";
            internal readonly SerializationCarrier Serializations = new();
            internal ParameterProvider? Formal;
            internal MethodProvider? Method;
            internal VariableExpression? Leaf;
            internal BinaryOperatorExpression? Inner, Outer;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => PathValue;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Formal = x;
                Leaf = x;
                Inner = new BinaryOperatorExpression("+", Leaf, y);
                Outer = new BinaryOperatorExpression("==", Inner, z);
                var method = new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public,
                    typeof(bool), null, new ParameterProvider[] { x, y, z }), Return(Outer), this);
                Method = method;
                return direct ? [method, Direct(this, "Direct")] : [method];
            }
        }

        private sealed class SerializationCarrier : IReadOnlyList<TypeProvider>
        {
            internal readonly List<TypeProvider> Items = [];
            public int Count => Items.Count;
            public TypeProvider this[int index] => Items[index];
            public IEnumerator<TypeProvider> GetEnumerator() => Items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static MethodProvider Direct(TypeProvider owner, string name)
        {
            var x = new ParameterProvider("x", $"", typeof(int));
            var y = new ParameterProvider("y", $"", typeof(int));
            return new MethodProvider(new MethodSignature(name, null, MethodSignatureModifiers.Public, typeof(bool), null,
                new ParameterProvider[] { x, y }), Return(new BinaryOperatorExpression("==", x, y)), owner);
        }
        private sealed class Control : TypeProvider
        {
            protected override string BuildName() => "Control";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Control.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods() => [Direct(this, "M")];
        }
        private sealed class Extra : TypeProvider
        {
            protected override string BuildName() => "Extra";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Extra.cs";
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

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal class PocBinaryParentRoleDescriptorCollisionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task TransientCapturedDescriptorCollisionRetainsWithoutThrowing(bool collide)
        {
            var directory = Path.Combine(Path.GetTempPath(), "task05-descriptor-collision", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("DescriptorCollision")));
                var state = new CollisionState { Enabled = collide };
                var first = new Clock("First", state, first: true);
                var second = new Clock("Second", state, first: false);
                state.Second = second;
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(first, second));
                CodeModelGenerator.Instance = generator;
                var selected = new Dictionary<string, CodeFile>();
                int uses = 0;
                int plans = -1;
                Exception? failure = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        plans = prepared!.PocGroupingCount;
                        TestContext.WriteLine($"DESCRIPTOR-PREP collide={collide}; refreshed={state.Refreshed}; cachedSecond={second.CachedRelativeFilePath}; liveSecondBacking={second.PathValue}; plans={prepared!.PocGroupingCount}");
                    },
                    PocRawFileObserved = (prepared, _, _) => uses = prepared!.PocGroupingUses,
                    PocSelectedFileObserved = file => selected.Add(file.Name, file)
                };
                try
                { await pipeline.ExecuteAsync(); }
                catch (Exception exception) { failure = exception; }
                TestContext.WriteLine($"DESCRIPTOR-COLLISION collide={collide}; refreshed={state.Refreshed}; baseHook={state.HookStack}; uses={uses}; selected={selected.Count}; failure={failure?.GetType().Name ?? "none"}");
                Assert.That(state.Refreshed, Is.EqualTo(collide), "The collision must occur after the first native path inventory");
                Assert.That(second.PathValue, Is.EqualTo("Second.cs"), "Actual native output paths remain distinct");
                Assert.That(failure, Is.Null, "Ambiguous newly captured physical descriptors must retain, not abort generation");
                Assert.That(selected.Keys, Is.EquivalentTo(new[] { "First.cs", "Second.cs" }));
                Assert.That(plans, Is.EqualTo(collide ? 0 : 4));
                Assert.That(uses, Is.EqualTo(collide ? 0 : 4));
                foreach (var file in selected.Values)
                {
                    Assert.That(file.Content, Does.Contain(collide ? "return ((x + y) == z);" : "return x + y == z;"));
                    var compilation = CSharpCompilation.Create("ActualDescriptor" + file.Name,
                        [CSharpSyntaxTree.ParseText(file.Content, path: file.Name)],
                        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                    Assert.That(PreparedGeneratedRuntime.Invoke(compilation, "Sample." + Path.GetFileNameWithoutExtension(file.Name), "M", [[1, 2, 3], [0, 0, 1]]),
                        Is.EqualTo(new object?[] { true, false }));
                }
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        [Test]
        public Task OrdinaryRelativeNameRemainsEligible() => CheckRootedName("Clock.cs", expected: true);

        [TestCase("drive-relative")]
        [TestCase("root-relative")]
        [Platform("Win")]
        public Task PartiallyQualifiedWindowsNamesRetainNative(string mode)
        {
            var directory = Path.GetFullPath(Path.GetTempPath());
            string name = mode == "drive-relative" ? Path.GetPathRoot(directory)![0] + ":Clock.cs" :
                Path.DirectorySeparatorChar + Path.GetRelativePath(Path.GetPathRoot(directory)!, Path.Combine(directory, "Clock.cs"));
            Assert.That(Path.IsPathRooted(name), Is.True);
            Assert.That(Path.IsPathFullyQualified(name), Is.False);
            return CheckRootedName(name, expected: false);
        }

        private static async Task CheckRootedName(string name, bool expected)
        {
            var directory = Path.Combine(Path.GetTempPath(), "task05-rooted-name", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // On Windows, keep partially-qualified names lexically within
                // this root so containment is not what proves strict relativity.
                if (Path.IsPathRooted(name) && !Path.IsPathFullyQualified(name) && name.StartsWith(Path.DirectorySeparatorChar))
                { name = Path.DirectorySeparatorChar + Path.GetRelativePath(Path.GetPathRoot(directory)!, Path.Combine(directory, "Clock.cs")); }
                var compilation = CSharpCompilation.Create("RootedName", references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(compilation));
                var owner = new Clock("Clock", new CollisionState(), first: false) { PathValue = name };
                var output = new TestOutputLibrary(owner);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, output);
                generator.SourceInputModel = new SourceInputModel(compilation, null)
                {
                    PreEmissionSourceInputs = new CompleteSourceInputSnapshot(directory, "controlled-rooted-name", compilation, ["immutable-inputs"], () => true)
                };
                CodeModelGenerator.Instance = generator;
                var roots = output.TypeProviders;
                foreach (var root in roots)
                { root.ProcessTypeForBackCompatibility(); }
                using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
                using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null);
                var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                bool allowed = prepared!.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(2));
                Assert.That(raw.SelectPreparedForms(true, true, allowed).Content,
                    Does.Contain(expected ? "return x + y == z;" : "return ((x + y) == z);"));
                Assert.That(Directory.GetFiles(directory), Is.Empty);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        private sealed class CollisionState
        {
            internal bool Enabled;
            internal bool PathsSeen;
            internal bool Refreshed;
            internal string? HookStack;
            internal Clock? Second;
        }
        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
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
                            "descriptor-collision", compilation, ["immutable-controlled-inputs"], () => true);
                    }
                }
            }
        }
        private sealed class Clock(string name, CollisionState state, bool first) : TypeProvider
        {
            internal string PathValue = name + ".cs";
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                if (!first && state.Enabled)
                { state.PathsSeen = true; }
                return PathValue;
            }
            protected override CSharpType? BuildBaseType()
            {
                // Require the actual field-scope phase, after the live path loop.
                if (first && state.Enabled && state.PathsSeen && !state.Refreshed &&
                    new StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "get_HasSimpleFieldTypeScope"))
                {
                    var other = state.Second!;
                    state.Refreshed = true;
                    state.HookStack = string.Join(">", new StackTrace().GetFrames().Take(12).Select(frame => frame.GetMethod()?.Name));
                    other.PathValue = "First.cs";
                    _ = other.RelativeFilePath; // Refresh observed cache only; no declaration mutation.
                    other.PathValue = "Second.cs";
                }
                return null;
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    new ParameterProvider[] { x, y, z }), Return(new BinaryOperatorExpression("==", new BinaryOperatorExpression("+", x, y), z)), this)];
            }
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public async Task ReentrantGeneratorSwitchCannotBindAnotherGeneratorsOutput()
        {
            using var globalState = new GeneratorGlobalStateSnapshot();
            var directory = Path.Combine(Path.GetTempPath(), "typespec-generation-owner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                const string config = "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Sample\",\"unreferenced-types-handling\":\"keepAll\"}";
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory, configuration: config,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
                var target = Target();
                var owner = new FieldOwner(target.Type);
                TypeProvider[] roots = [owner, target];
                var expectedOutput = new TestOutputLibrary(roots);
                var foreignOutput = new TestOutputLibrary(roots);
                var context = new GeneratorContext(mock.Object.Configuration);
                var first = new SwitchingSourceGenerator(context, mock.Object.InputLibrary, mock.Object.TypeFactory, expectedOutput);
                var second = new RestoringOutputGenerator(context, mock.Object.InputLibrary, mock.Object.TypeFactory, foreignOutput, first);
                first.SwitchTo = second;
                CodeModelGenerator.Instance = first;
                var generation = new CSharpGen();
                await generation.ExecuteAsync();
                Assert.That(first.Switched, Is.True);
                Assert.That(second.GetterCalls, Is.EqualTo(1), "The existing output getter is called exactly once on the switched receiver.");
                Assert.That(CodeModelGenerator.Instance, Is.SameAs(first));
                Assert.That(first.OutputLibrary, Is.SameAs(expectedOutput));
                Assert.That(foreignOutput, Is.Not.SameAs(expectedOutput));
                Assert.That(generation.LastObservedOwnedRootPhase, Is.EqualTo(false),
                    "Restoring the original generator must not bind an output obtained from a different generator.");
                Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "Clock.cs")), Does.Contain("private Widget _value;"),
                    "A phase-only failure must not discard the independently prepared field output.");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        [Test]
        public async Task ForwardedOutputObservationDoesNotClaimExclusiveProvenance()
        {
            using var globalState = new GeneratorGlobalStateSnapshot();
            var directory = Path.Combine(Path.GetTempPath(), "typespec-forwarded-owner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                const string config = "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Sample\",\"unreferenced-types-handling\":\"keepAll\"}";
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory, configuration: config,
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
                var target = Target();
                var owner = new FieldOwner(target.Type);
                var foreignOutput = new TestOutputLibrary(owner, target);
                var context = new GeneratorContext(mock.Object.Configuration);
                var second = new ForwardedOutputGenerator(context, mock.Object.InputLibrary, mock.Object.TypeFactory, foreignOutput);
                var first = new ForwardingGenerator(context, mock.Object.InputLibrary, mock.Object.TypeFactory, second);
                CodeModelGenerator.Instance = first;
                var generation = new CSharpGen();
                await generation.ExecuteAsync();
                Assert.That(first.GetterCalls, Is.GreaterThan(0));
                Assert.That(second.GetterCalls, Is.GreaterThan(0));
                Assert.That(generation.LastObservedOwnedRootPhase, Is.EqualTo(true),
                    "The observation binds A as direct receiver and the returned output; it does not prove that B cannot also reference that output.");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }

        private sealed class ForwardingGenerator(GeneratorContext context, InputLibrary input, TypeFactory factory, CodeModelGenerator forwarded)
            : CodeModelGenerator(context)
        {
            internal int GetterCalls { get; private set; }
            public override InputLibrary InputLibrary => input;
            public override TypeFactory TypeFactory => factory;
            public override OutputLibrary OutputLibrary { get { GetterCalls++; return forwarded.OutputLibrary; } }
        }

        private sealed class ForwardedOutputGenerator(GeneratorContext context, InputLibrary input, TypeFactory factory, OutputLibrary output)
            : CodeModelGenerator(context)
        {
            internal int GetterCalls { get; private set; }
            public override InputLibrary InputLibrary => input;
            public override TypeFactory TypeFactory => factory;
            public override OutputLibrary OutputLibrary { get { GetterCalls++; return output; } }
        }

        private sealed class SwitchingSourceGenerator(GeneratorContext context, InputLibrary input, TypeFactory factory, OutputLibrary output)
            : CodeModelGenerator(context)
        {
            internal CodeModelGenerator? SwitchTo { get; set; }
            internal bool Switched { get; private set; }
            public override InputLibrary InputLibrary => input;
            public override TypeFactory TypeFactory => factory;
            public override OutputLibrary OutputLibrary => output;
            public override SourceInputModel SourceInputModel
            {
                get => base.SourceInputModel;
                internal set
                {
                    base.SourceInputModel = value;
                    if (value.Customization is CSharpCompilation compilation)
                    {
                        value.PreEmissionSourceInputs = new CompleteSourceInputSnapshot(Configuration.OutputDirectory, "controlled-owner-test",
                            compilation, ["controlled-test-build"], () => true);
                    }
                    SwitchTo!.SourceInputModel = value;
                    Switched = true;
                    CodeModelGenerator.Instance = SwitchTo;
                }
            }
        }

        private sealed class RestoringOutputGenerator(GeneratorContext context, InputLibrary input, TypeFactory factory,
            OutputLibrary output, CodeModelGenerator previous) : CodeModelGenerator(context)
        {
            internal int GetterCalls { get; private set; }
            public override InputLibrary InputLibrary => input;
            public override TypeFactory TypeFactory => factory;
            public override OutputLibrary OutputLibrary
            {
                get
                {
                    GetterCalls++;
                    CodeModelGenerator.Instance = previous;
                    return output;
                }
            }
        }
    }
}

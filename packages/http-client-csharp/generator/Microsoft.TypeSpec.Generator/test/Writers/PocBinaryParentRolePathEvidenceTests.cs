// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
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
    internal class PocBinaryParentRolePathEvidenceTests
    {
        [TestCase("stable")]
        [TestCase("case-escape")]
        [TestCase("late-link")]
        public async Task ActualOutputNamesRequireCurrentContainmentAndNoReparsePoint(string mode)
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "task05-path-evidence", Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(sandbox, "root");
            var caseSibling = Path.Combine(sandbox, "ROOT");
            var linkedTarget = Path.Combine(sandbox, "link-target");
            Directory.CreateDirectory(directory);
            try
            {
                if (mode == "case-escape" && Directory.Exists(caseSibling))
                { Assert.Ignore("This filesystem treats the case-only sibling as the output directory"); }
                if (mode == "late-link" && OperatingSystem.IsWindows())
                { Assert.Ignore("The controlled symbolic-link fixture requires unprivileged Unix symlink creation"); }
                string name = mode == "case-escape" ? "../ROOT/Clock.cs" : mode == "late-link" ? "alias/Clock.cs" : "Clock.cs";
                var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: directory,
                    configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                    compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("PathEvidence")));
                var owner = new Clock(name);
                var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
                CodeModelGenerator.Instance = generator;
                int uses = 0;
                int visits = 0;
                bool decisionSeen = false;
                CodeFile? selected = null;
                CodeFile? raw = null;
                var pipeline = new CSharpGen
                {
                    PocPreparationObserved = prepared =>
                    {
                        Assert.That(prepared, Is.Not.Null);
                        prepared!.PocNativeWriteObserver = node => { if (ReferenceEquals(node, owner.Inner)) { visits++; } };
                    },
                    PocRawFileObserved = (prepared, provider, file) =>
                    {
                        Assert.That(ReferenceEquals(provider, owner), Is.True);
                        raw = file;
                        uses = prepared!.PocGroupingUses;
                        Assert.That(file.Name, Is.EqualTo(name));
                        Assert.That(file.Content, Does.Contain("return x + y == z;"));
                    },
                    PocGroupingDecisionObserved = (file, allowed) =>
                    {
                        decisionSeen = true;
                        if (mode == "late-link")
                        {
                            Assert.That(allowed, Is.True, "The initially ordinary actual path must genuinely admit before the link appears");
                            Directory.CreateDirectory(linkedTarget);
                            Directory.CreateSymbolicLink(Path.Combine(directory, "alias"), linkedTarget);
                        }
                    },
                    PocSelectedFileObserved = file => selected = file
                };
                await pipeline.ExecuteAsync();
                Assert.That(uses, Is.EqualTo(2));
                Assert.That(visits, Is.EqualTo(1));
                Assert.That(decisionSeen, Is.True);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected!.Name, Is.EqualTo(name));
                // This controlled host has one output and fixed immutable inputs:
                // physical distinctness is truthful even when that sole output is
                // redirected. It does not claim containment or immutable link state.
                Assert.That(generator.SourceChecks, Is.GreaterThan(0));
                if (mode == "late-link")
                {
                    Assert.That(new DirectoryInfo(Path.Combine(directory, "alias")).ResolveLinkTarget(true)?.FullName,
                        Is.EqualTo(linkedTarget));
                }
                if (mode != "stable")
                {
                    var compilerContext = new CompilerBuildGenerationContext(directory, null!);
                    Assert.Throws<InvalidOperationException>(() => compilerContext.ResolveOutputFile(name),
                        "The existing compiler-host resolver must independently retain its stricter path guard");
                }
                TestContext.WriteLine($"PATH-EVIDENCE mode={mode}; uses={uses}; visits={visits}; actualName={selected.Name}; short={selected.Content.Contains("return x + y == z;")}; sourceChecks={generator.SourceChecks}");
                Assert.That(selected.Content, Does.Contain(mode == "stable" ? "return x + y == z;" : "return ((x + y) == z);"));
                Assert.That(raw, Is.Not.Null);
                PreparedParenthesesTests.AssertOperations(raw!.QualifiedContent!, selected.Content);
                var actual = CSharpCompilation.Create("ActualPathEvidence",
                    [CSharpSyntaxTree.ParseText(selected.Content, path: selected.Name)],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.That(PreparedGeneratedRuntime.Invoke(actual, "Sample.Clock", "M", [[1, 2, 3], [0, 0, 1]]),
                    Is.EqualTo(new object?[] { true, false }));
            }
            finally { Directory.Delete(sandbox, recursive: true); }
        }

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            internal int SourceChecks;
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
                            "controlled-single-physical-output", compilation, ["immutable-controlled-inputs"], () => { SourceChecks++; return true; });
                    }
                }
            }
        }
        private sealed class Clock(string path) : TypeProvider
        {
            internal BinaryOperatorExpression? Inner;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                var z = new ParameterProvider("z", $"", typeof(int));
                Inner = new BinaryOperatorExpression("+", x, y);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null,
                    new ParameterProvider[] { x, y, z }), Return(new BinaryOperatorExpression("==", Inner, z)), this)];
            }
        }
    }
}

// Diagnostic-only test; no production permission is created by the observers below.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;
using static Microsoft.TypeSpec.Generator.SourceInput.CompilerBuildInputProof;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [NonParallelizable]
    internal class Task05HostOrderingDiagnostics
    {
        [TestCase(false)]
        [TestCase(true)]
        public Task PhysicalCompilerHostFinalPathCurrentness(bool invalidate) => RunPhysicalCompilerHost(invalidate, physicalOnly: false);

        [Test]
        public Task PhysicalInputHashExpirySelectsNativeWhileSessionEpochStaysCurrent() => RunPhysicalCompilerHost(invalidate: true, physicalOnly: true);

        private async Task RunPhysicalCompilerHost(bool invalidate, bool physicalOnly)
        {
            var root = Path.Combine(Path.GetTempPath(), "host-ordering-" + (invalidate ? "invalidate" : "stable") + "-" + Guid.NewGuid().ToString("N"));
            var original = Path.Combine(root, "original");
            var isolated = Path.Combine(root, "isolated");
            var source = Path.Combine(root, "linked.cs");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(isolated);
            File.WriteAllText(source, "namespace Other { class Unrelated {} }\n");
            var events = new List<string>();
            var mock = await MockHelpers.LoadMockGeneratorAsync(outputPath: original,
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(CSharpCompilation.Create("Customization")));
            var session = new Session(original, source, events);
            var group = new GroupOwner();
            var target = new Widget(session, invalidate, source, events, physicalOnly);
            var field = new FieldOwner(target.Type);
            var generator = new FixtureGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary,
                new TestOutputLibrary(group, field, target));
            CodeModelGenerator.Instance = generator;
            var selected = new Dictionary<string, CodeFile>();
            bool? allowed = null, hostAtSelection = null;
            CompleteSourceInputSnapshot? capturedProof = null;
            int uses = 0, writes = 0;
            var pipeline = new CSharpGen
            {
                PocPreparationObserved = prepared =>
                {
                    capturedProof = generator.CompilerBuildContext?.SourceProof;
                    events.Add($"prepared={prepared is not null} physicalProof={capturedProof is not null}");
                    if (prepared is not null) prepared.PocNativeWriteObserver = node => { if (ReferenceEquals(node, group.Binary)) writes++; };
                },
                PocRawFileObserved = (prepared, provider, file) =>
                {
                    if (ReferenceEquals(provider, group))
                    {
                        uses = prepared!.PocGroupingUses;
                        events.Add($"raw group={file.PreparedGroupingCount} shortened={file.Content.Contains("return x == y;")} qualified={file.QualifiedContent?.Contains("return (x == y);")} uses={uses}");
                    }
                },
                PocGroupingDecisionObserved = (file, value) =>
                {
                    if (file.Name == "Clock.cs")
                    {
                        allowed = value;
                        session.AfterDecision = true;
                        events.Add("decision=" + value);
                    }
                },
                PocSelectedFileObserved = file =>
                {
                    selected.Add(file.Name, file);
                    if (file.Name == "Clock.cs")
                    {
                        hostAtSelection = session.Current;
                        events.Add($"selected shortened={file.Content.Contains("return x == y;")} qualified={file.Content.Contains("return (x == y);")} host={hostAtSelection}");
                    }
                }
            };
            try
            {
                var result = await pipeline.ExecuteForCompilerBuildAsync(isolated, session);
                var diskPath = Path.Combine(isolated, "Clock.cs");
                var disk = File.Exists(diskPath) ? File.ReadAllText(diskPath) : "<missing>";
                bool? proofAfterResult = capturedProof?.IsCurrentFor(original);
                events.Add($"proofAfterResult={proofAfterResult}");
                events.Add($"finished usedCapturedResidualInputs={result.UsedCapturedResidualInputs} requireDiscard={!result.UsedCapturedResidualInputs} host={session.Current} checks={session.Checks} paths={target.PathReads} invalidations={target.Invalidations} disk={diskPath} diskShort={disk.Contains("return x == y;")} diskQualified={disk.Contains("return (x == y);")}");
                File.WriteAllText(Path.Combine(root, "events.txt"), string.Join("\n", events));
                File.WriteAllText(Path.Combine(root, "selected.txt"), selected.GetValueOrDefault("Clock.cs")?.Content ?? "<missing>");
                TestContext.Progress.WriteLine("HOST-DIAGNOSTIC " + root);
                Assert.Multiple(() =>
                {
                    Assert.That(allowed, Is.True, "actual grouping decision");
                    Assert.That(uses, Is.EqualTo(1));
                    Assert.That(writes, Is.EqualTo(1));
                    Assert.That(session.ResidualSeen, Is.True, "physical residual check observed");
                    Assert.That(target.Invalidations, Is.EqualTo(invalidate ? 1 : 0));
                    Assert.That(capturedProof, Is.Not.Null, "actual compiler host created and applied physical proof");
                    Assert.That(hostAtSelection, Is.EqualTo(!invalidate || physicalOnly), "host epoch at selection without querying proof");
                    Assert.That(proofAfterResult, Is.EqualTo(!invalidate), "physical proof after result");
                    Assert.That(result.UsedCapturedResidualInputs, Is.EqualTo(!invalidate), "false requires trusted caller discard");
                    Assert.That(File.Exists(diskPath), Is.True, "isolated output was written before result");
                    Assert.That(selected["Clock.cs"].Content, Does.Contain(invalidate ? "return (x == y);" : "return x == y;"), "diagnostic desired fallback after final-path stale event; RED is preserved, not a publication claim");
                });
            }
            catch (Exception ex)
            {
                events.Add("EXCEPTION " + ex);
                File.WriteAllText(Path.Combine(root, "events.txt"), string.Join("\n", events));
                TestContext.Progress.WriteLine("HOST-DIAGNOSTIC " + root);
                throw;
            }
        }

        private sealed class FixtureGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class Session(string outputDirectory, string source, List<string> events) : ITrustedCompilerBuildSession
        {
            private readonly string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            private readonly string corePath = typeof(object).Assembly.Location;
            private readonly string coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location)));
            internal bool Current = true, AfterDecision, ResidualSeen;
            internal int Checks;
            public CompilerBuildInputCapture Capture => new(outputDirectory, "host-ordering-epoch", "Debug|net10.0", "12.0", "Actual", [],
                [new CapturedCompilerSource(source, hash)], [new CapturedCompilerReference(corePath, coreHash, [], false)], false, false,
                new CSharpParseOptions(LanguageVersion.CSharp12), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), null);
            public bool IsCurrent()
            {
                Checks++;
                var stack = new StackTrace().ToString();
                if (AfterDecision)
                {
                    if (stack.Contains("TryCreateResidualWorkspaceAsync")) { ResidualSeen = true; }
                    string phase = stack.Contains("TryCreateResidualWorkspaceAsync") ? "residual" : stack.Contains("ValidateResult") ? "validate-result" : stack.Contains("CanCommitSharedSnapshot") ? "shared" : stack.Contains("PocSelectedFileObserved") ? "selected" : "other";
                    string detail = stack.Contains("CheckSnapshotCurrent") ? "snapshot" : stack.Contains("ObserveSourceSnapshot") ? "observe" : "other";
                    events.Add($"host-check #{Checks} phase={phase}/{detail} current={Current} residualSeen={ResidualSeen}");
                }
                return Current;
            }
        }

        private sealed class GroupOwner : TypeProvider
        {
            internal BinaryOperatorExpression? Binary;
            protected override string BuildName() => "Clock";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Clock.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                var x = new ParameterProvider("x", $"", typeof(int));
                var y = new ParameterProvider("y", $"", typeof(int));
                Binary = new BinaryOperatorExpression("==", x, y);
                return [new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(bool), null, new ParameterProvider[] { x, y }), Return(Binary), this)];
            }
        }
        private sealed class Widget(Session session, bool invalidate, string source, List<string> events, bool physicalOnly) : TypeProvider
        {
            internal int PathReads, Invalidations;
            protected override string BuildName() => "Widget";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath()
            {
                PathReads++;
                var stack = new StackTrace().ToString();
                bool finalSharedPath = session.AfterDecision && session.ResidualSeen && stack.Contains("CheckSnapshotCurrent") && stack.Contains("CanCommitSharedSnapshot");
                if (session.AfterDecision) { events.Add($"widget-path #{PathReads} finalSharedPath={finalSharedPath} host={session.Current} checks={session.Checks}\n{stack}"); }
                if (invalidate && finalSharedPath && Invalidations == 0)
                {
                    File.AppendAllText(source, "// input changed at final shared path callback\n");
                    if (!physicalOnly) { session.Current = false; }
                    Invalidations++;
                    events.Add($"INVALIDATED input, sessionEpochChanged={!physicalOnly}, widget-path #{PathReads}, checks={session.Checks}");
                }
                return "Widget.cs";
            }
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }
        private sealed class FieldOwner(CSharpType target) : TypeProvider
        {
            protected override string BuildName() => "Consumer";
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => "Consumer.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override FieldProvider[] BuildFields() => [new FieldProvider(FieldModifiers.Private, target, "_value", this)];
        }
    }
}

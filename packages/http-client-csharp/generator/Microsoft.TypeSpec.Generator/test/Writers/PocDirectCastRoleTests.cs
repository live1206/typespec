// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
    internal class PocDirectCastRoleTests
    {
        // Original M2 OuterCastGroupingPreservesConversion cases 1/2.
        // The return prototype has an extra raw wrapper not represented by IR;
        // the native cast writer supplies exactly one removable outer pair.
        [TestCase("identity", false)]
        [TestCase("identity", true)]
        [TestCase("longTarget", false)]
        [TestCase("longTarget", true)]
        [TestCase("literalOperand", false)]
        [TestCase("literalOperand", true)]
        [TestCase("hintAlias", false)]
        [TestCase("hintAlias", true)]
        [TestCase("longOperand", false)]
        [TestCase("longOperand", true)]
        [TestCase("nullableOperand", false)]
        [TestCase("nullableOperand", true)]
        [TestCase("refOperand", false)]
        [TestCase("refOperand", true)]
        [TestCase("extraFormal", false)]
        [TestCase("extraFormal", true)]
        [TestCase("nestedCast", false)]
        [TestCase("nestedCast", true)]
        [TestCase("unaryParent", false)]
        [TestCase("unaryParent", true)]
        [TestCase("memberReceiver", false)]
        [TestCase("memberReceiver", true)]
        [TestCase("unsupportedModifiers", false)]
        [TestCase("unsupportedModifiers", true)]
        [TestCase("unsupportedBase", false)]
        [TestCase("unsupportedBase", true)]
        public async Task NativeDirectCastUsesActualFormalAndPreservesTypeSpelling(string kind, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new CastOwner(kind, arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-direct-cast", compilation, ["controlled-direct-cast-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new CastOwner(kind, arrow);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            var visits = new List<object>();
            prepared!.PocNativeWriteObserver = visits.Add;
            var written = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            bool allowed = prepared.CanCommitPocGrouping(written) && prepared.SealPocGroupingWithoutCallbacks(written);
            var selected = written.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = kind switch
            {
                "literalOperand" => [[]],
                "longOperand" => [[(long)int.MinValue - 1], [(long)int.MaxValue + 1], [17L]],
                "extraFormal" => [[int.MinValue, 0], [int.MaxValue, 1], [17, -2]],
                "memberReceiver" => [[new Exception("first")], [new Exception("second")]],
                _ => [[int.MinValue], [int.MaxValue], [17]]
            };
            object?[] values = kind switch
            {
                "literalOperand" => [22],
                "longTarget" => [(long)int.MinValue, (long)int.MaxValue, 17L],
                "longOperand" => [int.MaxValue, int.MinValue, 17],
                "unaryParent" => [int.MinValue, -int.MaxValue, -17],
                "memberReceiver" => arguments.Select(args => (object?)((Exception)args[0]!).HResult).ToArray(),
                _ => [int.MinValue, int.MaxValue, 17]
            };
            Assert.That(Execute(native, arguments), Is.EqualTo(values));
            Assert.That(Execute(selected, arguments), Is.EqualTo(values));
            int expected = kind is "identity" or "hintAlias" ? 1 : 0;
            string expectedExpression = kind switch
            {
                "identity" or "hintAlias" => "(int)value",
                "longTarget" => "((long)value)",
                "literalOperand" => "((int)22)",
                "nestedCast" => "((int)((int)value))",
                "unaryParent" => "-((int)value)",
                "memberReceiver" => "((global::System.Exception)value).HResult",
                _ => "((int)value)"
            };
            string expression = PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace().ToFullString();
            TestContext.WriteLine($"CAST-ROLE kind={kind}; arrow={arrow}; plans={prepared.PocGroupingCount}; uses={prepared.PocGroupingUses}; physical={written.PreparedGroupingCount}; expression={expression}; castVisits={visits.OfType<CastExpression>().Count()}; receipt={written.SourceConsumption?.WasCurrentAtEndOfWrite}");
            Assert.Multiple(() =>
            {
                Assert.That(prepared.PocGroupingCount, Is.EqualTo(expected), "Exact direct-cast preparation, not legacy final shortening");
                Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
                Assert.That(written.PreparedGroupingCount, Is.EqualTo(expected));
                Assert.That(expression, Is.EqualTo(expectedExpression));
                Assert.That(visits.OfType<CastExpression>().Count(), Is.EqualTo(kind == "nestedCast" ? 2 : 1), "One visit per native cast occurrence");
                Assert.That(visits.Count(node => ReferenceEquals(node, owner.Cast.Inner)), Is.EqualTo(1), "One native operand visit");
                Assert.That(written.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
            });
        }

        [TestCase("stable", false)]
        [TestCase("stable", true)]
        [TestCase("before", false)]
        [TestCase("before", true)]
        [TestCase("opening", false)]
        [TestCase("opening", true)]
        [TestCase("after", false)]
        [TestCase("after", true)]
        [TestCase("host", false)]
        [TestCase("host", true)]
        [TestCase("restore", false)]
        [TestCase("restore", true)]
        [TestCase("formal", false)]
        [TestCase("formal", true)]
        public async Task ChangedCastSourceUsesTheSamePassNativeCompanion(string mode, bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new CastOwner("identity", arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            int openings = 0;
            int mutations = 0;
            void Mutate()
            {
                if (mode == "formal") { owner.Parameter.Update(type: typeof(long)); }
                else
                {
                    owner.Leaf.Update(type: typeof(long));
                    if (mode == "restore") { owner.Leaf.Update(type: typeof(int)); }
                }
                mutations++;
            }
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-direct-cast-mutation", compilation, ["controlled-cast-mutation-inputs"], () =>
                    {
                        if (mode == "host" && openings > 0 && mutations == 0) { Mutate(); }
                        return true;
                    })
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            var nativeOwner = new CastOwner("identity", arrow);
            nativeOwner.ProcessTypeForBackCompatibility();
            var native = new TypeProviderWriter(nativeOwner).Write();
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.PocGroupingCount, Is.EqualTo(1));
            var visits = new List<object>();
            prepared.PocNativeWriteObserver = visits.Add;
            prepared.PocAfterOpening = () =>
            {
                openings++;
                if (mode is "opening" or "restore" or "formal") { Mutate(); }
            };
            if (mode == "before") { Mutate(); }
            var raw = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
            if (mode == "after")
            {
                Assert.That(prepared.CanCommitPocGrouping(raw), Is.True, "A real positive precedes late expiry");
                Mutate();
            }
            int expected = mode == "before" ? 0 : 1;
            Assert.That(openings, Is.EqualTo(expected));
            Assert.That(prepared.PocGroupingUses, Is.EqualTo(expected));
            Assert.That(raw.PreparedGroupingCount, Is.EqualTo(expected));
            bool allowed = prepared.CanCommitPocGrouping(raw) && prepared.SealPocGroupingWithoutCallbacks(raw);
            if (expected > 0) { Assert.That(allowed, Is.EqualTo(mode == "stable")); }
            Assert.That(mutations, Is.EqualTo(mode == "stable" ? 0 : 1));
            var selected = raw.SelectPreparedForms(true, true, allowed);
            Assert.That(selected.Name, Is.EqualTo(native.Name));
            Assert.That(PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace().ToFullString(),
                Is.EqualTo(mode == "stable" ? "(int)value" : "((int)value)"));
            if (mode != "stable")
            {
                Assert.That(selected.Content, Is.EqualTo(raw.QualifiedContent));
                Assert.That(selected.Content, Is.EqualTo(native.Content));
            }
            Assert.That(visits.OfType<CastExpression>().Count(), Is.EqualTo(1));
            Assert.That(visits.Count(node => ReferenceEquals(node, owner.Cast.Inner)), Is.EqualTo(1));
            PreparedParenthesesTests.AssertOperations(native.Content, selected.Content);
            object?[][] arguments = [[int.MinValue], [int.MaxValue], [17]];
            Assert.That(Execute(selected, arguments), Is.EqualTo(Execute(native, arguments)));
            TestContext.WriteLine($"CAST-MUTATION mode={mode}; arrow={arrow}; openings={openings}; uses={prepared.PocGroupingUses}; mutations={mutations}; selected={PreparedParenthesesTests.MethodExpression(selected.Content).NormalizeWhitespace()}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingFormalNameAndHintDoNotEstablishDeclarationOwnership(bool arrow)
        {
            var compilation = Compilation();
            var mock = await MockHelpers.LoadMockGeneratorAsync(
                configuration: "{\"package-name\":\"Sample\",\"enable-pre-emission-simplification\":true,\"disable-xml-docs\":true,\"unreferenced-types-handling\":\"keepAll\"}",
                compilation: () => Task.FromResult<Compilation>(compilation));
            var owner = new CastOwner("unbound", arrow);
            var generator = new NativeGenerator(new GeneratorContext(mock.Object.Configuration), mock.Object.InputLibrary, new TestOutputLibrary(owner));
            generator.SourceInputModel = new SourceInputModel(compilation, null)
            {
                PreEmissionSourceInputs = new CompleteSourceInputSnapshot(generator.Configuration.OutputDirectory,
                    "task05-unbound-cast", compilation, ["controlled-unbound-cast-inputs"], () => true)
            };
            CodeModelGenerator.Instance = generator;
            var roots = generator.OutputLibrary.TypeProviders;
            foreach (var root in roots) { root.ProcessTypeForBackCompatibility(); }
            using var references = ProviderReferenceMapAnalyzer.PrepareForGeneration(roots);
            using var prepared = PreparedFieldTypes.CreateForGeneration(generator, roots, references.ShouldWriteProvider);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(((VariableExpression)owner.Cast.Inner).Declaration, Is.Not.SameAs(owner.Leaf.Declaration));
            Assert.That(prepared!.PocGroupingCount, Is.Zero);
            Assert.That(prepared.PocGroupingUses, Is.Zero);
            // Deliberately only an admission test: an unowned declaration need
            // not be allocated the same textual name by the native writer.
        }

        [TestCase("target")]
        [TestCase("operand")]
        public void CastOperationOracleRejectsForcedCorruption(string kind)
        {
            const string before = "public class Clock { public int M(int value) => ((int)value); }";
            string after = kind == "target"
                ? "public class Clock { public int M(int value) => (long)value; }"
                : "public class Clock { public int M(int value) => (int)(value + 1); }";
            Assert.Throws<AssertionException>(() => PreparedParenthesesTests.AssertOperations(before, after));
        }

        private static CSharpCompilation Compilation() => CSharpCompilation.Create("Task05DirectCast",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static object?[] Execute(CodeFile file, object?[][] arguments) => PreparedGeneratedRuntime.Invoke(
            Compilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(file.Content, path: file.Name)), "Sample.Clock", "M", arguments);

        private sealed class NativeGenerator(GeneratorContext context, InputLibrary input, OutputLibrary output) : CodeModelGenerator(context)
        {
            public override InputLibrary InputLibrary => input;
            public override OutputLibrary OutputLibrary => output;
        }

        private sealed class CastOwner(string kind, bool arrow, string name = "Clock") : TypeProvider
        {
            internal CastExpression Cast { get; private set; } = null!;
            internal VariableExpression Leaf { get; private set; } = null!;
            internal MethodProvider Method { get; private set; } = null!;
            internal ParameterProvider Parameter { get; private set; } = null!;
            protected override string BuildName() => name;
            protected override string BuildNamespace() => "Sample";
            protected override string BuildRelativeFilePath() => name + ".cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected internal override MethodProvider[] BuildMethods()
            {
                CSharpType parameterType = kind switch
                {
                    "longOperand" => typeof(long),
                    "nullableOperand" => typeof(int?),
                    "memberReceiver" => typeof(object),
                    _ => typeof(int)
                };
                Parameter = new ParameterProvider("value", $"", parameterType, isRef: kind == "refOperand")
                {
                    HasUnsupportedParameterModifiers = kind == "unsupportedModifiers"
                };
                Leaf = (VariableExpression)Parameter;
                if (kind == "hintAlias") { Leaf.Update(type: typeof(bool)); }
                ValueExpression operand = kind switch
                {
                    "literalOperand" => new LiteralExpression(22),
                    "nestedCast" => new CastExpression(Leaf, typeof(int)),
                    "unbound" => new VariableExpression(typeof(int), "value"),
                    _ => Leaf
                };
                CSharpType target = kind switch
                {
                    "longTarget" => typeof(long),
                    "memberReceiver" => typeof(Exception),
                    _ => typeof(int)
                };
                Cast = new CastExpression(operand, target);
                ValueExpression body = kind switch
                {
                    "unaryParent" => new UnaryOperatorExpression("-", Cast, OperandOnTheLeft: false),
                    "memberReceiver" => new MemberExpression(Cast, "HResult"),
                    _ => Cast
                };
                var signature = new MethodSignature("M", null, MethodSignatureModifiers.Public,
                    kind == "longTarget" ? typeof(long) : typeof(int), null,
                    kind == "literalOperand" ? [] : kind == "extraFormal" ? [Parameter, new ParameterProvider("other", $"", typeof(int))] : [Parameter])
                {
                    HasUnsupportedBaseContract = kind == "unsupportedBase"
                };
                Method = arrow ? new MethodProvider(signature, body, this) : new MethodProvider(signature, Return(body), this);
                return [Method];
            }
        }
    }
}

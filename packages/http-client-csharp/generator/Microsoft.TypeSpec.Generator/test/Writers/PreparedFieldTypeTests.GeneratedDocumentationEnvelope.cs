// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(4, true)]
        [TestCase(128, false)]
        public void FileDocumentationImportBudgetCoversPossibleNamespaceChains(int count, bool admitted)
        {
            Microsoft.TypeSpec.Generator.Tests.MockHelpers.LoadMockGenerator(includeXmlDocs: true);
            Assert.That(CodeModelGenerator.Instance.Configuration.DisableXmlDocs, Is.False);
            var arguments = Enumerable.Range(0, count).Select(i => (object)new CSharpType("T", $"N{i:D3}",
                false, false, null, Array.Empty<CSharpType>(), true, false)).ToArray();
            var format = string.Join(" ", Enumerable.Range(0, count).Select(i => $"{{{i}}}"));
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(
                new[] { System.Runtime.CompilerServices.FormattableStringFactory.Create(format, arguments) }));
            using var writer = new CodeWriter();
            writer.WriteXmlDocsNoScope(docs);
            Assert.That(writer.ToString(false), Does.Contain("<summary>").And.Contain("N000.T"));
            // Supported, ordinary namespace operands do not establish a bound
            // on the set's possible collision chains. This is a conservative
            // funding control, not a claim that these keys actually collide.
            var envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs);
            Assert.That(envelope is not null, Is.EqualTo(admitted));
            if (envelope is not null) { Assert.That(envelope.IsCurrent, Is.True); }
        }

        [Test]
        public void GeneratedDocumentationEnvelopeAcceptsActualDefaultMethodDocumentation()
        {
            var owner = new GeneratedCallableOwner("observe");
            owner.EnsureBuilt();
            var envelope = CaptureGeneratedDocumentation(owner.Methods[0].XmlDocs);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.True);
        }

        [Test]
        public void GeneratedDocumentationEnvelopeCustomInnerWriterIsNotExecuted()
        {
            var callbacks = 0;
            var inner = new GeneratedCustomDoc(() => callbacks++);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(new FormattableString[] { $"summary" }, inner));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Null);
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public void GeneratedDocumentationEnvelopeCustomFormatterGettersAreNotExecuted()
        {
            var inner = new XmlDocStatement($"<item>", $"</item>", Array.Empty<FormattableString>()) { StartTag = new GeneratedUnknownFormatter() };
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(Array.Empty<FormattableString>(), inner));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Null);
        }

        [Test]
        public void GeneratedDocumentationEnvelopeLineBreakingTagArgumentIsUnknown()
        {
            var inner = new XmlDocStatement($"<item>", $"</item>", Array.Empty<FormattableString>())
            { StartTag = FormattableStringFactory.Create("<item name=\"{0}\">", new object[] { "name\npublic class Escaped {}" }) };
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(Array.Empty<FormattableString>(), inner));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Null);
        }

        [Test]
        public void GeneratedDocumentationEnvelopeRetainedArgumentsInvalidateStickily()
        {
            var arguments = new object[] { "name" };
            var inner = new XmlDocStatement($"<item>", $"</item>", Array.Empty<FormattableString>())
            { StartTag = FormattableStringFactory.Create("<item name=\"{0}\">", arguments) };
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(Array.Empty<FormattableString>(), inner));
            var envelope = CaptureGeneratedDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.True);
            arguments[0] = "changed";
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False);
            arguments[0] = "name";
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False);
        }

        [Test]
        public void GeneratedDocumentationEnvelopeRejectsExceptionReasonSurfaceInitially()
        {
            var docs = new XmlDocProvider(exceptions: new[]
            { new XmlDocExceptionStatement(typeof(Exception), "reason\npublic class Escaped {}", Array.Empty<ParameterProvider>()) });
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Null);
        }

        [Test]
        public void FileDocumentationEnvelopeAcceptsSafeExceptionAndKeepsExistingProfileConservative()
        {
            var exception = new XmlDocExceptionStatement(typeof(InvalidOperationException), "A safe reason.", Array.Empty<ParameterProvider>());
            var docs = new XmlDocProvider(exceptions: new[] { exception });
            var cache = (IDictionary<Type, CSharpType>)typeof(CSharpType).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var wasCached = cache.ContainsKey(typeof(InvalidOperationException));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Null, "Existing proof consumers retain their original admission policy.");
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.True);
            Assert.That((IEnumerable<string>)GraphMember(envelope!, "ImportNamespaces")!, Does.Contain("System"));
            Assert.That(cache.ContainsKey(typeof(InvalidOperationException)), Is.EqualTo(wasCached), "Preplanning must not populate the canonical cache.");
            if (!wasCached)
            {
                Assert.That((IEnumerable<Type>)GraphMember(envelope!, "DeferredRuntimeImportTypes")!, Does.Contain(typeof(InvalidOperationException)),
                    "An uncached runtime identity must remain explicitly visible to later import proof.");
            }
            using var writer = new CodeWriter();
            exception.Write(writer);
            Assert.That(writer.ToString(false), Does.Contain("A safe reason."));
        }

        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\u0085")]
        [TestCase("\u2028")]
        [TestCase("\u2029")]
        public void FileDocumentationEnvelopeRejectsExceptionReasonLineBreaks(string lineBreak)
        {
            var docs = new XmlDocProvider(exceptions: new[]
            { new XmlDocExceptionStatement(typeof(Exception), "reason" + lineBreak + "public class Escaped {}", Array.Empty<ParameterProvider>()) });
            Assert.That(CaptureFileDocumentation(docs), Is.Null);
        }

        [Test]
        public void FileDocumentationEnvelopeRejectsUnknownExceptionWriterAndTypeWithoutExecutingThem()
        {
            var calls = 0;
            var customWriter = new XmlDocProvider(exceptions: new[] { new GeneratedCustomExceptionDoc(() => calls++) });
            var customType = new XmlDocProvider(exceptions: new[]
            { new XmlDocExceptionStatement(new GeneratedHostileExceptionType(), "safe", Array.Empty<ParameterProvider>()) });
            Assert.That(CaptureFileDocumentation(customWriter), Is.Null);
            Assert.That(CaptureFileDocumentation(customType), Is.Null);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void FileDocumentationEnvelopeRejectsUnknownCollectionsAndMissingExceptionType()
        {
            var unknownExceptions = new XmlDocProvider(exceptions: new GeneratedUnknownCollection<XmlDocExceptionStatement>());
            var unknownParameters = new XmlDocProvider(exceptions: new[]
            { new XmlDocExceptionStatement(typeof(Exception), "safe", new GeneratedUnknownCollection<ParameterProvider>()) });
            var missingType = new XmlDocProvider(exceptions: new[]
            { new XmlDocExceptionStatement(null!, "safe", Array.Empty<ParameterProvider>()) });
            Assert.That(CaptureFileDocumentation(unknownExceptions), Is.Null);
            Assert.That(CaptureFileDocumentation(unknownParameters), Is.Null);
            Assert.That(CaptureFileDocumentation(missingType), Is.Null);
        }

        [Test]
        public void FileDocumentationEnvelopeExceptionMembershipAndParametersInvalidateStickily()
        {
            var parameters = new List<ParameterProvider>();
            var exceptions = new List<XmlDocExceptionStatement>
            { new(typeof(Exception), "safe", parameters) };
            var docs = new XmlDocProvider(exceptions: exceptions);
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            parameters.Add(null!);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False);
            parameters.Clear();
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False);

            var other = CaptureFileDocumentation(docs);
            Assert.That(other, Is.Not.Null);
            var original = exceptions[0];
            exceptions[0] = new XmlDocExceptionStatement(typeof(Exception), "safe", Array.Empty<ParameterProvider>());
            Assert.That(GraphMember(other!, "IsCurrent"), Is.False);
            exceptions[0] = original;
            Assert.That(GraphMember(other!, "IsCurrent"), Is.False);
        }

        [TestCase("negative")]
        [TestCase("nan")]
        [TestCase("infinity")]
        [TestCase("changed-after-capture")]
        public void GeneratedDocumentationEnvelopeCultureSensitiveNumericArgumentsAreUnknown(string mode)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                object value = mode switch { "nan" => double.NaN, "infinity" => double.PositiveInfinity, _ => (object)(-1) };
                var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(new[] { FormattableStringFactory.Create("{0}", new[] { value }) }));
                var before = mode == "changed-after-capture" ? CaptureGeneratedDocumentation(docs) : null;
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                const string escape = "\npublic class Escaped {}\n//";
                culture.NumberFormat.NegativeSign = escape;
                culture.NumberFormat.NaNSymbol = escape;
                culture.NumberFormat.PositiveInfinitySymbol = escape;
                CultureInfo.CurrentCulture = culture;
                var envelope = mode == "changed-after-capture" ? before : CaptureGeneratedDocumentation(docs);
                using var writer = new CodeWriter();
                docs.Summary!.Write(writer);
                var output = writer.ToString(false) + "\npublic class Actual {}";
                var compilation = CallProofCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(output));
                Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty, output);
                Assert.That(compilation.GetTypeByMetadataName("Escaped"), Is.Not.Null, output);
                Assert.That(envelope, Is.Null);
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Test]
        public void FileDocumentationVetoBoundsAdditionalFormatWorkWithoutChangingSharedProfile()
        {
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create(new string('a', 10000))]));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Not.Null, "The existing shared profile remains unchanged.");
            Assert.That(CaptureFileDocumentation(docs), Is.Null, "The extra file-profile work must be bounded.");
        }

        [Test]
        public void FileDocumentationVetoRemainsStickyAfterOversizedCollectionRestoration()
        {
            var original = Array.Empty<XmlDocParamStatement>();
            var docs = new XmlDocProvider(parameters: original);
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            docs.Update(parameters: new XmlDocParamStatement[5000]);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False);
            docs.Update(parameters: original);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.False, "A bounded rejection remains sticky after restoration.");
        }

        [Test]
        public void FileDocumentationVetoRejectsChangingRuntimeTypeWithoutCallingIt()
        {
            var runtime = new GeneratedChangingFormattedType();
            var type = new CSharpType(runtime);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [type])]));
            runtime.Escape = true;
            var calls = runtime.CallbackCount;
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(runtime.CallbackCount, Is.EqualTo(calls), "Capture must not invoke a custom runtime Type.");
            using var writer = new CodeWriter();
            docs.Summary!.Write(writer);
            Assert.That(writer.ToString(false), Does.Contain("public class Escaped"), "The native formatter must expose the unsafe type name.");
            Assert.That(envelope, Is.Null, "Unknown runtime Type dispatch must veto the file profile.");
        }

        [Test]
        public async System.Threading.Tasks.Task FileDocumentationVetoPreservesNativeInheritForNullDeclarationName()
        {
            await MockHelpers.LoadMockGeneratorAsync(includeXmlDocs: true);
            var declaration = new CodeWriterDeclaration(null!);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [declaration])]),
                inherit: new XmlDocInheritStatement());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => writer.WriteXmlDocsNoScope(docs), "The native writer skips the unconsumed summary.");
            Assert.That(writer.ToString(false), Does.Contain("<inheritdoc/>"));
            object? envelope = null;
            Assert.DoesNotThrow(() => envelope = CaptureFileDocumentation(docs), "The POC veto must not throw before native writing.");
            Assert.That(envelope, Is.Null);
        }

        [Test]
        public void FileDocumentationRecordsNestedRuntimeBudgetUse()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PocNestedRuntime" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule("runtime");
            var builders = new List<TypeBuilder> { module.DefineType("N.Outer", TypeAttributes.Public) };
            for (int i = 0; i < 29; i++) { builders.Add(builders[^1].DefineNestedType($"T{i}", TypeAttributes.NestedPublic)); }
            Type? runtime = null;
            for (int i = builders.Count - 1; i >= 0; i--)
            {
                var created = builders[i].CreateType()!;
                if (i == builders.Count - 1) { runtime = created; }
            }
            var type = new CSharpType(runtime!);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [type])]));
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            var budget = (PocWorkBudget)envelope!.GetType().GetField("_captureBudget", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(envelope)!;
            Assert.That(budget.Remaining, Is.LessThan(3200),
                "Each runtime declaring-node visit must be accounted for before the import helper runs.");
        }

        [Test]
        public void FileDocumentationVetoBoundsNestedNamespaceImportWork()
        {
            var ns = new string('a', 350);
            CSharpType? type = null;
            for (int i = 0; i < 7; i++) { type = new CSharpType($"T{i}", ns, false, false, type, Array.Empty<CSharpType>(), true, false); }
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [type!])]));
            Assert.That(CaptureFileDocumentation(docs), Is.Null,
                "The file budget must include namespace work performed by the import helper, not only the first type scan.");
        }

        [Test]
        public void FileDocumentationVetoAcceptsModerateNestedNamespaceWork()
        {
            var ns = new string('a', 50);
            CSharpType? type = null;
            for (int i = 0; i < 7; i++) { type = new CSharpType($"T{i}", ns, false, false, type, Array.Empty<CSharpType>(), true, false); }
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [type!])]));
            Assert.That(CaptureFileDocumentation(docs), Is.Not.Null);
        }

        [Test]
        public void FileDocumentationVetoBoundsOriginalRuntimeNamespace()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PocLongNamespace" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
            var definition = assembly.DefineDynamicModule("runtime").DefineType(new string('a', 900) + ".Safe", TypeAttributes.Public).CreateType()!;
            var type = new CSharpType(definition) { Namespace = "Short" };
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create(new string('a', 3400) + "{0}", [type])]));
            Assert.That(CaptureFileDocumentation(docs), Is.Null,
                "A short cached namespace must not bypass bounded checks of the original runtime namespace.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async System.Threading.Tasks.Task FileDocumentationVetoPreservesNativeInheritForNullTypeText(bool nullName)
        {
            await MockHelpers.LoadMockGeneratorAsync(includeXmlDocs: true);
            var type = nullName ? new CSharpType(new GeneratedNullNameType()) : new CSharpType(typeof(Exception));
            if (!nullName) { type.Namespace = null!; }
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0}", [type])]),
                inherit: new XmlDocInheritStatement());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => writer.WriteXmlDocsNoScope(docs));
            Assert.That(writer.ToString(false), Does.Contain("<inheritdoc/>"));
            object? envelope = null;
            Assert.DoesNotThrow(() => envelope = CaptureFileDocumentation(docs));
            Assert.That(envelope, Is.Null);
        }

        [Test]
        public void FileDocumentationVetoRejectsUnsafeRuntimeGenericParameterName()
        {
            var definition = CreateRuntimeGeneric("TestNamespace.Safe`1", "T\npublic class Escaped {}\n//");
            var type = new CSharpType(definition, [new CSharpType(typeof(string))]);
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0:C}", [type])]));
            var envelope = CaptureFileDocumentation(docs);
            using var writer = new CodeWriter();
            docs.Summary!.Write(writer);
            Assert.That(writer.ToString(false), Does.Contain("public class Escaped"), "The native cref formatter must expose the generic parameter name.");
            Assert.That(envelope, Is.Null, "Runtime generic parameter names must not escape the file-profile check.");
        }

        [Test]
        public void FileDocumentationVetoDefersGenericCrefWithoutChangingSharedProfile()
        {
            var type = new CSharpType(typeof(List<int>));
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement([FormattableStringFactory.Create("{0:C}", [type])]));
            Assert.That(CaptureGeneratedDocumentation(docs), Is.Not.Null, "The existing shared profile must retain its prior admission.");
            Assert.That(CaptureFileDocumentation(docs), Is.Null, "Generic cref support remains deferred in the POC-only file profile.");
        }

        [Test]
        public async System.Threading.Tasks.Task FileDocumentationVetoPreservesNativeInheritForMalformedRuntimeType()
        {
            await MockHelpers.LoadMockGeneratorAsync(includeXmlDocs: true);
            var definition = CreateRuntimeGeneric("TestNamespace.Malformed", "T");
            Assert.That(definition.IsGenericType, Is.True);
            Assert.That(definition.Name, Does.Not.Contain("`"));
            Assert.That(definition.GetType(), Is.EqualTo(typeof(object).GetType()), "The control must exercise exact runtime Type dispatch.");
            var exception = new XmlDocExceptionStatement(definition, "safe", Array.Empty<ParameterProvider>());
            var docs = new XmlDocProvider(exceptions: new[] { exception }, inherit: new XmlDocInheritStatement());
            Assert.That(docs.Exceptions, Is.TypeOf<XmlDocExceptionStatement[]>());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => writer.WriteXmlDocsNoScope(docs));
            Assert.That(writer.ToString(false), Does.Contain("<inheritdoc/>"));
            object? envelope = null;
            Assert.DoesNotThrow(() => envelope = CaptureFileDocumentation(docs), "An unconsumed malformed type must not introduce a POC exception.");
            Assert.That(envelope, Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task FileDocumentationVetoPreservesNativeInheritForMalformedGenericBase()
        {
            await MockHelpers.LoadMockGeneratorAsync(includeXmlDocs: true);
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PocDocBase" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule("runtime");
            var baseBuilder = module.DefineType("TestNamespace.MalformedBase", TypeAttributes.Public);
            baseBuilder.DefineGenericParameters("T");
            var malformedBase = baseBuilder.CreateType()!.MakeGenericType(typeof(string));
            var derived = module.DefineType("TestNamespace.Derived", TypeAttributes.Public, malformedBase).CreateType()!;
            Assert.That(derived.GetType(), Is.EqualTo(typeof(object).GetType()));
            Assert.That(derived.IsGenericType, Is.False);
            Assert.That(derived.BaseType!.IsGenericType, Is.True);
            Assert.That(derived.BaseType.Name, Does.Not.Contain("`"));
            var exception = new XmlDocExceptionStatement(derived, "safe", Array.Empty<ParameterProvider>());
            var docs = new XmlDocProvider(exceptions: new[] { exception }, inherit: new XmlDocInheritStatement());
            using var writer = new CodeWriter();
            Assert.DoesNotThrow(() => writer.WriteXmlDocsNoScope(docs));
            Assert.That(writer.ToString(false), Does.Contain("<inheritdoc/>"));
            object? envelope = null;
            Assert.DoesNotThrow(() => envelope = CaptureFileDocumentation(docs), "An unconsumed malformed base must not introduce a POC exception.");
            Assert.That(envelope, Is.Null);
        }

        [Test]
        public void FileDocumentationVetoDoesNotMaterializeUncachedExceptionBase()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PocDocCache" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule("runtime");
            var parent = module.DefineType("TestNamespace.Parent", TypeAttributes.Public, typeof(Exception)).CreateType()!;
            var child = module.DefineType("TestNamespace.Child", TypeAttributes.Public, parent).CreateType()!;
            var cache = (IDictionary<Type, CSharpType>)typeof(CSharpType).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.That(cache.ContainsKey(parent), Is.False);
            Assert.That(cache.ContainsKey(child), Is.False);
            var docs = new XmlDocProvider(exceptions: new[] { new XmlDocExceptionStatement(child, "safe", Array.Empty<ParameterProvider>()) });
            var envelope = CaptureFileDocumentation(docs);
            Assert.That(cache.ContainsKey(parent), Is.False, "Preplanning must not materialize an uncached runtime base type.");
            Assert.That(cache.ContainsKey(child), Is.False);
            Assert.That(envelope, Is.Null, "A non-materialized exception type must conservatively remain unavailable.");
        }

        private static Type CreateRuntimeGeneric(string name, string parameterName)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PocDocRuntime" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
            var builder = assembly.DefineDynamicModule("runtime").DefineType(name, TypeAttributes.Public);
            builder.DefineGenericParameters(parameterName);
            return builder.CreateType()!;
        }

        [TestCase("bool")]
        [TestCase("char")]
        [TestCase("string")]
        public void GeneratedDocumentationEnvelopeCultureIndependentScalarControlsRemainAvailable(string mode)
        {
            object value = mode switch { "bool" => true, "char" => (object)'x', _ => "safe" };
            var docs = new XmlDocProvider(summary: new XmlDocSummaryStatement(new[] { FormattableStringFactory.Create("{0}", new[] { value }) }));
            var envelope = CaptureGeneratedDocumentation(docs);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(GraphMember(envelope!, "IsCurrent"), Is.True);
        }

        private static object? CaptureGeneratedDocumentation(XmlDocProvider docs)
        {
            var type = typeof(XmlDocProvider).Assembly.GetType("Microsoft.TypeSpec.Generator.Primitives.PreparedGeneratedDocumentationEnvelope");
            Assert.That(type, Is.Not.Null);
            var method = type!.GetMethod("TryCapture", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method!.Invoke(null, new object[] { docs });
        }

        private static object? CaptureFileDocumentation(XmlDocProvider docs)
        {
            var type = typeof(XmlDocProvider).Assembly.GetType("Microsoft.TypeSpec.Generator.Primitives.PreparedGeneratedDocumentationEnvelope");
            return type?.GetMethod("TryCaptureForFile", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { docs });
        }

        private sealed class GeneratedCustomExceptionDoc(Action callback)
            : XmlDocExceptionStatement(typeof(Exception), "safe", Array.Empty<ParameterProvider>())
        {
            internal override void WriteCore(CodeWriter writer) { callback(); writer.WriteRawLine("public class Escaped {}"); }
        }

        private sealed class GeneratedUnknownCollection<T> : IReadOnlyList<T>
        {
            public int Count => throw new InvalidOperationException("Unknown collection must not run.");
            public T this[int index] => throw new InvalidOperationException("Unknown collection must not run.");
            public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Unknown collection must not run.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class GeneratedChangingFormattedType() : TypeDelegator(typeof(Exception))
        {
            internal bool Escape { get; set; }
            internal int CallbackCount { get; private set; }
            public override bool IsGenericParameter { get { CallbackCount++; return Escape || base.IsGenericParameter; } }
            public override string Name { get { CallbackCount++; return Escape ? "T\npublic class Escaped {}\n//" : base.Name; } }
        }

        private sealed class GeneratedNullNameType() : TypeDelegator(typeof(Exception))
        {
            public override string Name => null!;
        }

        private sealed class GeneratedHostileExceptionType() : TypeDelegator(typeof(Exception))
        {
            public override string Name => throw new InvalidOperationException("Unknown type must not run.");
            public override string? Namespace => throw new InvalidOperationException("Unknown type must not run.");
        }

        private sealed class GeneratedCustomDoc(Action callback) : XmlDocStatement($"<item>", $"</item>", Array.Empty<FormattableString>())
        {
            internal override void WriteCore(CodeWriter writer) { callback(); writer.WriteRawLine("public class Escaped {}"); }
        }

        private sealed class GeneratedUnknownFormatter : FormattableString
        {
            public override string Format => throw new InvalidOperationException("Unknown formatter must not run.");
            public override int ArgumentCount => throw new InvalidOperationException("Unknown formatter must not run.");
            public override object? GetArgument(int index) => throw new InvalidOperationException("Unknown formatter must not run.");
            public override object?[] GetArguments() => throw new InvalidOperationException("Unknown formatter must not run.");
            public override string ToString(IFormatProvider? formatProvider) => throw new InvalidOperationException("Unknown formatter must not run.");
        }
    }
}

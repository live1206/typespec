// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.TestHelpers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PocFullSourceObservationNeedsIndependentCredits(bool isUsing)
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: isUsing);
            var statements = new MethodBodyStatement[]
            {
                new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate(),
                new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null)))
            };
            var method = owner.Methods.Single();
            method.Update(bodyStatements: new MethodBodyStatements(statements));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence);
            Assert.That(observation, Is.Not.Null);
            Assert.That(observation!.Method, Is.SameAs(method));
            Assert.That(observation.Occurrence, Is.SameAs(occurrence));
            Assert.That(observation.IsCurrent, Is.True);
            var beforeQuery = proofCalls;
            var observe = typeof(PreparedOrdinaryTypeSourceObservation).GetMethod("IsCurrentForPoc", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var funded = new PocWorkBudget(65536);
            Assert.That(observe.Invoke(observation, new object[] { funded }), Is.True);
            Assert.That(observe.Invoke(observation, new object[] { new PocWorkBudget(0) }), Is.False);
            Assert.That(funded.Remaining, Is.LessThan(65536));
            Assert.That(proofCalls, Is.EqualTo(beforeQuery), "Full IR currentness must not replay the separate physical-source host callback.");
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.HasStarted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocDirectSourceCaptureRequiresItsOwnCredits(bool isUsing)
        {
            using var host = new BodyOnlyPhysicalHost();
            var proofCalls = 0;
            host.Session.CurrentnessCallback = () => { proofCalls++; return true; };
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: isUsing);
            var statements = new MethodBodyStatement[]
            {
                new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate(),
                new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null)))
            };
            var method = owner.Methods.Single();
            method.Update(bodyStatements: new MethodBodyStatements(statements));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            var before = proofCalls;
            var full = new PocWorkBudget(65536);
            var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, full);
            Assert.That(observation, Is.Not.Null);
            Assert.That(observation!.Method, Is.SameAs(method));
            Assert.That(observation.Occurrence, Is.SameAs(occurrence));
            var required = 65536 - full.Remaining;
            Assert.That(required, Is.GreaterThan(0));
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, new PocWorkBudget(required)), Is.Not.Null);
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, new PocWorkBudget(required - 1)), Is.Null);
            var spent = new PocWorkBudget(required);
            Assert.That(spent.TrySpend(required), Is.True);
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, spent), Is.Null);
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, new PocWorkBudget(0)), Is.Null,
                "A previous query does not fund root discovery, path copying or another full source observation.");
            Assert.That(proofCalls, Is.EqualTo(before));
            Assert.That(file.IsCurrent(), Is.True);
        }

        [Test]
        public void PocFullSourceQueryCannotFundWideBodyWithOnlyEntryCredit()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: true);
            var statements = new MethodBodyStatement[101];
            statements[0] = new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate();
            for (var i = 1; i < statements.Length; i++)
            {
                statements[i] = new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null)));
            }
            owner.Methods.Single().Update(bodyStatements: new MethodBodyStatements(statements));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence);
            Assert.That(observation, Is.Not.Null);
            Assert.That(observation!.IsCurrentForPoc(new PocWorkBudget(65536)), Is.True,
                "The actual wide cached source graph must be admitted by a fully funded observation.");
            Assert.That(observation.IsCurrentForPoc(new PocWorkBudget(1)), Is.False,
                "Paying for the entry node cannot fund body-array copies, child comparisons and frame/type scans.");
            Assert.That(file.IsInvalid, Is.False);
            Assert.That(file.IsCurrent(), Is.True);
        }

        [TestCase("parameters")]
        [TestCase("callable")]
        [TestCase("arguments")]
        public void PocFullSourceSubframeRequiresIndependentCredits(string kind)
        {
            var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
            var call = new InvokeMethodExpression(TypeReferenceExpression.FromType(typeof(PreparedCastCallProbe)), "ObserveObject", new ValueExpression[] { new LiteralExpression(1) });
            var owner = new ParameterValueOwner(call, new[] { parameter }, emptyDocs: true);
            owner.EnsureBuilt();
            _ = owner.Type;
            var method = owner.Methods.Single();
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            object proof;
            if (kind == "parameters")
            {
                var body = (object?)method.BodyStatements ?? method.BodyExpression;
                proof = PreparedMethodParameterFrame.TryCapture(method, body!)!;
            }
            else
            {
                var contract = DeclaredCallableContract.FromProvider(method, CallProofCompilation());
                Assert.That(contract.IsAvailable, Is.True);
                proof = kind == "callable" ? contract : DeclaredArgumentMap.Create(contract, new ValueExpression[] { new LiteralExpression(1) });
                if (proof is DeclaredArgumentMap map) { Assert.That(map.IsAvailable, Is.True); }
            }
            Assert.That(proof, Is.Not.Null, "A supported nonempty full-frame proof is required before the budget contrast.");
            var observe = proof.GetType().GetMethod("IsCurrentWithoutInvalidating", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(observe.Invoke(proof, new object[] { new PocWorkBudget(65536) }), Is.True);
            Assert.That(observe.Invoke(proof, new object[] { new PocWorkBudget(0) }), Is.False,
                "A previous full-file or frame observation does not fund this nonempty frame again.");
            Assert.That(parameter.CachedVariableExpression, Is.Null, "Resource checking must not materialize the parameter variable.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocParameterFrameDoesNotReplayUnknownCultureNormalizer(bool materialized)
        {
            var previous = CultureInfo.CurrentCulture;
            var culture = new PocParameterTextInfoProbe();
            try
            {
                CultureInfo.CurrentCulture = culture;
                var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
                if (materialized) { _ = (VariableExpression)parameter; }
                var call = new InvokeMethodExpression(TypeReferenceExpression.FromType(typeof(PreparedCastCallProbe)), "ObserveObject", new ValueExpression[] { new LiteralExpression(1) });
                var owner = new ParameterValueOwner(call, new[] { parameter }, emptyDocs: true);
                owner.EnsureBuilt();
                _ = owner.Type;
                var method = owner.Methods.Single();
                var frame = PreparedMethodParameterFrame.TryCapture(method, (object?)method.BodyStatements ?? method.BodyExpression!);
                Assert.That(frame, Is.Not.Null);
                Assert.That(culture.Calls, Is.GreaterThan(0), "The unchanged native normalization route must actually dispatch the probe.");
                var before = culture.Calls;
                Assert.That(frame!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.EqualTo(materialized));
                Assert.That(culture.Calls, Is.EqualTo(before), "POC validation must never replay the unknown TextInfo getter.");
                Assert.That(parameter.CachedVariableExpression is not null, Is.EqualTo(materialized));
                Assert.That(frame.IsCurrent(), Is.True, "Unknown-culture refusal must not stale the native frame.");
                if (!materialized) { Assert.That(culture.Calls, Is.GreaterThan(before)); }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void PocParameterFrameAcceptsNativeLazyCarrierWithoutCreatingIt()
        {
            var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
            var call = new InvokeMethodExpression(TypeReferenceExpression.FromType(typeof(PreparedCastCallProbe)), "ObserveObject", new ValueExpression[] { new LiteralExpression(1) });
            var owner = new ParameterValueOwner(call, new[] { parameter }, emptyDocs: true);
            owner.EnsureBuilt();
            _ = owner.Type;
            var method = owner.Methods.Single();
            var frame = PreparedMethodParameterFrame.TryCapture(method, (object?)method.BodyStatements ?? method.BodyExpression!);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.Null);
            VariableExpression variable = parameter;
            Assert.That(parameter.CachedVariableExpression, Is.SameAs(variable));
            Assert.That(frame.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True,
                "Reading the actual canonically materialized carrier must preserve native lazy-currentness semantics.");
        }

        [Test]
        public void PocOwnedBlockFrameRetainsCompleteParserWithFundedCurrentName()
        {
            var owner = new TestTypeProvider();
            var parameter = new ParameterProvider("unused", $"parameter", typeof(object));
            var call = new InvokeMethodExpression(null, "Capture", Array.Empty<ValueExpression>())
            { TypeArguments = new CSharpType[] { typeof(Exception) } };
            var local = new DeclarationExpression(typeof(Exception), "seed");
            var body = new MethodBodyStatements(new MethodBodyStatement[]
            {
                new AssignmentExpression(local, new KeywordExpression("default", null)).Terminate(),
                new ExpressionStatement(new KeywordExpression("return", call))
            });
            var method = new MethodProvider(new MethodSignature("M", null, MethodSignatureModifiers.Public, typeof(object), null,
                new[] { parameter }), body, owner);
            method.Update(bodyStatements: body);
            var nativeCarrier = parameter.CachedVariableExpression;
            Assert.That(nativeCarrier, Is.Not.Null, "Native method validation materializes the object parameter before the POC check.");
            Assert.That(PreparedUnusedDefaultLocalCallBlock.GetInvocation(method), Is.SameAs(call));
            var frame = PreparedMethodParameterFrame.TryCapture(method, body);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(parameter.CachedVariableExpression, Is.SameAs(nativeCarrier), "POC parsing must neither replace nor rematerialize the native carrier.");
            Assert.That(PreparedUnusedDefaultLocalCallBlock.GetInvocationForPoc(method, new PocWorkBudget(0), "unused"), Is.Null);
            Assert.That(PreparedUnusedDefaultLocalCallBlock.GetInvocationForPoc(method, new PocWorkBudget(65536), "unused"), Is.SameAs(call));
        }

        [TestCase("documentation")]
        [TestCase("header")]
        [TestCase("constructor")]
        public void PocFullSourceHeaderChecksRequireIndependentCredits(string kind)
        {
            object? proof;
            if (kind == "documentation")
            {
                var text = "actual cached summary";
                proof = PreparedGeneratedDocumentationEnvelope.TryCapture(new XmlDocProvider(new XmlDocSummaryStatement(new FormattableString[] { $"{text}" })));
            }
            else if (kind == "header")
            {
                var owner = new GeneratedHeaderOwner();
                WarmGeneratedHeader(owner);
                proof = PreparedGeneratedTypeHeader.TryCapture(owner);
            }
            else
            {
                var initializer = new ConstructorInitializer(true, new ValueExpression[] { new LiteralExpression(1) });
                var body = new ExpressionStatement(new InvokeMethodExpression(TypeReferenceExpression.FromType(typeof(GC)), nameof(GC.KeepAlive),
                    new ValueExpression[] { new LiteralExpression(null) }));
                var owner = new BaseInitializerProofOwner(typeof(PreparedBaseInitializerProbe), initializer, body);
                owner.EnsureBuilt();
                FinalizeBaseInitializerOwner(owner);
                var compilation = CallProofCompilation();
                proof = PreparedConstructorInitializerFrame.TryCapture(owner.Constructors.Single(), compilation,
                    type => compilation.GetTypeByMetadataName(type.FrameworkType.FullName!));
            }
            Assert.That(proof, Is.Not.Null, "A supported actual header/documentation/constructor proof must precede the credit contrast.");
            var observe = proof!.GetType().GetMethod("IsCurrentWithoutInvalidating", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var full = new PocWorkBudget(65536);
            Assert.That(observe.Invoke(proof, new object[] { full }), Is.True);
            var required = 65536 - full.Remaining;
            Assert.That(required, Is.GreaterThan(0));
            Assert.That(observe.Invoke(proof, new object[] { new PocWorkBudget(required) }), Is.True);
            Assert.That(observe.Invoke(proof, new object[] { new PocWorkBudget(required - 1) }), Is.False);
            Assert.That(observe.Invoke(proof, new object[] { new PocWorkBudget(0) }), Is.False,
                "Previous source capture does not fund this full header/documentation check again.");
            Assert.That(proof.GetType().GetProperty("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(proof), Is.True,
                "POC exhaustion must not stale the shared proof.");
        }

        [Test]
        public void PocDocumentationRecheckFundsActualWideFactoryArgumentsWithoutStalingSharedProof()
        {
            var arguments = Enumerable.Repeat<object>("unused but inspected", 2048).ToArray();
            var placeholders = string.Join(" ", Enumerable.Range(0, arguments.Length).Select(index => "{" + index + "}"));
            var format = System.Runtime.CompilerServices.FormattableStringFactory.Create(placeholders, arguments);
            var summary = new XmlDocSummaryStatement(new[] { format });
            var liveArguments = summary.Lines.Single().GetArguments();
            Assert.That(liveArguments.Length, Is.EqualTo(2048), "The actual normalized source, not its pre-normalization input, must remain wide.");
            var docs = PreparedGeneratedDocumentationEnvelope.TryCapture(new XmlDocProvider(summary));
            Assert.That(docs, Is.Not.Null);
            Assert.That(docs!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(docs.IsCurrentWithoutInvalidating(new PocWorkBudget(32)), Is.False);
            var original = liveArguments[1024];
            liveArguments[1024] = new object();
            Assert.That(docs.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False);
            liveArguments[1024] = original;
            Assert.That(docs.IsCurrent, Is.True);
            Assert.That(docs.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
        }

        [Test]
        public void PocHeaderRecheckFundsLongLivePinnedPathWithoutStalingSharedProof()
        {
            var owner = new GeneratedHeaderOwner();
            WarmGeneratedHeader(owner);
            var path = new string('p', 4096) + ".cs";
            var interfaces = new System.Collections.Generic.List<CSharpType>();
            owner.Update(relativeFilePath: path, implements: interfaces);
            Assert.That(owner.CachedImplements, Is.SameAs(interfaces));
            var header = PreparedGeneratedTypeHeader.TryCapture(owner);
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(header.IsCurrentWithoutInvalidating(new PocWorkBudget(128)), Is.False);
            Assert.That(header.IsCurrent, Is.True, "POC credit exhaustion must not set the shared header stale flag.");
            // Mutate the actual supported carrier without a declaration-revision
            // change. Update(path) would intentionally make the native snapshot
            // permanently stale even after restoring the old path.
            interfaces.Add(typeof(IDisposable));
            Assert.That(header.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False);
            interfaces.Clear();
            Assert.That(header.IsCurrent, Is.True);
            Assert.That(header.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PocSuppressionPointNeedsCreditsAndOwnsMismatch(bool pocProfile, bool mutate)
        {
            var original = new SuppressionStatement(null, new LiteralExpression("CS0618"), "safe");
            var values = new[] { original };
            var envelope = pocProfile ? PreparedSuppressionEnvelope.TryCaptureForPoc(values, new PocWorkBudget(4096)) :
                PreparedSuppressionEnvelope.TryCapture(values);
            Assert.That(envelope, Is.Not.Null);
            var full = new PocWorkBudget(65536);
            Assert.That(envelope!.IsCurrentWithoutInvalidating(full), Is.True);
            if (!mutate)
            {
                Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(0)), Is.False,
                    "The earlier pragma capture cannot fund this reference/wrapper recheck.");
                var required = 65536 - full.Remaining;
                Assert.That(required, Is.GreaterThan(0));
                Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(required)), Is.True);
                Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(required - 1)), Is.False);
            }
            else
            {
                values[0] = new SuppressionStatement(null, new LiteralExpression("CS0618"), "replacement");
                Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False);
                values[0] = original;
            }
            Assert.That(envelope.IsCurrent, Is.True, "POC-only failure must not stale the native envelope after actual carrier restoration.");
            var nativeControl = pocProfile ? PreparedSuppressionEnvelope.TryCaptureForPoc(values, new PocWorkBudget(4096)) :
                PreparedSuppressionEnvelope.TryCapture(values);
            Assert.That(nativeControl, Is.Not.Null);
            values[0] = new SuppressionStatement(null, new LiteralExpression("CS0618"), "native replacement");
            Assert.That(nativeControl!.IsCurrent, Is.False);
            values[0] = original;
            Assert.That(nativeControl.IsCurrent, Is.False, "Native/shared mismatch behavior remains intentionally sticky.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocSuppressionPointWideCarrierNeedsElementCredits(bool pocProfile)
        {
            var original = new SuppressionStatement(null, new LiteralExpression("CS0618"), "safe");
            var values = Enumerable.Repeat(original, 128).ToArray();
            var envelope = pocProfile ? PreparedSuppressionEnvelope.TryCaptureForPoc(values, new PocWorkBudget(4096)) :
                PreparedSuppressionEnvelope.TryCapture(values);
            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope!.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.True);
            Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(32)), Is.False);
            values[^1] = new SuppressionStatement(null, new LiteralExpression("CS0618"), "replacement");
            Assert.That(envelope.IsCurrentWithoutInvalidating(new PocWorkBudget(65536)), Is.False);
            values[^1] = original;
            Assert.That(envelope.IsCurrent, Is.True);
        }

        private sealed class PocParameterTextInfoProbe() : CultureInfo("en-US")
        {
            internal int Calls;
            public override TextInfo TextInfo { get { Calls++; return base.TextInfo; } }
        }

        [Test]
        public void PocSourceMismatchDoesNotStaleSharedGraphAfterLiveArrayRestoration()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: true);
            var statements = new MethodBodyStatement[]
            {
                new AssignmentExpression(declaration, new KeywordExpression("default", null)).Terminate(),
                new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null)))
            };
            owner.Methods.Single().Update(bodyStatements: new MethodBodyStatements(statements));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence);
            Assert.That(observation, Is.Not.Null);
            Assert.That(observation!.IsCurrent, Is.True);
            var observe = typeof(PreparedOrdinaryTypeSourceObservation).GetMethod("IsCurrentForPoc", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var original = statements[1];
            statements[1] = new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null)));
            Assert.That(observe.Invoke(observation, new object[] { new PocWorkBudget(65536) }), Is.False);
            statements[1] = original;
            Assert.That(observe.Invoke(observation, new object[] { new PocWorkBudget(65536) }), Is.False,
                "POC mismatch observation stays sticky after restoring the exact live array element.");
            Assert.That(file.IsInvalid, Is.False, "An additional POC boundary must not write shared stale flags.");
            Assert.That(file.IsCurrent(), Is.True, "Native full currentness after restoration must remain independent.");
            Assert.That(file.HasStarted, Is.False);
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.Providers
{
    [NonParallelizable]
    internal class PreparedLocalTypeClientTests
    {
        [Test]
        public void RealFooUsingDeclarationWritesPreparedPipelineMessageType()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                var root = CSharpSyntaxTree.ParseText(file.Content).GetRoot();
                var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Single(m => m.Identifier.ValueText == "Three" && m.ParameterList.Parameters.Single().Identifier.ValueText == "options");
                var local = method.Body!.Statements.OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword), Is.True);
                Assert.That(local.Declaration.Variables.Single().Identifier.ValueText, Is.EqualTo("message"));
                Assert.That(local.Declaration.Type.ToString(), Is.EqualTo("PipelineMessage"),
                    "The actual unobserved Foo declaration should shorten in raw writer output.");
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooLocalTypeChoiceIsPreparedBeforeAnyOutputWriterStarts()
        {
            WithRealFoo((owner, prepared) =>
            {
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Assert.That(prepared.SourceFiles.All(file => (bool)started.GetValue(file)! == false), Is.True,
                    "The plan must be available before any output-provider writer starts.");
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var choice = prepared.GetTypeOfNamePermission(file, declaration);
                Assert.That(choice, Is.Not.Null, "The POC's Foo namespace choice must be prepared from the provider/IR before writing.");
                Assert.That(choice!.SimpleSpelling, Is.EqualTo("PipelineMessage"));
                Assert.That(choice.QualifiedSpelling, Is.EqualTo("global::System.ClientModel.Primitives.PipelineMessage"));
                Assert.That(prepared.SourceFiles.All(source => (bool)started.GetValue(source)! == false), Is.True);
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooOwnedRootPocPreparationDoesNotPopulateTask03PermissionStore()
        {
            WithRealFoo((owner, prepared) =>
            {
                Assert.That(prepared.FinalizedPocHeaders, Is.Not.Null);
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                Assert.That(prepared.GetTypeOfNamePermission(file, declaration), Is.Null,
                    "Task04 owned-root preparation must not populate Task03's single-file permission store.");
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooUsingDeclarationHasCapturedBodyGrammar()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var statement = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Statements.ExpressionStatement expression &&
                        expression.Expression is Microsoft.TypeSpec.Generator.Expressions.AssignmentExpression
                        { Variable: Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression });
                var grammar = PreparedPocBodyGrammar.TryCapture(file, statement, new PocWorkBudget(65536), out var failure);
                Assert.That(grammar, Is.Not.Null, failure);
                Assert.That(grammar!.IsCurrent(new PocWorkBudget(65536)), Is.True);
                Assert.That(prepared.GetTypeOfNamePermission(file, statement), Is.Null);
                Assert.That(file.HasStarted, Is.False);
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooContextAttributesUseApprovedCanonicalBoundaryWithoutWriteTimeRebuild()
        {
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                Assert.That(header.Attributes, Is.Not.Null);
                Assert.That(header.UsesCanonicalAttributes, Is.True);
                Assert.That(header.Attributes!.IsCurrent(new PocWorkBudget(4096)), Is.True);
                var cache = context.CachedWriteAttributes;
                var marker = typeof(TypeProvider).GetField("_pocNativeAttributeGenerator",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var before = marker.GetValue(context);
                Assert.That(before, Is.Not.Null);
                var nativeFile = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(marker.GetValue(context), Is.SameAs(before), "The approved final writer must not rebuild attributes again.");
                Assert.That(context.CachedWriteAttributes, Is.SameAs(cache));
                Assert.That(header.IsCurrent, Is.True);
                Assert.That(CSharpSyntaxTree.ParseText(nativeFile.Content).GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
            }, finalizePocHeaders: true);
        }

        [Test]
        public void CanonicalContextAttributesDoNotRetainReferenceMapRemovedModel()
        {
            string? native = null;
            string? canonical = null;
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeCache = null;
            object? beforeOrigin = null;
            void CheckRemoval(OutputLibrary output, ProviderReferenceMapSession referenceMap)
            {
                var model = output.TypeProviders.Single(type => type.Name == "PocUnreferencedModel");
                Assert.That(referenceMap.ShouldWriteProvider(model), Is.False, "The trigger must be a real final reference-map removal.");
                var context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                beforeCache = context.CachedWriteAttributes;
                beforeOrigin = context.PocNativeAttributeCacheOrigin;
            }
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                native = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
            }, addPocUnreferencedModel: true, beforePocPreparation: CheckRemoval);
            Assert.That(native, Does.Not.Contain("PocUnreferencedModel"));
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(prepared.FinalizedPocHeaders!.Get(context)?.Attributes, Is.Null,
                    "Model-containing discovery is a legacy fallback, not an admitted negative recipe.");
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                canonical = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
            }, addPocUnreferencedModel: true, beforePocPreparation: CheckRemoval, finalizePocHeaders: true);
            Assert.That(canonical, Does.Not.Contain("PocUnreferencedModel"));
            Assert.That(canonical, Is.EqualTo(native));
        }

        private sealed class InterfaceObservationProbe { }

        private sealed class InterfaceObservationTypeProbe(Type type) : System.Reflection.TypeDelegator(type)
        {
            internal int InterfaceReads { get; private set; }
            public override Type[] GetInterfaces()
            {
                InterfaceReads++;
                return base.GetInterfaces();
            }
        }

        [Test]
        public void ContextInterfaceObservationUsesOnlyNativeNegativeResults()
        {
            WithRealFoo((_, _) =>
            {
                var type = typeof(InterfaceObservationProbe);
                var generator = CodeModelGenerator.Instance;
                // This one-unit positive concerns a single native observation,
                // not all unrelated types observed while constructing Foo.
                // Collision/cardinality funding is checked independently by
                // InterfaceObservationFundsActualRuntimeIdentityCollisionProbe.
                var tableField = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                tableField.SetValue(generator, null);
                var remaining = 1;
                bool Spend(int units) { if (units > remaining) { return false; } remaining -= units; return true; }
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendNegative(generator, type, Spend), Is.False,
                    "A cold runtime type is not an observed negative result.");
                Assert.That(ClientModel.Utilities.ModelReaderWriterHelpers.ImplementsModelReaderWriter(type), Is.False);
                var table = tableField.GetValue(generator);
                Assert.That(table, Is.Not.Null);
                var entries = (Dictionary<Type, (int Cost, bool Result)>)table!.GetType()
                    .GetField("Helper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(table)!;
                Assert.That(entries.Count, Is.EqualTo(1), "Prove the single-key budget precondition rather than assuming dictionary lookup is constant work.");
                remaining = 1;
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendNegative(generator, type, Spend), Is.True);
                Assert.That(remaining, Is.Zero);
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendNegative(generator, type, Spend), Is.False,
                    "A negative observation still consumes the supplied work budget.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterfaceObservationFundsActualRuntimeIdentityCollisionProbe(bool direct)
        {
            WithRealFoo((unusedOwner, prepared) =>
            {
                Utilities.PocContextInterfaceObservations.Record(typeof(InterfaceObservationProbe), 0, false);
                var table = typeof(ScmCodeModelGenerator).GetField("PocContextInterfaceObservations",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(CodeModelGenerator.Instance);
                Assert.That(table, Is.Not.Null);
                var entries = (Dictionary<Type, (int Cost, bool Result)>)table!.GetType()
                    .GetField(direct ? "Direct" : "Helper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(table)!;
                int capacity = entries.EnsureCapacity(entries.Count + 32);
                var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new System.Reflection.AssemblyName("PocInterfaceCollisions" + Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
                var module = assembly.DefineDynamicModule("Main");
                var colliding = new List<Type>();
                int? bucket = null;
                for (int i = 0; colliding.Count < 32 && i < 32768; i++)
                {
                    var type = module.DefineType("T" + i, System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class).CreateType()!;
                    int current = (int)((uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(type) % (uint)capacity);
                    bucket ??= current;
                    if (current != bucket) { continue; }
                    var interfaces = type.GetInterfaces();
                    Assert.That(interfaces, Is.Empty);
                    if (direct) { Utilities.PocContextInterfaceObservations.RecordDirect(type, interfaces.Length, 0, false); }
                    else { Utilities.PocContextInterfaceObservations.Record(type, interfaces.Length, false); }
                    colliding.Add(type);
                }
                Assert.That(colliding.Count, Is.EqualTo(32));
                Assert.That(entries.EnsureCapacity(0), Is.EqualTo(capacity));
                Assert.That(colliding.All(type => (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(type) % (uint)capacity == (uint)bucket!), Is.True);
                var target = colliding[0];
                Assert.That(entries[target], Is.EqualTo((0, false)), "The immutable runtime key has an actual observed empty native interface result.");
                var budget = new PocWorkBudget(1);
                bool admitted = direct
                    ? Utilities.PocContextInterfaceObservations.TrySpendDirectNegative(target, budget.TrySpend)
                    : Utilities.PocContextInterfaceObservations.TrySpendHelperNegative(target, budget.TrySpend);
                Assert.That(admitted, Is.False, "One unit cannot fund the actual 32-key identity bucket before the observation is consulted.");
                Assert.That(budget.Exhausted, Is.True);
            });
        }

        [Test]
        public void ContextInterfaceObservationDoesNotProbeUnknownRuntimeType()
        {
            WithRealFoo((_, _) =>
            {
                var type = new InterfaceObservationTypeProbe(typeof(InterfaceObservationProbe));
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendNegative(CodeModelGenerator.Instance, type, _ => true), Is.False);
                Assert.That(type.InterfaceReads, Is.Zero);
                Assert.That(ClientModel.Utilities.ModelReaderWriterHelpers.ImplementsModelReaderWriter(type), Is.False);
                Assert.That(type.InterfaceReads, Is.EqualTo(1), "Only the native interface query may run.");
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendNegative(CodeModelGenerator.Instance, type, _ => true), Is.False);
                Assert.That(type.InterfaceReads, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnknownContextRuntimeTypeRetainsNativeReflectionTiming(bool finalizePocHeaders)
        {
            var runtimeType = new ContextRuntimeTypeProbe(typeof(InterfaceObservationProbe));
            TypeProvider? contextOwner = null;
            object? beforeCache = null;
            object? beforeOrigin = null;
            WithRealFoo((_, prepared) =>
            {
                Assert.That(runtimeType.AllInterfaceReads, Is.Zero, "No direct helper or preflight reflection call may escape the context-stack counter.");
                Assert.That(ReferenceEquals(contextOwner!.CachedWriteAttributes, beforeCache), Is.True);
                Assert.That(ReferenceEquals(contextOwner.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(contextOwner)!.Attributes, Is.Null); }
                Assert.That(runtimeType.ContextInterfaceReads, Is.Zero,
                    "An unknown runtime Type dependency must not be inspected by early context attribute materialization.");
            }, finalizePocHeaders: finalizePocHeaders, beforePocPreparation: (output, _) =>
            {
                var owner = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Foo");
                var method = owner.CanonicalView.Methods.First();
                method.Update(signature: new MethodSignature(method.Signature.Name, null, method.Signature.Modifiers,
                    new CSharpType(runtimeType), null, Array.Empty<ParameterProvider>()));
                contextOwner = output.TypeProviders.Single(provider => provider is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                beforeCache = contextOwner.CachedWriteAttributes;
                beforeOrigin = contextOwner.PocNativeAttributeCacheOrigin;
                runtimeType.ContextInterfaceReads = 0;
                runtimeType.AllInterfaceReads = 0;
            }, inspectGeneration: (output, referenceMap, ignoredOwner, prepared) =>
            {
                var context = output.TypeProviders.Single(provider => provider.GetType() ==
                    typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition));
                var writer = CodeModelGenerator.Instance.GetWriter(context);
                writer.PreparedFieldTypes = prepared;
                _ = writer.Write().Content;
                Assert.That(runtimeType.ContextInterfaceReads, Is.GreaterThan(0),
                    "The native final context write must retain its existing reflection callback for an unsupported dependency.");
            });
        }

        private sealed class ContextRuntimeTypeProbe(Type type) : System.Reflection.TypeDelegator(type)
        {
            internal int ContextInterfaceReads { get; set; }
            internal int AllInterfaceReads { get; set; }
            public override Type[] GetInterfaces()
            {
                AllInterfaceReads++;
                if (new System.Diagnostics.StackTrace().GetFrames()?.Any(frame =>
                    frame.GetMethod()?.DeclaringType == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)) == true)
                {
                    ContextInterfaceReads++;
                }
                return base.GetInterfaces();
            }
        }

        [Test]
        public void RealFooPostMapCanonicalAttributeInputsDiagnostic()
        {
            WithRealFoo((unusedOwner, prepared) => { }, finalizePocHeaders: true,
                beforePocPreparation: (output, referenceMap) =>
                {
                    var field = typeof(TypeProvider).GetField("_canonicalView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    foreach (var provider in output.TypeProviders.Where(referenceMap.ShouldWriteProvider))
                    {
                        var lazy = field.GetValue(provider)!;
                        var created = (bool)lazy.GetType().GetProperty("IsValueCreated")!.GetValue(lazy)!;
                        var canonical = provider.CachedCanonicalView;
                        Console.WriteLine($"POC-CANON-INPUT {provider.Name}: created={created}; type={provider.GetType().Name}; attributes={provider.CachedWriteAttributes?.GetType().Name}; canonical-properties={canonical?.CachedProperties?.GetType().Name}; canonical-methods={canonical?.CachedMethods?.GetType().Name}; implements={provider.CachedImplements?.GetType().Name}; serializations={provider.CachedSerializationProviders?.GetType().Name}; nested={provider.CachedNestedTypes?.GetType().Name}; custom-ready={provider.IsCustomCodeViewMaterialized}; contract-ready={provider.IsLastContractViewMaterialized}");
                    }
                });
        }

        [Test]
        public void ContextRecipeRejectsColdCanonicalViewBeforeContextCallback()
        {
            var calls = 0;
            Lazy<CanonicalTypeProvider>? cold = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(calls, Is.Zero, "Preflight must not force a cold canonical builder, on any stack.");
                Assert.That(cold!.IsValueCreated, Is.False);
                Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null);
                _ = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(calls, Is.GreaterThan(0), "The same cold factory must remain reachable by native final discovery.");
                Assert.That(cold.IsValueCreated, Is.True);
            }, finalizePocHeaders: true, beforePocPreparation: (output, map) =>
            {
                var foo = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                var canonical = (CanonicalTypeProvider)foo.CachedCanonicalView!;
                cold = new Lazy<CanonicalTypeProvider>(() => { calls++; return canonical; });
                typeof(TypeProvider).GetField("_canonicalView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(foo, cold);
            });
        }

        private sealed class ContextHashProbe() : Microsoft.TypeSpec.Generator.Statements.SuppressionStatement(
            new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute), Array.Empty<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>()),
            Microsoft.TypeSpec.Generator.Snippets.Snippet.Literal("CS0618"), "recipe hash probe")
        {
            internal int Calls;
            internal int AllCalls;
            public override int GetHashCode()
            {
                AllCalls++;
                if (new System.Diagnostics.StackTrace().GetFrames().Any(f => f.GetMethod()?.Name == "RebuildAttributes")) { Calls++; }
                return RuntimeHelpers.GetHashCode(this);
            }
        }

        [Test]
        public void ContextRecipeRejectsUnknownVisitorHashBeforeRebuild()
        {
            var probe = new ContextHashProbe();
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(probe.AllCalls, Is.Zero, "Unknown hash dispatch must not occur on any preparation stack.");
                Assert.That(probe.Calls, Is.Zero, "Unknown equality/hash dispatch must veto before native rebuilding.");
                Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null);
                _ = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(probe.Calls, Is.GreaterThan(0), "Legacy native rebuilding must still hash the visitor addition at write time.");
                Assert.That(probe.AllCalls, Is.GreaterThan(0));
            }, finalizePocHeaders: true, beforePocPreparation: (output, map) =>
            {
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                // Native carrier-injection control: public visitor Update takes
                // sealed AttributeStatements, so inject the unknown hash subclass
                // explicitly rather than pretending it is an ordinary attribute.
                typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(context, new Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[] { probe });
                probe.Calls = 0;
                probe.AllCalls = 0;
            });
        }

        [TestCase("cycle")]
        [TestCase("oversized")]
        [TestCase("element")]
        [TestCase("signature")]
        [TestCase("property-aba")]
        public void ContextRecipeRejectsUnsupportedOrChangedCurrentElements(string change)
        {
            WithRealFoo((unusedOwner, prepared) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders, map, removals, new PocWorkBudget(8192))!;
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var recipe = PreparedPocContextAttributeRecipe.TryCapture(context, boundary, new PocWorkBudget(262144));
                Assert.That(recipe, Is.Not.Null, "Real Foo must have a supported CURRENT native recipe before mutation.");
                var foo = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                var methods = (MethodProvider[])foo.CachedCanonicalView!.CachedMethods!;
                var method = methods[0];
                var original = method.Signature;
                if (change is "cycle" or "oversized")
                {
                    var arguments = change == "cycle" ? new CSharpType[] { typeof(int) } : Enumerable.Repeat<CSharpType>(typeof(int), 262145).ToArray();
                    var type = new CSharpType("RecipeShape", "Recipe", false, false, null, arguments, false, false);
                    if (change == "cycle") { arguments[0] = type; }
                    method.Update(signature: new MethodSignature(original.Name, null, original.Modifiers, type, null, []));
                    Assert.That(PreparedPocContextAttributeRecipe.TryCapture(context, boundary, new PocWorkBudget(262144)), Is.Null);
                    method.Update(signature: original);
                    return;
                }
                if (change == "element")
                {
                    methods[0] = methods[1];
                    Assert.That(recipe!.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False);
                    methods[0] = method;
                }
                else if (change == "signature")
                {
                    method.Update(signature: new MethodSignature(original.Name, null, original.Modifiers, typeof(decimal), null, []));
                    Assert.That(recipe!.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False);
                    method.Update(signature: original);
                }
                else
                {
                    var property = output.TypeProviders.Select(p => p.CachedCanonicalView).Where(p => p?.CachedProperties?.Count > 0).First()!.CachedProperties![0];
                    var type = property.Type;
                    property.Type = typeof(decimal);
                    property.Type = type;
                    Assert.That(recipe!.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False, "A tracked property type change/restoration must invalidate the captured operation.");
                }
                Assert.That(recipe!.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False, "Observed invalidation is sticky after restoration.");
            });
        }

        [Test]
        public void ContextRecipeRejectsInPlaceSignatureReturnTypeChangeAndRestoration()
        {
            WithRealFoo((unusedOwner, prepared) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders,
                    map, removals, new PocWorkBudget(8192))!;
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var recipe = PreparedPocContextAttributeRecipe.TryCapture(context, boundary, new PocWorkBudget(262144));
                Assert.That(recipe, Is.Not.Null);
                var foo = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                var method = foo.CachedCanonicalView!.CachedMethods!.First(m => m.Signature.ReturnType is not null);
                var signature = method.Signature;
                var original = signature.ReturnType!;
                signature.Update(returnType: new CSharpType(new ContextRuntimeTypeProbe(typeof(InterfaceObservationProbe))));
                Assert.That(method.Signature, Is.SameAs(signature), "The regression must mutate the existing signature, not replace it.");
                Assert.That(recipe!.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False);
                signature.Update(returnType: original);
                Assert.That(recipe.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False, "Observed invalidation stays sticky after restoration.");
            });
        }

        [Test]
        public void NativeContextDiscoveryReadsInPlaceSignatureReturnType()
        {
            var probe = new ContextRuntimeTypeProbe(typeof(InterfaceObservationProbe));
            WithRealFoo((unusedOwner, prepared) =>
            {
                Assert.That(probe.ContextInterfaceReads, Is.Zero);
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                _ = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(probe.ContextInterfaceReads, Is.GreaterThan(0));
            }, beforePocPreparation: (output, map) =>
            {
                var foo = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                foo.CachedCanonicalView!.CachedMethods!.First(m => m.Signature.ReturnType is not null).Signature.Update(returnType: new CSharpType(probe));
                probe.ContextInterfaceReads = 0;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangedWitnessedVisitorArgumentRetainsLegacyRebuildBoundary(bool finalizePocHeaders)
        {
            var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1) };
            var invocation = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "Call", arguments);
            ConstructedArgumentWitness.Record(invocation, arguments);
            long revision = invocation.DeclarationRevision;
            var opaque = new OpaqueHeaderArgument();
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { invocation });
            TypeProvider? context = null;
            object? before = null;
            object? origin = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                Assert.That(opaque.Writes, Is.Zero, "Preparation must not replay the operand writer.");
                Assert.That(ReferenceEquals(context!.CachedWriteAttributes, before), Is.True,
                    "The actual current unsupported argument must veto before the canonical builder changes its cache.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null); }
                var file = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(opaque.Writes, Is.EqualTo(1));
                Assert.That(file.Content, Does.Contain("opaque_header_argument"));
            }, finalizePocHeaders: finalizePocHeaders, additionalVisitor: new ContextPayloadVisitor(attribute),
                beforePocPreparation: (output, referenceMap) =>
                {
                    context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                    Assert.That(context.CachedWriteAttributes!.Any(item => ReferenceEquals(item, attribute)), Is.True,
                        "The real visitor attribute must survive native visitors and backcompat.");
                    Assert.That(ReferenceEquals(invocation.Arguments, arguments), Is.True);
                    Assert.That(invocation.DeclarationRevision, Is.EqualTo(revision));
                    Assert.That(ConstructedArgumentWitness.Read(invocation), Is.Not.Null);
                    arguments[0] = opaque;
                    before = context.CachedWriteAttributes;
                    origin = context.PocNativeAttributeCacheOrigin;
                    Assert.That(before, Is.Not.Null);
                    opaque.Writes = 0;
                });
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RequiredNullAttributeOperandRetainsNativeRebuildBoundary(bool finalizePocHeaders, bool binaryOperand)
        {
            // Deliberately malformed IR is not a valid-C# claim. The contract is
            // that its inherited native exception precedes any new early effect.
            var attribute = binaryOperand
                ? new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(CLSCompliantAttribute),
                    new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                    {
                        new Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression("+", null!,
                            new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1))
                    })
                : new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(CLSCompliantAttribute),
                    new KeyValuePair<string, Microsoft.TypeSpec.Generator.Expressions.ValueExpression>[]
                    {
                        new("SafeName", null!)
                    });
            TypeProvider? context = null;
            object? before = null;
            object? origin = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                Assert.That(ReferenceEquals(context!.CachedWriteAttributes, before), Is.True,
                    "Required missing operands must veto before canonical cache replacement.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null); }
                Assert.Throws<NullReferenceException>(() => new TypeProviderWriter(context)
                    { PreparedFieldTypes = prepared }.Write(), "The single native write must still reach the malformed operand.");
            }, finalizePocHeaders: finalizePocHeaders, additionalVisitor: new ContextPayloadVisitor(attribute),
                beforePocPreparation: (output, referenceMap) =>
                {
                    context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                    Assert.That(context.CachedWriteAttributes!.Any(item => ReferenceEquals(item, attribute)), Is.True);
                    before = context.CachedWriteAttributes;
                    origin = context.PocNativeAttributeCacheOrigin;
                    Assert.That(before, Is.Not.Null);
                });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CachedContextDocumentationFailurePrecedesCanonicalAttributes(bool finalizePocHeaders)
        {
            var operand = new ThrowingDocumentationOperand();
            var docs = new XmlDocProvider(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement(
                [System.Runtime.CompilerServices.FormattableStringFactory.Create("{0}", operand)]));
            Assert.That(operand.Calls, Is.Zero, "Constructing the fixture must not call the payload.");
            TypeProvider? context = null;
            object? before = null;
            object? origin = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                Assert.That(operand.Calls, Is.Zero, "Preparation must not replay native XML writing.");
                Assert.That(ReferenceEquals(context!.CachedWriteAttributes, before), Is.True,
                    "Unsupported cached docs must veto before the later native attribute operation.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
                Assert.Throws<InvalidOperationException>(() => new TypeProviderWriter(context)
                    { PreparedFieldTypes = prepared }.Write());
                Assert.That(operand.Calls, Is.EqualTo(1), "One native write must reach the original documentation exception.");
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, before), Is.True,
                    "The native documentation failure occurs before native attribute rebuilding.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
            }, finalizePocHeaders: finalizePocHeaders, additionalVisitor: new CachedContextDocumentationVisitor(docs),
                beforePocPreparation: (output, referenceMap) =>
                {
                    context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                    Assert.That(ReferenceEquals(context.CachedXmlDocs, docs), Is.True);
                    Assert.That(operand.Calls, Is.Zero, "Native visitors/reference-map setup must not call the payload.");
                    before = context.CachedWriteAttributes;
                    origin = context.PocNativeAttributeCacheOrigin;
                    Assert.That(before, Is.Not.Null);
                });
        }

        private sealed class CachedContextDocumentationVisitor(XmlDocProvider docs) : LibraryVisitor
        {
            protected override TypeProvider? VisitType(TypeProvider type)
            {
                if (type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                {
                    type.Update(xmlDocs: docs);
                }
                return type;
            }
        }

        private sealed class ThrowingDocumentationOperand
        {
            internal int Calls;
            public override bool Equals(object? other)
            {
                Calls++;
                throw new InvalidOperationException("native XML empty-line equality");
            }
            public override int GetHashCode() => base.GetHashCode();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomNumericCultureRetainsColdNativeDocumentationBoundary(bool emptyFallback)
        {
            (string Content, bool Cold, int PreparationCalls, int NativeCalls) Run(bool finalized)
            {
                var original = System.Globalization.CultureInfo.CurrentCulture;
                var culture = new NumericHeaderCulture();
                TypeProvider? client = null;
                string content = "";
                bool cold = false;
                int preparationCalls = 0;
                int nativeCalls = 0;
                try
                {
                    WithRealFoo((unusedOwner, prepared) =>
                    {
                        System.Globalization.CultureInfo.CurrentCulture = original;
                        preparationCalls = culture.Calls;
                        cold = client!.CachedXmlDocs is null;
                        culture.Calls = 0;
                        try
                        {
                            System.Globalization.CultureInfo.CurrentCulture = culture;
                            content = new TypeProviderWriter(client) { PreparedFieldTypes = prepared }.Write().Content;
                        }
                        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
                        nativeCalls = culture.Calls;
                        Assert.That(client.CachedXmlDocs, Is.Not.Null, "The one native write must perform the deferred getter.");
                    }, finalizePocHeaders: finalized, beforePocPreparation: (output, map) =>
                    {
                        client = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                            .Single(type => type.Name == "Foo");
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        if (emptyFallback)
                        {
                            var input = (Microsoft.TypeSpec.Generator.Input.InputClient)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)
                                .GetField("_inputClient", flags)!.GetValue(client)!;
                            typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Summary))!.SetValue(input, null);
                            typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Doc))!.SetValue(input, null);
                        }
                        typeof(TypeProvider).GetField("_description", flags)!.SetValue(client,
                            emptyFallback ? null : System.Runtime.CompilerServices.FormattableStringFactory.Create("safe {0}", "description"));
                        typeof(TypeProvider).GetField("_xmlDocs", flags)!.SetValue(client, null);
                        typeof(TypeProvider).GetField("_disabledFileWarnings", flags)!.SetValue(client, null);
                        culture.Calls = 0;
                        System.Globalization.CultureInfo.CurrentCulture = culture;
                    });
                }
                finally { System.Globalization.CultureInfo.CurrentCulture = original; }
                return (content, cold, preparationCalls, nativeCalls);
            }
            var native = Run(false);
            Assert.That(native.Cold, Is.True);
            Assert.That(native.NativeCalls, Is.GreaterThan(0));
            var finalized = Run(true);
            Assert.That(finalized.NativeCalls, Is.GreaterThan(0));
            Assert.That(finalized.Content, Is.EqualTo(native.Content));
            Assert.That(finalized.Cold, Is.True, "Warm-description and empty-fallback branches must not bypass the foreign-culture veto.");
            Assert.That(native.PreparationCalls, Is.Zero, "Non-authorizing metadata capture is also callback-free.");
            Assert.That(finalized.PreparationCalls, Is.Zero);
        }

        private sealed class NumericHeaderCulture : System.Globalization.CultureInfo
        {
            internal int Calls;
            internal NumericHeaderCulture() : base("en-US") { }
            public override System.Globalization.NumberFormatInfo NumberFormat
            {
                get { Calls++; return base.NumberFormat; }
                set => base.NumberFormat = value;
            }
            public override object? GetFormat(Type? formatType) { Calls++; return base.GetFormat(formatType); }
        }

        private sealed record OpaqueHeaderArgument : Microsoft.TypeSpec.Generator.Expressions.ValueExpression
        {
            internal int Writes { get; set; }
            internal override void WriteCore(CodeWriter writer)
            {
                Writes++;
                writer.AppendRaw("opaque_header_argument");
            }
        }

        [TestCase("- item", null)]
        [TestCase("1. item", null)]
        [TestCase("*italic*", null)]
        [TestCase("**bold**", null)]
        [TestCase("***both***", null)]
        [TestCase("first\r\nsecond", null)]
        [TestCase("{brace} & \"quote\"", null)]
        [TestCase("ignored summary", "selected doc")]
        [TestCase(null, null)]
        public void ColdSupportedDescriptionPreservesNativeOutput(string? summary, string? doc)
        {
            string Run(bool finalized)
            {
                TypeProvider? client = null;
                string? content = null;
                WithRealFoo((unusedOwner, prepared) =>
                {
                    Assert.That(client, Is.Not.Null);
                    if (finalized)
                    {
                        Assert.That(client!.CachedDescription, Is.Not.Null);
                        Assert.That(client.CachedXmlDocs, Is.Not.Null);
                        Assert.That(prepared.FinalizedPocHeaders!.Get(client), Is.Not.Null);
                        Assert.That(prepared.FinalizedPocHeaders.Get(client)!.IsCurrent, Is.True);
                    }
                    else
                    {
                        Assert.That(client!.CachedDescription, Is.Null);
                        Assert.That(client.CachedXmlDocs, Is.Null);
                    }
                    var description = client.CachedDescription;
                    var documentation = client.CachedXmlDocs;
                    content = new TypeProviderWriter(client) { PreparedFieldTypes = prepared }.Write().Content;
                    if (finalized)
                    {
                        Assert.That(ReferenceEquals(client.CachedDescription, description), Is.True);
                        Assert.That(ReferenceEquals(client.CachedXmlDocs, documentation), Is.True);
                    }
                }, finalizePocHeaders: finalized, beforePocPreparation: (output, referenceMap) =>
                {
                    client = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(type => type.Name == "Foo");
                    var input = (Microsoft.TypeSpec.Generator.Input.InputClient)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)
                        .GetField("_inputClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(client)!;
                    typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Summary))!.SetValue(input, summary);
                    typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Doc))!.SetValue(input, doc);
                    typeof(TypeProvider).GetField("_description", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, null);
                    typeof(TypeProvider).GetField("_xmlDocs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, null);
                    Assert.That(client.CachedDescription, Is.Null);
                    Assert.That(client.CachedXmlDocs, Is.Null);
                });
                return content!;
            }
            Assert.That(Run(true), Is.EqualTo(Run(false)));
        }

        [TestCase("\u200B", 1)]
        [TestCase("\u00AD", 1)]
        [TestCase("\u200B", 36)]
        [TestCase("\u00AD", 36)]
        [SetCulture("en-US")]
        public void ColdCultureSensitiveDescriptionPreservesNativeBoundary(string ignorable, int lines)
        {
            var prefix = "-" + ignorable + " ";
            Assert.That(prefix.StartsWith("- "), Is.True, "Establish actual native classification under this explicit culture.");
            var markdown = string.Concat(Enumerable.Repeat(prefix + "\n", lines));
            string Run(bool finalized)
            {
                TypeProvider? client = null;
                string? content = null;
                WithRealFoo((unusedOwner, prepared) =>
                {
                    var admitted = finalized && lines == 1;
                    Assert.That(client!.CachedDescription is not null, Is.EqualTo(admitted));
                    Assert.That(client.CachedXmlDocs is not null, Is.EqualTo(admitted));
                    if (finalized) { Assert.That(prepared.FinalizedPocHeaders!.Get(client) is not null, Is.EqualTo(admitted)); }
                    content = new TypeProviderWriter(client) { PreparedFieldTypes = prepared }.Write().Content;
                    Assert.That(client.CachedDescription, Is.Not.Null);
                    Assert.That(client.CachedXmlDocs, Is.Not.Null);
                    Assert.That(content, Does.Contain("<list type=\"bullet\">"));
                }, finalizePocHeaders: finalized, beforePocPreparation: (output, map) =>
                {
                    client = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(type => type.Name == "Foo");
                    var input = (Microsoft.TypeSpec.Generator.Input.InputClient)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)
                        .GetField("_inputClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(client)!;
                    typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Summary))!.SetValue(input, markdown);
                    typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Doc))!.SetValue(input, null);
                    foreach (var name in new[] { "_description", "_xmlDocs", "_disabledFileWarnings" })
                    { typeof(TypeProvider).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, null); }
                });
                return content!;
            }
            var native = Run(false);
            Assert.That(Run(true), Is.EqualTo(native), "Small admitted lists and larger legacy fallbacks both preserve native output.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ColdMarkdownDescriptionVetoesUnfundedExpansionBeforeCaching(bool finalizePocHeaders)
        {
            var markdown = string.Concat(Enumerable.Repeat("- \n", 1365));
            Assert.That(markdown.Length, Is.LessThanOrEqualTo(4096));
            Assert.That(Microsoft.TypeSpec.Generator.Utilities.DocHelpers.ConvertMarkdownToXml(markdown).Length, Is.GreaterThan(50000),
                "Independent native normalization establishes the actual expansion, not a guessed multiplier.");
            TypeProvider? client = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                Assert.That(client!.CachedDescription, Is.Null,
                    "An unfunded cold description must retain native write-time cache timing.");
                Assert.That(client.CachedXmlDocs, Is.Null);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(client), Is.Null); }
                var file = new TypeProviderWriter(client) { PreparedFieldTypes = prepared }.Write();
                Assert.That(client.CachedDescription, Is.Not.Null);
                Assert.That(client.CachedXmlDocs, Is.Not.Null);
                Assert.That(file.Content, Does.Contain("<list type=\"bullet\">"));
            }, finalizePocHeaders: finalizePocHeaders, beforePocPreparation: (output, referenceMap) =>
            {
                client = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(type => type.Name == "Foo");
                Assert.That(client.GetType(), Is.EqualTo(typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)));
                var input = (Microsoft.TypeSpec.Generator.Input.InputClient)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)
                    .GetField("_inputClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(client)!;
                typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Summary))!.SetValue(input, markdown);
                typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty(nameof(input.Doc))!.SetValue(input, null);
                typeof(TypeProvider).GetField("_description", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, null);
                typeof(TypeProvider).GetField("_xmlDocs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(client, null);
                Assert.That(client.CachedDescription, Is.Null);
                Assert.That(client.CachedXmlDocs, Is.Null);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnsupportedContextVisitorArgumentsRetainLegacyRebuildBoundary(bool finalizePocHeaders)
        {
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeCache = null;
            object? beforeOrigin = null;
            var arguments = new UnsupportedContextArguments();
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True,
                    "An unsupported visitor envelope must veto BEFORE an early native rebuild (compare identity without formatting self-enumerating IR).");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null); }
                Assert.That(arguments.Reads, Is.Zero, "The unsupported carrier indexer must not run during preparation.");
                Assert.That(arguments.CountReads, Is.Zero, "An unknown carrier's Count is not an admitted early callback either.");
                Assert.That(arguments.EnumerationReads, Is.Zero);
                var text = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False, "The single final native write must still rebuild.");
                Assert.That(text, Does.Contain("unsupported context visitor"));
                Assert.That(arguments.Reads, Is.GreaterThan(0), "The native attribute writer reads arguments by index, not enumeration.");
            }, additionalVisitor: new UnsupportedContextAttributeVisitor(arguments), finalizePocHeaders: finalizePocHeaders,
                beforePocPreparation: (output, map) =>
            {
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                beforeCache = context.CachedWriteAttributes;
                beforeOrigin = context.PocNativeAttributeCacheOrigin;
                arguments.Reads = 0;
                arguments.CountReads = 0;
                arguments.EnumerationReads = 0;
            });
        }

        [Test]
        public void ContextRecipeIdentityCacheProbeFundsActualCollisionChains()
        {
            WithRealFoo((_, _) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders,
                    map, removals, new PocWorkBudget(8192))!;
                var context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var recipe = PreparedPocContextAttributeRecipe.TryCapture(context, boundary, new PocWorkBudget(262144));
                Assert.That(recipe, Is.Not.Null);
                var types = (Dictionary<CSharpType, DeclaredTypeSnapshot>)typeof(PreparedPocContextAttributeRecipe)
                    .GetField("_types", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(recipe)!;
                types.Clear();
                var capacity = types.EnsureCapacity(32);
                var collisions = new List<CSharpType>();
                for (int candidate = 0; candidate < 100000 && collisions.Count < 32; candidate++)
                {
                    var type = new CSharpType("T" + candidate, "Budget", false, false, null, Array.Empty<CSharpType>(), true, false);
                    if ((uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(type) % (uint)capacity == 0) { collisions.Add(type); }
                }
                Assert.That(collisions.Count, Is.EqualTo(32));
                foreach (var type in collisions)
                {
                    Assert.That(DeclaredTypeSnapshot.TryCapture(type, out var snapshot), Is.True);
                    types.Add(type, snapshot!);
                }
                Assert.That(types.EnsureCapacity(0), Is.EqualTo(capacity));
                var capture = typeof(PreparedPocContextAttributeRecipe).GetMethod("CaptureType",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.That((bool)capture.Invoke(recipe, new object[] { collisions[0], new PocWorkBudget(1) })!, Is.False,
                    "The actual oldest key needs a full identity-hash bucket walk, not a constant-unit cache-hit assumption.");
                Assert.That((bool)capture.Invoke(recipe, new object[] { collisions[0], new PocWorkBudget(4096) })!, Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanonicalHeaderLeaseIsIndependentOfDisposedNamePlan(bool disposeHeaderOwner)
        {
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var headers = prepared.FinalizedPocHeaders!;
                Assert.That(headers.Get(context)!.Attributes, Is.Not.Null);
                var cache = context.CachedWriteAttributes;
                var origin = context.PocNativeAttributeCacheOrigin;
                prepared.Dispose(); // Name/source state is no longer available.
                var writer = new TypeProviderWriter(context) { PreparedPocHeaders = headers };
                if (disposeHeaderOwner)
                {
                    headers.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => writer.Write(), "An explicitly revoked lease must not silently replay a legacy build.");
                }
                else
                {
                    var file = writer.Write();
                    Assert.That(file.Content, Does.Contain("ModelReaderWriterContext"));
                    Assert.That(file.PreparedPocNameCount, Is.Zero, "A header lease carries no ordinary spelling permission.");
                }
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, cache), Is.True);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
            }, finalizePocHeaders: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiscardedSourcePreparationDoesNotReplayCanonicalAttributeBuild(bool finalizePocHeaders) =>
            RunDiscardedSourcePreparation(finalizePocHeaders, throwFromFinalCheck: false);

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowingSourcePreparationDoesNotReplayCanonicalAttributeBuild(bool finalizePocHeaders) =>
            RunDiscardedSourcePreparation(finalizePocHeaders, throwFromFinalCheck: true);

        private static void RunDiscardedSourcePreparation(bool finalizePocHeaders, bool throwFromFinalCheck)
        {
            bool armed = false;
            int directChecks = 0;
            WithRealFoo((_, _) => { }, observeAnalyzerActivity: true, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var frames = new System.Diagnostics.StackTrace().GetFrames();
                var index = Array.FindIndex(frames, frame => frame.GetMethod()?.DeclaringType == typeof(CompleteSourceInputSnapshot) &&
                    frame.GetMethod()?.Name == nameof(CompleteSourceInputSnapshot.IsCurrentFor));
                if (index >= 0 && index + 1 < frames.Length && frames[index + 1].GetMethod()?.DeclaringType == typeof(PreparedFieldTypes) &&
                    frames[index + 1].GetMethod()?.Name == "CreateForGenerationCore")
                {
                    directChecks++;
                    if (directChecks == 2 && throwFromFinalCheck) { throw new InvalidOperationException("Controlled unavailable source proof."); }
                    return directChecks < 2;
                }
                return true;
            }, beforePocPreparation: (output, map) =>
            {
                var context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var beforeCache = context.CachedWriteAttributes;
                var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                using var headerOwner = finalizePocHeaders ? new PreparedPocHeaderFinalization(CodeModelGenerator.Instance) : null;
                armed = true;
                using var discarded = finalizePocHeaders
                    ? PreparedFieldTypes.CreateForGenerationWithFinalizedPocHeaders(CodeModelGenerator.Instance, output.TypeProviders, map.ShouldWriteProvider, headerOwner!)
                    : PreparedFieldTypes.CreateForGenerationWithPocPlans(CodeModelGenerator.Instance, output.TypeProviders, map.ShouldWriteProvider);
                armed = false;
                Assert.That(directChecks, Is.EqualTo(2), "Only the final direct source-input check must veto after capture.");
                Assert.That(discarded, Is.Null, "A stale source proof must not retain spelling authority.");
                var afterCache = context.CachedWriteAttributes;
                var afterOrigin = context.PocNativeAttributeCacheOrigin;
                Assert.That(ReferenceEquals(afterCache, beforeCache), Is.EqualTo(!finalizePocHeaders));
                Assert.That(ReferenceEquals(afterOrigin, beforeOrigin), Is.EqualTo(!finalizePocHeaders));
                var outputText = new TypeProviderWriter(context) { PreparedFieldTypes = discarded, PreparedPocHeaders = headerOwner }.Write().Content;
                Assert.That(outputText, Does.Contain("ModelReaderWriterContext"));
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, afterCache), Is.EqualTo(finalizePocHeaders),
                    "Discarding name/source preparation must not replay an already-completed canonical native attribute build.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, afterOrigin), Is.EqualTo(finalizePocHeaders));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeContextCacheClearDoesNotRecreateVisitorAttributes(bool usePublicGetter)
        {
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("original lease payload") });
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(context.CachedWriteAttributes!.Any(item => ReferenceEquals(item, attribute)), Is.True,
                    "The actual native visitor payload must exist before the explicit clear.");
                var origin = context.PocNativeAttributeCacheOrigin;
                typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, null);
                if (usePublicGetter) { Assert.That(context.Attributes, Is.Not.Null); }
                var file = new TypeProviderWriter(context).Write();
                Assert.That(file.Content, Does.Not.Contain("original lease payload"),
                    "A native cache clear does not preserve visitor IR that existed only in the cleared carrier.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.False);
            }, additionalVisitor: new ContextPayloadVisitor(attribute));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratorSwitchRetiresOldCanonicalHeaderScope(bool finalized)
        {
            bool armed = false;
            bool switched = false;
            ScmCodeModelGenerator? original = null;
            ScmCodeModelGenerator? foreign = null;
            OutputLibrary? output = null;
            var scmField = typeof(ScmCodeModelGenerator).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var nativeOutput = typeof(ScmCodeModelGenerator).GetField("_scmOutputLibrary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("generator scope sentinel") });
            WithRealFoo((unusedOwner, prepared) =>
            {
                original = (ScmCodeModelGenerator)CodeModelGenerator.Instance;
                output = original.OutputLibrary;
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var headers = prepared.FinalizedPocHeaders;
                if (finalized) { Assert.That(headers!.Get(context)!.Attributes, Is.Not.Null); }
                var before = context.CachedWriteAttributes;
                var origin = context.PocNativeAttributeCacheOrigin;
                try
                {
                    armed = true;
                    var file = new TypeProviderWriter(context) { PreparedFieldTypes = prepared, PreparedPocHeaders = headers }.Write();
                    Assert.That(switched, Is.True, "The generator switch must occur inside the actual physical writer's source callback.");
                    Assert.That(ReferenceEquals(CodeModelGenerator.Instance, foreign), Is.True);
                    Assert.That(ReferenceEquals(ScmCodeModelGenerator.Instance, foreign), Is.True);
                    if (finalized) { Assert.That(headers!.IsActiveFor(foreign!), Is.False); }
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, before), Is.False,
                        "A new generator scope must use its native write, not consume a completed operation from the old scope.");
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.False);
                    Assert.That(file.Content, Does.Contain("generator scope sentinel"));
                    Assert.That(file.PreparedPocNameCount, Is.Zero);
                }
                finally
                {
                    armed = false;
                    CodeModelGenerator.Instance = original;
                    scmField.SetValue(null, original);
                }
            }, finalizePocHeaders: finalized, additionalVisitor: new ContextPayloadVisitor(attribute), sourceCurrent: () =>
            {
                if (armed && !switched && new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.DeclaringType == typeof(TypeProviderWriter)))
                {
                    // A distinct real SCM generator over the same already-built
                    // output isolates generator identity from output changes.
                    foreign = new ScmCodeModelGenerator(new GeneratorContext(original!.Configuration));
                    nativeOutput.SetValue(foreign, output);
                    CodeModelGenerator.Instance = foreign;
                    switched = true;
                }
                return true;
            });
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void CanonicalHeaderLeaseCannotBeConsumedByNestedWriter(bool finalized, bool retainNamePlan)
        {
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("nested lease sentinel") });
            WithRealFoo((owner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var headers = prepared.FinalizedPocHeaders;
                if (finalized) { Assert.That(headers!.Get(context)!.Attributes, Is.Not.Null); }
                var origin = context.PocNativeAttributeCacheOrigin;
                var cache = context.CachedWriteAttributes;
                if (!retainNamePlan) { prepared.Dispose(); }
                bool callback = false;
                var writer = new TypeProviderWriter(owner)
                {
                    PreparedFieldTypes = retainNamePlan ? prepared : null,
                    PreparedPocHeaders = headers
                };
                var file = writer.WriteWithMaterializedHeader((actual, writeDocs, docs) =>
                {
                    Assert.That(ReferenceEquals(actual, owner), Is.True);
                    Assert.That(new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.DeclaringType == typeof(TypeProviderWriter)), Is.True);
                    callback = true;
                    // The initial graph was admitted with the context as a
                    // physical root. Mutate only during this one native write;
                    // the nested emission must not borrow its physical lease.
                    owner.Update(nestedTypes: owner.NestedTypes.Append(context).ToArray());
                });
                Assert.That(callback, Is.True);
                Assert.That(file.Content, Does.Contain("nested lease sentinel"));
                var errors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(file.Content,
                    new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12))
                    .GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
                Assert.That(errors, Is.Empty, "Establish native declaration syntax separately from the mutated provider ownership graph.");
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, cache), Is.False,
                    "A nested writer has no physical canonical-header lease, including when a stale name plan remains non-null.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.False);
                Assert.That(file.PreparedPocNameCount, Is.Zero);
            }, finalizePocHeaders: finalized, additionalVisitor: new ContextPayloadVisitor(attribute));
        }

        [TestCase("replace")]
        [TestCase("mutate")]
        [TestCase("clear")]
        [TestCase("clear-public")]
        public void CanonicalHeaderLeasePreservesCacheMutationWithNoNamePlan(string change)
        {
            var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
            { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("original lease payload") };
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute), arguments);
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var headers = prepared.FinalizedPocHeaders!;
                Assert.That(headers.Get(context)!.Attributes, Is.Not.Null);
                var origin = context.PocNativeAttributeCacheOrigin;
                prepared.Dispose();
                if (change == "replace")
                {
                    context.Update(attributes: new[] { new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                        new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed lease payload") }) });
                }
                else if (change == "mutate")
                {
                    arguments[0] = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed lease payload");
                }
                else
                {
                    typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, null);
                    if (change == "clear-public") { Assert.That(context.Attributes, Is.Not.Null); }
                }
                Assert.That(headers.Get(context)!.IsCurrent, Is.False);
                var currentCache = context.CachedWriteAttributes;
                var file = new TypeProviderWriter(context) { PreparedPocHeaders = headers }.Write();
                Assert.That(file.PreparedPocNameCount, Is.Zero);
                if (change is "replace" or "mutate")
                {
                    Assert.That(file.Content, Does.Contain("changed lease payload"));
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, currentCache), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
                }
                else
                {
                    Assert.That(file.Content, Does.Not.Contain("original lease payload"),
                        "Match NativeContextCacheClearDoesNotRecreateVisitorAttributes: explicit clear retires visitor-only cached IR.");
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, currentCache), Is.False);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.False);
                }
            }, finalizePocHeaders: true, additionalVisitor: new ContextPayloadVisitor(attribute));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ForeignObservedOutputCannotPreflightActualContextBuilder(bool finalizeContextAttributes)
        {
            WithRealFoo((unusedOwner, prepared) => { }, observeAnalyzerActivity: true, beforePocPreparation: (actual, originalMap) =>
            {
                var roots = actual.TypeProviders;
                var foreign = new OutputLibrary { TypeProviders = roots };
                using var foreignMap = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(foreign, roots);
                Assert.That(foreignMap.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, foreign, roots,
                    foreignMap, removals, new PocWorkBudget(8192));
                Assert.That(boundary, Is.Not.Null, "A session may legitimately observe a different output; that is not native-builder identity.");
                var context = roots.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var beforeCache = context.CachedWriteAttributes;
                var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                var unknown = new ForeignOutputContextProviderProbe();
                actual.TypeProviders = roots.Concat(new[] { unknown }).ToArray();
                Assert.That(ReferenceEquals(CodeModelGenerator.Instance.OutputLibrary, actual), Is.True);
                Assert.That(ReferenceEquals(foreign.TypeProviders, roots), Is.True);
                try
                {
                    if (finalizeContextAttributes)
                    {
                        var headers = new PreparedPocHeaderFinalization();
                        object?[] arguments = [context, boundary, false];
                        var facet = typeof(PreparedPocHeaderFinalization).GetMethod("CaptureAttributes",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(headers, arguments);
                        Assert.That(unknown.PropertyBuilds, Is.Zero, "The recipe's foreign output must not admit early native traversal of the actual output.");
                        Assert.That(facet, Is.Null);
                        Assert.That((bool)arguments[2]!, Is.False);
                    }
                    Assert.That(unknown.PropertyBuilds, Is.Zero);
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                    var text = new TypeProviderWriter(context).Write().Content;
                    Assert.That(text, Does.Contain("ModelReaderWriterContext"));
                    Assert.That(unknown.PropertyBuilds, Is.EqualTo(1), "The single legacy final write must still traverse the actual unknown provider.");
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
                }
                finally { actual.TypeProviders = roots; }
            });
        }

        private sealed class ForeignOutputContextProviderProbe : TypeProvider
        {
            internal int PropertyBuilds { get; private set; }
            protected override string BuildName() => "ForeignOutputContextProvider";
            protected override string BuildRelativeFilePath() => "ForeignOutputContextProvider.cs";
            protected internal override PropertyProvider[] BuildProperties() { PropertyBuilds++; return []; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeaderDocumentationCannotDispatchForeignRuntimeCacheEquality(bool finalizePocHeaders)
        {
            var cache = (Dictionary<Type, CSharpType>)typeof(CSharpType).GetField("_cache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            var saved = cache.ToArray();
            var probe = new HeaderDocumentationCacheEqualityProbe();
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeContextCache = null;
            object? beforeContextOrigin = null;
            try
            {
                WithRealFoo((owner, prepared) =>
                {
                    if (finalizePocHeaders)
                    {
                        Assert.That(prepared.FinalizedPocHeaders!.Get(owner), Is.Null,
                            "An unsupported cached document must veto the header before warnings or attributes, even when native inherit skips its exception.");
                    }
                    Assert.That(probe.HeaderEqualityCalls, Is.Zero,
                        "Header finalization must veto an unsafe runtime cache without invoking foreign key equality.");
                    var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeContextCache), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeContextOrigin), Is.True);
                    probe.EqualityCalls = 0;
                    using var writer = new CodeWriter();
                    writer.WriteXmlDocsNoScope(owner.XmlDocs);
                    Assert.That(writer.ToString(false), Does.Contain("<inheritdoc/>"));
                    Assert.That(probe.EqualityCalls, Is.Zero, "The native inherit writer never consumes the exception.");
                    if (finalizePocHeaders)
                    {
                        // Retain the supported-header positive with the same
                        // cached document, changing only the hostile cache key.
                        // The probe deliberately returns false even for its
                        // own Equals call, so Dictionary.Remove cannot find it.
                        var supportedEntries = cache.Where(entry => !ReferenceEquals(entry.Key, probe)).ToArray();
                        Assert.That(supportedEntries.Length, Is.EqualTo(cache.Count - 1));
                        cache.Clear();
                        foreach (var entry in supportedEntries) { cache.Add(entry.Key, entry.Value); }
                        using var supported = new PreparedPocHeaderFinalization();
                        supported.Materialize(owner, CodeModelGenerator.Instance.InputLibrary.InputNamespace);
                        Assert.That(supported.Get(owner), Is.Not.Null);
                        Assert.That(supported.Get(owner)!.WritesDocs, Is.True);
                        Assert.That(probe.HeaderEqualityCalls, Is.Zero);
                    }
                }, finalizePocHeaders: finalizePocHeaders, beforePocPreparation: (output, map) =>
                {
                    var owner = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                    var docs = new XmlDocProvider(inherit: new Microsoft.TypeSpec.Generator.Statements.XmlDocInheritStatement(),
                        exceptions: new[] { new Microsoft.TypeSpec.Generator.Statements.XmlDocExceptionStatement(typeof(ArithmeticException), "unused", []) });
                    Assert.That(PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs), Is.Not.Null,
                        "Use an actually supported exception carrier, not an opaque compiler projection that vetoes before lookup.");
                    owner.Update(xmlDocs: docs);
                    var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                    beforeContextCache = context.CachedWriteAttributes;
                    beforeContextOrigin = context.PocNativeAttributeCacheOrigin;
                    CSharpType ignored = probe;
                    probe.EqualityCalls = 0;
                    _ = CSharpType.TryGetCachedRuntimeType(typeof(ArithmeticException), out _);
                    Assert.That(probe.EqualityCalls, Is.GreaterThan(0), "The inserted key must actually intercept the target dictionary bucket.");
                    probe.EqualityCalls = 0;
                    probe.HeaderEqualityCalls = 0;
                });
            }
            finally
            {
                cache.Clear();
                foreach (var pair in saved) { cache.Add(pair.Key, pair.Value); }
            }
        }

        private sealed class HeaderDocumentationCacheEqualityProbe() : System.Reflection.TypeDelegator(typeof(ArithmeticException))
        {
            internal int EqualityCalls { get; set; }
            internal int HeaderEqualityCalls { get; set; }
            public override int GetHashCode() => typeof(ArithmeticException).GetHashCode();
            public override bool Equals(object? other) => ObserveEquality();
            public override bool Equals(Type? other) => ObserveEquality();
            private bool ObserveEquality()
            {
                EqualityCalls++;
                if (new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                    frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(PreparedPocHeaderFinalization), StringComparison.Ordinal) == true))
                { HeaderEqualityCalls++; }
                return false;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnfundedRemovalSnapshotIsNotRetainedByPocPreparation(bool finalizePocHeaders)
        {
            WithRealFoo((unusedOwner, prepared) =>
            {
                var snapshot = typeof(PreparedFieldTypes).GetField("_pocRemovalCandidates",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared);
                Assert.That(snapshot is null, Is.True,
                    "The POC must reject insufficient snapshot funding BEFORE allocating/hashing an immutable removal snapshot.");
            }, precomputePoc: true, finalizePocHeaders: finalizePocHeaders, beforePocPreparation: (output, map) =>
            {
                var candidates = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
                candidates.Clear();
                for (int i = 0; i < 64; i++) { candidates.Add($"R{i:D7}"); }
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var legacySnapshot), Is.True,
                    "The unchanged native session's absolute bounds admit this input; only POC work funding must veto.");
                Assert.That(legacySnapshot!.Count, Is.EqualTo(64));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LargeSupportedContextVisitorRetainsLegacyRebuildBoundary(bool finalizePocHeaders)
        {
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeCache = null;
            object? beforeOrigin = null;
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(
                new CSharpType(typeof(PocVariadicContextAttribute)),
                Enumerable.Range(0, 1536).Select(i => (Microsoft.TypeSpec.Generator.Expressions.ValueExpression)
                    new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(i)).ToArray(), []);
            Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[] payload = [attribute];
            var largeEnvelope = PreparedPocAttributeEnvelope.TryCapture(payload, new PocWorkBudget(262144));
            Assert.That(largeEnvelope, Is.Not.Null, "The fixture must have a supported envelope under the recipe's larger budget.");
            Assert.That(largeEnvelope!.IsCurrent(new PocWorkBudget(262144)), Is.True);
            var smallEnvelope = PreparedPocAttributeEnvelope.TryCapture(payload, new PocWorkBudget(4096));
            Assert.That(smallEnvelope is null || !smallEnvelope.IsCurrent(new PocWorkBudget(4096)), Is.True,
                "The fixture must exceed the actual final header envelope limit, not a separate literal-size admission gate.");
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True,
                    "A supported payload too large for the final header envelope must veto BEFORE native materialization.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null); }
                var text = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                Assert.That(text, Does.Contain("PocVariadicContextAttribute").And.Contain("1535"));
            }, additionalVisitor: new ContextPayloadVisitor(attribute), finalizePocHeaders: finalizePocHeaders,
                beforePocPreparation: (output, map) =>
            {
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                beforeCache = context.CachedWriteAttributes;
                beforeOrigin = context.PocNativeAttributeCacheOrigin;
            });
        }

        [TestCase(8191, false)]
        [TestCase(262144, true)]
        public void ContextActualRecipeBudgetPreservesPreBuildBoundary(int remaining, bool shouldBuild)
        {
            WithRealFoo((_, _) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders,
                    map, removals, new PocWorkBudget(8192));
                Assert.That(boundary, Is.Not.Null);
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var beforeCache = context.CachedWriteAttributes;
                var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                var recipeBudget = new PocWorkBudget(262144);
                var recipe = PreparedPocContextAttributeRecipe.TryCapture(context, boundary!, recipeBudget);
                Assert.That(recipe, Is.Not.Null, "The actual finalized context must support the complete bounded recipe.");
                Assert.That(recipe!.TryReservePostBuildCurrentness(recipeBudget, out _), Is.True);
                var required = 8192 + 262144 - recipeBudget.Remaining;
                Assert.That(required, Is.LessThan(262144), "The complete actual recipe plus final envelope fits the moderate phase budget.");
                using var headers = new PreparedPocHeaderFinalization();
                var phase = (PocWorkBudget)typeof(PreparedPocHeaderFinalization)
                    .GetField("_budget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(headers)!;
                Assert.That(phase.TrySpend(1048576 - remaining), Is.True);
                object?[] arguments = [context, boundary, false];
                var facet = typeof(PreparedPocHeaderFinalization).GetMethod("CaptureAttributes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(headers, arguments);
                Assert.That(facet is not null, Is.EqualTo(shouldBuild));
                Assert.That((bool)arguments[2]!, Is.EqualTo(shouldBuild));
                if (!shouldBuild)
                {
                    Assert.That(context.CachedWriteAttributes, Is.SameAs(beforeCache), "Insufficient envelope funding must precede any native rebuild.");
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.SameAs(beforeOrigin));
                }
                else
                {
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.Not.Null);
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.Not.SameAs(beforeOrigin));
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContextActualRecipeRequiresExactPreBuildCredits(bool oneShort)
        {
            WithRealFoo((_, _) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders,
                    map, removals, new PocWorkBudget(8192));
                Assert.That(boundary, Is.Not.Null);
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var beforeCache = context.CachedWriteAttributes;
                var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                var measured = new PocWorkBudget(262144);
                var recipe = PreparedPocContextAttributeRecipe.TryCapture(context, boundary!, measured);
                Assert.That(recipe, Is.Not.Null);
                Assert.That(recipe!.TryReservePostBuildCurrentness(measured, out _), Is.True);
                var required = 8192 + 262144 - measured.Remaining;
                Assert.That(required, Is.GreaterThan(8192).And.LessThan(262144));
                using var headers = new PreparedPocHeaderFinalization();
                var phase = (PocWorkBudget)typeof(PreparedPocHeaderFinalization)
                    .GetField("_budget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(headers)!;
                Assert.That(phase.TrySpend(1048576 - required + (oneShort ? 1 : 0)), Is.True);
                object?[] arguments = [context, boundary, false];
                var facet = typeof(PreparedPocHeaderFinalization).GetMethod("CaptureAttributes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(headers, arguments);
                Assert.That(facet is not null, Is.EqualTo(!oneShort));
                Assert.That((bool)arguments[2]!, Is.EqualTo(!oneShort));
                if (oneShort)
                {
                    Assert.That(context.CachedWriteAttributes, Is.SameAs(beforeCache));
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.SameAs(beforeOrigin));
                }
                else
                {
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.Not.Null.And.Not.SameAs(beforeOrigin));
                    Assert.That(phase.Remaining, Is.Zero, "No second phase debit may occur after the native rebuild.");
                }
            });
        }

        [Test]
        public void ContextAggregateEnvelopeBudgetIsReservedBeforeNativeRebuild()
        {
            WithRealFoo((unusedOwner, prepared) => { }, observeAnalyzerActivity: true, beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var boundary = PreparedPocPostMapAttributeBoundary.TryCapture(CodeModelGenerator.Instance, output, output.TypeProviders,
                    map, removals, new PocWorkBudget(8192));
                Assert.That(boundary, Is.Not.Null);
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var beforeCache = context.CachedWriteAttributes;
                var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                var headers = new PreparedPocHeaderFinalization();
                var budget = (PocWorkBudget)typeof(PreparedPocHeaderFinalization)
                    .GetField("_budget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(headers)!;
                Assert.That(budget.TrySpend(1048576 - 8191), Is.True,
                    "Leave one credit less than the required final capture/readback envelope reservation.");
                object?[] arguments = [context, boundary, false];
                var facet = typeof(PreparedPocHeaderFinalization).GetMethod("CaptureAttributes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(headers, arguments);
                Assert.That(facet, Is.Null);
                Assert.That((bool)arguments[2]!, Is.False);
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PostBuildRecipeBudgetRetainsLegacyRebuildBoundary(bool finalizeContextAttributes) =>
            RunPostBuildRecipeBudgetBoundary(finalizeContextAttributes, finalizedRoute: false);

        [TestCase(false)]
        [TestCase(true)]
        public void PostBuildRecipeBudgetRetainsLegacyRebuildBoundaryOnFinalizedRoute(bool finalizeContextAttributes) =>
            RunPostBuildRecipeBudgetBoundary(finalizeContextAttributes, finalizedRoute: true);

        private static void RunPostBuildRecipeBudgetBoundary(bool finalizeContextAttributes, bool finalizedRoute)
        {
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeCache = null;
            object? beforeOrigin = null;
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True,
                    "Insufficient post-build verification funding must veto BEFORE changing the native cache.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                if (finalizedRoute && finalizeContextAttributes) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)?.Attributes, Is.Null); }
                var text = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(text, Does.Contain("ModelReaderWriterContext"));
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
            }, observeAnalyzerActivity: true, finalizePocHeaders: finalizedRoute && finalizeContextAttributes,
                beforePocPreparation: (output, map) =>
            {
                Assert.That(map.TryCaptureObservedRemovalCandidates(out var removals), Is.True);
                var context = output.TypeProviders.Single(p => p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                Assert.That(context.CachedWriteAttributes!.Count, Is.Zero);
                Assert.That(context.CachedOriginalAttributes!.Count, Is.Zero);
                beforeCache = context.CachedWriteAttributes;
                beforeOrigin = context.PocNativeAttributeCacheOrigin;
                Assert.That(context.TryCopyCurrentPocAttributeProjection(beforeCache!, new PocWorkBudget(4096), out var emptyProjection), Is.True);
                Assert.That(emptyProjection, Is.Empty);
                PreparedPocPostMapAttributeBoundary Boundary() => PreparedPocPostMapAttributeBoundary.TryCapture(
                    CodeModelGenerator.Instance, output, output.TypeProviders, map, removals, new PocWorkBudget(8192))!;
                var baselineBudget = new PocWorkBudget(262144);
                var baseline = PreparedPocContextAttributeRecipe.TryCapture(context, Boundary(), baselineBudget);
                Assert.That(baseline, Is.Not.Null);
                Assert.That(baseline!.IsCurrent(baselineBudget, afterBuild: false), Is.True);
                var view = output.TypeProviders.Single(p => p.Name == "ClientUriBuilder").CachedCanonicalView!;
                var methods = (MethodProvider[])view.CachedMethods!;
                var reset = methods.Single(m => m.Signature.Name == "Reset");
                Assert.That(reset.GetType(), Is.EqualTo(typeof(MethodProvider)));
                Assert.That(reset.Signature.GetType(), Is.EqualTo(typeof(MethodSignature)));
                Assert.That(reset.Signature.ReturnType, Is.Null, "Repeat the actual supported void method, not a malformed signature.");
                // Four units in capture/native enumeration, then two in each
                // of the two already-required pre-build currentness passes.
                var repetitions = baselineBudget.Remaining / 8;
                Assert.That(repetitions, Is.GreaterThan(0));
                view.Update(methods: methods.Concat(Enumerable.Repeat(reset, repetitions)).ToArray());
                var edgeBudget = new PocWorkBudget(262144);
                var edge = PreparedPocContextAttributeRecipe.TryCapture(context, Boundary(), edgeBudget);
                Assert.That(edge, Is.Not.Null, "The measured supported profile must pass recipe construction.");
                Assert.That(edge!.IsCurrent(edgeBudget, afterBuild: false), Is.True);
                Assert.That(edgeBudget.Remaining, Is.LessThan(8), "The fixture must exhaust only POST-build verification, not preflight.");
                Assert.That(edge.TryReservePostBuildCurrentness(edgeBudget, out _), Is.False,
                    "The new gate must reserve verification before permitting a native build.");
                Assert.That(edge.IsCurrent(new PocWorkBudget(262144), afterBuild: false), Is.False, "A funding veto is sticky.");
                if (finalizeContextAttributes && !finalizedRoute)
                {
                    var headers = new PreparedPocHeaderFinalization();
                    object?[] arguments = [context, Boundary(), false];
                    var facet = typeof(PreparedPocHeaderFinalization).GetMethod("CaptureAttributes",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(headers, arguments);
                    Assert.That(facet, Is.Null);
                    Assert.That((bool)arguments[2]!, Is.False);
                }
                Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True,
                    "Pre-build admission alone cannot fund the separate post-build verification pass.");
                Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullCanonicalMethodSignatureRetainsNativeExceptionTiming(bool finalizePocHeaders)
        {
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                if (finalizePocHeaders) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)!.Attributes, Is.Null); }
                Assert.Throws<NullReferenceException>(() => new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write());
            }, finalizePocHeaders: finalizePocHeaders, beforePocPreparation: (output, map) =>
            {
                var foo = output.TypeProviders.Single(p => p.Name == "Foo" && p is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider);
                var methods = (MethodProvider[])foo.CachedCanonicalView!.CachedMethods!;
                methods[0] = new MethodProvider(null!, foo, new XmlDocProvider());
            });
        }

        [AttributeUsage(AttributeTargets.Class)]
        public sealed class PocVariadicContextAttribute(params int[] values) : Attribute
        {
            public IReadOnlyList<int> Values { get; } = values;
        }

        private sealed class ContextPayloadVisitor(Microsoft.TypeSpec.Generator.Statements.AttributeStatement attribute) : LibraryVisitor
        {
            protected override TypeProvider? VisitType(TypeProvider type)
            {
                if (type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition) { type.Update(attributes: [attribute]); }
                return type;
            }
        }

        private sealed class UnsupportedContextAttributeVisitor(UnsupportedContextArguments arguments) : LibraryVisitor
        {
            protected override TypeProvider? VisitType(TypeProvider type)
            {
                if (type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                {
                    type.Update(attributes: [new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), arguments)]);
                }
                return type;
            }
        }

        private sealed class UnsupportedContextArguments : IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>
        {
            internal int Reads { get; set; }
            internal int CountReads { get; set; }
            internal int EnumerationReads { get; set; }
            public int Count { get { CountReads++; return 1; } }
            public Microsoft.TypeSpec.Generator.Expressions.ValueExpression this[int index]
            {
                get
                {
                    if (index != 0) { throw new ArgumentOutOfRangeException(nameof(index)); }
                    Reads++;
                    return new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("unsupported context visitor");
                }
            }
            public IEnumerator<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> GetEnumerator()
            {
                EnumerationReads++;
                return ((IEnumerable<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>)new[] { this[0] }).GetEnumerator();
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private interface IJsonModel<T> { }
        private sealed class NameOnlyContextInterface : IJsonModel<int> { }

        [Test]
        public void ContextInterfaceObservationsKeepNativeNameAndIdentityPoliciesSeparate()
        {
            WithRealFoo((unusedOwner, prepared) => { }, beforePocPreparation: (output, map) =>
            {
                var runtimeType = typeof(NameOnlyContextInterface);
                Assert.That(Utilities.ModelReaderWriterHelpers.ImplementsModelReaderWriter(runtimeType), Is.False);
                var direct = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                    .GetMethod("ImplementsIPersistableModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                Assert.That(direct.Invoke(null, new object?[] { new CSharpType(runtimeType), null }), Is.True);
                Assert.That(Utilities.PocContextInterfaceObservations.TrySpendHelperNegative(runtimeType, new PocWorkBudget(4096).TrySpend), Is.True);
                Assert.That(Utilities.PocContextInterfaceObservations.TrySpendDirectNegative(runtimeType, new PocWorkBudget(4096).TrySpend), Is.False);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContextInterfaceObservationOverflowOrConflictIsSticky(bool conflict)
        {
            WithRealFoo((unusedOwner, prepared) =>
            {
                var type = typeof(InterfaceObservationProbe);
                Assert.That(ClientModel.Utilities.ModelReaderWriterHelpers.ImplementsModelReaderWriter(type), Is.False);
                ClientModel.Utilities.PocContextInterfaceObservations.Record(type, conflict ? 1 : 65537, false);
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendHelperNegative(type, _ => true), Is.False);
                ClientModel.Utilities.PocContextInterfaceObservations.Record(type, 0, false);
                Assert.That(ClientModel.Utilities.PocContextInterfaceObservations.TrySpendHelperNegative(type, _ => true), Is.False);
            });
        }

        private sealed class ContextOutputGetterProbe(GeneratorContext context) : ScmCodeModelGenerator(context)
        {
            internal int ContextReads { get; private set; }
            internal int AllReads { get; private set; }
            internal void ResetReads() { ContextReads = 0; AllReads = 0; }
            public override OutputLibrary OutputLibrary
            {
                get
                {
                    AllReads++;
                    if (new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                        frame.GetMethod()?.DeclaringType == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition) &&
                        frame.GetMethod()?.Name == "CollectBuildableTypes")) { ContextReads++; }
                    return base.OutputLibrary;
                }
            }
        }

        [Test]
        public void UnknownContextOutputGetterRetainsLegacyWriteTimeCallbacks()
        {
            ContextOutputGetterProbe? probe = null;
            int? nativePreparationReads = null;
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? beforeCache = null;
            object? beforeOrigin = null;
            void BeforePreparation(OutputLibrary output, ProviderReferenceMapSession map)
            {
                var context = output.TypeProviders.Single(type => type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                beforeCache = context.CachedWriteAttributes;
                beforeOrigin = context.PocNativeAttributeCacheOrigin;
                probe!.ResetReads();
            }
            foreach (var finalized in new[] { false, true })
            {
                WithRealFoo((unusedOwner, prepared) =>
                {
                    var context = prepared.SourceFiles.Single(file => file.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                    Assert.That(probe!.ContextReads, Is.Zero, "Unknown output dispatch must not run during preparation.");
                    if (!finalized) { nativePreparationReads = probe.AllReads; }
                    else { Assert.That(probe.AllReads, Is.EqualTo(nativePreparationReads), "The finalized route must not add direct getter calls outside the context stack to existing native preparation."); }
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                    if (finalized) { Assert.That(prepared.FinalizedPocHeaders!.Get(context)?.Attributes, Is.Null); }
                    _ = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                    Assert.That(probe.ContextReads, Is.GreaterThan(0), "An unaudited virtual dependency must retain native write-time rebuilding callbacks.");
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
                }, generatorFactory: context => probe = new ContextOutputGetterProbe(context), finalizePocHeaders: finalized,
                    beforePocPreparation: BeforePreparation);
            }
        }

        [Test]
        public void RealFooCanonicalProjectionOmitsOnlyRedundantWitness()
        {
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], []);
            WithRealFoo((_, _) => { }, additionalVisitor: new ContextPayloadVisitor(attribute), finalizePocHeaders: true,
                inspectGeneration: (output, referenceMap, unusedOwner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var context = source.FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                var carrier = context.CachedWriteAttributes;
                Assert.That(carrier, Is.Not.Null);
                Assert.That(carrier!.Count, Is.EqualTo(1));
                Assert.That(carrier[0], Is.SameAs(attribute), "The actual visitor element identity must survive native rebuilding.");
                Assert.That(header.Attributes, Is.Not.Null);
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                Assert.That(context.PocPostMapAttributeOperation, Is.Not.Null);
                Assert.That(context.TryCopyCurrentPocAttributeProjection(carrier, new PocWorkBudget(0), out _), Is.False);
                Assert.That(context.TryCopyCurrentPocAttributeProjection(carrier, new PocWorkBudget(1), out _), Is.False);
                Assert.That(context.TryCopyCurrentPocAttributeProjection(carrier, new PocWorkBudget(2), out var live), Is.True);
                Assert.That(live.Single(), Is.SameAs(attribute));
                Assert.That(ContractCollections.TryCopy(carrier, out _), Is.False, "Shared Task03 copying remains opaque.");
                var rootPath = source.Roots[0].Id.MemberPath;
                Assert.That(source.Roots.Any(root => root.Id.MemberPath == rootPath && root.Id.RootRole == "type-attribute"), Is.False);
                var registered = ConstructedCollectionWitness.TryCopyConstruction<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>(carrier, new PocWorkBudget(4096), out var witnessed);
                if (registered) { Assert.That(witnessed.Single(), Is.SameAs(attribute)); }
                var plan = prepared.PocGenerationEnvelope!.GetFile(context)!;
                Assert.That(plan.HasCompleteNativeConsumption, Is.False);
                var origin = context.PocNativeAttributeCacheOrigin;
                var writes = 0;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, context)) { continue; }
                    writes++;
                    Assert.That(file.Content, Does.Contain("Obsolete"));
                    Assert.That(plan.HasCompleteNativeConsumption, Is.True, plan.NativeConsumptionFailure);
                    Assert.That(context.CachedWriteAttributes, Is.SameAs(carrier));
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.SameAs(origin));
                }
                Assert.That(writes, Is.EqualTo(1));
                Assert.That(registered, Is.False,
                    "The audited canonical rebuild must not register the redundant process-global construction witness.");
            });
        }

        [Test]
        public void CanonicalProjectionRetainsLiveAttributeMutationChecks()
        {
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] arguments =
                [new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("original")];
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), arguments, []);
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var context = source.FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                var carrier = context.CachedWriteAttributes!;
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                Assert.That(context.TryCopyCurrentPocAttributeProjection(carrier, new PocWorkBudget(4096), out var live), Is.True);
                Assert.That(live.Single(), Is.SameAs(attribute));
                Assert.That(header.Attributes!.IsCurrent(new PocWorkBudget(4096)), Is.True);
                var original = arguments[0];
                try
                {
                    arguments[0] = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed");
                    Assert.That(header.Attributes.IsCurrent(new PocWorkBudget(4096)), Is.False,
                        "The owner-bound carrier identity cannot replace checks of its actual live child elements.");
                }
                finally { arguments[0] = original; }
                Assert.That(header.Attributes.IsCurrent(new PocWorkBudget(4096)), Is.False, "An observed POC mismatch remains sticky.");
                Assert.That(context.TryCopyCurrentPocAttributeProjection(carrier, new PocWorkBudget(4096), out var restored), Is.True);
                Assert.That(restored.Single(), Is.SameAs(attribute));
                Assert.That(source.IsCurrent(), Is.True, "POC-only mismatch must not stale the shared opaque source graph.");
                Assert.That(prepared.CanCommit(), Is.True);
            }, additionalVisitor: new ContextPayloadVisitor(attribute), finalizePocHeaders: true);
        }

        [Test]
        public void CanonicalProjectionRejectsUnknownReplacementWithoutCallbacks()
        {
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], []);
            WithRealFoo((unusedOwner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var context = source.FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                Assert.That(context.PocPostMapAttributeOperation, Is.Not.Null);
                var cache = context.CachedWriteAttributes;
                Assert.That(cache, Is.Not.Null);
                Assert.That(context.TryCopyCurrentPocAttributeProjection(cache!, new PocWorkBudget(4096), out var live), Is.True);
                Assert.That(live.Single(), Is.SameAs(attribute));
                var field = typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var unknown = new UnknownCanonicalAttributeCarrier();
                try
                {
                    field.SetValue(context, unknown);
                    Assert.DoesNotThrow(() => Assert.That(PreparedPocFileConsumption.TryCapture(source, null, new PocWorkBudget(65536), out _), Is.Null));
                    Assert.That(unknown.Reads, Is.Zero, "A non-owned replacement cannot dispatch Count, indexer or enumeration.");
                }
                finally { field.SetValue(context, cache); }
                Assert.That(source.IsCurrent(), Is.True);
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
            }, additionalVisitor: new ContextPayloadVisitor(attribute), finalizePocHeaders: true);
        }

        private sealed class UnknownCanonicalAttributeCarrier : IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>
        {
            internal int Reads;
            public int Count { get { Reads++; throw new InvalidOperationException("Unknown Count must not execute."); } }
            public Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement this[int index]
            { get { Reads++; throw new InvalidOperationException("Unknown indexer must not execute."); } }
            public IEnumerator<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement> GetEnumerator()
            { Reads++; throw new InvalidOperationException("Unknown enumeration must not execute."); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void RealFooOrdinaryContextRebuildRetainsConstructionWitness()
        {
            var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], []);
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var output = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                Assert.That(output.Content, Does.Contain("Obsolete"));
                var carrier = context.CachedWriteAttributes!;
                Assert.That(carrier.Count, Is.EqualTo(1));
                Assert.That(carrier[0], Is.SameAs(attribute));
                Assert.That(ConstructedCollectionWitness.TryCopyConstruction<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>(carrier, new PocWorkBudget(4096), out var witnessed), Is.True,
                    "Ordinary native rebuilding outside the approved canonical call retains its witness.");
                Assert.That(witnessed.Single(), Is.SameAs(attribute));
            }, additionalVisitor: new ContextPayloadVisitor(attribute));
        }

        [Test]
        public void RealFooNonemptyCanonicalAttributesPreserveNativeWriterContent()
        {
            string? native = null;
            string? canonical = null;
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                native = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(context.CachedWriteAttributes!.Count, Is.EqualTo(1));
            }, additionalVisitor: new ContextAttributeVisitor());
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                Assert.That(header.Attributes, Is.Not.Null);
                Assert.That(header.Attributes!.Count, Is.EqualTo(1));
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                canonical = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(header.IsCurrent, Is.True);
            }, additionalVisitor: new ContextAttributeVisitor(), finalizePocHeaders: true);
            Assert.That(canonical, Is.EqualTo(native));
            Assert.That(canonical, Does.Contain("Obsolete"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanonicalAttributeCacheMutationVetoesSnapshotWithoutReplayingWriter(bool clearCache)
        {
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                var cacheField = typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var original = cacheField.GetValue(context);
                try
                {
                    cacheField.SetValue(context, clearCache ? null : new Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[]
                        { new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], []) });
                    Assert.That(header.IsCurrent, Is.False);
                    var writer = new TypeProviderWriter(context) { PreparedFieldTypes = prepared };
                    var file = writer.Write();
                    Assert.That(header.IsCurrent, Is.False);
                    Assert.That(CSharpSyntaxTree.ParseText(file.Content).GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
                    Assert.That(file.PreparedPocNameCount, Is.Zero);
                    if (clearCache) { Assert.That(header.CanUseCanonicalAttributes, Is.False, "Explicit cache clearing follows native fallback and changes native build provenance."); }
                    else { Assert.That(file.Content, Does.Contain("Obsolete"), "An explicitly replaced canonical cache is consumed without an implicit rebuild."); }
                }
                finally { cacheField.SetValue(context, original); }
            }, finalizePocHeaders: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanonicalAttributeSourceCallbackMutationUsesSingleNativeWriterPass(bool clearCache)
        {
            TypeProvider? context = null;
            PreparedPocHeaderFinalization.Header? header = null;
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>? replacement = null;
            bool armed = false;
            int mutations = 0;
            int writerCallbacks = 0;
            int replacementWrites = 0;
            WithRealFoo((unusedOwner, prepared) =>
            {
                context = prepared.SourceFiles.Single(f => f.FileOwner is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                header = prepared.FinalizedPocHeaders!.Get(context)!;
                Assert.That(header.Attributes, Is.Not.Null);
                Assert.That(header.IsCurrent, Is.True);
                var origin = context.PocNativeAttributeCacheOrigin;
                var writer = new TypeProviderWriter(context) { PreparedFieldTypes = prepared };
                armed = true;
                var file = writer.Write(); // Exactly one native writer invocation; never replay callbacks.
                armed = false;
                Assert.That(mutations, Is.EqualTo(1));
                Assert.That(writerCallbacks, Is.GreaterThan(0));
                Assert.That(replacementWrites, Is.EqualTo(clearCache ? 0 : 1), "An injected replacement operand must be written once, not replayed with source callbacks.");
                Assert.That(header.IsCurrent, Is.False, "Observed invalidation stays sticky after the writer completes.");
                if (clearCache)
                {
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.False);
                    Assert.That(file.Content, Does.Not.Contain("source callback replacement"));
                }
                else
                {
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, replacement), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, origin), Is.True);
                    Assert.That(file.Content, Does.Contain("source callback replacement"));
                }
            }, finalizePocHeaders: true, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                Assert.That(new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                    frame.GetMethod()?.DeclaringType == typeof(TypeProviderWriter)), Is.True,
                    "Mutation must occur inside the actual native writer, not during fixture preparation.");
                writerCallbacks++;
                if (mutations == 0)
                {
                    mutations++;
                    if (clearCache)
                    {
                        typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, null);
                    }
                    else
                    {
                        var attributes = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement[]
                        {
                            new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                                new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] { new FinalHeaderReviewReplacementWriteProbe(() => replacementWrites++) })
                        };
                        context!.Update(attributes: attributes);
                        replacement = context.CachedWriteAttributes;
                    }
                    Assert.That(header!.IsCurrent, Is.False);
                }
                return true;
            });
        }

        private sealed record FinalHeaderReviewReplacementWriteProbe(Action OnWrite) : Microsoft.TypeSpec.Generator.Expressions.ValueExpression
        {
            internal override void WriteCore(CodeWriter writer)
            {
                OnWrite();
                writer.AppendRaw("\"source callback replacement\"");
            }
        }

        [Test]
        public void ClearedCanonicalAttributeCacheCannotReuseOriginThroughPublicGetter()
        {
            WithRealFoo((unusedOwner, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var header = prepared.FinalizedPocHeaders!.Get(context)!;
                var origin = context.PocNativeAttributeCacheOrigin;
                Assert.That(header.CanUseCanonicalAttributes, Is.True);
                var cacheField = typeof(TypeProvider).GetField("_attributes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var cache = cacheField.GetValue(context);
                try
                {
                    cacheField.SetValue(context, null);
                    _ = context.Attributes;
                    Assert.That(context.CachedWriteAttributes, Is.Not.Null);
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.Null);
                    Assert.That(header.CanUseCanonicalAttributes, Is.False);
                    Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                    _ = new TypeProviderWriter(context) { PreparedFieldTypes = prepared }.Write();
                    Assert.That(context.PocNativeAttributeCacheOrigin, Is.Not.Null.And.Not.SameAs(origin));
                    Assert.That(header.IsCurrent, Is.False);
                }
                finally { cacheField.SetValue(context, cache); }
            }, finalizePocHeaders: true);
        }

        private sealed class ContextAttributeVisitor : LibraryVisitor
        {
            protected override TypeProvider? VisitType(TypeProvider type)
            {
                if (type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition)
                {
                    type.Update(attributes: [new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(new CSharpType(typeof(ObsoleteAttribute)), [], [])]);
                }
                return type;
            }
        }

        [Test]
        public void NativeFooContextAttributeRebuildHasEmptyContentControl()
        {
            WithRealFoo((_, prepared) =>
            {
                var context = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition).FileOwner;
                var fields = new[] { "_attributes", "_originalAttributes" }.Select(name => typeof(TypeProvider).GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!).ToArray();
                var original = fields.Select(field => field.GetValue(context)).ToArray();
                try
                {
                    // Native/native validation only, not pre-write planning.
                    var first = context.GetAttributesForWrite();
                    var second = context.GetAttributesForWrite();
                    Assert.That(first.Count, Is.Zero);
                    Assert.That(second.Count, Is.EqualTo(first.Count));
                    Assert.That(context.CachedWriteAttributes, Is.SameAs(second),
                        "CachedWriteAttributes exposes the cache even when native writing rebuilds it.");
                }
                finally
                {
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(context, original[i]); }
                }
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooContextAttributeCacheIsAvailableBeforeNamePlanning()
        {
            WithRealFoo((_, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => source.FileOwner is
                    Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelReaderWriterContextDefinition);
                var attributes = typeof(TypeProvider).GetField("_attributes",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(file.FileOwner);
                Assert.That(attributes, Is.Not.Null, "The native post-reference-map builder has already populated attributes.");
                Assert.That(((IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>)attributes!).Count, Is.Zero,
                    "The unchanged native/native control confirms this Foo fixture has no context attributes.");
                Assert.That(file.FileOwner.CachedWriteAttributes, Is.Not.Null,
                    "The pre-write cache is available; it is not by itself a contract for the later native rebuild.");
                Assert.That(file.HasStarted, Is.False);
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooPrecomputedPocCandidateDoesNotGrantTask03WriterPermission()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.SourceFiles.Count(), Is.GreaterThan(1),
                    "Task03's finite single-file admission cannot cover this generation.");
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null, "The ordinary-name proposal exists before the writer starts.");
                Assert.That(candidate!.SimpleSpelling, Is.EqualTo("PipelineMessage"));
                Assert.That(prepared.GetTypeOfNamePermission(file, declaration), Is.Null,
                    "A partial POC candidate must not bypass Task03's single-file permission contract.");
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                var syntax = CSharpSyntaxTree.ParseText(output.Content).GetRoot();
                var method = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Single(method => method.Identifier.ValueText == "Three" && method.ParameterList.Parameters.Single().Identifier.ValueText == "options");
                var local = method.Body!.Statements.OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(local.Declaration.Type.ToString(), Is.EqualTo(candidate.QualifiedSpelling),
                    "Diagnostic control only: the unchanged positive acceptance tests still require a future certified choice.");
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooFieldOnlyPassiveInventoryCannotAssumePipelineMessageWasResolved()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var declaration = source.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(source, declaration);
                Assert.That(candidate?.SimpleSpelling, Is.EqualTo("PipelineMessage"),
                    "Preserve the existing real Foo proposal while observing only already-cached fields.");
                var files = prepared.SourceFiles.ToArray();
                Assert.That(files.Length, Is.EqualTo(39));
                var uncached = files.Where(file => file.FileOwner.CachedFields is null).Select(file => file.FilePath).ToArray();
                Assert.That(uncached, Is.Empty, "An unmaterialized field carrier is unknown, never absence proof.");
                var matches = files.SelectMany(file => file.FileOwner.CachedFields!)
                    .Where(field => field.Type.IsFrameworkType && ReferenceEquals(field.Type.FrameworkType, typeof(PipelineMessage))).ToArray();
                Assert.That(matches, Is.Empty,
                    "The actual field-only Task03 loop cannot have resolved a PipelineMessage target when no current field uses it.");
                var usingAssignments = files.SelectMany(file => file.Roots.SelectMany(root => root.DescendantsAndSelf()))
                    .Where(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.AssignmentExpression
                    {
                        UseNullCoalesce: false,
                        Variable: Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression { IsUsing: true } local
                    } && local.Variable.Type.IsFrameworkType &&
                        ReferenceEquals(local.Variable.Type.FrameworkType, typeof(PipelineMessage))).ToArray();
                Assert.That(usingAssignments, Is.Not.Empty, "The actual source graph must include Foo-family using declarations.");
                var certified = usingAssignments.Count(node => node.SynchronousResourceTextAbsence is not null);
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var callEmissionKnown = (bool)typeof(PreparedFieldTypes).GetField("_callEmissionKnown", flags)!.GetValue(prepared)!;
                var rootStates = (System.Collections.IEnumerable)typeof(PreparedFieldTypes).GetField("_rootStates", flags)!.GetValue(prepared)!;
                var everyWritten = true;
                foreach (var state in rootStates)
                {
                    var recordType = state!.GetType();
                    var written = (bool)recordType.GetProperty("Written")!.GetValue(state)!;
                    var serializations = (bool[])recordType.GetProperty("SerializationWritten")!.GetValue(state)!;
                    everyWritten &= written && serializations.All(value => value);
                }
                var commentOnly = PreparedSourceFile.HasCommentOnlyLicenseHeader(CodeModelGenerator.Instance.LicenseHeader);
                var canResolve = callEmissionKnown && everyWritten && commentOnly;
                TestContext.Progress.WriteLine($"Cached Foo files={files.Length}; PipelineMessage fields={matches.Length}; using assignments={usingAssignments.Length}; retained synchronous-resource proofs={certified}; all-roots-written={everyWritten}; comment-only={commentOnly}; call-emission-known={callEmissionKnown}; resolver-enabled={canResolve}.");
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocOrdinaryCandidateUsesCanonicalInputsBeforeWriting(bool shadow)
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                var candidate = prepared.TryPreparePocOrdinaryCandidate(file, declaration);
                if (shadow)
                {
                    Assert.That(candidate, Is.Null, "A same-namespace source shadow prevents the POC bare-name candidate.");
                }
                else
                {
                    Assert.That(candidate, Is.Not.Null);
                    Assert.That(candidate!.File, Is.SameAs(file));
                    Assert.That(candidate.Occurrence, Is.SameAs(declaration));
                    Assert.That(candidate.SimpleSpelling, Is.EqualTo("PipelineMessage"));
                    Assert.That(candidate.QualifiedSpelling, Is.EqualTo("global::System.ClientModel.Primitives.PipelineMessage"));
                    Assert.That(candidate.SourceObservation.IsCurrent, Is.True);
                }
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
            }, addFooShadow: shadow, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPlanIsStoredBeforeAnyOutputWriter(bool shadow)
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (shadow) { Assert.That(candidate, Is.Null); }
                else
                {
                    Assert.That(candidate, Is.Not.Null);
                    Assert.That(candidate!.SimpleSpelling, Is.EqualTo("PipelineMessage"));
                    Assert.That(candidate.QualifiedSpelling, Is.EqualTo("global::System.ClientModel.Primitives.PipelineMessage"));
                }
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
            }, addFooShadow: shadow, precomputePoc: true);
        }

        [Test]
        public void RealFooPocCandidatePreparationDoesNotReplaySourceProofCallback()
        {
            var ordinaryCalls = 0;
            WithRealFoo((_, _) => { }, observeAnalyzerActivity: true, sourceCurrent: () => { ordinaryCalls++; return true; });
            var candidateCalls = 0;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
            }, precomputePoc: true, sourceCurrent: () => { candidateCalls++; return true; });
            TestContext.Progress.WriteLine($"Foo source proof callbacks: ordinary={ordinaryCalls}, candidate={candidateCalls}");
            Assert.That(candidateCalls, Is.EqualTo(ordinaryCalls), "Preparing the POC candidate must reuse the existing source-proof callback.");
        }

        [Test]
        public void RealFooPocPlanRejectsOccurrenceChangedByFinalSourceProofWithoutVetoingFields()
        {
            Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression? producer = null;
            var proofCalls = 0;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => ReferenceEquals(node.Producer, producer));
                Assert.That(proofCalls, Is.EqualTo(2), "Planning must reuse the two existing source-proof callbacks.");
                Assert.That(producer!.IsOut, Is.True, "The final source-proof callback must actually change the captured occurrence.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null);
                Assert.That(prepared.CanCommit(), Is.True, "A stale ordinary-type proposal must not veto an independent field decision.");
            }, precomputePoc: true, beforePreparation: owner =>
            {
                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                IEnumerable<object> Walk(object node)
                {
                    if (!visited.Add(node)) { yield break; }
                    yield return node;
                    foreach (var child in StructuredSourceShape.Capture(node, disableXmlDocs: true).Children)
                    foreach (var descendant in Walk(child.Producer)) { yield return descendant; }
                }
                producer = Walk(owner.Methods[0]).OfType<Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression>().Single();
            }, sourceCurrent: () =>
            {
                if (++proofCalls == 2) { producer!.Update(variable: null, isOut: true); }
                return true;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPlanRechecksReferenceMapRemovalMembership(bool changeDuringFinalProof)
        {
            var proofCalls = 0;
            string? competitorName = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (changeDuringFinalProof)
                {
                    Assert.That(candidate, Is.Null, "A newly written same-namespace competitor invalidates the captured lookup inventory.");
                    Assert.That(ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Add(competitorName!), Is.True);
                    Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                        "An observed removal-set change remains stale after restoration.");
                }
                else
                {
                    Assert.That(candidate, Is.Not.Null, "A removed competitor does not prevent the original Foo candidate.");
                }
            }, precomputePoc: true, addPocCompetitor: true, beforePreparation: owner =>
            {
                competitorName = owner.Type.Namespace + ".PipelineMessage";
                ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Add(competitorName);
            }, sourceCurrent: () =>
            {
                if (++proofCalls == 2 && changeDuringFinalProof)
                { Assert.That(ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(competitorName!), Is.True); }
                return true;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPlanRejectsChangedProviderStreamEvenAfterArrayRestoration(bool changeStream)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var roots = (TypeProvider[])output.TypeProviders;
                var original = roots.ToArray();
                var ownerIndex = Array.IndexOf(roots, owner);
                using var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                Assert.That(enumerator.MoveNext(), Is.True);
                var first = enumerator.Current;
                var firstIndex = Array.IndexOf(roots, first);
                var victimIndex = Enumerable.Range(firstIndex + 1, ownerIndex - firstIndex - 1)
                    .First(index => referenceMap.ShouldWriteProvider(roots[index]) && roots[index] is not ModelFactoryProvider);
                var victim = roots[victimIndex];
                var actual = new List<TypeProvider> { first };
                var restored = false;
                try
                {
                    if (changeStream) { roots[victimIndex] = first; }
                    while (enumerator.MoveNext())
                    {
                        actual.Add(enumerator.Current);
                        if (changeStream && !restored && ReferenceEquals(enumerator.Current, first))
                        { roots[victimIndex] = victim; restored = true; }
                    }
                }
                finally { roots[victimIndex] = victim; }
                Assert.That(roots, Is.EqualTo(original));
                if (changeStream)
                {
                    Assert.That(restored, Is.True);
                    Assert.That(actual.Count(provider => ReferenceEquals(provider, first)), Is.EqualTo(2));
                    Assert.That(actual.Contains(victim), Is.False);
                    Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                        "A restored root array cannot revive a proposal when the actual provider stream differed.");
                }
                else
                {
                    Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null,
                        "The unchanged provider stream must retain the precomputed Foo proposal.");
                }
            });
        }

        [Test]
        public void RealFooPocPlanRejectsIteratorDisposedAfterLastExpectedYield()
        {
            var expectedCount = 0;
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, _) =>
                expectedCount = GetWrittenProviderStream(output, referenceMap).Count());
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                using (var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(expectedCount, Is.GreaterThan(1));
                    for (var index = 0; index < expectedCount; index++) { Assert.That(enumerator.MoveNext(), Is.True); }
                    // Do not call MoveNext again: the iterator has not inspected
                    // any trailing roots and has not established exhaustion.
                }
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "Disposal after the last expected yield is not normal iterator exhaustion.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocWriteReceiptRejectsEnumerationBeforePlanAttachment(bool enumerateEarly)
        {
            object? phase = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, beforePocPreparation: (output, referenceMap) =>
            {
                // Diagnostic only: this constructs the private owner to isolate
                // witness ordering; production ownership still comes from CSharpGen.
                var phaseType = typeof(CSharpGen).GetNestedType("GenerationOwner", System.Reflection.BindingFlags.NonPublic)!;
                var ctor = phaseType.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Single();
                phase = ctor.Invoke([CodeModelGenerator.Instance, output, output.TypeProviders]);
                phaseType.GetMethod("Bind", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(phase, [referenceMap]);
                if (enumerateEarly) { _ = GetWrittenProviderStream(output, referenceMap).ToArray(); }
            }, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                try
                {
                    TestContext.Progress.WriteLine($"Reentry={enumerateEarly}, plan stream attached={prepared.HasPocProviderStream}");
                    foreach (var provider in GetWrittenProviderStream(output, referenceMap, phase))
                    {
                        var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                        prepared.BeginPocProviderWrite(provider, writer);
                        var file = writer.Write();
                        TestContext.Progress.WriteLine($"Reentry file {file.Name}: current={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                        prepared.EndPocProviderWrite(provider, writer, file);
                    }
                    Assert.That(prepared.IsObservedPocProviderWriteStreamComplete, Is.True);
                    Assert.That(referenceMap.IsObservedEnumerationRootCurrent(output, output.TypeProviders), Is.EqualTo(!enumerateEarly));
                    Assert.That(prepared.IsPocProviderWriteReceiptComplete, Is.EqualTo(!enumerateEarly),
                        "A privately owned iterator cannot repair a previously invalidated general enumeration witness.");
                }
                finally { ((IDisposable)phase!).Dispose(); }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsUnsafeLicenseHeaderBeforeWriting(bool unsafeHeader)
        {
            var header = unsafeHeader ? "safe\u2028using Poc.Unsafe;" : "safe\nsecond line";
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (unsafeHeader) { Assert.That(candidate, Is.Null, "A line terminator escaping the license comment must prevent the proposal."); }
                else { Assert.That(candidate, Is.Not.Null); }
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only license veto must not invalidate an independent field decision.");
            }, precomputePoc: true, licenseHeader: header);
        }

        [TestCase("warnings")]
        [TestCase("documentation")]
        public void RealFooPocHeaderCaptureObserversRequireIndependentCredits(string kind)
        {
            int Run(bool funded, int? credits = null)
            {
                var used = 0;
                WithRealFoo((owner, prepared) =>
                {
                    var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                    var occurrence = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                        .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                    Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.Not.Null);
                    Assert.That(prepared.GetPocHeaderObservation(file), Is.Null);
                    // Explicit native getter consumption is test setup, not
                    // preparation or replay of a source-proof callback.
                    var warnings = owner.DisabledFileWarnings;
                    var docs = owner.XmlDocs;
                    Assert.That(docs, Is.Not.Null);
                    var supplied = credits ?? (funded ? 65536 : 0);
                    var targetBudget = new PocWorkBudget(supplied);
                    prepared.ObservePocFileWarnings(owner, warnings, kind == "warnings" ? targetBudget : new PocWorkBudget(65536));
                    prepared.ObservePocFileDocumentation(owner, true, docs, kind == "documentation" ? targetBudget : new PocWorkBudget(65536));
                    used = supplied - targetBudget.Remaining;
                    if (funded)
                    {
                        Assert.That(prepared.GetPocHeaderObservation(file), Is.True,
                            "The exact native warning/documentation inputs must be supported before contrasting credits.");
                        Assert.That(used, Is.GreaterThan(4096), "Both surrounding bookkeeping and the entire existing capture allowance are prepaid.");
                    }
                    else
                    {
                        Assert.That(prepared.GetPocHeaderObservation(file), Is.Not.True,
                            "An unfunded observer cannot retain a certified warning/documentation capture.");
                        Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.Null);
                    }
                    Assert.That(prepared.CanCommit(), Is.True);
                }, precomputePoc: true);
                return used;
            }
            var required = Run(funded: true);
            Run(funded: false);
            Run(funded: true, credits: required);
            Run(funded: false, credits: required - 1);
            Run(funded: false, credits: 32);
        }

        [Test]
        public void RealFooPocHeaderQueryRequiresIndependentCreditsWithoutReplayingSourceProof()
        {
            var proofCalls = 0;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                Assert.That(prepared.GetPocHeaderObservation(file), Is.Null);
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                var beforeQuery = proofCalls;
                var full = new PocWorkBudget(65536);
                Assert.That(prepared.GetPocHeaderObservation(file, full), Is.True,
                    "The exact already-consumed header must be supported before contrasting credits.");
                Assert.That(prepared.GetPocHeaderObservation(file, new PocWorkBudget(0)), Is.False,
                    "An earlier native header write cannot fund current documentation/warning/probe work again.");
                var required = 65536 - full.Remaining;
                Assert.That(required, Is.GreaterThan(0));
                Assert.That(prepared.GetPocHeaderObservation(file, new PocWorkBudget(required)), Is.True);
                Assert.That(prepared.GetPocHeaderObservation(file, new PocWorkBudget(required - 1)), Is.False);
                Assert.That(proofCalls, Is.EqualTo(beforeQuery));
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true, sourceCurrent: () => { proofCalls++; return true; });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocHeaderGuardObservesCanonicalDocsWithoutHoisting(bool unsafeDocs)
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                if (!unsafeDocs) { Assert.That(owner.CachedXmlDocs, Is.Null, "Do not hoist Foo's normal type-documentation getter."); }
                Assert.That(prepared.GetPocHeaderObservation(file), Is.Null);
                var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                var output = writer.Write();
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(prepared.GetPocHeaderObservation(file), Is.EqualTo(!unsafeDocs),
                    "The preplanned veto must validate the exact canonical docs consumed by the ordinary writer.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only header veto must not invalidate an independent field decision.");
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                if (!unsafeDocs) { return; }
                var owner = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(provider => provider.Name == "Foo");
                var summary = new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("safe")])
                { StartTag = FormattableStringFactory.Create("<summary>\nusing Poc.Unsafe;") };
                owner.Update(xmlDocs: new XmlDocProvider(summary));
            });
        }

        [Test]
        public void RealFooPocHeaderGuardRejectsDocumentationChangedDuringSourceCallback()
        {
            TypeProvider? owner = null;
            XmlDocProvider? docs = null;
            Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((currentOwner, prepared) =>
            {
                owner = currentOwner;
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                Assert.That(owner.CachedXmlDocs, Is.Null);
                armed = true;
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                armed = false;
                Assert.That(changed && restored, Is.True, "The existing source-proof callback must change and restore the actual consumed documentation.");
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(prepared.GetPocHeaderObservation(file), Is.False,
                    "A captured header changed during an existing source callback must remain vetoed after restoration.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only header veto must not discard an independent field.");
            }, precomputePoc: true, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var inDocumentation = new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "WriteXmlDocsNoScope");
                if (!changed && inDocumentation && owner?.CachedXmlDocs is { Summary: { } summary } current)
                {
                    docs = current; original = summary;
                    var unsafeSummary = new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("safe")])
                    { StartTag = FormattableStringFactory.Create("<summary>\nusing Poc.Unsafe;") };
                    docs.Update(summary: unsafeSummary);
                    changed = true;
                }
                else if (changed && !restored && !inDocumentation)
                {
                    docs!.Update(summary: original!);
                    restored = true;
                }
                return true;
            });
        }

        [Test]
        public void RealFooPocHeaderGuardObservesDirectSnapshotCallbacks()
        {
            TypeProvider? owner = null;
            XmlDocProvider? docs = null;
            Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((currentOwner, prepared) =>
            {
                owner = currentOwner;
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                armed = true;
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                armed = false;
                Assert.That(changed && restored, Is.True, "The two existing direct snapshot callbacks must change and restore the captured docs.");
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(prepared.GetPocHeaderObservation(file), Is.False,
                    "A captured header observed changed by the first direct snapshot callback remains stale after the second restores it.");
                Assert.That(prepared.CanCommit(), Is.True, "POC-only header staleness must not veto an independent field.");
            }, precomputePoc: true, sourceCurrent: () =>
            {
                if (!armed || owner?.CachedXmlDocs is not { Summary: { } summary } current) { return true; }
                var frames = new System.Diagnostics.StackTrace().GetFrames();
                if (!frames.Any(frame => frame.GetMethod()?.Name == "WriteType") ||
                    frames.Any(frame => frame.GetMethod()?.Name == "WriteXmlDocsNoScope") ||
                    !frames.Any(frame => frame.GetMethod()?.Name == "ObserveSnapshot")) { return true; }
                if (!changed && frames.Any(frame => frame.GetMethod()?.Name == "ObserveSourceSnapshot"))
                {
                    docs = current; original = summary;
                    docs.Update(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("Changed")]));
                    changed = true;
                }
                else if (changed && !restored && frames.Any(frame => frame.GetMethod()?.Name == "CheckSnapshotCurrent"))
                {
                    docs!.Update(summary: original!);
                    restored = true;
                }
                return true;
            });
        }

        [Test]
        public void RealFooPocHeaderGuardKeepsObservedDocumentationStalenessSticky()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(prepared.GetPocHeaderObservation(file), Is.True);
                var docs = owner.CachedXmlDocs!;
                var original = docs.Summary!;
                docs.Update(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("Changed")]));
                Assert.That(prepared.GetPocHeaderObservation(file), Is.False);
                docs.Update(summary: original);
                Assert.That(prepared.GetPocHeaderObservation(file), Is.False, "An observed header change remains stale after restoration.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null);
                Assert.That(prepared.CanCommit(), Is.True, "POC-only header staleness must not veto an independent field.");
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocObservesPhysicalImportsWithoutReplayingWriter(bool conflictingImport)
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.ForbiddenImports, Does.Contain("Poc.PreparedImports"));
                var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                var output = writer.Write();
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                var field = typeof(PreparedFieldTypes).GetField("_bodyImports", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var allImports = (Dictionary<TypeProvider, HashSet<string>>)field.GetValue(prepared)!;
                Assert.That(allImports.TryGetValue(owner, out var imports), Is.True,
                    "The actual standard writer must record its physical imports for the precomputed candidate file.");
                Assert.That(imports!, Does.Contain("System.ClientModel.Primitives"));
                Assert.That(imports!.Contains("Poc.PreparedImports"), Is.EqualTo(conflictingImport));
                Assert.That(candidate.ForbiddenImports.Overlaps(imports), Is.EqualTo(conflictingImport));
                Assert.That(prepared.HasPocObservedImportConflict(candidate), Is.EqualTo(conflictingImport));
                Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate), Is.False,
                    "A standalone writer cannot supply the generation-owned candidate-file receipt.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only import conflict must not veto an independent field decision.");
            }, precomputePoc: true, addPocImportReference: true, additionalVisitor: conflictingImport ? new PocImportVisitor() : null);
        }

        [Test]
        public void RealFooPocObservesConflictingImportFromLaterMethod()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                var lateMethod = owner.Methods.Single(method => method.Signature.Name == "PocLateImport");
                var lateIndex = owner.Methods.ToList().IndexOf(lateMethod);
                var lateRoot = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == lateIndex);
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                var receipt = output.SourceConsumption;
                Assert.That(receipt?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(receipt!.Consumed.IndexOf(declaration.Id), Is.LessThan(receipt.Consumed.IndexOf(lateRoot.Id)),
                    "The competing namespace must first be encountered in a later method.");
                Assert.That(prepared.HasPocObservedImportConflict(candidate!), Is.True,
                    "A physical import emitted after the selected occurrence must still veto it.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only late import must not veto an independent field decision.");
            }, precomputePoc: true, addPocImportReference: true, additionalVisitor: new PocImportVisitor(late: true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooRestPocFileReceiptRequiresIndependentCreditsAndOwnsMismatch(bool mutateBody)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                Assert.That(rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Any(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence)?.Type.Name == "PipelineMessage"), Is.True);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                var full = new PocWorkBudget(65536);
                Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, full), Is.True,
                    "A real supported candidate-file write receipt must precede the resource/ownership contrast.");
                if (!mutateBody)
                {
                    Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, new PocWorkBudget(0)), Is.False,
                        "Earlier capture and writing do not fund this full-source receipt query again.");
                    var required = 65536 - full.Remaining;
                    Assert.That(required, Is.GreaterThan(0));
                    Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, new PocWorkBudget(required)), Is.True);
                    var oneShort = new PocWorkBudget(required - 1);
                    Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, oneShort), Is.False);
                    Assert.That(oneShort.Exhausted, Is.True);
                    Assert.That(rest.IsCurrent(), Is.True);
                    Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, new PocWorkBudget(65536)), Is.True,
                        "An exhausted POC receipt query must not invalidate an independently funded query.");
                    return;
                }
                var block = rest.Roots.Select(root => (root.Producer as MethodProvider)?.BodyStatements)
                    .OfType<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatements>()
                    .First(body => body.Statements.Count > 0 && (body.Statements is Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[] ||
                        body.Statements.GetType() == typeof(List<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>)));
                var statements = (IList<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement>)block.Statements;
                var original = statements[0];
                statements[0] = new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(
                    new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("return", new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(null)));
                try
                {
                    Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, new PocWorkBudget(65536)), Is.False);
                }
                finally { statements[0] = original; }
                Assert.That(rest.IsCurrent(), Is.True, "A POC-only receipt mismatch must not stale the shared source graph after actual carrier restoration.");
                Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest, new PocWorkBudget(65536)), Is.False,
                    "The actual mismatch remains sticky only in the POC receipt channel.");
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [TestCase("conflict")]
        [TestCase("receipt")]
        public void RealFooRestPocImportQueriesRequireIndependentCredits(string query)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                var candidate = rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Select(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence))
                    .First(choice => choice?.Type.Name == "PipelineMessage");
                Assert.That(candidate, Is.Not.Null);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest), Is.True);
                if (query == "conflict")
                {
                    var full = new PocWorkBudget(65536);
                    Assert.That(prepared.HasPocObservedImportConflict(candidate!, full), Is.False,
                        "The real writer import set must first be known nonconflicting with full credits.");
                    Assert.That(prepared.HasPocObservedImportConflict(candidate!, new PocWorkBudget(0)), Is.True,
                        "An unfunded import-set scan cannot certify absence of a conflict.");
                    var required = 65536 - full.Remaining;
                    Assert.That(required, Is.GreaterThan(0));
                    Assert.That(prepared.HasPocObservedImportConflict(candidate!, new PocWorkBudget(required)), Is.False);
                    Assert.That(prepared.HasPocObservedImportConflict(candidate!, new PocWorkBudget(required - 1)), Is.True);
                }
                else
                {
                    var full = new PocWorkBudget(65536);
                    Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate!, full), Is.True,
                        "A genuine candidate occurrence/import receipt must precede the credit contrast.");
                    Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate!, new PocWorkBudget(0)), Is.False,
                        "Earlier file checks cannot fund candidate-id/consumed-id/import checks again.");
                    var required = 65536 - full.Remaining;
                    Assert.That(required, Is.GreaterThan(0));
                    Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate!, new PocWorkBudget(required)), Is.True);
                    Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate!, new PocWorkBudget(required - 1)), Is.False);
                }
                Assert.That(rest.IsCurrent(), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooRestPocImportReceiptRejectsConflictingPhysicalImport(bool conflictingImport)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, addPocImportReference: true,
                additionalVisitor: conflictingImport ? new PocImportVisitor(rest: true) : null,
                inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                var candidate = rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Select(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence))
                    .First(choice => choice?.Type.Name == "PipelineMessage");
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.ForbiddenImports, Does.Contain("Poc.PreparedImports"));
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest), Is.True);
                Assert.That(prepared.HasPocObservedImportConflict(candidate), Is.EqualTo(conflictingImport));
                Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate), Is.EqualTo(!conflictingImport),
                    "The actual physical import must veto only the affected candidate's eventual use.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only conflict must not veto an independent field decision.");
                Assert.That(prepared.IsPocProviderWriteReceiptComplete, Is.False,
                    "An isolated fixture still cannot claim the private production generation-owner phase.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooRestPocImportReceiptRequiresCandidateOccurrence(bool omitOccurrence)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                var candidate = rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Select(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence))
                    .First(choice => choice?.Type.Name == "PipelineMessage");
                Assert.That(candidate, Is.Not.Null);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    if (omitOccurrence && ReferenceEquals(provider, rest.FileOwner))
                    {
                        var receipt = file.SourceConsumption!;
                        Assert.That(receipt.WasCurrentAtEndOfWrite, Is.True);
                        Assert.That(receipt.Consumed.Contains(candidate!.Occurrence.Id), Is.True);
                        var incomplete = receipt with { Consumed = receipt.Consumed.Where(id => id != candidate.Occurrence.Id).ToImmutableArray() };
                        file = new CodeFile(file.Content, file.Name, sourceConsumption: incomplete);
                    }
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                Assert.That(prepared.IsPocCandidateFileWriteReceiptCurrent(rest), Is.True);
                Assert.That(prepared.IsPocCandidateImportReceiptCurrent(candidate!), Is.EqualTo(!omitOccurrence),
                    "A file receipt that never consumed the selected occurrence cannot validate its import use.");
            });
        }

        [Test]
        public void RealFooPocReportsCandidateFileInventoryWithoutWriting()
        {
            WithRealFoo((_, prepared) =>
            {
                var candidateFiles = prepared.SourceFiles.Where(file => file.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Any(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence) is not null)).ToArray();
                foreach (var file in candidateFiles) { TestContext.Progress.WriteLine($"POC candidate file: {file.FilePath}"); }
                Assert.That(candidateFiles.Any(file => file.FilePath.EndsWith("/Foo.cs", StringComparison.Ordinal)), Is.True);
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocReceiptRejectsWrongOwnerWriterEvenWithSamePath(bool wrongOwner)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, addPocUnrelatedRoot: true,
                beforePocPreparation: (output, _) =>
                {
                    var unrelated = output.TypeProviders.OfType<PocReceiptProvider>().Single();
                    ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(unrelated.Type));
                }, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var unrelated = output.TypeProviders.OfType<PocReceiptProvider>().Single();
                var sawUnrelated = false;
                Assert.That(prepared.HasPocProviderStream, Is.True);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var actual = wrongOwner && ReferenceEquals(provider, unrelated)
                        ? new PocReceiptProvider("DifferentProvider", unrelated.Type.Namespace, unrelated.CachedRelativeFilePath!) : provider;
                    var writer = new TypeProviderWriter(actual) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    if (ReferenceEquals(provider, unrelated))
                    {
                        sawUnrelated = true;
                        Assert.That(file.Name, Is.EqualTo(unrelated.CachedRelativeFilePath));
                    }
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                Assert.That(sawUnrelated, Is.True, "The control must actually write the targeted provider.");
                Assert.That(prepared.IsObservedPocProviderWriteStreamComplete, Is.EqualTo(!wrongOwner),
                    "A writer for a different provider cannot satisfy the expected provider receipt even at the same path.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsFilteredSerializationParentChangedBeforeParentDecision(bool changeParent)
        {
            PocDecisionParent? parent = null;
            PocDecisionSerialization? serialization = null;
            PocDecisionMutator? mutator = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(mutator!.FiredDuringPreparation, Is.True);
                Assert.That(ProviderReferenceMapAnalyzer.ShouldWriteProvider(serialization!), Is.EqualTo(changeParent));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (changeParent) { Assert.That(candidate, Is.Null, "A filtered serialization's declaring identity changed after its write decision."); }
                else { Assert.That(candidate, Is.Not.Null); }
            }, precomputePoc: true, addPocParentDecisionRoots: true, beforePocPreparation: (output, _) =>
            {
                parent = output.TypeProviders.OfType<PocDecisionParent>().Single();
                serialization = output.TypeProviders.OfType<PocDecisionContainer>().Single().SerializationProviders.OfType<PocDecisionSerialization>().Single();
                mutator = output.TypeProviders.OfType<PocDecisionMutator>().Single();
                var foo = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(provider => provider.Name == "Foo");
                var removed = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
                removed.Add(ProviderReferenceMapAnalyzer.GetProviderTypeName(serialization.Type));
                removed.Add(ProviderReferenceMapAnalyzer.GetProviderTypeName(parent.Type));
                removed.Add(foo.Type.Namespace + ".PocDecisionParent");
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(mutator.Type));
                var container = output.TypeProviders.OfType<PocDecisionContainer>().Single();
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(container.Type));
                mutator.Reset();
                Assert.That(mutator.Type, Is.Not.Null);
                mutator.Arm(changeParent);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsFactoryIdentityChangedBetweenPolicyAndMethodCount(bool changeNamespace)
        {
            PocReentrantModelFactory? factory = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(factory!.FiredDuringPreparation, Is.True);
                Assert.That(factory.Methods, Has.Count.EqualTo(1));
                Assert.That(ProviderReferenceMapAnalyzer.ShouldWriteProvider(factory), Is.EqualTo(!changeNamespace));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (changeNamespace) { Assert.That(candidate, Is.Null, "Factory identity changed after its policy decision but before its method-count decision."); }
                else { Assert.That(candidate, Is.Not.Null); }
            }, precomputePoc: true, addPocReentrantFactory: true, beforePocPreparation: (output, _) =>
            {
                factory = output.TypeProviders.OfType<PocReentrantModelFactory>().Single();
                var foo = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(provider => provider.Name == "Foo");
                var removed = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(factory.Type));
                removed.Add(foo.Type.Namespace + ".PocReentrantFactory");
                factory.Reset();
                Assert.That(factory.Type, Is.Not.Null);
                factory.Arm(changeNamespace);
            });
        }

        [Test]
        public void RealFooPocRejectsPreviouslyEmptyFactoryBecomingWritable()
        {
            PocEmptyModelFactory? factory = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, addPocEmptyFactory: true,
                beforePocPreparation: (output, _) =>
            {
                factory = output.TypeProviders.OfType<PocEmptyModelFactory>().Single();
                factory.Update(methods: []);
                ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(factory.Type));
            }, inspectGeneration: (_, referenceMap, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(referenceMap.ShouldWriteProvider(factory!), Is.True);
                Assert.That(factory!.Methods, Is.Empty);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var signature = new MethodSignature("Create", null, MethodSignatureModifiers.Public | MethodSignatureModifiers.Static, null, null, []);
                factory.Update(methods: [new MethodProvider(signature, Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement.Empty, factory)]);
                Assert.That(factory.Methods, Has.Count.EqualTo(1));
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "A factory excluded only because it was empty cannot later become writable without invalidating the proposal.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsFilteredProviderBecomingWritable(bool serialization)
        {
            PocOrderingCompetitor? competitor = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, addPocFilteredRoot: !serialization,
                beforePocPreparation: (output, _) =>
            {
                if (serialization)
                {
                    competitor = new PocOrderingCompetitor();
                    competitor.EnsureBuilt();
                    var parent = output.TypeProviders.Single(provider => provider.GetType().Name == "ChangeTrackingDictionaryDefinition");
                    parent.Update(serializations: parent.SerializationProviders.Append(competitor).ToArray());
                }
                else { competitor = output.TypeProviders.OfType<PocOrderingCompetitor>().Single(); }
                ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Add(ProviderReferenceMapAnalyzer.GetProviderTypeName(competitor.Type));
            }, inspectGeneration: (_, referenceMap, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                Assert.That(referenceMap.ShouldWriteProvider(competitor!), Is.False);
                var original = competitor!.Type.Namespace;
                competitor.Type.Namespace = owner.Type.Namespace;
                Assert.That(referenceMap.ShouldWriteProvider(competitor), Is.True, "The filtered competitor must actually become writable.");
                var changed = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                competitor.Type.Namespace = original;
                Assert.That(changed, Is.Null, "A previously filtered competitor cannot become writable without invalidating the proposal.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "The observed identity change remains stale after restoration.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsFilteredProviderChangedDuringPreparation(bool changeNamespace)
        {
            PocOrderingCompetitor? competitor = null;
            PocOrderingOwner? callbackOwner = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(callbackOwner!.FiredDuringDeclarationScope, Is.True);
                Assert.That(ProviderReferenceMapAnalyzer.ShouldWriteProvider(competitor!), Is.EqualTo(changeNamespace));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (changeNamespace) { Assert.That(candidate, Is.Null, "A skipped provider became writable after the captured root decision."); }
                else { Assert.That(candidate, Is.Not.Null); }
            }, precomputePoc: true, addPocOrderingRoots: true, beforePocPreparation: (output, _) =>
            {
                competitor = output.TypeProviders.OfType<PocOrderingCompetitor>().Single();
                callbackOwner = output.TypeProviders.OfType<PocOrderingOwner>().Single();
                var removed = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
                removed.Add(ProviderReferenceMapAnalyzer.GetProviderTypeName(competitor.Type));
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(callbackOwner.Type));
                callbackOwner.Arm(changeNamespace);
            });
        }

        [Test]
        public void RealFooPocRelatedScopeObservedStalenessSurvivesCollectionRestoration()
        {
            IList<MethodProvider>? methods = null;
            MethodProvider? original = null;
            MethodProvider? replacement = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var index = methods!.Count - 1;
                methods[index] = replacement!;
                Assert.That(file.IsCurrent(), Is.True, "Only the sibling's retained collection changes.");
                var changed = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                methods[index] = original!;
                Assert.That(changed, Is.Null);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "Once observed, related-scope divergence remains stale after same-content restoration.");
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                    "The diagnostic on-demand path must not recreate a candidate for a sticky-invalidated file.");
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                var foo = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(provider => provider.Name == "Foo");
                var rest = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider>()
                    .Single(provider => provider.Type.Namespace == foo.Type.Namespace && provider.Type.Name == foo.Type.Name);
                var originalSignature = new MethodSignature("PocUnreferenced", null, MethodSignatureModifiers.Private, null, null, []);
                original = new MethodProvider(originalSignature, Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement.Empty, rest);
                var changedSignature = new MethodSignature("PipelineMessage", null, MethodSignatureModifiers.Private, null, null, []);
                replacement = new MethodProvider(changedSignature, Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement.Empty, rest);
                rest.Update(methods: rest.Methods.Append(original).ToArray());
                methods = rest.CachedMethods as IList<MethodProvider>;
                Assert.That(methods, Is.Not.Null, "The control requires the actual retained, indexable method collection.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsUnobservableDeclaringProvider(bool changeDeclaring)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, addPocDeclaringRoot: true, beforePocPreparation: (output, _) =>
            {
                var provider = output.TypeProviders.OfType<PocMutableDeclaringProvider>().Single();
                ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(provider.Type));
            }, inspectGeneration: (output, _, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var provider = output.TypeProviders.OfType<PocMutableDeclaringProvider>().Single();
                var calls = provider.DeclaringGetterCalls;
                if (changeDeclaring) { provider.ChangeDeclaring(owner); }
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "A null-returning unknown declaring-provider getter cannot supply a stable namespace inventory.");
                Assert.That(provider.DeclaringGetterCalls, Is.EqualTo(calls), "Currentness must not replay the virtual getter.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsNamespaceIndexStaleDuringPreparation(bool changeNamespace)
        {
            PocOrderingCompetitor? competitor = null;
            PocOrderingOwner? callbackOwner = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(callbackOwner!.FiredDuringDeclarationScope, Is.True,
                    "The callback must run during owner scope capture, after the generated namespace index was built.");
                Assert.That(competitor!.Type.Namespace, Is.EqualTo(changeNamespace ? owner.Type.Namespace : "Poc.Ordering.Initial"));
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                if (changeNamespace)
                {
                    Assert.That(candidate, Is.Null, "A competitor moved into Foo's namespace cannot be absent from the lookup index.");
                }
                else { Assert.That(candidate, Is.Not.Null); }
            }, precomputePoc: true, addPocOrderingRoots: true, beforePocPreparation: (output, _) =>
            {
                competitor = output.TypeProviders.OfType<PocOrderingCompetitor>().Single();
                callbackOwner = output.TypeProviders.OfType<PocOrderingOwner>().Single();
                var removed = ProviderReferenceMapAnalyzer.LatestResult!.RemoveCandidates;
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(competitor.Type));
                removed.Remove(ProviderReferenceMapAnalyzer.GetProviderTypeName(callbackOwner.Type));
                callbackOwner.Arm(changeNamespace);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocRejectsChangedSerializationMembershipBeforeEnumeration(bool addCompetitor)
        {
            List<TypeProvider>? serializations = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                var unrelated = output.TypeProviders.Single(provider => provider.GetType().Name == "ChangeTrackingDictionaryDefinition");
                serializations = unrelated.SerializationProviders.ToList();
                unrelated.Update(serializations: serializations);
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var competitor = new PocPipelineMessageCompetitor(owner.Type.Namespace);
                if (addCompetitor) { serializations!.Add(competitor); }
                var candidateBeforeEnumeration = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                var actualStream = GetWrittenProviderStream(output, referenceMap).ToArray();
                Assert.That(actualStream.Contains(competitor), Is.EqualTo(addCompetitor));
                if (addCompetitor)
                {
                    Assert.That(candidateBeforeEnumeration, Is.Null,
                        "A new writable serialization provider must invalidate the captured generated-name inventory before enumeration.");
                    serializations!.Remove(competitor);
                    Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null);
                }
                else { Assert.That(candidateBeforeEnumeration, Is.Not.Null); }
            });
        }

        [Test]
        public void RealFooPocRejectsChangedRelatedMethodSignatureAndRestore()
        {
            MethodSignature? independentSignature = null;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                var signature = independentSignature!;
                var original = signature.Name;
                Assert.That(original, Is.Not.EqualTo("PipelineMessage"));
                signature.Update(name: "PipelineMessage");
                Assert.That(file.IsCurrent(), Is.True, "The control must mutate an independent sibling signature, not Foo's own occurrence graph.");
                var changed = prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration);
                signature.Update(name: original);
                Assert.That(changed, Is.Null, "A changed sibling method name cannot reuse the captured lexical scope inventory.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, declaration), Is.Null,
                    "A signature revision observed stale cannot be revived by restoring its name.");
                Assert.That(candidate!.IsCurrent, Is.False);
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                var foo = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>().Single(provider => provider.Name == "Foo");
                var rest = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider>()
                    .Single(provider => provider.Type.Namespace == foo.Type.Namespace && provider.Type.Name == foo.Type.Name);
                independentSignature = new MethodSignature("PocIndependentMethod", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(independentSignature, Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement.Empty, rest);
                rest.Update(methods: rest.Methods.Append(method).ToArray());
            });
        }

        [Test]
        public void RealFooPocInputObservationRequiresIndependentWorkCredits()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var occurrence = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.Not.Null,
                    "The actual finalized Foo inventory/proposal must be supported before the zero-credit contrast.");
                Assert.That(prepared.CanCommit(), Is.True);
                var observe = typeof(PreparedFieldTypes).GetMethod("PocObservedInputsAreCurrent",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var budget = new PocWorkBudget(262144);
                Assert.That(observe.Invoke(prepared, new object[] { budget }), Is.True);
                Assert.That(budget.Remaining, Is.LessThan(262144));
                Assert.That(observe.Invoke(prepared, new object[] { new PocWorkBudget(0) }), Is.False,
                    "Stored namespace facts and a previous observation do not fund root/name/removal/receipt scans again.");
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(file, occurrence), Is.Null,
                    "The enclosing POC observation owns a sticky veto after an exhausted query.");
                Assert.That(prepared.CanCommit(), Is.True, "POC query exhaustion must not revoke independent Task03 choices.");
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocRejectsUnrelatedNamespaceMutationAndRestore()
        {
            WithRealFoo((owner, prepared) =>
            {
                var fooFile = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var fooOccurrence = fooFile.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence);
                Assert.That(candidate, Is.Not.Null);
                var unrelated = prepared.SourceFiles.Select(file => file.FileOwner)
                    .Single(provider => provider.GetType().Name == "ChangeTrackingDictionaryDefinition");
                var type = unrelated.CachedType!;
                var original = type.Namespace;
                type.Namespace = "PocChangedNamespace";
                type.Namespace = original;
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence), Is.Null,
                    "A generated type's observed identity revision cannot be hidden by restoring its namespace.");
                Assert.That(candidate!.IsCurrent, Is.False);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocUnrelatedDictionaryWriterDoesNotInvalidateFooProposal()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var fooFile = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var fooOccurrence = fooFile.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence), Is.Not.Null);
                var sawDictionary = false;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var originalType = provider.CachedType;
                    var originalName = originalType?.Name;
                    var originalNamespace = originalType?.Namespace;
                    var originalArity = originalType?.Arguments.Count;
                    var originalSerializations = provider.CachedSerializationProviders?.ToArray();
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (provider.GetType().Name == "ChangeTrackingDictionaryDefinition")
                    {
                        sawDictionary = true;
                        Assert.That(provider.CachedType, Is.SameAs(originalType));
                        Assert.That(provider.CachedType!.Name, Is.EqualTo(originalName));
                        Assert.That(provider.CachedType.Namespace, Is.EqualTo(originalNamespace));
                        Assert.That(provider.CachedType.Arguments.Count, Is.EqualTo(originalArity));
                        Assert.That(provider.CachedSerializationProviders, Is.EqualTo(originalSerializations));
                        Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence), Is.Not.Null,
                            "An unrelated standard writer must not discard Foo's unchanged namespace proposal.");
                        break;
                    }
                }
                Assert.That(sawDictionary, Is.True);
            });
        }

        [Test]
        public void RealFooPocSiblingMutationReportsSharedSourceInvalidation()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var fooFile = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                var restOccurrence = rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .First(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence) is not null);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    if (ReferenceEquals(provider, rest.FileOwner))
                    { ((Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression)restOccurrence.Producer).Update(variable: null, isOut: true); }
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                }
                var sourceDisposed = (bool)typeof(PreparedFieldTypes).GetField("_sourceDisposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared)!;
                var scopeField = typeof(PreparedFieldTypes).GetField("_callLookupScopes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var scopes = (PreparedSourceFile.OwnerSnapshot[])scopeField.GetValue(prepared)!;
                var ownerField = typeof(PreparedSourceFile.OwnerSnapshot).GetField("_owner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var staleOwners = scopes.Where(scope => !scope.IsCurrent()).Select(scope => ((TypeProvider)ownerField.GetValue(scope)!).GetType().Name).ToArray();
                var observed = (bool)typeof(PreparedFieldTypes).GetMethod("PocObservedInputsAreCurrent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(prepared, null)!;
                TestContext.Progress.WriteLine($"Sibling mutation: rest invalid={rest.IsInvalid}, Foo current={fooFile.IsCurrent()}, shared source disposed={sourceDisposed}, observed={observed}, stale owners={string.Join(",", staleOwners)}");
                Assert.That(rest.IsInvalid, Is.True);
            });
        }

        [Test]
        public void RealFooPocInvalidOtherCandidateFileDoesNotInvalidateFoo()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var fooFile = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var fooOccurrence = fooFile.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var rest = prepared.SourceFiles.Single(source => source.FilePath.EndsWith("/Foo.RestClient.cs", StringComparison.Ordinal));
                var restOccurrence = rest.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .First(occurrence => prepared.GetPrecomputedPocOrdinaryCandidate(rest, occurrence) is not null);
                var fooCandidate = prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence);
                Assert.That(fooCandidate, Is.Not.Null);
                var sawRest = false;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    if (ReferenceEquals(provider, rest.FileOwner))
                    { ((Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression)restOccurrence.Producer).Update(variable: null, isOut: true); }
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (ReferenceEquals(provider, owner) || ReferenceEquals(provider, rest.FileOwner))
                    {
                        object? State(string name) => typeof(PreparedFieldTypes).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared);
                        var scopes = (PreparedSourceFile.OwnerSnapshot[])State("_callLookupScopes")!;
                        var ownerField = typeof(PreparedSourceFile.OwnerSnapshot).GetField("_owner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        var staleOwners = scopes.Where(scope => !scope.IsCurrent()).Select(scope => ((TypeProvider)ownerField.GetValue(scope)!).GetType().Name).ToArray();
                        TestContext.Progress.WriteLine($"Isolation at {file.Name}: pocStale={State("_pocCandidateStale")}, valid={State("_valid")}, sourceDisposed={State("_sourceDisposed")}, fooCurrent={fooFile.IsCurrent()}, restInvalid={rest.IsInvalid}, staleOwners={string.Join(",", staleOwners)}");
                    }
                    if (ReferenceEquals(provider, rest.FileOwner))
                    {
                        sawRest = true;
                        Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False,
                            "The control must actually invalidate the sibling file's traversal.");
                        var retainedFoo = prepared.GetPrecomputedPocOrdinaryCandidate(fooFile, fooOccurrence);
                        var pocStale = typeof(PreparedFieldTypes).GetField("_pocCandidateStale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared);
                        TestContext.Progress.WriteLine($"Foo post-check: retained={retainedFoo is not null}, pocStale={pocStale}, sourceCurrent={fooCandidate!.SourceObservation.IsCurrent}, typeRevision={fooCandidate.Type.IdentityVersion}/{fooCandidate.TypeRevision}");
                        Assert.That(retainedFoo, Is.Not.Null,
                            "A file-only failure must not discard an independently current Foo proposal.");
                        Assert.That(prepared.GetPrecomputedPocOrdinaryCandidate(rest, restOccurrence), Is.Null);
                        break;
                    }
                }
                Assert.That(sawRest, Is.True);
            });
        }

        [TestCase("complete")]
        [TestCase("abort")]
        [TestCase("extra-yield")]
        [TestCase("repeat")]
        [TestCase("dispose")]
        public void RealFooAllFileEnvelopeRequiresSuccessfulStreamLifetime(string operation)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var envelope = prepared.PocGenerationEnvelope!;
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    for (var index = 0; index < envelope.PlannedFileCount; index++)
                    {
                        Assert.That(stream.MoveNext(), Is.True);
                        var provider = stream.Current;
                        var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                        prepared.BeginPocProviderWrite(provider, writer);
                        var file = writer.Write();
                        prepared.EndPocProviderWrite(provider, writer, file);
                        Assert.That(envelope.GetFile(provider)!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Matched));
                    }
                    Assert.That(envelope.HasCompleteTraversalReceipts, Is.False,
                        "Receiving the last expected receipt precedes successful iterator exhaustion.");
                    if (operation == "dispose") { prepared.Dispose(); }
                    if (operation != "abort") { Assert.That(stream.MoveNext(), Is.False); }
                }
                if (operation == "extra-yield") { prepared.ObservePocProviderYield(owner); }
                if (operation == "repeat") { prepared.BeginPocProviderStream(); }
                Assert.That(envelope.HasCompleteTraversalReceipts, Is.EqualTo(operation == "complete"));
                Assert.That(envelope.HasUncoveredEmission, Is.True);
            });
        }

        [Test]
        public void RealFooAllFileEnvelopeRejectsYieldDuringActiveWriter()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var envelope = prepared.PocGenerationEnvelope!;
                Assert.That(envelope.PlannedFileCount, Is.EqualTo(2));
                using var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                Assert.That(stream.MoveNext(), Is.True);
                var first = stream.Current;
                var firstWriter = new TypeProviderWriter(first) { PreparedFieldTypes = prepared };
                prepared.BeginPocProviderWrite(first, firstWriter);
                var firstFile = firstWriter.Write();
                Assert.That(stream.MoveNext(), Is.True, "Simulate observed advancement before the first write boundary ends.");
                var second = stream.Current;
                prepared.EndPocProviderWrite(first, firstWriter, firstFile);
                var secondWriter = new TypeProviderWriter(second) { PreparedFieldTypes = prepared };
                prepared.BeginPocProviderWrite(second, secondWriter);
                var secondFile = secondWriter.Write();
                prepared.EndPocProviderWrite(second, secondWriter, secondFile);
                Assert.That(stream.MoveNext(), Is.False);
                Assert.That(envelope.HasCompleteTraversalReceipts, Is.False,
                    "Ordered receipt identities do not repair an observed overlapping stream/write lifetime.");
            });
        }

        [Test]
        public void RealFooDictionaryLazyConstraintIsAbsentFromPrewriteSourceReceipt()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (_, _, _, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FilePath.EndsWith("/ChangeTrackingDictionary.cs", StringComparison.Ordinal));
                var owner = source.FileOwner;
                Assert.That(owner.CachedWhereClause, Is.Null, "Do not force the constraint during preparation.");
                Assert.That(source.Roots.Any(root => root.Id.RootRole == "type-constraint"), Is.False);
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(owner.CachedWhereClause, Is.Not.Null, "The normal writer materializes the dictionary constraint.");
                Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False,
                    "The late canonical constraint has no pre-write source root; stream identity alone cannot certify this file.");
            });
        }

        [Test]
        public void RealFooDictionaryConstraintPlanPrecedesAndMatchesNativeMaterialization()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FilePath.EndsWith("/ChangeTrackingDictionary.cs", StringComparison.Ordinal));
                var plan = prepared.PocGenerationEnvelope!.GetFile(source.FileOwner)!.TypeConstraint;
                Assert.That(plan, Is.Not.Null);
                Assert.That(plan!.IsCurrent, Is.True);
                Assert.That(source.FileOwner.CachedWhereClause, Is.Null);
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                var sawDictionary = false;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, source.FileOwner)) { continue; }
                    sawDictionary = true;
                    Assert.That(plan.HasObservedExpectedEmission, Is.True,
                        "The exact fragment was planned before writing, not discovered from native output.");
                    Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False,
                        "This fragment contract must not silently change the original shared receipt or grant a file permission.");
                    break;
                }
                Assert.That(sawDictionary, Is.True);
            });
        }

        [Test]
        public void RealFooDictionaryHasIndependentPocConsumptionDespiteLazyConstraint()
        {
            var targetWrites = 0;
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => file.FilePath.EndsWith("/ChangeTrackingDictionary.cs", StringComparison.Ordinal));
                var plan = prepared.PocGenerationEnvelope!.GetFile(source.FileOwner)!;
                var consumed = plan.GetType().GetProperty("HasCompleteNativeConsumption", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(consumed, Is.Not.Null, "A POC file must have its own planned consumption path, not reinterpret the stale shared receipt.");
                Assert.That((bool)consumed!.GetValue(plan)!, Is.False);
                Assert.That(source.FileOwner.CachedWhereClause, Is.Null);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, source.FileOwner)) { continue; }
                    targetWrites++;
                    Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.False,
                        "Do not relax Task03's original source graph to admit the late constraint.");
                    Assert.That((bool)consumed.GetValue(plan)!, Is.True,
                        "The separately planned root/producer sequence must cover normal lazy constraint materialization.");
                    break;
                }
                Assert.That(targetWrites, Is.EqualTo(1), "The actual dictionary writer must be reached.");
            });
        }

        [Test]
        public void RealFooScaffoldingCarriersAreCapturableWithoutExecutingThem()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (_, _, _, prepared) =>
            {
                var unavailable = new System.Collections.Generic.List<string>();
                void Check<T>(System.Collections.Generic.IReadOnlyList<T>? carrier, string role)
                {
                    if (carrier is null || ContractCollections.TryCopyPocConstruction(carrier, new PocWorkBudget(4096), out _)) { return; }
                    var description = $"{role}: {carrier.GetType().FullName}, assembly={carrier.GetType().Assembly.GetName().Name}";
                    unavailable.Add(description);
                    TestContext.Progress.WriteLine("POC scaffolding carrier " + description);
                }
                foreach (var file in prepared.SourceFiles)
                {
                    Check(file.FileOwner.CachedWriteAttributes, file.FilePath + "/attributes");
                    if (prepared.PocGenerationEnvelope!.GetFile(file.FileOwner)!.NativeConsumptionFailure is { } failure)
                    {
                        unavailable.Add(file.FilePath + ": " + failure);
                        TestContext.Progress.WriteLine($"POC prewrite capture {file.FilePath}: {failure}");
                    }
                    foreach (var node in file.Roots.SelectMany(root => root.DescendantsAndSelf()))
                    {
                        if (node.Producer is not MethodSignatureBase signature || !node.IsOpaque) { continue; }
                        Check(signature.Attributes, file.FilePath + "/signature-attributes");
                        Check(signature.Parameters, file.FilePath + "/parameters");
                        if (signature is MethodSignature method)
                        {
                            Check(method.GenericArguments, file.FilePath + "/generic-arguments");
                            Check(method.GenericParameterConstraints, file.FilePath + "/constraints");
                        }
                        if (signature is ConstructorSignature constructor) { Check(constructor.Initializer?.Arguments, file.FilePath + "/initializer"); }
                    }
                }
                Assert.That(unavailable, Is.Empty, "Concrete unsupported carriers implicated by native root/producer mismatches.");
            });
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RealFooIndependentPocConsumptionChecksChildlessRootIdentity(bool property, bool replace)
        {
            var targetWrites = 0;
            System.Collections.Generic.IList<FieldProvider>? fields = null;
            System.Collections.Generic.IList<PropertyProvider>? properties = null;
            FieldProvider? replacementField = null;
            PropertyProvider? replacementProperty = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, beforePreparation: owner =>
            {
                if (property)
                {
                    var original = new PropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), "PocOriginalRoot", new AutoPropertyBody(true), owner);
                    replacementProperty = new PropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), "PocReplacementRoot", new AutoPropertyBody(true), owner);
                    // Exercise the permitted nullable-doc state so both roots
                    // have no instrumented child emissions to reveal identity.
                    typeof(PropertyProvider).GetProperty(nameof(PropertyProvider.XmlDocs))!.SetValue(original, null);
                    typeof(PropertyProvider).GetProperty(nameof(PropertyProvider.XmlDocs))!.SetValue(replacementProperty, null);
                    properties = new[] { original };
                    owner.Update(properties: properties);
                    properties = (System.Collections.Generic.IList<PropertyProvider>)owner.CachedProperties!;
                }
                else
                {
                    fields = new[] { new FieldProvider(FieldModifiers.Private, typeof(int), "PocOriginalRoot", owner) };
                    replacementField = new FieldProvider(FieldModifiers.Private, typeof(int), "PocReplacementRoot", owner);
                    owner.Update(fields: fields);
                    fields = (System.Collections.Generic.IList<FieldProvider>)owner.CachedFields!;
                }
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    if (ReferenceEquals(provider, owner) && replace)
                    {
                        if (property) { properties![0] = replacementProperty!; }
                        else { fields![0] = replacementField!; }
                    }
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, owner)) { continue; }
                    targetWrites++;
                    Assert.That(file.Content.Contains("PocReplacementRoot", StringComparison.Ordinal), Is.EqualTo(replace));
                    Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.EqualTo(!replace));
                    Assert.That(prepared.PocGenerationEnvelope!.GetFile(owner)!.HasCompleteNativeConsumption, Is.EqualTo(!replace),
                        "Owner/role/index and an empty child sequence must not substitute for the actual root producer identity.");
                    break;
                }
                Assert.That(targetWrites, Is.EqualTo(1), "The actual childless-root owner writer must be reached.");
            });
        }

        [TestCase("foreign-root")]
        [TestCase("duplicate-root")]
        [TestCase("early-complete")]
        [TestCase("repeat-start")]
        public void RealFooIndependentPocConsumptionRejectsObservedProtocolMisuse(string operation)
        {
            var targetWrites = 0;
            PreparedPocFileConsumption? consumption = null;
            PreparedPocGenerationEnvelope.FilePlan? filePlan = null;
            PreparedFieldTypes? preparation = null;
            TypeProviderWriter? activeWriter = null;
            TypeProvider? owner = null;
            var armed = false;
            var injected = false;
            var started = typeof(PreparedPocFileConsumption).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            WithRealFoo((_, _) => { }, precomputePoc: true, fooGroupOnly: true, sourceCurrent: () =>
            {
                if (!armed || injected || consumption is null || !(bool)started.GetValue(consumption)!) { return true; }
                injected = true;
                switch (operation)
                {
                    case "foreign-root":
                        using (consumption.BeginRoot(new PocReceiptProvider("Foreign", "Other", "Foreign.cs"), "type-documentation", 0)) { }
                        break;
                    case "duplicate-root":
                        using (consumption.BeginRoot(owner!, "type-documentation", 0)) { }
                        break;
                    case "early-complete": consumption.Complete(); break;
                    default: filePlan!.StartNativeConsumption(preparation!, activeWriter!); break;
                }
                return true;
            }, inspectGeneration: (output, referenceMap, foo, prepared) =>
            {
                owner = foo; preparation = prepared;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var plan = prepared.PocGenerationEnvelope!.GetFile(provider)!;
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    if (ReferenceEquals(provider, owner))
                    {
                        filePlan = plan; activeWriter = writer;
                        consumption = (PreparedPocFileConsumption?)plan.GetType().GetField("_native", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(plan);
                        armed = true;
                    }
                    var file = writer.Write();
                    armed = false;
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, owner)) { continue; }
                    targetWrites++;
                    Assert.That(injected, Is.True);
                    Assert.That(plan.HasCompleteNativeConsumption, Is.False);
                    Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True,
                        "A new POC cursor failure must not invalidate the existing shared traversal.");
                    Assert.That(prepared.CanCommit(), Is.True, "Existing independent fields remain selectable.");
                    break;
                }
                Assert.That(targetWrites, Is.EqualTo(1), "The protocol probe must reach its actual owner writer.");
            });
        }

        [Test]
        public void RealFooAllWrittenProvidersHaveIndependentPocConsumption()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var rejected = new System.Collections.Generic.List<string>();
                var count = 0;
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var plan = prepared.PocGenerationEnvelope!.GetFile(provider);
                    Assert.That(plan, Is.Not.Null);
                    Assert.That(plan!.HasCompleteNativeConsumption, Is.False);
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!plan.HasCompleteNativeConsumption)
                    {
                        rejected.Add(file.Name);
                        TestContext.Progress.WriteLine($"POC consumption mismatch {file.Name}: {plan.NativeConsumptionFailure}");
                    }
                    count++;
                }
                Assert.That(count, Is.EqualTo(39));
                Assert.That(rejected, Is.Empty, "Every actual written provider needs independent POC root/producer accounting: " + string.Join(", ", rejected));
                Assert.That(prepared.PocGenerationEnvelope!.HasUncoveredEmission, Is.True,
                    "Even complete traversal is not yet a whole-file grammar or name-permission certificate.");
            });
        }

        private sealed class UnknownHeaderInputGenerator(GeneratorContext context) : ScmCodeModelGenerator(context)
        {
            internal bool Probe { get; set; }
            internal int Calls { get; private set; }
            public override Microsoft.TypeSpec.Generator.Input.InputLibrary InputLibrary
            {
                get
                {
                    if (Probe) { Calls++; throw new InvalidOperationException("Unknown input getter must stay native."); }
                    return base.InputLibrary;
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeaderFinalizationCannotUseBaseCacheToInvokeUnknownInputGetter(bool unknownGetter)
        {
            WithRealFoo((_, _) =>
            {
                var original = CodeModelGenerator.Instance;
                var singleton = typeof(ScmCodeModelGenerator).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var originalScm = singleton.GetValue(null);
                try
                {
                    ScmCodeModelGenerator generator = unknownGetter
                        ? new UnknownHeaderInputGenerator(new GeneratorContext(original.Configuration))
                        : new ScmCodeModelGenerator(new GeneratorContext(original.Configuration));
                    CodeModelGenerator.Instance = generator;
                    var loaded = generator.InputLibrary.InputNamespace;
                    Assert.That(loaded.Models, Is.Empty);
                    typeof(Microsoft.TypeSpec.Generator.Input.InputNamespace).GetProperty(nameof(loaded.Models))!
                        .SetValue(loaded, Array.Empty<Microsoft.TypeSpec.Generator.Input.InputModelType>());
                    Assert.That(ContractCollections.TryCopyForPoc(loaded.Models, new PocWorkBudget(4096), out var models), Is.True);
                    Assert.That(models.IsEmpty, Is.True);
                    var provider = new Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelSerializationExtensionsDefinition();
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    typeof(TypeProvider).GetField("_type", flags)!.SetValue(provider, new CSharpType(typeof(object)));
                    typeof(TypeProvider).GetField("_declarationModifiers", flags)!.SetValue(provider, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
                    if (generator is UnknownHeaderInputGenerator probe) { probe.Probe = true; }
                    using var phase = new PreparedPocHeaderFinalization();
                    Assert.DoesNotThrow(() => phase.Materialize(provider, loaded));
                    if (generator is UnknownHeaderInputGenerator unknown)
                    {
                        Assert.That(unknown.Calls, Is.Zero);
                        Assert.That(phase.Count, Is.Zero);
                        Assert.That(provider.CachedDisabledFileWarnings, Is.Null);
                        Assert.Throws<InvalidOperationException>(() => _ = provider.DisabledFileWarnings);
                        Assert.That(unknown.Calls, Is.EqualTo(1), "The unsupported input remains at its original native getter point.");
                    }
                    else
                    {
                        Assert.That(phase.Count, Is.EqualTo(1), "The otherwise-identical known getter and supported empty carrier must be admitted.");
                        Assert.That(provider.CachedDisabledFileWarnings, Is.Not.Null);
                    }
                }
                finally
                {
                    CodeModelGenerator.Instance = original;
                    singleton.SetValue(null, originalScm);
                }
            });
        }

        private sealed class ThrowingCachedDescription : FormattableString
        {
            internal int Calls { get; private set; }
            public override string Format { get { Calls++; throw new InvalidOperationException("Unknown cached description format."); } }
            public override int ArgumentCount => 0;
            public override object? GetArgument(int index) => throw new InvalidOperationException();
            public override object[] GetArguments() => [];
            public override string ToString(IFormatProvider? provider) { Calls++; throw new InvalidOperationException("Unknown cached description formatting."); }
        }

        private static void AssertRawCachedDescriptionRejectedBeforeNormalization(FormattableString description)
        {
            WithRealFoo((owner, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var input = ((Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner).InputClient;
                var summary = input.Summary;
                var doc = input.Doc;
                var fields = new[] { "_description", "_xmlDocs", "_disabledFileWarnings" }.Select(field => typeof(TypeProvider).GetField(field, flags)!).ToArray();
                var original = fields.Select(field => field.GetValue(owner)).ToArray();
                try
                {
                    input.Update(summary: "safe", doc: "safe");
                    fields[0].SetValue(owner, description);
                    fields[1].SetValue(owner, null);
                    fields[2].SetValue(owner, Array.Empty<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement>());
                    var phase = new PreparedPocHeaderFinalization();
                    phase.Materialize(owner, null);
                    Assert.That(owner.CachedXmlDocs, Is.Null, "Raw cached text and expansion must be bounded before the inherited normalizer executes.");
                    Assert.That(owner.CachedDescription, Is.SameAs(description));
                    Assert.That(phase.Count, Is.Zero);
                }
                finally
                {
                    input.GetType().GetProperty("Summary")!.SetValue(input, summary);
                    input.GetType().GetProperty("Doc")!.SetValue(input, doc);
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(owner, original[i]); }
                }
            });
        }

        [Test]
        public void CachedDescriptionMalformedTagIsRejectedBeforeNativeNormalizer()
            => AssertRawCachedDescriptionRejectedBeforeNormalization(FormattableStringFactory.Create("{0}", "<see "));

        [Test]
        public void CachedEmptyFormatWithUnusedMalformedTagIsRejectedBeforeNativeNormalizer()
            => AssertRawCachedDescriptionRejectedBeforeNormalization(FormattableStringFactory.Create(string.Empty, "<see "));

        [Test]
        public void ColdFallbackMalformedNameIsRejectedBeforeNativeNormalizer()
        {
            WithRealFoo((owner, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var input = ((Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner).InputClient;
                var type = owner.Type;
                var name = type.Name;
                var summary = input.Summary;
                var doc = input.Doc;
                var fields = new[] { "_description", "_xmlDocs", "_disabledFileWarnings" }.Select(field => typeof(TypeProvider).GetField(field, flags)!).ToArray();
                var original = fields.Select(field => field.GetValue(owner)).ToArray();
                try
                {
                    input.GetType().GetProperty("Summary")!.SetValue(input, null);
                    input.GetType().GetProperty("Doc")!.SetValue(input, null);
                    typeof(CSharpType).GetProperty("Name")!.SetValue(type, "<see ");
                    fields[0].SetValue(owner, null);
                    fields[1].SetValue(owner, null);
                    fields[2].SetValue(owner, Array.Empty<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement>());
                    var phase = new PreparedPocHeaderFinalization();
                    phase.Materialize(owner, null);
                    Assert.That(owner.CachedXmlDocs, Is.Null);
                    Assert.That(fields[0].GetValue(owner), Is.Null);
                    Assert.That(phase.Count, Is.Zero);
                }
                finally
                {
                    typeof(CSharpType).GetProperty("Name")!.SetValue(type, name);
                    input.GetType().GetProperty("Summary")!.SetValue(input, summary);
                    input.GetType().GetProperty("Doc")!.SetValue(input, doc);
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(owner, original[i]); }
                }
            });
        }

        [Test]
        public void CachedDescriptionRepeatedNestedExpansionIsRejectedBeforeNativeNormalizer()
        {
            object nested = "x";
            for (var i = 0; i < 16; i++) { nested = FormattableStringFactory.Create("{0}{0}", nested); }
            AssertRawCachedDescriptionRejectedBeforeNormalization(FormattableStringFactory.Create("{0}", nested));
        }

        [Test]
        public void HeaderFinalizationReusesAdmittedCachedDescription()
        {
            WithRealFoo((owner, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var input = ((Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner).InputClient;
                var summary = input.Summary;
                var doc = input.Doc;
                var fields = new[] { "_description", "_xmlDocs", "_disabledFileWarnings" }.Select(field => typeof(TypeProvider).GetField(field, flags)!).ToArray();
                var original = fields.Select(field => field.GetValue(owner)).ToArray();
                try
                {
                    fields[0].SetValue(owner, null);
                    fields[1].SetValue(owner, null);
                    fields[2].SetValue(owner, Array.Empty<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement>());
                    input.Update(summary: "captured description", doc: "captured description");
                    var captured = owner.Description;
                    input.Update(summary: "later input", doc: "later input");
                    var phase = new PreparedPocHeaderFinalization();
                    phase.Materialize(owner, null);
                    Assert.That(phase.Get(owner)?.IsCurrent, Is.True);
                    Assert.That(owner.CachedDescription, Is.SameAs(captured));
                    using var writer = new CodeWriter();
                    owner.CachedXmlDocs!.Summary!.Write(writer);
                    var content = writer.ToString(false);
                    Assert.That(content, Does.Contain("captured description"));
                    Assert.That(content, Does.Not.Contain("later input"));
                }
                finally
                {
                    input.GetType().GetProperty("Summary")!.SetValue(input, summary);
                    input.GetType().GetProperty("Doc")!.SetValue(input, doc);
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(owner, original[i]); }
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CachedDescriptionIsAdmittedBeforeXmlDocumentationNormalization(bool hostileNested)
        {
            WithRealFoo((owner, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var input = ((Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner).InputClient;
                var summary = input.Summary;
                var doc = input.Doc;
                var fields = new[] { "_description", "_xmlDocs", "_disabledFileWarnings" }.Select(field => typeof(TypeProvider).GetField(field, flags)!).ToArray();
                var original = fields.Select(field => field.GetValue(owner)).ToArray();
                var unknown = new ThrowingCachedDescription();
                try
                {
                    fields[0].SetValue(owner, null);
                    fields[1].SetValue(owner, null);
                    fields[2].SetValue(owner, Array.Empty<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement>());
                    if (hostileNested)
                    {
                        var cached = FormattableStringFactory.Create("{0}", "safe");
                        cached.GetArguments()[0] = unknown;
                        fields[0].SetValue(owner, cached);
                    }
                    else
                    {
                        var large = new string('a', 300000);
                        input.Update(summary: large, doc: large);
                        Assert.That(owner.Description, Is.Not.Null);
                    }
                    input.Update(summary: "safe", doc: "short current input");
                    var phase = new PreparedPocHeaderFinalization();
                    Assert.DoesNotThrow(() => phase.Materialize(owner, null));
                    Assert.That(owner.CachedXmlDocs, Is.Null, "Validate the cached description actually consumed by the builder, not just current input text.");
                    Assert.That(unknown.Calls, Is.Zero);
                    Assert.That(phase.Count, Is.Zero);
                }
                finally
                {
                    input.GetType().GetProperty("Summary")!.SetValue(input, summary);
                    input.GetType().GetProperty("Doc")!.SetValue(input, doc);
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(owner, original[i]); }
                }
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HeaderDocumentationIsBoundedBeforeFallbackDescriptionBuilder(bool oversizedName)
        {
            WithRealFoo((owner, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                var input = client.InputClient;
                var type = owner.Type;
                var name = type.Name;
                var summary = input.Summary;
                var doc = input.Doc;
                var fields = new[] { "_description", "_xmlDocs", "_disabledFileWarnings" }.Select(field => typeof(TypeProvider).GetField(field, flags)!).ToArray();
                var original = fields.Select(field => field.GetValue(owner)).ToArray();
                try
                {
                    input.GetType().GetProperty("Summary")!.SetValue(input, null);
                    input.GetType().GetProperty("Doc")!.SetValue(input, null);
                    fields[0].SetValue(owner, null);
                    fields[1].SetValue(owner, null);
                    fields[2].SetValue(owner, Array.Empty<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement>());
                    if (oversizedName) { typeof(CSharpType).GetProperty("Name")!.SetValue(type, new string('A', 300000)); }
                    var phase = new PreparedPocHeaderFinalization();
                    if (!oversizedName)
                    {
                        typeof(PreparedPocHeaderFinalization).GetField("_budget", flags)!.SetValue(phase, new PocWorkBudget(4096));
                    }
                    phase.Materialize(owner, null);
                    Assert.That(owner.CachedXmlDocs, Is.Null, "Do not normalize/allocate documentation before bounding the fallback name and reserving aggregate documentation work.");
                    Assert.That(fields[0].GetValue(owner), Is.Null);
                    Assert.That(phase.Count, Is.Zero);
                }
                finally
                {
                    typeof(CSharpType).GetProperty("Name")!.SetValue(type, name);
                    input.GetType().GetProperty("Summary")!.SetValue(input, summary);
                    input.GetType().GetProperty("Doc")!.SetValue(input, doc);
                    for (var i = 0; i < fields.Length; i++) { fields[i].SetValue(owner, original[i]); }
                }
            });
        }

        private static void AssertSameWarningContent(IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement> actual,
            IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.Select(warning => warning.Justification), Is.EqualTo(expected.Select(warning => warning.Justification)));
            // Test-only native writing validates externally observable pragma
            // content, not equality of separately constructed visitor IR nodes.
            static string Write(IReadOnlyList<Microsoft.TypeSpec.Generator.Statements.SuppressionStatement> warnings)
            {
                using var writer = new CodeWriter();
                using (writer.SuppressWarnings(warnings)) { }
                return writer.ToString();
            }
            Assert.That(Write(actual), Is.EqualTo(Write(expected)));
        }

        [Test]
        public void NativeNonemptyFileWarningInstancesHaveEqualContentControl()
        {
            WithRealFoo((_, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var first = new Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelSerializationExtensionsDefinition();
                var second = new Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelSerializationExtensionsDefinition();
                var field = first.GetType().GetField("_hasFileBinaryContentJsonModel", flags)!;
                field.SetValue(first, true);
                field.SetValue(second, true);
                AssertSameWarningContent(first.DisabledFileWarnings, second.DisabledFileWarnings);
            });
        }

        [Test]
        public void AuditedNonemptyFileWarningFinalizesWithNativeLiteralContent()
        {
            WithRealFoo((_, _) =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var provider = new Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelSerializationExtensionsDefinition();
                var legacy = new Microsoft.TypeSpec.Generator.ClientModel.Providers.ModelSerializationExtensionsDefinition();
                var kind = provider.GetType();
                kind.GetField("_hasFileBinaryContentJsonModel", flags)!.SetValue(provider, true);
                kind.GetField("_hasFileBinaryContentJsonModel", flags)!.SetValue(legacy, true);
                typeof(TypeProvider).GetField("_type", flags)!.SetValue(provider, new CSharpType(typeof(object)));
                typeof(TypeProvider).GetField("_declarationModifiers", flags)!.SetValue(provider, TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class);
                var phase = new PreparedPocHeaderFinalization();
                phase.Materialize(provider, null);
                Assert.That(phase.Get(provider)?.IsCurrent, Is.True);
                Assert.That(provider.CachedDisabledFileWarnings!.Count, Is.EqualTo(1));
                AssertSameWarningContent(provider.CachedDisabledFileWarnings, legacy.DisabledFileWarnings);
            });
        }

        [Test]
        public void RealFooApprovedHeaderFinalizationPrecedesSourceCapture()
        {
            WithRealFoo((_, _) => { }, finalizePocHeaders: true, inspectGeneration: (_, _, _, prepared) =>
            {
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.SourceFiles.Count(), Is.EqualTo(39));
                Assert.Multiple(() =>
                {
                    foreach (var file in prepared.SourceFiles)
                    {
                        Assert.That(file.FileOwner.CachedDisabledFileWarnings, Is.Not.Null,
                            file.FilePath + " warnings must be canonical before source capture/name planning.");
                        if (file.FileOwner.CachedDeclarationModifiers?.HasFlag(TypeSignatureModifiers.Public) == true)
                        {
                            Assert.That(file.FileOwner.CachedXmlDocs, Is.Not.Null,
                                file.FilePath + " public type documentation must be canonical before final writers.");
                        }
                        var header = prepared.FinalizedPocHeaders!.Get(file.FileOwner);
                        Assert.That(header?.IsCurrent, Is.True, file.FilePath + " must have a supported finalized header snapshot.");
                    }
                });
                var dictionary = prepared.SourceFiles.Single(file => file.FilePath.EndsWith("/ChangeTrackingDictionary.cs", StringComparison.Ordinal));
                Assert.That(dictionary.Roots.Any(root => root.Id.RootRole == "type-constraint"), Is.True);
            });
        }

        [Test]
        public void RealFooSerializationPhysicalFilesPreserveNativeHeaderRoute()
        {
            string[] Write(bool finalized)
            {
                string[]? files = null;
                WithRealFoo((unusedOwner, prepared) => { }, finalizePocHeaders: finalized,
                    inspectGeneration: (output, referenceMap, unused, prepared) =>
                {
                    var serializers = GetWrittenProviderStream(output, referenceMap)
                        .OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.FixedEnumSerializationProvider>().ToArray();
                    Assert.That(serializers, Is.Not.Empty, "Exercise a real supported serialization child, not an empty filtered stream.");
                    files = serializers.Select(provider =>
                    {
                        if (finalized) { Assert.That(prepared.FinalizedPocHeaders!.Get(provider)?.IsCurrent, Is.True); }
                        var writer = new TypeProviderWriter(provider)
                        {
                            PreparedFieldTypes = prepared,
                            PreparedPocHeaders = prepared.FinalizedPocHeaders
                        };
                        Assert.That(writer.HasPhysicalWriteEntry, Is.False);
                        var file = writer.Write();
                        Assert.That(writer.HasPhysicalWriteEntry, Is.True);
                        Assert.That(file.Name, Does.EndWith(".Serialization.cs"));
                        Assert.That(file.PreparedPocNameCount, Is.Zero);
                        if (finalized) { Assert.That(prepared.FinalizedPocHeaders!.Get(provider)?.IsCurrent, Is.True); }
                        // Header facts and physical entry do not manufacture a
                        // canonical attribute facet or a spelling permission.
                        return file.Name + "\n" + file.Content;
                    }).ToArray();
                });
                return files!;
            }
            Assert.That(Write(true), Is.EqualTo(Write(false)));
        }

        [Test]
        public void RealFooFinalizedHeadersRemainCurrentThroughAllNativeWrites()
        {
            WithRealFoo((_, _) => { }, finalizePocHeaders: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                Assert.That(prepared.FinalizedPocHeaders!.Count, Is.EqualTo(39));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                var failures = new System.Collections.Generic.List<string>();
                var count = 0;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    var plan = prepared.PocGenerationEnvelope!.GetFile(provider)!;
                    if (!plan.HasCompleteNativeConsumption) { failures.Add(file.Name + ": " + plan.NativeConsumptionFailure); }
                    if (prepared.FinalizedPocHeaders.Get(provider)?.IsCurrent != true) { failures.Add(file.Name + ": finalized header changed"); }
                    var syntax = CSharpSyntaxTree.ParseText(file.Content, new CSharpParseOptions(LanguageVersion.CSharp12));
                    foreach (var diagnostic in syntax.GetDiagnostics()) { failures.Add(file.Name + ": " + diagnostic); }
                    count++;
                }
                Assert.That(count, Is.EqualTo(39));
                Assert.That(failures, Is.Empty, "Finalized header identities and the independent traversal must match the single native pass.");
            });
        }

        [Test]
        public void RealFooHeaderFinalizationPreservesNormalWriterContentAndCallbacks()
        {
            (string Content, string[] Calls) Write(bool finalized)
            {
                var calls = new System.Collections.Generic.List<string>();
                string? content = null;
                WithRealFoo((owner, prepared) =>
                {
                    content = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write().Content;
                }, precomputePoc: !finalized, finalizePocHeaders: finalized, sourceCurrent: () => { calls.Add("source"); return true; });
                return (content!, calls.ToArray());
            }
            var legacy = Write(false);
            var finalized = Write(true);
            Assert.That(finalized.Content, Is.EqualTo(legacy.Content));
            Assert.That(finalized.Calls, Is.EqualTo(legacy.Calls), "Audited materialization must not add or replay source callbacks.");
        }

        [Test]
        public void RealFooAllFinalizedFilesPreserveNativeRawQualifiedOutputAndCallbackOrder()
        {
            (Dictionary<string, (string Raw, string Qualified)> Files, string[] Calls) Write(bool finalized)
            {
                var calls = new List<string>();
                int writerCallbacks = 0;
                var files = new Dictionary<string, (string Raw, string Qualified)>(StringComparer.Ordinal);
                var phase = "preparation";
                WithRealFoo((_, _) => { }, precomputePoc: !finalized, finalizePocHeaders: finalized,
                    sourceCurrent: () =>
                    {
                        calls.Add(phase);
                        if (new System.Diagnostics.StackTrace().GetFrames()?.Any(frame => frame.GetMethod()?.DeclaringType == typeof(TypeProviderWriter)) == true)
                        { writerCallbacks++; }
                        return true;
                    }, inspectGeneration: (output, map, _, prepared) =>
                {
                    foreach (var provider in GetWrittenProviderStream(output, map))
                    {
                        phase = provider.RelativeFilePath;
                        var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                        prepared.BeginPocProviderWrite(provider, writer);
                        var file = writer.Write();
                        prepared.EndPocProviderWrite(provider, writer, file);
                        files.Add(file.Name, (file.Content, file.SelectPreparedNames(false, false, false).Content));
                    }
                });
                Assert.That(files.Count, Is.EqualTo(39));
                Assert.That(writerCallbacks, Is.GreaterThan(0), "Equal callback sequences must include actual writer-phase callbacks, not only preparation.");
                return (files, calls.ToArray());
            }
            var native = Write(false);
            var finalized = Write(true);
            Assert.That(finalized.Files.Keys, Is.EquivalentTo(native.Files.Keys));
            foreach (var pair in native.Files)
            {
                Assert.That(finalized.Files[pair.Key].Raw, Is.EqualTo(pair.Value.Raw), pair.Key + " raw");
                Assert.That(finalized.Files[pair.Key].Qualified, Is.EqualTo(pair.Value.Qualified), pair.Key + " qualified");
            }
            Assert.That(finalized.Calls, Is.EqualTo(native.Calls), "Compare source callback order across the complete single native provider pass.");
        }

        [Test]
        public void RealFooFinalizedHeaderUsesCanonicalBoundaryValuesAndVetoesDocMutation()
        {
            WithRealFoo((owner, prepared) =>
            {
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                var docs = owner.CachedXmlDocs;
                var header = prepared.FinalizedPocHeaders!.Get(owner);
                Assert.That(docs, Is.Not.Null);
                Assert.That(header, Is.Not.Null);
                Assert.That(header!.IsCurrent, Is.True);
                client.InputClient.Update(doc: "PocInputChangedAfterHeaderBoundary");
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(owner.CachedXmlDocs, Is.SameAs(docs));
                Assert.That(output.Content, Does.Not.Contain("PocInputChangedAfterHeaderBoundary"),
                    "This is the explicitly approved canonicalization boundary, not legacy lazy input capture.");
                docs!.Update(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([$"changed canonical doc"]));
                Assert.That(header.IsCurrent, Is.False, "Changes to finalized canonical objects must veto their proof.");
            }, finalizePocHeaders: true);
        }

        [Test]
        public void RealFooNativeSyntaxBaselineHasNoUnconfinedParserErrors()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var failures = new System.Collections.Generic.List<string>();
                var count = 0;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(provider, writer, file);
                    // Validation only. Planning must never parse emitted text.
                    var tree = CSharpSyntaxTree.ParseText(file.Content, new CSharpParseOptions(LanguageVersion.CSharp12));
                    foreach (var diagnostic in tree.GetDiagnostics())
                    {
                        var message = file.Name + ": " + diagnostic;
                        TestContext.Progress.WriteLine("POC native syntax baseline " + message);
                        if (diagnostic.Severity == DiagnosticSeverity.Error) { failures.Add(message); }
                    }
                    count++;
                }
                Assert.That(count, Is.EqualTo(39));
                Assert.That(failures, Is.Empty,
                    "The real native baseline must be understood before a bounded IR grammar certificate is introduced.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooDictionaryConstraintCannotHideAnEmittedReplacement(bool replace)
        {
            var targetWrites = 0;
            TypeProvider? dictionary = null;
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]? constraints = null;
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression? original = null;
            var callbacks = 0;
            var armed = false;
            WithRealFoo((_, _) => { }, precomputePoc: true, sourceCurrent: () =>
            {
                if (!armed || dictionary?.CachedWhereClause is not { } where) { return true; }
                callbacks++;
                if (callbacks == 1 && replace)
                {
                    constraints = (Microsoft.TypeSpec.Generator.Expressions.ValueExpression[])where.Constraints;
                    original = constraints[0];
                    constraints[0] = new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("notnull\nclass PocEscaped {}", null);
                }
                if (callbacks == 3 && replace) { constraints![0] = original!; }
                return true;
            }, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                dictionary = prepared.SourceFiles.Single(file => file.FilePath.EndsWith("/ChangeTrackingDictionary.cs", StringComparison.Ordinal)).FileOwner;
                var plan = prepared.PocGenerationEnvelope!.GetFile(dictionary)!.TypeConstraint!;
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    armed = ReferenceEquals(provider, dictionary);
                    var file = writer.Write();
                    armed = false;
                    prepared.EndPocProviderWrite(provider, writer, file);
                    if (!ReferenceEquals(provider, dictionary)) { continue; }
                    targetWrites++;
                    Assert.That(callbacks, Is.GreaterThanOrEqualTo(3));
                    Assert.That(file.Content.Contains("class PocEscaped", StringComparison.Ordinal), Is.EqualTo(replace),
                        "Native output is a test-only witness of the retained producer, never a planning input.");
                    Assert.That(plan.HasObservedExpectedEmission, Is.EqualTo(!replace),
                        "Restoring the carrier before the retained keyword writes must not conceal that unsafe producer.");
                    break;
                }
                Assert.That(targetWrites, Is.EqualTo(1), "The actual constrained dictionary writer must be reached.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocStreamReceiptDistinguishesWrittenFilesFromEnumeration(bool writeFiles)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, inspectGeneration: (output, referenceMap, _, prepared) =>
            {
                var count = 0;
                var matched = 0;
                var rejected = 0;
                var envelope = prepared.PocGenerationEnvelope;
                Assert.That(envelope, Is.Not.Null);
                Assert.That(envelope!.HasCompleteTraversalReceipts, Is.False);
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                {
                    count++;
                    var plan = envelope.GetFile(provider);
                    Assert.That(plan, Is.Not.Null, "Every actual written provider must have a pre-write plan.");
                    Assert.That(plan!.State, Is.EqualTo(PreparedPocGenerationEnvelope.ReceiptState.Missing));
                    if (!writeFiles) { continue; }
                    var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(provider, writer);
                    var file = writer.Write();
                    TestContext.Progress.WriteLine($"POC receipt {file.Name}: current={file.SourceConsumption?.WasCurrentAtEndOfWrite}");
                    prepared.EndPocProviderWrite(provider, writer, file);
                    var expected = file.SourceConsumption?.WasCurrentAtEndOfWrite == true
                        ? PreparedPocGenerationEnvelope.ReceiptState.Matched : PreparedPocGenerationEnvelope.ReceiptState.Rejected;
                    Assert.That(plan.State, Is.EqualTo(expected), "All-file accounting must agree with the actual owned traversal, not just stream identity.");
                    if (plan.State == PreparedPocGenerationEnvelope.ReceiptState.Matched) { matched++; } else { rejected++; }
                }
                Assert.That(count, Is.GreaterThan(1));
                Assert.That(envelope.PlannedFileCount, Is.EqualTo(count));
                Assert.That(envelope.HasCompleteTraversalReceipts, Is.False,
                    "Enumeration has no receipts; writing currently exposes unsupported lazy/header boundaries.");
                Assert.That(envelope.HasUncoveredEmission, Is.True);
                if (writeFiles)
                {
                    Assert.That(matched, Is.GreaterThan(0));
                    Assert.That(rejected, Is.GreaterThan(0));
                    TestContext.Progress.WriteLine($"POC all-file envelope: matched={matched}, rejected={rejected}");
                }
                Assert.That(prepared.IsObservedPocProviderWriteStreamComplete, Is.EqualTo(writeFiles),
                    "Enumeration by itself is not evidence that the planned files were written.");
                Assert.That(prepared.IsPocProviderWriteReceiptComplete, Is.False,
                    "An isolated fixture cannot claim the private production generation-owner phase.");
            });
        }

        [Test]
        public void RealFooPocCandidateRejectsChangedSiblingPartialBeforeWriting()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.TryPreparePocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.IsCurrent, Is.True);
                var rest = prepared.SourceFiles.Single(source => source.FileOwner.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider) &&
                    source.FileOwner.CachedType?.Namespace == owner.CachedType!.Namespace && source.FileOwner.CachedType?.Name == owner.CachedType.Name).FileOwner;
                var previousNested = rest.NestedTypes.ToArray();
                rest.Update(nestedTypes: [new PocNestedPipelineMessageShadow(rest)]);
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                Assert.That(candidate.IsCurrent, Is.False);
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                    "A changed sibling partial cannot reuse the old scope-name inventory.");
                rest.Update(nestedTypes: previousNested);
                Assert.That(candidate.IsCurrent, Is.False, "Observed sibling staleness remains sticky after restoration.");
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocCandidateRejectsStaleSourceInputsBeforeWriting()
        {
            var current = true;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var candidate = prepared.TryPreparePocOrdinaryCandidate(file, declaration);
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.IsCurrent, Is.True);
                current = false;
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                    "A stale source-input proof cannot authorize even a bare-name candidate.");
                Assert.That(candidate.IsCurrent, Is.False);
                current = true;
                Assert.That(candidate.IsCurrent, Is.False, "Observed source-input staleness remains sticky after restoration.");
            }, precomputePoc: true, sourceCurrent: () => current);
        }

        [Test]
        public void RealFooPocCandidateRejectsCanonicalRootReplacementBeforeWriting()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var output = CodeModelGenerator.Instance.OutputLibrary;
                var roots = output.TypeProviders;
                output.TypeProviders = new List<TypeProvider>(roots);
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                    "A retained old root list is not the active output's canonical root.");
                output.TypeProviders = roots;
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocCandidateRejectsReplacedSourceModelBeforeWriting()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Not.Null);
                var generator = CodeModelGenerator.Instance;
                var original = generator.SourceInputModel;
                var replacement = new SourceInputModel(original.Customization, null) { PreEmissionSourceInputs = original.PreEmissionSourceInputs };
                generator.SourceInputModel = replacement;
                try
                {
                    Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                        "A previously captured source model is not the generator's replacement model.");
                }
                finally { generator.SourceInputModel = original; }
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocCandidateRejectsDeclarationMutationDuringSourceProof()
        {
            Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression? producer = null;
            var armed = false;
            var changed = false;
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var declaration = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                producer = (Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression)declaration.Producer;
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Not.Null);
                armed = true;
                Assert.That(prepared.TryPreparePocOrdinaryCandidate(file, declaration), Is.Null,
                    "The final source-proof callback must not leave the selected occurrence stale.");
                Assert.That(changed, Is.True);
            }, sourceCurrent: () =>
            {
                if (armed) { armed = false; changed = true; producer!.Update(variable: null, isOut: true); }
                return true;
            }, precomputePoc: true);
        }

        private sealed class PocDecisionParent : TypeProvider
        {
            protected override string BuildName() => "PocDecisionParent";
            protected override string BuildNamespace() => "Poc.Parent.Initial";
            protected override string BuildRelativeFilePath() => "src/Generated/PocDecisionParent.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class PocDecisionSerialization(PocDecisionParent parent) : TypeProvider
        {
            protected override string BuildName() => "PipelineMessage";
            protected override string BuildNamespace() => "Poc.Serialization";
            protected override string BuildRelativeFilePath() => "src/Generated/PocDecisionSerialization.cs";
            protected override TypeProvider? BuildDeclaringTypeProvider() => parent;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class PocDecisionContainer(PocDecisionSerialization serialization) : TypeProvider
        {
            protected override string BuildName() => "PocDecisionContainer";
            protected override string BuildNamespace() => "Poc.Control";
            protected override string BuildRelativeFilePath() => "src/Generated/PocDecisionContainer.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override TypeProvider[] BuildSerializationProviders() => [serialization];
        }

        private sealed class PocDecisionMutator(PocDecisionParent parent, string targetNamespace) : TypeProvider
        {
            private bool _armed;
            private bool _changeParent;
            internal bool FiredDuringPreparation { get; private set; }
            internal void Arm(bool changeParent) { _changeParent = changeParent; _armed = true; }
            protected override string BuildName() => "PocDecisionMutator";
            protected override string BuildNamespace() => "Poc.Control";
            protected override string BuildRelativeFilePath() => "src/Generated/PocDecisionMutator.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override TypeProvider[] BuildSerializationProviders()
            {
                if (_armed && new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "CreateCore"))
                {
                    _armed = false;
                    FiredDuringPreparation = true;
                    if (_changeParent) { parent.Type.Namespace = targetNamespace; }
                }
                return [];
            }
        }

        private sealed class PocReentrantModelFactory(string targetNamespace) : ModelFactoryProvider([])
        {
            private bool _armed;
            private bool _changeNamespace;
            internal bool FiredDuringPreparation { get; private set; }
            internal void Arm(bool changeNamespace) { _changeNamespace = changeNamespace; _armed = true; }
            protected override string BuildName() => "PocReentrantFactory";
            protected override string BuildNamespace() => "Poc.Factory.Initial";
            protected override string BuildRelativeFilePath() => "src/Generated/PocReentrantFactory.cs";
            protected internal override MethodProvider[] BuildMethods()
            {
                if (!_armed || !new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "ShouldWriteRoot")) { return []; }
                _armed = false;
                FiredDuringPreparation = true;
                if (_changeNamespace) { Type.Namespace = targetNamespace; }
                var signature = new MethodSignature("Create", null, MethodSignatureModifiers.Public | MethodSignatureModifiers.Static, null, null, []);
                return [new MethodProvider(signature, Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement.Empty, this)];
            }
        }

        private sealed class PocEmptyModelFactory(string ns) : ModelFactoryProvider([])
        {
            protected override string BuildName() => "PipelineMessage";
            protected override string BuildNamespace() => ns;
            protected override string BuildRelativeFilePath() => "src/Generated/PocEmptyModelFactory.cs";
            protected internal override MethodProvider[] BuildMethods() => [];
        }

        private sealed class PocMutableDeclaringProvider : TypeProvider
        {
            private TypeProvider? _declaring;
            internal int DeclaringGetterCalls { get; private set; }
            internal void ChangeDeclaring(TypeProvider provider) => _declaring = provider;
            protected override string BuildName() => "PocMutableDeclaring";
            protected override string BuildNamespace() => "Poc.Declaring";
            protected override string BuildRelativeFilePath() => "src/Generated/PocMutableDeclaring.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override TypeProvider? BuildDeclaringTypeProvider() { DeclaringGetterCalls++; return _declaring; }
        }

        private sealed class PocOrderingCompetitor : TypeProvider
        {
            protected override string BuildName() => "PipelineMessage";
            protected override string BuildNamespace() => "Poc.Ordering.Initial";
            protected override string BuildRelativeFilePath() => "src/Generated/PocOrderingCompetitor.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class PocOrderingOwner(PocOrderingCompetitor competitor, string targetNamespace) : TypeProvider
        {
            private bool _armed;
            private bool _changeNamespace;
            internal bool FiredDuringDeclarationScope { get; private set; }
            internal void Arm(bool changeNamespace) { _changeNamespace = changeNamespace; _armed = true; }
            protected override string BuildName() => "PocOrderingOwner";
            protected override string BuildNamespace() => "Poc.Ordering.Control";
            protected override string BuildRelativeFilePath() => "src/Generated/PocOrderingOwner.cs";
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
            protected override Microsoft.TypeSpec.Generator.Expressions.WhereExpression? BuildWhereClause()
            {
                if (_armed && new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "IsSupportedDeclarationGroup"))
                {
                    _armed = false;
                    FiredDuringDeclarationScope = true;
                    if (_changeNamespace) { competitor.Type.Namespace = targetNamespace; }
                }
                return null;
            }
        }

        private sealed class PocReceiptProvider(string name, string ns, string path) : TypeProvider
        {
            protected override string BuildName() => name;
            protected override string BuildNamespace() => ns;
            protected override string BuildRelativeFilePath() => path;
            protected override TypeSignatureModifiers BuildDeclarationModifiers() => TypeSignatureModifiers.Public | TypeSignatureModifiers.Class;
        }

        private sealed class PocPipelineMessageCompetitor(string ns) : TypeProvider
        {
            protected override string BuildName() => "PipelineMessage";
            protected override string BuildNamespace() => ns;
            protected override string BuildRelativeFilePath() => "PocPipelineMessageCompetitor.cs";
        }

        private sealed class PocNestedPipelineMessageShadow(TypeProvider declaring) : TypeProvider
        {
            protected override string BuildName() => "PipelineMessage";
            protected override string BuildNamespace() => declaring.Type.Namespace;
            protected override string BuildRelativeFilePath() => "PocNestedPipelineMessageShadow.cs";
            protected override TypeProvider? BuildDeclaringTypeProvider() => declaring;
        }

        [Test]
        public void RealFooLocalSourceObservationIsCapturedBeforeAnyOutputWriter()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var methodRoot = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0);
                var declaration = methodRoot.DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                foreach (var node in methodRoot.DescendantsAndSelf().Where(node => declaration.Id.EdgePath == node.Id.EdgePath ||
                    declaration.Id.EdgePath.StartsWith(node.Id.EdgePath + "/", StringComparison.Ordinal)))
                {
                    TestContext.Progress.WriteLine($"Foo local ancestor: {node.Id.EdgePath} {node.Producer.GetType().FullName} opaque={node.IsOpaque}");
                }
                var task03Observation = file.Observations?.GetCurrent(declaration);
                Assert.That(task03Observation, Is.Null, "The existing global Task 03 call-observation gate stays unchanged.");
                var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, declaration);
                Assert.That(observation, Is.Not.Null, "The POC's direct, no-enclosing-call source case is independently available.");
                Assert.That(observation!.IsCurrent, Is.True);
                Assert.That(observation.Occurrence, Is.SameAs(declaration));
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Assert.That(prepared.SourceFiles.All(source => (bool)started.GetValue(source)! == false), Is.True);
            });
        }

        [Test]
        public void RealFooExceptionDocumentationHasBoundedEmissionEnvelope()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var docs = file.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Select(node => node.Producer).OfType<XmlDocProvider>()
                    .First(documentation => documentation.Exceptions.Count > 0);
                Assert.That(docs.Exceptions.Count, Is.EqualTo(1));
                var envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs);
                Assert.That(envelope, Is.Not.Null, "Real Foo exception documentation must be confined without executing arbitrary formatting.");
                Assert.That(envelope!.IsCurrent, Is.True);
            });
        }

        [Test]
        public void RealFooPocReportsOriginalOpaqueArgumentCarrierShape()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var calls = file.Roots.Where(root => root.Id.RootRole == "method" && root.Id.RootIndex is 2 or 3 or 6 or 7)
                    .SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsOpaque &&
                        node.Producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression).ToArray();
                Assert.That(calls.Length, Is.EqualTo(4));
                foreach (var node in calls)
                {
                    var arguments = ((Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)node.Producer).Arguments;
                    var type = arguments.GetType();
                    TestContext.Progress.WriteLine($"Foo carrier: {type.FullName}, assembly={type.Assembly.GetName().Name}, sealed={type.IsSealed}, compilerGenerated={type.IsDefined(typeof(CompilerGeneratedAttribute), false)}");
                }
            });
        }

        [Test]
        public void RealFooConstructionWitnessKeepsOriginalArgumentIdentitiesAndRejectsReplacement()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var nodes = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Select(node => node.Producer).ToArray();
                var convenience = nodes.OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>()
                    .Where(call => call.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync").ToArray();
                var pipeline = nodes.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider>()
                    .Select(wrapper => wrapper.Original).OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>().ToArray();
                Assert.That(convenience, Has.Length.EqualTo(4));
                Assert.That(pipeline, Has.Length.EqualTo(4));
                foreach (var call in convenience.Concat(pipeline))
                {
                    var original = call.Arguments;
                    Assert.That(original, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>>(),
                        "The approved concrete-carrier change preserves the read-only interface, not compiler-private type identity.");
                    Assert.That(ContractCollections.TryCopy(original, out _), Is.False, "Shared Task03 carrier policy remains unchanged.");
                    Assert.That(ContractCollections.TryCopyForPoc(original, new PocWorkBudget(4096), out _), Is.True,
                        "The explicit producer carrier supplies bounded current elements without compiler-private layout.");
                    var witness = ConstructedArgumentWitness.Read(call);
                    Assert.That(witness.HasValue, Is.True, $"Missing witness for {call.MethodSignature?.Name ?? call.MethodName} at revision {call.DeclarationRevision}.");
                    Assert.That(witness!.Value.Length, Is.EqualTo(original.Count));
                    for (int i = 0; i < original.Count; i++) { Assert.That(witness.Value[i], Is.SameAs(original[i])); }
                    var clone = call with { };
                    Assert.That(clone, Is.EqualTo(call), "A sidecar must not change record equality.");
                    Assert.That(clone.GetHashCode(), Is.EqualTo(call.GetHashCode()));
                    Assert.That(ConstructedArgumentWitness.Read(clone).HasValue, Is.False, "A record clone must not inherit another call's sidecar.");
                }

                var changed = convenience[0];
                var carrier = changed.Arguments;
                changed.Arguments = new ThrowOnArgumentEnumeration();
                Assert.That(ConstructedArgumentWitness.Read(changed).HasValue, Is.False, "A replacement must not be enumerated or inherit the sidecar.");
                changed.Arguments = carrier;
                Assert.That(ConstructedArgumentWitness.Read(changed).HasValue, Is.False, "Restoration through the IR setter remains stale.");
                Assert.That(ConstructedArgumentWitness.Read(convenience[1]).HasValue, Is.True, "A different call retains its own sidecar.");
                convenience[2].Update(arguments: convenience[2].Arguments);
                Assert.That(ConstructedArgumentWitness.Read(convenience[2]).HasValue, Is.False, "Even same-carrier Update changes the invocation revision.");
            });
        }

        private sealed class ThrowOnArgumentEnumeration : IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>
        {
            public int Count => throw new InvalidOperationException("Unknown carrier must not be accessed.");
            public Microsoft.TypeSpec.Generator.Expressions.ValueExpression this[int index] => throw new InvalidOperationException("Unknown carrier must not be accessed.");
            public System.Collections.Generic.IEnumerator<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> GetEnumerator() => throw new InvalidOperationException("Unknown carrier must not be enumerated.");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PipelineCallDoesNotRecordWitnessWhenPreEmissionIsDisabled(bool unconfiguredCore)
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-poc-disabled-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var coreInstance = typeof(CodeModelGenerator).GetField("_instance", flags)!;
            var clientInstance = typeof(ScmCodeModelGenerator).GetField("_instance", flags)!;
            var previousCore = coreInstance.GetValue(null);
            var previousClient = clientInstance.GetValue(null);
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), flags)!;
            var previousPolicy = policy.GetValue(null);
            try
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PreparedFieldTypes", "ClientStructureDefault.json"),
                    Path.Combine(directory, "tspCodeModel.json"));
                var configuration = Configuration.Load(directory, "{\"enable-pre-emission-simplification\":false,\"package-name\":\"Client.Structure.Service.Default\"}");
                var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = unconfiguredCore ? new UnconfiguredGenerator() : generator;
                var pipeline = generator.TypeFactory.ClientPipelineApi.FromExpression(new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("pipeline", null));
                var message = new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("message", null);
                var response = Microsoft.TypeSpec.Generator.ClientModel.Snippets.ClientPipelineApiSnippets.ProcessMessage(pipeline, message, null, false);
                var call = (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)response.Original;
                Assert.That(ConstructedArgumentWitness.Read(call).HasValue, Is.False);
                Assert.That(call.Arguments, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>>());
                var publicList = (IList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>)call.Arguments;
                Assert.That(publicList.IsReadOnly, Is.True);
                Assert.Throws<NotSupportedException>(() => publicList[0] = message);
                Assert.That(call.Arguments[0], Is.SameAs(message));
                Assert.That(ContractCollections.TryCopy(call.Arguments, out _), Is.False, "A disabled witness must not alter shared graph policy.");
            }
            finally
            {
                coreInstance.SetValue(null, previousCore);
                clientInstance.SetValue(null, previousClient);
                policy.SetValue(null, previousPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class UnconfiguredGenerator : CodeModelGenerator { }

        [Test]
        public void RealFooInvocationProducersSupplyExplicitReadOnlyArguments()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var opaque = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsOpaque).ToArray();
                var calls = opaque.Select(node => node.Producer)
                    .OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>()
                    .Where(call => call.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync")
                    .Concat(opaque.Select(node => node.Producer)
                        .OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider>()
                        .Select(wrapper => (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)wrapper.Original)).ToArray();
                Assert.That(calls.Length, Is.EqualTo(8), "Both audited producers must be represented before output starts.");
                foreach (var call in calls)
                {
                    Assert.That(call.Arguments, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>>(),
                        "The producer must construct the explicit read-only carrier; a visitor must not repair it.");
                    Assert.That(ContractCollections.TryCopyForPoc(call.Arguments, new PocWorkBudget(4096), out var current), Is.True);
                    Assert.That(current.Length, Is.EqualTo(call.Arguments.Count));
                    for (int i = 0; i < current.Length; i++) { Assert.That(ReferenceEquals(current[i], call.Arguments[i]), Is.True); }
                    var occurrence = opaque.Single(node => ReferenceEquals(node.Producer, call) ||
                        node.Producer is Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider wrapper && ReferenceEquals(wrapper.Original, call));
                    Assert.That(prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)?.IsCurrent, Is.True,
                        "All eight original supplemental segments must be current before any writer or visitor cleanup.");
                    if (current.Length != 0)
                    {
                        Assert.Throws<NotSupportedException>(() => ((IList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>)call.Arguments)[0] = current[0]);
                    }
                }
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocSupplementalCallObservationsArePreparedBeforeWriting()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
                var opaque = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsOpaque).ToArray();
                var convenience = opaque.Where(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call &&
                    call.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync").ToArray();
                var wrappers = opaque.Where(node => node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider)).ToArray();
                Assert.That(convenience, Has.Length.EqualTo(4));
                Assert.That(wrappers, Has.Length.EqualTo(4));
                var supplemental = prepared.GetPocSupplementalFile(file);
                Assert.That(supplemental, Is.Not.Null, "The bounded observation must be prepared before the writer starts.");
                foreach (var occurrence in convenience.Concat(wrappers))
                {
                    var segment = supplemental!.GetSegment(occurrence.Id);
                    Assert.That(segment, Is.Not.Null, occurrence.Id.ToString());
                    Assert.That(segment!.IsCurrent, Is.True);
                    Assert.That(segment.CallAndMemberRawNamesAreSafe, Is.True, "This observes only raw call/member names, not the complete file or binding.");
                }
                TestContext.Progress.WriteLine("All eight original prewrite segment-currentness/raw-name checks passed.");
                foreach (var occurrence in convenience.Concat(wrappers))
                {
                    var segment = supplemental!.GetSegment(occurrence.Id);
                    Assert.That(supplemental.GetSegment(occurrence.Id, new PocWorkBudget(1)), Is.Null,
                        "An actual segment-key hash and possible equality chain require their own credits.");
                    Assert.That(supplemental.GetSegment(occurrence.Id, new PocWorkBudget(65536)), Is.SameAs(segment));
                }
                Assert.That(convenience.All(node => node.IsOpaque) && wrappers.All(node => node.IsOpaque), Is.True,
                    "The shared source graph must remain unchanged.");
                var local = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetTypeOfNamePermission(file, local), Is.Null, "Supplemental observation is not a spelling permission.");
                Assert.That(prepared.SourceFiles.All(source => !source.HasStarted), Is.True);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocSupplementalCallsAreObservedInOwnedSingleWrite()
        {
            WithRealFoo((_, _) => { }, precomputePoc: true,
                inspectGeneration: (output, referenceMap, owner, prepared) =>
                {
                    var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                    var supplemental = prepared.GetPocSupplementalFile(source);
                    Assert.That(supplemental, Is.Not.Null, $"Foo requires a precomputed supplemental plan; candidate count={prepared.PocCandidateCount}.");
                    var targets = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsOpaque &&
                        (node.Producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call &&
                            call.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync" ||
                         node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider))).ToArray();
                    Assert.That(targets, Has.Length.EqualTo(8));
                    Assert.That(targets.All(node => supplemental!.GetSegment(node.Id)?.WasConsumedExactlyOnce == false), Is.True);
                    string? content = null;
                    CodeFile? fooOutput = null;
                    foreach (var provider in GetWrittenProviderStream(output, referenceMap))
                    {
                        var writer = new TypeProviderWriter(provider) { PreparedFieldTypes = prepared };
                        prepared.BeginPocProviderWrite(provider, writer);
                        var file = writer.Write();
                        prepared.EndPocProviderWrite(provider, writer, file);
                        if (ReferenceEquals(provider, owner)) { content = file.Content; fooOutput = file; }
                    }
                    Assert.That(content, Does.Contain("global::System.ClientModel.Primitives.PipelineMessage"), "Observation does not authorize a spelling.");
                    foreach (var node in targets)
                    {
                        Assert.That(supplemental!.GetSegment(node.Id)?.WasConsumedExactlyOnce, Is.True, node.Id.ToString());
                    }
                    var memberDocs = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                        .Where(node => node.IsOpaque && node.Producer is XmlDocProvider).ToArray();
                    Assert.That(memberDocs, Has.Length.EqualTo(11));
                    foreach (var node in memberDocs)
                    {
                        Assert.That(supplemental!.IsDocumentationConsumedExactlyOnce(node.Id), Is.True, node.Id.ToString());
                        Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(node.Id, new PocWorkBudget(0)), Is.False,
                            "A completed native receipt does not pay for a new occurrence-key/receipt scan.");
                    }
                    // Warm the same one-credit path before measuring. A point
                    // observation that has spent its only credit must stop
                    // before copying/rechecking the real eleven documentation
                    // boundaries and eight segment graphs.
                    Assert.That(supplemental!.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, new PocWorkBudget(1)), Is.False);
                    var oneCredit = new PocWorkBudget(1);
                    var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
                    var underfunded = supplemental.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, oneCredit);
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
                    Assert.That(underfunded, Is.False);
                    Assert.That(allocated, Is.LessThan(1024),
                        "A spent point-query credit cannot fund the actual file/segment/documentation rechecks.");
                    var fullQuery = new PocWorkBudget(65536);
                    Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, fullQuery), Is.True,
                        "An unobserved one-credit rejection must not stale a supported completed receipt.");
                    var requiredQuery = 65536 - fullQuery.Remaining;
                    Assert.That(requiredQuery, Is.GreaterThan(1));
                    Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, new PocWorkBudget(requiredQuery)), Is.True);
                    Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, new PocWorkBudget(requiredQuery - 1)), Is.False);
                    Assert.That(supplemental.IsRejected, Is.False, "Late external exhaustion must not stale the completed POC receipt.");
                    Assert.That(supplemental.IsDocumentationConsumedExactlyOnce(memberDocs[0].Id, new PocWorkBudget(65536)), Is.True);
                    Assert.That(prepared.CanCommit(), Is.True, "POC-only traversal observation must not veto an independent field.");

                    var sibling = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Select(node => node.Producer)
                        .OfType<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatements>().Select(body => body.Statements)
                        .OfType<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[]>().First(array => array.Length > 0);
                    var original = sibling[0];
                    try
                    {
                        sibling[0] = new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(
                            new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("default", null));
                        Assert.That(supplemental!.GetSegment(targets[0].Id)!.WasConsumedExactlyOnce, Is.False,
                            "A completed receipt must observe the changed sibling.");
                    }
                    finally { sibling[0] = original; }
                    Assert.That(supplemental!.IsRejected, Is.True, "The observed mismatch is a sticky POC-only veto.");
                    foreach (var target in targets)
                    {
                        Assert.That(supplemental.GetSegment(target.Id)!.WasConsumedExactlyOnce, Is.False,
                            "A file-level mismatch vetoes every supplemental segment.");
                    }
                    Assert.That(source.IsCurrent(), Is.True, "The POC-only receipt query must not invalidate shared traversal.");
                    Assert.That(prepared.CanCommit(), Is.True, "The POC-only veto must not discard an independent field.");
                    var selected = fooOutput!.SelectPreparedNames(fieldAllowed: prepared.CanCommit(), bodyAllowed: false);
                    Assert.That(selected.Content, Does.Contain("private readonly ClientType _client;"),
                        "The same-write output retains its independent prepared field choice after the POC veto.");
                    Assert.That(selected.Content, Does.Contain("global::System.ClientModel.Primitives.PipelineMessage"),
                        "The independent POC body choice remains qualified.");
                });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocSupplementalCursorObservesNestedAttributeKey(bool unsafeKey)
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            const string injected = "Name\n)] public class Escaped {} [Obsolete(";
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                var attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute),
                    new[] { new KeyValuePair<string, Microsoft.TypeSpec.Generator.Expressions.ValueExpression>(unsafeKey ? injected : "SafeKey",
                        new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(true)) });
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.TryExpression(attribute)
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticOpaqueAttribute", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                Assert.That(occurrence.IsOpaque, Is.True, "The shared graph must remain opaque.");
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                var segment = supplemental.GetSegment(occurrence.Id);
                Assert.That(segment, Is.Not.Null);
                Assert.That(segment!.KnownRawNamesAreSafe, Is.EqualTo(!unsafeKey));
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.EqualTo(unsafeKey));
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1), "The nested attribute must traverse the existing same-write cursor.");
                    Assert.That(segment.KnownRawNamesAreSafe, Is.EqualTo(!unsafeKey));
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.EqualTo(unsafeKey));
                    if (unsafeKey) { Assert.That(file.Content, Does.Contain(injected)); }
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"),
                        "The independent prepared field remains shortened in the same writer output.");
                }
                Assert.That(prepared.CanCommit(), Is.True, "Incomplete POC stream handling must not discard the field decision.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocSupplementalCursorObservesNestedRawOperator(bool unsafeOperator)
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            const string injected = "+\n} public class Escaped {} /*";
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression(unsafeOperator ? injected : "+",
                        new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1), new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(2))
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticOpaqueOperator", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                Assert.That(occurrence.IsOpaque, Is.True);
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                var segment = supplemental.GetSegment(occurrence.Id);
                Assert.That(segment, Is.Not.Null);
                Assert.That(segment!.KnownRawOperatorsAreSafe, Is.EqualTo(!unsafeOperator));
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawOperators, Is.EqualTo(unsafeOperator));
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1));
                    Assert.That(segment.KnownRawOperatorsAreSafe, Is.EqualTo(!unsafeOperator));
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawOperators, Is.EqualTo(unsafeOperator));
                    if (unsafeOperator) { Assert.That(file.Content, Does.Contain(injected)); }
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocSupplementalCursorObservesNestedRawKeyword(bool unsafeKeyword)
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            const string injected = "return\n} public class Escaped {} /*";
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression(unsafeKeyword ? injected : "return",
                        new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1))
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticOpaqueKeyword", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                Assert.That(occurrence.IsOpaque, Is.True);
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                var segment = supplemental.GetSegment(occurrence.Id);
                Assert.That(segment, Is.Not.Null);
                Assert.That(segment!.KnownRawKeywordsAreSafe, Is.EqualTo(!unsafeKeyword));
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawKeywords, Is.EqualTo(unsafeKeyword));
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1));
                    Assert.That(segment.KnownRawKeywordsAreSafe, Is.EqualTo(!unsafeKeyword));
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawKeywords, Is.EqualTo(unsafeKeyword));
                    if (unsafeKeyword) { Assert.That(file.Content, Does.Contain(injected)); }
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [TestCase("interpolated", false)]
        [TestCase("interpolated", true)]
        [TestCase("directive", false)]
        [TestCase("directive", true)]
        public void RealFooPocSupplementalCursorObservesNestedTextAndDirectives(string kind, bool unsafeText)
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            const string injectedLiteral = "raw\"; public class Escaped //";
            const string injectedDirective = "DEBUG\n#endif\npublic class Escaped {}";
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                Microsoft.TypeSpec.Generator.Expressions.ValueExpression argument = kind == "interpolated"
                    ? new Microsoft.TypeSpec.Generator.Expressions.FormattableStringExpression(unsafeText ? injectedLiteral : "safe literal", Array.Empty<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>())
                    : new Microsoft.TypeSpec.Generator.Expressions.TryExpression(new Microsoft.TypeSpec.Generator.Statements.IfElsePreprocessorStatement(
                        unsafeText ? injectedDirective : "DEBUG", new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(
                            new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("break", null))));
                var arguments = new[] { argument };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticOpaqueText", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                Assert.That(occurrence.IsOpaque, Is.True);
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                var segment = supplemental.GetSegment(occurrence.Id);
                Assert.That(segment, Is.Not.Null);
                Assert.That(kind == "interpolated" ? segment!.KnownRawInterpolatedTextIsSafe : segment!.KnownDirectiveConditionsAreSafe, Is.EqualTo(!unsafeText));
                Assert.That(kind == "interpolated" ? supplemental.HasUnsafeOrUnavailableKnownInterpolatedText : supplemental.HasUnsafeOrUnavailableKnownDirectiveConditions, Is.EqualTo(unsafeText));
                if (kind == "directive") { Assert.That(segment.ContainsDirectives, Is.True, "Known directives remain retention facts."); }
                else { Assert.That(segment.ContainsDirectives, Is.Null, "A segment without structural directives does not prove physical absence."); }
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1));
                    Assert.That(kind == "interpolated" ? segment.KnownRawInterpolatedTextIsSafe : segment.KnownDirectiveConditionsAreSafe, Is.EqualTo(!unsafeText));
                    Assert.That(kind == "interpolated" ? supplemental.HasUnsafeOrUnavailableKnownInterpolatedText : supplemental.HasUnsafeOrUnavailableKnownDirectiveConditions, Is.EqualTo(unsafeText));
                    if (kind == "directive") { Assert.That(segment.ContainsDirectives, Is.True); }
                    else { Assert.That(segment.ContainsDirectives, Is.Null); }
                    if (unsafeText) { Assert.That(file.Content, Does.Contain(kind == "interpolated" ? injectedLiteral : injectedDirective)); }
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [Test]
        public void RealFooPocInterpolatedArgumentObservationStaysVetoedAfterRestoration()
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]? holes = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, beforePreparation: owner =>
            {
                holes = [new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1)];
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.FormattableStringExpression("{0}", holes)
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticInterpolatedCurrentness", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (_, _, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)!;
                Assert.That(segment.KnownRawInterpolatedTextIsSafe, Is.True);
                var original = holes![0];
                holes[0] = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(2);
                Assert.That(segment.KnownRawInterpolatedTextIsSafe, Is.False);
                holes[0] = original;
                Assert.That(segment.KnownRawInterpolatedTextIsSafe, Is.False, "An observed hole mutation remains stale after restoration.");
                Assert.That(source.IsCurrent(), Is.True, "The POC-only segment query must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [Test]
        public void RealFooPocStructuralDirectivePresenceBecomesUnknownAfterObservedRestoration()
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            Microsoft.TypeSpec.Generator.Expressions.KeywordExpression? keyword = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                keyword = new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("break", null);
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.TryExpression(new Microsoft.TypeSpec.Generator.Statements.IfElsePreprocessorStatement(
                        "DEBUG", new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(keyword)))
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticDirectiveCurrentness", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)!;
                Assert.That(segment.ContainsDirectives, Is.True);
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1));
                    Assert.That(segment.ContainsDirectives, Is.True);
                    keyword!.Update("continue", null);
                    Assert.That(segment.ContainsDirectives, Is.Null);
                    keyword.Update("break", null);
                    Assert.That(segment.ContainsDirectives, Is.Null, "A stale structural presence must remain unknown after restoration.");
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(source.IsCurrent(), Is.True, "The POC-only segment query does not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [Test]
        public void RealFooPocSupplementalRawTextCannotProveDirectiveAbsence()
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            const string injected = "raw\";\n#if DEBUG\n#endif\n//";
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.FormattableStringExpression(injected, Array.Empty<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>())
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticRawDirective", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id);
                Assert.That(segment, Is.Not.Null);
                Assert.That(segment!.KnownRawInterpolatedTextIsSafe, Is.False);
                Assert.That(segment.ContainsDirectives, Is.Null, "Unsafe raw text cannot establish physical directive absence.");
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(segment.Consumed, Is.EqualTo(1));
                    Assert.That(segment.ContainsDirectives, Is.Null);
                    Assert.That(file.Content, Does.Contain("\n#if DEBUG\n#endif\n"), "Test-only output confirms raw directive text is emitted.");
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        [TestCase("oversized")]
        [TestCase("unknown")]
        public void RealFooPocSupplementalUnavailableNestedOperatorVetoesWithoutReplayingCallbacks(string mode)
        {
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? syntheticCall = null;
            CountingOperatorOperand? callback = null;
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true, beforePreparation: owner =>
            {
                Microsoft.TypeSpec.Generator.Expressions.ValueExpression left = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1);
                if (mode == "unknown") { callback = new CountingOperatorOperand(left); left = callback; }
                var arguments = new Microsoft.TypeSpec.Generator.Expressions.ValueExpression[]
                {
                    new Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression(mode == "oversized" ? new string('+', 5000) : "+",
                        left, new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(2))
                };
                syntheticCall = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "SyntheticCall", Microsoft.TypeSpec.Generator.Tests.Common.ArgumentCarrierFactory.Create(arguments));
                ConstructedArgumentWitness.Record(syntheticCall, arguments);
                var signature = new MethodSignature("SyntheticUnavailableOperator", null, MethodSignatureModifiers.Private, null, null, []);
                var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(syntheticCall), owner, new XmlDocProvider());
                owner.Update(methods: owner.Methods.Append(method));
            }, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, syntheticCall));
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                Assert.That(occurrence.IsOpaque, Is.True);
                Assert.That(supplemental.GetSegment(occurrence.Id), Is.Null, "An incomplete supplemental segment cannot acquire a receipt.");
                Assert.That(callback?.CallbackCount ?? 0, Is.Zero, "POC planning must not invoke a custom operand callback.");
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawOperators, Is.True, "Unknown nested operators must veto rather than appear safe.");
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawKeywords, Is.True, "An incomplete subtree cannot establish a keyword fact either.");
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownInterpolatedText, Is.True);
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownDirectiveConditions, Is.True);
                using (var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator())
                {
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var file = writer.Write();
                    prepared.EndPocProviderWrite(owner, writer, file);
                    Assert.That(supplemental.GetSegment(occurrence.Id), Is.Null);
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawOperators, Is.True);
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawKeywords, Is.True);
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownInterpolatedText, Is.True);
                    Assert.That(supplemental.HasUnsafeOrUnavailableKnownDirectiveConditions, Is.True);
                    Assert.That(callback?.CallbackCount ?? 0, Is.EqualTo(mode == "unknown" ? 1 : 0), "Only the native write invokes the operand callback.");
                    Assert.That(file.Content, Does.Contain("private readonly ClientType _client;"));
                }
                Assert.That(prepared.CanCommit(), Is.True);
            });
        }

        private sealed record CountingOperatorOperand(Microsoft.TypeSpec.Generator.Expressions.ValueExpression Value)
            : Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider(Value)
        {
            internal int CallbackCount { get; private set; }
            protected internal override bool IsEmptyExpression() { CallbackCount++; return false; }
        }

        [Test]
        public void RealFooPocReceiptQueryRejectsChangedSupplementalNode()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node =>
                    node.IsOpaque && node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider));
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                var segment = supplemental.GetSegment(occurrence.Id)!;
                var wrapper = (Microsoft.TypeSpec.Generator.Snippets.ScopedApi)occurrence.Producer;
                var original = wrapper.Original;
                try
                {
                    wrapper.Update(original: new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed"));
                    Assert.That(segment.WasConsumedExactlyOnce, Is.False);
                }
                finally { wrapper.Update(original: original); }
                Assert.That(supplemental.IsRejected, Is.True, "A receipt query observes the changed supplemental node even before completion.");
                Assert.That(segment.WasConsumedExactlyOnce, Is.False);
                Assert.That(source.IsCurrent(), Is.True, "The additional POC query must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True, "The POC-only veto must not discard an independent field.");
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocSupplementalRejectsChangedAndRestoredWrapper()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node =>
                    node.IsOpaque && node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider));
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)!;
                var wrapper = (Microsoft.TypeSpec.Generator.Snippets.ScopedApi)occurrence.Producer;
                var original = wrapper.Original;
                Assert.That(segment.IsCurrent, Is.True);
                wrapper.Update(original: new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed"));
                Assert.That(segment.IsCurrent, Is.False);
                wrapper.Update(original: original);
                Assert.That(segment.IsCurrent, Is.False, "An observed wrapper mutation must remain sticky after restoration.");
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only supplemental veto must not veto an independent field.");
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocSupplementalRejectsReplacementWithoutEnumeratingIt()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node => node.IsOpaque &&
                    node.Producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call &&
                    call.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync");
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)!;
                var call = (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)occurrence.Producer;
                var original = call.Arguments;
                call.Arguments = new ThrowOnArgumentEnumeration();
                Assert.DoesNotThrow(() => Assert.That(segment.IsCurrent, Is.False));
                call.Arguments = original;
                Assert.That(segment.IsCurrent, Is.False, "A changed carrier cannot regain its construction witness.");
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocSupplementalObservesMutationInsideSourceCallback()
        {
            Microsoft.TypeSpec.Generator.Snippets.ScopedApi? wrapper = null;
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node =>
                    node.IsOpaque && node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider));
                var segment = prepared.GetPocSupplementalFile(source)!.GetSegment(occurrence.Id)!;
                wrapper = (Microsoft.TypeSpec.Generator.Snippets.ScopedApi)occurrence.Producer;
                original = wrapper.Original;
                armed = true;
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                armed = false;
                Assert.That(changed && restored, Is.True, "The existing source callback must change and restore the actual wrapper.");
                Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(segment.IsCurrent, Is.False, "A wrapper changed by an observed callback must remain stale.");
                Assert.That(prepared.CanCommit(), Is.True, "The POC-only supplemental veto must not discard an independent field.");
            }, precomputePoc: true, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var inDocumentation = new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "WriteXmlDocsNoScope");
                if (!changed && inDocumentation)
                {
                    wrapper!.Update(original: new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("changed"));
                    changed = true;
                }
                else if (changed && !restored && !inDocumentation)
                {
                    wrapper!.Update(original: original!);
                    restored = true;
                }
                return true;
            });
        }

        [Test]
        public void PocSupplementRejectsUnregisteredDerivedWrapperWithoutCallingIt()
        {
            WithRealFoo((_, _) =>
            {
                var call = new Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression(null, "Call", System.Array.Empty<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>());
                var wrapper = new UnregisteredResponseWrapper(call);
                Assert.That(ConstructedArgumentWitness.IsAuditedWrapper(wrapper), Is.False);
                Assert.DoesNotThrow(() => Assert.That(StructuredSourceShape.CaptureForPocSupplement(wrapper, disableXmlDocs: false).Opaque, Is.True));
                Assert.That(wrapper.CallbackCount, Is.Zero);
            }, precomputePoc: true);
        }

        private sealed record UnregisteredResponseWrapper(Microsoft.TypeSpec.Generator.Expressions.ValueExpression Value)
            : Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider(Value)
        {
            internal int CallbackCount { get; private set; }
            protected internal override bool IsEmptyExpression() { CallbackCount++; throw new InvalidOperationException("Unknown wrapper must not be executed."); }
        }

        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("out-of-order")]
        [TestCase("wrong-nesting")]
        public void RealFooPocSupplementalRejectsIncorrectBoundaryEvents(string mode)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true,
                inspectGeneration: (output, referenceMap, owner, prepared) =>
                {
                    var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                    var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node =>
                        node.IsOpaque && node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider));
                    var supplemental = prepared.GetPocSupplementalFile(source)!;
                    var segment = supplemental.GetSegment(occurrence.Id)!;
                    var events = segment.Boundaries;
                    Assert.That(events.Length, Is.GreaterThan(2));
                    using var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner), "This test-only root ordering must write Foo first.");
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    var cursor = supplemental.Start(prepared, writer);
                    Assert.That(cursor, Is.Not.Null);
                    var root = cursor!.Enter(events[0].Producer, occurrence);
                    if (mode == "duplicate")
                    {
                        using var duplicate = cursor.Enter(events[0].Producer, occurrence);
                    }
                    else if (mode == "out-of-order")
                    {
                        var later = events.Skip(2).First(boundary => !ReferenceEquals(boundary.Producer, events[1].Producer));
                        using var misplaced = cursor.Enter(later.Producer, occurrence);
                    }
                    else if (mode == "wrong-nesting")
                    {
                        using var child = cursor.Enter(events[1].Producer, occurrence);
                        root.Dispose();
                    }
                    root.Dispose();
                    Assert.That(supplemental.IsRejected, Is.True, mode);
                    Assert.That(segment.WasConsumedExactlyOnce, Is.False);
                    Assert.That(prepared.CanCommit(), Is.True, "A POC-only event mismatch must not veto an independent field.");
                });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SupplementalObservationDoesNotInvalidateSharedReceiptForRestoredSibling(bool precomputePoc)
        {
            Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[]? statements = null;
            Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                statements = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Select(node => node.Producer)
                    .OfType<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatements>()
                    .Select(body => body.Statements).OfType<Microsoft.TypeSpec.Generator.Statements.MethodBodyStatement[]>()
                    .First(array => array.Length > 0);
                original = statements[0];
                armed = true;
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                armed = false;
                Assert.That(changed && restored, Is.True, "Two existing source callbacks must mutate and restore the sibling array.");
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True,
                    "A POC-only extra check must not invalidate shared traversal when the baseline sees the original array.");
                if (precomputePoc) { Assert.That(prepared.GetPocSupplementalFile(source)?.IsRejected, Is.True, "The POC-only observation still records the divergence as a sticky veto."); }
                Assert.That(prepared.CanCommit(), Is.True, "The POC-only observation must not discard an independent field.");
            }, precomputePoc: precomputePoc, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var frames = new System.Diagnostics.StackTrace().GetFrames();
                var duringDirectSnapshot = frames.Any(frame => frame.GetMethod()?.Name == "WriteType") &&
                    frames.Any(frame => frame.GetMethod()?.Name == "ObserveSnapshot") &&
                    frames.Any(frame => frame.GetMethod()?.Name == "CheckSnapshotCurrent");
                if (!changed && duringDirectSnapshot)
                {
                    statements![0] = new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(
                        new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("default", null));
                    changed = true;
                }
                else if (changed && !restored && !duringDirectSnapshot)
                {
                    statements![0] = original!;
                    restored = true;
                }
                return true;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocSupplementalRejectsWrongWriterAndIncompleteStream(bool throwDuringWrite)
        {
            WithRealFoo((_, _) => { }, precomputePoc: true, pocFooFirst: true,
                inspectGeneration: (output, referenceMap, owner, prepared) =>
                {
                    var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                    var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).First(node =>
                        node.IsOpaque && node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider));
                    var supplemental = prepared.GetPocSupplementalFile(source)!;
                    var segment = supplemental.GetSegment(occurrence.Id)!;
                    using var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    var otherWriter = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    prepared.BeginPocProviderWrite(owner, writer);
                    Assert.That(supplemental.Start(prepared, otherWriter), Is.Null, "A different writer for the same owner/path cannot claim the observation.");
                    if (throwDuringWrite)
                    {
                        Assert.Throws<InvalidOperationException>(() => writer.WriteWithMaterializedHeader((_, _, _) => throw new InvalidOperationException("test stop")));
                    }
                    else
                    {
                        var file = writer.Write();
                        Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                        prepared.EndPocProviderWrite(owner, writer, file);
                    }
                    stream.Dispose();
                    Assert.That(segment.WasConsumedExactlyOnce, Is.False, "An incomplete or exceptional stream cannot confirm the receipt.");
                    Assert.That(prepared.CanCommit(), Is.True, "A POC-only ownership veto must not discard an independent field.");
                });
        }

        [Test]
        public void RealFooPocSupplementalPreservesWriterOutputAndSourceCallbackOrder()
        {
            (string Content, string? Qualified, string[] Callbacks) Run(bool precomputePoc)
            {
                var callbacks = new List<string>();
                var armed = false;
                string? content = null, qualified = null;
                WithRealFoo((_, _) => { }, precomputePoc: precomputePoc, pocFooFirst: true, sourceCurrent: () =>
                {
                    if (armed)
                    {
                        var frames = new System.Diagnostics.StackTrace().GetFrames().Select(frame => frame.GetMethod())
                            .Where(method => method?.Name is "ObserveSourceSnapshot" or "CheckSnapshotCurrent" or "BeginSourceRoot" or "EnterSourceNode" or "CompleteSourceFile" or "WriteXmlDocsNoScope")
                            .Select(method => $"{method!.DeclaringType?.Name}.{method.Name}");
                        callbacks.Add(string.Join(" > ", frames));
                    }
                    return true;
                }, inspectGeneration: (output, referenceMap, owner, prepared) =>
                {
                    using var stream = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                    Assert.That(stream.MoveNext(), Is.True);
                    Assert.That(stream.Current, Is.SameAs(owner));
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    if (precomputePoc) { prepared.BeginPocProviderWrite(owner, writer); }
                    armed = true;
                    var file = writer.Write();
                    armed = false;
                    if (precomputePoc) { prepared.EndPocProviderWrite(owner, writer, file); }
                    content = file.Content; qualified = file.QualifiedContent;
                });
                return (content!, qualified, callbacks.ToArray());
            }

            var baseline = Run(false);
            var planned = Run(true);
            Assert.That(baseline.Callbacks, Is.Not.Empty);
            Assert.That(planned.Callbacks, Is.EqualTo(baseline.Callbacks), "The POC must not replay or reorder source-proof callbacks.");
            Assert.That(planned.Content, Is.EqualTo(baseline.Content));
            Assert.That(planned.Qualified, Is.EqualTo(baseline.Qualified));
        }

        [Test]
        public void RealFooPocReportsUnconfinedProducerKindsBeforeWriting()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var nodes = source.Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
                foreach (var group in nodes.Where(node => node.IsOpaque || node.IntrinsicSource.Tokens == SourceFactState.Unavailable)
                    .GroupBy(node => (node.Producer.GetType().FullName, node.IsOpaque)).OrderBy(group => group.Key.FullName))
                {
                    TestContext.Progress.WriteLine($"Foo IR: {group.Key.FullName} opaque={group.Key.IsOpaque}, count={group.Count()}");
                }
                Assert.That(nodes.Count(node => node.IsOpaque), Is.GreaterThanOrEqualTo(8));
                Assert.That(source.HasStarted, Is.False);
                var local = source.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                Assert.That(prepared.GetTypeOfNamePermission(source, local), Is.Null,
                    "A partial traversal observation does not establish full-file token or lexical confinement.");
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPreplansMemberDocumentationVeto(bool unsafeDocumentation)
        {
            XmlDocProvider? changed = null;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var docs = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Where(node => node.IsOpaque && node.Producer is XmlDocProvider).ToArray();
                Assert.That(docs, Has.Length.EqualTo(11));
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                Assert.That(supplemental.HasUnavailableDocumentation, Is.EqualTo(unsafeDocumentation));
                foreach (var node in docs)
                {
                    var observation = supplemental.GetDocumentation(node.Id);
                    if (ReferenceEquals(node.Producer, changed)) { Assert.That(observation, Is.Null); }
                    else { Assert.That(observation?.IsCurrent, Is.True, node.Id.ToString()); }
                }
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only documentation veto must not discard an independent field.");
            }, precomputePoc: true, beforePreparation: owner =>
            {
                if (!unsafeDocumentation) { return; }
                changed = owner.Methods.Select(method => method.XmlDocs).First(documentation => documentation.Summary is not null);
                changed.Update(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("safe")])
                { StartTag = FormattableStringFactory.Create("<summary>\nusing Poc.Unsafe;") });
            });
        }

        [Test]
        public void RealFooPocMemberDocumentationMutationIsStickyAndIndependent()
        {
            XmlDocProvider? docs = null;
            Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .First(node => node.IsOpaque && node.Producer is XmlDocProvider { Summary: not null });
                docs = (XmlDocProvider)occurrence.Producer;
                original = docs.Summary;
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                Assert.That(supplemental.GetDocumentation(occurrence.Id)?.IsCurrent, Is.True);
                armed = true;
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                armed = false;
                Assert.That(changed && restored, Is.True);
                Assert.That(supplemental.IsRejected, Is.True, "A member-doc mutation observed by the existing callback is sticky.");
                Assert.That(supplemental.GetDocumentation(occurrence.Id)?.IsCurrent, Is.False);
                Assert.That(output.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True, "The POC-only doc veto must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True, "The POC-only doc veto must not discard an independent field.");
            }, precomputePoc: true, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var inDocumentation = new System.Diagnostics.StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "WriteXmlDocsNoScope");
                if (!changed && inDocumentation)
                {
                    docs!.Update(summary: new Microsoft.TypeSpec.Generator.Statements.XmlDocSummaryStatement([FormattableStringFactory.Create("changed")]));
                    changed = true;
                }
                else if (changed && !restored && !inDocumentation)
                {
                    docs!.Update(summary: original!);
                    restored = true;
                }
                return true;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocShapeRecheckDoesNotThrowForTemporarilyMalformedSibling(bool precomputePoc)
        {
            Microsoft.TypeSpec.Generator.Expressions.MemberExpression? member = null;
            CodeWriterDeclaration? original = null;
            var armed = false;
            var changed = false;
            var restored = false;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                member = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Select(node => node.Producer)
                    .OfType<Microsoft.TypeSpec.Generator.Expressions.MemberExpression>().First();
                original = member.Declaration;
                armed = true;
                CodeFile? output = null;
                Assert.DoesNotThrow(() => output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write());
                armed = false;
                Assert.That(changed && restored, Is.True);
                Assert.That(output!.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                if (precomputePoc) { Assert.That(prepared.GetPocSupplementalFile(source)?.IsRejected, Is.True); }
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: precomputePoc, sourceCurrent: () =>
            {
                if (!armed) { return true; }
                var frames = new System.Diagnostics.StackTrace().GetFrames();
                var duringDirectSnapshot = frames.Any(frame => frame.GetMethod()?.Name == "WriteType") &&
                    frames.Any(frame => frame.GetMethod()?.Name == "ObserveSnapshot") &&
                    frames.Any(frame => frame.GetMethod()?.Name == "CheckSnapshotCurrent");
                if (!changed && duringDirectSnapshot)
                {
                    member!.Declaration = new CodeWriterDeclaration(null!);
                    changed = true;
                }
                else if (changed && !restored && !duringDirectSnapshot)
                {
                    member!.Declaration = original;
                    restored = true;
                }
                return true;
            });
        }

        [Test]
        public void RealFooPocReportsGenerationConfinementGapsBeforeWriting()
        {
            WithRealFoo((_, prepared) =>
            {
                var expectedField = typeof(PreparedFieldTypes).GetField("_pocExpectedProviders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var expected = (ImmutableArray<TypeProvider>)expectedField.GetValue(prepared)!;
                var sources = prepared.SourceFiles;
                Assert.That(expected, Is.Not.Empty);
                Assert.That(sources.All(file => !file.HasStarted), Is.True);
                var absent = expected.Where(provider => !sources.Any(file => ReferenceEquals(file.FileOwner, provider))).ToArray();
                var inventory = prepared.PocGenerationRawInventory;
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(PreparedPocGenerationRawInventory.FileObservation);
                var plannedNames = type.GetField("_unsupportedVisibleRawName", flags)!;
                var plannedFragments = type.GetField("_unsupportedVisibleRawFragment", flags)!;
                var captured = type.GetField("_captured", flags)!;
                var observations = expected.Select(provider => inventory?.GetFile(provider)).ToArray();
                var unavailable = observations.Count(value => value is null || captured.GetValue(value) is not true);
                var nameVetoes = observations.Count(value => value is not null && plannedNames.GetValue(value) is true);
                var fragmentVetoes = observations.Count(value => value is not null && plannedFragments.GetValue(value) is true);
                TestContext.Progress.WriteLine($"POC generation IR: written-providers={expected.Length}, captured-files={sources.Length}, missing-file-snapshots={absent.Length}, raw-inventory-unavailable={unavailable}, planned-name-vetoes={nameVetoes}, planned-fragment-vetoes={fragmentVetoes}");
                foreach (var provider in absent) { TestContext.Progress.WriteLine($"POC missing file snapshot: {provider.GetType().FullName}"); }
                foreach (var file in sources)
                {
                    var nodes = file.Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
                    var owner = file.FileOwner;
                    string Dispatch(string name) => owner.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null)?.DeclaringType?.Name ?? "unknown";
                    TestContext.Progress.WriteLine($"POC header IR: {file.FilePath}, owner={owner.GetType().Name}, pinned-path={owner.PinnedRelativeFilePath is not null}, " +
                        $"type={owner.CachedType is not null}, modifiers={owner.CachedDeclarationModifiers is not null}, interfaces={owner.CachedImplements is not null}, " +
                        $"attributes={owner.CachedWriteAttributes is not null}, warnings={owner.CachedDisabledFileWarnings is not null}, docs={owner.CachedXmlDocs is not null}, " +
                        $"where={owner.CachedWhereClause is not null}, where-dispatch={Dispatch("BuildWhereClause")}, docs-dispatch={Dispatch("get_ShouldWriteTypeXmlDocs")}, " +
                        $"attribute-dispatch={Dispatch("get_ShouldAnalyzeAttributesInReferenceMap")}");
                    var opaque = nodes.Where(node => node.IsOpaque).GroupBy(node => node.Producer.GetType().Name)
                        .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}");
                    var unknownTokens = nodes.Where(node => !node.IsOpaque && node.IntrinsicSource.Tokens == SourceFactState.Unavailable)
                        .GroupBy(node => node.Producer.GetType().Name).OrderBy(group => group.Key)
                        .Select(group => $"{group.Key}:{group.Count()}");
                    var observation = inventory?.GetFile(file.FileOwner);
                    var blockedNames = nodes.Where(node => !node.IsOpaque && node.Producer is
                            (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression or Microsoft.TypeSpec.Generator.Expressions.MemberExpression or MethodSignatureBase or
                                Microsoft.TypeSpec.Generator.Statements.AttributeStatement or FieldProvider or ParameterProvider or PropertyProvider or Microsoft.TypeSpec.Generator.Statements.DeclareLocalFunctionStatement))
                        .Select(node => (Node: node, Shape: StructuredSourceShape.CaptureForPocSupplement(node.Producer, file.DisableXmlDocs)))
                        .Where(pair => pair.Shape.Opaque || !pair.Shape.IntrinsicTokens).ToArray();
                    var nameBlockers = blockedNames.GroupBy(pair => pair.Node.Producer.GetType().Name).OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}");
                    foreach (var (node, shape) in blockedNames)
                    {
                        var token = node.Producer switch
                        {
                            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call => call.MethodSignature?.Name ?? call.MethodName,
                            MethodSignatureBase signature => signature.Name,
                            ParameterProvider parameter => parameter.Name,
                            PropertyProvider property => property.Name,
                            FieldProvider field => field.Name,
                            _ => "<other>"
                        };
                        var typeName = node.Producer switch
                        {
                            MethodSignatureBase signature => signature.ReturnType?.Name,
                            ParameterProvider parameter => parameter.Type?.Name,
                            PropertyProvider property => property.Type?.Name,
                            FieldProvider field => field.Type?.Name,
                            _ => null
                        };
                        TestContext.Progress.WriteLine($"POC name blocker: {file.FilePath}, kind={node.Producer.GetType().Name}, token={token}, type={typeName ?? "<none>"}, reason={shape.UnavailableReason ?? "unproved intrinsic tokens"}");
                    }
                    foreach (var node in nodes.Where(node => !node.IsOpaque && node.Producer is Microsoft.TypeSpec.Generator.Expressions.KeywordExpression))
                    {
                        var shape = StructuredSourceShape.CaptureForPocSupplement(node.Producer, file.DisableXmlDocs);
                        if (shape.Opaque || !shape.IntrinsicTokens)
                        { TestContext.Progress.WriteLine($"POC keyword blocker: {file.FilePath}, token={((Microsoft.TypeSpec.Generator.Expressions.KeywordExpression)node.Producer).Keyword}, reason={shape.UnavailableReason ?? "unproved intrinsic tokens"}"); }
                    }
                    TestContext.Progress.WriteLine($"POC file IR: {file.FilePath}, roots={file.Roots.Length}, planned-name-veto={(observation is null ? "unavailable" : plannedNames.GetValue(observation))}, planned-fragment-veto={(observation is null ? "unavailable" : plannedFragments.GetValue(observation))}, visible-name-blockers=[{string.Join(",", nameBlockers)}], opaque=[{string.Join(",", opaque)}], unknown-token-facts=[{string.Join(",", unknownTokens)}]");
                    var operators = nodes.Where(node => !node.IsOpaque && node.Producer is
                        (Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression or Microsoft.TypeSpec.Generator.Expressions.UnaryOperatorExpression)).ToArray();
                    if (operators.Length != 0)
                    {
                        var known = operators.Count(node => StructuredSourceShape.CaptureForPocSupplement(node.Producer, file.DisableXmlDocs).IntrinsicTokens);
                        TestContext.Progress.WriteLine($"POC IR operators: {file.FilePath}, known-raw={known}, unavailable={operators.Length - known}");
                    }
                    foreach (var directive in nodes.Where(node => !node.IsOpaque).Select(node => node.Producer).OfType<Microsoft.TypeSpec.Generator.Statements.IfElsePreprocessorStatement>())
                    {
                        var facts = StructuredSourceShape.CaptureForPocSupplement(directive, file.DisableXmlDocs);
                        TestContext.Progress.WriteLine($"POC IR directive: {file.FilePath}, known-condition={facts.IntrinsicTokens}, retains-directives={facts.HasDirectives}");
                    }
                    foreach (var format in nodes.Where(node => !node.IsOpaque).Select(node => node.Producer).OfType<Microsoft.TypeSpec.Generator.Expressions.FormattableStringExpression>())
                    {
                        var holes = 0;
                        var simpleLiteralParts = true;
                        var numericHoles = true;
                        var quote = false; var slash = false; var brace = false; var control = false; var nonAscii = false;
                        foreach (var (span, isLiteral) in Microsoft.TypeSpec.Generator.Utilities.StringExtensions.GetFormattableStringFormatParts(format.Format))
                        {
                            if (isLiteral)
                            {
                                foreach (var character in span)
                                {
                                    simpleLiteralParts &= character is >= ' ' and <= '~' && character is not ('"' or '\\' or '{' or '}');
                                    quote |= character == '"'; slash |= character == '\\'; brace |= character is '{' or '}';
                                    control |= character < ' '; nonAscii |= character > '~';
                                }
                            }
                            else
                            {
                                holes++;
                                numericHoles &= !span.IsEmpty;
                                foreach (var character in span) { numericHoles &= character is >= '0' and <= '9'; }
                            }
                        }
                        var facts = StructuredSourceShape.CaptureForPocSupplement(format, file.DisableXmlDocs);
                        TestContext.Progress.WriteLine($"POC IR format: {file.FilePath}, known-literal={facts.IntrinsicTokens}, simple-literals={simpleLiteralParts}, numeric-holes={numericHoles}, holes={holes}, args={format.Arguments.Count}, quote={quote}, backslash={slash}, brace={brace}, control={control}, non-ascii={nonAscii}");
                    }
                }
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPreplansKnownRawNameVeto(bool unsafeComment)
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var supplemental = prepared.GetPocSupplementalFile(source);
                Assert.That(supplemental, Is.Not.Null, "The raw-name veto must be captured before writing.");
                Assert.That(supplemental!.HasUnsafeOrUnavailableKnownRawNames, Is.EqualTo(unsafeComment));
                Assert.That(source.HasStarted, Is.False);
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only raw-name veto must not discard an independent field.");
            }, precomputePoc: true, beforePreparation: owner =>
            {
                if (unsafeComment)
                {
                    owner.Methods.First(method => method.Signature.Name == "Three").Signature.Update(nonDocumentComment: "safe\npublic class Escaped {}");
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocPreplansGenerationRawInventory(bool injectInOtherFile)
        {
            TypeProvider? other = null;
            WithRealFoo((owner, prepared) =>
            {
                var inventory = prepared.PocGenerationRawInventory;
                Assert.That(inventory, Is.Not.Null);
                Assert.That(inventory!.WrittenFileCount, Is.EqualTo(39));
                Assert.That(inventory.HasUncoveredEmission, Is.True, "Opaque headers and other emitters are not a confinement proof.");
                Assert.That(other, Is.Not.Null);
                Assert.That(inventory.GetFile(other!)?.HasUnsupportedVisibleRawName, Is.EqualTo(injectInOtherFile));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True, "Generation raw vetoes must not discard an independent field.");
                if (injectInOtherFile)
                {
                    var nativeOutput = new TypeProviderWriter(other!).Write();
                    Assert.That(nativeOutput.Content, Does.Contain("Call\npublic class Escaped {}"),
                        "The test-only writer verifies the mutated cross-file branch is emitted; planning never reads it.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                if (!injectInOtherFile) { return; }
                foreach (var root in other.Methods.Cast<object>().Concat(other.Constructors).Concat(other.Properties).Concat(other.Fields))
                {
                    if (FindRawNameProducer(root, "call", new HashSet<object>(ReferenceEqualityComparer.Instance), 0)
                        is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call)
                    { call.Update(methodName: "Call\npublic class Escaped {}"); return; }
                }
                Assert.Fail("The control must mutate an existing raw call in a different written provider.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileRawAttributeKey(bool unsafeKey)
        {
            TypeProvider? other = null;
            Microsoft.TypeSpec.Generator.Statements.AttributeStatement? attribute = null;
            KeyValuePair<string, Microsoft.TypeSpec.Generator.Expressions.ValueExpression>[]? named = null;
            const string injected = "Name\n)] public class Escaped {} [Obsolete(";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, attribute));
                Assert.That(occurrence.IsOpaque, Is.False, "The control must exercise a visible attribute occurrence.");
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawName, Is.EqualTo(unsafeKey));
                if (!unsafeKey)
                {
                    var original = named![0];
                    named[0] = new(injected, original.Value);
                    Assert.That(observation!.HasUnsupportedVisibleRawName, Is.True);
                    named[0] = original;
                    Assert.That(observation.HasUnsupportedVisibleRawName, Is.True, "An observed key mutation stays vetoed after restoration.");
                }
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeKey)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms that the unsafe other-file key is emitted.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                named = [new KeyValuePair<string, Microsoft.TypeSpec.Generator.Expressions.ValueExpression>(unsafeKey ? injected : "SafeKey",
                    new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(true))];
                attribute = new Microsoft.TypeSpec.Generator.Statements.AttributeStatement(typeof(ObsoleteAttribute), named);
                var method = other.Methods.First(CodeWriter.HasMethodDeclaration);
                method.Signature.Update(attributes: method.Signature.Attributes.Append(attribute).ToArray());
            });
        }

        [Test]
        public void RealFooPocSupplementalFieldDeclarationVetoIsStickyAfterRestoration()
        {
            FieldProvider? field = null;
            const string injected = "field\n;} public class Escaped {} /*";
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var supplemental = prepared.GetPocSupplementalFile(source)!;
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.False);
                field!.Declaration.Update(name: injected);
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.True);
                field.Declaration.Update(name: "SyntheticField");
                Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.True);
                Assert.That(supplemental.IsRejected, Is.True);
                Assert.That(source.IsCurrent(), Is.True, "The POC-only veto must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true, beforePreparation: owner =>
            {
                field = new FieldProvider(FieldModifiers.Private, typeof(int), "SyntheticField", owner);
                owner.Update(fields: owner.Fields.Append(field));
                _ = field.Declaration;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocFieldDeclarationRevisionVetoesUnobservedRestoration(bool candidateFile)
        {
            TypeProvider? target = null;
            FieldProvider? field = null;
            WithRealFoo((owner, prepared) =>
            {
                target = candidateFile ? owner : target;
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, target));
                var observation = prepared.PocGenerationRawInventory!.GetFile(target!)!;
                var supplemental = candidateFile ? prepared.GetPocSupplementalFile(source) : null;
                Assert.That(observation.HasUnsupportedVisibleRawName, Is.False);
                if (supplemental is not null) { Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.False); }
                field!.Declaration.Update(name: "field\n;} public class Escaped {} /*");
                field.Declaration.Update(name: "SyntheticField");
                // No query occurred between mutation and restoration; the
                // declaration's own revision is the available witness.
                Assert.That(observation.HasUnsupportedVisibleRawName, Is.True);
                if (supplemental is not null) { Assert.That(supplemental.HasUnsafeOrUnavailableKnownRawNames, Is.True); }
                Assert.That(source.IsCurrent(), Is.True, "POC-only revision observations must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true, beforePreparation: owner =>
            {
                if (!candidateFile) { return; }
                target = owner;
                field = new FieldProvider(FieldModifiers.Private, typeof(int), "SyntheticField", owner);
                owner.Update(fields: owner.Fields.Append(field));
                _ = field.Declaration;
            }, beforePocPreparation: (output, referenceMap) =>
            {
                if (candidateFile) { return; }
                target = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                field = new FieldProvider(FieldModifiers.Private, typeof(int), "SyntheticField", target);
                target.Update(fields: target.Fields.Append(field));
                _ = field.Declaration;
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileIndexerParameter(bool unsafeName)
        {
            TypeProvider? other = null;
            IndexPropertyProvider? indexer = null;
            const string injected = "param\n] {} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, indexer));
                Assert.That(occurrence.IsOpaque, Is.False);
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawName, Is.EqualTo(unsafeName));
                if (!unsafeName)
                {
                    var variable = ParameterProvider.GetVariableExpression(indexer!.IndexerParameter);
                    Assert.That(observation!.HasUnsupportedVisibleRawName, Is.False, "Canonical lazy indexer materialization remains supported.");
                    variable.Update(name: injected);
                    variable.Update(name: "safeParam");
                    Assert.That(observation.HasUnsupportedVisibleRawName, Is.True, "Indexer declaration revisions veto unobserved restoration.");
                    Assert.That(source.IsInvalid, Is.False);
                }
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeName)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe indexer parameter is emitted.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                var parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
                if (unsafeName) { ParameterProvider.GetVariableExpression(parameter).Update(name: injected); }
                indexer = new IndexPropertyProvider(null, MethodSignatureModifiers.Public, typeof(int), parameter, new AutoPropertyBody(false), other);
                other.Update(properties: other.Properties.Append(indexer));
            });
        }

        [Test]
        public void RealFooPocParameterRevisionVetoesUnobservedRestorationAfterLazyMaterialization()
        {
            TypeProvider? other = null;
            ParameterProvider? parameter = null;
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var observation = prepared.PocGenerationRawInventory!.GetFile(other!)!;
                Assert.That(parameter!.CachedVariableExpression, Is.Null);
                Assert.That(observation.HasUnsupportedVisibleRawName, Is.False);
                var variable = ParameterProvider.GetVariableExpression(parameter);
                Assert.That(observation.HasUnsupportedVisibleRawName, Is.False, "Canonical writer-variable materialization remains supported.");
                variable.Update(name: "param\n) {} public class Escaped {} /*");
                variable.Update(name: "safeParam");
                Assert.That(observation.HasUnsupportedVisibleRawName, Is.True,
                    "The existing variable/declaration revisions observe change-and-restore without an intermediate query.");
                Assert.That(source.IsInvalid, Is.False, "The POC-only query must not invalidate shared traversal.");
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
                var method = other.Methods.First(CodeWriter.HasMethodDeclaration);
                method.Signature.Update(parameters: method.Signature.Parameters.Append(parameter).ToArray());
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileParameterDeclaration(bool unsafeName)
        {
            TypeProvider? other = null;
            ParameterProvider? parameter = null;
            const string injected = "param\n) {} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, parameter));
                Assert.That(occurrence.IsOpaque, Is.False);
                Assert.That(parameter!.Name, Is.EqualTo("safeParam"));
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawName, Is.EqualTo(unsafeName));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeName)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe parameter declaration is emitted.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                parameter = new ParameterProvider("safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
                if (unsafeName) { ParameterProvider.GetVariableExpression(parameter).Update(name: injected); }
                var method = other.Methods.First(CodeWriter.HasMethodDeclaration);
                method.Signature.Update(parameters: method.Signature.Parameters.Append(parameter).ToArray());
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileFieldDeclaration(bool unsafeName)
        {
            TypeProvider? other = null;
            FieldProvider? field = null;
            const string injected = "field\n;} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, field));
                Assert.That(occurrence.IsOpaque, Is.False);
                Assert.That(field!.Name, Is.Not.EqualTo(injected), "The writer reads a distinct declaration name for class fields.");
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawName, Is.EqualTo(unsafeName));
                if (!unsafeName)
                {
                    field.Declaration.Update(name: injected);
                    Assert.That(observation!.HasUnsupportedVisibleRawName, Is.True);
                    field.Declaration.Update(name: "SyntheticField");
                    Assert.That(observation.HasUnsupportedVisibleRawName, Is.True, "An observed declaration mutation stays vetoed after restoration.");
                }
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeName)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe field declaration is emitted.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                field = new FieldProvider(FieldModifiers.Private, typeof(int), "SyntheticField", other);
                other.Update(fields: other.Fields.Append(field));
                var declaration = field.Declaration; // Test setup materializes the canonical writer input.
                if (unsafeName) { declaration.Update(name: injected); }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileLocalFunctionParameter(bool unsafeName)
        {
            TypeProvider? other = null;
            Microsoft.TypeSpec.Generator.Statements.DeclareLocalFunctionStatement? local = null;
            const string injected = "param\n) {} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, local));
                Assert.That(occurrence.IsOpaque, Is.False);
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawName, Is.EqualTo(unsafeName));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeName)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the local function parameter is emitted raw.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                var parameter = new ParameterProvider(unsafeName ? injected : "safeParam", FormattableStringFactory.Create(string.Empty), typeof(int));
                local = new Microsoft.TypeSpec.Generator.Statements.DeclareLocalFunctionStatement(new CodeWriterDeclaration("Local"),
                    new[] { parameter }, typeof(int), new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1));
                other.Methods.First(CodeWriter.HasMethodDeclaration).Update(bodyStatements: local);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileRawKeyword(bool unsafeKeyword)
        {
            TypeProvider? other = null;
            Microsoft.TypeSpec.Generator.Expressions.KeywordExpression? keyword = null;
            const string injected = "return\n} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, keyword));
                Assert.That(occurrence.IsOpaque, Is.False);
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawFragment, Is.EqualTo(unsafeKeyword));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeKeyword)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe keyword text is emitted raw.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                keyword = new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression(unsafeKeyword ? injected : "return",
                    new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1));
                other.Methods.First(CodeWriter.HasMethodDeclaration)
                    .Update(bodyStatements: new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(keyword));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileRawOperator(bool unsafeOperator)
        {
            TypeProvider? other = null;
            Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression? binary = null;
            const string injected = "+\n} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, binary));
                Assert.That(occurrence.IsOpaque, Is.False);
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                Assert.That(observation?.HasUnsupportedVisibleRawFragment, Is.EqualTo(unsafeOperator));
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeOperator)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe operator is emitted raw.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                binary = new Microsoft.TypeSpec.Generator.Expressions.BinaryOperatorExpression(unsafeOperator ? injected : "+",
                    new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(1), new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression(2));
                other.Methods.First(CodeWriter.HasMethodDeclaration)
                    .Update(bodyStatements: new Microsoft.TypeSpec.Generator.Statements.ExpressionStatement(binary));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryVetoesOtherFileRawEnumField(bool unsafeName)
        {
            TypeProvider? other = null;
            FieldProvider? field = null;
            const string injected = "Name\n} public class Escaped {} /*";
            WithRealFoo((_, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, other));
                var occurrence = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, field));
                Assert.That(occurrence.IsOpaque, Is.False);
                var observation = prepared.PocGenerationRawInventory?.GetFile(other!);
                var planned = typeof(PreparedPocGenerationRawInventory.FileObservation)
                    .GetField("_unsupportedVisibleRawName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(observation);
                Assert.That(planned, Is.EqualTo(unsafeName), "Test-only inspection isolates the preplanned enum field name.");
                Assert.That(prepared.SourceFiles.All(file => !file.HasStarted), Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                if (unsafeName)
                {
                    Assert.That(new TypeProviderWriter(other!).Write().Content, Does.Contain(injected),
                        "Test-only output confirms the unsafe enum field name is emitted raw.");
                }
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<FixedEnumProvider>().Single(provider => provider.CachedType?.Name == "ClientType");
                field = other.Fields.First();
                if (unsafeName) { field.Update(name: injected); }
            });
        }

        [Test]
        public void RealFooPocGenerationRawInventoryRejectsDuplicateProviderFacts()
        {
            WithRealFoo((owner, prepared) =>
            {
                var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance);
                foreach (var file in prepared.SourceFiles) { sources.Add(file.FileOwner, file); }
                var single = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner), sources);
                Assert.That(single.GetFile(owner)?.HasUnsupportedVisibleRawName, Is.False,
                    "The duplicate control needs an otherwise known visible-name fact.");
                var duplicate = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner, owner), sources);
                Assert.That(duplicate.GetFile(owner)?.HasUnsupportedVisibleRawName is null or true, Is.True,
                    "A duplicate planned provider cannot retain a positive file fact.");
                Assert.That(duplicate.GetFile(owner), Is.Null, "Duplicate streams must clear, not merely veto, the file inventory.");
                Assert.That(duplicate.HasUncoveredEmission, Is.True);
                var other = prepared.SourceFiles.First(file => !ReferenceEquals(file.FileOwner, owner)).FileOwner;
                var missingSources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance) { [owner] = sources[owner] };
                var missingAfterValid = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner, other), missingSources);
                Assert.That(missingAfterValid.GetFile(owner), Is.Null, "A later missing file must clear earlier observations.");
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocGenerationRawInventoryClearsEarlierFilesOnWorkExhaustion()
        {
            WithRealFoo((owner, prepared) =>
            {
                var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance);
                foreach (var file in prepared.SourceFiles) { sources.Add(file.FileOwner, file); }
                var other = sources.Keys.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                var before = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner), sources);
                Assert.That(before.GetFile(owner)?.HasUnsupportedVisibleRawName, Is.False);
                var call = other.Methods.Cast<object>().Concat(other.Constructors).Concat(other.Properties).Concat(other.Fields)
                    .Select(root => FindRawNameProducer(root, "call", new HashSet<object>(ReferenceEqualityComparer.Instance), 0))
                    .OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>().First();
                var original = call.MethodName!;
                call.Update(methodName: new string('A', 270000));
                // The additional inventory capture sees this long value on
                // the canonical producer referenced by the shared graph.
                var exhausted = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner, other), sources);
                Assert.That(exhausted.GetFile(owner), Is.Null, "A later budget exhaustion must clear earlier file facts.");
                call.Update(methodName: original);
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooPocGenerationRawInventoryRejectsMalformedOwnedMapValues(bool nullValue)
        {
            WithRealFoo((owner, prepared) =>
            {
                var ownerFile = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var otherFile = prepared.SourceFiles.First(file => !ReferenceEquals(file.FileOwner, owner));
                var sources = new Dictionary<TypeProvider, PreparedSourceFile>(ReferenceEqualityComparer.Instance)
                {
                    [owner] = nullValue ? null! : otherFile
                };
                var malformed = PreparedPocGenerationRawInventory.Capture(ImmutableArray.Create(owner), sources);
                Assert.That(malformed.GetFile(owner), Is.Null, "Null or mismatched file ownership cannot yield a visible-name fact.");
                Assert.That(malformed.HasUncoveredEmission, Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
                Assert.That(ownerFile.HasStarted, Is.False);
            }, precomputePoc: true);
        }

        [Test]
        public void RealFooPocGenerationRawInventoryVetoRemainsStickyAfterRestoration()
        {
            TypeProvider? other = null;
            Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression? call = null;
            WithRealFoo((_, prepared) =>
            {
                var file = prepared.PocGenerationRawInventory!.GetFile(other!);
                Assert.That(file, Is.Not.Null);
                Assert.That(file!.HasUnsupportedVisibleRawName, Is.False);
                var name = call!.MethodName!;
                call.Update(methodName: "Call\npublic class Escaped {}");
                Assert.That(file.HasUnsupportedVisibleRawName, Is.True);
                call.Update(methodName: name);
                Assert.That(file.HasUnsupportedVisibleRawName, Is.True);
                Assert.That(prepared.CanCommit(), Is.True);
            }, precomputePoc: true, beforePocPreparation: (output, _) =>
            {
                other = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Bar");
                foreach (var root in other.Methods.Cast<object>().Concat(other.Constructors).Concat(other.Properties).Concat(other.Fields))
                {
                    if (FindRawNameProducer(root, "call", new HashSet<object>(ReferenceEqualityComparer.Instance), 0)
                        is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression found)
                    { call = found; return; }
                }
                Assert.Fail("The control must find a raw call in a different written provider.");
            });
        }

        [TestCase("member")]
        [TestCase("call")]
        public void RealFooPocPreplansRawExpressionNameVeto(string kind)
        {
            var changed = false;
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                Assert.That(changed, Is.True, "The control must mutate an existing emitted raw-name branch before planning.");
                Assert.That(prepared.GetPocSupplementalFile(source)?.HasUnsafeOrUnavailableKnownRawNames, Is.True);
                Assert.That(source.HasStarted, Is.False);
                Assert.That(prepared.CanCommit(), Is.True, "A POC-only raw-name veto must not discard an independent field.");
                // Test-only output assertion verifies the selected native writer
                // branch is actually emitted; it is not an input to planning.
                var output = new TypeProviderWriter(owner).Write();
                Assert.That(output.Content, Does.Contain(kind == "member" ? "Member\npublic class Escaped {}" : "Call\npublic class Escaped {}"));
            }, precomputePoc: true, beforePreparation: owner =>
            {
                foreach (var root in owner.Methods.Cast<object>().Concat(owner.Constructors).Concat(owner.Properties).Concat(owner.Fields))
                {
                    var found = FindRawNameProducer(root, kind, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
                    if (found is Microsoft.TypeSpec.Generator.Expressions.MemberExpression member)
                    {
                        // Exercise the raw-name branch even if this particular
                        // canonical member originally used a declaration.
                        member.Declaration = null;
                        member.Update(memberName: "Member\npublic class Escaped {}");
                        changed = true; break;
                    }
                    if (found is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression call)
                    { call.Update(methodName: "Call\npublic class Escaped {}"); changed = true; break; }
                }
            });
        }

        private static object? FindRawNameProducer(object producer, string kind, HashSet<object> ancestors, int depth)
        {
            if (depth >= 128 || !ancestors.Add(producer)) { return null; }
            try
            {
                var shape = StructuredSourceShape.Capture(producer, disableXmlDocs: false);
                if (shape.Opaque) { return null; }
                if (kind == "member" && producer is Microsoft.TypeSpec.Generator.Expressions.MemberExpression member) { return member; }
                if (kind == "call" && producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression { MethodSignature: null, MethodName: not null } call) { return call; }
                foreach (var child in shape.Children)
                {
                    if (FindRawNameProducer(child.Producer, kind, ancestors, depth + 1) is { } found) { return found; }
                }
                return null;
            }
            finally { ancestors.Remove(producer); }
        }

        [Test]
        public void RealFooConvenienceCallsStayOpaqueWithoutScopedProfile()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var calls = source.Roots
                    .Where(root => root.Id.RootRole == "method" && root.Id.RootIndex is 2 or 3 or 6 or 7)
                    .SelectMany(root => root.DescendantsAndSelf())
                    .Where(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression invocation &&
                        invocation.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync")
                    .ToArray();
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(calls.Length, Is.EqualTo(4));
                // The shared Task03 graph must not gain call authority merely to support a later file profile.
                Assert.That(calls.Count(node => node.IsOpaque), Is.EqualTo(4));
                Assert.That(calls.All(node => ((Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)node.Producer).Arguments
                    is not System.Collections.Immutable.ImmutableArray<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>), Is.True);
                Assert.That(file.Content, Does.Contain("return this.Three(cancellationToken.ToRequestOptions());"));
                Assert.That(file.Content, Does.Contain("return await this.ThreeAsync(cancellationToken.ToRequestOptions()).ConfigureAwait(false);"));
                Assert.That(file.Content, Does.Contain("return this.Four(cancellationToken.ToRequestOptions());"));
                Assert.That(file.Content, Does.Contain("return await this.FourAsync(cancellationToken.ToRequestOptions()).ConfigureAwait(false);"));
            });
        }

        [Test]
        public void CustomVisitorReceivesOriginalFooCallArgumentRepresentations()
        {
            var visitor = new FooArgumentRepresentationVisitor();
            WithRealFoo((_, _) => { }, additionalVisitor: visitor);
            Assert.That(visitor.Convenience.Count, Is.EqualTo(4));
            Assert.That(visitor.Pipeline.Count, Is.EqualTo(4));
            Assert.That(visitor.Convenience, Has.All.EqualTo(false), "Custom visitors must see the original convenience argument representation.");
            Assert.That(visitor.Pipeline, Has.All.EqualTo(false), "Custom visitors must see the original pipeline argument representation.");
        }

        [Test]
        public void RealFooVisitorSeesConstructionWitnessBeforeTraversalButNotAfterReplacement()
        {
            var visitor = new FooArgumentIdentityVisitor();
            WithRealFoo((_, _) => { }, additionalVisitor: visitor);
            Assert.That(visitor.Calls, Has.Count.EqualTo(8));
            foreach (var call in visitor.Calls)
            {
                Assert.That(call.WitnessOnVisit, Is.True, call.Method);
                Assert.That(ConstructedArgumentWitness.Read(call.Invocation).HasValue, Is.False,
                    $"The visitor-replaced argument carrier for {call.Method} must not retain the old witness.");
            }
        }

        [Test]
        public void RealFooVisitorArgumentRepresentationsAreObservedBeforeAndAfterTraversal()
        {
            var visitor = new FooArgumentIdentityVisitor();
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var nodes = source.Roots.SelectMany(root => root.DescendantsAndSelf()).Select(node => node.Producer).ToArray();
                var finalizedCalls = nodes.OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>()
                    .Concat(nodes.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider>()
                        .Select(wrapper => wrapper.Original).OfType<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>()).ToArray();
                Assert.That(visitor.Calls.Count(call => call.Kind == "convenience"), Is.EqualTo(4));
                Assert.That(visitor.Calls.Count(call => call.Kind == "pipeline"), Is.EqualTo(4));
                foreach (var call in visitor.Calls)
                {
                    Assert.That(finalizedCalls.Any(final => ReferenceEquals(final, call.Invocation)), Is.True, call.Method);
                    var after = call.Invocation.Arguments;
                    Assert.That(after, Is.InstanceOf<List<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>>());
                    Assert.That(after, Is.Not.SameAs(call.Before));
                    Assert.That(call.Invocation.DeclarationRevision, Is.GreaterThan(call.Revision));
                    var sameElements = call.Elements.Length == after.Count &&
                        call.Elements.Select((element, index) => ReferenceEquals(element, after[index])).All(same => same);
                    Assert.That(sameElements, Is.True, "A no-op native visitor must preserve argument element identities and order.");
                    TestContext.Progress.WriteLine($"Foo {call.Kind} {call.Method}: before={call.Before.GetType().FullName}#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(call.Before)}, after={after.GetType().FullName}#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(after)}, same-elements={sameElements}, revision={call.Revision}->{call.Invocation.DeclarationRevision}");
                }
            }, additionalVisitor: visitor);
        }

        [Test]
        public void CollectionExpressionProbeDocumentsActualArgumentCarriers()
        {
            var first = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("first");
            var second = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("second");
            var replacement = new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("replacement");
            var source = new List<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> { first, second };
            IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> spread = [.. source];
            IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> fixedElements = [first, second];
            source[0] = replacement;
            source.Add(replacement);
            Assert.That(spread.Count, Is.EqualTo(2));
            Assert.That(spread[0], Is.SameAs(first));
            Assert.That(spread[1], Is.SameAs(second));
            Assert.That(fixedElements[0], Is.SameAs(first));
            Assert.That(fixedElements[1], Is.SameAs(second));
            TestContext.Progress.WriteLine($"Disposable collection expression: spread={spread.GetType().FullName}; {ProbeArgumentListMutation(spread, replacement)}");
            TestContext.Progress.WriteLine($"Disposable collection expression: fixed={fixedElements.GetType().FullName}; {ProbeArgumentListMutation(fixedElements, replacement)}");
            // This runtime probe is not an ownership or immutability proof.
        }

        private static string ProbeArgumentListMutation(IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> arguments,
            Microsoft.TypeSpec.Generator.Expressions.ValueExpression replacement)
        {
            var outcomes = new List<string>();
            if (arguments is IList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> generic)
            {
                try { var original = generic[0]; generic[0] = replacement; generic[0] = original; outcomes.Add($"generic readonly={generic.IsReadOnly}, setter=accepted"); }
                catch (NotSupportedException) { outcomes.Add($"generic readonly={generic.IsReadOnly}, setter=rejected"); }
            }
            else { outcomes.Add("no generic IList"); }
            if (arguments is System.Collections.IList nongeneric)
            {
                try { var original = nongeneric[0]; nongeneric[0] = replacement; nongeneric[0] = original; outcomes.Add($"nongeneric readonly={nongeneric.IsReadOnly}, setter=accepted"); }
                catch (NotSupportedException) { outcomes.Add($"nongeneric readonly={nongeneric.IsReadOnly}, setter=rejected"); }
            }
            else { outcomes.Add("no nongeneric IList"); }
            return string.Join("; ", outcomes);
        }

        [Test]
        public void BuiltInFooWrapperTypesInheritStandardEmissionDispatch()
        {
            var baseType = typeof(Microsoft.TypeSpec.Generator.Snippets.ScopedApi);
            var types = new[]
            {
                typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider),
                typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientPipelineProvider),
                typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.RequestOptionsProvider),
                typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineMessageProvider),
                typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineRequestProvider)
            };
            Assert.That(baseType.GetProperty("Original")!.GetMethod!.IsVirtual, Is.False);
            Assert.That(baseType.GetProperty("Type")!.GetMethod!.IsVirtual, Is.False);
            foreach (var type in types)
            {
                Assert.That(type.Assembly, Is.SameAs(typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider).Assembly));
                var current = type;
                while (current != baseType)
                {
                    Assert.That(current, Is.Not.Null);
                    var declarations = current.GetMethods(System.Reflection.BindingFlags.DeclaredOnly |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    Assert.That(declarations.Any(method => method.Name is "WriteCore" or "IsEmptyExpression"), Is.False, current.FullName);
                    current = current.BaseType!;
                }
            }
        }

        [Test]
        public void RealFooPipelineResponseWrappersStayOpaqueWithoutScopedProfile()
        {
            WithRealFoo((owner, prepared) =>
            {
                var source = prepared.SourceFiles.Single(file => ReferenceEquals(file.FileOwner, owner));
                var wrappers = source.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Where(node => node.Producer.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider)).ToArray();
                Assert.That(wrappers.Length, Is.EqualTo(4));
                Assert.That(wrappers.All(node => node.IsOpaque), Is.True);
                foreach (var node in wrappers)
                {
                    var wrapper = (Microsoft.TypeSpec.Generator.ClientModel.Providers.PipelineResponseProvider)node.Producer;
                    Assert.That(wrapper.Original, Is.InstanceOf<Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression>());
                    var invocation = (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)wrapper.Original;
                    Assert.That(invocation.Arguments, Is.Not.InstanceOf<System.Collections.Immutable.ImmutableArray<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>>());
                    Assert.That(invocation.Arguments.Count, Is.EqualTo(2));
                }
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(file.Content, Does.Contain("ClientResult.FromResponse(Pipeline.ProcessMessage(message, options))"));
                Assert.That(file.Content, Does.Contain("ClientResult.FromResponse(await Pipeline.ProcessMessageAsync(message, options).ConfigureAwait(false))"));
            });
        }

        [Test]
        public void RealFooExplicitHeaderMaterializationReusesCanonicalObjectsAndPreservesRawOutput()
        {
            string? baseline = null;
            WithRealFoo((owner, prepared) =>
            {
                Assert.That(owner.CachedXmlDocs, Is.Null);
                Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
                baseline = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write().Content;
                Assert.That(owner.CachedXmlDocs, Is.Not.Null);
                Assert.That(owner.CachedDisabledFileWarnings, Is.Not.Null);
            });

            WithRealFoo((owner, prepared) =>
            {
                var docs = owner.CachedXmlDocs;
                var warnings = owner.CachedDisabledFileWarnings;
                Assert.That(docs, Is.Not.Null);
                Assert.That(warnings, Is.Not.Null);
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                Assert.That(file.Content, Is.EqualTo(baseline));
                Assert.That(owner.CachedXmlDocs, Is.SameAs(docs));
                Assert.That(owner.CachedDisabledFileWarnings, Is.SameAs(warnings));
            }, materializeHeaderBeforePreparation: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealFooTypeOnlyNamespaceLookupIncludesAllGeneratedRootsAndSourceShadow(bool shadow)
        {
            WithRealFoo((owner, prepared) =>
            {
                var sources = prepared.SourceFiles;
                var source = sources.Single(file => ReferenceEquals(file.FileOwner, owner));
                var site = source.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var target = ((Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression)site.Producer).Variable.Type;
                var compilation = shadow
                    ? source.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Client.Structure.Service { public class PipelineMessage {} }"))
                    : source.Compilation;
                var symbol = compilation.GetTypeByMetadataName("System.ClientModel.Primitives.PipelineMessage");
                Assert.That(symbol, Is.Not.Null);
                var generated = sources.Select(file => file.FileOwner)
                    .GroupBy(provider => (provider.CachedType!.Namespace, provider.CachedType.Name))
                    .ToDictionary(group => group.Key, group => group.ToArray());
                var prefixes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var provider in sources.Select(file => file.FileOwner))
                {
                    var ns = provider.CachedType!.Namespace;
                    while (ns.Length > 0 && prefixes.Add(ns))
                    {
                        var dot = ns.LastIndexOf('.');
                        ns = dot < 0 ? string.Empty : ns[..dot];
                    }
                }
                var lookup = new PreparedFieldImportLookup(compilation, generated, prefixes);
                var supported = lookup.TryPrepareTypeOnlyNamespaceLookup(owner.CachedType!.Namespace, target, symbol!, out var forbidden);
                Assert.That(supported, Is.EqualTo(!shadow));
                var hasPocCandidate = lookup.TryPreparePocBareNamespaceCandidate(owner.CachedType!.Namespace, target, symbol!, out var form, out var pocForbidden);
                Assert.That(hasPocCandidate, Is.EqualTo(!shadow));
                if (!shadow)
                {
                    Assert.That(forbidden, Is.Empty);
                    Assert.That(pocForbidden, Is.Empty);
                    var partCount = target.Namespace.Split('.').Length + 1;
                    Assert.That(form.Root, Is.EqualTo(PreparedPocNameForms.RootKind.Ordinary));
                    Assert.That(form.Start, Is.EqualTo(partCount - 1), "The POC selects the bare type first when that namespace lookup succeeds.");
                    Assert.That(form.Spelling, Is.EqualTo("PipelineMessage"));
                }
                // This is ranking and namespace lookup evidence, not a local-type permission.
            });
        }

        [Test]
        public void RealFooSourceShadowPresentBeforePreparationRetainsQualifiedLocal()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var site = file.Roots.Single(root => root.Id.RootRole == "method" && root.Id.RootIndex == 0)
                    .DescendantsAndSelf().Single(node => node.Producer is Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression);
                var target = ((Microsoft.TypeSpec.Generator.Expressions.DeclarationExpression)site.Producer).Variable.Type;
                var symbol = file.Compilation.GetTypeByMetadataName("System.ClientModel.Primitives.PipelineMessage");
                Assert.That(symbol, Is.Not.Null);
                var generated = prepared.SourceFiles.Select(source => source.FileOwner)
                    .GroupBy(provider => (provider.CachedType!.Namespace, provider.CachedType.Name))
                    .ToDictionary(group => group.Key, group => group.ToArray());
                var prefixes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var provider in prepared.SourceFiles.Select(source => source.FileOwner))
                {
                    var ns = provider.CachedType!.Namespace;
                    while (ns.Length > 0 && prefixes.Add(ns))
                    {
                        var dot = ns.LastIndexOf('.');
                        ns = dot < 0 ? string.Empty : ns[..dot];
                    }
                }
                var lookup = new PreparedFieldImportLookup(file.Compilation, generated, prefixes);
                Assert.That(lookup.TryPreparePocBareNamespaceCandidate(owner.CachedType!.Namespace, target, symbol!, out _, out _), Is.False);
                var output = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                var syntax = CSharpSyntaxTree.ParseText(output.Content).GetRoot();
                var method = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Single(item => item.Identifier.ValueText == "Three" && item.ParameterList.Parameters.Single().Identifier.ValueText == "options");
                var local = method.Body!.Statements.OfType<LocalDeclarationStatementSyntax>().Single();
                Assert.That(local.Declaration.Type.ToString(), Is.Not.EqualTo("PipelineMessage"),
                    "A source shadow must not cause the bare spelling to bind to the wrong type.");
            }, addFooShadow: true);
        }

        [Test]
        public void RealFooProspectiveLifetimeKeepsCanonicalRootsThroughWriterPause()
        {
            string? expected = null;
            WithRealFoo((owner, prepared) => expected = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write().Content);
            WithRealFoo((owner, prepared) =>
            {
                Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
                var roots = CodeModelGenerator.Instance.OutputLibrary.TypeProviders;
                TestContext.Progress.WriteLine($"Real Foo canonical root carrier: {roots.GetType().FullName}");
                Assert.That(prepared.HasProspectiveListVersionWitness, Is.EqualTo(roots.GetType() == typeof(List<TypeProvider>)));
                Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True, "This is an observation, not group permission for an unversioned carrier.");
                Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
                var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                var callbacks = 0;
                var file = writer.WriteWithMaterializedHeader((_, _, _) =>
                {
                    callbacks++;
                    Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
                    Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True);
                    Assert.That(prepared.AreProspectiveOwnerSnapshotsCurrent(), Is.True);
                });
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(file.Content, Is.EqualTo(expected));
                Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
            }, prospective: true);
        }

        [Test]
        public void RealFooObservedAnalyzerActivitySurvivesItsNormalWriterPause()
        {
            string? expected = null;
            WithRealFoo((owner, prepared) => expected = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write().Content);
            WithRealFoo((_, _) => { }, prospective: true, observeAnalyzerActivity: true,
                inspectGeneration: (_, referenceMap, owner, prepared) =>
                {
                    Assert.That(referenceMap.IsObservedAnalyzerActivityCurrent(), Is.True);
                    var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                    var file = writer.WriteWithMaterializedHeader((_, _, _) =>
                        Assert.That(referenceMap.IsObservedAnalyzerActivityCurrent(), Is.True));
                    Assert.That(referenceMap.IsObservedAnalyzerActivityCurrent(), Is.True);
                    Assert.That(file.Content, Is.EqualTo(expected));
                    Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.True);
                    // This records analyzer entries only; it is not phase or group permission.
                });
        }

        [Test]
        public void RealFooObservedRootPhaseBindsReferenceMapAndIteratorWithoutGrantingPermission()
        {
            WithRealFoo((_, _) => { }, prospective: true, observeAnalyzerActivity: true,
                inspectGeneration: (output, referenceMap, _, prepared) =>
                {
                    var roots = output.TypeProviders;
                    Assert.That(referenceMap.IsObservedReferenceMapRootCurrent(output, roots), Is.True);
                    Assert.That(prepared.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False,
                        "The deferred iterator has not obtained its root yet.");
                    using var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                    Assert.That(enumerator.MoveNext(), Is.True);
                    Assert.That(referenceMap.IsObservedEnumerationRootCurrent(output, roots), Is.True);
                    Assert.That(prepared.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False,
                        "A manually constructed fixture session is not an active CSharpGen-owned phase.");
                    var foreign = new OutputLibrary { TypeProviders = roots };
                    Assert.That(referenceMap.IsObservedReferenceMapRootCurrent(foreign, roots), Is.False);
                    Assert.That(referenceMap.IsObservedEnumerationRootCurrent(foreign, roots), Is.False);
                    output.TypeProviders = new List<TypeProvider>(roots);
                    output.TypeProviders = roots;
                    Assert.That(prepared.IsObservedGenerationRootPhaseCurrent(referenceMap), Is.False);
                    Assert.That(prepared.CanCommit(), Is.True, "A prospective-only phase failure must not veto an existing field decision.");
                });
        }

        [Test]
        public void RealFooArrayRestoreCanHideAChangedProviderStream()
        {
            var victimIndex = -1;
            string? victimType = null;
            string? victimName = null;
            WithRealFoo((_, _) => { }, inspectGeneration: (output, referenceMap, owner, _) =>
            {
                var roots = (TypeProvider[])output.TypeProviders;
                var emitted = GetWrittenProviderStream(output, referenceMap).ToArray();
                var first = emitted[0];
                var firstRootIndex = Array.IndexOf(roots, first);
                var fooStreamIndex = Array.IndexOf(emitted, owner);
                var fooRootIndex = Array.IndexOf(roots, owner);
                Assert.That(firstRootIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(fooStreamIndex, Is.GreaterThan(1));
                Assert.That(fooRootIndex, Is.GreaterThan(firstRootIndex + 1));
                var victim = emitted.Take(fooStreamIndex).Reverse().First(provider =>
                    Array.IndexOf(roots, provider) > firstRootIndex && !ReferenceEquals(provider, owner));
                victimIndex = Array.IndexOf(roots, victim);
                Assert.That(victimIndex, Is.LessThan(fooRootIndex));
                victimType = victim.GetType().FullName;
                victimName = victim.CachedType!.Name;
            });

            WithRealFoo((_, _) => { }, prospective: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var roots = (TypeProvider[])output.TypeProviders;
                var initial = roots.ToArray();
                var victim = roots[victimIndex];
                Assert.That(victim.GetType().FullName, Is.EqualTo(victimType));
                Assert.That(victim.CachedType!.Name, Is.EqualTo(victimName));
                Assert.That(prepared.HasProspectiveListVersionWitness, Is.False);
                var actual = new List<TypeProvider>();
                using var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                Assert.That(enumerator.MoveNext(), Is.True);
                var earlier = enumerator.Current;
                actual.Add(earlier);
                Assert.That(Array.IndexOf(roots, earlier), Is.LessThan(victimIndex));
                roots[victimIndex] = earlier;
                var restored = false;
                try
                {
                    while (enumerator.MoveNext())
                    {
                        var current = enumerator.Current;
                        actual.Add(current);
                        if (!restored && ReferenceEquals(current, earlier))
                        {
                            // Model an earlier writer changing an upcoming array slot and
                            // the intervening writer restoring it before Foo starts.
                            roots[victimIndex] = victim;
                            restored = true;
                        }
                    }
                }
                finally { roots[victimIndex] = victim; }
                Assert.That(restored, Is.True);
                Assert.That(actual.Count(provider => ReferenceEquals(provider, earlier)), Is.EqualTo(2));
                Assert.That(actual.Any(provider => ReferenceEquals(provider, victim)), Is.False);
                Assert.That(actual.Any(provider => ReferenceEquals(provider, owner)), Is.True);
                TestContext.Progress.WriteLine($"Iterator-only trace: slot {victimIndex} ({victimName}) yielded {earlier.CachedType?.Name} instead; no writer guards were run.");
                Assert.That(roots, Is.EqualTo(initial));
                Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
                Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True,
                    "Observation-only array equality cannot prove that the already traversed provider stream was unchanged.");
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var fooParts = prepared.SourceFiles.Where(file => file.FileOwner.CachedType?.Namespace == owner.CachedType!.Namespace &&
                    file.FileOwner.CachedType?.Name == owner.CachedType.Name).ToArray();
                Assert.That(fooParts.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(fooParts.All(file => (bool)started.GetValue(file)! == false), Is.True);
            });
        }

        [Test]
        public void RealFooWriterObservationsRejectChangedArrayBeforeNextYield()
        {
            WithRealFoo((_, _) => { }, prospective: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var roots = (TypeProvider[])output.TypeProviders;
                var fooIndex = Array.IndexOf(roots, owner);
                using var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                Assert.That(enumerator.MoveNext(), Is.True);
                var earlier = enumerator.Current;
                Assert.That(Array.IndexOf(roots, earlier), Is.LessThan(fooIndex));
                var fooFiles = prepared.SourceFiles.Where(file => file.FileOwner.CachedType?.Namespace == owner.CachedType!.Namespace &&
                    file.FileOwner.CachedType?.Name == owner.CachedType.Name).ToArray();
                Assert.That(fooFiles.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(fooFiles.All(file => file.IsCurrent()), Is.True);
                var writer = CodeModelGenerator.Instance.GetWriter(earlier);
                Assert.That(writer.GetType(), Is.EqualTo(typeof(TypeProviderWriter)));
                writer.PreparedFieldTypes = prepared;
                var mutated = false;
                prepared.ObserveSnapshot(); // Same pre-writer observation as CSharpGen.AddFile.
                CodeFile file;
                try
                {
                    file = writer.WriteWithMaterializedHeader((_, _, _) => { roots[fooIndex] = earlier; mutated = true; });
                    prepared.ObserveSnapshot(); // Same post-writer observation as CSharpGen.AddFile.
                }
                finally { roots[fooIndex] = owner; }
                Assert.That(mutated, Is.True);
                Assert.That(file.SourceConsumption?.WasCurrentAtEndOfWrite, Is.Not.EqualTo(true));
                Assert.That(prepared.CanCommit(), Is.False, "Observed root membership divergence stays invalid after restore.");
                Assert.That(fooFiles.All(source => !source.IsCurrent()), Is.True);
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Assert.That(fooFiles.All(source => (bool)started.GetValue(source)! == false), Is.True);
            });
        }

        [Test]
        public void RealFooEarlierWriterCanObserveAChangeWithoutChangingTheStream()
        {
            string[]? baseline = null;
            WithRealFoo((_, _) => { }, inspectGeneration: (output, referenceMap, _, _) =>
                baseline = GetWrittenProviderStream(output, referenceMap).Select(DescribeProvider).ToArray());
            WithRealFoo((_, _) => { }, prospective: true, inspectGeneration: (output, referenceMap, owner, prepared) =>
            {
                var roots = (TypeProvider[])output.TypeProviders;
                var initial = roots.ToArray();
                var fooIndex = Array.IndexOf(roots, owner);
                using var enumerator = GetWrittenProviderStream(output, referenceMap).GetEnumerator();
                Assert.That(enumerator.MoveNext(), Is.True);
                var earlier = enumerator.Current;
                Assert.That(Array.IndexOf(roots, earlier), Is.LessThan(fooIndex));
                var actual = new List<string> { DescribeProvider(earlier) };
                var sawTransientRoot = false;
                var writer = CodeModelGenerator.Instance.GetWriter(earlier);
                Assert.That(writer.GetType(), Is.EqualTo(typeof(TypeProviderWriter)));
                writer.PreparedFieldTypes = prepared;
                prepared.ObserveSnapshot();
                var written = writer.WriteWithMaterializedHeader((_, _, _) =>
                {
                    roots[fooIndex] = earlier;
                    sawTransientRoot = ReferenceEquals(output.TypeProviders[fooIndex], earlier);
                    roots[fooIndex] = owner;
                });
                prepared.ObserveSnapshot();
                while (enumerator.MoveNext()) { actual.Add(DescribeProvider(enumerator.Current)); }
                Assert.That(sawTransientRoot, Is.True);
                Assert.That(roots, Is.EqualTo(initial));
                Assert.That(actual, Is.EqualTo(baseline), "The input stream alone does not prove what an earlier callback observed.");
                Assert.That(prepared.IsProspectiveRootReferenceCurrent(), Is.True);
                Assert.That(prepared.AreProspectiveRootMembersCurrent(), Is.True);
                var fooFiles = prepared.SourceFiles.Where(file => file.FileOwner.CachedType?.Namespace == owner.CachedType!.Namespace &&
                    file.FileOwner.CachedType?.Name == owner.CachedType.Name).ToArray();
                Assert.That(fooFiles.Length, Is.GreaterThanOrEqualTo(2));
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Assert.That(fooFiles.All(file => (bool)started.GetValue(file)! == false), Is.True);
                TestContext.Progress.WriteLine($"Same-stream trace: earlier callback observed a transient root; field commit={prepared.CanCommit()}, shared commit={prepared.CanCommitSharedSnapshot()}, earlier source receipt current={written.SourceConsumption?.WasCurrentAtEndOfWrite}.");
            });
        }

        private static string DescribeProvider(TypeProvider provider) =>
            $"{provider.GetType().FullName}|{provider.CachedType?.Namespace}|{provider.CachedType?.Name}";

        // Test the non-authorizing snapshot mechanics without fabricating a
        // private CSharpGen-owned phase in this isolated real-Foo fixture.
        private static PreparedFieldTypes? CreateUnownedProspectiveSnapshot(CodeModelGenerator generator, OutputLibrary output,
            Func<TypeProvider, bool> shouldWrite)
        {
            var core = typeof(PreparedFieldTypes).GetMethod("CreateForGenerationCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            return (PreparedFieldTypes?)core.Invoke(null, new object?[] { generator, null, shouldWrite, true, output, false });
        }

        private static IEnumerable<TypeProvider> GetWrittenProviderStream(OutputLibrary output, ProviderReferenceMapSession referenceMap, object? generationOwner = null)
        {
            var method = typeof(CSharpGen).GetMethod("GetWrittenProviders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            return (IEnumerable<TypeProvider>)method.Invoke(null, new object?[] { output, referenceMap, generationOwner })!;
        }

        [Test]
        public void RealFooWriterPausesWithCanonicalDocsBeforeRestPartialStarts()
        {
            string? expected = null;
            string? expectedQualified = null;
            WithRealFoo((owner, prepared) =>
            {
                var baseline = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                expected = baseline.Content;
                expectedQualified = baseline.QualifiedContent;
            });

            WithRealFoo((owner, prepared) =>
            {
                var ownerType = owner.CachedType!;
                var rest = prepared.SourceFiles.Single(file => file.FileOwner.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider) &&
                    file.FileOwner.CachedType?.Namespace == ownerType.Namespace && file.FileOwner.CachedType?.Name == ownerType.Name);
                var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.That(started, Is.Not.Null);
                var callbacks = 0;
                var writer = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared };
                var file = writer.WriteWithMaterializedHeader((current, writeDocs, docs) =>
                {
                    callbacks++;
                    Assert.That(current, Is.SameAs(owner));
                    Assert.That(writeDocs, Is.True);
                    Assert.That(docs, Is.SameAs(owner.CachedXmlDocs));
                    Assert.That(owner.CachedDisabledFileWarnings, Is.Not.Null);
                    Assert.That(started!.GetValue(rest), Is.EqualTo(false));
                });
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(file.Content, Is.EqualTo(expected));
                Assert.That(file.QualifiedContent, Is.EqualTo(expectedQualified));
                Assert.That(file.SourceConsumption, Is.Not.Null);
                Assert.That(file.SourceConsumption!.WasCurrentAtEndOfWrite, Is.True);
                Assert.That(started!.GetValue(rest), Is.EqualTo(false));
            });
        }

        [Test]
        public void RealFooHasBothCanonicalPartialFilesInPreparedUniverse()
        {
            WithRealFoo((owner, prepared) =>
            {
                var ownerType = owner.CachedType!;
                var parts = prepared.SourceFiles.Where(file =>
                    file.FileOwner.CachedType?.Namespace == ownerType.Namespace && file.FileOwner.CachedType?.Name == ownerType.Name).ToArray();
                Assert.That(parts.Select(part => part.FilePath), Is.EquivalentTo(new[]
                {
                    Path.Combine("src", "Generated", "Foo.cs"),
                    Path.Combine("src", "Generated", "Foo.RestClient.cs")
                }));
                Assert.That(parts.Count(part => ReferenceEquals(part.FileOwner, owner)), Is.EqualTo(1));
                var rest = parts.Single(part => part.FileOwner.GetType() == typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider)).FileOwner;
                Assert.That(rest.PartialDeclarationOwner, Is.SameAs(owner));
                Assert.That(owner.PartialDeclarationParts, Does.Contain(rest));
                // This is a minimum partial group, not proof that other source or generated files are unrelated.
            });
        }

        [Test]
        public void RealFooExistingHeaderProofDoesNotAdmitUnpinnedClient()
        {
            WithRealFoo((owner, prepared) =>
            {
                var path = owner.CachedRelativeFilePath;
                var docs = owner.CachedXmlDocs;
                TestContext.Progress.WriteLine($"Foo header: path={path}, pinned={owner.PinnedRelativeFilePath != null}, typeDocsMaterialized={docs != null}, customViewMaterialized={owner.IsCustomCodeViewMaterialized}, customViewPresent={owner.CachedCustomCodeView != null}");
                TestContext.Progress.WriteLine($"Foo header data: type={owner.CachedType?.Namespace}.{owner.CachedType?.Name}, base={owner.CachedType?.BaseType?.Name ?? "none"}, where={owner.CachedWhereClause != null}, interfacesOwned={ContractCollections.TryCopy(owner.CachedImplements, out var interfaces)} count={interfaces.Length}, attrsOwned={ContractCollections.TryCopy(owner.CachedWriteAttributes, out var attributes)} count={attributes.Length}, warningsOwned={ContractCollections.TryCopy(owner.CachedDisabledFileWarnings, out var warnings)} count={warnings.Length}");
                foreach (var name in new[] { "BuildRelativeFilePath", "BuildDescription", "BuildAttributes", "BuildDisabledFileWarnings", "get_ShouldAnalyzeAttributesInReferenceMap", "get_ShouldWriteTypeXmlDocs", "BuildDeclaringTypeProvider" })
                {
                    var method = owner.GetType().GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    TestContext.Progress.WriteLine($"Foo dispatch: {name} = {method?.DeclaringType?.FullName ?? "not found"}");
                }
                Assert.That(PreparedGeneratedTypeHeader.TryCapture(owner), Is.Null);
                Assert.That(owner.CachedRelativeFilePath, Is.EqualTo(path));
                Assert.That(owner.CachedXmlDocs, Is.SameAs(docs));
            });
        }

        [Test]
        public void RealFooHeaderInputPreflightDoesNotMaterializeLazyCaches()
        {
            WithRealFoo((owner, prepared) =>
            {
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                var input = client.InputClient;
                var field = typeof(TypeProvider).GetField("_description", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.That(field, Is.Not.Null);
                var description = field!.GetValue(owner);
                var docs = owner.CachedXmlDocs;
                var warnings = owner.CachedDisabledFileWarnings;
                TestContext.Progress.WriteLine($"Foo input: summaryEmpty={string.IsNullOrEmpty(input.Summary)}, docEmpty={string.IsNullOrEmpty(input.Doc)}, parentPresent={input.Parent != null}, experimentalPresent={input.Experimental != null}, descriptionMaterialized={description != null}, docsMaterialized={docs != null}, warningsMaterialized={warnings != null}");
                Assert.That(field.GetValue(owner), Is.SameAs(description));
                Assert.That(owner.CachedXmlDocs, Is.SameAs(docs));
                Assert.That(owner.CachedDisabledFileWarnings, Is.SameAs(warnings));
            });
        }

        [Test]
        public void RealFooInputCanChangeAndRestoreWhileLazyDocumentationCapturesIntermediateValue()
        {
            WithRealFoo((owner, prepared) =>
            {
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                var input = client.InputClient;
                Assert.That(owner.CachedXmlDocs, Is.Null);
                input.Update(summary: "before", doc: "before");
                var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
                input.Update(doc: "during");
                var docs = owner.XmlDocs;
                input.Update(doc: "before");
                Assert.That(input.Doc, Is.EqualTo("before"));
                Assert.That(snapshot.IsCurrent(), Is.True,
                    "The existing owner snapshot alone does not version mutable ClientModel input.");
                Assert.That(docs.Summary, Is.Not.Null);
                using var writer = new Microsoft.TypeSpec.Generator.CodeWriter();
                docs.Summary!.Write(writer);
                Assert.That(writer.ToString(false), Does.Contain("during"));
            });
        }

        [Test]
        public void ExperimentalDependencyListCanChangeAndRestoreWhileWarningsCaptureIntermediateValue()
        {
            WithRealFoo((owner, prepared) =>
            {
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                var ids = new System.Collections.Generic.List<string> { "Before" };
                var experimental = new Microsoft.TypeSpec.Generator.Input.InputExperimentalDetails(dependsOn: ids);
                // The input converter can assign this internal setter. Reflection here simulates
                // that already-loaded non-null input; it is not part of a production proof.
                typeof(Microsoft.TypeSpec.Generator.Input.InputClient).GetProperty("Experimental")!.SetValue(client.InputClient, experimental);
                Assert.That(owner.CachedDisabledFileWarnings, Is.Null);
                var snapshot = new PreparedSourceFile.OwnerSnapshot(owner);
                ids[0] = "During";
                var warnings = owner.DisabledFileWarnings;
                ids[0] = "Before";
                Assert.That(ids[0], Is.EqualTo("Before"));
                Assert.That(snapshot.IsCurrent(), Is.True);
                Assert.That(warnings.Single().Code.ToDisplayString(), Does.Contain("During"));
            });
        }

        [TestCase("\r")]
        [TestCase("\n")]
        [TestCase("\u0085")]
        [TestCase("\u2028")]
        [TestCase("\u2029")]
        public void RealFooTypeDocumentationKeepsInputLineBreaksInsideComments(string lineBreak)
        {
            WithRealFoo((owner, prepared) =>
            {
                var client = (Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider)owner;
                client.InputClient.Update(doc: "header" + lineBreak + "public class Escaped {}", summary: "header");
                Assert.That(owner.CachedXmlDocs, Is.Null);
                var file = new TypeProviderWriter(owner) { PreparedFieldTypes = prepared }.Write();
                var root = CSharpSyntaxTree.ParseText(file.Content).GetRoot();
                Assert.That(root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Any(declaration => declaration.Identifier.ValueText == "Escaped"), Is.False);
                Assert.That(file.Content, Does.Contain("/// public class Escaped {}"));
            });
        }

        [Test]
        public void RealFooAllExceptionDocumentationUsesBoundedFileProfile()
        {
            WithRealFoo((owner, prepared) =>
            {
                var file = prepared.SourceFiles.Single(source => ReferenceEquals(source.FileOwner, owner));
                var docs = file.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .Select(node => node.Producer).OfType<XmlDocProvider>()
                    .Where(documentation => documentation.Exceptions.Count > 0).ToArray();
                Assert.That(docs.Length, Is.EqualTo(8));
                foreach (var documentation in docs)
                {
                    var envelope = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(documentation);
                    Assert.That(envelope, Is.Not.Null);
                    Assert.That(envelope!.IsCurrent, Is.True);
                    Assert.That(envelope.ImportNamespaces, Does.Contain("System.ClientModel"));
                }
            });
        }

        private sealed class PocImportVisitor(bool rest = false, bool late = false) : LibraryVisitor
        {
            protected override TypeProvider? VisitType(TypeProvider type)
            {
                if (type.Name == "Foo" && (rest
                    ? type is Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider
                    : type is Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider))
                {
                    if (late)
                    {
                        var signature = new MethodSignature("PocLateImport", null, MethodSignatureModifiers.Private,
                            typeof(Poc.PreparedImports.Helper), null, []);
                        var method = new MethodProvider(signature, new Microsoft.TypeSpec.Generator.Expressions.KeywordExpression("default", null), type);
                        type.Update(methods: type.Methods.Append(method).ToArray());
                    }
                    else
                    {
                        var field = new FieldProvider(FieldModifiers.Private, typeof(Poc.PreparedImports.Helper), "_pocImport", type);
                        type.Update(fields: type.Fields.Append(field).ToArray());
                    }
                }
                return type;
            }
        }

        private sealed class FooArgumentIdentityVisitor : LibraryVisitor
        {
            internal sealed record Visit(string Kind, string Method,
                Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression Invocation,
                IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> Before,
                Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] Elements, long Revision, bool WitnessOnVisit);

            internal List<Visit> Calls { get; } = [];

            protected internal override Microsoft.TypeSpec.Generator.Expressions.ValueExpression? VisitInvokeMethodExpression(
                Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression expression, MethodProvider method)
            {
                if (method.EnclosingType.Name != "Foo" || method.Signature.Parameters.Count != 1) { return expression; }
                var parameter = method.Signature.Parameters[0].Name;
                string? kind = parameter == "cancellationToken" && expression.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync"
                    ? "convenience"
                    : parameter == "options" && expression.MethodName is "ProcessMessage" or "ProcessMessageAsync" ? "pipeline" : null;
                if (kind is not null)
                {
                    Calls.Add(new Visit(kind, method.Signature.Name, expression, expression.Arguments,
                        expression.Arguments.ToArray(), expression.DeclarationRevision, ConstructedArgumentWitness.Read(expression).HasValue));
                }
                return expression;
            }
        }

        private sealed class FooArgumentRepresentationVisitor : LibraryVisitor
        {
            internal System.Collections.Generic.List<bool> Convenience { get; } = [];
            internal System.Collections.Generic.List<bool> Pipeline { get; } = [];

            protected internal override Microsoft.TypeSpec.Generator.Expressions.ValueExpression? VisitInvokeMethodExpression(
                Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression expression, MethodProvider method)
            {
                if (method.EnclosingType.Name != "Foo") { return expression; }
                bool immutable = expression.Arguments is System.Collections.Immutable.ImmutableArray<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>;
                if (method.Signature.Parameters.Count == 1 && method.Signature.Parameters[0].Name == "cancellationToken" &&
                    expression.MethodSignature?.Name is "Three" or "ThreeAsync" or "Four" or "FourAsync")
                {
                    Convenience.Add(immutable);
                }
                if (method.Signature.Parameters.Count == 1 && method.Signature.Parameters[0].Name == "options" &&
                    expression.MethodName is "ProcessMessage" or "ProcessMessageAsync")
                {
                    Pipeline.Add(immutable);
                }
                return expression;
            }
        }

        [Test]
        public void ExplicitConvenienceCarrierKeepsNativeSingleEnumerationAndOrder()
        {
            WithRealFoo((owner, _) =>
            {
                var method = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ScmMethodProviderCollection)
                    .GetMethod("InvokeConvenienceProtocol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] elements =
                    [new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("first"), new Microsoft.TypeSpec.Generator.Expressions.LiteralExpression("second")];
                var baselineSource = new CountingArgumentInput(elements);
                IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> nativeSpread = [.. baselineSource];
                var native = Microsoft.TypeSpec.Generator.Snippets.Snippet.This.Invoke(owner.Methods.First().Signature, nativeSpread, false);
                var currentSource = new CountingArgumentInput(elements);
                var current = (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)method.Invoke(null,
                    new object[] { Microsoft.TypeSpec.Generator.Snippets.Snippet.This, owner.Methods.First().Signature, currentSource, false })!;
                Assert.That(baselineSource.Enumerations, Is.EqualTo(1));
                Assert.That(currentSource.Enumerations, Is.EqualTo(baselineSource.Enumerations));
                Assert.That(currentSource.Trace, Is.EqualTo(baselineSource.Trace));
                Assert.That(currentSource.CountReads + currentSource.IndexReads, Is.Zero, "No new dispatch to the source Count/indexer.");
                Assert.That(current.Arguments.Count, Is.EqualTo(elements.Length));
                for (int i = 0; i < elements.Length; i++)
                {
                    Assert.That(ReferenceEquals(current.Arguments[i], native.Arguments[i]) && ReferenceEquals(current.Arguments[i], elements[i]), Is.True);
                }
                Assert.That(ConstructedArgumentWitness.Read(current), Is.Null, "Unknown source carrier must not acquire the exact-list witness.");
            });
        }

        private sealed class CountingArgumentInput(Microsoft.TypeSpec.Generator.Expressions.ValueExpression[] elements)
            : IReadOnlyList<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>
        {
            internal int Enumerations, CountReads, IndexReads;
            internal readonly List<string> Trace = [];
            public int Count { get { CountReads++; throw new InvalidOperationException("source Count must not be added"); } }
            public Microsoft.TypeSpec.Generator.Expressions.ValueExpression this[int index]
            { get { IndexReads++; throw new InvalidOperationException("source indexer must not be added"); } }
            public IEnumerator<Microsoft.TypeSpec.Generator.Expressions.ValueExpression> GetEnumerator()
            {
                Enumerations++; Trace.Add("start");
                for (int i = 0; i < elements.Length; i++) { Trace.Add("element" + i); yield return elements[i]; }
                Trace.Add("end");
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [TestCase(4096)]
        [TestCase(4097)]
        public void FinalHeaderReviewConvenienceProducerBoundsAdditionalWitnessCopy(int count)
        {
            WithRealFoo((owner, prepared) =>
            {
                var method = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ScmMethodProviderCollection)
                    .GetMethod("InvokeConvenienceProtocol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var arguments = Enumerable.Repeat<Microsoft.TypeSpec.Generator.Expressions.ValueExpression>(Microsoft.TypeSpec.Generator.Snippets.Snippet.Literal(1), count).ToList();
                var receiver = Microsoft.TypeSpec.Generator.Snippets.Snippet.This;
                var signature = owner.Methods.First().Signature;
                var invocationArguments = new object[] { receiver, signature, arguments, false };
                var current = CodeModelGenerator.Instance;
                long Measure(bool observed, out Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression invocation)
                {
                    CodeModelGenerator.Instance = observed ? current : new UnconfiguredGenerator();
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    invocation = (Microsoft.TypeSpec.Generator.Expressions.InvokeMethodExpression)method.Invoke(null, invocationArguments)!;
                    return GC.GetAllocatedBytesForCurrentThread() - before;
                }
                try
                {
                    _ = Measure(false, out _);
                    _ = Measure(true, out _);
                    var nativeBytes = Measure(false, out var native);
                    var observedBytes = Measure(true, out var observed);
                    Assert.That(native.Arguments.Count, Is.EqualTo(count));
                    Assert.That(observed.Arguments.Count, Is.EqualTo(count));
                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(ReferenceEquals(native.Arguments[i], arguments[i]), Is.True);
                        Assert.That(ReferenceEquals(observed.Arguments[i], arguments[i]), Is.True);
                    }
                    if (count == 4096) { Assert.That(ConstructedArgumentWitness.Read(observed).HasValue, Is.True); }
                    else
                    {
                        TestContext.WriteLine($"PRODUCER-OBSERVATION native={nativeBytes}; observed={observedBytes}");
                        Assert.That(observedBytes - nativeBytes, Is.LessThanOrEqualTo(1024), "Oversized observation must skip its additional arrays, not native invocation construction.");
                        Assert.That(ConstructedArgumentWitness.Read(observed).HasValue, Is.False);
                    }
                }
                finally { CodeModelGenerator.Instance = current; }
            });
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FinalHeaderReviewFooFixtureRestoresPriorState(bool finalized, bool failBody)
        {
            AssertFinalHeaderReviewPriorStateRestored(() =>
            {
                void Run() => WithRealFoo((owner, prepared) =>
                {
                    if (failBody) { throw new InvalidOperationException("final-header-review-body"); }
                }, finalizePocHeaders: finalized);
                if (failBody) { Assert.That(Assert.Throws<InvalidOperationException>(Run)!.Message, Is.EqualTo("final-header-review-body")); }
                else { Run(); }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalHeaderReviewDisabledWitnessFixtureRestoresPriorPolicy(bool unconfiguredCore)
            => AssertFinalHeaderReviewPriorStateRestored(() => PipelineCallDoesNotRecordWitnessWhenPreEmissionIsDisabled(unconfiguredCore));

        private static void AssertFinalHeaderReviewPriorStateRestored(Action exercise)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var coreField = typeof(CodeModelGenerator).GetField("_instance", flags)!;
            var scmField = typeof(ScmCodeModelGenerator).GetField("_instance", flags)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), flags)!;
            var originalCore = coreField.GetValue(null);
            var originalScm = scmField.GetValue(null);
            var originalPolicy = policy.GetValue(null);
            var directory = Path.Combine(Path.GetTempPath(), "typespec-header-prior-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var configuration = Configuration.Load(directory, "{\"package-name\":\"PriorSample\",\"unreferenced-types-handling\":\"keepAll\"}");
                var prior = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = prior;
                exercise();
                Assert.Multiple(() =>
                {
                    Assert.That(ReferenceEquals(coreField.GetValue(null), prior), Is.True, "Restore the prior base generator.");
                    Assert.That(ReferenceEquals(scmField.GetValue(null), prior), Is.True, "Restore the distinct SCM singleton.");
                    Assert.That(Configuration.UnreferencedTypesHandling, Is.EqualTo(Configuration.UnreferencedTypesHandlingOption.KeepAll));
                });
            }
            finally
            {
                coreField.SetValue(null, originalCore);
                scmField.SetValue(null, originalScm);
                policy.SetValue(null, originalPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase("options", false)]
        [TestCase("options", true)]
        [TestCase("configuration", false)]
        [TestCase("configuration", true)]
        public void FinalHeaderReviewAdditionalColdDescriptionDependencyIsBounded(string kind, bool finalized)
        {
            TypeProvider? child = null;
            CSharpType? dependency = null;
            string? originalNamespace = null;
            try
            {
                WithRealFoo((owner, prepared) =>
                {
                    Assert.That(child, Is.Not.Null);
                    Assert.Multiple(() =>
                    {
                        Assert.That(child!.CachedDescription is null, Is.True);
                        Assert.That(child.CachedXmlDocs is null, Is.True);
                        Assert.That(child.CachedDisabledFileWarnings is null, Is.True);
                    });
                }, finalizePocHeaders: finalized, beforePocPreparation: (output, map) =>
                {
                    const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    if (kind == "options")
                    {
                        var singleton = typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientOptionsProvider)
                            .GetField("_singletonInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
                        child = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientOptionsProvider>()
                            .First(provider => !ReferenceEquals(provider, singleton) && provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public));
                        var parent = (TypeProvider)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientOptionsProvider)
                            .GetField("_clientProvider", instance)!.GetValue(child)!;
                        dependency = parent.CachedType!;
                    }
                    else
                    {
                        child = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientSettingsProvider>()
                            .First(provider => provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public));
                        dependency = (CSharpType)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientSettingsProvider)
                            .GetField("IConfigurationSectionType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                    }
                    _ = child.Type;
                    _ = child.DeclarationModifiers;
                    foreach (var name in new[] { "_description", "_xmlDocs", "_disabledFileWarnings" })
                    { typeof(TypeProvider).GetField(name, instance)!.SetValue(child, null); }
                    Assert.That(dependency, Is.Not.Null);
                    originalNamespace = dependency!.Namespace;
                    dependency.Update(@namespace: new string('A', 5000));
                    Assert.That(child.CachedType!.Namespace.Length, Is.LessThan(4096));
                });
            }
            finally
            {
                // The assertion is about pre-write cache timing, not an output
                // alias contrast. Restore the shared static configuration type.
                if (dependency is not null && originalNamespace is not null) { dependency.Update(@namespace: originalNamespace); }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinalHeaderReviewColdSettingsRejectsUnsupportedParentBeforeCaching(bool finalized)
        {
            TypeProvider? settings = null;
            WithRealFoo((owner, prepared) =>
            {
                Assert.That(settings, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(settings!.CachedDescription is null, Is.True, "Unsupported parent graphs must retain cold native description timing.");
                    Assert.That(settings.CachedXmlDocs is null, Is.True);
                    Assert.That(settings.CachedDisabledFileWarnings is null, Is.True);
                });
            }, finalizePocHeaders: finalized, beforePocPreparation: (output, map) =>
            {
                settings = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientSettingsProvider>()
                    .First(provider => provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public));
                _ = settings.Type;
                _ = settings.DeclarationModifiers;
                var parent = (TypeProvider)typeof(Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientSettingsProvider)
                    .GetField("_clientProvider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings)!;
                Assert.That(parent.CachedType, Is.Not.Null);
                foreach (var name in new[] { "_description", "_xmlDocs", "_disabledFileWarnings" })
                {
                    typeof(TypeProvider).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(settings, null);
                }
                parent.CachedType!.Update(@namespace: new string('A', 5000));
                Assert.That(settings.CachedType!.Namespace.Length, Is.LessThan(4096));
            });
        }

        private static void WithRealFoo(Action<TypeProvider, PreparedFieldTypes> run, bool materializeHeaderBeforePreparation = false,
            LibraryVisitor? additionalVisitor = null, bool prospective = false,
            Action<OutputLibrary, ProviderReferenceMapSession, TypeProvider, PreparedFieldTypes>? inspectGeneration = null,
            bool observeAnalyzerActivity = false, bool addFooShadow = false, Func<bool>? sourceCurrent = null, bool precomputePoc = false,
            Action<TypeProvider>? beforePreparation = null, bool addPocCompetitor = false, bool fooGroupOnly = false,
            Action<OutputLibrary, ProviderReferenceMapSession>? beforePocPreparation = null, bool addPocUnrelatedRoot = false,
            bool addPocOrderingRoots = false, bool addPocDeclaringRoot = false, bool addPocFilteredRoot = false,
            bool addPocEmptyFactory = false, bool addPocParentDecisionRoots = false, bool addPocReentrantFactory = false,
            bool addPocImportReference = false, string? licenseHeader = null, bool pocFooFirst = false, bool finalizePocHeaders = false,
            bool addPocUnreferencedModel = false, Func<GeneratorContext, ScmCodeModelGenerator>? generatorFactory = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "typespec-prepared-foo-local-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var coreInstance = typeof(CodeModelGenerator).GetField("_instance", flags)!;
            var scmInstance = typeof(ScmCodeModelGenerator).GetField("_instance", flags)!;
            var policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), flags)!;
            var previousCore = coreInstance.GetValue(null);
            var previousScm = scmInstance.GetValue(null);
            var previousPolicy = policy.GetValue(null);
            try
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PreparedFieldTypes", "ClientStructureDefault.json"),
                    Path.Combine(directory, "tspCodeModel.json"));
                var license = licenseHeader is null ? string.Empty : ",\"license\":{\"name\":\"test\",\"company\":\"test\",\"link\":\"\",\"description\":\"\",\"header\":" +
                    System.Text.Json.JsonSerializer.Serialize(licenseHeader) + "}";
                var referenceMapMode = addPocUnreferencedModel ? ",\"unreferenced-types-handling\":\"removeOrInternalize\"" : string.Empty;
                var configuration = Configuration.Load(directory, "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Client.Structure.Service.Default\"" + license + referenceMapMode + "}");
                var generatorContext = new GeneratorContext(configuration);
                var generator = generatorFactory?.Invoke(generatorContext) ?? new ScmCodeModelGenerator(generatorContext);
                CodeModelGenerator.Instance = generator;
                ((CodeModelGenerator)generator).Configure();
                if (additionalVisitor is not null) { generator.AddVisitor(additionalVisitor); }
                var compilation = CSharpCompilation.Create("KnownClientInputs", references:
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(PipelineMessage).Assembly.Location),
                ]);
                Assert.That(compilation.GetTypeByMetadataName("System.ClientModel.Primitives.PipelineMessage"), Is.Not.Null);
                if (addFooShadow)
                {
                    compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Client.Structure.Service { public class PipelineMessage {} }"));
                }
                if (addPocImportReference)
                {
                    compilation = compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Poc.PreparedImports.Helper).Assembly.Location));
                }
                generator.SourceInputModel = new SourceInputModel(compilation, null)
                {
                    // A controlled complete-input snapshot for this fixture, not a production host certificate.
                    PreEmissionSourceInputs = new CompleteSourceInputSnapshot(directory, "foo-local", compilation, ["Debug|net10.0"], sourceCurrent ?? (() => true))
                };
                var output = generator.OutputLibrary;
                if (addPocUnreferencedModel)
                {
                    var input = Microsoft.TypeSpec.Generator.Tests.Common.InputFactory.Model("PocUnreferencedModel", access: null!);
                    var model = generator.TypeFactory.CreateModel(input)!;
                    output.TypeProviders = output.TypeProviders.Append(model).ToArray();
                }
                foreach (var provider in output.TypeProviders) { provider.EnsureBuilt(); }
                foreach (var visitor in generator.Visitors) { visitor.VisitLibrary(output); }
                CSharpGen.FilterAllCustomizedMembers(output);
                foreach (var provider in output.TypeProviders) { provider.ProcessTypeForBackCompatibility(); }

                var owner = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.ClientProvider>()
                    .Single(provider => provider.Name == "Foo");
                if (addPocCompetitor) { output.TypeProviders = output.TypeProviders.Append(new PocPipelineMessageCompetitor(owner.Type.Namespace)).ToArray(); }
                if (fooGroupOnly)
                {
                    var rest = output.TypeProviders.OfType<Microsoft.TypeSpec.Generator.ClientModel.Providers.RestClientProvider>()
                        .Single(provider => provider.Type.Namespace == owner.Type.Namespace && provider.Type.Name == owner.Type.Name);
                    output.TypeProviders = new TypeProvider[] { owner, rest };
                }
                if (pocFooFirst)
                {
                    // Test-only order, established before the reference-map phase.
                    output.TypeProviders = new[] { owner }.Concat(output.TypeProviders.Where(provider => !ReferenceEquals(provider, owner))).ToArray();
                }
                if (addPocUnrelatedRoot)
                {
                    var unrelated = new PocReceiptProvider("PocReceiptOwner", owner.Type.Namespace, "src/Generated/PocReceiptOwner.cs");
                    unrelated.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Append(unrelated).ToArray();
                }
                if (addPocOrderingRoots)
                {
                    var competitor = new PocOrderingCompetitor();
                    var callbackOwner = new PocOrderingOwner(competitor, owner.Type.Namespace);
                    competitor.EnsureBuilt(); callbackOwner.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Concat(new TypeProvider[] { competitor, callbackOwner }).ToArray();
                }
                if (addPocDeclaringRoot)
                {
                    var declaringProvider = new PocMutableDeclaringProvider();
                    declaringProvider.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Append(declaringProvider).ToArray();
                }
                if (addPocFilteredRoot)
                {
                    var competitor = new PocOrderingCompetitor();
                    competitor.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Append(competitor).ToArray();
                }
                if (addPocEmptyFactory)
                {
                    var factory = new PocEmptyModelFactory(owner.Type.Namespace);
                    factory.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Append(factory).ToArray();
                }
                if (addPocParentDecisionRoots)
                {
                    var parent = new PocDecisionParent();
                    var serialization = new PocDecisionSerialization(parent);
                    var container = new PocDecisionContainer(serialization);
                    var mutator = new PocDecisionMutator(parent, owner.Type.Namespace);
                    parent.EnsureBuilt(); container.EnsureBuilt(); mutator.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Concat(new TypeProvider[] { container, mutator, parent }).ToArray();
                }
                if (addPocReentrantFactory)
                {
                    var factory = new PocReentrantModelFactory(owner.Type.Namespace);
                    factory.EnsureBuilt();
                    output.TypeProviders = output.TypeProviders.Append(factory).ToArray();
                }
                using var referenceMap = observeAnalyzerActivity || precomputePoc || finalizePocHeaders
                    ? ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, output.TypeProviders)
                    : ProviderReferenceMapAnalyzer.PrepareForGeneration(output.TypeProviders);
                if (materializeHeaderBeforePreparation)
                {
                    // Test-only controlled ordering; production needs a separate lifecycle proof.
                    _ = owner.DisabledFileWarnings;
                    _ = owner.XmlDocs;
                }
                beforePreparation?.Invoke(owner);
                beforePocPreparation?.Invoke(output, referenceMap);
                using var preparedHeaders = finalizePocHeaders ? new PreparedPocHeaderFinalization(generator) : null;
                using var prepared = finalizePocHeaders
                    ? PreparedFieldTypes.CreateForGenerationWithFinalizedPocHeaders(generator, output.TypeProviders, referenceMap.ShouldWriteProvider, preparedHeaders!)
                    : prospective
                    ? CreateUnownedProspectiveSnapshot(generator, output, referenceMap.ShouldWriteProvider)
                    : precomputePoc
                        ? PreparedFieldTypes.CreateForGenerationWithPocPlans(generator, output.TypeProviders, referenceMap.ShouldWriteProvider)
                        : PreparedFieldTypes.CreateForGeneration(generator, output.TypeProviders, referenceMap.ShouldWriteProvider);
                Assert.That(prepared, Is.Not.Null, "The controlled Foo inputs must reach the standard preparation lifetime.");
                run(owner, prepared!);
                inspectGeneration?.Invoke(output, referenceMap, owner, prepared!);
            }
            finally
            {
                ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
                coreInstance.SetValue(null, previousCore);
                scmInstance.SetValue(null, previousScm);
                policy.SetValue(null, previousPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    [TestFixture]
    internal class PocNativeTypeCaptureTests
    {
        [SetUp]
        public void SetUp() => MockHelpers.LoadMockGenerator(includeXmlDocs: true);

        private static CSharpType Foo(IReadOnlyList<CSharpType>? arguments = null) =>
            new("Foo", "Native.Types", false, false, null, arguments ?? Array.Empty<CSharpType>(), true, false);

        // Synthetic labels are unit inputs, not compiler/source/permission proof.
        private static SourceOccurrenceId Occurrence() => new(Guid.NewGuid(), "Capture.cs", "M", "body", 0, "local");
        private static void Enable(CodeWriter writer, DeclarationExpression declaration, CSharpType type, int characters = 100)
        {
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, Occurrence(),
                new PocWorkBudget(PocNativeTypeCapture.SetupWork + characters), characters), Is.True);
        }

        [Test]
        public void NativeDeclarationHasSeparateObservationSnapshotSeam()
        {
            using var writer = new CodeWriter();
            new DeclarationExpression(Foo(), "value").Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            // Baseline runtime red: the native formatter works, but no separate
            // observation snapshot API exists. This is not a runtime capture proof.
            Assert.That(typeof(CodeWriter).GetProperty("NativeTypeUse", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CaptureIsByteIdenticalAndRebasesBothExactRanges(bool companion, bool header)
        {
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value", isUsing: true, isOut: true, isConst: true);
            using var native = new CodeWriter();
            using var observed = new CodeWriter();
            if (companion) { native.EnableQualifiedFallback(); observed.EnableQualifiedFallback(); }
            var id = Occurrence();
            Assert.That(observed.TryEnableNativeTypeCapture(declaration, type, id,
                new PocWorkBudget(1200), 100, slot: 7), Is.True);
            foreach (var writer in new[] { native, observed })
            {
                using (writer.Scope())
                {
                    writer.WriteRawLine("#region local");
                    if (companion) { Assert.That(writer.TryAppendPreparedBodyName("B", "global::Other.B"), Is.True); }
                    writer.WriteLine();
                    declaration.Write(writer);
                    writer.WriteLine();
                    writer.WriteRawLine("#endregion");
                }
            }
            Assert.That(observed.NativeTypeUse, Is.Null, "Publication requires normal finalization.");
            var expected = native.ToString(header);
            var content = observed.ToString(header);
            Assert.That(content, Is.EqualTo(expected));
            Assert.That(observed.QualifiedContent, Is.EqualTo(native.QualifiedContent));
            var use = observed.NativeTypeUse!;
            Assert.That(use.Occurrence, Is.SameAs(id));
            Assert.That(use.Slot, Is.EqualTo(7));
            Assert.That(use.Declaration, Is.SameAs(declaration));
            Assert.That(use.Type, Is.SameAs(type));
            Assert.That(use.QualifiedAliasesPrimary, Is.EqualTo(!companion));
            Assert.That(content.Substring(use.PrimaryStart, use.PrimaryLength), Is.EqualTo("global::Native.Types.Foo"));
            var qualified = observed.QualifiedContent ?? content;
            Assert.That(qualified.Substring(use.QualifiedStart, use.QualifiedLength), Is.EqualTo("global::Native.Types.Foo"));
            Assert.That(content[use.PrimaryStart - 1], Is.EqualTo(' '));
            Assert.That(content[use.PrimaryStart + use.PrimaryLength], Is.EqualTo(' '));
            Assert.That(use.QualifiedStart - use.PrimaryStart, Is.EqualTo(companion ? 14 : 0));
            Assert.That(use.RegistrationNamespace, Is.EqualTo("Native.Types"));
            Assert.That(use.EmittedNamespace, Is.EqualTo("Native.Types"));
            Assert.That(use.EmittedName, Is.EqualTo("Foo"));
            Assert.That(use.IsDeclaration || use.WriteTypeNameOnly, Is.False);
            Assert.That(use.GenericDepth, Is.Zero);
            Assert.That(use.TrueFacts, Is.EqualTo(PocNativeTypeFacts.None));
            Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.IsNullable), Is.True);
            Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.IsValueType), Is.False,
                "Short-circuited native operands must remain unobserved.");
            Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.ArrayHasGenericArguments), Is.False);
            Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.IsFrameworkType), Is.False);
            Assert.That(observed.NameAlternatives, Is.EqualTo(native.NameAlternatives));
        }

        [TestCase(27, true)]
        [TestCase(26, false)]
        [TestCase(0, false)]
        public void RetentionIsPrepaidBeforeStoringEachActualReturnedString(int characters, bool available)
        {
            using var writer = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            var parent = new PocWorkBudget(2000);
            var ledger = new PocWorkBudget(PocNativeTypeCapture.SetupWork + characters, parent);
            var independent = new PocWorkBudget(100, parent);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, Occurrence(), ledger, characters), Is.True);
            Assert.That(ledger.Remaining, Is.Zero);
            Assert.That(ledger.TrySpend(), Is.False);
            int parentRemaining = parent.Remaining;
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse is not null, Is.EqualTo(available));
            Assert.That(parent.Remaining, Is.EqualTo(parentRemaining));
            Assert.That(independent.Remaining, Is.EqualTo(100));
        }

        [Test]
        public void SetupDenialDoesNotAffectIndependentLegacyNames()
        {
            using var writer = new CodeWriter();
            writer.EnableQualifiedFallback();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, Occurrence(),
                new PocWorkBudget(PocNativeTypeCapture.SetupWork - 1), 0), Is.False);
            Assert.That(writer.TryAppendPreparedBodyName("B", "global::Other.B"), Is.True);
            writer.AppendRaw("|");
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("B|global::Native.Types.Foo value"));
            Assert.That(writer.QualifiedContent, Is.EqualTo("global::Other.B|global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Null);
            Assert.That(writer.NameAlternatives.Length, Is.EqualTo(1));
        }

        [Test]
        public void WrongDeclarationAndEqualButUnownedTypeCannotBeBorrowed()
        {
            using var writer = new CodeWriter();
            var expected = Foo();
            var actual = Foo();
            var declaration = new DeclarationExpression(actual, "value");
            Enable(writer, declaration, expected);
            new DeclarationExpression(expected, "other").Write(writer);
            writer.AppendRaw("|");
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo other|global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Null);
        }

        [TestCase("keyword")]
        [TestCase("generic")]
        [TestCase("nested")]
        [TestCase("nullable")]
        [TestCase("array")]
        [TestCase("emptyNamespace")]
        [TestCase("nullNamespace")]
        public void UnsupportedRoutesStillFormatExactlyOnce(string shape)
        {
            CSharpType type = shape switch
            {
                "keyword" => typeof(int),
                "generic" => new CSharpType(typeof(List<>), typeof(int)),
                "nested" => new CSharpType("Child", "Native.Types", false, false, Foo(), Array.Empty<CSharpType>(), true, false),
                "nullable" => new CSharpType("Foo", "Native.Types", true, true, null, Array.Empty<CSharpType>(), true, true),
                "array" => typeof(int[]),
                _ => Foo()
            };
            if (shape == "emptyNamespace") { type.Namespace = string.Empty; }
            if (shape == "nullNamespace") { type.Namespace = null!; }
            var declaration = new DeclarationExpression(type, "value");
            using var native = new CodeWriter();
            using var observed = new CodeWriter();
            Enable(observed, declaration, type);
            declaration.Write(native);
            declaration.Write(observed);
            Assert.That(observed.ToString(false), Is.EqualTo(native.ToString(false)));
            Assert.That(observed.NativeTypeUse, Is.Null);
        }

        [Test]
        public void OriginalAnyDoesNotGainCountIndexerOrEnumerationReads()
        {
            foreach (bool capture in new[] { false, true })
            {
                var arguments = new CallbackArguments();
                var type = Foo(arguments);
                var declaration = new DeclarationExpression(type, "value");
                using var writer = new CodeWriter();
                if (capture) { Enable(writer, declaration, type); }
                declaration.Write(writer);
                Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
                Assert.That(arguments.Enumerations, Is.EqualTo(1));
                Assert.That(arguments.Moves, Is.EqualTo(1));
                Assert.That(arguments.Disposals, Is.EqualTo(1));
                Assert.That(writer.NativeTypeUse is not null, Is.EqualTo(capture));
            }
        }

        [TestCase("foreign")]
        [TestCase("emptyAppend")]
        [TestCase("emptyRaw")]
        [TestCase("nestedType")]
        [TestCase("nestedDeclaration")]
        [TestCase("drain")]
        [TestCase("emptyDrain")]
        [TestCase("dispose")]
        public void ReentrantNativeActivityAbandonsOnlyCapture(string action)
        {
            foreach (bool capture in new[] { false, true })
            {
                using var writer = new CodeWriter();
                writer.EnableQualifiedFallback();
                string? drained = null;
                var arguments = new CallbackArguments
                {
                    Callback = () =>
                    {
                        switch (action)
                        {
                            case "foreign": writer.AppendRaw("foreign"); break;
                            case "emptyAppend": writer.Append($""); break;
                            case "emptyRaw": writer.AppendRaw(string.Empty); break;
                            case "nestedType": writer.Append($"{Foo()}"); break;
                            case "nestedDeclaration": new DeclarationExpression(Foo(), "nested").Write(writer); break;
                            case "drain": drained = writer.ToString(false); break;
                            case "emptyDrain":
                                drained = writer.ToString(false);
                                Assert.That(writer.ToString(false), Is.Empty);
                                break;
                            case "dispose": writer.Dispose(); break;
                        }
                    }
                };
                var type = Foo(arguments);
                var declaration = new DeclarationExpression(type, "value");
                if (capture) { Enable(writer, declaration, type); }
                declaration.Write(writer);
                if (action == "dispose")
                {
                    // The unchanged no-capture control already throws here:
                    // native disposal clears buffers, not the legacy counter.
                    var error = Assert.Throws<InvalidOperationException>(() => writer.ToString(false));
                    Assert.That(error!.Message, Is.EqualTo("Qualified payload changed outside append accounting."));
                    Assert.That(writer.NativeTypeUse, Is.Null);
                    continue;
                }
                var content = writer.ToString(false);
                Assert.That(content, Is.EqualTo(action switch
                {
                    "foreign" => "global::Native.Types.Fooforeign value",
                    "emptyAppend" or "emptyRaw" => "global::Native.Types.Foo value",
                    "nestedType" => "global::Native.Types.Fooglobal::Native.Types.Foo value",
                    "nestedDeclaration" => "global::Native.Types.Fooglobal::Native.Types.Foo nested value",
                    _ => " value"
                }));
                Assert.That(writer.QualifiedContent, Is.EqualTo(content));
                if (drained is not null) { Assert.That(drained, Is.EqualTo("global::Native.Types.Foo")); }
                Assert.That(writer.NativeTypeUse, Is.Null);
                Assert.That(arguments.Moves, Is.EqualTo(1));
            }
        }

        [Test]
        public void NativeFailurePropagatesSameExceptionAndFinallyCannotPublish()
        {
            foreach (bool capture in new[] { false, true })
            {
                using var writer = new CodeWriter();
                var error = new InvalidOperationException("native Any");
                var arguments = new CallbackArguments { Callback = () => throw error };
                var type = Foo(arguments);
                var declaration = new DeclarationExpression(type, "value");
                if (capture) { Enable(writer, declaration, type); }
                Assert.That(Assert.Throws<InvalidOperationException>(() => declaration.Write(writer)), Is.SameAs(error));
                writer.AppendRaw("tail");
                Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Footail"));
                Assert.That(writer.NativeTypeUse, Is.Null);
                Assert.That(arguments.Enumerations, Is.EqualTo(1));
                Assert.That(arguments.Disposals, Is.EqualTo(1));
            }
        }

        [Test]
        public void RepeatedDrainsAndDisposalCannotMutatePublishedMetadataOrReattach()
        {
            using var writer = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            Enable(writer, declaration, type);
            declaration.Write(writer);
            var firstContent = writer.ToString(true);
            var first = writer.NativeTypeUse!;
            type.Namespace = "Changed";
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Changed.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Null);
            Assert.That(writer.ToString(true), Is.Empty);
            Assert.That(writer.NativeTypeUse, Is.Null);
            Assert.That(first.EmittedNamespace, Is.EqualTo("Native.Types"));
            Assert.That(firstContent.Substring(first.PrimaryStart, first.PrimaryLength), Is.EqualTo("global::Native.Types.Foo"));
            writer.Dispose();
            writer.Dispose();
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, Occurrence(), new PocWorkBudget(2000), 100), Is.False);
        }

        [Test]
        public void DrainBeforeUseResetsPrimaryAccountingAndDoesNotLeaveAWindow()
        {
            using var writer = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            Enable(writer, declaration, type);
            Assert.That(writer.ToString(false), Is.Empty);
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Null);
        }

        [Test]
        public void SyntheticPrimaryCounterSaturationIsCaptureOnlyAndDoesNotThrow()
        {
            using var writer = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            Enable(writer, declaration, type);
            var capture = (PocNativeTypeCapture)typeof(CodeWriter).GetField("_nameAlternatives", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(writer)!;
            capture.PrimaryPayloadLength = int.MaxValue - 1;
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Null);
        }

        [TestCase(20, true)]
        [TestCase(19, false)]
        public void SyntheticRangeUnitRetainsDistinctNativeReadValuesAndAliasesNoFacts(int characters, bool available)
        {
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            var capture = new PocNativeTypeCapture(declaration, type, Occurrence(), 0, characters) { Active = true };
            capture.Dispatch(type, false);
            capture.Root(type, false, false, 0);
            capture.Fact(PocNativeTypeFacts.IsArray, false);
            capture.Fact(PocNativeTypeFacts.FriendlyName, false);
            capture.Registration("Registered");
            capture.Text("global::", PocNativeTypeSite.Global);
            capture.Chunk(PocNativeTypeSite.Global, 9, 42, false);
            capture.Text("Emitted", PocNativeTypeSite.Namespace);
            capture.Chunk(PocNativeTypeSite.Namespace, 17, 50, false);
            capture.Text(".", PocNativeTypeSite.Dot);
            capture.Chunk(PocNativeTypeSite.Dot, 24, 57, false);
            capture.Fact(PocNativeTypeFacts.HasDeclaringType, false);
            capture.Text("Foo", PocNativeTypeSite.Name);
            capture.Chunk(PocNativeTypeSite.Name, 25, 58, false);
            capture.Fact(PocNativeTypeFacts.HasArguments, false);
            capture.Fact(PocNativeTypeFacts.WritingNewInstance, true);
            capture.Fact(PocNativeTypeFacts.PositiveGenericDepth, false);
            capture.RootReturned(28, 61);
            capture.End(true);
            if (available)
            {
                Assert.That(capture.Publish(int.MaxValue, 35, 35, false), Is.Null, "Rebasing overflow denies metadata, not native output.");
                Assert.That(capture.Publish(7, 36, 35, false), Is.Null, "Accounting mismatch denies metadata without parsing output.");
            }
            var use = capture.Publish(7, 35, 35, false);
            Assert.That(use is not null, Is.EqualTo(available));
            if (available)
            {
                Assert.That(use!.RegistrationNamespace, Is.EqualTo("Registered"));
                Assert.That(use.EmittedNamespace, Is.EqualTo("Emitted"));
                Assert.That(use.PrimaryStart, Is.EqualTo(16));
                Assert.That(use.QualifiedStart, Is.EqualTo(49));
                Assert.That(use.PrimaryLength, Is.EqualTo(19));
                Assert.That(use.QualifiedLength, Is.EqualTo(19));
                Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.IsNullable), Is.False);
                Assert.That(capture.Publish(7, 35, 35, false), Is.Null, "One prepaid slot cannot allocate a second immutable snapshot.");
            }
        }

        [Test]
        public void ExactOwnerEndCannotCompleteAnotherWriterOrResurrectADrainedWindow()
        {
            using var first = new CodeWriter();
            using var second = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            Enable(first, declaration, type);
            Enable(second, declaration, type);
            var firstWindow = first.BeginNativeTypeCapture(declaration);
            var secondWindow = second.BeginNativeTypeCapture(declaration);
            first.Append($"{type}");
            second.Append($"{type}");
            second.EndNativeTypeCapture(firstWindow, true);
            first.EndNativeTypeCapture(firstWindow, true);
            Assert.That(first.ToString(false), Is.EqualTo("global::Native.Types.Foo"));
            var published = first.NativeTypeUse;
            Assert.That(published, Is.Not.Null);
            Assert.That(second.ToString(false), Is.EqualTo("global::Native.Types.Foo"));
            Assert.That(second.NativeTypeUse, Is.Null, "Wrong owner did not complete the active use.");
            second.EndNativeTypeCapture(secondWindow, true);
            Assert.That(second.ToString(false), Is.Empty);
            Assert.That(second.NativeTypeUse, Is.Null);
            first.EndNativeTypeCapture(firstWindow, false);
            Assert.That(first.NativeTypeUse, Is.EqualTo(published));
        }

        [Test]
        public void NullableReferenceWithoutNativeSuffixRemainsAnObservationNotAnInferredFalseFlag()
        {
            var type = new CSharpType("Foo", "Native.Types", false, true, null, Array.Empty<CSharpType>(), true, false);
            var declaration = new DeclarationExpression(type, "value");
            using var writer = new CodeWriter();
            Enable(writer, declaration, type);
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            var use = writer.NativeTypeUse!;
            Assert.That(use.TrueFacts.HasFlag(PocNativeTypeFacts.IsNullable), Is.True);
            Assert.That(use.Observed.HasFlag(PocNativeTypeFacts.IsValueType), Is.True);
            Assert.That(use.TrueFacts.HasFlag(PocNativeTypeFacts.IsValueType), Is.False);
        }

        [Test]
        public void CompetingSetupMakesOneLedgerDebitAndNeverReattaches()
        {
            using var writer = new CodeWriter();
            var type = Foo();
            var declaration = new DeclarationExpression(type, "value");
            var id = Occurrence();
            var budget = new PocWorkBudget(3000);
            bool first = false, second = false;
            Parallel.Invoke(
                () => first = writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 100),
                () => second = writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 100));
            Assert.That(first ^ second, Is.True);
            Assert.That(budget.Remaining, Is.EqualTo(3000 - PocNativeTypeCapture.SetupWork - 100));
            declaration.Write(writer);
            Assert.That(writer.ToString(false), Is.EqualTo("global::Native.Types.Foo value"));
            Assert.That(writer.NativeTypeUse, Is.Not.Null);
            Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, budget, 100), Is.False);
        }

        private sealed class CallbackArguments : IReadOnlyList<CSharpType>
        {
            internal Action? Callback;
            internal int Enumerations;
            internal int Moves;
            internal int Disposals;
            public int Count => throw new InvalidOperationException("Unexpected Count corroboration");
            public CSharpType this[int index] => throw new InvalidOperationException("Unexpected indexer");
            public IEnumerator<CSharpType> GetEnumerator() { Enumerations++; return new Enumerator(this); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(CallbackArguments owner) : IEnumerator<CSharpType>
            {
                public CSharpType Current => throw new InvalidOperationException("Unexpected Current");
                object IEnumerator.Current => Current;
                public bool MoveNext() { owner.Moves++; owner.Callback?.Invoke(); return false; }
                public void Reset() => throw new NotSupportedException();
                public void Dispose() => owner.Disposals++;
            }
        }
    }
}

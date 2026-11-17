// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    // Mechanical unit records only: these IDs and masks establish no compiler or binding authority.
    [TestFixture]
    public class PocNativeFrameTests
    {
        [Test]
        public void InvalidDisabledPocRangeRetainsIndependentlySelectedLegacyBase()
        {
            var owner = new object();
            var budget = new PocWorkBudget(10000);
            var native = new CodeFile("F|global::N.P", "Frame.cs", "global::N.F|global::N.P",
                nameAlternatives: ImmutableArray.Create(new PreparedNameAlternative(0, 11, "F", PreparedNameChannel.Field)));
            var legacyBase = native.SelectPreparedNames(true, false);
            var uses = ImmutableArray.Create(new PocFrameAlternative(owner, 0, 0, -1, 11, 12, 11, "P", true));
            Assert.That(PocNativeFrame.TryCreate(native, owner, uses, ImmutableArray.Create(true), budget, out var frame), Is.True);
            Assert.That(frame!.TryRetainBase(legacyBase, true, false, budget, out var retained), Is.True);
            Assert.That(frame.TryCreateSelection(ImmutableArray.Create(false), budget, out var selection), Is.True);
            Assert.That(frame.TryReserve(budget, out var reservation), Is.True);
            Assert.That(frame.RenderMechanicalSelection(retained!, selection!, PocFramePhase.Final, reservation), Is.SameAs(legacyBase));
            Assert.That(native.Content, Is.EqualTo("F|global::N.P"));
        }

        private static readonly string Qualified = "[global::N.F|global::N.P|global::N.B|global::N.R|global::N.C]";
        private static readonly string Primary = "[F|global::N.P|B|global::N.R|C]";

        private static (CodeFile Native, ImmutableArray<PocFrameAlternative> Uses, object Owner) Mixed()
        {
            var owner = new object();
            var receipt = new SourceConsumptionReceipt(Guid.NewGuid(), "Mixed.cs", 0, ImmutableArray<SourceOccurrenceId>.Empty, true);
            var native = new CodeFile(Primary, receipt.File, Qualified, receipt, ImmutableArray.Create(
                new PreparedNameAlternative(1, 11, "F", PreparedNameChannel.Field),
                new PreparedNameAlternative(25, 11, "B", PreparedNameChannel.Body),
                new PreparedNameAlternative(49, 11, "C", PreparedNameChannel.Field)));
            return (native, ImmutableArray.Create(
                new PocFrameAlternative(owner, 0, 0, 3, 11, 13, 11, "P", true),
                new PocFrameAlternative(owner, 1, 1, 17, 11, 37, 11, "R", true)), owner);
        }

        private static PocNativeFrame Create(CodeFile native, object owner, ImmutableArray<PocFrameAlternative> uses,
            PocWorkBudget budget, params bool[] required)
        {
            Assert.That(PocNativeFrame.TryCreate(native, owner, uses, ImmutableArray.Create(required), budget, out var frame), Is.True);
            return frame!;
        }

        private static PocFrameSelectionBaseResult Retain(PocNativeFrame frame, CodeFile file, bool field, bool body, PocWorkBudget budget)
        {
            Assert.That(frame.TryRetainBase(file, field, body, budget, out var basis), Is.True);
            return basis!;
        }

        private static PocFrameSelection Select(PocNativeFrame frame, PocWorkBudget budget, params bool[] enabled)
        {
            Assert.That(frame.TryCreateSelection(ImmutableArray.Create(enabled), budget, out var selection), Is.True);
            return selection!;
        }

        private static CodeFile Render(PocNativeFrame frame, PocFrameSelectionBaseResult basis, PocFrameSelection selection,
            PocWorkBudget budget, PocFramePhase phase = PocFramePhase.Final)
        {
            Assert.That(frame.TryReserve(budget, out var reservation), Is.True);
            return frame.RenderMechanicalSelection(basis, selection, phase, reservation);
        }

        [TestCase(true, true, true, true)]
        [TestCase(true, false, true, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        [TestCase(true, true, false, false)]
        public void ForwardProjectionKeepsLegacyChoicesAndNativeIdentity(bool field, bool body, bool p, bool r)
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var selected = input.Native.SelectPreparedNames(field, body);
            var basis = Retain(frame, selected, field, body, budget);
            var result = Render(frame, basis, Select(frame, budget, p, r), budget);
            Assert.That(result.Content, Is.EqualTo($"[{(field ? "F" : "global::N.F")}|{(p ? "P" : "global::N.P")}|{(body ? "B" : "global::N.B")}|{(r ? "R" : "global::N.R")}|{(field ? "C" : "global::N.C")}]"));
            Assert.That(result.Name, Is.SameAs(input.Native.Name));
            Assert.That(result.SourceConsumption, Is.SameAs(input.Native.SourceConsumption));
            Assert.That(frame.Native, Is.SameAs(input.Native));
            Assert.That(input.Native.Content, Is.SameAs(Primary));
            Assert.That(input.Native.QualifiedContent, Is.SameAs(Qualified));
            if (!p && !r) { Assert.That(result, Is.SameAs(selected)); }
        }

        [TestCase("owner")]
        [TestCase("incomplete")]
        [TestCase("negative")]
        [TestCase("overflow")]
        [TestCase("primary-overflow")]
        [TestCase("zero")]
        [TestCase("length")]
        [TestCase("primary-projection")]
        [TestCase("short-empty")]
        [TestCase("not-shorter")]
        [TestCase("duplicate-slot")]
        [TestCase("slot-overflow")]
        [TestCase("order")]
        [TestCase("overlap")]
        [TestCase("nesting")]
        [TestCase("legacy-overlap")]
        public void EveryDisabledEntryIsValidatedBeforeAllAllowedFastPaths(string defect)
        {
            var input = Mixed();
            var bad = input.Uses[1];
            bad = defect switch
            {
                "owner" => bad with { Owner = new object() },
                "incomplete" => bad with { Completed = false },
                "negative" => bad with { QualifiedOffset = -1 },
                "overflow" => bad with { QualifiedOffset = int.MaxValue, QualifiedLength = int.MaxValue },
                "primary-overflow" => bad with { PrimaryOffset = int.MaxValue, PrimaryLength = int.MaxValue },
                "zero" => bad with { QualifiedLength = 0 },
                "length" => bad with { PrimaryLength = 10 },
                "primary-projection" => bad with { PrimaryOffset = 16 },
                "short-empty" => bad with { ShortSpelling = "" },
                "not-shorter" => bad with { ShortSpelling = "global::N.R" },
                "duplicate-slot" => bad with { SlotId = 0 },
                "slot-overflow" => bad with { SlotId = int.MaxValue },
                "order" => bad with { NativeOrder = 0 },
                "overlap" => bad with { QualifiedOffset = 20 },
                "nesting" => bad with { QualifiedOffset = 14, QualifiedLength = 2, PrimaryOffset = 4, PrimaryLength = 2 },
                "legacy-overlap" => bad with { QualifiedOffset = 25, PrimaryOffset = 15 },
                _ => throw new ArgumentException(defect)
            };
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, ImmutableArray.Create(input.Uses[0], bad), budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            Assert.That(Render(frame, basis, Select(frame, budget, true, false), budget), Is.SameAs(input.Native));
            Assert.That(Render(frame, basis, Select(frame, budget, true, true), budget), Is.SameAs(input.Native));
        }

        [TestCase("offset")]
        [TestCase("overflow")]
        [TestCase("channel")]
        [TestCase("overlap")]
        [TestCase("empty")]
        public void MalformedLegacyMapUsesPrepaidQualifiedWrapperButLegacyBypassStaysUnchanged(string defect)
        {
            var input = Mixed();
            var legacy = input.Native.NativeLegacyAlternatives.ToArray();
            legacy[1] = defect switch
            {
                "offset" => legacy[1] with { QualifiedOffset = -1 },
                "overflow" => legacy[1] with { QualifiedLength = int.MaxValue },
                "channel" => legacy[1] with { Channel = (PreparedNameChannel)99 },
                "overlap" => legacy[1] with { QualifiedOffset = 2 },
                "empty" => legacy[1] with { ShortSpelling = "" },
                _ => throw new ArgumentException(defect)
            };
            var native = new CodeFile(Primary, input.Native.Name, Qualified, input.Native.SourceConsumption, ImmutableArray.Create(legacy));
            Assert.That(native.SelectPreparedNames(true, true), Is.SameAs(native), "Deliberate legacy compatibility must survive.");
            var budget = new PocWorkBudget(100000);
            using var frame = Create(native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            var result = Render(frame, basis, selection, budget);
            Assert.That(result.Content, Is.SameAs(Qualified));
            Assert.That(result.SourceConsumption, Is.SameAs(native.SourceConsumption));
            Assert.That(Render(frame, basis, selection, budget), Is.SameAs(result), "No late fallback allocation.");
        }

        [Test]
        public void MissingRequiredUsesDenyButUnusedPlannedSlotsDoNot()
        {
            var owner = new object();
            var native = new CodeFile("global::N.P", "P.cs");
            var uses = ImmutableArray.Create(new PocFrameAlternative(owner, 1, 0, 0, 11, 0, 11, "P", true));
            var budget = new PocWorkBudget(100000);
            using var optional = Create(native, owner, uses, budget, false, true);
            var basis = Retain(optional, native, true, true, budget);
            Assert.That(Render(optional, basis, Select(optional, budget, true, true), budget).Content, Is.EqualTo("P"));
            using var missing = Create(native, owner, uses, budget, true, true);
            var missingBase = Retain(missing, native, true, true, budget);
            Assert.That(Render(missing, missingBase, Select(missing, budget, false, true), budget), Is.SameAs(native));
        }

        [Test]
        public void UnsupportedNoMapAndNoCompanionCasesPreserveNativeLegacyHandling()
        {
            var owner = new object();
            var budget = new PocWorkBudget(100000);
            var legacy = new CodeFile("F|global::N.P", "Old.cs", "global::N.F|global::N.P");
            var uses = ImmutableArray.Create(new PocFrameAlternative(owner, 0, 0, 2, 11, 12, 11, "P", true));
            using var frame = Create(legacy, owner, uses, budget, true);
            var selected = legacy.SelectPreparedNames(true, false);
            Assert.That(Render(frame, Retain(frame, selected, true, false, budget), Select(frame, budget, true), budget), Is.SameAs(selected));
            var noCompanion = new CodeFile("F|global::N.P", "Old.cs", nameAlternatives: ImmutableArray.Create(
                new PreparedNameAlternative(-1, int.MaxValue, "F", PreparedNameChannel.Field)));
            using var unsupported = Create(noCompanion, owner, uses, budget, true);
            Assert.That(Render(unsupported, Retain(unsupported, noCompanion, true, true, budget), Select(unsupported, budget, true), budget), Is.SameAs(noCompanion));
        }

        [Test]
        public void ExactVectorReusesBytesButTwoDistinctPhaseViewsAreTheLimit()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var first = Render(frame, basis, Select(frame, budget, true, false), budget, PocFramePhase.Tentative);
            var same = Render(frame, basis, Select(frame, budget, true, false), budget, PocFramePhase.Final);
            Assert.That(same, Is.SameAs(first));
            Assert.That(Render(frame, basis, Select(frame, budget, false, true), budget), Is.SameAs(input.Native));
            Assert.That(Render(frame, basis, Select(frame, budget, true, true), budget, PocFramePhase.Tentative), Is.SameAs(input.Native));

            using var two = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var base2 = Retain(two, input.Native, true, true, budget);
            var tentative = Render(two, base2, Select(two, budget, true, false), budget, PocFramePhase.Tentative);
            var final = Render(two, base2, Select(two, budget, false, true), budget);
            Assert.That(final, Is.Not.SameAs(tentative));
            Assert.That(final.Content, Is.EqualTo("[F|global::N.P|B|R|C]"));
            Assert.That(Render(two, base2, Select(two, budget, true, true), budget), Is.SameAs(input.Native));
        }

        [Test]
        public void ReadyFinalBytesCanBeReusedByTentativeWithoutRerendering()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var final = Render(frame, basis, Select(frame, budget, true, false), budget);
            Assert.That(Render(frame, basis, Select(frame, budget, true, false), budget, PocFramePhase.Tentative), Is.SameAs(final));
        }

        [Test]
        public void CacheDoesNotSubstituteChangedLegacyBaseOrWrongFrame()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            var result = Render(frame, basis, selection, budget);
            var changedFile = input.Native.SelectPreparedNames(false, true);
            var changedBase = Retain(frame, changedFile, false, true, budget);
            Assert.That(Render(frame, changedBase, selection, budget), Is.SameAs(changedFile));
            var wrongFlags = Retain(frame, input.Native, false, false, budget);
            Assert.That(Render(frame, wrongFlags, selection, budget, PocFramePhase.Tentative), Is.SameAs(input.Native));
            using var other = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            Assert.That(other.TryReserve(budget, out var ticket), Is.True);
            Assert.That(other.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, ticket), Is.SameAs(input.Native));
            Assert.That(Render(frame, basis, selection, budget), Is.SameAs(result));
        }

        [Test]
        public void SetupSelectionBaseAndAttemptHaveExactAndOneShortBoundaries()
        {
            var input = Mixed();
            long setup = PocFrameWorkReservations.SetupCost(2, 2);
            var shortSetup = new PocWorkBudget((int)setup - 1);
            Assert.That(PocNativeFrame.TryCreate(input.Native, input.Owner, input.Uses, ImmutableArray.Create(true, true), shortSetup, out _), Is.False);
            Assert.That(shortSetup.Remaining, Is.EqualTo(setup - 1));
            Assert.That(shortSetup.Exhausted, Is.False);
            var setupOnly = new PocWorkBudget((int)setup);
            using var setupFrame = Create(input.Native, input.Owner, input.Uses, setupOnly, true, true);
            Assert.That(setupOnly.Remaining, Is.Zero);

            foreach (bool oneShort in new[] { false, true })
            {
                var total = (int)(setup + PocFrameWorkReservations.BaseCost + PocFrameWorkReservations.SelectionCost(2) +
                    PocFrameWorkReservations.AttemptCost(3, 2, 2, Qualified.Length)) - (oneShort ? 1 : 0);
                var budget = new PocWorkBudget(total);
                using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
                var basis = Retain(frame, input.Native, true, true, budget);
                var selection = Select(frame, budget, true, true);
                Assert.That(frame.TryReserve(budget, out var reservation), Is.EqualTo(!oneShort));
                var result = frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, reservation);
                Assert.That(result.Content, Is.EqualTo(oneShort ? Primary : "[F|P|B|R|C]"));
                Assert.That(budget.Exhausted, Is.False);
                if (!oneShort) { Assert.That(budget.Remaining, Is.Zero); }
            }

            foreach (int tail in new[] { PocFrameWorkReservations.BaseCost - 1, PocFrameWorkReservations.BaseCost })
            {
                var budget = new PocWorkBudget((int)setup + tail);
                using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
                Assert.That(frame.TryRetainBase(input.Native, true, true, budget, out _), Is.EqualTo(tail == PocFrameWorkReservations.BaseCost));
            }
            long vectorCost = PocFrameWorkReservations.SelectionCost(2);
            foreach (int tail in new[] { (int)vectorCost - 1, (int)vectorCost })
            {
                var budget = new PocWorkBudget((int)setup + tail);
                using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
                Assert.That(frame.TryCreateSelection(ImmutableArray.Create(true, true), budget, out _), Is.EqualTo(tail == vectorCost));
            }
        }

        [Test]
        public void UnfundedRequestCanRetryWithSameOwnerAndDoesNotPoisonOtherPhase()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            Assert.That(frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Tentative, null), Is.SameAs(input.Native));
            var result = Render(frame, basis, selection, budget, PocFramePhase.Tentative);
            Assert.That(result.Content, Is.EqualTo("[F|P|B|R|C]"));
            Assert.That(frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, null), Is.SameAs(input.Native), "Cache equality supplies neither funding nor authority.");
            Assert.That(Render(frame, basis, selection, budget), Is.SameAs(result));
            Assert.That(frame.TryReserve(new PocWorkBudget(100000), out _), Is.False, "Cannot mint a new per-file allowance.");
        }

        [Test]
        public void LinkedReservationWithdrawsRootCapacityAndOversizedCostsDenyBeforeAllocation()
        {
            var input = Mixed();
            var root = new PocWorkBudget(100000);
            var child = new PocWorkBudget(100000, root);
            using var frame = Create(input.Native, input.Owner, input.Uses, child, true, true);
            var basis = Retain(frame, input.Native, true, true, child);
            var selection = Select(frame, child, true, true);
            int before = root.Remaining;
            Assert.That(frame.TryReserve(child, out var ticket), Is.True);
            Assert.That(root.Remaining, Is.EqualTo(before - frame.AttemptCost));
            Assert.That(root.Remaining, Is.EqualTo(child.Remaining));
            Assert.That(frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, ticket).Content, Is.EqualTo("[F|P|B|R|C]"));
            int remaining = root.Remaining;
            Assert.That(PocFrameWorkReservations.Withdraw(root, PocFrameWorkReservations.AttemptCost(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)), Is.False);
            Assert.That(root.Remaining, Is.EqualTo(remaining));
            Assert.That(root.Exhausted, Is.False);
        }

        [Test]
        public void ZeroCreditIngressAndDisposeDoNotAllocateManagedStorage()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            Assert.That(budget.TrySpend(budget.Remaining), Is.True);
            // Measure warmed managed-allocation behavior only, not a cold CLR/JIT proof.
            _ = frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, null);
            long before = GC.GetAllocatedBytesForCurrentThread();
            CodeFile? result = null;
            for (int i = 0; i < 1024; i++)
            {
                result = frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, null);
            }
            frame.Dispose();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, Is.SameAs(input.Native));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void BaseContextRequiresNameAndReceiptIdentityAndProjectionBytes()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var renamed = new CodeFile(input.Native.Content, "Wrong.cs", sourceConsumption: input.Native.SourceConsumption);
            Assert.That(frame.TryRetainBase(renamed, true, true, budget, out _), Is.False);
            var noReceipt = new CodeFile(input.Native.Content, input.Native.Name);
            Assert.That(frame.TryRetainBase(noReceipt, true, true, budget, out _), Is.False);
            var wrongBytes = new CodeFile("arbitrary bytes", input.Native.Name, sourceConsumption: input.Native.SourceConsumption);
            var retained = Retain(frame, wrongBytes, true, true, budget);
            Assert.That(Render(frame, retained, Select(frame, budget, true, true), budget), Is.SameAs(wrongBytes), "No POC result may certify arbitrary supplied bytes.");
        }

        [Test]
        public void OwnedCopiesSurviveCallerMutationAndFramesStayIndependent()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            var uses = input.Uses.ToArray();
            var required = new[] { true, true };
            Assert.That(PocNativeFrame.TryCreate(input.Native, input.Owner, ImmutableCollectionsMarshal.AsImmutableArray(uses),
                ImmutableCollectionsMarshal.AsImmutableArray(required), budget, out var frame), Is.True);
            var enabled = new[] { true, false };
            Assert.That(frame!.TryCreateSelection(ImmutableCollectionsMarshal.AsImmutableArray(enabled), budget, out var selection), Is.True);
            uses[0] = uses[0] with { PrimaryOffset = -1 };
            required[0] = false;
            enabled[0] = false;
            enabled[1] = true;
            var basis = Retain(frame, input.Native, true, true, budget);
            Assert.That(Render(frame, basis, selection!, budget).Content, Is.EqualTo("[F|P|B|global::N.R|C]"));
            using var other = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var otherBase = Retain(other, input.Native, true, true, budget);
            Assert.That(Render(other, otherBase, Select(other, budget, false, true), budget).Content, Is.EqualTo("[F|global::N.P|B|R|C]"));
            frame.Dispose();
        }

        [Test]
        public void TicketsAreSingleUseClaimsAndDisposalIsTerminal()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            Assert.That(frame.TryReserve(budget, out var first), Is.True);
            Assert.That(frame.TryReserve(budget, out var second), Is.True);
            Assert.That(first, Is.Not.SameAs(second));
            var result = frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, first);
            Assert.That(result.Content, Is.EqualTo("[F|P|B|R|C]"));
            Assert.That(frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, first), Is.SameAs(input.Native));
            frame.Dispose();
            Assert.That(frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Tentative, second), Is.SameAs(input.Native));
            Assert.That(frame.TryReserve(budget, out _), Is.False);
            frame.Dispose();
            Assert.That(result.Content, Is.EqualTo("[F|P|B|R|C]"));
        }

        [Test]
        public void ContentionDeniesWithoutWaitingAndIndependentPhaseWorksAfterRelease()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            Assert.That(frame.TryReserve(budget, out var held), Is.True);
            // Unit simulation of an already owned claim, not a production synchronization hook.
            var field = typeof(PocNativeFrame).GetField("_tentative", BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(frame, held);
            Assert.That(Render(frame, basis, selection, budget, PocFramePhase.Tentative), Is.SameAs(input.Native));
            // Simulate that owner's release; a contender never releases someone else's claim.
            field.SetValue(frame, null);
            Assert.That(Render(frame, basis, selection, budget).Content, Is.EqualTo("[F|P|B|R|C]"));
            frame.Dispose();
            Assert.That(field.GetValue(frame), Is.Not.SameAs(held));
        }

        [Test]
        public void OtherPhaseMaterializationContentionDeniesRatherThanCreatingDuplicateBytes()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            Assert.That(frame.TryReserve(budget, out var inFlight), Is.True);
            // Explicit unit state: another phase owns variable work but has not published bytes.
            var field = typeof(PocNativeFrame).GetField("_tentative", BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(frame, inFlight);
            Assert.That(Render(frame, basis, selection, budget), Is.SameAs(input.Native));
        }

        [Test]
        public void ConcurrentPrepaidRequestsNeverShareScratchOrMintCredits()
        {
            var input = Mixed();
            var budget = new PocWorkBudget(100000);
            using var frame = Create(input.Native, input.Owner, input.Uses, budget, true, true);
            var basis = Retain(frame, input.Native, true, true, budget);
            var selection = Select(frame, budget, true, true);
            var tickets = Enumerable.Range(0, 16).Select(_ => { Assert.That(frame.TryReserve(budget, out var t), Is.True); return t; }).ToArray();
            int remaining = budget.Remaining;
            var results = new CodeFile[16];
            Parallel.For(0, results.Length, i => results[i] = frame.RenderMechanicalSelection(basis, selection, PocFramePhase.Final, tickets[i]));
            Assert.That(results.All(f => ReferenceEquals(f, input.Native) || f.Content == "[F|P|B|R|C]"), Is.True);
            Assert.That(results.Any(f => !ReferenceEquals(f, input.Native)), Is.True);
            Assert.That(budget.Remaining, Is.EqualTo(remaining));
            var views = results.Where(f => !ReferenceEquals(f, input.Native)).ToArray();
            Assert.That(views.All(f => ReferenceEquals(f, views[0])), Is.True);
        }
    }
}

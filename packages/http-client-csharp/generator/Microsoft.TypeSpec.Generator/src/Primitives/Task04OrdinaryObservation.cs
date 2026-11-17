// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // ISOLATED EXPERIMENT ONLY. A prewriter proposal/native-use bridge, NOT a
    // semantic choice, permission, confinement proof, or complete cost ledger.
    // Default Write never selects this entry point. Known capture/frame primitive
    // reservations use the EXISTING generation account. New wrapper observations,
    // allocations, activations and candidate-producing cold compiler work remain
    // explicit unclosed proof obligations; no guessed flat tariff certifies them.
    internal sealed class Task04OrdinaryObservation
    {
        private readonly PreparedFieldTypes _preparation;
        private readonly PocWorkBudget _account;
        private readonly int _retainedCharacters;
        private bool _attached;
        private bool _completed;

        internal Task04OrdinaryObservation(PreparedFieldTypes preparation,
            PreparedFieldTypes.PreparedPocOrdinaryCandidate candidate, PocWorkBudget account)
        {
            _preparation = preparation;
            Candidate = candidate;
            _account = account;
            _retainedCharacters = 2 * candidate.QualifiedSpelling.Length + candidate.SimpleSpelling.Length;
        }

        internal PreparedFieldTypes.PreparedPocOrdinaryCandidate Candidate { get; }
        internal PreparedPocOwnedRootEffect? OwnedRootEffect { get; set; }
        internal PocNativeTypeCaptureUse? NativeUse { get; private set; }
        internal CodeFile? NativeFile { get; private set; }
        internal bool CandidateCurrentAtReturn { get; private set; }
        internal bool HasUncoveredEmission { get; private set; }
        internal bool MechanicalFrameCreated { get; private set; }
        internal bool MechanicalSelectionRetainedNative { get; private set; }
        // Trial2 counters only: same reads in eager/requested controls. No tariff,
        // checks, reservation ordering, enabled vector, or native path is changed.
        internal int BeforeAttachObservation, AfterAttachObservation, AfterCaptureReservation;
        internal int BeforePostWriteObservation, AfterPostWriteObservation, AfterFrameCreation, AfterMechanicalSetup, AfterRender;
        internal string Outcome { get; private set; } = "prepared-observation-only";

        internal void Attach(CodeWriter writer, TypeProviderWriter physicalWriter)
        {
            BeforeAttachObservation = _account.Remaining;
            if (_attached || _completed || !physicalWriter.OwnsPreparation(_preparation, Candidate.File.FileOwner) ||
                !_preparation.ObserveTask04OrdinaryCandidate(Candidate) ||
                Candidate.Occurrence.Producer is not DeclarationExpression declaration)
            {
                Outcome = "attachment-refused";
                return;
            }
            AfterAttachObservation = _account.Remaining;
            _attached = writer.TryEnableNativeTypeCapture(declaration, Candidate.Type, Candidate.Occurrence.Id,
                _account, _retainedCharacters);
            AfterCaptureReservation = _account.Remaining;
            Outcome = _attached ? "native-capture-attached" : "capture-reservation-or-state-refused";
        }

        // Invoked only after ToString, original RelativeFilePath evaluation and
        // CompleteSourceFile. The original native CodeFile is already independent.
        internal CodeFile Complete(CodeFile native, PocNativeTypeCaptureUse? use)
        {
            if (_completed) { return native; }
            _completed = true;
            NativeFile = native;
            NativeUse = use;
            if (!_attached || use is null || !ReferenceEquals(use.Occurrence, Candidate.Occurrence.Id) ||
                !ReferenceEquals(use.Declaration, Candidate.Occurrence.Producer) || !ReferenceEquals(use.Type, Candidate.Type))
            {
                Outcome = "native-use-unavailable-or-unowned";
                return native;
            }
            BeforePostWriteObservation = _account.Remaining;
            CandidateCurrentAtReturn = _preparation.ObserveTask04OrdinaryCandidate(Candidate);
            AfterPostWriteObservation = _account.Remaining;
            HasUncoveredEmission = _preparation.PocGenerationEnvelope?.HasUncoveredEmission != false;
            var alternative = new PocFrameAlternative(this, 0, 0, use.PrimaryStart, use.PrimaryLength,
                use.QualifiedStart, use.QualifiedLength, Candidate.SimpleSpelling, Completed: true);
            if (!PocNativeFrame.TryCreate(native, this, ImmutableArray.Create(alternative), ImmutableArray.Create(true), _account, out var frame))
            {
                Outcome = "mechanical-frame-reservation-refused";
                return native;
            }
            using (frame)
            {
                MechanicalFrameCreated = true;
                AfterFrameCreation = _account.Remaining;
                // No semantic authority factory exists in the accepted inputs.
                // Exercise only the DISABLED vector: this is not a forced coverage
                // flag, a positive control, or an enabled fallback reconstruction.
                if (!frame!.TryRetainBase(native, field: true, body: true, _account, out var basis) ||
                    !frame.TryCreateSelection(ImmutableArray.Create(false), _account, out var selection) ||
                    !frame.TryReserve(_account, out var reservation))
                {
                    AfterMechanicalSetup = _account.Remaining;
                    Outcome = "mechanical-attempt-reservation-refused";
                    return native;
                }
                AfterMechanicalSetup = _account.Remaining;
                var result = frame.RenderMechanicalSelection(basis!, selection!, PocFramePhase.Final, reservation);
                AfterRender = _account.Remaining;
                MechanicalSelectionRetainedNative = ReferenceEquals(result, native);
                Outcome = "captured-target-and-native-use-but-emission-authority-unavailable";
                // This observer may never promote its proposal or enabled vector.
                // Return the independently formatted native object in all cases.
                return native;
            }
        }
    }
}

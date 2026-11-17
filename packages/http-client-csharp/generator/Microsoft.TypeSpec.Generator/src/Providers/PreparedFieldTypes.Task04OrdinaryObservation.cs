// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        // Exact dictionary ownership without GetSourceFile/ObserveSourceSnapshot:
        // those replay host proof callbacks and are not an attachment witness.
        internal bool OwnsTask04EffectSource(PreparedSourceFile file, PocWorkBudget account) =>
            account.TrySpendReferenceLookup(_sourceFiles.Count) && !_sourceDisposed &&
            file.Generation == _sourceGeneration && _sourceFiles.TryGetValue(file.FileOwner, out var owned) &&
            ReferenceEquals(owned, file);

        internal PreparedPocOwnedRootEffect? PrepareTask04OwnedRootEffect(PreparedSourceFile file,
            PreparedSourceOccurrence root, PreparedSourceOccurrence body) =>
            _pocCandidateSourceBudget is { } account
                ? PreparedPocOwnedRootEffect.TryCapture(this, file, root, body, account) : null;

        // Experimental proposal only. This is intentionally NOT an implementation
        // of IPreparedTypeOfNameInspection or a Task03/Task04 permission factory.
        internal Task04OrdinaryObservation? PrepareTask04OrdinaryObservation(
            PreparedPocOrdinaryCandidate candidate)
        {
            if (_pocCandidateSourceBudget is not { } account || candidate.File.HasStarted ||
                candidate.QualifiedSpelling.Length > (int.MaxValue - candidate.SimpleSpelling.Length) / 2 ||
                !ObserveTask04OrdinaryCandidate(candidate))
            {
                return null;
            }
            return new Task04OrdinaryObservation(this, candidate, account);
        }

        // Unlike the diagnostic convenience getter, all three extra observation
        // passes use the SAME finite generation account. No independent budget
        // constructor/reset, compiler binding query or host proof callback here.
        // This removes R0's fresh subledgers, not the remaining wrapper/source-cost
        // certificate and existing cold compiler work obligations.
        internal bool ObserveTask04OrdinaryCandidate(PreparedPocOrdinaryCandidate candidate)
        {
            var budget = _pocCandidateSourceBudget;
            if (budget is null || _pocCandidateSourceCaptureIncomplete ||
                !SourceCurrentnessChecks.OccurrenceLookup(candidate.Occurrence.Id, _pocCandidates.Count, budget) ||
                !_pocCandidates.TryGetValue(candidate.Occurrence.Id, out var captured) || !ReferenceEquals(captured, candidate) ||
                !budget.TrySpendReferenceLookup(_pocOwnedCandidates.Count))
            {
                return false;
            }
            return PocObservedInputsAreCurrent(budget) && IsPocCandidateObservedCurrent(candidate, budget, budget);
        }
    }
}

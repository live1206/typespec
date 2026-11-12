// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator
{
    internal sealed partial class CodeWriter
    {
        private PreparedFieldTypes? _sourcePreparation;
        private TypeProviderWriter? _standardSourceWriter;
        private PreparedSourceFile? _sourceFile;
        private PreparedSourceFile.Cursor? _sourceCursor;
        private PreparedPocSupplementalFile.Cursor? _pocSupplementalCursor;
        private PreparedPocTypeConstraint? _pocTypeConstraint;
        private PreparedPocFileConsumption? _pocFileConsumption;
        private PreparedPocFileConsumption? _pocDeclarationConsumption;
        private PreparedPocFileConsumption? _pocBoundConsumption;
        internal PreparedSourceOccurrence? CurrentSourceOccurrence => _sourceCursor?.Current;
        internal PreparedSourceObservations.PreparedOccurrenceObservations? CurrentSourceObservations => _sourceCursor?.Current is { } occurrence
            ? _sourceFile?.Observations?.GetCurrent(occurrence) : null;

        internal void AttachSourceFile(PreparedFieldTypes preparation, TypeProvider owner, TypeProviderWriter standardWriter)
        {
            if (!standardWriter.OwnsPreparation(preparation, owner)) { return; }
            _standardSourceWriter = standardWriter;
            _sourcePreparation = preparation;
            _sourceFile = preparation.GetSourceFile(owner);
            _sourceCursor = _sourceFile?.Start();
            var pocFile = preparation.PocGenerationEnvelope?.GetFile(owner);
            _pocTypeConstraint = pocFile?.TypeConstraint;
            _pocBoundConsumption = pocFile?.NativeConsumption;
            var consumption = pocFile?.StartNativeConsumption(preparation, standardWriter, this);
            if (consumption is not null)
            {
                _pocFileConsumption = consumption;
                _pocDeclarationConsumption = consumption;
                // A competing failure or disposal can race publication.
                if (!ReferenceEquals(System.Threading.Volatile.Read(ref _pocBoundConsumption), consumption))
                {
                    System.Threading.Interlocked.CompareExchange(ref _pocFileConsumption, null, consumption);
                    System.Threading.Interlocked.CompareExchange(ref _pocDeclarationConsumption, null, consumption);
                    consumption.ReleaseFailureObserver(this);
                }
                else if (consumption.HasFailed) { OnPocConsumptionFailed(consumption); }
            }
            else if (_pocBoundConsumption is { HasFailed: true } failed && !failed.HasParameterSelectionFailure)
            { PublishPendingPocDeclaration(failed); }
            if (_sourceFile is { } file && _sourceCursor is not null)
            { _pocSupplementalCursor = preparation.GetPocSupplementalFile(file)?.Start(preparation, standardWriter); }
        }

        internal bool TryWritePreparedTypeOfOperand(TypeOfExpression producer) =>
            TryWritePreparedNamespaceSite(producer, producer.Type.Namespace);

        internal bool TryWritePreparedLocalDeclarationType(DeclarationExpression producer) =>
            TryWritePreparedNamespaceSite(producer, producer.Variable.Type.Namespace);

        private bool TryWritePreparedNamespaceSite(object producer, string namespaceName)
        {
            if (_sourcePreparation is not { } preparation || _sourceFile is not { } file ||
                _standardSourceWriter?.OwnsPreparation(preparation, file.FileOwner) != true ||
                CurrentSourceOccurrence is not { } occurrence || !ReferenceEquals(occurrence.Producer, producer) ||
                preparation.GetTask03TypeOfNamePermission(file, occurrence) is not { } permission) { return false; }
            // Exactly the namespace the original native type formatter registers.
            UseNamespace(namespaceName);
            if (!permission.IsCurrent || !TryAppendPreparedBodyName(permission.SimpleSpelling, permission.QualifiedSpelling)) { return false; }
            preparation.RegisterBodyNameUse(permission);
            return true;
        }

        internal T ObserveSourceRootProducer<T>(T producer) where T : class
        {
            if (_pocFileConsumption is { } consumption && !consumption.ObserveRootProducer(producer))
            { OnPocConsumptionFailed(consumption); }
            return producer;
        }

        internal void OnPocConsumptionFailed(PreparedPocFileConsumption consumption)
        {
            if (!ReferenceEquals(System.Threading.Volatile.Read(ref _pocBoundConsumption), consumption)) { return; }
            // The notifier may be delayed after taking the observer. The
            // physical writer can also help detach its exact failed sidecar.
            // Release the reverse reference if it has not yet been taken.
            consumption.ReleaseFailureObserver(this);
            // Retain a pending declaration veto when no declaration is in
            // progress and none has yet been missed.
            System.Threading.Interlocked.CompareExchange(ref _pocFileConsumption, null, consumption);
            if (!consumption.HasParameterSelectionFailure && consumption.HasOpenDeclaration)
            { consumption.MarkOpenDeclarationUnavailable(); }
            if (consumption.HasParameterSelectionFailure)
            { System.Threading.Interlocked.CompareExchange(ref _pocDeclarationConsumption, null, consumption); }
            else { PublishPendingPocDeclaration(consumption); }
        }

        private void PublishPendingPocDeclaration(PreparedPocFileConsumption consumption)
        {
            // A declaration or disposal may have cleared the exact reference
            // after the notification's earlier state check. Publish only into
            // an empty slot, then undo this publication if it is now stale.
            System.Threading.Interlocked.CompareExchange(ref _pocDeclarationConsumption, consumption, null);
            if (consumption.HasParameterSelectionFailure ||
                !ReferenceEquals(System.Threading.Volatile.Read(ref _pocBoundConsumption), consumption))
            { System.Threading.Interlocked.CompareExchange(ref _pocDeclarationConsumption, null, consumption); }
        }

        private void DetachRejectedPocDeclaration(PreparedPocFileConsumption consumption)
        {
            if (!consumption.HasParameterSelectionFailure) { return; }
            consumption.ReleaseFailureObserver(this);
            System.Threading.Interlocked.CompareExchange(ref _pocFileConsumption, null, consumption);
            System.Threading.Interlocked.CompareExchange(ref _pocDeclarationConsumption, null, consumption);
        }

        private void ObservePocDeclarationStart(MethodSignatureBase? signature)
        {
            if (_pocDeclarationConsumption is not { } consumption) { return; }
            consumption.BeginDeclaration(signature);
            DetachRejectedPocDeclaration(consumption);
        }

        private void ObservePocParameter(MethodSignatureBase? signature, int ordinal, ParameterProvider? parameter)
        {
            if (_pocDeclarationConsumption is not { } consumption) { return; }
            consumption.ObserveParameter(signature, ordinal, parameter);
            DetachRejectedPocDeclaration(consumption);
        }

        private void ObservePocDeclarationComplete(MethodSignatureBase? signature)
        {
            if (_pocDeclarationConsumption is not { } consumption) { return; }
            consumption.CompleteDeclaration(signature);
            DetachRejectedPocDeclaration(consumption);
        }

        private void ReleasePocFailureObserver()
        {
            var bound = System.Threading.Interlocked.Exchange(ref _pocBoundConsumption, null);
            bound?.ReleaseFailureObserver(this);
            _pocFileConsumption = null;
            _pocDeclarationConsumption = null;
        }

        internal IDisposable BeginSourceRoot(TypeProvider owner, string role, int index)
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            _sourcePreparation?.ObservePocHeaderAfterSourceCallback(_sourceFile);
            var shared = _sourceCursor?.BeginRoot(owner, role, index) ?? PreparedSourceFile.EmptyScope.Instance;
            if (_pocFileConsumption is { } consumption)
            {
                var poc = consumption.BeginRoot(owner, role, index);
                if (consumption.HasFailed) { poc.Dispose(); OnPocConsumptionFailed(consumption); }
                else if (!ReferenceEquals(poc, PreparedSourceFile.EmptyScope.Instance)) { shared = new SupplementalScope(shared, poc); }
            }
            if (role == "type-constraint" && index == 0 && ReferenceEquals(owner, _sourceFile?.FileOwner) && _pocTypeConstraint is { } constraint)
            { return new SupplementalScope(shared, constraint.BeginNativeRoot()); }
            return shared;
        }

        internal IDisposable EnterSourceNode(object producer)
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            _sourcePreparation?.ObservePocHeaderAfterSourceCallback(_sourceFile);
            // The producer was retained before the callback above. Validate that
            // exact object, not just its carrier's potentially restored endpoint.
            _pocTypeConstraint?.ObserveNativeProducer(producer);
            var shared = _sourceCursor?.Enter(producer) ?? PreparedSourceFile.EmptyScope.Instance;
            var supplemental = _pocSupplementalCursor?.Enter(producer, _sourceCursor?.Current) ?? PreparedSourceFile.EmptyScope.Instance;
            var original = ReferenceEquals(supplemental, PreparedSourceFile.EmptyScope.Instance) ? shared : new SupplementalScope(shared, supplemental);
            if (_pocFileConsumption is not { } consumption) { return original; }
            var poc = consumption.Enter(producer);
            if (consumption.HasFailed) { poc.Dispose(); OnPocConsumptionFailed(consumption); return original; }
            return ReferenceEquals(poc, PreparedSourceFile.EmptyScope.Instance) ? original : new SupplementalScope(original, poc);
        }

        internal SourceConsumptionReceipt? CompleteSourceFile()
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            _sourcePreparation?.ObservePocHeaderAfterSourceCallback(_sourceFile);
            if (_pocFileConsumption is { } consumption)
            {
                consumption.Complete();
                if (consumption.HasFailed) { OnPocConsumptionFailed(consumption); }
            }
            if (_sourceFile is null || _sourceCursor is null) { return null; }
            var current = _sourceCursor.Complete();
            var consumed = _sourceCursor.Consumed;
            _pocSupplementalCursor?.Complete(current, consumed);
            return new(_sourceFile.Generation, _sourceFile.FilePath, _sourceFile.Roots.Length,
                consumed, current);
        }

        private sealed class SupplementalScope(IDisposable shared, IDisposable supplemental) : IDisposable
        {
            public void Dispose()
            {
                try { supplemental.Dispose(); }
                finally { shared.Dispose(); }
            }
        }
    }
}

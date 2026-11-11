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
            _pocFileConsumption = pocFile?.StartNativeConsumption(preparation, standardWriter);
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
                preparation.GetTypeOfNamePermission(file, occurrence) is not { } permission) { return false; }
            // Exactly the namespace the original native type formatter registers.
            UseNamespace(namespaceName);
            if (!permission.IsCurrent || !TryAppendPreparedBodyName(permission.SimpleSpelling, permission.QualifiedSpelling)) { return false; }
            preparation.RegisterBodyNameUse(permission);
            return true;
        }

        internal T ObserveSourceRootProducer<T>(T producer) where T : class
        {
            _pocFileConsumption?.ObserveRootProducer(producer);
            return producer;
        }

        private void DetachRejectedPocDeclaration(PreparedPocFileConsumption consumption)
        {
            // A rejected sidecar must not continue input-proportional work on
            // later native parameters or roots. Other observers stay attached.
            if (consumption.HasParameterSelectionFailure && ReferenceEquals(_pocFileConsumption, consumption))
            { _pocFileConsumption = null; }
        }

        private void ObservePocDeclarationStart(MethodSignatureBase? signature)
        {
            if (_pocFileConsumption is not { } consumption) { return; }
            consumption.BeginDeclaration(signature);
            DetachRejectedPocDeclaration(consumption);
        }

        private void ObservePocParameter(MethodSignatureBase? signature, int ordinal, ParameterProvider? parameter)
        {
            if (_pocFileConsumption is not { } consumption) { return; }
            consumption.ObserveParameter(signature, ordinal, parameter);
            DetachRejectedPocDeclaration(consumption);
        }

        private void ObservePocDeclarationComplete(MethodSignatureBase? signature)
        {
            if (_pocFileConsumption is not { } consumption) { return; }
            consumption.CompleteDeclaration(signature);
            DetachRejectedPocDeclaration(consumption);
        }

        internal IDisposable BeginSourceRoot(TypeProvider owner, string role, int index)
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            _sourcePreparation?.ObservePocHeaderAfterSourceCallback(_sourceFile);
            var shared = _sourceCursor?.BeginRoot(owner, role, index) ?? PreparedSourceFile.EmptyScope.Instance;
            if (_pocFileConsumption is { } consumption)
            { shared = new SupplementalScope(shared, consumption.BeginRoot(owner, role, index)); }
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
            return _pocFileConsumption is { } consumption
                ? new SupplementalScope(original, consumption.Enter(producer)) : original;
        }

        internal SourceConsumptionReceipt? CompleteSourceFile()
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            _sourcePreparation?.ObservePocHeaderAfterSourceCallback(_sourceFile);
            _pocFileConsumption?.Complete();
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

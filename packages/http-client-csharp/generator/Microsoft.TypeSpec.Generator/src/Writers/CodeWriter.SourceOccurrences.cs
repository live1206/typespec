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

        internal int PreparedGroupingCount { get; private set; }
        internal bool TryBeginPocGrouping(ValueExpression expression)
        {
            bool used = _qualifiedBuilder is not null && !_atBeginningOfLine && _sourcePreparation is not null &&
                _sourceFile is not null && _sourceCursor?.Current is { } node &&
                _sourcePreparation.TryUsePocGrouping(_sourceFile, node, expression);
            if (used) { PreparedGroupingCount++; }
            return used;
        }

        internal void PocAfterGroupingOpening() => _sourcePreparation?.PocAfterOpening?.Invoke();

        internal void AppendPocGroupingDelimiter(char delimiter)
        {
            // Prefix/children already establish a non-BOL token. The omitted
            // delimiter has no selected bytes, line break or indentation effect.
            if (_qualifiedBuilder is null || _atBeginningOfLine) { throw new System.InvalidOperationException("Probe delimiter precondition"); }
            var destination = _qualifiedBuilder.GetSpan(1);
            destination[0] = delimiter;
            _qualifiedBuilder.Advance(1);
            _qualifiedPayloadLength = checked(_qualifiedPayloadLength + 1);
        }

        private PreparedSourceFile.Cursor? _sourceCursor;
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

        internal IDisposable BeginSourceRoot(TypeProvider owner, string role, int index)
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            return _sourceCursor?.BeginRoot(owner, role, index) ?? PreparedSourceFile.EmptyScope.Instance;
        }

        internal IDisposable EnterSourceNode(object producer)
        {
            _sourcePreparation?.PocNativeWriteObserver?.Invoke(producer);
            _sourcePreparation?.ObserveSourceSnapshot();
            return _sourceCursor?.Enter(producer) ?? PreparedSourceFile.EmptyScope.Instance;
        }

        internal SourceConsumptionReceipt? CompleteSourceFile()
        {
            _sourcePreparation?.ObserveSourceSnapshot();
            if (_sourceFile is null || _sourceCursor is null) { return null; }
            return new(_sourceFile.Generation, _sourceFile.FilePath, _sourceFile.Roots.Length,
                _sourceCursor.Consumed, _sourceCursor.Complete());
        }
    }
}

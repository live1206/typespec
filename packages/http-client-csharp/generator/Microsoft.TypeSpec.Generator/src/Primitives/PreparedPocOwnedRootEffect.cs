// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // ISOLATED POC: a native-consumption component, never member/file/export or
    // spelling authority. Existing traversal/currentness alone cannot seal it.
    // Primitive shape/event work uses the generation source account. Retention,
    // allocations, activations and cleanup are NOT a closed resource certificate.
    internal sealed class PreparedPocOwnedRootEffect
    {
        private readonly PreparedFieldTypes _preparation;
        private readonly PreparedSourceFile _file;
        private readonly PreparedSourceOccurrence _root, _body;
        private readonly MethodProvider _method;
        private readonly long _methodRevision, _ownerRevision;
        private readonly PreparedPocNativeCallEffect _effect;
        private readonly PocWorkBudget _account;
        private readonly ImmutableArray<PreparedPocNativeCallEffect.NativeStep> _nativeSteps;
        private int _nextNative;
        private bool _nativeActive, _nativeComplete;
        private object? _cursor;
        private PreparedPocMethodHeadComponent? _methodHead;

        internal bool IsSelectedMethodHead(PreparedPocMethodHeadComponent head) => ReferenceEquals(_methodHead, head);

        // Explicit opt-in; refusal never promotes the owned body into member authority.
        internal PreparedPocMethodHeadComponent? PrepareMethodHeadComponent()
        {
            if (_cursor is not null || _file.HasStarted || _methodHead is not null || !CheckEffect()) { return null; }
            return _methodHead = PreparedPocMethodHeadComponent.TryCapture(this, _method, _file.DisableXmlDocs, _account);
        }
        internal void BeginDeclaration(MethodSignatureBase? signature)
        { if (_inTargetRoot) { _methodHead?.BeginDeclaration(signature); } }
        internal void CompleteDeclaration(MethodSignatureBase? signature)
        { if (_inTargetRoot) { _methodHead?.CompleteDeclaration(signature); } }
        private int _nextRoot;
        private bool _inTargetRoot, _producerObserved, _inBody, _bodyExited, _rootExited, _completed, _returned;
        internal string? Failure { get; private set; }
        internal int RootEntries { get; private set; }
        internal int ProducerObservations { get; private set; }
        internal int BodyEntries { get; private set; }
        internal int BodyExits { get; private set; }
        internal int RootExits { get; private set; }
        internal int NativeEntries { get; private set; }
        internal int NativeExits { get; private set; }
        internal int ExpectedNativeEntries => _nativeSteps.Length;
        internal bool HasCompleted => _completed && _returned && Failure is null;
        internal CodeFile? NativeFile { get; private set; }
        internal PreparedSourceFile File => _file;
        internal PreparedSourceOccurrence Root => _root;
        internal PreparedSourceOccurrence Body => _body;

        private PreparedPocOwnedRootEffect(PreparedFieldTypes preparation, PreparedSourceFile file,
            PreparedSourceOccurrence root, PreparedSourceOccurrence body, MethodProvider method,
            PreparedPocNativeCallEffect effect, PocWorkBudget account,
            ImmutableArray<PreparedPocNativeCallEffect.NativeStep> nativeSteps)
        {
            _preparation = preparation; _file = file; _root = root; _body = body;
            _method = method; _methodRevision = method.DeclarationRevision;
            _ownerRevision = file.FileOwner.DeclarationRevision; _effect = effect; _account = account; _nativeSteps = nativeSteps;
        }

        internal static PreparedPocOwnedRootEffect? TryCapture(PreparedFieldTypes preparation, PreparedSourceFile file,
            PreparedSourceOccurrence root, PreparedSourceOccurrence body, PocWorkBudget account)
        {
            // Only already-owned plans: no body getter/materialization or host callback.
            if (file.HasStarted || !preparation.OwnsTask04EffectSource(file, account) ||
                root.Id.Generation != file.Generation || body.Id.Generation != file.Generation ||
                !account.TrySpendStringComparison(root.Id.File, file.FilePath) ||
                !account.TrySpendStringComparison(body.Id.File, file.FilePath) ||
                root.Id.RootRole != "method" || root.Producer is not MethodProvider method ||
                !ReferenceEquals(method.EnclosingType, file.FileOwner) ||
                method.BodyStatements is not MethodBodyStatement statement || !ReferenceEquals(body.Producer, statement) ||
                !ContainsRoot(file, root, account) || !ContainsBody(root, body, account) ||
                !file.IsShapeCurrentWithoutInvalidating(account)) { return null; }
            var effect = PreparedPocNativeCallEffect.TryCaptureStatement(statement, file.DisableXmlDocs, account, out _);
            if (effect is null || !effect.TryCreateNativeSteps(account, out var nativeSteps) || !account.TrySpend()) { return null; }
            return new(preparation, file, root, body, method, effect, account, nativeSteps);
        }

        private static bool ContainsRoot(PreparedSourceFile file, PreparedSourceOccurrence root, PocWorkBudget account)
        {
            foreach (var item in file.Roots)
            { if (!account.TrySpend()) { return false; } if (ReferenceEquals(item, root)) { return true; } }
            return false;
        }
        private static bool ContainsBody(PreparedSourceOccurrence root, PreparedSourceOccurrence body, PocWorkBudget account)
        {
            // A method's directly owned block body, not a detached matching producer.
            foreach (var child in root.Children)
            { if (!account.TrySpend()) { return false; } if (ReferenceEquals(child, body)) { return true; } }
            return false;
        }

        internal void Reject(string reason) { Failure ??= reason; }
        private bool Spend()
        {
            if (Failure is not null) { return false; }
            if (_account.TrySpend()) { return true; }
            Reject("source-budget"); return false;
        }
        private bool CheckEffect()
        {
            if (!Spend()) { return false; }
            if (_file.IsInvalid || _method.DeclarationRevision != _methodRevision ||
                _file.FileOwner.DeclarationRevision != _ownerRevision ||
                !ReferenceEquals(_method.BodyStatements, _body.Producer) || !_effect.IsCurrent(_account) ||
                _methodHead is not null && !_methodHead.IsCurrent())
            { Reject("changed-owned-effect"); return false; }
            return true;
        }

        internal void Attach(PreparedSourceFile file, object cursor, TypeProviderWriter? writer)
        {
            if (!Spend()) { return; }
            if (_cursor is not null || !ReferenceEquals(file, _file) || writer is null ||
                !writer.HasPhysicalWriteEntry || !writer.OwnsPreparation(_preparation, _file.FileOwner))
            { Reject("file-generation-writer-or-duplicate-attachment"); return; }
            _cursor = cursor;
            CheckEffect();
        }

        internal void BeginRoot(object cursor, PreparedSourceOccurrence root)
        {
            if (!Spend()) { return; }
            if (!ReferenceEquals(cursor, _cursor) || _completed || _nextRoot >= _file.Roots.Length ||
                !ReferenceEquals(root, _file.Roots[_nextRoot++])) { Reject("root-order-or-owner"); return; }
            if (!ReferenceEquals(root, _root)) { return; }
            if (_inTargetRoot || RootEntries != 0 || !CheckEffect()) { Reject("duplicate-or-changed-root"); return; }
            RootEntries++; _inTargetRoot = true;
        }
        internal void ObserveProducer(object producer)
        {
            if (!Spend() || !_inTargetRoot) { return; }
            if (_producerObserved || !ReferenceEquals(producer, _root.Producer) || !CheckEffect())
            { Reject("root-producer"); return; }
            _producerObserved = true; ProducerObservations++;
        }
        internal void Enter(PreparedSourceOccurrence occurrence)
        {
            if (!Spend() || !ReferenceEquals(occurrence, _body)) { return; }
            if (!_inTargetRoot || !_producerObserved || _inBody || BodyEntries != 0 || !CheckEffect())
            { Reject("body-order-or-change"); return; }
            _inBody = true; BodyEntries++;
        }
        internal void Exit(PreparedSourceOccurrence occurrence)
        {
            if (!Spend() || !ReferenceEquals(occurrence, _body)) { return; }
            if (!_inBody || _bodyExited || !CheckEffect()) { Reject("body-exit-or-change"); return; }
            _inBody = false; _bodyExited = true; BodyExits++;
        }
        // Called for EVERY real native source-node event, including children
        // hidden behind the old cursor's opaque boundary. No producer replay.
        internal IDisposable? EnterNative(object producer)
        {
            if (Failure is not null) { return null; }
            if (_inTargetRoot) { _methodHead?.ObserveDocumentation(producer); }
            if (!_nativeActive && !ReferenceEquals(producer, _body.Producer)) { return null; }
            if (!Spend()) { return null; }
            if (!_nativeActive)
            {
                if (!_inBody || _nativeComplete || _nextNative != 0) { Reject("native-body-phase"); return null; }
                _nativeActive = true;
            }
            if (_nextNative >= _nativeSteps.Length || !ReferenceEquals(_nativeSteps[_nextNative].Producer, producer))
            { Reject("native-child-source-or-order"); return null; }
            var index = _nextNative++;
            var end = _nativeSteps[index].End;
            NativeEntries++;
            return new NativeScope(() =>
            {
                if (!Spend()) { return; }
                if (_nextNative != end) { Reject("native-child-missing-or-duplicate"); return; }
                NativeExits++;
                if (index == 0) { _nativeActive = false; _nativeComplete = true; }
            });
        }

        private sealed class NativeScope(Action end) : IDisposable
        {
            private Action? _end = end;
            public void Dispose() { var action = _end; _end = null; action?.Invoke(); }
        }

        internal void EndRoot(PreparedSourceOccurrence root)
        {
            if (!Spend() || !ReferenceEquals(root, _root)) { return; }
            if (!_inTargetRoot || !_producerObserved || !_bodyExited || _inBody || !_nativeComplete || _nativeActive ||
                _methodHead is not null && !_methodHead.HasNativeHead || !CheckEffect())
            { Reject("root-incomplete-or-change"); return; }
            _inTargetRoot = false; _rootExited = true; RootExits++;
        }
        // Last check after the observer's original post-write callbacks, directly
        // before raw return. No host proof callback is replayed by this check.
        internal void ConfirmRawReturn(CodeFile native)
        {
            if (!Spend()) { return; }
            if (!_completed || _returned || native.SourceConsumption is not { WasCurrentAtEndOfWrite: true } receipt ||
                receipt.Generation != _file.Generation || receipt.RootCount != _file.Roots.Length ||
                !_account.TrySpendStringComparison(receipt.File, _file.FilePath) ||
                !_account.TrySpendStringComparison(native.Name, _file.FilePath) ||
                !_preparation.OwnsTask04EffectSource(_file, _account) || !CheckEffect())
            { Reject("raw-return-source-or-phase"); return; }
            NativeFile = native; _returned = true;
        }

        internal void Complete(object cursor)
        {
            if (!Spend()) { return; }
            if (!ReferenceEquals(cursor, _cursor) || _completed || _file.IsInvalid || !_rootExited || _inTargetRoot || _inBody ||
                _nextRoot != _file.Roots.Length || !CheckEffect()) { Reject("file-incomplete-or-change"); return; }
            _completed = true;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // K=1. Allocated only after setup + retention + transfer + cleanup prepayment.
    // A unique instance is also the window/drain epoch: it is never reinstalled.
    internal sealed class PocNativeTypeCapture(
        DeclarationExpression declaration, CSharpType expectedType, SourceOccurrenceId occurrence,
        int slot, int retainedCharacters)
    {
        // Fixed prepayment: 128 state setup, 256 immutable snapshot allocation/
        // field transfer, 256 terminal cleanup, 384 checks/rebasing/slot slack.
        // Retained native string characters are reserved separately, up front.
        internal const int SetupWork = 1024;
        internal readonly DeclarationExpression Declaration = declaration;
        private readonly CSharpType _expectedType = expectedType;
        private readonly SourceOccurrenceId _occurrence = occurrence;
        private readonly int _slot = slot;
        private int _characters = retainedCharacters;
        internal bool Started;
        internal bool Active;
        internal bool Completed;
        private bool _appendEntered;
        private bool _dispatched;
        private bool _root;
        private bool _rootReturned;
        // null = live, this = exact publishing claim, expected type = terminal,
        // snapshot = ready. These already-owned identities require no marker objects.
        private object? _publication;
        private int _drained;
        internal int PrimaryPayloadLength;
        internal bool PrimaryPayloadOverflow;
        internal List<PreparedNameAlternative>? Alternatives;
        internal bool Drained => Volatile.Read(ref _drained) != 0;
        internal PocNativeTypeCaptureUse? Published => Volatile.Read(ref _publication) as PocNativeTypeCaptureUse;
        private PocNativeTypeSite _next = PocNativeTypeSite.Global;
        private PocNativeTypeFacts _observed;
        private PocNativeTypeFacts _true;
        private string? _registration;
        private string? _namespace;
        private string? _name;
        private int _primaryStart;
        private int _qualifiedStart;
        private int _primaryLength;
        private int _qualifiedLength;
        private bool _aliases;

        internal void AdvancePrimary(int length)
        {
            if (Drained) { return; }
            if (length < 0 || length > int.MaxValue - PrimaryPayloadLength)
            {
                PrimaryPayloadOverflow = true;
                Abandon();
                return;
            }
            PrimaryPayloadLength += length;
        }

        internal bool Drain()
        {
            if (Interlocked.Exchange(ref _drained, 1) != 0)
            {
                // Repeated/reentrant drains clear the view and invalidate any
                // earlier drain still finalizing headers, without allocating.
                Interlocked.Exchange(ref _publication, _expectedType);
                Abandon();
                return false;
            }
            if (Active) { Abandon(); }
            PrimaryPayloadLength = 0;
            PrimaryPayloadOverflow = false;
            return true;
        }

        internal void Release()
        {
            Interlocked.Exchange(ref _drained, 1);
            // Preserve a previously ready immutable snapshot. Invalidate either
            // live or publishing state in at most two CAS operations, no retry.
            var previous = Interlocked.CompareExchange(ref _publication, _expectedType, null);
            if (ReferenceEquals(previous, this))
            {
                Interlocked.CompareExchange(ref _publication, _expectedType, this);
            }
            Abandon();
        }

        internal void Abandon()
        {
            Active = false;
            Completed = false;
            _registration = null;
            _namespace = null;
            _name = null;
        }

        internal void AppendEntry()
        {
            if (!Active) { return; }
            if (_appendEntered) { Abandon(); return; }
            _appendEntered = true;
        }

        internal void Dispatch(CSharpType type, bool isDeclaration)
        {
            if (!Active) { return; }
            if (_dispatched || isDeclaration || !ReferenceEquals(type, _expectedType)) { Abandon(); return; }
            _dispatched = true;
        }

        internal void Root(CSharpType type, bool isDeclaration, bool nameOnly, int depth)
        {
            if (!Active) { return; }
            if (!_dispatched || _root || !ReferenceEquals(type, _expectedType) || isDeclaration || nameOnly || depth != 0)
            { Abandon(); return; }
            _root = true;
        }

        internal void Fact(PocNativeTypeFacts fact, bool value)
        {
            if (!Active) { return; }
            if (!_root || _rootReturned) { Abandon(); return; }
            _observed |= fact;
            if (value) { _true |= fact; }
            if (value && (fact & (PocNativeTypeFacts.IsArray | PocNativeTypeFacts.FriendlyName |
                PocNativeTypeFacts.HasDeclaringType | PocNativeTypeFacts.HasArguments)) != 0) { Abandon(); }
        }

        private bool Retain(string? value)
        {
            int length = value?.Length ?? 0;
            if (length > _characters) { Abandon(); return false; }
            _characters -= length;
            return true;
        }

        internal void Registration(string? value)
        {
            if (!Active) { return; }
            if (!_root || _rootReturned || _next != PocNativeTypeSite.Global || _registration is not null)
            { Abandon(); return; }
            if (Retain(value)) { _registration = value; }
        }

        internal void Text(string? value, PocNativeTypeSite site)
        {
            if (!Active) { return; }
            if (!_root || _rootReturned || site != _next) { Abandon(); return; }
            if (site == PocNativeTypeSite.Namespace && Retain(value)) { _namespace = value; }
            else if (site == PocNativeTypeSite.Name && Retain(value)) { _name = value; }
        }

        internal void Chunk(PocNativeTypeSite site, int primary, int qualified, bool aliases)
        {
            if (!Active) { return; }
            if (!_root || _rootReturned || site != _next || site == PocNativeTypeSite.Foreign)
            { Abandon(); return; }
            if (site == PocNativeTypeSite.Global)
            {
                _primaryStart = primary;
                _qualifiedStart = qualified;
                _aliases = aliases;
            }
            _next = site + 1;
        }

        internal void RootReturned(int primary, int qualified)
        {
            if (!Active) { return; }
            const PocNativeTypeFacts required = PocNativeTypeFacts.IsArray | PocNativeTypeFacts.FriendlyName |
                PocNativeTypeFacts.HasDeclaringType | PocNativeTypeFacts.HasArguments;
            if (_next != PocNativeTypeSite.End || (_observed & required) != required ||
                primary < _primaryStart || qualified < _qualifiedStart)
            { Abandon(); return; }
            _primaryLength = primary - _primaryStart;
            _qualifiedLength = qualified - _qualifiedStart;
            _rootReturned = true;
        }

        internal void End(bool success)
        {
            if (!Active) { return; }
            if (!success || !_rootReturned) { Abandon(); return; }
            Active = false;
            Completed = true;
        }

        internal PocNativeTypeCaptureUse? Publish(int header, long actualPrimary, int primary, bool overflow)
        {
            if (!Completed || overflow || actualPrimary != primary || header > int.MaxValue - _primaryStart ||
                header > int.MaxValue - _qualifiedStart) { return null; }
            // Claim BEFORE allocating: a competing publisher cannot allocate a
            // second snapshot. Disposal/drain can invalidate this exact token.
            if (Interlocked.CompareExchange(ref _publication, this, null) is not null) { return null; }
            var snapshot = new PocNativeTypeCaptureUse(_occurrence, _slot, Declaration, _expectedType, false, false, 0,
                _observed, _true, _registration, _namespace, _name,
                header + _primaryStart, _primaryLength, header + _qualifiedStart, _qualifiedLength, _aliases, 0);
            return ReferenceEquals(Interlocked.CompareExchange(ref _publication, snapshot, this), this) ? snapshot : null;
        }
    }

    [Flags]
    internal enum PocNativeTypeFacts
    {
        None = 0,
        IsArray = 1,
        ArrayHasGenericArguments = 2,
        FriendlyName = 4,
        IsFrameworkType = 8,
        HasDeclaringType = 16,
        HasArguments = 32,
        WritingNewInstance = 64,
        PositiveGenericDepth = 128,
        IsNullable = 256,
        IsValueType = 512
    }

    internal enum PocNativeTypeSite { Foreign, Global, Namespace, Dot, Name, End }
}

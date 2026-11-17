// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator
{
    internal sealed partial class CodeWriter
    {
        // The original legacy reference slot is a private union: null (fresh),
        // original List, _scopes (setup claimed/denied), this (disposed), or paid
        // capture state. No optional storage is added to native writer instances.
        private PocNativeTypeCapture? NativeTypeCapture => Volatile.Read(ref _nameAlternatives) as PocNativeTypeCapture;
        private int PrimaryPayloadLength => NativeTypeCapture?.PrimaryPayloadLength ?? 0;
        internal PocNativeTypeCaptureUse? NativeTypeUse => NativeTypeCapture?.Published;

        // Observation-only, explicitly opt-in. The caller already owns these real
        // identities and exclusively supplies an existing generation-linked ledger.
        // One installation attempt, one slot, one drain; no native concurrent writing.
        // The installing owner must supply its already-audited ledger chain;
        // TrySpend is paid setup, never part of native constant ingress.
        // No discovery, choice creation, new budget, or spelling permission is implied.
        internal bool TryEnableNativeTypeCapture(DeclarationExpression declaration, CSharpType expectedType,
            SourceOccurrenceId occurrence, PocWorkBudget budget, int retainedCharacters, int slot = 0)
        {
            // Only the claimant can debit the non-thread-safe ledger. The existing
            // sticky payload flag also denies setup after a legacy list is drained.
            if (Interlocked.CompareExchange(ref _nameAlternatives, _scopes, null) is not null) { return false; }
            if (!ReferenceEquals(Volatile.Read(ref _nameAlternatives), _scopes) || _hasWrittenPayload ||
                declaration is null || expectedType is null || occurrence is null || budget is null ||
                retainedCharacters < 0 || retainedCharacters > int.MaxValue - PocNativeTypeCapture.SetupWork) { return false; }
            if (!budget.TrySpend(PocNativeTypeCapture.SetupWork + retainedCharacters)) { return false; }
            var capture = new PocNativeTypeCapture(declaration, expectedType, occurrence, slot, retainedCharacters);
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _nameAlternatives, capture, _scopes), _scopes))
            {
                capture.Release();
                return false;
            }
            return true;
        }

        internal PocNativeTypeCapture? BeginNativeTypeCapture(DeclarationExpression declaration)
        {
            var capture = NativeTypeCapture;
            if (capture is null || capture.Drained) { return null; }
            if (capture.Active) { capture.Abandon(); return null; }
            if (capture.Started || !ReferenceEquals(capture.Declaration, declaration)) { return null; }
            capture.Started = true;
            capture.Active = true;
            return capture;
        }

        internal void EndNativeTypeCapture(PocNativeTypeCapture? capture, bool success)
        {
            if (capture is not null && !capture.Drained && ReferenceEquals(NativeTypeCapture, capture)) { capture.End(success); }
        }

        private bool ObserveNativeTypeFact(PocNativeTypeFacts fact, bool value)
        {
            NativeTypeCapture?.Fact(fact, value);
            return value;
        }

        private void RegisterNativeTypeNamespace(string value)
        {
            NativeTypeCapture?.Registration(value);
            UseNamespace(value);
        }

        private void AppendNativeTypeChunk(string value, PocNativeTypeSite site)
        {
            NativeTypeCapture?.Text(value, site);
            AppendRaw(value.AsSpan(), value.AsSpan(), nativeSite: site);
        }

        private void AdvancePrimaryPayload(int length) => NativeTypeCapture?.AdvancePrimary(length);

        private int NativeQualifiedPosition => _qualifiedBuilder is null ? PrimaryPayloadLength : _qualifiedPayloadLength;

        private PocNativeTypeCapture? DrainNativeTypeCapture(out int primary, out bool overflow)
        {
            var capture = NativeTypeCapture;
            primary = capture?.PrimaryPayloadLength ?? 0;
            overflow = capture?.PrimaryPayloadOverflow ?? false;
            return capture is not null && capture.Drain() ? capture : null;
        }

        private List<PreparedNameAlternative> GetNameAlternatives()
        {
            if (_nameAlternatives is PocNativeTypeCapture capture) { return capture.Alternatives ??= new(); }
            if (_nameAlternatives is List<PreparedNameAlternative> alternatives) { return alternatives; }
            // An alternative requires native payload, whose sticky flag now denies
            // any later setup even when this original list is taken at drain.
            var result = new List<PreparedNameAlternative>();
            _nameAlternatives = result;
            return result;
        }

        private List<PreparedNameAlternative>? TakeNameAlternatives()
        {
            if (_nameAlternatives is PocNativeTypeCapture capture)
            {
                var result = capture.Alternatives;
                capture.Alternatives = null;
                return result;
            }
            if (_nameAlternatives is not List<PreparedNameAlternative> alternatives) { return null; }
            _nameAlternatives = null;
            return alternatives;
        }

        private void ReleaseNativeTypeCapture()
        {
            // At most two CAS operations: close fresh, then a raced setup claim.
            // If installation wins either race, invalidate its paid state instead.
            // Native List ownership is unchanged; sticky payload already denies setup.
            var previous = Interlocked.CompareExchange(ref _nameAlternatives, this, null);
            if (ReferenceEquals(previous, _scopes))
            {
                previous = Interlocked.CompareExchange(ref _nameAlternatives, this, _scopes);
            }
            if (previous is PocNativeTypeCapture capture) { capture.Release(); }
        }
    }
}

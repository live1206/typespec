// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Explicit opt-in lifecycle phase: audited headers become canonical BEFORE
    // source capture/name planning. This is not grammar or spelling permission.
    internal sealed class PreparedPocHeaderFinalization : IDisposable
    {
        private CodeModelGenerator? _generator;
        private bool _disposed;
        internal PreparedPocHeaderFinalization(CodeModelGenerator? generator = null) => _generator = generator ?? CodeModelGenerator.Instance;

        internal bool IsActiveFor(CodeModelGenerator generator) => !_disposed && ReferenceEquals(generator, _generator);

        // This lease owns only the already-approved header lifecycle, never
        // source/name permission. A discarded name plan cannot revoke it.
        internal bool CanConsumeCanonicalAttributes(TypeProvider owner, TypeProviderWriter writer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return writer.GetType() == typeof(TypeProviderWriter) && writer.HasPhysicalWriteEntry && ReferenceEquals(CodeModelGenerator.Instance, _generator) &&
                Get(owner)?.CanUseCanonicalAttributes == true;
        }

        public void Dispose()
        {
            _disposed = true;
            _headers.Clear(); // Never clear, restore, or rebuild native provider caches.
            _generator = null;
        }

        private readonly Dictionary<TypeProvider, Header> _headers = new(ReferenceEqualityComparer.Instance);
        private const int PhaseWorkLimit = 1048576;
        private readonly PocWorkBudget _budget = new(PhaseWorkLimit);
        internal int Count => _headers.Count;
        internal Header? Get(TypeProvider owner)
        {
            if (_disposed) { return null; }
            // Every insertion spends more than one phase unit, so the number
            // of retained entries is strictly below this fixed read limit.
            // Fund each point lookup separately: exhaustion of preparation
            // must not revoke an already-completed canonical operation.
            var lookup = new PocWorkBudget(PhaseWorkLimit);
            if (!lookup.TrySpendReferenceLookup(_headers.Count))
            { throw new InvalidOperationException("The bounded header-owner inventory was exceeded."); }
            return _headers.TryGetValue(owner, out var header) ? header : null;
        }

        internal void Materialize(TypeProvider owner, InputNamespace? loadedInput,
            PreparedPocPostMapAttributeBoundary? postMapBoundary = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(CodeModelGenerator.Instance, _generator) || _budget.Exhausted || !_budget.TrySpendReferenceLookup(_headers.Count) || _headers.ContainsKey(owner) ||
                !_budget.TrySpendReferenceLookup(_headers.Count) || owner.CachedType is null || owner.CachedDeclarationModifiers is not { } modifiers ||
                owner.CachedDeclaringTypeProvider is not null || !owner.HasNonExperimentalHeaderInputs) { return; }
            // Native XML normalization and composite writing parse numeric
            // indices through the current culture. Unknown culture dispatch
            // cannot be moved ahead of native documentation/attribute order.
            // Inspect identity only: do not warm NumberFormat/CompareInfo here.
            var headerCulture = System.Globalization.CultureInfo.CurrentCulture;
            if (headerCulture.GetType() != typeof(System.Globalization.CultureInfo)) { return; }
            // Client/enum fallback descriptions use the cached type name as a
            // string argument. Bound it BEFORE any audit or description builder.
            var name = owner.CachedType.Name;
            if (name is null || name.Length > 4096 || !_budget.TrySpend(name.Length) || name.IndexOf('<') >= 0) { return; }
            var writeDocs = owner.PreserveTypeXmlDocs || modifiers.HasFlag(TypeSignatureModifiers.Public);
            bool coldDescription = writeDocs && owner.CachedXmlDocs is null && owner.CachedDescription is null;
            if (!_budget.TrySpend()) { return; }
            var descriptionInputs = ReadDescriptionInputs(owner);
            System.Globalization.CultureInfo? descriptionCulture = null;
            if (descriptionInputs is { } inputs)
            {
                // Pay the audited raw-input scans and, for the cold helper
                // path, conversion/normalization BEFORE a cache can change.
                if (!PreparedPocColdDescription.TryReserveWithCulture(inputs, coldDescription, _budget, out descriptionCulture)) { return; }
            }
            else if (!_budget.TrySpend(8192)) { return; }
            // The final documentation envelope has its own bounded capture.
            if (writeDocs && !_budget.TrySpend(4096)) { return; }
            if (!writeDocs && Builder(owner, "get_ShouldWriteTypeXmlDocs") != typeof(TypeProvider)) { return; }
            if (!Audited(owner, VerifiedCachedInput(loadedInput), writeDocs)) { return; }
            if (writeDocs && owner.CachedXmlDocs is null && owner.CachedDescription is { } description)
            {
                // Capture and per-occurrence normalization work are separately
                // bounded at 4096; reserve both before inspecting the cached tree.
                if (!_budget.TrySpend(8192) ||
                    PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderDescription(description) is not { IsCurrent: true }) { return; }
            }

            // An unsupported cached document can throw during native writing
            // before attributes are rebuilt. Prove it before any early builder,
            // reserving its post-warning currentness pass separately.
            var cachedDocs = writeDocs ? owner.CachedXmlDocs : null;
            PreparedGeneratedDocumentationEnvelope? documentation = null;
            if (cachedDocs is not null && (!_budget.TrySpend(4096) ||
                (documentation = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(cachedDocs)) is null)) { return; }

            Func<object, object?>? descriptionTypeOwnerReader = null;
            TypeProvider? descriptionTypeOwner = null;
            CSharpType? descriptionParent = null;
            PreparedGeneratedDocumentationEnvelope? descriptionTypeInputs = null;
            if (coldDescription && PocHeaderFinalizationBridge.Get(owner) is { } descriptionAudit)
            {
                descriptionTypeOwnerReader = descriptionAudit.DescriptionTypeOwner ?? descriptionAudit.RequiredTypeOwner;
                descriptionTypeOwner = descriptionTypeOwnerReader?.Invoke(owner) as TypeProvider;
                descriptionParent = descriptionTypeOwner?.CachedType;
                var extra = descriptionAudit.AdditionalDescriptionType;
                if (extra is not null && extra is not CSharpType) { return; }
                if (descriptionParent is not null || extra is not null)
                {
                    // Fund the combined 4096 capture and its 4096 currentness
                    // readback BEFORE warnings or documentation can change.
                    if (!_budget.TrySpend(8192) ||
                        (descriptionTypeInputs = PreparedGeneratedDocumentationEnvelope.TryCaptureForHeaderTypeArguments(
                            descriptionParent, (CSharpType?)extra)) is null) { return; }
                }
            }

            // Reserve bounded warning work BEFORE copying cached data or calling
            // an audited builder. Per-header and aggregate bounds both apply.
            // The normal warning getter is NOT called (its merge uses a writer).
            if (!_budget.TrySpend(4096)) { return; }
            if (!owner.TryFinalizePocFileWarnings(new PocWorkBudget(4096), out var warnings, out var suppression)) { return; }
            if (coldDescription && descriptionInputs is { } captured)
            {
                // The recheck was reserved before warnings. It reads only
                // the same audited cached fields, without re-running scans.
                if (owner.CachedDescription is not null || owner.CachedXmlDocs is not null ||
                    ReadDescriptionInputs(owner) is not { } current ||
                    !ReferenceEquals(current.Summary, captured.Summary) || !ReferenceEquals(current.Doc, captured.Doc) ||
                    !ReferenceEquals(current.FallbackName, captured.FallbackName) || current.FallbackCharacters != captured.FallbackCharacters) { return; }
            }
            if (coldDescription && descriptionTypeOwnerReader is not null &&
                (!ReferenceEquals(descriptionTypeOwnerReader(owner), descriptionTypeOwner) ||
                    !ReferenceEquals(descriptionTypeOwner?.CachedType, descriptionParent))) { return; }
            if (descriptionTypeInputs is not null && !descriptionTypeInputs.IsCurrent) { return; }
            // Point recheck after warning processing and immediately before the
            // native getter. This is not locking or culture-ABA protection.
            if (!ReferenceEquals(System.Globalization.CultureInfo.CurrentCulture, headerCulture) ||
                !PreparedPocColdDescription.IsCultureCurrent(descriptionCulture)) { return; }
            if (cachedDocs is not null && (!ReferenceEquals(owner.CachedXmlDocs, cachedDocs) || documentation?.IsCurrent != true)) { return; }
            var docs = writeDocs ? owner.CachedXmlDocs ?? owner.XmlDocs : null;
            // Use the prepaid final capture before attributes, never as a
            // post-effect veto. Retain the same envelope rather than recapture.
            if (docs is not null && documentation is null &&
                (documentation = PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs)) is null) { return; }
            if (owner.GetType() == typeof(ChangeTrackingDictionaryDefinition)) { _ = owner.WhereClause; }
            var attributes = CaptureAttributes(owner, postMapBoundary, out var canonicalAttributes);
            _headers.Add(owner, new Header(owner, writeDocs, docs, documentation, warnings, suppression!, attributes, canonicalAttributes));
        }

        private static PocHeaderFinalizationBridge.ColdDescriptionInputs? ReadDescriptionInputs(TypeProvider owner)
        {
            if (PocHeaderFinalizationBridge.Get(owner)?.DescriptionInputs is { } read) { return read(owner); }
            if (owner.GetType() == typeof(FixedEnumProvider) && owner.HeaderInputType is InputEnumType input)
            { return new(input.Summary, input.Doc, 0); }
            return null;
        }

        // Exact audited rebuilding providers need a complete current recipe
        // BEFORE their one native build at this post-map boundary. Neither a
        // same-generator cache nor post-call output inspection is preflight.
        // Unknown/unsupported recipes retain native write-time rebuilding.
        internal const int AttributeEnvelopeLimit = 4096;
        private const int AttributeEnvelopeReservation = AttributeEnvelopeLimit * 2;

        private PreparedPocAttributeEnvelope? CaptureAttributes(TypeProvider owner,
            PreparedPocPostMapAttributeBoundary? postMapBoundary, out bool canonical)
        {
            canonical = false;
            var policy = Builder(owner, "get_ShouldAnalyzeAttributesInReferenceMap");
            var builder = Builder(owner, "BuildAttributes");
            var backCompatibility = owner.GetType().GetMethod("BuildAttributesForBackCompatibility", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(IReadOnlyList<MethodBodyStatement>)], null)?.DeclaringType;
            var audit = PocHeaderFinalizationBridge.GetCachedAttributes(owner);
            if (policy != typeof(TypeProvider))
            {
                if (audit is null || policy != audit.AnalysisPolicy || builder != audit.Builder ||
                    backCompatibility != audit.BackCompatibilityBuilder || audit.SafeDependencies?.Invoke(owner) != true)
                {
                    return null;
                }

                if (!TryCapturePostMapCanonicalAttributes(owner, postMapBoundary, audit, out var cache))
                {
                    return null;
                }

                canonical = true;
                return CaptureCanonicalAttributeEnvelope(owner, cache, ref canonical, reservationHeld: true);
            }

            if ((builder != typeof(TypeProvider) || backCompatibility != typeof(TypeProvider)) &&
                (audit is null || builder != audit.Builder || backCompatibility != audit.BackCompatibilityBuilder || policy != audit.AnalysisPolicy))
            {
                return null;
            }

            return owner.CachedWriteAttributes is { } defaultCache
                ? CaptureCanonicalAttributeEnvelope(owner, defaultCache, ref canonical)
                : null;
        }

        private bool TryCapturePostMapCanonicalAttributes(TypeProvider owner,
            PreparedPocPostMapAttributeBoundary? postMapBoundary, PocHeaderFinalizationBridge.CachedAttributeAudit audit,
            out IReadOnlyList<MethodBodyStatement> cache)
        {
            cache = [];
            const int recipeLimit = 262144;
            // Keep the recipe's own cap while charging its actual preflight,
            // native-work and post-build verification reservations to the
            // aggregate phase before the native builder can change the cache.
            // The final envelope is also reserved before any native rebuild.
            if (postMapBoundary is null || !_budget.TrySpend(AttributeEnvelopeReservation)) { return false; }
            var budget = new PocWorkBudget(recipeLimit, _budget);
            var recipe = PreparedPocContextAttributeRecipe.TryCapture(owner, postMapBoundary, budget);
            if (recipe is null || audit.SafeDependencies?.Invoke(owner) != true ||
                !recipe.TryReservePostBuildCurrentness(budget, out var postBuildBudget)) { return false; }
            var previousOrigin = owner.PocNativeAttributeCacheOrigin;

            // One native rebuild, only AFTER the complete negative recipe has
            // proved all reachable work pure and bounded. No source callback,
            // output planning, writer replay or parallel attribute emitter.
            _ = owner.GetAttributesForPocCanonicalization();

            if (audit.SafeDependencies?.Invoke(owner) != true || !recipe.IsCurrent(postBuildBudget, afterBuild: true) ||
                !owner.HasCurrentPocNativeAttributeCache || owner.CachedWriteAttributes is not { } materialized ||
                !owner.TryMarkPocPostMapAttributes(new object(), previousOrigin)) { return false; }
            cache = materialized;
            return true;
        }

        private PreparedPocAttributeEnvelope? CaptureCanonicalAttributeEnvelope(TypeProvider owner,
            IReadOnlyList<MethodBodyStatement> cache, ref bool canonical, bool reservationHeld = false)
        {
            // Rebuilding providers reserve this before their native operation;
            // cached default-policy providers have not changed lifecycle yet.
            if (!reservationHeld && !_budget.TrySpend(AttributeEnvelopeReservation)) { canonical = false; return null; }
            var envelope = PreparedPocAttributeEnvelope.TryCapture(cache, new PocWorkBudget(AttributeEnvelopeLimit), owner);
            if (envelope?.IsCurrent(new PocWorkBudget(AttributeEnvelopeLimit)) != true) { canonical = false; return null; }
            return envelope;
        }

        private static InputNamespace? VerifiedCachedInput(InputNamespace? loaded)
        {
            if (loaded is null) { return null; }
            try
            {
                var generator = CodeModelGenerator.Instance;
                var library = generator.CachedInputLibrary;
                var getter = generator.GetType().GetProperty(nameof(CodeModelGenerator.InputLibrary), BindingFlags.Instance | BindingFlags.Public);
                // Match the exact namespace the native base getters will read.
                // An overriding/shadowing getter or library is never invoked.
                return getter?.GetMethod?.DeclaringType == typeof(CodeModelGenerator) &&
                    getter.GetIndexParameters().Length == 0 && library?.GetType() == typeof(InputLibrary) &&
                    ReferenceEquals(library.CachedInputNamespace, loaded) ? loaded : null;
            }
            catch (AmbiguousMatchException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static Type? Builder(TypeProvider owner, string name)
        {
            try
            {
                return owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null)?.DeclaringType;
            }
            catch (AmbiguousMatchException) { return null; }
        }

        private static bool Audited(TypeProvider owner, InputNamespace? loadedInput, bool writeDocs)
        {
            var warningBuilder = Builder(owner, "BuildDisabledFileWarnings");
            var docsBuilder = Builder(owner, "BuildXmlDocs");
            var descriptionBuilder = Builder(owner, "BuildDescription");
            if (PocHeaderFinalizationBridge.Get(owner) is { } audit)
            {
                if (writeDocs && audit.RequiredTypeOwner is { } required &&
                    required(owner) is not TypeProvider { CachedType: not null }) { return false; }
                return warningBuilder == audit.WarningBuilder && (!writeDocs ||
                    docsBuilder == audit.XmlDocsBuilder && descriptionBuilder == audit.DescriptionBuilder) &&
                    audit.SafeInputs(owner, loadedInput);
            }
            if ((owner.GetType().Assembly != typeof(TypeProvider).Assembly &&
                !PocHeaderFinalizationBridge.IsRegisteredAssembly(owner.GetType().Assembly)) || warningBuilder != typeof(TypeProvider)) { return false; }
            if (!writeDocs) { return Builder(owner, "get_ShouldWriteTypeXmlDocs") == typeof(TypeProvider); }
            if (docsBuilder != typeof(TypeProvider)) { return false; }
            if (descriptionBuilder == typeof(TypeProvider)) { return true; }
            return owner.GetType() == typeof(FixedEnumProvider) && descriptionBuilder == typeof(EnumProvider) &&
                owner.HeaderInputType is InputEnumType input &&
                PocHeaderFinalizationBridge.SafeDescriptionText(input.Summary) && PocHeaderFinalizationBridge.SafeDescriptionText(input.Doc);
        }

        internal sealed class Header
        {
            private readonly TypeProvider _owner;
            private readonly long _revision;
            private readonly CSharpType? _type;
            private readonly long? _typeRevision;
            private readonly TypeSignatureModifiers? _modifiers;
            private readonly bool _preserveDocs;
            private readonly object? _where;
            private readonly XmlDocProvider? _docs;
            private readonly IReadOnlyList<SuppressionStatement> _warnings;
            private readonly PreparedGeneratedDocumentationEnvelope? _documentation;
            private readonly PreparedSuppressionEnvelope? _suppression;
            private bool _stale;
            internal bool WritesDocs { get; }

            private readonly object? _attributeOrigin;
            private readonly object? _postMapOperation;
            internal PreparedPocAttributeEnvelope? Attributes { get; }
            internal bool CanUseCanonicalAttributes => UsesCanonicalAttributes && _postMapOperation is not null &&
                ReferenceEquals(_postMapOperation, _owner.PocPostMapAttributeOperation) && _owner.IsPocNativeAttributeOriginCurrent(_attributeOrigin);
            internal bool UsesCanonicalAttributes { get; }
            internal Header(TypeProvider owner, bool writesDocs, XmlDocProvider? docs, PreparedGeneratedDocumentationEnvelope? documentation,
                IReadOnlyList<SuppressionStatement> warnings, PreparedSuppressionEnvelope suppression,
                PreparedPocAttributeEnvelope? attributes = null, bool canonicalAttributes = false)
            {
                _owner = owner; _revision = owner.DeclarationRevision; WritesDocs = writesDocs;
                Attributes = attributes; UsesCanonicalAttributes = canonicalAttributes;
                _attributeOrigin = canonicalAttributes ? owner.PocNativeAttributeCacheOrigin : null;
                _postMapOperation = canonicalAttributes ? owner.PocPostMapAttributeOperation : null;
                _type = owner.CachedType; _typeRevision = _type?.IdentityVersion;
                _modifiers = owner.CachedDeclarationModifiers; _preserveDocs = owner.PreserveTypeXmlDocs;
                _where = owner.CachedWhereClause;
                _docs = docs; _warnings = warnings;
                _documentation = documentation;
                _suppression = suppression;
            }

            internal bool IsCurrent
            {
                get
                {
                    if (_stale) { return false; }
                    if (_owner.DeclarationRevision != _revision || _type is null || !ReferenceEquals(_owner.CachedType, _type) ||
                        _type.IdentityVersion != _typeRevision || _owner.CachedDeclarationModifiers != _modifiers ||
                        _owner.PreserveTypeXmlDocs != _preserveDocs || !ReferenceEquals(_owner.CachedWhereClause, _where) ||
                        !ReferenceEquals(_owner.CachedDisabledFileWarnings, _warnings) ||
                        _suppression?.IsCurrent != true || UsesCanonicalAttributes && !CanUseCanonicalAttributes || Attributes is not null &&
                        (!ReferenceEquals(_owner.CachedWriteAttributes, Attributes.Carrier) || !Attributes.IsCurrent(new PocWorkBudget(4096))) || WritesDocs &&
                        (!ReferenceEquals(_owner.CachedXmlDocs, _docs) || _documentation?.IsCurrent != true))
                    { _stale = true; return false; }
                    return true;
                }
            }
        }
    }
}

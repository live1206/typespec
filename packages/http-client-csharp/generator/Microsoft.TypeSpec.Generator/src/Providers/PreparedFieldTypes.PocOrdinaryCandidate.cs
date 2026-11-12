// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        /// <summary>
        /// A first POC-ranked bare-name candidate from the existing finalized
        /// provider inventory. It is not a file-confinement proof, a complete
        /// source/import proof, a writer permission, or a commit decision.
        /// Generic/alias and fieldless cases remain for the broader POC port.
        /// </summary>
        internal sealed class PreparedPocOrdinaryCandidate
        {
            private readonly PreparedFieldTypes _session;
            internal PreparedSourceFile File { get; }
            internal PreparedSourceOccurrence Occurrence { get; }
            internal PreparedOrdinaryTypeSourceObservation SourceObservation { get; }
            internal CSharpType Type { get; }
            internal long TypeRevision { get; }
            internal INamedTypeSymbol MetadataTarget { get; }
            internal PreparedPocNameForms.Form Form { get; }
            internal string SimpleSpelling { get; }
            internal string QualifiedSpelling { get; }
            internal ImmutableHashSet<string> ForbiddenImports { get; }
            internal bool IsCurrent => _session.IsPocCandidateCurrent(this);

            internal PreparedPocOrdinaryCandidate(PreparedFieldTypes session, PreparedSourceFile file, PreparedSourceOccurrence occurrence,
                PreparedOrdinaryTypeSourceObservation source, CSharpType type, INamedTypeSymbol target,
                PreparedPocNameForms.Form form, string simpleSpelling, string qualifiedSpelling, ImmutableHashSet<string> forbiddenImports)
            {
                _session = session;
                File = file; Occurrence = occurrence; SourceObservation = source; Type = type; TypeRevision = type.IdentityVersion;
                MetadataTarget = target; Form = form; SimpleSpelling = simpleSpelling; QualifiedSpelling = qualifiedSpelling; ForbiddenImports = forbiddenImports;
            }
        }

        // Diagnostic count only; it is neither a writer permission nor a
        // currentness check for any stored proposal.
        internal int PocCandidateCount => _pocCandidateSourceCaptureIncomplete ? 0 :
            _pocCandidates.Values.Count(candidate => !_pocInvalidCandidateFiles.Contains(candidate.File));
        internal string[] PocCandidateFilePaths => _pocCandidateSourceCaptureIncomplete ? [] :
            _pocCandidates.Values.Where(candidate => !_pocInvalidCandidateFiles.Contains(candidate.File))
                .Select(candidate => candidate.File.FilePath).Distinct(StringComparer.Ordinal).ToArray();

        private readonly Dictionary<SourceOccurrenceId, PreparedPocOrdinaryCandidate> _pocCandidates = new();
        private readonly HashSet<PreparedPocOrdinaryCandidate> _pocOwnedCandidates = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PreparedSourceFile, SourceConsumptionReceipt> _pocWrittenCandidateFiles = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<TypeProvider, PocHeaderObservation> _pocHeaders = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PreparedSourceFile, PreparedPocSupplementalFile> _pocSupplementalFiles = new(ReferenceEqualityComparer.Instance);
        private PocHeaderObservation? _pocActiveHeader;
        private readonly HashSet<PreparedSourceFile> _pocInvalidCandidateFiles = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<TypeProvider, PreparedSourceFile.OwnerSnapshot> _pocRelatedOwnerScopes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<TypeProvider, PocRelatedScopeSnapshot> _pocScopeNames = new(ReferenceEqualityComparer.Instance);
        private const int PocRelatedScopeWorkLimit = 262144;
        private readonly PocWorkBudget _pocRelatedScopeCaptureBudget = new(PocRelatedScopeWorkLimit);
        private readonly HashSet<TypeProvider> _pocUnsupportedOwners = new(ReferenceEqualityComparer.Instance);
        private readonly List<PocProviderNameSnapshot> _pocWriteDecisionNames = [];
        private const int PocNameInventoryWorkLimit = 262144;
        private readonly PocWorkBudget _pocNameInventoryCaptureBudget = new(PocNameInventoryWorkLimit);
        private readonly List<PocTypeNameSnapshot> _pocWriteDecisionParents = [];
        private readonly List<PocModelFactorySnapshot> _pocModelFactories = [];
        private const int PocModelFactoryWorkLimit = 262144;
        private readonly PocWorkBudget _pocModelFactoryCaptureBudget = new(PocModelFactoryWorkLimit);
        private bool _pocWriteDecisionInventoryInvalid;
        private ImmutableArray<PocProviderNameSnapshot> _pocProviderNames;
        private ImmutableArray<PocRootSerializationSnapshot> _pocRootSerializations;
        private PreparedFieldImportLookup? _pocLookup;
        private ProviderReferenceMapSession? _pocReferenceMap;
        private OutputLibrary? _pocOutput;
        private IReadOnlyList<TypeProvider>? _pocRoots;
        private string? _pocLicenseHeader;
        private ImmutableHashSet<string>? _pocRemovalCandidates;
        private bool _pocHasDirectSourceGetter;
        private int _pocCandidateStale;
        // This subledger bounds additional candidate/source, input and scope
        // checks. It does not establish a bound for cold compiler lookup work.
        private PocWorkBudget? _pocCandidateSourceBudget;
        private volatile bool _pocCandidateSourceCaptureIncomplete;
        private ImmutableArray<TypeProvider> _pocExpectedProviders;
        private int _pocStreamState;
        private int _pocYieldCount;
        private int _pocWriteCount;
        private TypeProvider? _pocWritingProvider;
        private TypeProviderWriter? _pocWritingWriter;

        // Namespace lookup depends on provider type identities, not arbitrary
        // members of unrelated providers. Related declaration scopes retain
        // their separate, stronger owner snapshots below.
        private sealed record PocProviderNameSnapshot(TypeProvider Provider, CSharpType Type, long Version,
            string Name, string Namespace, int Arity, TypeProvider? DeclaringProvider, CSharpType? DeclaringType)
        {
            internal bool IsCurrent() => ReferenceEquals(Provider.CachedType, Type) && Type.IdentityVersion == Version &&
                ReferenceEquals(Provider.CachedDeclaringTypeProvider, DeclaringProvider) &&
                Type.Name == Name && Type.Namespace == Namespace && ReferenceEquals(Type.DeclaringType, DeclaringType) &&
                ContractCollections.TryGetCount(Type.Arguments, out var arity) && arity == Arity;

            internal bool IsCurrentForPoc(PocWorkBudget budget) => budget.TrySpend() &&
                budget.TrySpendStringComparison(Type.Name, Name) && budget.TrySpendStringComparison(Type.Namespace, Namespace) && IsCurrent();
        }

        private sealed class PocHeaderObservation(PreparedSourceFile file)
        {
            internal PreparedSourceFile File { get; } = file;
            internal IReadOnlyList<SuppressionStatement>? Warnings;
            internal PreparedSuppressionEnvelope? WarningEnvelope;
            internal bool WarningsSeen;
            internal XmlDocProvider? Docs;
            internal PreparedGeneratedDocumentationEnvelope? DocumentationEnvelope;
            internal bool DocsSeen;
            internal bool DocsEmitted;
            internal bool Invalid;
        }

        private sealed class PocHeaderWriterScope(PreparedFieldTypes session, PocHeaderObservation header) : IDisposable
        {
            private int _disposed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
                if (!ReferenceEquals(Interlocked.CompareExchange(ref session._pocActiveHeader, null, header), header))
                { session.InvalidatePocHeader(header); }
            }
        }

        internal IDisposable EnterPocHeaderWriter(TypeProvider owner)
        {
            if (_pocCandidateSourceCaptureIncomplete || !_pocHeaders.TryGetValue(owner, out var header) || header.Invalid) { return PreparedSourceFile.EmptyScope.Instance; }
            var active = Interlocked.CompareExchange(ref _pocActiveHeader, header, null);
            if (active is not null)
            {
                InvalidatePocHeader(active);
                InvalidatePocHeader(header);
                return PreparedSourceFile.EmptyScope.Instance;
            }
            return new PocHeaderWriterScope(this, header);
        }

        private void ObservePocActiveHeaderAfterSourceCallback()
        {
            var file = Volatile.Read(ref _pocActiveHeader)?.File;
            ObservePocHeaderAfterSourceCallback(file);
            if (file is not null && _pocSupplementalFiles.TryGetValue(file, out var supplemental)) { supplemental.ObserveCurrent(); }
        }

        private sealed record PocTypeNameSnapshot(CSharpType Type, long Version, string Name, string Namespace, int Arity, CSharpType? DeclaringType)
        {
            internal bool IsCurrent() => Type.IdentityVersion == Version && Type.Name == Name && Type.Namespace == Namespace &&
                ReferenceEquals(Type.DeclaringType, DeclaringType) && ContractCollections.TryGetCount(Type.Arguments, out var arity) && arity == Arity;

            internal bool IsCurrentForPoc(PocWorkBudget budget) => budget.TrySpend() &&
                budget.TrySpendStringComparison(Type.Name, Name) && budget.TrySpendStringComparison(Type.Namespace, Namespace) && IsCurrent();
        }

        private sealed record PocRootSerializationSnapshot(TypeProvider Root, ImmutableArray<TypeProvider> Serializations)
        {
            internal bool IsCurrent() => ContractCollections.SameReferences(Root.CachedSerializationProviders, Serializations);
            internal bool IsCurrentForPoc(PocWorkBudget budget) =>
                ContractCollections.ReserveSharedReferencesForPoc(Root.CachedSerializationProviders, Serializations.Length, budget) && IsCurrent();
        }

        private sealed record PocModelFactorySnapshot(TypeProvider Root, ImmutableArray<MethodProvider> Methods)
        {
            internal bool IsCurrent(PocWorkBudget? budget = null)
            {
                budget ??= new PocWorkBudget(PocModelFactoryWorkLimit);
                // Preserve the shared collection profile. Reserve comparison
                // work separately from the live carrier copy's own debit.
                return budget.TrySpend() && budget.TrySpend(Methods.Length) &&
                    ContractCollections.TryGetCount(Root.CachedMethods, out _) &&
                    ContractCollections.SameReferencesForPoc(Root.CachedMethods, Methods, budget);
            }
        }

        private bool ArePocModelFactoriesCurrent(PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(PocModelFactoryWorkLimit);
            foreach (var factory in _pocModelFactories) { if (!factory.IsCurrent(budget)) { return false; } }
            return true;
        }

        // Capture every root and examined serialization at its actual write-policy
        // decision, including filtered providers omitted from the lookup index.
        // Read only already-cached, audited data; do not replay the policy.
        private void CapturePocWriteDecisionProvider(TypeProvider provider, PocWorkBudget? budget = null)
        {
            if (_pocWriteDecisionInventoryInvalid) { return; }
            budget ??= _pocNameInventoryCaptureBudget;
            if (!budget.TrySpend()) { _pocWriteDecisionInventoryInvalid = true; return; }
            var type = provider.CachedType;
            var declaring = provider.CachedDeclaringTypeProvider;
            if (type is null || type.GetType() != typeof(CSharpType) ||
                declaring is null && !HasKnownStableNullDeclaringGetter(provider) ||
                !ContractCollections.TryGetCount(type.Arguments, out var arity))
            { _pocWriteDecisionInventoryInvalid = true; return; }
            // Pay receipt allocation and possible list growth before Add. This
            // is arity-only: no argument-element traversal is required.
            if (!budget.TrySpendReferenceLookup(_pocWriteDecisionNames.Count)) { _pocWriteDecisionInventoryInvalid = true; return; }
            _pocWriteDecisionNames.Add(new(provider, type, type.IdentityVersion, type.Name, type.Namespace,
                arity, declaring, type.DeclaringType));
            if (!budget.TrySpend() || !budget.TrySpendReferenceLookup(0)) { _pocWriteDecisionInventoryInvalid = true; return; }
            var seen = new HashSet<CSharpType>(ReferenceEqualityComparer.Instance) { type };
            for (var parent = type.DeclaringType; parent is not null; parent = parent.DeclaringType)
            {
                if (!budget.TrySpend() || seen.Count >= 32 || !budget.TrySpendReferenceLookup(seen.Count) || !seen.Add(parent) ||
                    parent.GetType() != typeof(CSharpType) || !ContractCollections.TryGetCount(parent.Arguments, out var parentArity))
                { _pocWriteDecisionInventoryInvalid = true; return; }
                if (!budget.TrySpendReferenceLookup(_pocWriteDecisionParents.Count)) { _pocWriteDecisionInventoryInvalid = true; return; }
                _pocWriteDecisionParents.Add(new(parent, parent.IdentityVersion, parent.Name, parent.Namespace,
                    parentArity, parent.DeclaringType));
            }
        }

        private void CapturePocModelFactoryMethods(TypeProvider provider, PocWorkBudget? budget = null)
        {
            if (_pocWriteDecisionInventoryInvalid) { return; }
            budget ??= _pocModelFactoryCaptureBudget;
            // Pay the new receipt/list growth before allocating, and keep the
            // original shared opacity before using the funded copy routine.
            if (!budget.TrySpend() || !budget.TrySpend(_pocModelFactories.Count) ||
                !ContractCollections.TryGetCount(provider.CachedMethods, out _) ||
                !ContractCollections.TryCopyForPoc(provider.CachedMethods, budget, out var methods))
            { _pocWriteDecisionInventoryInvalid = true; return; }
            _pocModelFactories.Add(new(provider, methods));
        }

        private sealed class PocRelatedScopeSnapshot
        {
            private readonly PreparedSourceFile.OwnerSnapshot _owner;
            private readonly ImmutableArray<(FieldProvider Field, string Name)> _fields;
            private readonly ImmutableArray<(PropertyProvider Property, string Name)> _properties;
            private readonly ImmutableArray<(MethodProvider Method, MethodSignature Signature, long Revision, string Name)> _methods;
            private readonly ImmutableArray<(TypeProvider Provider, CSharpType Type, long Revision, string Name)> _nested;
            internal ImmutableHashSet<string> Names { get; }

            private PocRelatedScopeSnapshot(PreparedSourceFile.OwnerSnapshot owner,
                ImmutableArray<(FieldProvider Field, string Name)> fields, ImmutableArray<(PropertyProvider Property, string Name)> properties,
                ImmutableArray<(MethodProvider Method, MethodSignature Signature, long Revision, string Name)> methods,
                ImmutableArray<(TypeProvider Provider, CSharpType Type, long Revision, string Name)> nested)
            {
                _owner = owner; _fields = fields; _properties = properties; _methods = methods; _nested = nested;
                Names = fields.Select(field => field.Name).Concat(properties.Select(property => property.Name))
                    .Concat(methods.Select(method => method.Name)).Concat(nested.Select(type => type.Name)).ToImmutableHashSet(StringComparer.Ordinal);
            }

            internal static PocRelatedScopeSnapshot? TryCapture(TypeProvider provider, PreparedSourceFile.OwnerSnapshot owner, PocWorkBudget? budget = null)
            {
                budget ??= new PocWorkBudget(PocRelatedScopeWorkLimit);
                if (!budget.TrySpend() || !owner.IsCurrentForPocSharedAvailability(budget) ||
                    !ContractCollections.TryCopySharedForPoc(provider.CachedFields, budget, out var fields) ||
                    !ContractCollections.TryCopySharedForPoc(provider.CachedProperties, budget, out var properties) ||
                    !ContractCollections.TryCopySharedForPoc(provider.CachedMethods, budget, out var methods) ||
                    !ContractCollections.TryCopySharedForPoc(provider.CachedNestedTypes, budget, out var nested)) { return null; }
                // Bound validation, tuple projection/copying and the future
                // ordinal name-set hash/probe work before constructing either.
                long count = (long)fields.Length + properties.Length + methods.Length + nested.Length;
                if (count > int.MaxValue / 4 || !budget.TrySpend((int)(4 * count)) ||
                    fields.Any(field => field is null) || properties.Any(property => property is null) ||
                    methods.Any(method => method?.Signature is null) || nested.Any(type => type?.CachedType is null)) { return null; }
                var examined = 0;
                foreach (var field in fields) { if (!ReserveName(field.Name, ref examined, budget)) { return null; } }
                foreach (var property in properties) { if (!ReserveName(property.Name, ref examined, budget)) { return null; } }
                foreach (var method in methods) { if (!ReserveName(method.Signature.Name, ref examined, budget)) { return null; } }
                foreach (var type in nested) { if (!ReserveName(type.CachedType!.Name, ref examined, budget)) { return null; } }
                return new(owner, fields.Select(field => (field, field.Name)).ToImmutableArray(),
                    properties.Select(property => (property, property.Name)).ToImmutableArray(),
                    methods.Select(method => (method, method.Signature, method.Signature.DeclarationRevision, method.Signature.Name)).ToImmutableArray(),
                    nested.Select(type => (type, type.CachedType!, type.CachedType!.IdentityVersion, type.CachedType!.Name)).ToImmutableArray());
            }

            private static bool ReserveName(string? name, ref int examined, PocWorkBudget budget)
            {
                long work = (1L + (name?.Length ?? 0)) * (examined + 1L);
                if (work > int.MaxValue || !budget.TrySpend((int)work)) { return false; }
                examined++;
                return true;
            }

            internal bool IsCurrent(PocWorkBudget? budget = null)
            {
                budget ??= new PocWorkBudget(PocRelatedScopeWorkLimit);
                if (!budget.TrySpend() || !_owner.IsCurrentForPocSharedAvailability(budget)) { return false; }
                foreach (var field in _fields)
                {
                    if (!budget.TrySpendStringComparison(field.Field.Name, field.Name) || field.Field.Name != field.Name) { return false; }
                }
                foreach (var property in _properties)
                {
                    if (!budget.TrySpendStringComparison(property.Property.Name, property.Name) || property.Property.Name != property.Name) { return false; }
                }
                foreach (var method in _methods)
                {
                    if (!budget.TrySpend() || !ReferenceEquals(method.Method.Signature, method.Signature) ||
                        method.Signature.DeclarationRevision != method.Revision ||
                        !budget.TrySpendStringComparison(method.Signature.Name, method.Name) || method.Signature.Name != method.Name) { return false; }
                }
                foreach (var type in _nested)
                {
                    if (!budget.TrySpend() || !ReferenceEquals(type.Provider.CachedType, type.Type) || type.Type.IdentityVersion != type.Revision ||
                        !budget.TrySpendStringComparison(type.Type.Name, type.Name) || type.Type.Name != type.Name) { return false; }
                }
                return true;
            }
        }

        private bool CapturePocProviderNames(PocWorkBudget? budget = null)
        {
            budget ??= _pocNameInventoryCaptureBudget;
            if (!budget.TrySpend()) { return RejectPocProviderNames(); }
            if (_providerStates.Length == 0 || _providerStates.Length != _callLookupScopes.Length || _pocWriteDecisionInventoryInvalid ||
                _pocWriteDecisionNames.Count == 0 || _pocWriteDecisionNames.Any(name => !name.IsCurrentForPoc(budget)) ||
                _pocWriteDecisionParents.Any(parent => !parent.IsCurrentForPoc(budget)) || !ArePocModelFactoriesCurrent(budget)) { return false; }
            if (!budget.TrySpend(_rootStates.Length) || !budget.TrySpend(_rootStates.Length) || !budget.TrySpend()) { return RejectPocProviderNames(); }
            var serializations = ImmutableArray.CreateBuilder<PocRootSerializationSnapshot>();
            foreach (var root in _rootStates)
            {
                if (!root.Written) { continue; }
                if (!budget.TrySpend(root.Serializations.Length) ||
                    !ContractCollections.ReserveSharedReferencesForPoc(root.Provider.CachedSerializationProviders, root.Serializations.Length, budget))
                { return RejectPocProviderNames(); }
                var expected = ImmutableArray.CreateRange(root.Serializations);
                if (!ContractCollections.SameReferences(root.Provider.CachedSerializationProviders, expected)) { return false; }
                serializations.Add(new(root.Provider, expected));
            }
            _pocRootSerializations = serializations.ToImmutable();
            if (!budget.TrySpend()) { return RejectPocProviderNames(); }
            var knownTypes = new HashSet<CSharpType>(ReferenceEqualityComparer.Instance);
            foreach (var state in _providerStates) { if (!AddPocKnownType(knownTypes, state.Type, budget)) { return RejectPocProviderNames(); } }
            foreach (var name in _pocWriteDecisionNames) { if (!AddPocKnownType(knownTypes, name.Type, budget)) { return RejectPocProviderNames(); } }
            foreach (var parent in _pocWriteDecisionParents) { if (!AddPocKnownType(knownTypes, parent.Type, budget)) { return RejectPocProviderNames(); } }
            foreach (var name in _pocWriteDecisionNames)
            {
                if (!budget.TrySpend()) { return RejectPocProviderNames(); }
                if (name.DeclaringType is { } declaring &&
                    (!budget.TrySpendReferenceLookup(knownTypes.Count) || !knownTypes.Contains(declaring))) { return false; }
            }
            if (!budget.TrySpend(_providerStates.Length) || !budget.TrySpend(_providerStates.Length)) { return RejectPocProviderNames(); }
            var names = ImmutableArray.CreateBuilder<PocProviderNameSnapshot>(_providerStates.Length);
            for (var index = 0; index < _providerStates.Length; index++)
            {
                var state = _providerStates[index];
                var type = state.Type;
                if (!budget.TrySpend() || !budget.TrySpendStringComparison(type.Name, state.Name) ||
                    !budget.TrySpendStringComparison(type.Namespace, state.Namespace)) { return RejectPocProviderNames(); }
                if (type.GetType() != typeof(CSharpType) || !ReferenceEquals(state.Provider.CachedType, type) ||
                    !ReferenceEquals(state.Provider.CachedDeclaringTypeProvider, state.Declaring) ||
                    state.Declaring is null && !HasKnownStableNullDeclaringGetter(state.Provider) ||
                    type.Name != state.Name || type.Namespace != state.Namespace || !ReferenceEquals(type.DeclaringType, state.DeclaringType) ||
                    type.DeclaringType is { } declaring && (!budget.TrySpendReferenceLookup(knownTypes.Count) || !knownTypes.Contains(declaring)) ||
                    !ContractCollections.TryGetCount(type.Arguments, out var arity) || arity != state.Arity) { return false; }
                if (!budget.TrySpendReferenceLookup(_pocRelatedOwnerScopes.Count)) { return RejectPocProviderNames(); }
                if (!_pocRelatedOwnerScopes.TryAdd(state.Provider, _callLookupScopes[index])) { return false; }
                names.Add(new(state.Provider, type, type.IdentityVersion, state.Name, state.Namespace, state.Arity, state.Declaring, state.DeclaringType));
            }
            _pocProviderNames = names.ToImmutable();
            foreach (var (provider, owner) in _owners)
            {
                if (!budget.TrySpend()) { return RejectPocProviderNames(); }
                var scopeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var name in owner.SourceScopeNames)
                {
                    if (!AddPocScopeName(scopeNames, name, budget)) { return RejectPocProviderNames(); }
                }
                foreach (var part in owner.Parts)
                {
                    if (!budget.TrySpendReferenceLookup(_pocScopeNames.Count)) { return RejectPocProviderNames(); }
                    if (!_pocScopeNames.TryGetValue(part, out var scope))
                    {
                        if (!budget.TrySpendReferenceLookup(_pocRelatedOwnerScopes.Count)) { return RejectPocProviderNames(); }
                        if (!_pocRelatedOwnerScopes.TryGetValue(part, out var ownerSnapshot) ||
                            PocRelatedScopeSnapshot.TryCapture(part, ownerSnapshot, _pocRelatedScopeCaptureBudget) is not { } captured)
                        {
                            if (!MarkPocUnsupportedOwner(provider, budget)) { return RejectPocProviderNames(); }
                            break;
                        }
                        scope = captured;
                        if (!budget.TrySpendReferenceLookup(_pocScopeNames.Count)) { return RejectPocProviderNames(); }
                        _pocScopeNames.Add(part, scope);
                    }
                    foreach (var name in scope.Names)
                    {
                        if (!AddPocScopeName(scopeNames, name, budget)) { return RejectPocProviderNames(); }
                    }
                }
                if (!budget.TrySpendReferenceLookup(_pocUnsupportedOwners.Count)) { return RejectPocProviderNames(); }
                if (_pocUnsupportedOwners.Contains(provider)) { continue; }
                var equal = owner.PlannedScopeNames.Count == scopeNames.Count;
                if (equal)
                {
                    foreach (var name in scopeNames)
                    {
                        if (!ReservePocScopeNameLookup(name, owner.PlannedScopeNames.Count, budget)) { return RejectPocProviderNames(); }
                        if (!owner.PlannedScopeNames.Contains(name)) { equal = false; break; }
                    }
                }
                if (!equal && !MarkPocUnsupportedOwner(provider, budget)) { return RejectPocProviderNames(); }
            }
            return true;
        }

        private bool RejectPocProviderNames()
        {
            _pocWriteDecisionInventoryInvalid = true;
            return false;
        }

        private static bool AddPocKnownType(HashSet<CSharpType> types, CSharpType type, PocWorkBudget budget)
        {
            if (!budget.TrySpendReferenceLookup(types.Count)) { return false; }
            types.Add(type);
            return true;
        }

        private static bool ReservePocScopeNameLookup(string? name, int count, PocWorkBudget budget)
        {
            long work = (1L + (name?.Length ?? 0)) * (count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        private static bool AddPocScopeName(HashSet<string> names, string name, PocWorkBudget budget)
        {
            if (!ReservePocScopeNameLookup(name, names.Count, budget)) { return false; }
            names.Add(name);
            return true;
        }

        private bool MarkPocUnsupportedOwner(TypeProvider provider, PocWorkBudget budget)
        {
            if (!budget.TrySpendReferenceLookup(_pocUnsupportedOwners.Count)) { return false; }
            _pocUnsupportedOwners.Add(provider);
            return true;
        }

        private bool PocRelatedScopesAreCurrent(PreparedSourceFile file, PocWorkBudget? pointBudget = null)
        {
            var budget = pointBudget ?? new PocWorkBudget(PocRelatedScopeWorkLimit);
            if (!budget.TrySpendReferenceLookup(_owners.Count) || !_owners.TryGetValue(file.FileOwner, out var owner)) { return false; }
            // Reserve every possible future rejection lookup/insertion BEFORE
            // observing a mismatch. A short reservation observes nothing and
            // need not mutate either POC or shared currentness.
            long rejectionWork = (long)owner.Parts.Length *
                (_sourceFiles.Count + (long)_pocInvalidCandidateFiles.Count + owner.Parts.Length + 2);
            if (rejectionWork > int.MaxValue || !budget.TrySpend((int)rejectionWork) || !budget.TrySpend(owner.Parts.Length) ||
                !budget.TrySpendReferenceLookup(_pocUnsupportedOwners.Count)) { return false; }
            if (!_pocUnsupportedOwners.Contains(file.FileOwner) && owner.Parts.All(part =>
                budget.TrySpendReferenceLookup(_pocScopeNames.Count) && _pocScopeNames.TryGetValue(part, out var snapshot) && snapshot.IsCurrent(budget))) { return true; }
            // Exhaustion inside a scope snapshot has not observed a mismatch.
            if (budget.Exhausted) { return false; }
            // The whole declaration-group mark was prepaid above. An observed
            // mismatch stays sticky without vetoing unrelated files or fields.
            foreach (var part in owner.Parts)
            {
                if (_sourceFiles.TryGetValue(part, out var relatedFile)) { _pocInvalidCandidateFiles.Add(relatedFile); }
            }
            return false;
        }

        private bool PocObservedInputsAreCurrentForCapture(PocWorkBudget? phase)
        {
            if (phase is null) { return PocObservedInputsAreCurrent(); }
            if (!phase.TrySpend()) { return false; }
            return PocObservedInputsAreCurrent(new PocWorkBudget(PocNameInventoryWorkLimit, phase));
        }

        private bool PocRelatedScopesAreCurrentForCapture(PreparedSourceFile file, PocWorkBudget? phase)
        {
            if (phase is null) { return PocRelatedScopesAreCurrent(file); }
            if (!phase.TrySpend()) { return false; }
            return PocRelatedScopesAreCurrent(file, new PocWorkBudget(PocRelatedScopeWorkLimit, phase));
        }

        private static bool HasKnownStableNullDeclaringGetter(TypeProvider provider)
        {
            // Null results are not memoized. The base getter always returns null;
            // these two inherited implementations return readonly constructor
            // fields. Unknown overrides are never invoked a second time to certify
            // absence of an enclosing provider.
            var methods = provider.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(method => method.Name == "BuildDeclaringTypeProvider" && !method.IsGenericMethod && method.GetParameters().Length == 0).ToArray();
            return methods.Length == 1 && (methods[0].DeclaringType == typeof(TypeProvider) ||
                methods[0].DeclaringType == typeof(FixedEnumProvider) || methods[0].DeclaringType == typeof(ExtensibleEnumProvider));
        }

        private static bool HasKnownDirectSourceInputGetter(CodeModelGenerator generator)
        {
            var accessors = generator.GetType().GetProperties().Where(property => property.Name == nameof(CodeModelGenerator.SourceInputModel) &&
                property.GetIndexParameters().Length == 0).ToArray();
            return accessors.Length == 1 && accessors[0].PropertyType == typeof(SourceInputModel) &&
                accessors[0].GetMethod?.DeclaringType == typeof(CodeModelGenerator);
        }

        // The on-demand diagnostic checks the source proof. Production planning
        // must instead share the existing final preparation check below.
        internal PreparedPocOrdinaryCandidate? TryPreparePocOrdinaryCandidate(PreparedSourceFile file, PreparedSourceOccurrence occurrence) =>
            TryCapturePocOrdinaryCandidate(file, occurrence, sourceAlreadyChecked: false);

        private PreparedPocOrdinaryCandidate? TryCapturePocOrdinaryCandidate(PreparedSourceFile file, PreparedSourceOccurrence occurrence,
            bool sourceAlreadyChecked, PocWorkBudget? sourcePhase = null, bool pathKnownNotDirect = false)
        {
            if (_sourceDisposed) { return null; }
            // Pay the additional candidate's identity probes and full pre-write
            // file-start scan before inspecting any of those inventories.
            if (sourcePhase is not null &&
                (!sourcePhase.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count) ||
                 !sourcePhase.TrySpendReferenceLookup(_sourceFiles.Count) || !sourcePhase.TrySpendReferenceLookup(_sourceFiles.Count) ||
                 !sourcePhase.TrySpendReferenceLookup(_owners.Count))) { return null; }
            if (_pocInvalidCandidateFiles.Contains(file) || _sourceInputs is null || _providerStates.Length == 0 || _pocLookup is null ||
                _sourceFiles.Values.Any(source => source.HasStarted) ||
                !_sourceFiles.TryGetValue(file.FileOwner, out var owned) || !ReferenceEquals(owned, file) ||
                !_owners.TryGetValue(file.FileOwner, out var owner) ||
                occurrence.Producer is not DeclarationExpression declaration) { return null; }
            if (sourcePhase is not null && !sourcePhase.TrySpend()) { return null; }
            var captureBudget = sourcePhase is null ? null : new PocWorkBudget(65536, sourcePhase);
            if (PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence, captureBudget, pathKnownNotDirect) is not { } source ||
                !(sourceAlreadyChecked ? PocObservedInputsAreCurrentForCapture(sourcePhase) : PocInputsAreCurrent()) ||
                !PocRelatedScopesAreCurrentForCapture(file, sourcePhase)) { return null; }

            var methodArguments = source.Method.Signature.GenericArguments;
            if (methodArguments is not null &&
                (!ContractCollections.TryGetCount(methodArguments, out var arity) || arity != 0)) { return null; }
            var type = declaration.Variable.Type;
            if (type.GetType() != typeof(CSharpType) || !type.IsFrameworkType || type.FrameworkType.GetType() != typeof(object).GetType()) { return null; }
            if (sourcePhase is not null && !SourceCurrentnessChecks.OrdinalLookup(type.Name, owner.PlannedScopeNames.Count, sourcePhase)) { return null; }
            if (owner.PlannedScopeNames.Contains(type.Name) || type.FrameworkType.FullName is not { } metadataName ||
                _sourceInputs.Compilation.GetTypeByMetadataName(metadataName) is not { } target ||
                !_pocLookup.TryPreparePocBareNamespaceCandidate(owner.Type.Namespace, type, target, out var candidate, out var forbidden)) { return null; }
            if (sourcePhase is not null && !sourcePhase.TrySpend()) { return null; }
            var currentBudget = sourcePhase is null ? null : new PocWorkBudget(65536, sourcePhase);
            if (!(currentBudget is null ? source.IsCurrent : source.IsCurrentForPoc(currentBudget)) || !ReferenceEquals(declaration.Variable.Type, type) ||
                !PocObservedInputsAreCurrentForCapture(sourcePhase) || !PocRelatedScopesAreCurrentForCapture(file, sourcePhase)) { return null; }
            long textWork = 10L + type.Namespace.Length + type.Name.Length;
            if (sourcePhase is not null && (textWork > int.MaxValue || !sourcePhase.TrySpend((int)textWork) ||
                !sourcePhase.TrySpendReferenceLookup(_pocOwnedCandidates.Count))) { return null; }
            var result = new PreparedPocOrdinaryCandidate(this, file, occurrence, source, type, target, candidate.Form, candidate.Spelling,
                // The fallback is the native formatter's exact raw token spelling;
                // candidate escaping belongs only to the independently ranked form.
                "global::" + type.Namespace + "." + type.Name, forbidden);
            _pocOwnedCandidates.Add(result);
            return result;
        }

        private static IEnumerable<(PreparedSourceOccurrence Occurrence, bool CouldBeDirect)> EnumeratePocOccurrences(
            PreparedSourceOccurrence current, PocWorkBudget budget, int directStage)
        {
            // The caller pays for this iterator and its current node before
            // entering it. Retain the complete original preorder, including
            // rejected declarations, while tracking only immutable path shapes.
            yield return (current, directStage == 5);
            for (int index = 0; index < current.Children.Length; index++)
            {
                if (!budget.TrySpend(2)) { yield break; }
                var child = current.Children[index];
                int nextStage = directStage switch
                {
                    1 when child.Producer is MethodBodyStatements block && block.GetType() == typeof(MethodBodyStatements) => 2,
                    2 when child.Producer is ExpressionStatement statement && statement.GetType() == typeof(ExpressionStatement) => 3,
                    3 when child.Producer is AssignmentExpression => 4,
                    4 when child.Producer is DeclarationExpression => 5,
                    _ => 0
                };
                foreach (var descendant in EnumeratePocOccurrences(child, budget, nextStage)) { yield return descendant; }
                if (budget.Exhausted) { yield break; }
            }
        }

        private void CapturePocOrdinaryCandidates()
        {
            if (_preparedGenerator is not { } generator) { return; }
            var budget = _pocCandidateSourceBudget = new PocWorkBudget(1048576);
            var license = generator.LicenseHeader;
            if (!PreparedSourceFile.HasCommentOnlyLicenseHeader(license)) { return; }
            _pocLicenseHeader = license;
            if (!CapturePocProviderNames()) { return; }
            if (!PocObservedInputsAreCurrentForCapture(budget))
            {
                if (budget.Exhausted) { _pocCandidateSourceCaptureIncomplete = true; }
                return;
            }
            if (!budget.TrySpendReferenceLookup(_sourceFiles.Count)) { _pocCandidateSourceCaptureIncomplete = true; return; }
            foreach (var file in _sourceFiles.Values)
            {
                if (!budget.TrySpend(file.Roots.Length)) { _pocCandidateSourceCaptureIncomplete = true; return; }
                for (int index = 0; index < file.Roots.Length; index++)
                {
                    if (!budget.TrySpend(2)) { _pocCandidateSourceCaptureIncomplete = true; return; }
                    var root = file.Roots[index];
                    if (!budget.TrySpendStringComparison(root.Id.RootRole, "method")) { _pocCandidateSourceCaptureIncomplete = true; return; }
                    int directStage = root.Id.RootRole == "method" && root.Producer is MethodProvider ? 1 : 0;
                    foreach (var visit in EnumeratePocOccurrences(root, budget, directStage))
                    {
                        var occurrence = visit.Occurrence;
                        if (occurrence.Producer is not DeclarationExpression) { continue; }
                        var candidate = TryCapturePocOrdinaryCandidate(file, occurrence, sourceAlreadyChecked: true,
                            sourcePhase: budget, pathKnownNotDirect: !visit.CouldBeDirect);
                        if (budget.Exhausted) { _pocCandidateSourceCaptureIncomplete = true; return; }
                        if (candidate is null) { continue; }
                        if (!SourceCurrentnessChecks.OccurrenceLookup(occurrence.Id, _pocCandidates.Count, budget) ||
                            !budget.TrySpendReferenceLookup(_pocHeaders.Count) || !budget.TrySpendReferenceLookup(_pocHeaders.Count) ||
                            !budget.TrySpendReferenceLookup(_bodyImports.Count) || !budget.TrySpendReferenceLookup(_bodyImports.Count) ||
                            !budget.TrySpend(2)) { _pocCandidateSourceCaptureIncomplete = true; return; }
                        _pocCandidates.Add(occurrence.Id, candidate);
                        if (!_pocHeaders.ContainsKey(file.FileOwner)) { _pocHeaders.Add(file.FileOwner, new PocHeaderObservation(file)); }
                        if (!_bodyImports.ContainsKey(file.FileOwner))
                        { _bodyImports.Add(file.FileOwner, new HashSet<string>(StringComparer.Ordinal)); }
                    }
                    if (budget.Exhausted) { _pocCandidateSourceCaptureIncomplete = true; return; }
                }
            }
        }

        private void FinalizePocOrdinaryCandidates()
        {
            if (_pocCandidateSourceCaptureIncomplete || _pocCandidates.Count == 0) { return; }
            var budget = _pocCandidateSourceBudget!;
            if (!PocObservedInputsAreCurrentForCapture(budget)) { _pocCandidateSourceCaptureIncomplete = true; return; }
            if (!budget.TrySpendReferenceLookup(_pocCandidates.Count)) { _pocCandidateSourceCaptureIncomplete = true; return; }
            foreach (var candidate in _pocCandidates.Values)
            {
                if (!budget.TrySpendReferenceLookup(_pocOwnedCandidates.Count) || !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count) ||
                    !budget.TrySpend(2)) { _pocCandidateSourceCaptureIncomplete = true; return; }
                var sourceBudget = new PocWorkBudget(65536, budget);
                var scopeBudget = new PocWorkBudget(PocRelatedScopeWorkLimit, budget);
                if (!IsPocCandidateObservedCurrent(candidate, sourceBudget, scopeBudget))
                {
                    if (sourceBudget.Exhausted || scopeBudget.Exhausted || !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count))
                    { _pocCandidateSourceCaptureIncomplete = true; return; }
                    _pocInvalidCandidateFiles.Add(candidate.File);
                }
                else
                {
                    if (!budget.TrySpendReferenceLookup(_pocSupplementalFiles.Count)) { _pocCandidateSourceCaptureIncomplete = true; return; }
                    if (_pocSupplementalFiles.ContainsKey(candidate.File)) { continue; }
                    if (!TryReservePocSupplementalFileInsertion(budget) || !budget.TrySpend(7))
                    { _pocCandidateSourceCaptureIncomplete = true; return; }
                    // The linked budget, supplemental file, two empty maps and
                    // three empty lists are all reserved before construction.
                    var supplementalBudget = new PocWorkBudget(65536, budget);
                    PreparedPocSupplementalFile supplemental;
                    try { supplemental = PreparedPocSupplementalFile.Capture(candidate.File, supplementalBudget); }
                    finally { supplementalBudget.DetachParent(); }
                    if (budget.Exhausted) { _pocCandidateSourceCaptureIncomplete = true; return; }
                    _pocSupplementalFiles.Add(candidate.File, supplemental);
                }
            }
        }

        private bool TryReservePocSupplementalFileInsertion(PocWorkBudget budget)
        {
            var count = _pocSupplementalFiles.Count;
            if (!budget.TrySpendReferenceLookup(count) || !budget.TrySpend()) { return false; }
            if (count < _pocSupplementalFiles.EnsureCapacity(0)) { return true; }
            // Every file lookup is charged, bounding this map below 1,448
            // entries. The supported runtime's next prime is below 3(n+1)
            // in that range: reserve both arrays, copies and rethreading.
            long growth = 6L * (count + 1L) + 2L * count + 2;
            if (growth > int.MaxValue) { budget.Reject(); return false; }
            return budget.TrySpend((int)growth);
        }

        internal PreparedPocSupplementalFile? GetPocSupplementalFile(PreparedSourceFile file) =>
            !_pocCandidateSourceCaptureIncomplete && _pocSupplementalFiles.TryGetValue(file, out var supplemental) && supplemental.IsFor(file) ? supplemental : null;

        internal PreparedPocGenerationRawInventory? PocGenerationRawInventory { get; private set; }
        internal PreparedPocGenerationEnvelope? PocGenerationEnvelope { get; private set; }

        // Capture the provider stream from the already-recorded write decisions;
        // never call the write-policy delegate or enumerate output a second time.
        private void AttachPocProviderStream()
        {
            if (_pocCandidateSourceCaptureIncomplete || _pocCandidates.Count == 0 || _pocReferenceMap is null) { return; }
            var expected = ImmutableArray.CreateBuilder<TypeProvider>();
            foreach (var root in _rootStates)
            {
                if (!root.Written) { continue; }
                expected.Add(root.Provider);
                for (var index = 0; index < root.Serializations.Length; index++)
                {
                    if (root.SerializationWritten[index]) { expected.Add(root.Serializations[index]); }
                }
            }
            _pocExpectedProviders = expected.ToImmutable();
            if (!_pocExpectedProviders.IsEmpty)
            {
                PocGenerationRawInventory = PreparedPocGenerationRawInventory.Capture(_pocExpectedProviders, _sourceFiles);
                PocGenerationEnvelope = PreparedPocGenerationEnvelope.Capture(_pocExpectedProviders, _sourceFiles);
            }
            if (_pocExpectedProviders.IsEmpty || !_pocReferenceMap.TryAttachPocProviderStream(this))
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); }
        }

        internal bool HasPocProviderStream => !_pocCandidateSourceCaptureIncomplete && !_pocExpectedProviders.IsDefaultOrEmpty;

        internal void BeginPocProviderStream()
        {
            if (Interlocked.CompareExchange(ref _pocStreamState, 1, 0) != 0)
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); }
        }

        internal void ObservePocProviderYield(TypeProvider provider)
        {
            // Preserve the established candidate channel, but a future all-file
            // certificate cannot accept observed iterator/writer overlap.
            if (_pocWritingProvider is not null || _pocWritingWriter is not null) { PocGenerationEnvelope?.Reject(); }
            if (Volatile.Read(ref _pocStreamState) != 1 || _pocYieldCount >= _pocExpectedProviders.Length ||
                !ReferenceEquals(_pocExpectedProviders[_pocYieldCount], provider))
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); return; }
            _pocYieldCount++;
        }

        internal void EndPocProviderStream(bool exhausted)
        {
            // Only already-observed state: no new input/currentness callbacks.
            if (_sourceDisposed || Volatile.Read(ref _sharedSnapshotStale) != 0 || Volatile.Read(ref _pocCandidateStale) != 0 ||
                _pocWritingProvider is not null || _pocWritingWriter is not null) { PocGenerationEnvelope?.Reject(); }
            if (!exhausted || Interlocked.CompareExchange(ref _pocStreamState, 2, 1) != 1 || _pocYieldCount != _pocExpectedProviders.Length)
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); }
            else { PocGenerationEnvelope?.CompleteStream(); }
        }

        internal void BeginPocProviderWrite(TypeProvider provider, TypeProviderWriter writer)
        {
            if (Volatile.Read(ref _pocStreamState) != 1 || _pocWritingProvider is not null || _pocWritingWriter is not null ||
                _pocWriteCount >= _pocExpectedProviders.Length || _pocYieldCount != _pocWriteCount + 1 ||
                !ReferenceEquals(_pocExpectedProviders[_pocWriteCount], provider) || !writer.OwnsPreparation(this, provider))
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); return; }
            _pocWritingProvider = provider;
            _pocWritingWriter = writer;
        }

        internal void EndPocProviderWrite(TypeProvider provider, TypeProviderWriter writer, CodeFile output)
        {
            if (!ReferenceEquals(_pocWritingProvider, provider) || !ReferenceEquals(_pocWritingWriter, writer) ||
                !writer.OwnsPreparation(this, provider) || !_sourceFiles.TryGetValue(provider, out var file) || output.Name != file.FilePath)
            { PocGenerationEnvelope?.Reject(); Interlocked.Exchange(ref _pocCandidateStale, 1); _pocWritingProvider = null; _pocWritingWriter = null; return; }
            // Passive all-file accounting is separate from candidate-file and
            // Task03 commit channels. Missing coverage cannot authorize a choice.
            PocGenerationEnvelope?.ObserveWrite(provider, output.SourceConsumption);
            if (_pocCandidates.Values.Any(candidate => ReferenceEquals(candidate.File, file)))
            {
                if (output.SourceConsumption is not { WasCurrentAtEndOfWrite: true } receipt || receipt.Generation != file.Generation ||
                    receipt.File != file.FilePath || receipt.RootCount != file.Roots.Length || !_pocWrittenCandidateFiles.TryAdd(file, receipt))
                { _pocInvalidCandidateFiles.Add(file); }
                else if (_pocSupplementalFiles.TryGetValue(file, out var supplemental)) { supplemental.ConfirmWrite(this, writer); }
            }
            _pocWritingProvider = null;
            _pocWritingWriter = null;
            _pocWriteCount++;
        }

        internal void ObservePocTypeConstraint(TypeProvider owner, TypeProviderWriter writer, WhereExpression? constraint)
        {
            if (PocGenerationEnvelope?.GetFile(owner) is not { } plan) { return; }
            if (!ReferenceEquals(_pocWritingProvider, owner) || !ReferenceEquals(_pocWritingWriter, writer) ||
                !writer.OwnsPreparation(this, owner)) { PocGenerationEnvelope.Reject(); return; }
            plan.TypeConstraint?.ObserveNative(constraint);
        }

        internal int PocSourceFileCount => _sourceFiles.Count;

        internal bool OwnsPocSupplementalWrite(PreparedSourceFile file, TypeProviderWriter writer) =>
            Volatile.Read(ref _pocStreamState) == 1 && Volatile.Read(ref _pocCandidateStale) == 0 &&
            ReferenceEquals(_pocWritingProvider, file.FileOwner) && ReferenceEquals(_pocWritingWriter, writer) &&
            _sourceFiles.TryGetValue(file.FileOwner, out var owned) && ReferenceEquals(owned, file) && writer.OwnsPreparation(this, file.FileOwner);

        internal bool IsPocCandidateFileWriteReceiptCurrent(PreparedSourceFile file, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            if (_pocCandidateSourceCaptureIncomplete || budget.Exhausted || !budget.TrySpendReferenceLookup(_pocWrittenCandidateFiles.Count) || !_pocWrittenCandidateFiles.ContainsKey(file) ||
                !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count) || _pocInvalidCandidateFiles.Contains(file) ||
                !budget.TrySpendReferenceLookup(_sourceFiles.Count) || !_sourceFiles.TryGetValue(file.FileOwner, out var owned) || !ReferenceEquals(owned, file) ||
                // Reserve the possible POC-only rejection before observing a
                // mismatch; do not partly record it after exhausting credits.
                !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count)) { return false; }
            // An absent native declaration plan cannot certify selection for
            // this candidate file. This is a query-only veto: do not poison
            // shared state or require full native completion after a late failure.
            if (PocGenerationEnvelope is not { } envelope) { return false; }
            var plan = envelope.GetFile(file.FileOwner, budget);
            if (budget.Exhausted || plan is null || !ReferenceEquals(plan.Source, file) ||
                plan.NativeConsumption is null || plan.HasObservedParameterSelectionFailure) { return false; }
            if (file.IsCurrentWithoutInvalidating(budget)) { return true; }
            // Running out of credits does not establish a source mismatch and
            // must not poison a later independently funded POC query.
            if (!budget.Exhausted) { _pocInvalidCandidateFiles.Add(file); }
            return false;
        }

        // The live import set is populated only by the existing standard writer's
        // UseNamespace calls. It does not certify raw syntax or name binding.
        internal bool HasPocObservedImportConflict(PreparedPocOrdinaryCandidate candidate, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            if (_pocCandidateSourceCaptureIncomplete || budget.Exhausted || !budget.TrySpendReferenceLookup(_pocOwnedCandidates.Count)) { return true; }
            if (!_pocOwnedCandidates.Contains(candidate)) { return false; }
            if (!budget.TrySpendReferenceLookup(_bodyImports.Count)) { return true; }
            if (!_bodyImports.TryGetValue(candidate.File.FileOwner, out var imports)) { return false; }
            foreach (var ns in imports)
            {
                // Split scan, substring storage, vector slots, identifier scan
                // and both keyword probes, all before the native grammar check.
                long textWork = 6L * ns.Length + 1;
                if (textWork > int.MaxValue || !budget.TrySpend((int)textWork)) { return true; }
                if (ns.Split('.').Any(part => !IsSimpleIdentifier(part))) { return true; }
            }
            foreach (var ns in imports)
            {
                if (!SourceCurrentnessChecks.OrdinalLookup(ns, candidate.ForbiddenImports.Count, budget)) { return true; }
                if (candidate.ForbiddenImports.Contains(ns)) { return true; }
            }
            return false;
        }

        // These are preplanned vetoes over the exact objects already obtained by
        // the standard writer. They never force a documentation/warning getter.
        // Prepay lookup, possible POC rejection and the existing capture allowance
        // before observing inputs or allocating a nested capture budget.
        internal void ObservePocFileWarnings(TypeProvider owner, IReadOnlyList<SuppressionStatement> warnings, PocWorkBudget? budget = null)
        {
            if (_pocCandidateSourceCaptureIncomplete) { return; }
            budget ??= new PocWorkBudget(65536);
            if (budget.Exhausted || !budget.TrySpendReferenceLookup(_pocHeaders.Count) ||
                !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count))
            { Interlocked.Exchange(ref _pocCandidateStale, 1); return; }
            if (!_pocHeaders.TryGetValue(owner, out var header)) { return; }
            if (header.WarningsSeen || !ReferenceEquals(owner.CachedDisabledFileWarnings, warnings) ||
                !budget.TrySpend(4096) || TryCapturePocFileWarnings(warnings) is not { } envelope)
            { InvalidatePocHeader(header); return; }
            header.Warnings = warnings;
            header.WarningEnvelope = envelope;
            header.WarningsSeen = true;
        }

        private static PreparedSuppressionEnvelope? TryCapturePocFileWarnings(IReadOnlyList<SuppressionStatement> warnings)
            // Use the additional bounded exact-pragma profile here as well as
            // at finalization; the shared Task03 profile remains unchanged.
            => PreparedSuppressionEnvelope.TryCaptureForPoc(warnings, new PocWorkBudget(4096));

        internal void ObservePocFileDocumentation(TypeProvider owner, bool writeDocs, XmlDocProvider? docs, PocWorkBudget? budget = null)
        {
            if (_pocCandidateSourceCaptureIncomplete) { return; }
            budget ??= new PocWorkBudget(65536);
            if (budget.Exhausted || !budget.TrySpendReferenceLookup(_pocHeaders.Count) ||
                !budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count))
            { Interlocked.Exchange(ref _pocCandidateStale, 1); return; }
            if (!_pocHeaders.TryGetValue(owner, out var header)) { return; }
            if (header.DocsSeen || docs is not null && !ReferenceEquals(owner.CachedXmlDocs, docs))
            { InvalidatePocHeader(header); return; }
            header.DocsSeen = true;
            header.DocsEmitted = writeDocs && !header.File.DisableXmlDocs && docs is not null;
            header.Docs = docs;
            if (header.DocsEmitted)
            {
                // Reserve the existing bounded capture before creating its
                // budget or inspecting any of its input-dependent content.
                if (!budget.TrySpend(4096) || PreparedGeneratedDocumentationEnvelope.TryCaptureForFile(docs!) is not { } envelope)
                { InvalidatePocHeader(header); return; }
                header.DocumentationEnvelope = envelope;
            }
        }

        private void InvalidatePocHeader(PocHeaderObservation header)
        {
            header.Invalid = true;
            _pocInvalidCandidateFiles.Add(header.File);
        }

        // Recheck each captured part immediately after an existing source-proof
        // callback, including while only warnings (not docs) have been observed.
        internal void ObservePocHeaderAfterSourceCallback(PreparedSourceFile? file)
        {
            if (_pocCandidateSourceCaptureIncomplete) { return; }
            if (!TryObservePocHeaderAfterSourceCallback(file, new PocWorkBudget(65536)))
            {
                // A void native observer cannot return an unavailable result.
                // Reject only the POC channel if even recording a local failure
                // could not be funded; never write shared source stale flags.
                Interlocked.Exchange(ref _pocCandidateStale, 1);
            }
        }

        private bool TryObservePocHeaderAfterSourceCallback(PreparedSourceFile? file, PocWorkBudget budget)
        {
            if (file is null) { return true; }
            if (budget.Exhausted || !budget.TrySpendReferenceLookup(_pocHeaders.Count)) { return false; }
            if (!_pocHeaders.TryGetValue(file.FileOwner, out var header) || !ReferenceEquals(header.File, file) || header.Invalid) { return true; }
            if (!budget.TrySpendReferenceLookup(_pocInvalidCandidateFiles.Count)) { return false; }
            bool unavailable = false;
            if ((header.WarningsSeen && (!ReferenceEquals(file.FileOwner.CachedDisabledFileWarnings, header.Warnings) ||
                    header.WarningEnvelope?.IsCurrentWithoutInvalidating(budget) != true)) ||
                (header.DocsSeen && header.DocsEmitted &&
                    (!ReferenceEquals(file.FileOwner.CachedXmlDocs, header.Docs) || header.DocumentationEnvelope is not { } envelope ||
                        !envelope.IsCurrentWithoutInvalidating(budget, out unavailable))))
            {
                // Point-query exhaustion is not evidence of divergence. The
                // void native observer separately rejects unavailable coverage.
                if (budget.Exhausted || unavailable) { return false; }
                InvalidatePocHeader(header);
            }
            return true;
        }

        // Header confinement only; this does not prove the rest of the file,
        // its lexical binding or any permission to shorten an occurrence.
        internal bool? GetPocHeaderObservation(PreparedSourceFile file, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            if (_pocCandidateSourceCaptureIncomplete || budget.Exhausted || !budget.TrySpendReferenceLookup(_pocHeaders.Count)) { return false; }
            if (!_pocHeaders.TryGetValue(file.FileOwner, out var header) || !ReferenceEquals(header.File, file)) { return null; }
            if (!TryObservePocHeaderAfterSourceCallback(file, budget)) { return false; }
            if (header.Invalid) { return false; }
            return header.WarningsSeen && header.DocsSeen ? true : null;
        }

        internal bool IsPocCandidateImportReceiptCurrent(PreparedPocOrdinaryCandidate candidate, PocWorkBudget? budget = null)
        {
            budget ??= new PocWorkBudget(65536);
            if (_pocCandidateSourceCaptureIncomplete || budget.Exhausted || !budget.TrySpendReferenceLookup(_pocOwnedCandidates.Count) || !_pocOwnedCandidates.Contains(candidate) ||
                !SourceCurrentnessChecks.OccurrenceLookup(candidate.Occurrence.Id, _pocCandidates.Count, budget) ||
                !_pocCandidates.TryGetValue(candidate.Occurrence.Id, out var captured) || !ReferenceEquals(captured, candidate) ||
                !IsPocCandidateFileWriteReceiptCurrent(candidate.File, budget) ||
                !budget.TrySpendReferenceLookup(_pocWrittenCandidateFiles.Count) || !_pocWrittenCandidateFiles.TryGetValue(candidate.File, out var receipt) ||
                receipt.Consumed.IsDefault) { return false; }
            var matches = 0;
            foreach (var id in receipt.Consumed)
            {
                if (!SourceCurrentnessChecks.OccurrenceComparison(id, candidate.Occurrence.Id, budget)) { return false; }
                if (id == candidate.Occurrence.Id) { matches++; }
            }
            if (matches != 1 || candidate.File.FileOwner.CachedType is not { } ownerType ||
                !budget.TrySpendReferenceLookup(_bodyImports.Count) || !_bodyImports.TryGetValue(candidate.File.FileOwner, out var imports) ||
                !budget.TrySpendStringComparison(candidate.Type.Namespace, ownerType.Namespace)) { return false; }
            if (candidate.Type.Namespace != ownerType.Namespace &&
                (!SourceCurrentnessChecks.OrdinalLookup(candidate.Type.Namespace, imports.Count, budget) || !imports.Contains(candidate.Type.Namespace))) { return false; }
            return !HasPocObservedImportConflict(candidate, budget);
        }

        // These are stream/path and candidate-file traversal observations only.
        // Non-candidate files are not certified for syntax or confinement; this
        // is not permission to shorten an occurrence.
        internal bool IsObservedPocProviderWriteStreamComplete => !_pocCandidateSourceCaptureIncomplete && Volatile.Read(ref _pocCandidateStale) == 0 &&
            Volatile.Read(ref _pocStreamState) == 2 && _pocYieldCount == _pocExpectedProviders.Length &&
            _pocWriteCount == _pocExpectedProviders.Length && _pocWritingProvider is null && _pocWritingWriter is null;

        internal bool IsPocProviderWriteReceiptComplete => IsObservedPocProviderWriteStreamComplete &&
            _preparedGenerator is { } generator && _pocOutput is { } output && _pocRoots is { } roots &&
            _pocReferenceMap is { } referenceMap && referenceMap.IsOwnedEnumerationRootCurrent(generator, output, roots) &&
            referenceMap.IsObservedEnumerationRootCurrent(output, roots) &&
            referenceMap.IsOwnedEnumerationRootCurrent(generator, output, roots);

        // This is a stored pre-write proposal, not a currentness check or edit
        // permission. Consumers must apply the existing source/currentness guards.
        internal PreparedPocOrdinaryCandidate? GetPrecomputedPocOrdinaryCandidate(PreparedSourceFile file, PreparedSourceOccurrence occurrence)
        {
            if (_pocCandidateSourceCaptureIncomplete || !_pocCandidates.TryGetValue(occurrence.Id, out var candidate) || !ReferenceEquals(candidate.File, file) ||
                !ReferenceEquals(candidate.Occurrence, occurrence) || !PocObservedInputsAreCurrent() ||
                !IsPocCandidateObservedCurrent(candidate)) { return null; }
            return candidate;
        }

        private bool IsPocCandidateCurrent(PreparedPocOrdinaryCandidate candidate) =>
            !_pocCandidateSourceCaptureIncomplete && _pocOwnedCandidates.Contains(candidate) && PocInputsAreCurrent() && IsPocCandidateObservedCurrent(candidate) && PocObservedInputsAreCurrent();

        private bool IsPocCandidateObservedCurrent(PreparedPocOrdinaryCandidate candidate, PocWorkBudget? sourceBudget = null, PocWorkBudget? scopeBudget = null) =>
            _pocOwnedCandidates.Contains(candidate) && !_pocInvalidCandidateFiles.Contains(candidate.File) &&
            PocRelatedScopesAreCurrent(candidate.File, scopeBudget) &&
            (sourceBudget is null ? candidate.SourceObservation.IsCurrent : candidate.SourceObservation.IsCurrentForPoc(sourceBudget)) &&
            candidate.Occurrence.Producer is DeclarationExpression declaration && ReferenceEquals(declaration.Variable.Type, candidate.Type) &&
            candidate.Type.IdentityVersion == candidate.TypeRevision;

        private bool PocInputsAreCurrent()
        {
            if (!PocObservedInputsAreCurrent()) { return false; }
            if (SourceInputsAreCurrent() && PocObservedInputsAreCurrent()) { return true; }
            Interlocked.Exchange(ref _pocCandidateStale, 1);
            return false;
        }

        private static bool ReservePocRemovalComparison(ImmutableHashSet<string> snapshot, PocWorkBudget budget)
        {
            if (!budget.TrySpend(snapshot.Count)) { return false; }
            foreach (var name in snapshot)
            {
                // The existing native check requires equal counts before any
                // Contains call. Reserve every possible ordinal probe first.
                if (!ReservePocScopeNameLookup(name, snapshot.Count, budget)) { return false; }
            }
            return true;
        }

        private bool PocObservedInputsAreCurrent(PocWorkBudget? budget = null)
        {
            if (Volatile.Read(ref _pocCandidateStale) != 0) { return false; }
            budget ??= new PocWorkBudget(PocNameInventoryWorkLimit);
            if (budget.TrySpend() && Volatile.Read(ref _valid) != 0 && !_sourceDisposed && _preparedGenerator is { } generator && IsCurrentGenerator(generator) &&
                _sourceInputs is not null && _sourceModel is not null && ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs) &&
                _pocHasDirectSourceGetter && generator.IsBaseSourceInputModelCurrent(_sourceModel) &&
                _pocReferenceMap is not null && _pocOutput is not null && _pocRoots is not null && _pocRemovalCandidates is not null &&
                ReferenceEquals(_pocRoots, _sourceRootList) && _pocReferenceMap.IsObservedReferenceMapRootCurrent(_pocOutput, _pocRoots) &&
                ReservePocRemovalComparison(_pocRemovalCandidates, budget) && _pocReferenceMap.IsObservedRemovalCandidatesCurrent(_pocRemovalCandidates) &&
                _sourceConfiguration is not null && ReferenceEquals(generator.Configuration, _sourceConfiguration) &&
                _pocLicenseHeader is not null && budget.TrySpendStringComparison(generator.LicenseHeader, _pocLicenseHeader) && generator.LicenseHeader == _pocLicenseHeader &&
                _sourceConfiguration.DisableXmlDocs == _sourceDisableXmlDocs && _sourceRootList is not null &&
                ContractCollections.ReserveSharedReferencesForPoc(_sourceRootList, _sourceRootSnapshot.Length, budget) &&
                ContractCollections.SameReferences(_sourceRootList, _sourceRootSnapshot) &&
                !_pocProviderNames.IsDefaultOrEmpty && _pocProviderNames.All(name => name.IsCurrentForPoc(budget)) &&
                !_pocWriteDecisionInventoryInvalid && _pocWriteDecisionNames.All(name => name.IsCurrentForPoc(budget)) &&
                _pocWriteDecisionParents.All(parent => parent.IsCurrentForPoc(budget)) && ArePocModelFactoriesCurrent(budget) &&
                !_pocRootSerializations.IsDefault && _pocRootSerializations.All(root => root.IsCurrentForPoc(budget))) { return true; }
            if (!budget.Exhausted) { Interlocked.Exchange(ref _pocCandidateStale, 1); }
            return false;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // A deliberately narrow, negative context recipe: native discovery can
    // produce NO buildable entries. This conclusion comes from CURRENT roots,
    // members, lookup trees and native interface facts, never cached attributes.
    // Models, MRW-positive framework types, custom/contract views, unknown
    // dispatch/carriers and cold canonical members take the legacy path.
    // This is an observed capture operation, not a lock, an ABA proof for raw
    // arrays, or namespace/Task03/Task04 permission.
    internal sealed class PreparedPocContextAttributeRecipe
    {
        private const int MaxDepth = 64;
        private static readonly Type s_runtimeType = typeof(object).GetType();
        private readonly TypeProvider _owner;
        private readonly PreparedPocPostMapAttributeBoundary _boundary;
        private readonly PocHeaderFinalizationBridge.ContextRecipeAudit _audit;
        private readonly Dictionary<TypeProvider, ProviderInput> _providers = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CSharpType, DeclaredTypeSnapshot> _types = new(ReferenceEqualityComparer.Instance);
        private readonly List<ElementInput> _elements = new();
        private readonly List<NullableRuntimeInput> _nullableRuntime = new();
        private readonly List<ProviderInput> _lookupOrder = new();
        private readonly IReadOnlyList<MethodBodyStatement>? _attributeCarrier;
        private readonly HashSet<MethodBodyStatement>? _originalCarrier;
        private readonly ImmutableArray<MethodBodyStatement> _attributes;
        private readonly ImmutableArray<MethodBodyStatement> _original;
        private readonly ImmutableArray<MethodBodyStatement> _visitorAdditions;
        private readonly PreparedPocAttributeEnvelope _visitorEnvelope;
        private bool _stale;

        private PreparedPocContextAttributeRecipe(TypeProvider owner, PreparedPocPostMapAttributeBoundary boundary,
            PocHeaderFinalizationBridge.ContextRecipeAudit audit, ImmutableArray<MethodBodyStatement> attributes,
            ImmutableArray<MethodBodyStatement> original, ImmutableArray<MethodBodyStatement> additions,
            PreparedPocAttributeEnvelope visitorEnvelope)
        {
            _owner = owner; _boundary = boundary; _audit = audit;
            _attributeCarrier = owner.CachedWriteAttributes; _originalCarrier = owner.CachedOriginalAttributes;
            _attributes = attributes; _original = original; _visitorAdditions = additions;
            _visitorEnvelope = visitorEnvelope;
        }

        internal static PreparedPocContextAttributeRecipe? TryCapture(TypeProvider owner,
            PreparedPocPostMapAttributeBoundary boundary, PocWorkBudget budget)
        {
            var audit = PocHeaderFinalizationBridge.GetContextRecipe(owner);
            if (audit is null || !ReferenceEquals(audit.CachedNativeOutput(), boundary.Output) ||
                !budget.TrySpend(audit.ProviderTypes.Length + audit.MethodTypes.Length) ||
                audit.ProviderTypes.Length > 32 || audit.MethodTypes.Length > 8 || !boundary.IsCurrent(budget) ||
                !TryAttributeInputs(owner, budget, out var attributes, out var original, out var additions)) { return null; }
            // Root identity hashing is not payload admission. Validate the
            // actual visitor IR before changing the native cache/origin, so an
            // unsupported envelope retains ONLY its legacy final-write build.
            if (!budget.TrySpend(1 + additions.Length * 2 + PreparedPocHeaderFinalization.AttributeEnvelopeLimit * 2)) { return null; }
            var visitorCarrier = new MethodBodyStatement[additions.Length];
            additions.CopyTo(visitorCarrier);
            // Prove the eventual header's per-pass limit, not merely this
            // recipe's larger aggregate allowance. Its native-owned projection
            // costs one more root-copy unit than this private proof array, in
            // BOTH capture and currentness (public Count/indexed readback).
            const int visitorLimit = PreparedPocHeaderFinalization.AttributeEnvelopeLimit - 1;
            var visitorEnvelope = PreparedPocAttributeEnvelope.TryCapture(visitorCarrier, new PocWorkBudget(visitorLimit));
            if (visitorEnvelope is null || !visitorEnvelope.IsCurrent(new PocWorkBudget(visitorLimit))) { return null; }
            var result = new PreparedPocContextAttributeRecipe(owner, boundary, audit, attributes, original, additions, visitorEnvelope);
            var ancestors = new HashSet<TypeProvider>(ReferenceEqualityComparer.Instance);
            foreach (var root in boundary.Roots)
            {
                if (!result.CaptureTree(root, ancestors, budget, 0)) { return null; }
            }
            if (!budget.TrySpendReferenceLookup(result._providers.Count) ||
                !result._providers.TryGetValue(owner, out var ownerInput) || !ownerInput.ShouldWrite ||
                !result.PrechargeNativeRecipe(budget) || !result.IsCurrent(budget, afterBuild: false)) { return null; }
            return result;
        }

        private bool CaptureTree(TypeProvider provider, HashSet<TypeProvider> ancestors, PocWorkBudget budget, int depth)
        {
            if (provider is null || depth >= MaxDepth || !budget.TrySpend() ||
                !budget.TrySpendReferenceLookup(ancestors.Count) || !ancestors.Add(provider) ||
                !budget.TrySpendReferenceLookup(_providers.Count)) { return false; }
            if (!_providers.TryGetValue(provider, out var input))
            {
                // GetType is nonvirtual. Do not touch ANY provider getters until
                // exact built-in dispatch has been established.
                if (!KnownProvider(provider.GetType(), budget) || provider.CachedType is not { } type ||
                    !CaptureType(type, budget) || type.DeclaringType is not null ||
                    !SimpleName(type.Name, false, budget) || !SimpleName(type.Namespace, true, budget) ||
                    !provider.IsCustomCodeViewMaterialized || provider.CachedCustomCodeView is not null ||
                    !provider.IsLastContractViewMaterialized || provider.CachedLastContractView is not null ||
                    !ContractCollections.TryCopyForPoc(provider.CachedNestedTypes, budget, out var nested) ||
                    !ContractCollections.TryCopyForPoc(provider.CachedSerializationProviders, budget, out var serialization) ||
                    !ContractCollections.TryCopyForPoc(provider.CachedImplements, budget, out var implements)) { return false; }
                foreach (var implemented in implements) { if (!CaptureType(implemented, budget)) { return false; } }
                // Each metadata name creation/normalization/hash/lookup has a
                // separate charge below. These simple nonempty names make the
                // normalizer an identity operation; no simple-name/factory
                // fallback is reachable for a root's exact lookup.
                if (!SpendMetadataName(type, budget) || !_boundary.TryShouldWrite(provider, type, budget, out var shouldWrite)) { return false; }
                input = new ProviderInput(provider, type, ProviderReferenceMapAnalyzer.GetProviderTypeName(type),
                    nested, serialization, implements, shouldWrite);
                if (!budget.TrySpendReferenceLookup(_providers.Count)) { return false; }
                _providers.Add(provider, input);
            }
            _lookupOrder.Add(input);
            foreach (var child in input.Nested) { if (!CaptureTree(child, ancestors, budget, depth + 1)) { return false; } }
            foreach (var child in input.Serialization) { if (!CaptureTree(child, ancestors, budget, depth + 1)) { return false; } }
            if (!budget.TrySpendReferenceLookup(ancestors.Count)) { return false; }
            ancestors.Remove(provider);
            return true;
        }

        private bool PrechargeNativeRecipe(PocWorkBudget budget)
        {
            var rootNames = new NativeNames();
            var visitedFrameworkNames = new NativeNames();
            // Native name deduplication happens BEFORE ShouldWrite/canonical
            // getters. In particular, cold duplicate RestClient views are not
            // forced just because an earlier Client has the same type name.
            foreach (var root in _boundary.Roots)
            {
                if (!budget.TrySpendReferenceLookup(_providers.Count)) { return false; }
                var input = _providers[root];
                if (!rootNames.TryAdd(input.Type, budget, out var added, copies: 2)) { return false; }
                if (!added) { continue; }
                // Native ShouldWrite also executes for a removed root. Fund
                // every membership chain separately from metadata creation.
                if (!SpendMetadataName(input.Type, budget) || !_boundary.TrySpendNativeWriteLookup(input.Type, budget)) { return false; }
                if (!input.ShouldWrite) { continue; }
                if (!PrechargeExactRootLookup(input, budget) || !ProviderIsNegative(input, budget, 0)) { return false; }
                if (!CaptureMembers(input, budget)) { return false; }
                foreach (var property in input.Properties)
                {
                    var type = property.Type;
                    if (!InnermostCollection(type, budget, out type) || !FrameworkIsNegative(type, visitedFrameworkNames, budget)) { return false; }
                }
                foreach (var method in input.Methods)
                {
                    if (!UnwrapReturnType(method.Signature.ReturnType, budget, out var type) ||
                        type is not null && !FrameworkIsNegative(type, visitedFrameworkNames, budget)) { return false; }
                }
                // Non-model branch of CollectBuildableTypesRecursiveCore.
                foreach (var implemented in input.Implements)
                {
                    if (!FrameworkIsNegative(implemented, visitedFrameworkNames, budget)) { return false; }
                }
            }
            // Both native buildable sets and the sorted generated-attribute
            // dictionary are now proven empty. No GetProperties, source lookup,
            // attribute construction/formatting, suppression writer or sorter
            // with caller data can be reached. Rebuild still merges visitors.
            return budget.TrySpend(32 + _attributes.Length * 4 + _visitorAdditions.Length * 4);
        }

        private bool PrechargeExactRootLookup(ProviderInput root, PocWorkBudget budget)
        {
            if (!SpendMetadataName(root.Type, budget)) { return false; }
            // Mirror the actual DFS order (nested, then serialization), including
            // repeated occurrences. Charge each repeated name creation, ordinal
            // comparison and carrier scan BEFORE native lookup can execute it.
            foreach (var candidate in _lookupOrder)
            {
                if (!SpendMetadataName(candidate.Type, budget) ||
                    !budget.TrySpend(root.MetadataName.Length + candidate.MetadataName.Length + 1)) { return false; }
                if (candidate.MetadataName == root.MetadataName)
                {
                    // IsResolvableBuildableProvider's exact match cannot enter
                    // Model/SystemObjectModel/source/factory branches in this
                    // registered profile. It does execute another ShouldWrite.
                    return SpendMetadataName(candidate.Type, budget) && _boundary.TrySpendNativeWriteLookup(candidate.Type, budget);
                }
            }
            return false;
        }

        private bool ProviderIsNegative(ProviderInput input, PocWorkBudget budget, int depth)
        {
            // ModelReaderWriterHelpers.ImplementsModelReaderWriter(provider):
            // first serialization OfType scan, then Implements.Any, then a
            // second serialization scan with recursive helper calls. MRW and
            // model provider runtime types are excluded by KnownProvider.
            if (depth >= MaxDepth || !budget.TrySpend(1 + input.Serialization.Length * 2 + input.Implements.Length)) { return false; }
            foreach (var implemented in input.Implements)
            {
                if (!budget.TrySpend(implemented.Name.Length + implemented.Namespace.Length) ||
                    implemented.Namespace == "System.ClientModel.Primitives" &&
                    implemented.Name is "IJsonModel" or "IPersistableModel") { return false; }
            }
            foreach (var child in input.Serialization)
            {
                if (!budget.TrySpendReferenceLookup(_providers.Count) ||
                    !ProviderIsNegative(_providers[child], budget, depth + 1)) { return false; }
            }
            return true;
        }

        private bool CaptureMembers(ProviderInput input, PocWorkBudget budget)
        {
            var view = input.Provider.CachedCanonicalView;
            if (view is null || view.GetType() != typeof(CanonicalTypeProvider) ||
                !ContractCollections.TryCopyForPoc(view.CachedProperties, budget, out var properties) ||
                !ContractCollections.TryCopyForPoc(view.CachedMethods, budget, out var methods)) { return false; }
            input.Canonical = view; input.PropertyCarrier = view.CachedProperties; input.MethodCarrier = view.CachedMethods;
            var propertyInputs = ImmutableArray.CreateBuilder<PropertyInput>(properties.Length);
            foreach (var property in properties)
            {
                if (property is null || !budget.TrySpend() ||
                    property.GetType() != typeof(PropertyProvider) && property.GetType() != typeof(IndexPropertyProvider) ||
                    !CaptureType(property.Type, budget)) { return false; }
                propertyInputs.Add(new(property, property.Type, property.TypeRevision));
            }
            var methodInputs = ImmutableArray.CreateBuilder<MethodInput>(methods.Length);
            foreach (var method in methods)
            {
                if (method is null || !budget.TrySpend(_audit.MethodTypes.Length + 1) ||
                    method.GetType() != typeof(MethodProvider) && !ExactTypeIn(method.GetType(), _audit.MethodTypes) ||
                    method.Signature is null || method.Signature.GetType() != typeof(MethodSignature) ||
                    method.Signature.ReturnType is { } returnType && !CaptureType(returnType, budget)) { return false; }
                var signature = method.Signature;
                methodInputs.Add(new(method, signature, method.DeclarationRevision, signature.DeclarationRevision, signature.ReturnType));
            }
            input.Properties = propertyInputs.MoveToImmutable(); input.Methods = methodInputs.MoveToImmutable();
            input.PropertyElements = properties; input.MethodElements = methods;
            // The future native enumeration of each member is separate from
            // capture/copy work; it reads these exact members/signatures/types.
            return budget.TrySpend(properties.Length + methods.Length + input.Implements.Length);
        }

        private bool CaptureType(CSharpType type, PocWorkBudget budget)
        {
            if (type is null || type.GetType() != typeof(CSharpType) || !budget.TrySpend()) { return false; }
            if (!budget.TrySpendReferenceLookup(_types.Count)) { return false; }
            if (_types.ContainsKey(type)) { return true; }
            if (!ExactTypeTree(type, new HashSet<CSharpType>(ReferenceEqualityComparer.Instance), budget, 0) ||
                !DeclaredTypeSnapshot.TryCaptureForPoc(type, budget, out var snapshot)) { return false; }
            if (!budget.TrySpendReferenceLookup(_types.Count)) { return false; }
            _types.Add(type, snapshot!);
            return true;
        }

        private static bool ExactTypeTree(CSharpType type, HashSet<CSharpType> ancestors, PocWorkBudget budget, int depth)
        {
            if (type is null || type.GetType() != typeof(CSharpType) || depth >= MaxDepth || !budget.TrySpend() ||
                !budget.TrySpendReferenceLookup(ancestors.Count) || !ancestors.Add(type) || type.IsLiteral || type.HasUnionMetadata || type.Name is null || type.Namespace is null ||
                type.Name.Length > 4096 || type.Namespace.Length > 4096 ||
                !budget.TrySpend(type.Name.Length + type.Namespace.Length) ||
                type.IsFrameworkType && type.FrameworkType.GetType() != s_runtimeType ||
                !ContractCollections.TryCopyForPoc(type.Arguments, budget, out var arguments)) { return false; }
            foreach (var argument in arguments) { if (!ExactTypeTree(argument, ancestors, budget, depth + 1)) { return false; } }
            if (type.DeclaringType is { } declaring && !ExactTypeTree(declaring, ancestors, budget, depth + 1)) { return false; }
            if (!budget.TrySpendReferenceLookup(ancestors.Count)) { return false; }
            ancestors.Remove(type);
            return true;
        }

        private bool InnermostCollection(CSharpType start, PocWorkBudget budget, out CSharpType result)
        {
            result = start;
            // Native properties unwrap collections, but do NOT unwrap arbitrary
            // generic wrappers. Do not substitute the method-return policy.
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (!budget.TrySpend()) { return false; }
                if (!result.TryGetPocCollectionKind(out var collection, out var dictionary)) { return false; }
                if (!collection) { return true; }
                var index = dictionary ? 1 : 0;
                if (!ContractCollections.TryCopyForPoc(result.Arguments, budget, out var arguments) || arguments.Length <= index) { return false; }
                // A previously cached ElementType, not the original argument,
                // is what the native getter actually returns after mutations.
                var element = result.CachedElementType ?? arguments[index];
                if (!CaptureType(element, budget)) { return false; }
                _elements.Add(new(result, result.CachedElementType, element));
                result = element;
            }
            return false;
        }

        private bool UnwrapReturnType(CSharpType? start, PocWorkBudget budget, out CSharpType? result)
        {
            result = start;
            if (result is null) { return true; }
            if (!budget.TrySpend()) { return false; }
            if (result.IsFrameworkType && (ReferenceEquals(result.FrameworkType, typeof(System.Threading.Tasks.Task)) ||
                ReferenceEquals(result.FrameworkType, typeof(System.Threading.Tasks.ValueTask))))
            {
                if (result.Arguments.Count == 0) { result = null; return true; }
                result = result.Arguments[0];
            }
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (!budget.TrySpend()) { return false; }
                if (!result.IsFrameworkType || result.Arguments.Count != 1)
                {
                    if (!InnermostCollection(result, budget, out var element)) { return false; }
                    result = element; return true;
                }
                // Native UnwrapReturnType's helper call happens BEFORE taking
                // the one argument. Only an observed negative result can admit
                // this path; the future GetInterfaces array is precharged here.
                if (!_audit.SpendHelperNegative(result.FrameworkType, budget.TrySpend)) { return false; }
                result = result.Arguments[0];
            }
            return false;
        }

        private bool FrameworkIsNegative(CSharpType type, NativeNames visited, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return false; }
            // The caller clears nullability BEFORE visited-type deduplication.
            // Charge every copy, even when that framework name was seen earlier.
            if (type.IsNullable)
            {
                if (type.IsFrameworkType)
                {
                    if (!type.TrySpendPocNullableRuntimeConstruction(budget, out var cachedBase)) { return false; }
                    _nullableRuntime.Add(new(type, cachedBase));
                }
                else if (!budget.TrySpend(48 + type.Name.Length + type.Namespace.Length + type.Arguments.Count * 2)) { return false; }
            }
            if (!type.IsFrameworkType) { return true; }
            if (!visited.TryAdd(type, budget, out var added)) { return false; }
            if (!added) { return true; }
            // This is ShouldProcessCSharpType, including concrete nongeneric
            // returns: it calls the IDENTITY helper. The unused direct context
            // NAME predicate is separately observed, never conflated with it.
            return _audit.SpendHelperNegative(type.FrameworkType, budget.TrySpend);
        }

        internal bool TryReservePostBuildCurrentness(PocWorkBudget budget, out PocWorkBudget postBuildBudget)
        {
            postBuildBudget = null!;
            if (!IsCurrent(budget, afterBuild: false, out var commonWork)) { return false; }
            // Negative discovery constructs no generated attribute/buildable
            // types. The only native framework CSharpType construction is a
            // nongeneric/non-nested nullable copy; each actual base-cache hit
            // was proved by TrySpendPocNullableRuntimeConstruction. Neither that
            // constructor nor the negative helper/reflection observations can
            // grow the runtime CSharpType cache on unchanged admitted inputs.
            // Thus its per-occurrence scans and the other common checks retain
            // their measured bound. This is not a concurrent-mutation witness.
            // The after-tail has empty originals and only visitor additions:
            // native projection readback (1 + v), then reference comparison (v).
            long postWork = commonWork + 2L * _visitorAdditions.Length + 1;
            if (postWork > int.MaxValue || !budget.TrySpend((int)postWork)) { _stale = true; return false; }
            postBuildBudget = new PocWorkBudget((int)postWork);
            return true;
        }

        internal bool IsCurrent(PocWorkBudget budget, bool afterBuild) => IsCurrent(budget, afterBuild, out _);

        private bool IsCurrent(PocWorkBudget budget, bool afterBuild, out int commonWork)
        {
            commonWork = 0;
            var before = budget.Remaining;
            if (_stale) { return false; }
            // The session may legitimately observe a foreign output. Only the
            // exact audited nonforcing SCM cache can identify what its native
            // builder will actually traverse; never force an output getter.
            if (!ReferenceEquals(_audit.CachedNativeOutput(), _boundary.Output) ||
                !_boundary.IsCurrent(budget) || !_visitorEnvelope.IsCurrent(budget)) { _stale = true; return false; }
            foreach (var snapshot in _types.Values)
            {
                if (!snapshot.IsCurrentForPoc(budget)) { _stale = true; return false; }
            }
            foreach (var input in _providers.Values)
            {
                if (!input.IsCurrent(_boundary, budget)) { _stale = true; return false; }
            }
            foreach (var conversion in _nullableRuntime)
            {
                if (!conversion.Type.TrySpendPocNullableRuntimeConstruction(budget, out var currentBase) ||
                    !ReferenceEquals(currentBase, conversion.CachedBase)) { _stale = true; return false; }
            }
            foreach (var element in _elements)
            {
                if (!budget.TrySpend() || element.Type.CachedElementType is { } current && !ReferenceEquals(current, element.Expected) ||
                    element.Original is not null && !ReferenceEquals(element.Type.CachedElementType, element.Original))
                { _stale = true; return false; }
            }
            commonWork = before - budget.Remaining;
            if (afterBuild)
            {
                if (_owner.CachedOriginalAttributes is not { Count: 0 } originals || originals.GetType() != typeof(HashSet<MethodBodyStatement>) ||
                    !ReferenceEquals(originals.Comparer, EqualityComparer<MethodBodyStatement>.Default) ||
                    !TryCopyAttributes(_owner, budget, out var actual) || !Same(actual, _visitorAdditions, budget)) { _stale = true; return false; }
            }
            else if (!ReferenceEquals(_owner.CachedWriteAttributes, _attributeCarrier) ||
                !ReferenceEquals(_owner.CachedOriginalAttributes, _originalCarrier) ||
                !TryCopyAttributes(_owner, budget, out var actual) || !Same(actual, _attributes, budget) ||
                !TryCopyOriginal(_owner, budget, out var original) || !Same(original, _original, budget)) { _stale = true; return false; }
            return true;
        }

        private bool KnownProvider(Type type, PocWorkBudget budget) => budget.TrySpend(_audit.ProviderTypes.Length + 8) &&
            (type == typeof(FixedEnumProvider) || type == typeof(ChangeTrackingListDefinition) || type == typeof(ChangeTrackingDictionaryDefinition) ||
             type == typeof(ArgumentDefinition) || type == typeof(CodeGenMemberAttributeDefinition) || type == typeof(CodeGenSerializationAttributeDefinition) ||
             type == typeof(CodeGenSuppressAttributeDefinition) || type == typeof(CodeGenTypeAttributeDefinition) || ExactTypeIn(type, _audit.ProviderTypes));
        private static bool ExactTypeIn(Type type, Type[] types)
        {
            foreach (var candidate in types) { if (ReferenceEquals(type, candidate)) { return true; } }
            return false;
        }
        private static bool SimpleName(string text, bool ns, PocWorkBudget budget)
        {
            if (text.Length == 0 || !budget.TrySpend(text.Length)) { return false; }
            foreach (var c in text) { if (!(char.IsLetterOrDigit(c) || c == '_' || ns && c == '.')) { return false; } }
            return true;
        }
        private static bool SpendMetadataName(CSharpType type, PocWorkBudget budget) =>
            budget.TrySpend(type.Name.Length * 3 + type.Namespace.Length * 2 + 16);

        private static bool SafeHash(MethodBodyStatement statement) => statement is not null &&
            (statement.GetType() == typeof(AttributeStatement) || statement.GetType() == typeof(SuppressionStatement));
        private static bool TryCopyAttributes(TypeProvider owner, PocWorkBudget budget, out ImmutableArray<MethodBodyStatement> items)
        {
            if (owner.CachedWriteAttributes is not { } attributes) { items = []; return true; }
            return ContractCollections.TryCopyForPoc(attributes, budget, out items) ||
                !budget.Exhausted && owner.TryCopyCurrentPocAttributeProjection(attributes, budget, out items);
        }
        private static bool TryCopyOriginal(TypeProvider owner, PocWorkBudget budget, out ImmutableArray<MethodBodyStatement> items)
        {
            items = [];
            if (owner.CachedOriginalAttributes is not { } original) { return true; }
            if (original.GetType() != typeof(HashSet<MethodBodyStatement>) ||
                !ReferenceEquals(original.Comparer, EqualityComparer<MethodBodyStatement>.Default) ||
                !budget.TrySpend(original.EnsureCapacity(0))) { return false; }
            var builder = ImmutableArray.CreateBuilder<MethodBodyStatement>(original.Count);
            foreach (var statement in original) { if (!SafeHash(statement)) { return false; } builder.Add(statement); }
            if (builder.Count != original.Count) { return false; }
            items = builder.MoveToImmutable(); return true;
        }
        private static bool TryAttributeInputs(TypeProvider owner, PocWorkBudget budget, out ImmutableArray<MethodBodyStatement> attributes,
            out ImmutableArray<MethodBodyStatement> original, out ImmutableArray<MethodBodyStatement> additions)
        {
            attributes = []; original = []; additions = [];
            if (!TryCopyAttributes(owner, budget, out attributes) || !TryCopyOriginal(owner, budget, out original)) { return false; }
            // Validate BOTH original hash keys and queried visitor values before
            // any native HashSet.Contains/default GetHashCode/Equals. These exact
            // two classes inherit object identity equality and hashing.
            foreach (var statement in attributes) { if (!budget.TrySpend() || !SafeHash(statement)) { return false; } }
            var result = ImmutableArray.CreateBuilder<MethodBodyStatement>();
            if (owner.CachedWriteAttributes is not null && owner.CachedOriginalAttributes is { } originals)
            {
                foreach (var statement in attributes)
                {
                    // The preflight probe and native RebuildAttributes probe
                    // each traverse the OLD original set. Charge both collision
                    // chains, plus native visitor-set construction/filter work,
                    // before performing the first probe.
                    if (!budget.TrySpend(original.Length * 2 + attributes.Length + 4)) { return false; }
                    if (!originals.Contains(statement)) { result.Add(statement); }
                }
            }
            additions = result.ToImmutable(); return true;
        }
        private static bool Same(ImmutableArray<MethodBodyStatement> left, ImmutableArray<MethodBodyStatement> right, PocWorkBudget budget)
        {
            if (left.Length != right.Length || !budget.TrySpend(left.Length)) { return false; }
            for (int i = 0; i < left.Length; i++) { if (!ReferenceEquals(left[i], right[i])) { return false; } }
            return true;
        }

        private sealed record PropertyInput(PropertyProvider Property, CSharpType Type, long Revision);
        private sealed record MethodInput(MethodProvider Method, MethodSignature Signature, long Revision,
            long SignatureRevision, CSharpType? ReturnType);
        private sealed record ElementInput(CSharpType Type, CSharpType? Original, CSharpType Expected);
        private sealed record NullableRuntimeInput(CSharpType Type, CSharpType? CachedBase);
        private sealed class ProviderInput
        {
            internal readonly TypeProvider Provider;
            internal readonly CSharpType Type;
            internal readonly string MetadataName;
            internal readonly ImmutableArray<TypeProvider> Nested, Serialization;
            internal readonly ImmutableArray<CSharpType> Implements;
            internal readonly bool ShouldWrite;
            private readonly long _revision;
            private readonly IReadOnlyList<TypeProvider>? _nestedCarrier, _serializationCarrier;
            private readonly IReadOnlyList<CSharpType>? _implementsCarrier;
            internal TypeProvider? Canonical;
            internal IReadOnlyList<PropertyProvider>? PropertyCarrier;
            internal IReadOnlyList<MethodProvider>? MethodCarrier;
            internal ImmutableArray<PropertyProvider> PropertyElements = [];
            internal ImmutableArray<MethodProvider> MethodElements = [];
            internal ImmutableArray<PropertyInput> Properties = [];
            internal ImmutableArray<MethodInput> Methods = [];
            internal ProviderInput(TypeProvider provider, CSharpType type, string metadataName, ImmutableArray<TypeProvider> nested,
                ImmutableArray<TypeProvider> serialization, ImmutableArray<CSharpType> implements, bool shouldWrite)
            {
                Provider = provider; Type = type; MetadataName = metadataName; Nested = nested; Serialization = serialization;
                Implements = implements; ShouldWrite = shouldWrite; _revision = provider.DeclarationRevision;
                _nestedCarrier = provider.CachedNestedTypes; _serializationCarrier = provider.CachedSerializationProviders;
                _implementsCarrier = provider.CachedImplements;
            }
            internal bool IsCurrent(PreparedPocPostMapAttributeBoundary boundary, PocWorkBudget budget)
            {
                if (!budget.TrySpend() || Provider.DeclarationRevision != _revision || !ReferenceEquals(Provider.CachedType, Type) ||
                    !Provider.IsCustomCodeViewMaterialized || Provider.CachedCustomCodeView is not null ||
                    !Provider.IsLastContractViewMaterialized || Provider.CachedLastContractView is not null ||
                    !ReferenceEquals(Provider.CachedNestedTypes, _nestedCarrier) || !ReferenceEquals(Provider.CachedSerializationProviders, _serializationCarrier) ||
                    !ReferenceEquals(Provider.CachedImplements, _implementsCarrier) ||
                    !ContractCollections.SameReferencesForPoc(_nestedCarrier, Nested, budget) ||
                    !ContractCollections.SameReferencesForPoc(_serializationCarrier, Serialization, budget) ||
                    !ContractCollections.SameReferencesForPoc(_implementsCarrier, Implements, budget) ||
                    !SpendMetadataName(Type, budget) || !boundary.TryShouldWrite(Provider, Type, budget, out var shouldWrite) ||
                    shouldWrite != ShouldWrite) { return false; }
                if (Canonical is null) { return true; }
                if (!ReferenceEquals(Provider.CachedCanonicalView, Canonical) || !ReferenceEquals(Canonical.CachedProperties, PropertyCarrier) ||
                    !ReferenceEquals(Canonical.CachedMethods, MethodCarrier) ||
                    !ContractCollections.SameReferencesForPoc(PropertyCarrier, PropertyElements, budget) ||
                    !ContractCollections.SameReferencesForPoc(MethodCarrier, MethodElements, budget) || !budget.TrySpend(Properties.Length + Methods.Length)) { return false; }
                foreach (var property in Properties)
                {
                    if (!ReferenceEquals(property.Property.Type, property.Type) || property.Property.TypeRevision != property.Revision) { return false; }
                }
                foreach (var method in Methods)
                {
                    if (!ReferenceEquals(method.Method.Signature, method.Signature) || method.Method.DeclarationRevision != method.Revision ||
                        method.Signature.DeclarationRevision != method.SignatureRevision ||
                        !ReferenceEquals(method.Signature.ReturnType, method.ReturnType)) { return false; }
                }
                return true;
            }
        }

        // Same name-comparer hash recipe as native context discovery. Hash the
        // bounded strings and account for actual collision buckets, rather than
        // assuming constant HashSet work or reserving a fixed amount as proof.
        private sealed class NativeNames
        {
            private readonly Dictionary<int, List<CSharpType>> _buckets = new();
            private int _count;
            internal bool TryAdd(CSharpType type, PocWorkBudget budget, out bool added, int copies = 1)
            {
                added = false;
                // Even DIFFERENT full hash codes can share a native bucket.
                // Bound its entry-chain walk by the entire current set size;
                // only equal full hashes can invoke string equality below.
                // Two root passes cover both eligible and visited provider sets.
                if (!budget.TrySpend(copies * (type.Name.Length + type.Namespace.Length + _count + 2))) { return false; }
                HashCode hash = new(); hash.Add(type.Namespace); hash.Add(type.Name);
                var code = hash.ToHashCode();
                if (!budget.TrySpendReferenceLookup(_buckets.Count)) { return false; }
                if (!_buckets.TryGetValue(code, out var bucket))
                {
                    if (!budget.TrySpendReferenceLookup(_buckets.Count)) { return false; }
                    bucket = new(); _buckets.Add(code, bucket);
                }
                foreach (var previous in bucket)
                {
                    if (!budget.TrySpend(copies * (type.Name.Length + type.Namespace.Length + 2))) { return false; }
                    if (previous.Namespace == type.Namespace && previous.Name == type.Name) { return true; }
                }
                if (!budget.TrySpend()) { return false; }
                bucket.Add(type); _count++; added = true; return true;
            }
        }
    }
}

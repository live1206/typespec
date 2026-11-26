// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        // CSharpGen's actual native reference-map session owns this policy. No
        // arbitrary retained predicate is called after the final host check.
        // Generator accessors must honor the stable/base-forwarding invocation
        // contract; base-slot identity does not audit an arbitrary virtual getter.
        internal bool SharedSelectionDataIsCurrentWithoutCallbacks(ProviderReferenceMapSession session,
            CodeModelGenerator generator, OutputLibrary output)
        {
            if (_sourceDisposed || System.Threading.Volatile.Read(ref _sharedSnapshotStale) != 0 ||
                !ReferenceEquals(CodeModelGenerator.Instance, generator) ||
                !ReferenceEquals(generator.CachedSourceInputModel, _sourceModel) ||
                !ReferenceEquals(output.CachedTypeProviders, _roots) ||
                _shouldWrite != (Func<TypeProvider, bool>)session.ShouldWriteProvider ||
                !SameCachedProviders(_roots, _rootSnapshot) ||
                _sourceInputs is not null && (_sourceModel is null || !ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs))) { return false; }
            foreach (var root in _rootStates)
            {
                if (!session.TryGetCachedWriteDecision(root.Provider.CachedType, out var written)) { return false; }
                if (written && root.Provider is ModelFactoryProvider)
                {
                    if (!ContractCollections.TryCopy(root.Provider.CachedMethods, out var methods)) { return false; }
                    written = !methods.IsEmpty;
                }
                if (written != root.Written) { return false; }
                if (!root.Written) { continue; }
                if (!SameCachedProviders(root.Provider.CachedSerializationProviders, root.Serializations)) { return false; }
                for (int i = 0; i < root.Serializations.Length; i++)
                {
                    if (!session.TryGetCachedWriteDecision(root.Serializations[i].CachedType, out var serializationWritten) ||
                        serializationWritten != root.SerializationWritten[i]) { return false; }
                }
            }
            return true;
        }

        // Supplements successful callbackful checks on the Task05 path. A miss
        // denies field alternatives only; it creates no permission or shared
        // invalidation and never calls a host, builder or write-policy delegate.
        internal bool FieldDataIsCurrentWithoutCallbacks()
        {
            if (System.Threading.Volatile.Read(ref _valid) == 0 || _sourceDisposed ||
                System.Threading.Volatile.Read(ref _sharedSnapshotStale) != 0 ||
                !ReferenceEquals(_sourceConfiguration, CodeModelGenerator.Instance.Configuration) ||
                _sourceInputs is not null && (_sourceModel is null || !ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs)) ||
                !IsPolicyEnabled(CodeModelGenerator.Instance) ||
                !SameCachedProviders(_roots, _rootSnapshot)) { return false; }
            foreach (var state in _providerStates)
            {
                var type = state.Provider.CachedType;
                if (!ReferenceEquals(type, state.Type) || type is null || type.Name != state.Name || type.Namespace != state.Namespace ||
                    !ContractCollections.TryCopy(type.Arguments, out var arguments) || arguments.Length != state.Arity ||
                    !ReferenceEquals(type.DeclaringType, state.DeclaringType) ||
                    !SameCachedProviders(state.Provider.CachedNestedTypes, state.Nested) ||
                    state.Provider.CachedRelativeFilePath != state.RelativeFilePath) { return false; }
            }
            foreach (var root in _rootStates)
            {
                if (root.Written && !SameCachedProviders(root.Provider.CachedSerializationProviders, root.Serializations)) { return false; }
            }
            foreach (var (owner, state) in _owners)
            {
                if (!ReferenceEquals(owner.CachedType, state.Type) || !CachedDeclarationGroupIsCurrent(state.Parts, true) ||
                    !ContractCollections.TryCopy(owner.CachedFields, out var fields) ||
                    !TryCaptureCachedScopeNames(state.Parts, out var names)) { return false; }
                names.UnionWith(state.SourceScopeNames);
                if (!names.All(IsSimpleIdentifier)) { return false; }
                foreach (var (field, candidate) in state.Fields)
                {
                    if (!ReferenceEquals(candidate.FieldType, field.Type) || !ReferenceEquals(field.EnclosingType, owner) ||
                        fields.Count(current => ReferenceEquals(current, field)) != 1 || names.Contains(candidate.FieldType.Name) ||
                        !CachedCandidateIsCurrent(state, candidate)) { return false; }
                }
            }
            return true;
        }

        private static bool SameCachedProviders(IReadOnlyList<TypeProvider>? current, TypeProvider[] expected)
        {
            if (!ContractCollections.TryCopy(current, out var captured) || captured.Length != expected.Length) { return false; }
            for (int i = 0; i < expected.Length; i++)
            {
                if (!ReferenceEquals(captured[i], expected[i])) { return false; }
            }
            return true;
        }

        private static bool CachedCandidateIsCurrent(OwnerState owner, Candidate candidate)
        {
            if (!CachedFieldTypeShape(candidate.FieldType)) { return false; }
            if (candidate.Target is { } target)
            {
                return ReferenceEquals(candidate.FieldType, target.CachedType) && CachedDeclarationGroupIsCurrent(candidate.TargetParts, false);
            }
            return candidate.ReferencedTarget is { } referenced && candidate.FieldType.IdentityVersion == candidate.FieldTypeVersion &&
                candidate.FieldType.Name == referenced.Name && (candidate.CrossNamespace || candidate.FieldType.Namespace == owner.Type.Namespace) &&
                candidate.FieldType.Namespace == referenced.ContainingNamespace.ToDisplayString();
        }

        private static bool CachedFieldTypeShape(CSharpType type)
        {
            // IsArray may call a runtime Type implementation. Only the native CLR
            // implementation is inspected; unknown runtime types retain native text.
            if (type.IsFrameworkType && type.FrameworkType.GetType() != typeof(int).GetType()) { return false; }
            return !type.IsArray && !type.IsNullable && type.DeclaringType is null && !type.IsLiteral && !type.HasUnionMetadata &&
                ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;
        }

        private static bool CachedDeclarationGroupIsCurrent(TypeProvider[] parts, bool simple)
        {
            if (parts.Length == 0 || parts.Any(part => part.CachedType is null || (simple || parts.Length > 1) && !part.HasCachedSimpleFieldTypeScope)) { return false; }
            if (parts.Length == 1) { return true; }
            var first = parts[0].CachedType!;
            if (parts.Any(part => part.CachedDeclarationModifiers is not { } modifiers || !modifiers.HasFlag(TypeSignatureModifiers.Partial) ||
                part.CachedType!.Name != first.Name || part.CachedType.Namespace != first.Namespace)) { return false; }
            return parts.Count(root => parts.All(part => ReferenceEquals(root, part) ||
                ReferenceEquals(part.SerializationProviderOwner, root) && ContractCollections.TryCopy(root.CachedSerializationProviders, out var children) &&
                    children.Any(child => ReferenceEquals(child, part)) ||
                ReferenceEquals(part.PartialDeclarationOwner, root) && !root.PartialDeclarationParts.IsDefault &&
                    root.PartialDeclarationParts.Any(child => ReferenceEquals(child, part)))) == 1;
        }

        private static bool TryCaptureCachedScopeNames(TypeProvider[] parts, out HashSet<string> names)
        {
            names = new(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (!ContractCollections.TryCopy(part.CachedNestedTypes, out var nested) ||
                    !ContractCollections.TryCopy(part.CachedFields, out var fields) ||
                    !ContractCollections.TryCopy(part.CachedProperties, out var properties) ||
                    !ContractCollections.TryCopy(part.CachedMethods, out var methods)) { return false; }
                foreach (var child in nested)
                {
                    if (child?.CachedType is not { } type) { return false; }
                    names.Add(type.Name);
                }
                foreach (var field in fields) { if (field is null) { return false; } names.Add(field.Name); }
                foreach (var property in properties) { if (property is null) { return false; } names.Add(property.Name); }
                foreach (var method in methods) { if (method is null) { return false; } names.Add(method.Signature.Name); }
            }
            return true;
        }
    }
}

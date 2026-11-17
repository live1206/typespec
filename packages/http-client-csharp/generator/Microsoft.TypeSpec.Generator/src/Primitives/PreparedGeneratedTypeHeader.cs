// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>
    /// A bounded standard-writer type header over already finalized caches.
    /// It does not establish a complete generated member group or source universe.
    /// </summary>
    internal sealed class PreparedGeneratedTypeHeader
    {
        internal TypeProvider Owner { get; }
        internal IEnumerable<string> ImportNamespaces => _metadataBase is null ? _documentation.ImportNamespaces : _documentation.ImportNamespaces.Append(_metadataBase.Namespace);
        internal IEnumerable<CSharpType> ImportTypes => _metadataBase is null ? _documentation.ImportTypes : _documentation.ImportTypes.Append(_metadataBase);
        private readonly PreparedSourceFile.OwnerSnapshot _ownerSnapshot;
        private readonly XmlDocProvider _docs;
        private readonly PreparedGeneratedDocumentationEnvelope _documentation;
        private readonly CSharpType? _metadataBase;
        private readonly DeclaredTypeSnapshot? _baseSnapshot;
        private int _stale;

        private PreparedGeneratedTypeHeader(TypeProvider owner, XmlDocProvider docs, PreparedGeneratedDocumentationEnvelope documentation,
            CSharpType? metadataBase = null, DeclaredTypeSnapshot? baseSnapshot = null)
        {
            Owner = owner;
            _ownerSnapshot = new(owner);
            _docs = docs;
            _documentation = documentation;
            _metadataBase = metadataBase;
            _baseSnapshot = baseSnapshot;
        }

        internal static PreparedGeneratedTypeHeader? TryCapture(TypeProvider owner)
        {
            if (!HasKnownHeader(owner) || owner.CachedXmlDocs is not { } docs ||
                PreparedGeneratedDocumentationEnvelope.TryCapture(docs) is not { } documentation) { return null; }
            return new(owner, docs, documentation);
        }

        // A separate constructor-owner profile; ordinary generated-group and
        // import-envelope admission intentionally keep their no-base boundary.
        internal static PreparedGeneratedTypeHeader? TryCaptureMetadataBase(TypeProvider owner)
        {
            if (owner.CachedType?.BaseType is not { } metadataBase || metadataBase.GetType() != typeof(CSharpType) ||
                !metadataBase.IsFrameworkType || !PreparedCallImportScope.HasPureFormattedTypes(new[] { metadataBase }) ||
                !DeclaredTypeSnapshot.TryCapture(metadataBase, out var snapshot) || snapshot is null ||
                !HasKnownHeader(owner, metadataBase) || owner.CachedXmlDocs is not { } docs ||
                PreparedGeneratedDocumentationEnvelope.TryCapture(docs) is not { } documentation) { return null; }
            return new(owner, docs, documentation, metadataBase, snapshot);
        }

        internal bool IsCurrent => IsCurrentCore(invalidate: true);
        internal bool IsCurrentWithoutInvalidating(PocWorkBudget? budget = null) => IsCurrentCore(invalidate: false, budget);

        private bool IsCurrentCore(bool invalidate, PocWorkBudget? budget = null)
        {
            if (System.Threading.Volatile.Read(ref _stale) != 0 || !SourceCurrentnessChecks.Spend(budget)) { return false; }
            if ((budget is null ? _ownerSnapshot.IsCurrent() : _ownerSnapshot.IsCurrentForPocSharedAvailability(budget)) &&
                HasKnownHeader(Owner, _metadataBase, budget, useCapturedDispatch: budget is not null) && SourceCurrentnessChecks.Type(_baseSnapshot, budget) &&
                ReferenceEquals(Owner.CachedXmlDocs, _docs) &&
                (invalidate ? _documentation.IsCurrent : _documentation.IsCurrentWithoutInvalidating(budget))) { return true; }
            if (invalidate) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return false;
        }

        private static bool HasKnownHeader(TypeProvider owner, CSharpType? permittedMetadataBase = null,
            PocWorkBudget? budget = null, bool useCapturedDispatch = false)
        {
            if (owner.CachedType is not { } type || type.GetType() != typeof(CSharpType) || type.IsFrameworkType ||
                type.DeclaringType is not null || !ReferenceEquals(type.BaseType, permittedMetadataBase) || !ReserveHeaderText(type, budget) || !Identifier(type.Name) ||
                type.Namespace.Length == 0 || !type.Namespace.Split('.').All(part => Identifier(part) && SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None) ||
                !SourceCurrentnessChecks.Copy(type.Arguments, budget, out var arguments) || !arguments.IsEmpty ||
                owner.CachedDeclarationModifiers is not { } modifiers || !modifiers.HasFlag(TypeSignatureModifiers.Class)) { return false; }
            const TypeSignatureModifiers allowed = TypeSignatureModifiers.Class | TypeSignatureModifiers.Public | TypeSignatureModifiers.Internal | TypeSignatureModifiers.Partial;
            if ((modifiers & ~allowed) != 0 || (modifiers & (TypeSignatureModifiers.Public | TypeSignatureModifiers.Internal)) ==
                (TypeSignatureModifiers.Public | TypeSignatureModifiers.Internal)) { return false; }
            // An observed path is not a cached value of the intentionally uncached
            // RelativeFilePath getter. This first family requires explicit pinning.
            if (owner.PinnedRelativeFilePath is not { Length: > 0 } path || !SourceCurrentnessChecks.Spend(budget, path.Length) ||
                path.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0 ||
                !owner.IsCustomCodeViewMaterialized || owner.CachedCustomCodeView is not null || owner.SerializationProviderOwner is not null ||
                owner.CachedWhereClause is not null || !SourceCurrentnessChecks.Copy(owner.CachedImplements, budget, out var interfaces) || !interfaces.IsEmpty ||
                !SourceCurrentnessChecks.Copy(owner.CachedWriteAttributes, budget, out var attributes) || !attributes.IsEmpty ||
                !SourceCurrentnessChecks.Copy(owner.CachedDisabledFileWarnings, budget, out var warnings) || !warnings.IsEmpty) { return false; }
            // These null-returning getters are not memoized when they return null.
            // Inspect dispatch metadata rather than calling an override to guess
            // what its next invocation during writing might do.
            // Both private construction paths established these CLR dispatch
            // facts for this same readonly Owner. They are immutable host facts,
            // not a witness for any of the live header inputs checked above.
            if (useCapturedDispatch) { return true; }
            return DefaultMethod(owner, "BuildWhereClause") && DefaultMethod(owner, "BuildDeclaringTypeProvider") &&
                DefaultMethod(owner, "get_ShouldWriteTypeXmlDocs") && DefaultMethod(owner, "get_ShouldAnalyzeAttributesInReferenceMap");
        }

        private static bool ReserveHeaderText(CSharpType type, PocWorkBudget? budget) =>
            // Name scanning; namespace split scanning, substring storage,
            // segment-vector slots (at most length + 1), identifier scans and
            // keyword probes. Reserve each variable pass before splitting.
            budget is null || (SourceCurrentnessChecks.Spend(budget, type.Name.Length) &&
            SourceCurrentnessChecks.Spend(budget, type.Namespace.Length) &&
            SourceCurrentnessChecks.Spend(budget, type.Namespace.Length) &&
            SourceCurrentnessChecks.Spend(budget, type.Namespace.Length) &&
            SourceCurrentnessChecks.Spend(budget, type.Namespace.Length) &&
            SourceCurrentnessChecks.Spend(budget, type.Namespace.Length) && SourceCurrentnessChecks.Spend(budget));

        private static bool DefaultMethod(TypeProvider owner, string name) =>
            owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)?.DeclaringType == typeof(TypeProvider);

        private static bool Identifier(string value) => value.Length > 0 && (value[0] == '_' || char.IsAsciiLetter(value[0])) &&
            value.All(c => c == '_' || char.IsAsciiLetterOrDigit(c));
    }
}

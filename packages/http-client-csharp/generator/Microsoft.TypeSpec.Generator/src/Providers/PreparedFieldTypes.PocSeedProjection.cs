// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Isolated audited native property projection; not production activation.
using System;
using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        private readonly Dictionary<PropertyProvider, PocSeedProjection> _pocSeeds = new(ReferenceEqualityComparer.Instance);

        private sealed class PocSeedProjection
        {
            private readonly PropertyProvider _property;
            private readonly TypeProvider _owner;
            private readonly CSharpType _type;
            private readonly DeclaredTypeSnapshot _typeSnapshot;
            private readonly ExpressionPropertyBody _body;
            private readonly KeywordExpression _getter;
            private readonly long _getterSource, _getterDeclaration;
            private readonly IReadOnlyList<AttributeStatement> _attributes;
            private readonly IReadOnlyList<SuppressionStatement> _suppressions;

            private PocSeedProjection(PropertyProvider property, TypeProvider owner, DeclaredTypeSnapshot typeSnapshot,
                ExpressionPropertyBody body, KeywordExpression getter)
            {
                _property = property; _owner = owner; _type = property.Type; _typeSnapshot = typeSnapshot;
                _body = body; _getter = getter; _getterSource = getter.SourceRevision; _getterDeclaration = getter.DeclarationRevision;
                _attributes = property.Attributes; _suppressions = property.Suppressions;
            }

            internal static PocSeedProjection? TryCapture(PropertyProvider property, TypeProvider owner)
            {
                // Every consumed native WriteProperty input in this exact branch
                // is either represented below or closed by the outer file envelope
                // (disabled docs, audited empty formatter/suppression map).
                if (property.GetType() != typeof(PropertyProvider) || !ReferenceEquals(property.EnclosingType, owner) ||
                    property.Name != "Seed" || property.Modifiers != MethodSignatureModifiers.Public || property.IsRef ||
                    property.ExplicitInterface is not null || property.InputProperty is not null ||
                    !PocType(property.Type, typeof(DateTimeOffset)) ||
                    !DeclaredTypeSnapshot.TryCapture(property.Type, out var typeSnapshot) ||
                    !ContractCollections.TryCopy(property.Attributes, out var attributes) || !attributes.IsEmpty ||
                    !ContractCollections.TryCopy(property.Suppressions, out var suppressions) || !suppressions.IsEmpty ||
                    property.Body is not ExpressionPropertyBody body || body.GetType() != typeof(ExpressionPropertyBody) || body.Setter is not null ||
                    body.Getter is not KeywordExpression getter || getter.GetType() != typeof(KeywordExpression) ||
                    getter.Keyword != "default" || getter.Expression is not null) { return null; }
                return new(property, owner, typeSnapshot!, body, getter);
            }

            internal bool IsCurrent => ReferenceEquals(_property.EnclosingType, _owner) && _property.Name == "Seed" &&
                _property.Modifiers == MethodSignatureModifiers.Public && !_property.IsRef && _property.ExplicitInterface is null &&
                _property.InputProperty is null && ReferenceEquals(_property.Type, _type) && _typeSnapshot.IsCurrent() &&
                ReferenceEquals(_property.Attributes, _attributes) && ContractCollections.TryCopy(_attributes, out var attributes) && attributes.IsEmpty &&
                ReferenceEquals(_property.Suppressions, _suppressions) && ContractCollections.TryCopy(_suppressions, out var suppressions) && suppressions.IsEmpty &&
                ReferenceEquals(_property.Body, _body) && ReferenceEquals(_body.Getter, _getter) && _body.Setter is null &&
                _getter.SourceRevision == _getterSource && _getter.DeclarationRevision == _getterDeclaration &&
                _getter.Keyword == "default" && _getter.Expression is null;
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Expressions;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Immutable output evidence only. Occurrence/slot IDs are caller-owned labels,
    // not compiler/source/epoch proof, and this value grants no spelling authority.
    internal sealed record PocNativeTypeCaptureUse(
        SourceOccurrenceId Occurrence, int Slot, DeclarationExpression Declaration, CSharpType Type,
        bool IsDeclaration, bool WriteTypeNameOnly, int GenericDepth,
        PocNativeTypeFacts Observed, PocNativeTypeFacts TrueFacts,
        string? RegistrationNamespace, string? EmittedNamespace, string? EmittedName,
        int PrimaryStart, int PrimaryLength, int QualifiedStart, int QualifiedLength,
        bool QualifiedAliasesPrimary, int NativeOrder);
}

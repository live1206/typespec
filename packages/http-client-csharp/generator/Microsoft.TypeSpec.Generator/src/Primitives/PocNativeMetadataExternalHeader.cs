// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // EXTERNAL headers only. These historical integers are not binding facts,
    // metadata leases, Task03 permissions, or candidate/writer activation.
    internal readonly record struct PocNativeMetadataExternalHeader(
        int MetadataLength, int TypeDef, int TypeRef, int ExportedType, int AssemblyRef,
        int NestedClass, int GenericParam, int CustomAttribute, int MethodDef, int Field,
        int MemberRef, int TypeSpec, int MethodSpec);
}

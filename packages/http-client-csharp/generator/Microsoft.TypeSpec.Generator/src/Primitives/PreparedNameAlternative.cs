// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal enum PreparedNameChannel
    {
        Field,
        Body,
        Poc
    }

    // Owned spelling plus a numeric range, never a span into pooled writer buffers.
    // Offset is payload-relative during capture and file-relative after rendering.
    // This is output evidence only, not a name-binding or source-edit permission.
    internal readonly record struct PreparedNameAlternative(
        int QualifiedOffset, int QualifiedLength, string ShortSpelling, PreparedNameChannel Channel);

    // Published together by the writer's existing offset-transfer pass.
    // This is output metadata, not permission to select the POC channel.
    internal readonly record struct PreparedNameAlternativesSnapshot(
        System.Collections.Immutable.ImmutableArray<PreparedNameAlternative> Alternatives, bool HasPoc);
}

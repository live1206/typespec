// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Mechanical, owned output coordinates, NOT compiler/source evidence or a permission.
    // Owner and slot IDs have meaning only within this explicitly supplied file table.
    // Coordinates are file-relative (caller adds the actual native header exactly once).
    // NativeOrder is the dense index in the completed, native-order handoff array.
    internal readonly record struct PocFrameAlternative(
        object Owner, int SlotId, int NativeOrder,
        int PrimaryOffset, int PrimaryLength, int QualifiedOffset, int QualifiedLength,
        string ShortSpelling, bool Completed);
}

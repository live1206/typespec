// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection.Metadata;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Copied extents and source-backed reference sizing, NOT valid rows, names,
    // strings, namespace/binding facts, permissions, counts or image leases.
    internal readonly struct PocNativeMetadataLayout
    {
        internal MetadataKind Kind { get; }
        internal int ModuleRowSize { get; }
        // Module = ushort + one string reference + three GUID references.
        // Both reference widths are 2/4 in the pinned schema. No heap-size guess;
        // zero means unknown (default/pure extent helper), not a usable width.
        internal int StringReferenceWidth => ModuleRowSize switch { 10 or 16 => 2, 12 or 18 => 4, _ => 0 };
        internal int TypeDefRowSize { get; }
        internal int TypeDefTableOffset { get; }
        internal int StringHeapOffset { get; }
        internal int StringHeapSize { get; }

        private PocNativeMetadataLayout(MetadataKind kind, int moduleRowSize, int rowSize, int tableOffset, int stringOffset, int stringSize)
        {
            Kind = kind;
            ModuleRowSize = moduleRowSize;
            TypeDefRowSize = rowSize;
            TypeDefTableOffset = tableOffset;
            StringHeapOffset = stringOffset;
            StringHeapSize = stringSize;
        }

        // Pure extent checks intentionally make no Module/schema observation.
        internal static bool TryCreate(int metadataLength, int rowCount, MetadataKind kind,
            int rowSize, int tableOffset, int stringOffset, int stringSize, out PocNativeMetadataLayout layout) =>
            TryCreateCore(metadataLength, rowCount, kind, 0, rowSize, tableOffset, stringOffset, stringSize, out layout);

        internal static bool TryCreateWithModule(int metadataLength, int rowCount, MetadataKind kind, int moduleRowSize,
            int rowSize, int tableOffset, int stringOffset, int stringSize, out PocNativeMetadataLayout layout)
        {
            layout = default;
            // Live capture requires one of the four source-backed Module sizes.
            // Unsupported shape rejects conditionally, before any publication.
            if (moduleRowSize != 10 && moduleRowSize != 12 && moduleRowSize != 16 && moduleRowSize != 18) { return false; }
            return TryCreateCore(metadataLength, rowCount, kind, moduleRowSize, rowSize, tableOffset, stringOffset, stringSize, out layout);
        }

        private static bool TryCreateCore(int metadataLength, int rowCount, MetadataKind kind, int moduleRowSize,
            int rowSize, int tableOffset, int stringOffset, int stringSize, out PocNativeMetadataLayout layout)
        {
            layout = default;
            // Widen before arithmetic; two nonnegative ints multiply within long.
            // Explicit offset<=length guards precede subtraction. No checked cast,
            // pointer dereference, row read or exception path validates this data.
            long length = metadataLength;
            long rows = rowCount;
            long size = rowSize;
            long table = tableOffset;
            long strings = stringOffset;
            long heapSize = stringSize;
            if (kind != MetadataKind.Ecma335 || length < 0 || table < 0 || rows < 0 || size <= 0 || table > length ||
                rows * size > length - table || strings < 0 || heapSize < 0 || strings > length || heapSize > length - strings)
            { return false; }
            // Narrow profile: absent/empty heaps may have a default/null block.
            // Reject them conservatively, without interpreting a pointer-derived
            // offset or inferring the encoded string-reference width from size.
            if (heapSize == 0) { return false; }
            layout = new PocNativeMetadataLayout(kind, moduleRowSize, rowSize, tableOffset, stringOffset, stringSize);
            return true;
        }
    }
}

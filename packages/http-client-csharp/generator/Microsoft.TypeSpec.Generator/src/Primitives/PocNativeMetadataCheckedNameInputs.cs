// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Historical raw prefix observations only. NOT compiler-visible members,
    // exact names, binder negatives, accessibility or writer permissions.
    internal readonly struct PocNativeMetadataCheckedNameInputs
    {
        internal int RowsVisited { get; }
        internal int PossibleNamePrefixes { get; }
        internal int PossibleNameAndNamespacePrefixes { get; }
        internal int AmbiguousRows { get; }

        internal PocNativeMetadataCheckedNameInputs(int rows, int names, int namespaces, int ambiguous)
        {
            RowsVisited = rows;
            PossibleNamePrefixes = names;
            PossibleNameAndNamespacePrefixes = namespaces;
            AmbiguousRows = ambiguous;
        }
    }
}

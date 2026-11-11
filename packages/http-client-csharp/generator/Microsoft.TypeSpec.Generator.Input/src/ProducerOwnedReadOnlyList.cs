// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Microsoft.TypeSpec.Generator.Input
{
    // Constructed explicitly by built-in producers, never inferred from an
    // arbitrary read-only wrapper or a construction witness. The same array is
    // passed to the public BCL wrapper and retained for bounded live readback.
    // Read-only is not immutable: changes through the original array are live.
    internal sealed class ProducerOwnedReadOnlyList<T> : ReadOnlyCollection<T>
    {
        internal ProducerOwnedReadOnlyList(T[] items) : base(items)
        {
            CurrentItems = items;
        }

        internal T[] CurrentItems { get; }
    }
}

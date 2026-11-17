// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.TypeSpec.Generator.Input;

namespace Microsoft.TypeSpec.Generator.Tests.Common
{
    // Use the same explicit producer construction as the native ClientModel
    // sites. No caller-supplied (wrapper, backing-array) provenance registration.
    public static class ArgumentCarrierFactory
    {
        public static IReadOnlyList<T> Create<T>(T[] items) => new ProducerOwnedReadOnlyList<T>(items);
    }
}

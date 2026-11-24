// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // The delimiters of default(T) are required, not operand grouping.
    internal sealed record TypedDefaultExpression(CSharpType Type) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer) => writer.Append($"default({Type})");
    }
}

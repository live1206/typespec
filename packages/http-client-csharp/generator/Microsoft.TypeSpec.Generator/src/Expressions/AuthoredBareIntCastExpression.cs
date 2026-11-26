// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // Owns the required type delimiters, not an optional expression pair.
    internal sealed record AuthoredBareIntCastExpression(VariableExpression Operand) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            writer.AppendRaw("(int)");
            Operand.Write(writer);
        }
    }
}

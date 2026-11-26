// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // These delimiters belong to checked, not to the operand's grouping.
    internal sealed record CheckedExpression(ValueExpression Operand) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            writer.AppendRaw("checked(");
            Operand.Write(writer);
            writer.AppendRaw(")");
        }
    }
}

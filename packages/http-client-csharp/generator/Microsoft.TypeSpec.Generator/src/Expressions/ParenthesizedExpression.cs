// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // An authored expression pair, distinct from any delimiters the operand owns.
    internal sealed record ParenthesizedExpression(ValueExpression Operand) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            bool omit = writer.TryBeginPocGrouping(this);
            if (omit) { writer.AppendPocGroupingDelimiter('('); writer.PocAfterGroupingOpening(); }
            else { writer.AppendRaw("("); }
            Operand.Write(writer);
            if (omit) { writer.AppendPocGroupingDelimiter(')'); }
            else { writer.AppendRaw(")"); }
        }
    }
}

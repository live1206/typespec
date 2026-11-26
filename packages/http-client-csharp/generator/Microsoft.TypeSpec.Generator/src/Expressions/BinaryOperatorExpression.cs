// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    public sealed record BinaryOperatorExpression(string Operator, ValueExpression Left, ValueExpression Right, bool AddParentheses = true) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            bool omit = AddParentheses && writer.TryBeginPocGrouping(this);
            if (omit) { writer.AppendPocGroupingDelimiter('('); writer.PocAfterGroupingOpening(); }
            else { writer.AppendRawIf("(", AddParentheses); }
            Left.Write(writer);
            writer.AppendRawIf(" ", !Left.IsEmptyExpression());
            writer.AppendRaw(Operator);
            writer.AppendRawIf(" ", !Right.IsEmptyExpression());
            Right.Write(writer);
            if (omit) { writer.AppendPocGroupingDelimiter(')'); }
            else { writer.AppendRawIf(")", AddParentheses); }
        }
    }
}

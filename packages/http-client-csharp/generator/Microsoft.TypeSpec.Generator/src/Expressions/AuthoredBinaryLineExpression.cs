// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.TypeSpec.Generator.Expressions
{
    internal sealed record AuthoredBinaryLineExpression : ValueExpression
    {
        public BinaryOperatorExpression Operand { get; }
        internal bool CrLf { get; }
        internal string After { get; }

        internal AuthoredBinaryLineExpression(BinaryOperatorExpression operand, bool crLf, string after)
        {
            if (operand is not { Operator: "+", AddParentheses: false, Left: VariableExpression left, Right: VariableExpression right } ||
                left.GetType() != typeof(VariableExpression) || right.GetType() != typeof(VariableExpression))
            { throw new ArgumentException("Expected the native addition of two variable references", nameof(operand)); }
            Operand = operand;
            CrLf = crLf;
            After = AuthoredBinaryTriviaExpression.BlockComment(after);
        }

        internal override void WriteCore(CodeWriter writer)
        {
            string ending = CrLf ? "\r\n" : "\n";
            bool omit = writer.TryBeginPocGrouping(this);
            if (omit) { writer.AppendPocGroupingDelimiter('('); writer.PocAfterGroupingOpening(); }
            else { writer.AppendRaw("("); }
            writer.AppendRaw(ending);
            writer.WriteAuthoredBinaryAtColumnZero(Operand);
            writer.AppendAuthoredBinaryTail(ending, After, omit);
        }
    }
}

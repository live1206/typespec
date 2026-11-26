// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    internal sealed record AuthoredCompactCastLineExpression : ValueExpression
    {
        public AuthoredBareIntCastExpression Operand { get; }
        internal bool CrLf { get; }
        internal string After { get; }

        internal AuthoredCompactCastLineExpression(AuthoredBareIntCastExpression operand, bool crLf, char after)
        {
            Operand = operand;
            CrLf = crLf;
            After = AuthoredCompactCastTriviaExpression.BlockComment(after);
        }

        internal override void WriteCore(CodeWriter writer)
        {
            string ending = CrLf ? "\r\n" : "\n";
            writer.AppendRaw("(" + ending);
            writer.WriteAuthoredIntCastAtColumnZero(Operand);
            writer.AppendRaw(ending + ")" + After);
        }
    }
}

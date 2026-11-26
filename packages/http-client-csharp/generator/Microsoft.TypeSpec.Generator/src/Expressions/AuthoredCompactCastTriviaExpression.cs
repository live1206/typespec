// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.TypeSpec.Generator.Expressions
{
    internal sealed record AuthoredCompactCastTriviaExpression : ValueExpression
    {
        public AuthoredCompactCastLineExpression Operand { get; }
        internal string Before { get; }

        internal AuthoredCompactCastTriviaExpression(AuthoredCompactCastLineExpression operand, char before)
        {
            Operand = operand;
            Before = BlockComment(before);
        }

        // This native profile only authors single-letter block comments.
        internal static string BlockComment(char content) => char.IsAsciiLetter(content)
            ? "/*" + content + "*/" : throw new ArgumentException("Expected a single ASCII letter.", nameof(content));

        internal override void WriteCore(CodeWriter writer)
        {
            bool omit = writer.TryBeginPocGrouping(this);
            if (omit) { writer.AppendPocGroupingDelimiter('('); writer.PocAfterGroupingOpening(); }
            else { writer.AppendRaw("("); }
            writer.AppendRaw(Before);
            Operand.Write(writer);
            if (omit) { writer.AppendPocGroupingDelimiter(')'); }
            else { writer.AppendRaw(")"); }
        }
    }
}

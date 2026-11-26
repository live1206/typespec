// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // Opt-in authored trivia, not a way to infer trivia from emitted or raw source.
    internal sealed record AuthoredBinaryTriviaExpression : ValueExpression
    {
        public AuthoredBinaryLineExpression Operand { get; }
        internal string Before { get; }

        internal AuthoredBinaryTriviaExpression(AuthoredBinaryLineExpression operand, string before)
        {
            Operand = operand;
            Before = BlockComment(before);
        }

        internal static string BlockComment(string content)
        {
            ArgumentNullException.ThrowIfNull(content);
            // Single-line ASCII bodies only. The delimiters and spaces below
            // are authored by this node, never supplied as raw C# fragments.
            foreach (char c in content)
            {
                if (c is < ' ' or > '~' or '*') { throw new ArgumentException("Unsupported comment body", nameof(content)); }
            }
            return " /* " + content + " */ ";
        }

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

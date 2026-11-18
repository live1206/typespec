// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Expressions
{
    /// <summary>
    /// Represents a cast expression.
    /// </summary>
    /// <param name="Inner">The inner value expression that is being casted.</param>
    /// <param name="Type">The type to cast to.</param>
    public sealed record CastExpression(ValueExpression Inner, CSharpType Type) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            // Retain receiver/operand barriers unless this exact source occurrence
            // has a prepared direct-root choice. The native type and child writes
            // are unchanged; the qualified companion keeps the outer pair.
            bool omit = writer.TryBeginPocGrouping(this);
            if (omit) { writer.AppendPocGroupingDelimiter('('); writer.PocAfterGroupingOpening(); }
            else { writer.AppendRaw("("); }
            writer.Append($"({Type})");
            Inner.Write(writer);
            if (omit) { writer.AppendPocGroupingDelimiter(')'); }
            else { writer.AppendRaw(")"); }
        }
    }
}

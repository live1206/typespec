// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // The target owns its own grouping. These delimiters belong to the call.
    internal sealed record DirectDelegateInvocationExpression(ParenthesizedExpression Target, ValueExpression Argument) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            Target.Write(writer);
            writer.AppendRaw("(");
            Argument.Write(writer);
            writer.AppendRaw(")");
        }
    }
}

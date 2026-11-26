// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // The cast writes the target grouping; this node owns only the empty argument list.
    internal sealed record ParameterlessCastDelegateInvocationExpression(CastExpression Target) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            Target.Write(writer);
            writer.AppendRaw("()");
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    internal sealed record AuthoredCompactLocalAdditionExpression(VariableExpression Local, InvokeMethodExpression Call) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            Local.Write(writer);
            writer.AppendRaw("+");
            Call.Write(writer);
        }
    }
}

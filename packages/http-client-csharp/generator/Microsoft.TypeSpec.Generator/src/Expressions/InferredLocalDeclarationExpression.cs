// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Expressions
{
    // Opt-in source spelling. The carrier keeps its real type and declaration;
    // writing `var` does not establish that the compiler will infer that type.
    internal sealed record InferredLocalDeclarationExpression(VariableExpression Variable) : ValueExpression
    {
        internal override void WriteCore(CodeWriter writer)
        {
            writer.AppendRaw("var ");
            Variable.Write(writer);
        }
    }
}

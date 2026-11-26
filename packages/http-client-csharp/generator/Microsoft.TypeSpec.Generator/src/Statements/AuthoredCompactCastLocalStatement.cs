// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.TypeSpec.Generator.Expressions;

namespace Microsoft.TypeSpec.Generator.Statements
{
    // The two statements share one authored line boundary after the initializer.
    internal sealed class AuthoredCompactCastLocalStatement(InferredLocalDeclarationExpression declaration,
        AuthoredCompactCastTriviaExpression initializer, AuthoredCompactLocalAdditionExpression result) : MethodBodyStatement
    {
        public InferredLocalDeclarationExpression Declaration { get; } = declaration;
        public AuthoredCompactCastTriviaExpression Initializer { get; } = initializer;
        public KeywordExpression Return { get; } = new("return", result);

        internal override void WriteCore(CodeWriter writer)
        {
            Declaration.Write(writer);
            writer.AppendRaw("=");
            Initializer.Write(writer);
            writer.AppendRaw(";");
            Return.Write(writer);
            writer.WriteRawLine(";");
        }
    }
}

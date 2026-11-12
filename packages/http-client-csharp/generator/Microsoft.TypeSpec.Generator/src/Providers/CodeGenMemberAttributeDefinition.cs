// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;
using static Microsoft.TypeSpec.Generator.Snippets.Snippet;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal class CodeGenMemberAttributeDefinition : CustomCodeAttributeDefinition
    {
        protected override string BuildRelativeFilePath() => Path.Combine("src", "Generated", "Internal", $"{Name}.cs");

        protected override string BuildName() => "CodeGenMemberAttribute";

        protected override string BuildNamespace() => CodeModelGenerator.CustomizationAttributeNamespace;

        protected override TypeSignatureModifiers BuildDeclarationModifiers() =>
            TypeSignatureModifiers.Internal | TypeSignatureModifiers.Class;

        protected internal override CSharpType[] BuildImplements() => [new CodeGenTypeAttributeDefinition().Type];

        protected override IReadOnlyList<AttributeStatement> BuildAttributes()
        {
            AttributeStatement attribute;
            return ConstructedCollectionWitness.Record<AttributeStatement>(
                [attribute = new AttributeStatement(typeof(AttributeUsageAttribute),
                new BinaryOperatorExpression(
                    "|",
                    FrameworkEnumValue(AttributeTargets.Property),
                    FrameworkEnumValue(AttributeTargets.Field)))], attribute);
        }
        protected internal override ConstructorProvider[] BuildConstructors()
        {
            var parameter = new ParameterProvider("originalName", $"The original name of the member.", typeof(string));
            ValueExpression argument;

            return
            [
                new ConstructorProvider(
                    new ConstructorSignature(
                        Type,
                        null,
                        MethodSignatureModifiers.Public,
                        [parameter],
                        initializer: new ConstructorInitializer(IsBase: true,
                            ConstructedCollectionWitness.Record<ValueExpression>([argument = parameter], argument))),
                    MethodBodyStatement.Empty,
                this)
            ];
        }
    }
}

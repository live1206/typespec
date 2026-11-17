// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Primitives;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal class PocNativeWriterStorageTests
    {
        // Captured from the unchanged baseline assembly's PE field definitions.
        private static readonly string[] BaselineInstanceFields =
        [
            "_activeWarnings", "_atBeginningOfLine", "_builder", "_currentNamespace", "_hasWrittenPayload",
            "_nameAlternatives", "_pocBoundConsumption", "_pocDeclarationConsumption", "_pocFileConsumption",
            "_pocSupplementalCursor", "_pocTypeConstraint", "_preparedFieldTypes", "_preparedFileOwner",
            "_qualifiedBuilder", "_qualifiedPayloadLength", "_scopes", "_sourceCursor", "_sourceFile",
            "_sourcePreparation", "_standardSourceWriter", "_usingNamespaces", "_writingNewInstance",
            "_writingXmlDocumentation", "<NameAlternativesSnapshot>k__BackingField", "<QualifiedContent>k__BackingField"
        ];

        [Test]
        public void CaptureOffRetainsOnlyBaselineNativeCarrierSlots()
        {
            var budget = new PocWorkBudget(0);
            var writers = Enumerable.Range(0, 4).Select(_ => new CodeWriter()).ToArray();
            try
            {
                var fields = typeof(CodeWriter).GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Assert.That(fields.Select(field => field.Name), Is.EquivalentTo(BaselineInstanceFields),
                    "Optional capture must not add unpaid storage to every native writer allocation.");
                Assert.That(fields.Single(field => field.Name == "_nameAlternatives").FieldType.IsValueType, Is.False,
                    "Reusing the existing reference slot must not grow its native storage width.");
                foreach (var writer in writers)
                {
                    writer.AppendRaw("native");
                    Assert.That(writer.ToString(false), Is.EqualTo("native"));
                }
                Assert.That(budget.Remaining, Is.Zero);
            }
            finally
            {
                foreach (var writer in writers) { writer.Dispose(); }
            }
        }
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Providers
{
    public class PreparedPocNameFormsTests
    {
        [Test]
        public void OrdinaryFormsKeepThePocShortestFirstOrder()
        {
            var forms = PreparedPocNameForms.Rank(4, ImmutableArray<PreparedPocNameForms.Alias>.Empty);
            Assert.That(forms.Select(form => (form.Root, form.Start, form.Cost)).ToArray(), Is.EqualTo(new[]
            {
                (PreparedPocNameForms.RootKind.Ordinary, 3, 1),
                (PreparedPocNameForms.RootKind.Ordinary, 2, 2),
                (PreparedPocNameForms.RootKind.Ordinary, 1, 3),
                (PreparedPocNameForms.RootKind.Ordinary, 0, 4),
                (PreparedPocNameForms.RootKind.Global, 0, 5)
            }));
        }

        [TestCase("class", "@class")]
        [TestCase("file", "@file")]
        [TestCase("PipelineMessage", "PipelineMessage")]
        [TestCase("not-valid", null)]
        public void IdentifierRenderingKeepsThePocKeywordEscaping(string identifier, string? expected)
        {
            Assert.That(PreparedPocNameForms.RenderIdentifier(identifier), Is.EqualTo(expected));
        }

        [Test]
        public void AliasesKeepThePocCostAndTieBreakOrder()
        {
            var aliases = ImmutableArray.Create(
                new PreparedPocNameForms.Alias("Z", NamespaceAlias: true, Preferred: false),
                new PreparedPocNameForms.Alias("A", NamespaceAlias: false, Preferred: true));
            var forms = PreparedPocNameForms.Rank(4, aliases);
            Assert.That(forms.Take(5).Select(form => (form.Root, form.Start, form.AliasName, form.Cost)).ToArray(), Is.EqualTo(new[]
            {
                (PreparedPocNameForms.RootKind.Alias, 4, "A", 1),
                (PreparedPocNameForms.RootKind.Ordinary, 3, (string?)null, 1),
                (PreparedPocNameForms.RootKind.Alias, 3, "A", 2),
                (PreparedPocNameForms.RootKind.Ordinary, 2, (string?)null, 2),
                (PreparedPocNameForms.RootKind.Alias, 3, "Z", 2)
            }));
            Assert.That(forms.Any(form => form.AliasName == "Z" && form.Start == 4), Is.False,
                "A namespace alias alone does not denote the terminal type.");
        }
    }
}

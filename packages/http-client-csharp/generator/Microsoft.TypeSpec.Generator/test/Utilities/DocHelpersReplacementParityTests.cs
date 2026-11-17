// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.TypeSpec.Generator.Utilities;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Utilities
{
    [NonParallelizable]
    public class DocHelpersReplacementParityTests
    {
        // Independent legacy oracle: retain the original patterns, replacement
        // strings and pass order, including for inputs outside POC admission.
        private static readonly Regex LegacyTriple = new(@"\*\*\*([^*]+?)\*\*\*", RegexOptions.Compiled);
        private static readonly Regex LegacyBold = new(@"\*\*([^*]+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex LegacyItalic = new(@"(?<!\*)\*([^*]+?)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Func<string, string> Inline =
            typeof(DocHelpers).GetMethod("ConvertInlineMarkdown", BindingFlags.NonPublic | BindingFlags.Static)!
                .CreateDelegate<Func<string, string>>();

        private static string Legacy(string text) =>
            LegacyItalic.Replace(LegacyBold.Replace(LegacyTriple.Replace(text, "<b><i>$1</i></b>"), "<b>$1</b>"), "<i>$1</i>");

        private static void Check(string text)
        {
            var expected = Legacy(text);
            var actual = Inline(text);
            if (actual != expected)
            {
                Assert.That(actual, Is.EqualTo(expected), $"Legacy inline replacement parity for {text}");
            }
        }

        [Test]
        public void InlineReplacementMatchesLegacyForOverlapsUnicodeAndLiteralReplacementSyntax()
        {
            string[] samples =
            [
                "", "plain", "*", "**", "***", "****", "*one* and *two*", "**one** **two**",
                "***one*** ***two***", "****a****", "******a******", "***a**b*", "**a*b**", "*a**b*",
                "*a***b*", "***a* b**", "*** ***", "** **", "* *", "**a**b**c**", "***a***b***c***",
                "*\n*", "**\r\n**", "***\u0085\u2028\u2029***", "***😀***", "**x\u2028y**", "\ud800*x*\udfff",
                "literal $1 ${name} $$ $& $` $' *payload $1*", "<b>*text & <tag>*</b>", "***{0}:$1***",
                "*\tword\t*", "***leading*** trailing *one* **two**", "\r\n***line***\r\n", "**a**\n**b**"
            ];
            foreach (var sample in samples) { Check(sample); }
            var unchanged = new string('x', 300);
            Assert.That(Inline(unchanged), Is.SameAs(unchanged), "No-match conversion must retain the original string.");
        }

        [Test]
        public void InlineReplacementMatchesLegacyForExhaustiveShortInputs()
        {
            char[] alphabet = ['*', 'a', ' ', '$', '\n'];
            int examined = 0;
            for (int length = 0; length <= 7; length++)
            {
                int count = 1;
                for (int index = 0; index < length; index++) { count *= alphabet.Length; }
                var buffer = new char[length];
                for (int value = 0; value < count; value++)
                {
                    int digits = value;
                    for (int index = 0; index < length; index++)
                    {
                        buffer[index] = alphabet[digits % alphabet.Length];
                        digits /= alphabet.Length;
                    }
                    Check(new string(buffer));
                    examined++;
                }
            }
            Assert.That(examined, Is.EqualTo(97656));
        }
    }
}

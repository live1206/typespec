// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Microsoft.TypeSpec.Generator.Input;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Count-only admission for the audited DocHelpers.GetDescription path. This
    // neither converts Markdown nor constructs/normalizes a documentation tree.
    internal static class PreparedPocColdDescription
    {
        internal static bool TryReserve(PocHeaderFinalizationBridge.ColdDescriptionInputs inputs,
            bool cold, PocWorkBudget phase) => TryReserveWithCulture(inputs, cold, phase, out _);

        internal static bool IsCultureCurrent(CultureInfo? culture) =>
            culture is null || ReferenceEquals(CultureInfo.CurrentCulture, culture);

        internal static bool TryReserveWithCulture(PocHeaderFinalizationBridge.ColdDescriptionInputs inputs,
            bool cold, PocWorkBudget phase, out CultureInfo? culture)
        {
            culture = null;
            int summaryLength = inputs.Summary?.Length ?? 0;
            int docLength = inputs.Doc?.Length ?? 0;
            int fallbackLength = inputs.FallbackName?.Length ?? 0;
            if (summaryLength > 4096 || docLength > 4096 || fallbackLength > 4096 || inputs.FallbackCharacters < 0) { return false; }
            // Pay both this validation and the later exact-audit validation. The
            // non-selected input is still scanned, but is never converted.
            if (!phase.TrySpend(2 * (summaryLength + docLength + fallbackLength)) ||
                !PocHeaderFinalizationBridge.SafeDescriptionText(inputs.Summary) ||
                !PocHeaderFinalizationBridge.SafeDescriptionText(inputs.Doc) ||
                !PocHeaderFinalizationBridge.SafeDescriptionText(inputs.FallbackName)) { return false; }
            if (!cold) { return true; }

            // Native GetDescription selects a nonempty Doc, otherwise Summary.
            var selected = docLength != 0 ? inputs.Doc : inputs.Summary;
            int length = selected?.Length ?? 0;
            long upper;
            long lines;
            int normalizationFactor = 12;
            int replacements = 0;
            long runnerStorage = 0;
            if (length == 0)
            {
                upper = inputs.FallbackCharacters;
                lines = 1;
                if (inputs.FallbackName is { } fallback)
                {
                    foreach (var character in fallback)
                    {
                        if (character is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029') { lines++; }
                        if (character is '&' or '>') { replacements++; }
                    }
                }
            }
            else
            {
                if (!phase.TrySpend(length) || !phase.TrySpend(2 * length + 1)) { return false; }
                // Approved narrow lifecycle exception: known BCL culture/
                // collation initialization may happen here. Its startup/cache
                // work is not claimed to fit the input-work budget. Fund the
                // bounded comparisons first and never dispatch a custom culture.
                culture = CultureInfo.CurrentCulture;
                if (culture.GetType() != typeof(CultureInfo)) { return false; }
                var comparer = culture.CompareInfo;
                if (comparer.GetType() != typeof(CompareInfo)) { return false; }
                int items = 0;
                int groups = 0;
                int listKind = 0;
                int emphasized = 0;
                int nativeLines = 0;
                int otherBreaks = 0;
                bool noXmlRewrite = true;
                var text = selected!.AsSpan();
                int start = 0;
                for (int end = 0; end <= text.Length; end++)
                {
                    if (end < text.Length)
                    {
                        if (text[end] is '\u0085' or '\u2028' or '\u2029') { otherBreaks++; noXmlRewrite = false; }
                        if (text[end] is '&' or '>') { noXmlRewrite = false; replacements++; }
                    }
                    if (end < text.Length && text[end] is not ('\r' or '\n')) { continue; }
                    var line = text[start..end];
                    nativeLines++;
                    if (!CountLine(line, comparer, ref items, ref groups, ref listKind, ref emphasized)) { return false; }
                    if (end + 1 < text.Length && text[end] == '\r' && text[end + 1] == '\n') { end++; }
                    start = end + 1;
                }
                // Consecutive items of the same kind form one native group,
                // not one group/line per item. Native splitting removes at
                // least L-1 delimiters and each item removes >=2 prefix chars.
                // Generated markup is <=40/item, 27/group, 8/emphasis pair;
                // platform newline insertion contributes at most 2/output line.
                lines = nativeLines - items + groups + otherBreaks;
                upper = length - (nativeLines - 1L) - 2L * items +
                    40L * items + 27L * groups + 8L * emphasized + 2 * lines;
                // Additional cold runner/match/capture storage, independent of
                // shared-regex cache reuse, for compiled or interpreter paths.
                // Include completion MoveNext after each possible successful
                // emphasis match. This is a storage subledger, not a proof of
                // matching, interpreter initialization or total native work.
                runnerStorage = 671L * nativeLines + 273L * emphasized + 184L * items + 2L * length;
                if (items > 0 && noXmlRewrite)
                {
                    // Raw '<' was excluded. All remaining tags are complete
                    // allowed helper tags; EscapeLine has no replacements and
                    // returns each original string. Six passes cover the
                    // requires-normalization scan, optional CRLF count/fill,
                    // LF split, substring copies and EscapeLine on every OS.
                    normalizationFactor = 6;
                }
            }

            // FromString uses one string hole. BreakLines retains at most one
            // such argument per output line; EscapeLine expands a character by
            // at most six. Native summary start/end and the root cost <128;
            // 48/line covers copies, formats, args and both ancestor probes.
            // The general 12/character bound covers escaping. The no-rewrite
            // list profile above needs six passes and retains string identities
            // during EscapeLine; its final envelope captures <=upper chars.
            long admission = 128 + normalizationFactor * upper + 48 * lines;
            if (admission > 4096) { return false; }

            // Restricted star runs below leave at most two regex candidates per
            // inline pass. Splitting, trimming, numbered-list scans, the three
            // replacements and their bounded copies have fixed linear passes.
            // The caller already reserved the final 4096-unit file envelope;
            // reserve normalization separately here, before any getter.
            long converter = length == 0 ? 0 : 32L * (length + upper + lines + 1);
            // EscapeLine inserts at most one position per raw '&' or '>'.
            // Its dictionary has no removals. Fund every possible insertion
            // chain separately; helper-generated complete tags add none. A
            // global triangle also bounds the sum across all output lines.
            long replacementProbes = replacements * (replacements + 1L) / 2;
            long work = converter + runnerStorage + 4096 + 1 + replacementProbes; // includes the final input recheck
            return work <= int.MaxValue && phase.TrySpend((int)work);
        }

        private static bool CountLine(ReadOnlySpan<char> line, CompareInfo comparer,
            ref int items, ref int groups, ref int listKind, ref int emphasized)
        {
            int first = 0;
            int kind = 0;
            while (first < line.Length && char.IsWhiteSpace(line[first])) { first++; }
            // Match native string.StartsWith(string), including ignorable
            // characters. Native then removes TWO UTF-16 units, not the
            // linguistic match length; decline its short-input exception path.
            if (comparer.IsPrefix(line[first..], "- ".AsSpan(), CompareOptions.None))
            {
                if (line.Length - first < 2) { return false; }
                kind = 1;
            }
            else
            {
                int digit = first;
                while (digit < line.Length && char.IsDigit(line[digit])) { digit++; }
                if (digit > first && digit + 1 < line.Length && line[digit] == '.' && char.IsWhiteSpace(line[digit + 1])) { kind = 2; }
            }
            if (kind != 0)
            {
                items++;
                if (kind != listKind) { groups++; }
            }
            listKind = kind;
            int runs = 0;
            int width = 0;
            int firstEnd = 0;
            for (int index = 0; index < line.Length; index++)
            {
                if (line[index] != '*') { continue; }
                int begin = index;
                while (index < line.Length && line[index] == '*') { index++; }
                int count = index - begin;
                runs++;
                if (count > 3 || runs > 2 || runs == 2 && (width != count || begin == firstEnd)) { return false; }
                if (runs == 1) { width = count; firstEnd = index; }
                index--;
            }
            if (runs == 1) { return false; }
            if (runs == 2) { emphasized++; }
            return true;
        }
    }
}

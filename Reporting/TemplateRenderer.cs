// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Token + row-repeat substitution against an external HTML template.
//
// Two primitives:
//   {{snake_case}}                      scalar substitution; throws if missing
//   <tag data-repeat="key">...</tag>    the element itself is the row template;
//                                       the renderer emits one copy per row in
//                                       repeats[key] (with the data-repeat attr
//                                       stripped), each with row-scoped tokens.
//
// data-repeat sits on the row element itself (rather than a wrapper whose first
// child is the row template) because (a) it composes uniformly with
// single-element repeats like <th data-repeat="..."> inside a header row, and
// (b) nested repeats are a single recursive call.
//
// Nested repeats are supported: a row of an outer repeat may carry its own
// per-row repeat dictionary in RepeatRow.Repeats. The inner row template is
// rendered with the row-scoped tokens (falling back to the outer scope) and
// the row-scoped sub-repeats.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ReRT.Reporting
{
    /// <summary>
    /// One row in a repeat block. Tokens are scalar substitutions scoped to
    /// this row; Repeats carry per-row sub-repeat data for nested data-repeat
    /// elements that appear inside the row template.
    /// </summary>
    public sealed class RepeatRow
    {
        public IReadOnlyDictionary<string, string> Tokens { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> Repeats { get; }

        public RepeatRow(
            IReadOnlyDictionary<string, string> tokens,
            IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> repeats = null)
        {
            Tokens = tokens ?? new Dictionary<string, string>();
            Repeats = repeats ?? EmptyRepeats;
        }

        public static readonly IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> EmptyRepeats
            = new Dictionary<string, IReadOnlyList<RepeatRow>>();
    }

    public sealed class TemplateRenderer
    {
        private static readonly Regex TokenRegex =
            new Regex(@"\{\{\s*([a-z_][a-z0-9_]*)\s*\}\}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // HTML comments are documentation — they may legitimately reference
        // {{tokens}} by name. Strip them before substitution so the renderer
        // doesn't try to resolve documentation as data.
        private static readonly Regex HtmlCommentRegex =
            new Regex(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

        // Matches the opening tag of any element carrying data-repeat="..."
        // (single or double quotes). Captures the tag name and the repeat key.
        private static readonly Regex RepeatOpenRegex =
            new Regex(@"<(?<tag>[a-zA-Z][a-zA-Z0-9]*)\b(?<attrs>[^>]*?)\bdata-repeat\s*=\s*[""'](?<key>[a-z_][a-z0-9_]*)[""'](?<rest>[^>]*)>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Convenience overload accepting the simple flat shape (no nested
        /// sub-repeats). Each inner dictionary is treated as a row with no
        /// sub-repeats.
        /// </summary>
        public string Render(
            string templateHtml,
            IReadOnlyDictionary<string, string> tokens,
            IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> repeats)
        {
            var rich = new Dictionary<string, IReadOnlyList<RepeatRow>>(StringComparer.Ordinal);
            if (repeats != null)
            {
                foreach (var kv in repeats)
                {
                    var rows = (IReadOnlyList<RepeatRow>)kv.Value
                        .Select(d => new RepeatRow(d))
                        .ToList();
                    rich[kv.Key] = rows;
                }
            }
            return Render(templateHtml, tokens, rich);
        }

        public string Render(
            string templateHtml,
            IReadOnlyDictionary<string, string> tokens,
            IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> repeats)
        {
            if (templateHtml == null) throw new ArgumentNullException(nameof(templateHtml));
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (repeats == null) repeats = RepeatRow.EmptyRepeats;

            string stripped = HtmlCommentRegex.Replace(templateHtml, string.Empty);
            string expanded = ExpandRepeats(stripped, tokens, repeats);
            return SubstituteScalarTokens(expanded, tokens);
        }

        private string ExpandRepeats(
            string html,
            IReadOnlyDictionary<string, string> outerTokens,
            IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> outerRepeats)
        {
            var sb = new StringBuilder();
            int cursor = 0;
            while (cursor < html.Length)
            {
                Match m = RepeatOpenRegex.Match(html, cursor);
                if (!m.Success)
                {
                    sb.Append(html, cursor, html.Length - cursor);
                    break;
                }

                sb.Append(html, cursor, m.Index - cursor);

                string tag = m.Groups["tag"].Value;
                string key = m.Groups["key"].Value;

                // Locate the matching close tag, accounting for nested
                // same-tag elements opened between here and there.
                int contentStart = m.Index + m.Length;
                int closeStart = FindMatchingClose(html, contentStart, tag);
                if (closeStart < 0)
                    throw new InvalidOperationException(
                        $"Template error: unclosed <{tag} data-repeat=\"{key}\"> element.");
                int closeEnd = closeStart + ("</" + tag + ">").Length;

                // Build the row template: same opening tag with data-repeat
                // stripped, original inner content, original closing tag.
                string strippedOpen = StripDataRepeatAttr(m.Value);
                string innerContent = html.Substring(contentStart, closeStart - contentStart);
                string rowTemplate = strippedOpen + innerContent + html.Substring(closeStart, closeEnd - closeStart);

                if (!outerRepeats.TryGetValue(key, out var rows))
                    throw new InvalidOperationException(
                        $"Template error: data-repeat=\"{key}\" has no entry in the repeats dictionary.");

                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        // Row-scoped tokens fall back to the outer scope so
                        // surrounding markup constants resolve consistently.
                        var rowTokens = MergeTokens(outerTokens, row.Tokens);
                        var rowRepeats = MergeRepeats(outerRepeats, row.Repeats);

                        // Recurse: the row template may itself contain nested
                        // data-repeat elements driven by row.Repeats.
                        string rendered = ExpandRepeats(rowTemplate, rowTokens, rowRepeats);
                        // Resolve scalar tokens with the *row* scope before appending.
                        // The final top-level pass only sees outer-scope content, so
                        // row-only tokens (course_id, plan_id, …) must be substituted
                        // here.
                        rendered = SubstituteScalarTokens(rendered, rowTokens);
                        sb.Append(rendered);
                    }
                }

                cursor = closeEnd;
            }
            return sb.ToString();
        }

        private static int FindMatchingClose(string html, int start, string tag)
        {
            string closeTag = "</" + tag + ">";
            int depth = 1;
            int i = start;
            while (i < html.Length)
            {
                int nextClose = IndexOfIgnoreCase(html, closeTag, i);
                if (nextClose < 0) return -1;

                int nextOpen = FindNextSameTagOpen(html, i, nextClose, tag);
                if (nextOpen >= 0)
                {
                    depth++;
                    i = nextOpen + ("<" + tag).Length;
                    continue;
                }
                depth--;
                if (depth == 0) return nextClose;
                i = nextClose + closeTag.Length;
            }
            return -1;
        }

        // Returns the index of the next opening tag '<tag ' or '<tag>' for
        // the same tag name in [start, before). Returns -1 if none.
        private static int FindNextSameTagOpen(string html, int start, int before, string tag)
        {
            int from = start;
            while (from < before)
            {
                int candidate = IndexOfIgnoreCase(html, "<" + tag, from);
                if (candidate < 0 || candidate >= before) return -1;
                int after = candidate + 1 + tag.Length;
                if (after >= html.Length) return -1;
                char nextChar = html[after];
                if (nextChar == ' ' || nextChar == '\t' || nextChar == '\r' ||
                    nextChar == '\n' || nextChar == '>' || nextChar == '/')
                    return candidate;
                from = candidate + 1; // false hit (e.g. <table> when tag=tab)
            }
            return -1;
        }

        private static int IndexOfIgnoreCase(string haystack, string needle, int start)
            => haystack.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);

        private static string StripDataRepeatAttr(string openTag)
        {
            // Remove just the data-repeat="..." attribute (with surrounding
            // whitespace) without touching the rest of the open tag.
            return Regex.Replace(openTag,
                @"\s+data-repeat\s*=\s*[""'][^""']*[""']",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        private string SubstituteScalarTokens(
            string html,
            IReadOnlyDictionary<string, string> tokens)
        {
            return TokenRegex.Replace(html, m =>
            {
                string name = m.Groups[1].Value;
                if (!tokens.TryGetValue(name, out var value))
                    throw new InvalidOperationException(
                        $"Template error: token '{{{{ {name} }}}}' has no value. " +
                        "Add it to the tokens dictionary or remove it from the template.");
                return value ?? string.Empty;
            });
        }

        private static IReadOnlyDictionary<string, string> MergeTokens(
            IReadOnlyDictionary<string, string> outer,
            IReadOnlyDictionary<string, string> inner)
        {
            if (inner == null || inner.Count == 0) return outer;
            var merged = new Dictionary<string, string>(outer.Count + inner.Count);
            foreach (var kv in outer) merged[kv.Key] = kv.Value;
            foreach (var kv in inner) merged[kv.Key] = kv.Value;
            return merged;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> MergeRepeats(
            IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> outer,
            IReadOnlyDictionary<string, IReadOnlyList<RepeatRow>> inner)
        {
            if (inner == null || inner.Count == 0) return outer;
            var merged = new Dictionary<string, IReadOnlyList<RepeatRow>>(outer.Count + inner.Count);
            foreach (var kv in outer) merged[kv.Key] = kv.Value;
            foreach (var kv in inner) merged[kv.Key] = kv.Value;
            return merged;
        }

        // Number formatting helper used by sections to keep snapshots stable
        // across cultures.
        public static string Round(double value, int digits)
            => Math.Round(value, digits).ToString(CultureInfo.InvariantCulture);
    }
}

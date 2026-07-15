// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FluentAssertions;
using ReRT.Reporting;
using Xunit;

namespace ReRT.Tests.Reporting
{
    public class TemplateRendererTests
    {
        private readonly TemplateRenderer _r = new TemplateRenderer();

        private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            NoRepeats => new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>();

        // ---- Scalar tokens ----------------------------------------------------

        [Fact]
        public void Render_ScalarToken_Substitutes()
        {
            var html = "<p>Hello {{name}}!</p>";
            var tokens = new Dictionary<string, string> { ["name"] = "world" };

            var output = _r.Render(html, tokens, NoRepeats);

            output.Should().Be("<p>Hello world!</p>");
        }

        [Fact]
        public void Render_ScalarToken_AllowsWhitespaceInsideBraces()
        {
            var html = "<p>{{ name }}</p>";
            var tokens = new Dictionary<string, string> { ["name"] = "x" };

            _r.Render(html, tokens, NoRepeats).Should().Be("<p>x</p>");
        }

        [Fact]
        public void Render_MissingToken_ThrowsWithTokenName()
        {
            var html = "<p>{{nope}}</p>";
            var tokens = new Dictionary<string, string> { ["something_else"] = "x" };

            Action act = () => _r.Render(html, tokens, NoRepeats);

            act.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
        }

        [Fact]
        public void Render_NullTokenValue_TreatedAsEmpty()
        {
            var html = "<p>before{{x}}after</p>";
            var tokens = new Dictionary<string, string> { ["x"] = null };

            _r.Render(html, tokens, NoRepeats).Should().Be("<p>beforeafter</p>");
        }

        // ---- Row repeats ------------------------------------------------------

        [Fact]
        public void Render_RowRepeat_EmitsOneCopyPerRow()
        {
            var html = @"<table><tr data-repeat=""rows""><td>{{a}}</td></tr></table>";
            var tokens = new Dictionary<string, string>();
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["a"] = "1" },
                    new Dictionary<string, string> { ["a"] = "2" },
                    new Dictionary<string, string> { ["a"] = "3" },
                }
            };

            var output = _r.Render(html, tokens, repeats);

            output.Should().Be("<table><tr><td>1</td></tr><tr><td>2</td></tr><tr><td>3</td></tr></table>");
        }

        [Fact]
        public void Render_RowRepeat_EmptyDataset_RemovesRowTemplate()
        {
            var html = @"<table><tr data-repeat=""rows""><td>{{a}}</td></tr></table>";
            var tokens = new Dictionary<string, string>();
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>(),
            };

            _r.Render(html, tokens, repeats).Should().Be("<table></table>");
        }

        [Fact]
        public void Render_RowRepeat_WithScalarsInSurroundingMarkup_DoesNotInterfere()
        {
            var html = @"<h1>{{title}}</h1><table><tr data-repeat=""rows""><td>{{a}}</td></tr></table><p>{{footer}}</p>";
            var tokens = new Dictionary<string, string>
            {
                ["title"] = "T",
                ["footer"] = "F",
            };
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["a"] = "x" },
                }
            };

            _r.Render(html, tokens, repeats)
                .Should().Be("<h1>T</h1><table><tr><td>x</td></tr></table><p>F</p>");
        }

        [Fact]
        public void Render_RowRepeat_RowTokensFallBackToOuterScope()
        {
            var html = @"<tr data-repeat=""rows""><td>{{a}}</td><td>{{shared}}</td></tr>";
            var tokens = new Dictionary<string, string> { ["shared"] = "S" };
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["a"] = "1" },
                    new Dictionary<string, string> { ["a"] = "2" },
                }
            };

            _r.Render(html, tokens, repeats)
                .Should().Be("<tr><td>1</td><td>S</td></tr><tr><td>2</td><td>S</td></tr>");
        }

        // ---- Nested repeats ---------------------------------------------------

        [Fact]
        public void Render_NestedRepeats_ExpandsBothLevels()
        {
            var html =
@"<div data-repeat=""courses"">
  <h2>{{course_id}}</h2>
  <ul><li data-repeat=""plans"">{{plan_id}}</li></ul>
</div>";
            var tokens = new Dictionary<string, string>();
            var repeats = new Dictionary<string, IReadOnlyList<RepeatRow>>
            {
                ["courses"] = new List<RepeatRow>
                {
                    new RepeatRow(
                        new Dictionary<string, string> { ["course_id"] = "C1" },
                        new Dictionary<string, IReadOnlyList<RepeatRow>>
                        {
                            ["plans"] = new List<RepeatRow>
                            {
                                new RepeatRow(new Dictionary<string, string> { ["plan_id"] = "P1A" }),
                                new RepeatRow(new Dictionary<string, string> { ["plan_id"] = "P1B" }),
                            }
                        }),
                    new RepeatRow(
                        new Dictionary<string, string> { ["course_id"] = "C2" },
                        new Dictionary<string, IReadOnlyList<RepeatRow>>
                        {
                            ["plans"] = new List<RepeatRow>
                            {
                                new RepeatRow(new Dictionary<string, string> { ["plan_id"] = "P2A" }),
                            }
                        }),
                }
            };

            var output = _r.Render(html, tokens, repeats);

            output.Should().Contain("<h2>C1</h2>");
            output.Should().Contain("<li>P1A</li><li>P1B</li>");
            output.Should().Contain("<h2>C2</h2>");
            output.Should().Contain("<li>P2A</li>");
            // The outer-scope keys should not leak between courses.
            output.Should().NotContain("{{course_id}}");
            output.Should().NotContain("{{plan_id}}");
        }

        [Fact]
        public void Render_RepeatKeyMissingFromDictionary_Throws()
        {
            var html = @"<tr data-repeat=""rows""><td>{{a}}</td></tr>";

            Action act = () => _r.Render(
                html,
                new Dictionary<string, string>(),
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>());

            act.Should().Throw<InvalidOperationException>().WithMessage("*rows*");
        }

        [Fact]
        public void Render_NestedSameTag_BalancesProperly()
        {
            // An outer <div data-repeat> wraps content that itself contains
            // another <div> (no data-repeat). The matcher must skip past the
            // inner closing </div> and pair with the outer one.
            var html = @"<div data-repeat=""rows""><div class=""inner"">{{a}}</div></div>";
            var tokens = new Dictionary<string, string>();
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["a"] = "1" },
                    new Dictionary<string, string> { ["a"] = "2" },
                }
            };

            _r.Render(html, tokens, repeats)
                .Should().Be(@"<div><div class=""inner"">1</div></div><div><div class=""inner"">2</div></div>");
        }

        [Fact]
        public void Render_DataRepeatAttributeIsStrippedFromOutput()
        {
            var html = @"<tr class=""x"" data-repeat=""rows"" id=""y""><td>{{a}}</td></tr>";
            var tokens = new Dictionary<string, string>();
            var repeats = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["rows"] = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["a"] = "x" },
                }
            };

            var output = _r.Render(html, tokens, repeats);

            output.Should().NotContain("data-repeat");
            output.Should().Contain(@"class=""x""");
            output.Should().Contain(@"id=""y""");
        }

        // ---- Static Round helper ----------------------------------------------

        [Theory]
        // Round is a thin wrapper over Math.Round + ToString(Invariant).
        // We only need to pin down: (a) it returns a string, (b) it uses
        // Invariant decimal separator (covered by the next test), and
        // (c) it actually rounds. Avoid halfway values whose binary
        // representation makes banker's-rounding behaviour brittle.
        [InlineData(12.34, 2, "12.34")]
        [InlineData(0.0,   2, "0")]
        [InlineData(-3.7,  1, "-3.7")]
        [InlineData(100.0, 0, "100")]
        [InlineData(1.6,   0, "2")]
        public void Round_FormatsWithInvariantCulture(double input, int digits, string expected)
        {
            TemplateRenderer.Round(input, digits).Should().Be(expected);
        }

        [Fact]
        public void Round_UsesInvariantCulture_NotCurrentCulture()
        {
            // Lock the current thread to a comma-decimal locale (de-DE) and
            // verify Round still emits a dot. Reports are written as
            // HTML/Word with InvariantCulture; if Round drifted to the
            // current locale, section snapshots would diverge per-host.
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                TemplateRenderer.Round(3.14, 2).Should().Be("3.14");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }
}

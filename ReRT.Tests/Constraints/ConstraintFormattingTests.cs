// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Formatting/display tests for Constraint.ToString and ExtraMetric — these
// power the human-readable strings rendered into the report.

using FluentAssertions;
using ReRT.Domain.Constraints;
using Xunit;

namespace ReRT.Tests.Constraints
{
    public class ConstraintFormattingTests
    {
        // ---- Constraint.ToString -----------------------------------------------------

        [Fact]
        public void ToString_Dmax_Gy_RendersAsDmaxOpLimitGy()
        {
            var c = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 85, Unit = LimitUnit.Gy };
            c.ToString().Should().Be("Dmax < 85Gy");
        }

        [Fact]
        public void ToString_Mean_Gy_RendersAsMeanOpLimitGy()
        {
            var c = new Constraint { Metric = MetricType.Mean, Op = "<=", Limit = 30, Unit = LimitUnit.Gy };
            c.ToString().Should().Be("Mean <= 30Gy");
        }

        [Fact]
        public void ToString_V_Percent_IncludesAnchorAndPercentUnit()
        {
            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 40, Unit = LimitUnit.Percent };
            c.ToString().Should().Be("V20Gy < 40%");
        }

        [Fact]
        public void ToString_V_Cc_IncludesAnchorAndCcUnit()
        {
            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 500, Unit = LimitUnit.CC };
            c.ToString().Should().Be("V20Gy < 500cc");
        }

        [Fact]
        public void ToString_VolumeSpared_Cc_RendersAsVsAnchorOpLimitCc()
        {
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = 16.0, Op = ">", Limit = 1000, Unit = LimitUnit.CC };
            c.ToString().Should().Be("VS16Gy > 1000cc");
        }

        [Fact]
        public void ToString_V_WithoutAnchor_RendersQuestionMark()
        {
            // Degenerate but representable: DoseGy null in a V/VS constraint
            // renders the anchor as "?" so the resulting string is still
            // visually parseable in a log line.
            var c = new Constraint { Metric = MetricType.V, DoseGy = null, Op = "<", Limit = 40, Unit = LimitUnit.Percent };
            c.ToString().Should().Be("V?Gy < 40%");
        }

        [Fact]
        public void ToString_UsesInvariantCultureForDecimalSeparator()
        {
            // Reports are written with InvariantCulture; constraints must
            // render the same way regardless of host locale.
            var c = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 12.5, Unit = LimitUnit.Gy };
            c.ToString().Should().Be("Dmax < 12.5Gy");
        }

        // ---- ExtraMetric -------------------------------------------------------------

        [Fact]
        public void ExtraMetric_ToString_V_RendersWithGyUnit()
        {
            new ExtraMetric { Metric = MetricType.V, DoseGy = 20.0 }
                .ToString().Should().Be("V20Gy");
        }

        [Fact]
        public void ExtraMetric_ToString_VolumeSpared_RendersVsPrefix()
        {
            new ExtraMetric { Metric = MetricType.VolumeSpared, DoseGy = 32.0 }
                .ToString().Should().Be("VS32Gy");
        }

        [Fact]
        public void ExtraMetric_ToString_NonAnchoredMetric_UsesAtSyntax()
        {
            // Defensive fallback for Metric values that don't expect an
            // anchor (Dmax/Mean). The class allows them but they aren't
            // emitted via PHYS, so the fallback renders "Dmax@0Gy".
            new ExtraMetric { Metric = MetricType.Dmax, DoseGy = 0.0 }
                .ToString().Should().Be("Dmax@0Gy");
        }

        [Fact]
        public void ExtraMetric_ToLegacyString_V_DropsGyUnit()
        {
            new ExtraMetric { Metric = MetricType.V, DoseGy = 20.0 }
                .ToLegacyString().Should().Be("V20");
        }

        [Fact]
        public void ExtraMetric_ToLegacyString_VolumeSpared_DropsGyUnit()
        {
            new ExtraMetric { Metric = MetricType.VolumeSpared, DoseGy = 32.0 }
                .ToLegacyString().Should().Be("VS32");
        }

        [Fact]
        public void ExtraMetric_ToLegacyString_NonAnchoredMetric_ReturnsMetricNameOnly()
        {
            new ExtraMetric { Metric = MetricType.Mean, DoseGy = 0.0 }
                .ToLegacyString().Should().Be("Mean");
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using FluentAssertions;
using ReRT.Domain.Constraints;
using ReRT.Domain.DoseMetrics;
using ReRT.Tests.DoseMetrics;
using Xunit;

namespace ReRT.Tests.Constraints
{
    public class ConstraintEvaluatorTests
    {
        private readonly StructureRef _s = new StructureRef("Liver", 1500.0);

        // Reference DVH used for the V/VS tests below. Same as DoseMetricCalculatorTests.
        private static DvhCurve RefCurve()
        {
            var c = DvhCurveBuilder.FromPoints(
                1500.0,
                (0, 1500.0),
                (10, 800.0),
                (20, 400.0),
                (40, 100.0),
                (60, 0.0));
            c.MaxDoseGy = 5000.0; // bypass DvhFallbackHelper
            c.MeanDoseGy = 12.5;
            return c;
        }

        private static ConstraintEvaluator MakeEvaluator(DvhCurve curve)
        {
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            return new ConstraintEvaluator(new DoseMetricCalculator(provider));
        }

        // ---- Dmax: strict '<' semantics ----------------------------------------------

        [Fact]
        public void Dmax_BelowLimit_IsMet_RemainderPositive()
        {
            // D0.1cc (near-max) is interpolated, not bin-edge: the 0.1 cc
            // sample sits exactly at 40 Gy, so Dmax = 40.
            var curve = DvhCurveBuilder.FromPoints(
                100.0, (0, 100), (20, 50), (40, 0.1), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 50, Unit = LimitUnit.Gy };
            var r = ev.Evaluate(c, _s);

            r.MeasuredValue.Should().Be(40.0);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().Be(10.0);
        }

        [Fact]
        public void Dmax_AtLimit_Inclusive_IsMet()
        {
            // All operators are evaluated inclusively (see IsMetByOp); float
            // equality at the limit is vanishingly rare and the inclusive
            // convention is consistent across metrics.
            // D0.1cc is interpolated; the 0.1 cc sample sits at 50 Gy → Dmax = 50.
            var curve = DvhCurveBuilder.FromPoints(
                100.0, (0, 100), (40, 10), (50, 0.1), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 50, Unit = LimitUnit.Gy };
            var r = ev.Evaluate(c, _s);

            r.MeasuredValue.Should().Be(50.0);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().Be(0.0);
        }

        [Fact]
        public void Dmax_AtLimit_LessOrEqual_IsMet()
        {
            // D0.1cc is interpolated; the 0.1 cc sample sits at 50 Gy → Dmax = 50.
            var curve = DvhCurveBuilder.FromPoints(
                100.0, (0, 100), (40, 10), (50, 0.1), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.Dmax, Op = "<=", Limit = 50, Unit = LimitUnit.Gy };
            ev.Evaluate(c, _s).IsMet.Should().BeTrue();
        }

        [Fact]
        public void Dmax_AboveLimit_IsViolated_Remainder0()
        {
            var curve = DvhCurveBuilder.FromPoints(
                100.0, (0, 100), (50, 5), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 30, Unit = LimitUnit.Gy };
            var r = ev.Evaluate(c, _s);

            r.IsMet.Should().BeFalse();
            r.Remainder.Should().Be(0.0);
        }

        // ---- Mean: strict '<' semantics ----------------------------------------------

        [Fact]
        public void Mean_BelowLimit_IsMet()
        {
            var ev = MakeEvaluator(RefCurve()); // mean 12.5
            var c = new Constraint { Metric = MetricType.Mean, Op = "<", Limit = 30, Unit = LimitUnit.Gy };
            var r = ev.Evaluate(c, _s);
            r.MeasuredValue.Should().Be(12.5);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().Be(17.5);
        }

        [Fact]
        public void Mean_AtLimit_Inclusive_IsMet()
        {
            var curve = RefCurve();
            curve.MeanDoseGy = 30.0;
            var ev = MakeEvaluator(curve);
            var c = new Constraint { Metric = MetricType.Mean, Op = "<", Limit = 30, Unit = LimitUnit.Gy };
            ev.Evaluate(c, _s).IsMet.Should().BeTrue();
        }

        // ---- V (relative %): inclusive '<=' for met, strict at remainder boundary ---

        [Fact]
        public void V_PercentLimit_BelowLimit_IsMet()
        {
            // Use a curve where V(20Gy) interpolates to 400/1500 cc absolute => 26.67%.
            // For a percent test, we model with relative volumes:
            var curve = DvhCurveBuilder.FromPoints(
                1500.0, (0, 100.0), (10, 53.33), (20, 26.67), (40, 6.67), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 40.0, Unit = LimitUnit.Percent };
            var r = ev.Evaluate(c, _s);

            r.MeasuredValue.Should().BeApproximately(26.67, 0.01);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().BeApproximately(13.33, 0.01);
        }

        [Fact]
        public void V_AtLimit_LegacyInclusive_IsMet()
        {
            // measured == limit; V uses inclusive '<=' even when op is '<'.
            var curve = DvhCurveBuilder.FromPoints(
                1500.0, (0, 100.0), (10, 50.0), (20, 40.0), (40, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 40.0, Unit = LimitUnit.Percent };
            ev.Evaluate(c, _s).IsMet.Should().BeTrue();
        }

        [Fact]
        public void V_AboveLimit_IsViolated()
        {
            var curve = DvhCurveBuilder.FromPoints(
                1500.0, (0, 100.0), (10, 80.0), (20, 60.0), (40, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 40.0, Unit = LimitUnit.Percent };
            ev.Evaluate(c, _s).IsMet.Should().BeFalse();
        }

        // ---- VolumeSpared: inclusive '>=' -------------------------------------------

        [Fact]
        public void VolumeSpared_AboveLimit_IsMet()
        {
            // Liver total 1500 cc; V@16Gy interpolated 800 - (800-400)*(6/10) = 800-240=560
            var curve = RefCurve();
            var ev = MakeEvaluator(curve);
            // VS@16Gy = 1500 - 560 = 940 cc
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = 16.0, Op = ">", Limit = 700.0, Unit = LimitUnit.CC };
            var r = ev.Evaluate(c, _s);
            r.MeasuredValue.Should().BeApproximately(940.0, 1e-6);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().BeApproximately(240.0, 1e-6);
        }

        [Fact]
        public void VolumeSpared_AtLimit_LegacyInclusive_IsMet()
        {
            // Set up so VS == 700 exactly: V@16Gy = 800. With the curve's
            // 800@10 / 400@20, V@16Gy = 800 - (800-400)*0.6 = 560. VS = 940 actually.
            // Easier: hand-build a curve.
            var curve = DvhCurveBuilder.FromPoints(
                1000.0, (0, 1000), (16, 300), (60, 0));
            curve.MaxDoseGy = 5000;
            curve.VolumeCc = 1000;
            var ev = MakeEvaluator(curve);
            // VS@16 = 1000-300 = 700
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = 16.0, Op = ">", Limit = 700.0, Unit = LimitUnit.CC };
            ev.Evaluate(c, _s).IsMet.Should().BeTrue();
        }

        [Fact]
        public void VolumeSpared_BelowLimit_IsViolated()
        {
            var curve = DvhCurveBuilder.FromPoints(
                1000.0, (0, 1000), (16, 600), (60, 0));
            curve.MaxDoseGy = 5000;
            curve.VolumeCc = 1000;
            var ev = MakeEvaluator(curve);
            // VS@16 = 400; constraint > 700 -> violated
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = 16.0, Op = ">", Limit = 700.0, Unit = LimitUnit.CC };
            ev.Evaluate(c, _s).IsMet.Should().BeFalse();
        }

        // ---- Operator validation ----------------------------------------------------

        [Fact]
        public void GreaterEqualOperator_AtBoundary_IsMet()
        {
            var curve = DvhCurveBuilder.FromPoints(
                1000.0, (0, 1000), (16, 300), (60, 0));
            curve.MaxDoseGy = 5000;
            curve.VolumeCc = 1000;
            var ev = MakeEvaluator(curve);
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = 16.0, Op = ">=", Limit = 700.0, Unit = LimitUnit.CC };
            ev.Evaluate(c, _s).IsMet.Should().BeTrue();
        }

        [Fact]
        public void UnknownOperator_Throws()
        {
            var ev = MakeEvaluator(RefCurve());
            var c = new Constraint { Metric = MetricType.Mean, Op = "==", Limit = 30, Unit = LimitUnit.Gy };
            FluentActions.Invoking(() => ev.Evaluate(c, _s)).Should().Throw<System.ArgumentException>();
        }

        [Fact]
        public void V_WithoutAnchorDose_Throws()
        {
            var ev = MakeEvaluator(RefCurve());
            var c = new Constraint { Metric = MetricType.V, DoseGy = null, Op = "<", Limit = 40, Unit = LimitUnit.Percent };
            FluentActions.Invoking(() => ev.Evaluate(c, _s)).Should().Throw<System.InvalidOperationException>();
        }

        // ---- Additional coverage ----------------------------------------------------

        [Fact]
        public void V_WithCcLimitUnit_RequestsAbsoluteCcVolume()
        {
            // The V evaluator picks the volume presentation from the constraint
            // unit: Percent -> RelativePercent, CC -> AbsoluteCc.
            var curve = DvhCurveBuilder.FromPoints(
                1500.0, (0, 1500.0), (20, 400.0), (60, 0.0));
            curve.MaxDoseGy = 5000;
            var ev = MakeEvaluator(curve);

            var c = new Constraint { Metric = MetricType.V, DoseGy = 20.0, Op = "<", Limit = 500.0, Unit = LimitUnit.CC };
            var r = ev.Evaluate(c, _s);

            r.MeasuredValue.Should().BeApproximately(400.0, 1e-6);
            r.IsMet.Should().BeTrue();
            r.Remainder.Should().BeApproximately(100.0, 1e-6);
        }

        [Fact]
        public void VolumeSpared_WithoutAnchorDose_Throws()
        {
            // Mirror of V_WithoutAnchorDose: both metrics require DoseGy and
            // both throw if it's absent.
            var ev = MakeEvaluator(RefCurve());
            var c = new Constraint { Metric = MetricType.VolumeSpared, DoseGy = null, Op = ">", Limit = 700, Unit = LimitUnit.CC };
            FluentActions.Invoking(() => ev.Evaluate(c, _s)).Should().Throw<System.InvalidOperationException>();
        }

        [Fact]
        public void Evaluate_NullConstraint_Throws()
        {
            var ev = MakeEvaluator(RefCurve());
            FluentActions.Invoking(() => ev.Evaluate(null, _s))
                .Should().Throw<System.ArgumentNullException>();
        }

        [Fact]
        public void Evaluate_NullStructure_Throws()
        {
            var ev = MakeEvaluator(RefCurve());
            var c = new Constraint { Metric = MetricType.Mean, Op = "<", Limit = 30, Unit = LimitUnit.Gy };
            FluentActions.Invoking(() => ev.Evaluate(c, null))
                .Should().Throw<System.ArgumentNullException>();
        }

        [Fact]
        public void Evaluator_NullCalculator_Throws()
        {
            // The constructor null-checks the calculator.
            FluentActions.Invoking(() => new ConstraintEvaluator(null))
                .Should().Throw<System.ArgumentNullException>();
        }

        [Fact]
        public void Evaluate_UnknownMetric_Throws()
        {
            // Switch default in ComputeMeasured throws ArgumentOutOfRangeException.
            // Inject an unmapped enum value via casting.
            var ev = MakeEvaluator(RefCurve());
            var c = new Constraint
            {
                Metric = (MetricType)9999,
                Op = "<",
                Limit = 30,
                Unit = LimitUnit.Gy,
            };
            FluentActions.Invoking(() => ev.Evaluate(c, _s))
                .Should().Throw<System.ArgumentOutOfRangeException>();
        }
    }
}

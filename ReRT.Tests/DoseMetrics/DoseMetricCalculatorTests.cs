// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using FluentAssertions;
using ReRT.Domain.DoseMetrics;
using Xunit;

namespace ReRT.Tests.DoseMetrics
{
    public class DoseMetricCalculatorTests
    {
        private readonly StructureRef _s = new StructureRef("Liver", 1500.0);

        // 5-point reference curve, AbsoluteCc volume, ascending by dose.
        // Points: (0 Gy -> 1500 cc), (10 -> 800), (20 -> 400), (40 -> 100), (60 -> 0)
        private static DvhCurve RefCurve() => DvhCurveBuilder.FromPoints(
            1500.0,
            (0, 1500.0),
            (10, 800.0),
            (20, 400.0),
            (40, 100.0),
            (60, 0.0));

        [Fact]
        public void InterpolateVolume_DoseInsideInterval_LinearBlend()
        {
            // Between (10, 800) and (20, 400): at 15 Gy expect 600 cc.
            var curve = RefCurve();
            DoseMetricCalculator.InterpolateVolume(curve, 15.0).Should().BeApproximately(600.0, 1e-9);
        }

        [Fact]
        public void InterpolateVolume_AtKnownPoint_ReturnsExactValue()
        {
            var curve = RefCurve();
            DoseMetricCalculator.InterpolateVolume(curve, 20.0).Should().BeApproximately(400.0, 1e-9);
        }

        [Fact]
        public void InterpolateVolume_DoseAboveCurveMax_ReturnsLastVolume()
        {
            var curve = RefCurve();
            DoseMetricCalculator.InterpolateVolume(curve, 100.0).Should().Be(0.0);
        }

        [Fact]
        public void InterpolateVolume_DoseBelowCurveMin_ReturnsFirstVolume()
        {
            var curve = RefCurve();
            DoseMetricCalculator.InterpolateVolume(curve, -5.0).Should().Be(1500.0);
        }

        [Fact]
        public void InterpolateVolume_NullCurve_ReturnsZero()
        {
            DoseMetricCalculator.InterpolateVolume(null, 10).Should().Be(0.0);
        }

        [Fact]
        public void InterpolateVolume_EmptyPoints_ReturnsZero()
        {
            var c = new DvhCurve { Points = new DvhPoint[0] };
            DoseMetricCalculator.InterpolateVolume(c, 10).Should().Be(0.0);
        }

        [Fact]
        public void InterpolateVolume_SinglePointCurve_ReturnsThatVolume()
        {
            var c = DvhCurveBuilder.FromPoints(100.0, (25, 42.0));
            DoseMetricCalculator.InterpolateVolume(c, 25.0).Should().Be(42.0);
            DoseMetricCalculator.InterpolateVolume(c,  5.0).Should().Be(42.0);
            DoseMetricCalculator.InterpolateVolume(c, 80.0).Should().Be(42.0);
        }

        [Fact]
        public void InterpolateVolume_BinarySearchHitsCorrectBracketDeepInCurve()
        {
            // 6-point curve with a precisely known final bracket (40, 100)→(60, 0).
            // Forces the binary search to descend past several earlier samples
            // before landing on the correct bracket. At 45 Gy:
            //   t = (45-40)/(60-40) = 0.25; v = 100 + 0.25*(0-100) = 75.
            var curve = DvhCurveBuilder.FromPoints(
                100.0,
                (0,  100.0),
                (10, 100.0),
                (20, 100.0),
                (30, 100.0),
                (40, 100.0),
                (60,   0.0));
            DoseMetricCalculator.InterpolateVolume(curve, 45.0).Should().BeApproximately(75.0, 1e-9);
        }

        [Fact]
        public void Dmax_InterpolatesAtThresholdCrossing()
        {
            // Bracket: (60, 0.5) → (70, 0.05). Threshold 0.1 cc.
            //   t = (0.5 - 0.1) / (0.5 - 0.05) = 0.4 / 0.45 = 0.8888...
            //   dose = 60 + 0.8889 * (70 - 60) = 68.8888...
            var curve = DvhCurveBuilder.FromPoints(
                100.0,
                (0, 100.0),
                (20, 50.0),
                (40, 10.0),
                (60, 0.5),
                (70, 0.05),
                (80, 0.0));
            curve.MaxDoseGy = 5000.0; // bypass fallback
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            calc.Dmax(_s).Should().BeApproximately(68.88888888, 1e-6);
        }

        [Fact]
        public void Dmax_AllPointsBelowThreshold_ReturnsZero()
        {
            // Every cumulative-volume sample is at or below 0.1 cc — no
            // meaningful near-max dose.
            var curve = DvhCurveBuilder.FromPoints(
                0.05,
                (0,  0.05),
                (10, 0.02),
                (20, 0.00));
            curve.MaxDoseGy = 5000.0;
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            calc.Dmax(_s).Should().Be(0.0);
        }

        [Fact]
        public void Dmax_AllPointsAboveThreshold_ReturnsHighestDose()
        {
            // No crossing within the sampled range; fall back to the
            // highest sampled dose.
            var curve = DvhCurveBuilder.FromPoints(
                100.0,
                (0,   100.0),
                (20,   80.0),
                (40,   60.0),
                (60,   40.0));
            curve.MaxDoseGy = 5000.0;
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            calc.Dmax(_s).Should().Be(60.0);
        }

        [Fact]
        public void Dmax_EmptyCurve_ReturnsZero()
        {
            // Provider returns an empty curve; Dmax must not throw.
            var provider = new FakeDvhProvider(
                (s, d, v, b) => new DvhCurve { Points = new DvhPoint[0], MaxDoseGy = 5000 });
            var calc = new DoseMetricCalculator(provider);

            calc.Dmax(_s).Should().Be(0.0);
        }

        [Fact]
        public void Mean_DelegatesToCurveMeanDose()
        {
            var curve = RefCurve();
            curve.MeanDoseGy = 17.42;
            curve.MaxDoseGy = 5000;
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            calc.Mean(_s).Should().Be(17.42);
        }

        [Fact]
        public void VolumeAtDose_ReturnsInterpolatedVolume()
        {
            var curve = RefCurve();
            curve.MaxDoseGy = 5000;
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            calc.VolumeAtDose(_s, 15.0, VolumePresentation.AbsoluteCc).Should().BeApproximately(600.0, 1e-9);
        }

        [Fact]
        public void VolumeSparedAtDose_IsTotalMinusIrradiated()
        {
            var curve = RefCurve();
            curve.MaxDoseGy = 5000;
            var provider = new FakeDvhProvider((s, d, v, b) => curve);
            var calc = new DoseMetricCalculator(provider);

            // V@15Gy = 600 cc, structure total = 1500 cc, so VS@15Gy = 900 cc
            calc.VolumeSparedAtDose(_s, 15.0).Should().BeApproximately(900.0, 1e-9);
        }
    }
}

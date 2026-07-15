// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using FluentAssertions;
using ReRT;
using ReRT.Configuration.Loaders;
using Xunit;

namespace ReRT.Tests.Configuration
{
    public class DiscountCalculatorTests
    {
        // Discount 60 @ 6mo, 40 @ 12mo, 20 @ 24mo; beyond = max(50, 20) = 50.
        private static RecoveryCurve Curve() => new RecoveryCurve
        {
            Discount = new List<double> { 60, 40, 20 },
            Timepoint = new List<double> { 6, 12, 24 },
        };

        [Fact]
        public void BeforeFirstEdgeWindow_ReturnsFirstBucket()
        {
            // 3 months <= 6 - 0.5 => first bucket (60)
            DiscountCalculator.Calculate(Curve(), 3).Should().Be(60);
        }

        [Fact]
        public void AtEdge_RampsFullyIntoNextBucket()
        {
            // month == edge (6): t = (6 - 5.5)/0.5 = 1 => Lerp(60, 40, 1) = 40
            DiscountCalculator.Calculate(Curve(), 6).Should().BeApproximately(40, 1e-9);
        }

        [Fact]
        public void MidRamp_InterpolatesLinearly()
        {
            // month 5.75: t = (5.75 - 5.5)/0.5 = 0.5 => Lerp(60, 40, 0.5) = 50
            DiscountCalculator.Calculate(Curve(), 5.75).Should().BeApproximately(50, 1e-9);
        }

        [Fact]
        public void PastLastEdge_ReturnsBeyondValue()
        {
            DiscountCalculator.Calculate(Curve(), 30).Should().Be(50);
        }

        [Fact]
        public void NegativeMonth_ClampedToZero_ReturnsFirstBucket()
        {
            DiscountCalculator.Calculate(Curve(), -4).Should().Be(60);
        }

        [Fact]
        public void NullOrEmptyCurve_ReturnsZero()
        {
            DiscountCalculator.Calculate(null, 5).Should().Be(0);
            DiscountCalculator.Calculate(new RecoveryCurve(), 5).Should().Be(0);
        }

        [Fact]
        public void Interpolation_RoundsToNearestInteger()
        {
            // Ramp 60 -> 40 over months [5.5, 6]. At 5.5625, t = 0.125, so the
            // exact interpolation is 57.5 — it must surface as the whole 58 the
            // GUI shows, not 57.5, so the dose math matches the display.
            DiscountCalculator.Calculate(Curve(), 5.5625).Should().Be(58);
        }

        [Fact]
        public void EveryResult_IsAWholeNumber()
        {
            // No fractional discount may escape the calculator anywhere on the ramp.
            for (double m = 5.5; m <= 6.0; m += 0.01)
                (DiscountCalculator.Calculate(Curve(), m) % 1).Should().Be(0.0);
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using FluentAssertions;
using ReRT.Domain.DoseConversion;
using Xunit;

namespace ReRT.Tests.DoseConversion
{
    public class DoseFormulasTests
    {
        // --- Forward formula --------------------------------------------------

        [Theory]
        // (physGy, αβ, fxCount, expectedEqd2Gy)
        // Hand-computed: physGy * (αβ + physGy/fx) / (αβ + 2)
        // 60 Gy / 30 fx, αβ=2: 60 · (2 + 2) / 4 = 60 → identical, sanity check.
        [InlineData(60.0,  2.0, 30, 60.0)]
        // 60 Gy / 30 fx, αβ=3: 60 · (3 + 2) / 5 = 60 → also identity at 2 Gy/fx, αβ doesn't matter.
        [InlineData(60.0,  3.0, 30, 60.0)]
        // 50 Gy / 5 fx (10 Gy/fx), αβ=10: 50 · (10 + 10) / 12 = 50 · 20 / 12 = 83.333…
        [InlineData(50.0, 10.0,  5, 83.3333333333333)]
        // 50 Gy / 5 fx, αβ=3: 50 · (3 + 10) / 5 = 50 · 13 / 5 = 130
        [InlineData(50.0,  3.0,  5, 130.0)]
        // 24 Gy / 3 fx (8 Gy/fx), αβ=2: 24 · (2 + 8) / 4 = 60
        [InlineData(24.0,  2.0,  3, 60.0)]
        public void PhysicalToEqd2_KnownInputs_MatchesHandComputed(
            double physicalDose, double alphaBeta, int fxCount, double expected)
        {
            var actual = DoseFormulas.PhysicalToEqd2(physicalDose, alphaBeta, fxCount);
            actual.Should().BeApproximately(expected, 1e-9);
        }

        [Fact]
        public void PhysicalToEqd2_ZeroDose_ReturnsZero()
        {
            DoseFormulas.PhysicalToEqd2(0.0, 3.0, 5).Should().Be(0.0);
        }

        [Fact]
        public void PhysicalToEqd2_NegativeDose_ReturnsZero()
        {
            // Clamp: any negative input collapses to 0 rather than
            // propagating a (mathematically valid) negative result.
            DoseFormulas.PhysicalToEqd2(-10.0, 3.0, 5).Should().Be(0.0);
        }

        [Fact]
        public void PhysicalToEqd2_FxCountZero_Throws()
        {
            Action act = () => DoseFormulas.PhysicalToEqd2(60.0, 3.0, 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void PhysicalToEqd2_FxCountNegative_Throws()
        {
            Action act = () => DoseFormulas.PhysicalToEqd2(60.0, 3.0, -1);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(50.0, 3.0, 5, 0.5,  65.0)]   // 130 · 0.5 = 65
        [InlineData(50.0, 3.0, 5, 2.0, 260.0)]   // 130 · 2.0 = 260
        public void PhysicalToEqd2_ScalingMultipliesResult(
            double physicalDose, double alphaBeta, int fxCount, double scaling, double expected)
        {
            DoseFormulas.PhysicalToEqd2(physicalDose, alphaBeta, fxCount, scaling)
                .Should().BeApproximately(expected, 1e-9);
        }

        // --- Inverse formula --------------------------------------------------

        [Theory]
        // Inverses of the known forward cases; the sanity ones (input==output
        // at 2 Gy/fx) round-trip exactly even before the round-trip test.
        [InlineData(60.0,  2.0, 30, 60.0)]
        [InlineData(60.0,  3.0, 30, 60.0)]
        [InlineData(83.3333333333333, 10.0,  5, 50.0)]
        [InlineData(130.0,  3.0,  5, 50.0)]
        [InlineData(60.0,  2.0,  3, 24.0)]
        public void Eqd2ToPhysical_KnownInputs_MatchesHandComputed(
            double eqd2, double alphaBeta, int fxCount, double expected)
        {
            DoseFormulas.Eqd2ToPhysical(eqd2, alphaBeta, fxCount)
                .Should().BeApproximately(expected, 1e-9);
        }

        [Fact]
        public void Eqd2ToPhysical_ZeroEqd2_ReturnsZero()
        {
            DoseFormulas.Eqd2ToPhysical(0.0, 3.0, 5).Should().Be(0.0);
        }

        [Fact]
        public void Eqd2ToPhysical_NegativeEqd2_ReturnsZero()
        {
            DoseFormulas.Eqd2ToPhysical(-5.0, 3.0, 5).Should().Be(0.0);
        }

        [Fact]
        public void Eqd2ToPhysical_FxCountZero_Throws()
        {
            Action act = () => DoseFormulas.Eqd2ToPhysical(60.0, 3.0, 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        // --- Round-trip exhaustive --------------------------------------------

        [Theory]
        [InlineData(0.01)]
        [InlineData(1.0)]
        [InlineData(50.0)]
        [InlineData(200.0)]
        public void RoundTrip_PhysicalToEqd2ToPhysical_PreservesValue(double physGy)
        {
            const double alphaBeta = 3.0;
            const int fxCount = 30;
            double eqd2 = DoseFormulas.PhysicalToEqd2(physGy, alphaBeta, fxCount);
            double back = DoseFormulas.Eqd2ToPhysical(eqd2, alphaBeta, fxCount);
            back.Should().BeApproximately(physGy, 1e-9);
        }

        public static TheoryData<double, double> AlphaBetaFxDoseGrid =>
            // αβ × fxDose grid.
            new TheoryData<double, double>
            {
                {  1.5,  1.8 }, {  1.5,  2.0 }, {  1.5,  5.0 }, {  1.5,  8.0 }, {  1.5, 20.0 },
                {  2.5,  1.8 }, {  2.5,  2.0 }, {  2.5,  5.0 }, {  2.5,  8.0 }, {  2.5, 20.0 },
                {  3.0,  1.8 }, {  3.0,  2.0 }, {  3.0,  5.0 }, {  3.0,  8.0 }, {  3.0, 20.0 },
                { 10.0,  1.8 }, { 10.0,  2.0 }, { 10.0,  5.0 }, { 10.0,  8.0 }, { 10.0, 20.0 },
            };

        [Theory]
        [MemberData(nameof(AlphaBetaFxDoseGrid))]
        public void RoundTrip_AllAlphaBeta_PreservesValueAcrossGrid(double alphaBeta, double fxDose)
        {
            // 5-fraction equivalent at the per-fx dose; round-trip should
            // recover the total dose to within float precision.
            const int fxCount = 5;
            double totalGy = fxDose * fxCount;

            double eqd2 = DoseFormulas.PhysicalToEqd2(totalGy, alphaBeta, fxCount);
            double back = DoseFormulas.Eqd2ToPhysical(eqd2, alphaBeta, fxCount);

            back.Should().BeApproximately(totalGy, 1e-9);
        }

        // --- Identity ---------------------------------------------------------

        [Theory]
        [InlineData( 50.0,  1.0, 50.0)]
        [InlineData( 50.0,  2.0, 100.0)]
        [InlineData(  0.0,  1.0,  0.0)]
        [InlineData(-10.0,  1.0, -10.0)] // identity does NOT clamp negatives
        public void Identity_ReturnsDoseTimesScaling(double dose, double scaling, double expected)
        {
            DoseFormulas.Identity(dose, alphaBeta: 3.0, fxCount: 5, scaling: scaling)
                .Should().BeApproximately(expected, 1e-12);
        }
    }
}

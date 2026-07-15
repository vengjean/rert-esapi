// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using FluentAssertions;
using ReRT.Domain.DoseConversion;
using Xunit;

namespace ReRT.Tests.DoseConversion
{
    public class ConservativeMaxAccumulatorTests
    {
        private static ConservativePlanInput Plan(
            double physGy, int delivered, int planned, double discount = 0.0, bool present = true)
            => new ConservativePlanInput
            {
                Present = present,
                PhysicalDoseGy = physGy,
                DeliveredFractions = delivered,
                PlannedFractions = planned,
                DiscountPercent = discount,
            };

        [Fact]
        public void SinglePlan_NoScaling_NoDiscount_MatchesEqd2Formula()
        {
            // 60 Gy / 30 fx, αβ=3: 60·(3+2)/5 = 60
            var row = new ConservativeStructureInput
            {
                Label = "SpinalCord",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput> { Plan(60.0, 30, 30) },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells.Should().HaveCount(1);
            result.Cells[0].Present.Should().BeTrue();
            result.Cells[0].PhysicalGy.Should().BeApproximately(60.0, 1e-9);
            result.Cells[0].Eqd2Gy.Should().BeApproximately(60.0, 1e-9);
            result.TotalEqd2Gy.Should().BeApproximately(60.0, 1e-9);
        }

        [Fact]
        public void MultiplePlans_TotalIsSumOfPerPlanEqd2()
        {
            // plan1: 60 Gy / 30 fx, αβ=3 -> 60
            // plan2: 50 Gy /  5 fx, αβ=3 -> 50·(3+10)/5 = 130
            var row = new ConservativeStructureInput
            {
                Label = "OAR",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput> { Plan(60.0, 30, 30), Plan(50.0, 5, 5) },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells[0].Eqd2Gy.Should().BeApproximately(60.0, 1e-9);
            result.Cells[1].Eqd2Gy.Should().BeApproximately(130.0, 1e-9);
            result.TotalEqd2Gy.Should().BeApproximately(190.0, 1e-9);
        }

        [Fact]
        public void DeliveredLessThanPlanned_AppliesFractionScaling()
        {
            // delivered 4 / planned 5 of a 50 Gy plan -> scaled 40 Gy; αβ=3, fx=4
            // EQD2 = 40·(3 + 40/4)/5 = 40·13/5 = 104
            var row = new ConservativeStructureInput
            {
                Label = "OAR",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput> { Plan(50.0, 4, 5) },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells[0].PhysicalGy.Should().BeApproximately(40.0, 1e-9);
            result.Cells[0].Eqd2Gy.Should().BeApproximately(104.0, 1e-9);
            result.TotalEqd2Gy.Should().BeApproximately(104.0, 1e-9);
        }

        [Fact]
        public void Discount_ReducesPerPlanEqd2AndTotal()
        {
            // 104 Gy EQD2 with 25% discount -> 78
            var row = new ConservativeStructureInput
            {
                Label = "OAR",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput> { Plan(50.0, 4, 5, discount: 25.0) },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells[0].Eqd2Gy.Should().BeApproximately(78.0, 1e-9);
            result.Cells[0].DiscountPercent.Should().Be(25.0);
            result.TotalEqd2Gy.Should().BeApproximately(78.0, 1e-9);
        }

        [Fact]
        public void AbsentPlan_ExcludedFromTotalAndMarkedNotPresent()
        {
            var row = new ConservativeStructureInput
            {
                Label = "OAR",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput>
                {
                    Plan(60.0, 30, 30),
                    Plan(99.0, 5, 5, present: false),
                },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells[1].Present.Should().BeFalse();
            result.TotalEqd2Gy.Should().BeApproximately(60.0, 1e-9);
        }

        [Theory]
        [InlineData(0, 5)]  // planned <= 0
        [InlineData(5, 0)]  // delivered <= 0
        public void NonPositiveFractions_AreGuardedAndExcluded(int delivered, int planned)
        {
            var row = new ConservativeStructureInput
            {
                Label = "OAR",
                AlphaBeta = 3.0,
                Plans = new List<ConservativePlanInput> { Plan(60.0, delivered, planned) },
            };

            var result = ConservativeMaxAccumulator.Accumulate(row);

            result.Cells[0].Present.Should().BeFalse();
            result.TotalEqd2Gy.Should().Be(0.0);
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// The views display several editable numbers rounded (StringFormat=N1 / 0.0 /
// 0 / 0.#) while the TwoWay bindings used to store the raw typed value, so the
// number on screen and the number used in computations could differ (typing
// 0.86 showed 0.9 but computed with 0.86). These pin the fix: setters store
// the display-rounded value, away-from-zero to match .NET display formatting.

using System.Collections.ObjectModel;
using FluentAssertions;
using Xunit;

namespace ReRT.Tests.ViewModels
{
    public class DisplayRoundingTests
    {
        [Fact]
        public void MonthsSinceTreatment_StoresOneDecimal_AsDisplayedByN1()
        {
            var plan = new PlanSelectionViewModel { MonthsSinceTreatment = 5.96 };
            plan.MonthsSinceTreatment.Should().Be(6.0);
        }

        [Fact]
        public void MonthsSinceTreatment_NegativeStillClampsToZero()
        {
            var plan = new PlanSelectionViewModel { MonthsSinceTreatment = -3.21 };
            plan.MonthsSinceTreatment.Should().Be(0.0);
        }

        [Fact]
        public void AlphaBetaRatio_StoresOneDecimal_AsDisplayed()
        {
            // Seeded config values get the same rounding as user edits.
            var row = new AnalysisStructureViewModel("Lung", 2.75);
            row.AlphaBetaRatio.Should().Be(2.8);

            row.AlphaBetaRatio = 3.14;
            row.AlphaBetaRatio.Should().Be(3.1);
        }

        [Fact]
        public void Discount_StoresWholePercent_AsDisplayed()
        {
            var cell = new AnalysisPlanCellViewModel(0, new ObservableCollection<string>());
            cell.Discount = 12.4;
            cell.Discount.Should().Be(12.0);

            // Display formatting rounds the half case away from zero.
            cell.Discount = 12.5;
            cell.Discount.Should().Be(13.0);
        }
    }
}

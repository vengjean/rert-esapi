// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Section row-shape tests. Each section consumes a fixed ReportParams
// fixture and is asserted against the rows it should produce. We assert
// row counts and the values of representative tokens rather than diff
// against a serialized snapshot file — the value is the same (drift
// detection) without the maintenance burden of fixtures.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReRT;
using ReRT.Reporting;
using ReRT.Reporting.Sections;
using Xunit;

namespace ReRT.Tests.Reporting
{
    public class SectionSnapshotTests
    {
        // ---- Fixture ----------------------------------------------------------

        private static Model.ReportParams BuildFixture()
        {
            var rp = new Model.ReportParams
            {
                PatientFirstName = "Jean",
                PatientLastName  = "Doe",
                PatientId        = "MRN-1",
                PhysicianName    = "Smith",
                RegistrationType = "rigid",
                PatientDateOfBirth = "01/02/1970",
                Date     = "2026-05-07 10:00",
                Hospital = "hospitalA",
                DoseType = "Both",
                NewPlanFx = 5,
            };

            // Two courses: A has two plans, B has one. Plan B1 is the oldest
            // so it should sort first in the discount table headers.
            rp.PlanNames.AddRange(new[] { "A1", "A2", "B1" });
            rp.CourseDict["A"] = new List<Model.PlanParams>
            {
                new Model.PlanParams { CourseId = "A", PlanId = "A1", Energy = "6X",
                    PlannedFraction = "5", DeliveredFraction = "5", DosePerFraction = "200",
                    TotalDose = "1000", ElapsedMonths = "3" },
                new Model.PlanParams { CourseId = "A", PlanId = "A2", Energy = "10X",
                    PlannedFraction = "10", DeliveredFraction = "10", DosePerFraction = "300",
                    TotalDose = "3000", ElapsedMonths = "6" },
            };
            rp.CourseDict["B"] = new List<Model.PlanParams>
            {
                new Model.PlanParams { CourseId = "B", PlanId = "B1", Energy = "6X",
                    PlannedFraction = "5", DeliveredFraction = "5", DosePerFraction = "400",
                    TotalDose = "2000", ElapsedMonths = "12" },
            };

            // Two structures: Lung (constraint met), Liver (violated).
            rp.StructureNames.AddRange(new[] { "Lung", "Liver" });
            rp.StructureAlphaBeta.AddRange(new[] { "2.5", "3" });
            rp.StructureRegistrationConfidence.AddRange(new[] { "Good local alignment", "Fair local alignment" });
            rp.StructureStats.AddRange(new[] { "30 Gy", "45 Gy" });
            rp.Metric.AddRange(new[] { "VS16Gy", "D0.1cc" });
            rp.Michigan.AddRange(new[] { ">1000 cc", "<40 Gy" });
            rp.SABR_Michigan.AddRange(new[] { ">1000 cc", "N/A" });
            rp.ConstraintMet.AddRange(new[] { true, false });
            rp.Remainder.AddRange(new[] { "100 cc", "5 Gy" });

            // Lung has one SABR sub-row; Liver has none.
            rp.SABR_Metric.Add(new List<string> { "V20Gy" });
            rp.SABR_Constraint.Add(new List<string> { "<40%" });
            rp.SABR_Stats.Add(new List<string> { "20 %" });
            rp.SABR_Metric.Add(new List<string>());
            rp.SABR_Constraint.Add(new List<string>());
            rp.SABR_Stats.Add(new List<string>());

            // Per-plan discounts in original PlanNames order. Lung: 0.1, 0.2, 0.3 (A1, A2, B1).
            rp.StructureDiscount.Add(new List<string> { "0.1", "0.2", "0.3" });
            rp.StructureDiscount.Add(new List<string> { "0.0", "0.0", "0.5" });

            // Conservative per-plan EQD2 D0.1cc, PlanNames order. Liver is
            // absent from B1 (empty cell).
            rp.StructureEqd2PerPlan.Add(new List<string> { "10.00", "20.00", "30.00" });
            rp.StructureEqd2PerPlan.Add(new List<string> { "1.00", "2.00", "" });

            // Physical stats: Lung has Dmax + Dmean; Liver has only Dmax.
            var lungStats = new Model.PhysicalDoseStats { Dmax = "30 Gy", Dmean = "10 Gy" };
            lungStats.Extra_Metrics.AddRange(new[] { "D0.1cc", "Dmean" });
            lungStats.Extra_Stats.AddRange(new[] { "30 Gy", "10 Gy" });
            var liverStats = new Model.PhysicalDoseStats { Dmax = "45 Gy", Dmean = "20 Gy" };
            liverStats.Extra_Metrics.Add("D0.1cc");
            liverStats.Extra_Stats.Add("45 Gy");
            rp.PHYS_Stats.AddRange(new[] { lungStats, liverStats });

            return rp;
        }

        private static string Tok(RepeatRow row, string key) => row.Tokens[key];

        // ---- CourseTableSection -----------------------------------------------

        [Fact]
        public void CourseTable_OneRowPerCourse_WithNestedPlanRows()
        {
            var rows = new CourseTableSection().BuildRows(BuildFixture());

            rows.Should().HaveCount(2);
            Tok(rows[0], "course_id").Should().Be("A");
            Tok(rows[1], "course_id").Should().Be("B");

            // Nested plan_rows should match the fixture's per-course plan list.
            var aPlans = rows[0].Repeats["plan_rows"];
            aPlans.Should().HaveCount(2);
            Tok(aPlans[0], "plan_id").Should().Be("A1");
            Tok(aPlans[0], "delivered_fraction").Should().Be("5");
            Tok(aPlans[1], "plan_id").Should().Be("A2");
            Tok(aPlans[1], "energy").Should().Be("10X");

            var bPlans = rows[1].Repeats["plan_rows"];
            bPlans.Should().HaveCount(1);
            Tok(bPlans[0], "plan_id").Should().Be("B1");
        }

        // ---- DiscountTableSection ---------------------------------------------

        [Fact]
        public void DiscountTable_ColumnsSortedByElapsedMonthsDescending()
        {
            var d = new DiscountTableSection().Build(BuildFixture());

            d.PlanCount.Should().Be(3);
            d.Columns.Should().HaveCount(3);
            // Order by ElapsedMonths desc: B1 (12), A2 (6), A1 (3).
            Tok(d.Columns[0], "plan_id").Should().Be("B1");
            Tok(d.Columns[1], "plan_id").Should().Be("A2");
            Tok(d.Columns[2], "plan_id").Should().Be("A1");
        }

        [Fact]
        public void DiscountTable_RowsAlignDiscountCellsToColumnOrder()
        {
            // Lung's discounts in PlanNames order [A1, A2, B1] = [0.1, 0.2, 0.3].
            // After header reordering to [B1, A2, A1], cell order should be [0.3, 0.2, 0.1].
            var d = new DiscountTableSection().Build(BuildFixture());
            d.Rows.Should().HaveCount(2);

            var lungCells = d.Rows[0].Repeats["discount_cells"];
            lungCells.Select(r => Tok(r, "discount")).Should().Equal("0.3", "0.2", "0.1");

            var liverCells = d.Rows[1].Repeats["discount_cells"];
            liverCells.Select(r => Tok(r, "discount")).Should().Equal("0.5", "0.0", "0.0");
        }

        [Fact]
        public void DiscountTable_ConservativeSubHeaders_TwoPerPlanColumn()
        {
            var d = new DiscountTableSection().Build(BuildFixture());

            // One Discount/EQD2 pair per plan, in the same sorted column order.
            d.SubHeaders.Should().HaveCount(6);
            d.SubHeaders.Select(r => Tok(r, "label")).Should().Equal(
                "Discount (%)", "EQD2 D0.1cc (Gy)",
                "Discount (%)", "EQD2 D0.1cc (Gy)",
                "Discount (%)", "EQD2 D0.1cc (Gy)");
        }

        [Fact]
        public void DiscountTable_ConservativeRows_AlternateDiscountAndEqd2InColumnOrder()
        {
            // Column order is [B1, A2, A1]; each plan contributes a
            // (discount, eqd2) cell pair. Liver is absent from B1, so its
            // EQD2 cell is empty while the discount cell still shows.
            var d = new DiscountTableSection().Build(BuildFixture());
            d.ConservativeRows.Should().HaveCount(2);

            Tok(d.ConservativeRows[0], "structure_name").Should().Be("Lung");
            var lungCells = d.ConservativeRows[0].Repeats["conservative_cells"];
            lungCells.Select(r => Tok(r, "value")).Should().Equal(
                "0.3", "30.00", "0.2", "20.00", "0.1", "10.00");

            var liverCells = d.ConservativeRows[1].Repeats["conservative_cells"];
            liverCells.Select(r => Tok(r, "value")).Should().Equal(
                "0.5", "", "0.0", "2.00", "0.0", "1.00");
        }

        [Fact]
        public void DiscountTable_MissingRegistrationConfidence_RendersEmpty()
        {
            // The conservative report leaves StructureRegistrationConfidence
            // empty; the section must not index past the empty list.
            var rp = BuildFixture();
            rp.StructureRegistrationConfidence.Clear();

            var d = new DiscountTableSection().Build(rp);

            d.Rows.Should().HaveCount(2);
            Tok(d.Rows[0], "registration_confidence").Should().BeEmpty();
        }

        // ---- ConstraintTableSection -------------------------------------------

        [Fact]
        public void ConstraintTable_FlattensMainRowsAndSabrSubRows()
        {
            var rows = new ConstraintTableSection().BuildRows(BuildFixture());

            // Lung main + 1 SABR sub-row + Liver main + 0 SABR = 3 rows.
            rows.Should().HaveCount(3);

            Tok(rows[0], "row_kind").Should().Be("main");
            Tok(rows[0], "structure_name").Should().Be("Lung");
            Tok(rows[0], "row_style").Should().Contain("c6efce"); // green = met

            Tok(rows[1], "row_kind").Should().Be("sabr");
            Tok(rows[1], "structure_name").Should().BeEmpty();
            Tok(rows[1], "alpha_beta").Should().BeEmpty();
            Tok(rows[1], "metric").Should().Be("V20Gy");
            Tok(rows[1], "michigan").Should().Be("N/A");
            Tok(rows[1], "sabr").Should().Be("<40%");

            Tok(rows[2], "row_kind").Should().Be("main");
            Tok(rows[2], "structure_name").Should().Be("Liver");
            Tok(rows[2], "row_style").Should().Contain("ffc7ce"); // pink = violated
        }

        [Fact]
        public void ConstraintTable_NaMichiganGetsNeutralRowColor()
        {
            var rp = BuildFixture();
            rp.Michigan[0] = "N/A";
            var rows = new ConstraintTableSection().BuildRows(rp);
            Tok(rows[0], "row_style").Should().Contain("ffffffff");
        }

        [Fact]
        public void ConstraintTable_PhysicalOnlyMode_LeavesEqd2ParallelArraysEmpty_ReturnsZeroRows()
        {
            // Regression: in PHYS-only mode, ReportParamsBuilder fills
            // StructureNames/StructureAlphaBeta but skips the EQD2 branch,
            // so Michigan/ConstraintMet/Metric/StructureStats/SABR_* stay
            // empty. Pre-fix, BuildRows indexed Michigan[i] for each
            // StructureName and threw ArgumentOutOfRangeException. The
            // template hides this section via `show_eqd2`, so the correct
            // behavior is to produce zero rows, not crash.
            var rp = BuildFixture();
            rp.DoseType = "Physical";
            rp.Michigan.Clear();
            rp.ConstraintMet.Clear();
            rp.Metric.Clear();
            rp.StructureStats.Clear();
            rp.SABR_Michigan.Clear();
            rp.SABR_Metric.Clear();
            rp.SABR_Constraint.Clear();
            rp.SABR_Stats.Clear();

            var rows = new ConstraintTableSection().BuildRows(rp);

            rows.Should().BeEmpty();
        }

        [Fact]
        public void ConstraintTable_PrePlanMode_LeavesMichiganEmpty_ReturnsZeroRows()
        {
            // Symmetric: PrePlanAnalysis mode also leaves Michigan empty
            // (its branch populates only Metric/StructureStats/PreConstraint/
            // Remainder, never Michigan/ConstraintMet/SABR_*). The template
            // hides the EQD2 constraint section via `show_eqd2`, so
            // BuildRows must not index past empty arrays.
            var rp = BuildFixture();
            rp.PrePlanAnalysis = true;
            rp.Michigan.Clear();
            rp.ConstraintMet.Clear();
            rp.SABR_Michigan.Clear();
            rp.SABR_Metric.Clear();
            rp.SABR_Constraint.Clear();
            rp.SABR_Stats.Clear();

            var rows = new ConstraintTableSection().BuildRows(rp);

            rows.Should().BeEmpty();
        }

        // ---- PrePlanAnalysisSection -------------------------------------------

        [Fact]
        public void PrePlan_BuildsOneRowPerPrePlanStructure()
        {
            var rp = BuildFixture();
            rp.PrePlanAnalysis = true;
            rp.PrePlanStructureNames.Add("Lung");
            rp.PreConstraint.Add("20 Gy");
            // StructureStats[0] is "30 Gy", Metric[0] is "VS16Gy", Remainder[0] is "100 cc"

            var rows = new PrePlanAnalysisSection().BuildRows(rp);

            rows.Should().HaveCount(1);
            Tok(rows[0], "structure_name").Should().Be("Lung");
            Tok(rows[0], "received_eqd2").Should().Be("30 Gy");
            Tok(rows[0], "pre_constraint").Should().Be("20 Gy");
            Tok(rows[0], "remainder").Should().Be("100 cc");
            Tok(rows[0], "new_plan_fx").Should().Be("5");
            Tok(rows[0], "course_list").Should().Be("A,B");
            // Constraint present → remainder-dose clause is rendered.
            rows[0].Repeats["show_remainder"].Should().HaveCount(1);
        }

        [Fact]
        public void PrePlan_StructureWithoutConstraint_ListsPriorDose_OmitsRemainderClause()
        {
            var rp = BuildFixture();
            rp.PrePlanAnalysis = true;
            rp.PrePlanStructureNames.Add("Lung");
            // No PreConstraint entry → empty cell; prior EQD2 is still listed
            // from StructureStats[0] ("30 Gy").

            var rows = new PrePlanAnalysisSection().BuildRows(rp);

            rows.Should().HaveCount(1);
            Tok(rows[0], "structure_name").Should().Be("Lung");
            Tok(rows[0], "received_eqd2").Should().Be("30 Gy");
            Tok(rows[0], "pre_constraint").Should().BeEmpty();
            // No constraint → remainder-dose clause is suppressed.
            rows[0].Repeats["show_remainder"].Should().BeEmpty();
        }

        // ---- PhysicalDoseSection ----------------------------------------------

        [Fact]
        public void Physical_OneRowPerStructureExtraMetric()
        {
            var rows = new PhysicalDoseSection().BuildRows(BuildFixture());

            // Lung: 2 metrics + Liver: 1 metric = 3 rows.
            rows.Should().HaveCount(3);
            Tok(rows[0], "structure_name").Should().Be("Lung");
            Tok(rows[0], "metric").Should().Be("D0.1cc");
            Tok(rows[1], "metric").Should().Be("Dmean");
            Tok(rows[2], "structure_name").Should().Be("Liver");
            Tok(rows[2], "evaluation").Should().Be("45 Gy");
        }

        [Fact]
        public void Physical_Eqd2OnlyMode_LeavesPhysStatsEmpty_ReturnsZeroRows()
        {
            // Mirror of the EQD2-only ConstraintTable case: in EQD2-only
            // mode ReportParamsBuilder skips the PHYS branch entirely, so
            // PHYS_Stats stays empty even though StructureNames is filled.
            // PhysicalDoseSection's defensive `i >= PHYS_Stats.Count` break
            // is what keeps this from throwing; pin it so the guard can't
            // regress.
            var rp = BuildFixture();
            rp.DoseType = "EQD2";
            rp.PHYS_Stats.Clear();

            var rows = new PhysicalDoseSection().BuildRows(rp);

            rows.Should().BeEmpty();
        }
    }
}

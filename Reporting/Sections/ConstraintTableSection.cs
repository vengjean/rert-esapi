// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds the EQD2 constraint table rows. The layout is a "main row" per
// structure followed by zero or more SABR sub-rows in which the structure
// name and α/β cells are blank. They are flattened into a single row list
// (with a row_kind discriminator) so the template stamps one <tr> per row and
// color-codes the main row based on ConstraintMet.

using System.Collections.Generic;

namespace ReRT.Reporting.Sections
{
    public sealed class ConstraintTableSection
    {
        // Background colors: green for met, pink/red for violated, white for
        // "N/A" (no Michigan constraint).
        private const string ColorMet      = "background-color: #c6efce;";
        private const string ColorViolated = "background-color: #ffc7ce;";
        private const string ColorNeutral  = "background-color: #ffffffff;";

        public IReadOnlyList<RepeatRow> BuildRows(Model.ReportParams reportParams)
        {
            var rows = new List<RepeatRow>();

            for (int i = 0; i < reportParams.StructureNames.Count; i++)
            {
                // EQD2-side arrays (Michigan, ConstraintMet, Metric,
                // StructureStats, SABR_*) are only populated when
                // PopulateStructureRows runs the `doseType ∈ {EQD2, Both}`
                // branch. In PHYS-only or PrePlanAnalysis mode they stay
                // empty even though StructureNames is filled. The template
                // hides this section via `show_eqd2`, but BuildRows still
                // runs — bail out so we don't index past the parallel
                // arrays.
                if (i >= reportParams.Michigan.Count) break;

                string rowColor;
                if (reportParams.Michigan[i] == "N/A")
                    rowColor = ColorNeutral;
                else
                    rowColor = reportParams.ConstraintMet[i] ? ColorMet : ColorViolated;

                rows.Add(new RepeatRow(new Dictionary<string, string>
                {
                    ["row_kind"]       = "main",
                    ["row_style"]      = rowColor,
                    ["structure_name"] = reportParams.StructureNames[i],
                    ["alpha_beta"]     = reportParams.StructureAlphaBeta[i],
                    ["metric"]         = reportParams.Metric[i],
                    ["evaluation"]     = reportParams.StructureStats[i],
                    ["michigan"]       = reportParams.Michigan[i],
                    ["sabr"]           = reportParams.SABR_Michigan[i],
                }));

                for (int j = 0; j < reportParams.SABR_Constraint[i].Count; j++)
                {
                    rows.Add(new RepeatRow(new Dictionary<string, string>
                    {
                        ["row_kind"]       = "sabr",
                        ["row_style"]      = string.Empty,
                        ["structure_name"] = string.Empty,
                        ["alpha_beta"]     = string.Empty,
                        ["metric"]         = reportParams.SABR_Metric[i][j],
                        ["evaluation"]     = reportParams.SABR_Stats[i][j],
                        ["michigan"]       = "N/A",
                        ["sabr"]           = reportParams.SABR_Constraint[i][j],
                    }));
                }
            }

            return rows;
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds the discount table: column headers (one per plan, sorted by elapsed
// months descending) and structure rows (one per included structure with a
// per-plan sub-repeat of discount cells).
//
// Two table variants share the column order: the registration report shows a
// registration-confidence column and one discount cell per plan; the
// conservative max-dose report instead groups two subcolumns under each plan
// header — Discount (%) and the plan's discounted EQD2 D0.1cc contribution —
// flattened into an alternating cell list (SubHeaders / ConservativeRows).
//
// Sorting: the column headers and in-row cells are ordered by ElapsedMonths
// descending, while the rest of ReportParams (PlanNames, StructureDiscount inner
// indices) keeps the original plan order — preserved here so the layout matches.

using System.Collections.Generic;
using System.Linq;

namespace ReRT.Reporting.Sections
{
    public sealed class DiscountTableSection
    {
        public DiscountRows Build(Model.ReportParams reportParams)
        {
            var allPlans = reportParams.CourseDict.Values
                .SelectMany(plans => plans)
                .OrderByDescending(p => SafeInt(p.ElapsedMonths))
                .ToList();

            var columns = new List<RepeatRow>();
            var orderedIndices = new List<int>();
            foreach (var plan in allPlans)
            {
                int legacyIndex = reportParams.PlanNames.IndexOf(plan.PlanId);
                orderedIndices.Add(legacyIndex);
                columns.Add(new RepeatRow(new Dictionary<string, string>
                {
                    ["plan_id"]        = plan.PlanId ?? string.Empty,
                    ["elapsed_months"] = plan.ElapsedMonths ?? string.Empty,
                }));
            }

            var subHeaders = new List<RepeatRow>();
            foreach (var _ in allPlans)
            {
                subHeaders.Add(new RepeatRow(new Dictionary<string, string> { ["label"] = "Discount (%)" }));
                subHeaders.Add(new RepeatRow(new Dictionary<string, string> { ["label"] = "EQD2 D0.1cc (Gy)" }));
            }

            var rows = new List<RepeatRow>();
            var conservativeRows = new List<RepeatRow>();
            for (int i = 0; i < reportParams.StructureNames.Count; i++)
            {
                var cells = new List<RepeatRow>();
                var pairedCells = new List<RepeatRow>();
                foreach (var legacyIndex in orderedIndices)
                {
                    string discount = (legacyIndex >= 0 && legacyIndex < reportParams.StructureDiscount[i].Count)
                        ? reportParams.StructureDiscount[i][legacyIndex]
                        : string.Empty;
                    string eqd2 = (legacyIndex >= 0 && i < reportParams.StructureEqd2PerPlan.Count
                                   && legacyIndex < reportParams.StructureEqd2PerPlan[i].Count)
                        ? reportParams.StructureEqd2PerPlan[i][legacyIndex]
                        : string.Empty;
                    cells.Add(new RepeatRow(new Dictionary<string, string>
                    {
                        ["discount"] = discount,
                    }));
                    pairedCells.Add(new RepeatRow(new Dictionary<string, string> { ["value"] = discount }));
                    pairedCells.Add(new RepeatRow(new Dictionary<string, string> { ["value"] = eqd2 }));
                }

                string confidence = i < reportParams.StructureRegistrationConfidence.Count
                    ? reportParams.StructureRegistrationConfidence[i]
                    : string.Empty;
                rows.Add(new RepeatRow(
                    new Dictionary<string, string>
                    {
                        ["structure_name"]            = reportParams.StructureNames[i],
                        ["registration_confidence"]   = confidence,
                    },
                    new Dictionary<string, IReadOnlyList<RepeatRow>>
                    {
                        ["discount_cells"] = cells,
                    }));
                conservativeRows.Add(new RepeatRow(
                    new Dictionary<string, string>
                    {
                        ["structure_name"] = reportParams.StructureNames[i],
                    },
                    new Dictionary<string, IReadOnlyList<RepeatRow>>
                    {
                        ["conservative_cells"] = pairedCells,
                    }));
            }

            return new DiscountRows
            {
                Columns = columns,
                Rows = rows,
                SubHeaders = subHeaders,
                ConservativeRows = conservativeRows,
                PlanCount = columns.Count,
            };
        }

        private static int SafeInt(string s)
            => int.TryParse(s, out var n) ? n : 0;

        public sealed class DiscountRows
        {
            public IReadOnlyList<RepeatRow> Columns;
            public IReadOnlyList<RepeatRow> Rows;
            // Conservative variant: two header cells per plan column
            // ("Discount (%)", "EQD2 D0.1cc (Gy)") ...
            public IReadOnlyList<RepeatRow> SubHeaders;
            // ... and structure rows whose "conservative_cells" sub-repeat
            // alternates discount / EQD2 values in the same column order.
            public IReadOnlyList<RepeatRow> ConservativeRows;
            public int PlanCount;
        }
    }
}

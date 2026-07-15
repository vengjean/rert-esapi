// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds the pre-plan analysis rows: one paragraph per pre-plan structure
// describing prior EQD2 dose and, when a suggested constraint is set, the
// remaining headroom in physical dose for a new fraction count. Structures
// without a constraint list their prior EQD2 dose only — the remainder-dose
// clause is toggled off per row via the nested "show_remainder" repeat.

using System.Collections.Generic;

namespace ReRT.Reporting.Sections
{
    public sealed class PrePlanAnalysisSection
    {
        public IReadOnlyList<RepeatRow> BuildRows(Model.ReportParams reportParams)
        {
            var rows = new List<RepeatRow>();
            string courseList = string.Join(",", reportParams.CourseDict.Keys);

            for (int i = 0; i < reportParams.PrePlanStructureNames.Count; i++)
            {
                string preConstraint = SafeAt(reportParams.PreConstraint, i);
                bool hasConstraint = !string.IsNullOrEmpty(preConstraint);

                var tokens = new Dictionary<string, string>
                {
                    ["structure_name"]    = reportParams.PrePlanStructureNames[i],
                    ["received_eqd2"]     = SafeAt(reportParams.StructureStats, i),
                    ["course_list"]       = courseList,
                    ["pre_constraint"]    = preConstraint,
                    ["metric"]            = SafeAt(reportParams.Metric, i),
                    ["remainder"]         = SafeAt(reportParams.Remainder, i),
                    ["new_plan_fx"]       = reportParams.NewPlanFx.ToString(),
                };

                // The remainder-dose clause is shown only for OARs with a
                // suggested constraint (0-or-1-row toggle, like the report's
                // other show_* flags); constraint-less OARs list prior dose only.
                var repeats = new Dictionary<string, IReadOnlyList<RepeatRow>>
                {
                    ["show_remainder"] = hasConstraint
                        ? (IReadOnlyList<RepeatRow>)new List<RepeatRow> { new RepeatRow(new Dictionary<string, string>()) }
                        : new List<RepeatRow>(),
                };

                rows.Add(new RepeatRow(tokens, repeats));
            }
            return rows;
        }

        private static string SafeAt(IReadOnlyList<string> list, int i)
            => (list != null && i < list.Count) ? list[i] : string.Empty;
    }
}

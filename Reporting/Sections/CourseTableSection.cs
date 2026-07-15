// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds the per-course block rows: one outer row per course, each carrying
// a sub-repeat of plan rows. The template stamps the layout; this section
// only produces typed values.

using System.Collections.Generic;

namespace ReRT.Reporting.Sections
{
    public sealed class CourseTableSection
    {
        public IReadOnlyList<RepeatRow> BuildRows(Model.ReportParams reportParams)
        {
            var courseRows = new List<RepeatRow>();

            foreach (var course in reportParams.CourseDict)
            {
                var planRows = new List<RepeatRow>();
                foreach (var plan in course.Value)
                {
                    planRows.Add(new RepeatRow(new Dictionary<string, string>
                    {
                        ["course_id"]          = plan.CourseId ?? string.Empty,
                        ["plan_id"]            = plan.PlanId ?? string.Empty,
                        ["energy"]             = plan.Energy ?? string.Empty,
                        ["delivered_fraction"] = plan.DeliveredFraction ?? string.Empty,
                        ["planned_fraction"]   = plan.PlannedFraction ?? string.Empty,
                        ["dose_per_fraction"]  = plan.DosePerFraction ?? string.Empty,
                        ["total_dose"]         = plan.TotalDose ?? string.Empty,
                        ["elapsed_months"]     = plan.ElapsedMonths ?? string.Empty,
                    }));
                }

                courseRows.Add(new RepeatRow(
                    new Dictionary<string, string>
                    {
                        ["course_id"] = course.Key,
                    },
                    new Dictionary<string, IReadOnlyList<RepeatRow>>
                    {
                        ["plan_rows"] = planRows,
                    }));
            }

            return courseRows;
        }
    }
}

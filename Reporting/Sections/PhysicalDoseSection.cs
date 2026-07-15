// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds the accumulated physical dose statistics rows: one row per
// (structure, extra-metric) pair, emitting a row for each entry in
// PhysicalDoseStats.Extra_*.

using System.Collections.Generic;

namespace ReRT.Reporting.Sections
{
    public sealed class PhysicalDoseSection
    {
        public IReadOnlyList<RepeatRow> BuildRows(Model.ReportParams reportParams)
        {
            var rows = new List<RepeatRow>();
            for (int i = 0; i < reportParams.StructureNames.Count; i++)
            {
                if (i >= reportParams.PHYS_Stats.Count) break;
                var stats = reportParams.PHYS_Stats[i];
                for (int j = 0; j < stats.Extra_Metrics.Count; j++)
                {
                    rows.Add(new RepeatRow(new Dictionary<string, string>
                    {
                        ["structure_name"] = reportParams.StructureNames[i],
                        ["metric"]         = stats.Extra_Metrics[j],
                        ["evaluation"]     = stats.Extra_Stats[j],
                    }));
                }
            }
            return rows;
        }
    }
}

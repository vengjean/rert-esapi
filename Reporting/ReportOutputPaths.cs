// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Shared output-path resolution for generated reports, so the registration-based
// report and the conservative max-dose report write to the same
// institution-configured locations.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ReRT.Configuration.Loaders;

namespace ReRT.Reporting
{
    internal static class ReportOutputPaths
    {
        /// <summary>
        /// Resolve the full output file paths for a report. If any output
        /// entry's <c>match</c> is found in the patient's hospital field, write
        /// to those entries; otherwise write to every empty-<c>match</c> default
        /// entry; otherwise fall back to a folder next to the assembly. Each
        /// path is <c>&lt;root&gt;\&lt;year&gt;\&lt;month&gt;\&lt;Last, First&gt;\&lt;prefix&gt;_&lt;timestamp&gt;.docx</c>.
        /// </summary>
        public static IReadOnlyList<string> Resolve(
            string patientLast, string patientFirst, string hospital,
            InstitutionConfig institution, string fileNamePrefix)
        {
            string year = DateTime.Now.Year.ToString();
            string monthFormatted = $"{DateTime.Now.Month} {DateTime.Now.ToString("MMMM")}";
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportName = $"{fileNamePrefix}_{timestamp}.docx";
            hospital = hospital ?? string.Empty;

            var roots = new List<string>();
            foreach (var output in institution.ReportOutputs)
            {
                if (string.IsNullOrEmpty(output.Path)) continue;
                if (!string.IsNullOrEmpty(output.Match) && hospital.IndexOf(output.Match, StringComparison.OrdinalIgnoreCase) >= 0)
                    roots.Add(output.Path);
            }

            if (roots.Count == 0)
            {
                foreach (var output in institution.ReportOutputs)
                {
                    if (string.IsNullOrEmpty(output.Path)) continue;
                    if (string.IsNullOrEmpty(output.Match))
                        roots.Add(output.Path);
                }
            }

            if (roots.Count == 0)
                roots.Add(Path.Combine(ResolveAssemblyDir(), "ReRT_Reports"));

            var paths = new List<string>(roots.Count);
            foreach (var root in roots)
            {
                string folder = Path.Combine(root, year, monthFormatted, $"{patientLast}, {patientFirst}\\");
                paths.Add(Path.Combine(folder, reportName));
            }
            return paths;
        }

        private static string ResolveAssemblyDir()
        {
            string asmLocation = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(asmLocation))
                asmLocation = AppDomain.CurrentDomain.BaseDirectory;
            return Path.GetDirectoryName(asmLocation);
        }
    }
}

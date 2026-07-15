// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Orchestrates report assembly: load template, build sections, render to
// HTML, hand the HTML body to DocxWriter, and surface post-save UI side
// effects (open the saved DOCX, show the clamped-dose warning when raised).
//
// All institution-specific values (department header, logo, SABR attribution,
// output paths) come from InstitutionConfig — no per-institution literals below.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using ReRT.Configuration.Loaders;
using ReRT.Reporting.Sections;

namespace ReRT.Reporting
{
    public sealed class ReportBuilder
    {
        // Title is intrinsic to this report type ("Prior RT Physics Consult"
        // is what the script generates, regardless of institution).
        private const string ReportTitle = "Prior RT Physics Consult";

        private readonly ITemplateLoader _loader;
        private readonly TemplateRenderer _renderer;
        private readonly DocxWriter _writer;
        private readonly DvhPlotter _dvhPlotter;
        private readonly InstitutionConfig _institution;

        public ReportBuilder(InstitutionConfig institution)
            : this(institution, new FileTemplateLoader(), new TemplateRenderer(), new DocxWriter(), new DvhPlotter())
        { }

        public ReportBuilder(
            InstitutionConfig institution,
            ITemplateLoader loader,
            TemplateRenderer renderer,
            DocxWriter writer,
            DvhPlotter dvhPlotter)
        {
            _institution = institution ?? throw new ArgumentNullException(nameof(institution));
            _loader     = loader     ?? throw new ArgumentNullException(nameof(loader));
            _renderer   = renderer   ?? throw new ArgumentNullException(nameof(renderer));
            _writer     = writer     ?? throw new ArgumentNullException(nameof(writer));
            _dvhPlotter = dvhPlotter ?? throw new ArgumentNullException(nameof(dvhPlotter));
        }

        public string Build(Model.ReportParams reportParams)
        {
            if (reportParams == null) throw new ArgumentNullException(nameof(reportParams));

            string template = _loader.LoadReportTemplate();

            var courseSection     = new CourseTableSection().BuildRows(reportParams);
            var discount          = new DiscountTableSection().Build(reportParams);
            var prePlanRows       = new PrePlanAnalysisSection().BuildRows(reportParams);
            var eqd2Rows          = new ConstraintTableSection().BuildRows(reportParams);
            var physicalRows      = new PhysicalDoseSection().BuildRows(reportParams);

            bool isPrePlan = reportParams.PrePlanAnalysis;
            string doseType = reportParams.DoseType ?? string.Empty;
            bool showEqd2     = !isPrePlan && (doseType == "EQD2" || doseType == "Both");
            bool showPhysical = !isPrePlan && (doseType == "Physical" || doseType == "Both");

            bool showDvhAppendix = !isPrePlan && !reportParams.IsConservative;
            string dvhEqd2 = showDvhAppendix
                ? WrapAsImg(_dvhPlotter.RenderToDataUri(reportParams.EQD2_DVH_List, reportParams.StructureNames, "EQD2 Accumulated DVH"))
                : string.Empty;
            string dvhPhys = showDvhAppendix
                ? WrapAsImg(_dvhPlotter.RenderToDataUri(reportParams.PHYS_DVH_List, reportParams.StructureNames, "Physical Accumulated DVH"))
                : string.Empty;

            var tokens = new Dictionary<string, string>
            {
                ["run_date"]                          = reportParams.Date ?? string.Empty,
                ["physician_name"]                    = reportParams.PhysicianName ?? string.Empty,
                ["patient_first_name"]                = reportParams.PatientFirstName ?? string.Empty,
                ["patient_last_name"]                 = reportParams.PatientLastName ?? string.Empty,
                ["patient_id"]                        = reportParams.PatientId ?? string.Empty,
                ["patient_dob"]                       = reportParams.PatientDateOfBirth ?? string.Empty,
                ["patient_hospital"]                  = reportParams.Hospital ?? string.Empty,
                ["registration_type"]                 = reportParams.RegistrationType ?? string.Empty,
                ["dose_type"]                         = doseType,
                ["course_list_inline"]                = BuildCourseListInline(reportParams),
                ["structure_list_inline"]             = BuildStructureListInline(reportParams),
                ["plan_count"]                        = reportParams.PlanNames.Count.ToString(),
                ["new_plan_fx"]                       = reportParams.NewPlanFx.ToString(),
                ["dvh_eqd2_img_html"]                 = dvhEqd2,
                ["dvh_phys_img_html"]                 = dvhPhys,
                ["conservative_region"]               = reportParams.ConservativeRegion ?? string.Empty,
                ["institution_constraint_attribution"] = _institution.Institution.ConstraintAttribution ?? string.Empty,
            };

            var repeats = new Dictionary<string, IReadOnlyList<RepeatRow>>
            {
                ["course_blocks"]                  = courseSection,
                ["discount_columns"]               = discount.Columns,
                ["discount_rows"]                  = discount.Rows,
                ["conservative_subheaders"]        = discount.SubHeaders,
                ["conservative_rows"]              = discount.ConservativeRows,
                ["pre_plan_rows"]                  = prePlanRows,
                ["eqd2_constraint_rows"]           = eqd2Rows,
                ["physical_stat_rows"]             = physicalRows,
                ["show_pre_plan"]                  = SingleRow(isPrePlan),
                ["show_eqd2"]                      = SingleRow(showEqd2),
                ["show_physical"]                  = SingleRow(showPhysical),
                ["show_post_constraint_appendix"]  = SingleRow(showDvhAppendix),
                ["show_registration_accumulation"] = SingleRow(!reportParams.IsConservative),
                ["show_conservative_accumulation"] = SingleRow(reportParams.IsConservative),
                ["show_conservative_expansion"]    = SingleRow(reportParams.IsConservative && !reportParams.ConservativeGlobal),
            };

            string body = _renderer.Render(template, tokens, repeats);

            var header = new DocxHeader
            {
                LogoPath        = _institution.Institution.ResolvedLogoPath ?? string.Empty,
                DepartmentName  = _institution.Institution.DepartmentHeader ?? string.Empty,
                ReportTitle     = ReportTitle,
                PatientName     = $"{reportParams.PatientLastName}, {reportParams.PatientFirstName}",
                PatientId       = reportParams.PatientId,
                PatientDob      = reportParams.PatientDateOfBirth,
                PhysicianName   = reportParams.PhysicianName,
            };

            var outputPaths = ResolveOutputPaths(reportParams, _institution);
            _writer.WriteHtmlToDocx(body, header, outputPaths);

            string lastSaved = outputPaths[outputPaths.Count - 1];
            try { Process.Start(lastSaved); }
            catch (Exception) { /* viewer-not-available is non-fatal for the save itself. */ }

            if (reportParams.ShowClampedMessage)
            {
                MessageBox.Show(
                    "Warning: dose in some voxels exceeded the limit handled by Eclipse. " +
                    "Maximum dose is capped at an arbitrary high value.");
            }

            return lastSaved;
        }

        // --- Output path resolution -------------------------------------------

        // Delegates to the shared ReportOutputPaths helper (also used by the
        // conservative max-dose report) with this report type's file prefix.
        private static IReadOnlyList<string> ResolveOutputPaths(
            Model.ReportParams rp, InstitutionConfig institution)
        {
            return ReportOutputPaths.Resolve(
                rp.PatientLastName, rp.PatientFirstName, rp.Hospital, institution, "PriorRT_Report");
        }

        // --- Token helpers -----------------------------------------------------

        private static string BuildCourseListInline(Model.ReportParams rp)
        {
            // Emit "{key}, " for each course, including the trailing ", ".
            var sb = new StringBuilder();
            foreach (var key in rp.CourseDict.Keys)
                sb.Append(key).Append(", ");
            return sb.ToString();
        }

        private static string BuildStructureListInline(Model.ReportParams rp)
        {
            int n = rp.StructureNames.Count;
            if (n == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Append("<strong>").Append(rp.StructureNames[i]).Append("</strong>");
                if (i < n - 2)      sb.Append(", ");
                else if (i == n - 2) sb.Append(" and ");
                else                 sb.Append(".");
            }
            return sb.ToString();
        }

        private static IReadOnlyList<RepeatRow> SingleRow(bool visible)
        {
            return visible
                ? (IReadOnlyList<RepeatRow>)new List<RepeatRow> { new RepeatRow(new Dictionary<string, string>()) }
                : new List<RepeatRow>();
        }

        private static string WrapAsImg(string dataUri)
        {
            if (string.IsNullOrEmpty(dataUri)) return string.Empty;
            return $"<p><img src='{dataUri}' style='max-width: 500px;' /></p>";
        }
    }
}

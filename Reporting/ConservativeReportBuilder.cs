// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Produces the prior-RT consult report for the conservative maximum-dose
// method by assembling a Model.ReportParams from the GUI state and delegating
// to the shared ReportBuilder. The output is the standard consult report
// (template, title, filename, constraint color-coding, plan listing) with the
// conservative near-max EQD2 values and without the DVH/screenshot appendix.
using System;
using System.Collections.Generic;
using System.Globalization;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;

namespace ReRT.Reporting
{
    public sealed class ConservativeReportParams
    {
        public string PatientFirstName { get; set; }
        public string PatientLastName { get; set; }
        public string PatientId { get; set; }
        public string PatientDateOfBirth { get; set; }
        public string Hospital { get; set; }
        public string PhysicianName { get; set; }
        public string Date { get; set; }
        public string TargetName { get; set; }
        public double InPlaneMarginCm { get; set; }
        public double SupInfMarginCm { get; set; }
        public bool GlobalEvaluation { get; set; }
        public List<PlanSelectionViewModel> Plans { get; set; } = new List<PlanSelectionViewModel>();
        public List<ConservativeStructureResult> Structures { get; set; } = new List<ConservativeStructureResult>();
    }

    public sealed class ConservativeStructureResult
    {
        public string Label { get; set; }
        public double AlphaBeta { get; set; }
        public double TotalEqd2Gy { get; set; }
        public List<string> PerPlanDiscount { get; set; } = new List<string>();
        // Per-plan discounted EQD2 D0.1cc (Gy) display strings, aligned to
        // PerPlanDiscount; empty string where the structure is absent.
        public List<string> PerPlanEqd2 { get; set; } = new List<string>();
        public ConstraintSet Constraints { get; set; }
    }

    public sealed class ConservativeReportBuilder
    {
        private readonly InstitutionConfig _institution;

        public ConservativeReportBuilder(InstitutionConfig institution)
        {
            _institution = institution ?? throw new ArgumentNullException(nameof(institution));
        }

        public string Build(ConservativeReportParams rp)
        {
            if (rp == null) throw new ArgumentNullException(nameof(rp));
            return new ReportBuilder(_institution).Build(ToReportParams(rp));
        }

        private static Model.ReportParams ToReportParams(ConservativeReportParams rp)
        {
            var model = new Model.ReportParams
            {
                PatientFirstName   = rp.PatientFirstName,
                PatientLastName    = rp.PatientLastName,
                PatientId          = rp.PatientId,
                PatientDateOfBirth = rp.PatientDateOfBirth,
                Hospital           = rp.Hospital,
                PhysicianName      = rp.PhysicianName,
                Date               = rp.Date,
                RegistrationType   = string.Empty,
                DoseType           = "EQD2",
                PrePlanAnalysis    = false,
                NewPlanFx          = 0,
                IsConservative     = true,
                ConservativeRegion = BuildRegion(rp),
                ConservativeGlobal = rp.GlobalEvaluation,
            };

            foreach (var p in rp.Plans)
            {
                model.PlanNames.Add(p.Id);
                double dosePerFx = p.PlannedFraction > 0 ? Math.Round(p.TotalDose / p.PlannedFraction, 1) : 0.0;
                var pp = new Model.PlanParams
                {
                    CourseId          = p.CourseId,
                    PlanId            = p.Id,
                    Energy            = p.Energy ?? string.Empty,
                    PlannedFraction   = p.PlannedFraction.ToString(CultureInfo.InvariantCulture),
                    DeliveredFraction = p.Fraction.ToString(CultureInfo.InvariantCulture),
                    DosePerFraction   = dosePerFx.ToString("0.#", CultureInfo.InvariantCulture),
                    TotalDose         = p.TotalDose.ToString("0", CultureInfo.InvariantCulture),
                    ElapsedMonths     = Math.Round(p.MonthsSinceTreatment).ToString("0", CultureInfo.InvariantCulture),
                };
                if (model.CourseDict.TryGetValue(p.CourseId, out var list)) list.Add(pp);
                else model.CourseDict[p.CourseId] = new List<Model.PlanParams> { pp };
            }

            foreach (var s in rp.Structures)
            {
                var eval = NearMaxConstraintEvaluator.Evaluate(s.Constraints, s.TotalEqd2Gy);

                model.StructureNames.Add(s.Label ?? string.Empty);
                model.StructureAlphaBeta.Add(s.AlphaBeta.ToString(CultureInfo.InvariantCulture));
                model.StructureDiscount.Add(s.PerPlanDiscount ?? new List<string>());
                model.StructureEqd2PerPlan.Add(s.PerPlanEqd2 ?? new List<string>());

                model.Metric.Add("D0.1cc");
                model.StructureStats.Add(s.TotalEqd2Gy.ToString("0.00", CultureInfo.InvariantCulture) + " Gy");
                model.Michigan.Add(eval.HasMichigan ? HtmlLt(eval.MichiganDisplay) : "N/A");
                model.ConstraintMet.Add(!eval.HasMichigan || eval.Met);
                model.Remainder.Add(string.Empty);

                model.SABR_Michigan.Add(HtmlLt(eval.SabrDisplay));
                model.SABR_Constraint.Add(new List<string>());
                model.SABR_Metric.Add(new List<string>());
                model.SABR_Stats.Add(new List<string>());
            }

            return model;
        }

        // Phrase describing the evaluation region for the report narrative.
        private static string BuildRegion(ConservativeReportParams rp)
        {
            if (rp.GlobalEvaluation)
                return "the entire structure (no target expansion)";
            string ip = rp.InPlaneMarginCm.ToString("0.#", CultureInfo.InvariantCulture);
            string si = rp.SupInfMarginCm.ToString("0.#", CultureInfo.InvariantCulture);
            return $"a {ip} cm in-plane / {si} cm superior-inferior expansion around the {rp.TargetName} structure";
        }

        // Constraint cells render "<" as the HTML entity in the consult template.
        private static string HtmlLt(string s) => s?.Replace("<", "&lt;") ?? string.Empty;
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Builds Model.ReportParams from a completed dose-conversion run. The pipeline
// owns the ESAPI side-effect code (plan-sum creation, voxel transformation);
// this class owns the ESAPI-read-only step of producing the report's data bag.
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ReRT.Domain.Constraints;
using ReRT.Domain.DoseConversion;
using DomainDvhProvider = ReRT.Domain.DoseMetrics.IDvhProvider;
using EsapiDvhProvider = ReRT.Domain.DoseMetrics.EsapiDvhProvider;
using DoseMetricCalculator = ReRT.Domain.DoseMetrics.DoseMetricCalculator;
using DomainStructureRef = ReRT.Domain.DoseMetrics.StructureRef;
using DomainVolPres = ReRT.Domain.DoseMetrics.VolumePresentation;

namespace ReRT.Reporting
{
    /// <summary>
    /// Inputs the report-params builder needs once the pipeline's plan-sum
    /// creation phase is complete. Kept explicit so the builder doesn't
    /// reach back into pipeline state.
    /// </summary>
    public sealed class ReportParamsInput
    {
        public PlanSum SourceSum;
        public List<PlanSetup> PlanList;
        public Dictionary<string, ViewModel.PlanEdit> PlansDict;
        public Dictionary<string, string> ConvertParams;
        public List<StructureViewModel> Mappings;
        public string DoseType;
        public PlanSum Eqd2Sum;     // nullable
        public PlanSum PhysSum;     // nullable
        public bool PrePlanAnalysis;
        public int NewPlanFx;
    }

    public sealed class ReportParamsBuilder
    {
        // Display labels shown under "Registration" in the report. The
        // dropdown values map 1:1 here; a missing key falls back to
        // verbatim display.
        private static readonly Dictionary<string, string> RegistrationDisplay =
            new Dictionary<string, string>
        {
            { "Good local alignment",            "Good local alignment" },
            { "Fair local alignment",            "Fair local alignment" },
            { "Bad (will use direct sum)",       "Bad local alignment"  },
            { "Other (please specify in report)", "Other: "             },
        };

        public Model.ReportParams Build(ReportParamsInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var rp = BuildHeader(input.SourceSum, input.PlanList, input.PlansDict,
                                 input.ConvertParams, input.DoseType);
            rp.PrePlanAnalysis = input.PrePlanAnalysis;
            rp.NewPlanFx = input.NewPlanFx;
            PopulateStructureRows(rp, input);
            return rp;
        }

        private static Model.ReportParams BuildHeader(
            PlanSum planSum,
            List<PlanSetup> planList,
            Dictionary<string, ViewModel.PlanEdit> plansDict,
            Dictionary<string, string> convertParams,
            string doseType)
        {
            var activeCourse = planSum.Course;
            var rp = new Model.ReportParams
            {
                PatientFirstName  = activeCourse.Patient.FirstName,
                PatientLastName   = activeCourse.Patient.LastName,
                PatientId         = activeCourse.Patient.Id,
                Date              = DateTime.Now.ToString("MM/dd/yyyy HH:mm"),
                PhysicianName     = convertParams["physicianName"],
                RegistrationType  = convertParams["registrationType"],
                PatientDateOfBirth = ((DateTime)activeCourse.Patient.DateOfBirth).ToString("MM/dd/yyyy"),
                DoseType          = doseType,
                Hospital          = activeCourse.Patient.Hospital.Name,
            };

            foreach (var plan in planList)
            {
                string planKey = plan.Course.Id + "/" + plan.Id;
                var planEdit = plansDict[planKey];
                int deliveredFraction = planEdit.Fraction;
                int elapsedMonths = planEdit.ElapsedMonths;
                string energy = "";
                foreach (var beam in plan.Beams)
                {
                    if (beam.IsSetupField) continue;
                    if (string.IsNullOrEmpty(energy))
                        energy = beam.EnergyModeDisplayName;
                    else if (!energy.Contains(beam.EnergyModeDisplayName))
                        energy += $", {beam.EnergyModeDisplayName}";
                }

                rp.PlanNames.Add(plan.Id);
                var pp = new Model.PlanParams
                {
                    CourseId         = plan.Course.Id,
                    PlanId           = plan.Id,
                    Energy           = energy,
                    PlannedFraction  = planEdit.PlannedFraction.ToString(),
                    DeliveredFraction = deliveredFraction.ToString(),
                    DosePerFraction  = planEdit.DosePerFraction.ToString(),
                    TotalDose        = planEdit.TotalDose.ToString(),
                    ElapsedMonths    = elapsedMonths.ToString(),
                };
                if (rp.CourseDict.TryGetValue(plan.Course.Id, out var list)) list.Add(pp);
                else rp.CourseDict[plan.Course.Id] = new List<Model.PlanParams> { pp };
            }
            return rp;
        }

        private static void PopulateStructureRows(Model.ReportParams rp, ReportParamsInput input)
        {
            var planSum = input.SourceSum;
            var planList = input.PlanList;
            var mappings = input.Mappings;
            var doseType = input.DoseType;
            var eqd2Sum = input.Eqd2Sum;
            var physSum = input.PhysSum;

            DomainDvhProvider eqd2Provider = eqd2Sum != null ? new EsapiDvhProvider(eqd2Sum) : null;
            DomainDvhProvider physProvider = physSum != null ? new EsapiDvhProvider(physSum) : null;
            DoseMetricCalculator eqd2Calc = eqd2Provider != null ? new DoseMetricCalculator(eqd2Provider) : null;
            DoseMetricCalculator physCalc = physProvider != null ? new DoseMetricCalculator(physProvider) : null;
            ConstraintEvaluator eqd2Evaluator = eqd2Calc != null ? new ConstraintEvaluator(eqd2Calc) : null;

            // PrePlanAnalysis evaluates prior EQD2 dose vs the user-set
            // constraint; it has no meaning without an EQD2 sum. The UI
            // is expected to gate this, but fail loudly if a Physical-only
            // PrePlan run ever reaches here — eqd2Calc.Dmax() would NPE
            // mid-loop and leave a half-populated ReportParams behind.
            if (input.PrePlanAnalysis && eqd2Calc == null)
            {
                var err = "PrePlanAnalysis requires an EQD2 dose sum, but doseType yielded no EQD2 conversion. " +
                          "Re-run with doseType set to EQD2 or Both.";
                Helpers.SeriLog.LogError(err);
                throw new InvalidOperationException(err);
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                var strVM = mappings[i];
                if (!strVM.Include || strVM.IsBody) continue;

                var structure = planSum.StructureSet.Structures.FirstOrDefault(
                    x => string.Equals(x.Id, strVM.StructureId, StringComparison.InvariantCultureIgnoreCase));
                if (structure == null)
                {
                    // Mapping references a structure that's no longer in the
                    // structure set (renamed/deleted between mapping capture
                    // and report build). Surface a readable error instead of
                    // letting the next `structure.Volume` access NPE.
                    var err = $"Structure '{strVM.StructureId}' from mappings was not found in plan sum '{planSum.Id}' structure set.";
                    Helpers.SeriLog.LogError(err);
                    throw new InvalidOperationException(err);
                }

                rp.StructureNames.Add(strVM.StructureId);
                rp.StructureAlphaBeta.Add(strVM.AlphaBetaRatio.ToString());
                if (!RegistrationDisplay.TryGetValue(strVM.RegistrationConfidence, out var displayLabel))
                    displayLabel = strVM.RegistrationConfidence;
                rp.StructureRegistrationConfidence.Add(displayLabel);

                var discountList = new List<string>();
                for (int planIdx = 0; planIdx < planList.Count; planIdx++)
                {
                    var d = (strVM.Discounts != null && planIdx < strVM.Discounts.Count)
                        ? strVM.Discounts[planIdx] : 0.0;
                    discountList.Add(d.ToString());
                }
                rp.StructureDiscount.Add(discountList);

                if (input.PrePlanAnalysis)
                {
                    rp.PrePlanStructureNames.Add(strVM.StructureId);

                    var prePlanRef = new DomainStructureRef(strVM.StructureId, structure.Volume);
                    double nearmax = eqd2Calc.Dmax(prePlanRef);
                    rp.StructureStats.Add($"{Math.Round(nearmax, 2)} Gy");

                    // OARs with no suggested constraint are still listed with
                    // their prior EQD2 dose, but carry no remainder budget. The
                    // empty constraint/metric/remainder cells signal
                    // PrePlanAnalysisSection to drop the remainder-dose clause.
                    if (string.IsNullOrEmpty(strVM.PreConstraint))
                    {
                        rp.Metric.Add(string.Empty);
                        rp.PreConstraint.Add(string.Empty);
                        rp.Remainder.Add(string.Empty);
                        continue;
                    }

                    double.TryParse(strVM.PreConstraint, out double preConstraint);
                    double remainder = Math.Max(0, preConstraint - nearmax);
                    double physicalRemainder = DoseFormulas.Eqd2ToPhysical(remainder, strVM.AlphaBetaRatio, input.NewPlanFx);

                    rp.Metric.Add("D0.1cc");
                    rp.PreConstraint.Add($"{Math.Round(preConstraint, 2)} Gy");
                    rp.Remainder.Add($"{Math.Round(physicalRemainder, 2)} Gy");
                    continue;
                }

                if (doseType == "Physical" || doseType == "Both")
                {
                    rp.PHYS_DVH_List.Add(physSum.GetDVHCumulativeData(structure,
                        DoseValuePresentation.Absolute, VolumePresentation.Relative, 1.0));

                    var physRef = new DomainStructureRef(strVM.StructureId, structure.Volume);
                    var physStats = new Model.PhysicalDoseStats
                    {
                        Dmax  = $"{Math.Round(physCalc.Dmax(physRef), 2)} Gy",
                        Dmean = $"{Math.Round(physCalc.Mean(physRef), 2)} Gy",
                    };
                    physStats.Extra_Metrics.Add("D0.1cc");
                    physStats.Extra_Stats.Add(physStats.Dmax);
                    physStats.Extra_Metrics.Add("Dmean");
                    physStats.Extra_Stats.Add(physStats.Dmean);

                    if (!string.IsNullOrEmpty(strVM.PHYS))
                    {
                        foreach (var token in strVM.PHYS.Split(';'))
                        {
                            if (token.StartsWith("VS"))
                            {
                                if (double.TryParse(token.Substring(2), out double doseGy))
                                {
                                    double vs = physCalc.VolumeSparedAtDose(physRef, doseGy);
                                    physStats.Extra_Metrics.Add("VS" + doseGy + "Gy");
                                    physStats.Extra_Stats.Add($"{Math.Round(vs, 1)} cc");
                                }
                            }
                            else if (token.StartsWith("V"))
                            {
                                if (double.TryParse(token.Substring(1), out double doseGy))
                                {
                                    double v = physCalc.VolumeAtDose(physRef, doseGy, DomainVolPres.RelativePercent);
                                    physStats.Extra_Metrics.Add("V" + doseGy + "Gy");
                                    physStats.Extra_Stats.Add($"{Math.Round(v, 1)} %");
                                }
                            }
                        }
                    }
                    rp.PHYS_Stats.Add(physStats);
                }

                if (doseType == "EQD2" || doseType == "Both")
                {
                    rp.EQD2_DVH_List.Add(eqd2Sum.GetDVHCumulativeData(structure,
                        DoseValuePresentation.Absolute, VolumePresentation.Relative, 1.0));

                    var eqd2Ref = new DomainStructureRef(strVM.StructureId, structure.Volume);

                    // SABR rows: first slot is the "Michigan-equivalent"
                    // header (display only); evaluable entries follow.
                    var sabr = strVM.Constraints?.GetForSet("SABR");
                    var sabrConstraint = new List<string>();
                    var sabrMetric     = new List<string>();
                    var sabrStats      = new List<string>();
                    string sabrMichigan;

                    if (sabr == null || sabr.Count == 0)
                    {
                        sabrMichigan = "N/A";
                    }
                    else
                    {
                        sabrMichigan = FormatSabrMichiganHeader(sabr[0]);
                        for (int idx = 1; idx < sabr.Count; idx++)
                        {
                            var c = sabr[idx];
                            if (c == null) continue;
                            var result = eqd2Evaluator.Evaluate(c, eqd2Ref);
                            AppendSabrRow(result, sabrMetric, sabrConstraint, sabrStats);
                            rp.AllEvaluations.Add(new Model.ConstraintEvaluationRecord
                            {
                                StructureName = strVM.StructureId,
                                Result = result,
                            });
                        }
                    }
                    rp.SABR_Constraint.Add(sabrConstraint);
                    rp.SABR_Metric.Add(sabrMetric);
                    rp.SABR_Stats.Add(sabrStats);
                    rp.SABR_Michigan.Add(sabrMichigan);

                    // Michigan row: at most one constraint is read; when none
                    // exist, emit a D0.1cc placeholder row so the per-structure
                    // table layout is preserved.
                    var michigans = strVM.Constraints?.GetEvaluable("Michigan");
                    if (michigans == null || michigans.Count == 0)
                    {
                        double nearmax = eqd2Calc.Dmax(eqd2Ref);
                        rp.Metric.Add("D0.1cc");
                        rp.StructureStats.Add($"{Math.Round(nearmax, 2)} Gy");
                        rp.Michigan.Add("N/A");
                        rp.Remainder.Add("N/A");
                        rp.ConstraintMet.Add(true);
                    }
                    else
                    {
                        var result = eqd2Evaluator.Evaluate(michigans[0], eqd2Ref);
                        AppendMichiganRow(result, rp);
                        rp.AllEvaluations.Add(new Model.ConstraintEvaluationRecord
                        {
                            StructureName = strVM.StructureId,
                            Result = result,
                        });
                    }
                }
            }
        }

        // ----- Constraint-row formatting ----------------------------------------

        private static string FormatSabrMichiganHeader(Constraint c)
        {
            if (c == null) return "N/A";
            switch (c.Metric)
            {
                case MetricType.VolumeSpared:
                    return (c.Op == ">" || c.Op == ">=") ? ">" + c.Limit + " cc" : "N/A";
                case MetricType.Dmax:
                case MetricType.Mean:
                    return (c.Op == "<" || c.Op == "<=") ? "&lt;" + c.Limit + " Gy" : "N/A";
                case MetricType.V:
                    return (c.Op == "<" || c.Op == "<=") ? "&lt;" + c.Limit + " %" : "N/A";
                default:
                    return "N/A";
            }
        }

        private static void AppendSabrRow(EvaluationResult r, List<string> metricL,
                                          List<string> constraintL, List<string> statsL)
        {
            var c = r.Constraint;
            switch (c.Metric)
            {
                case MetricType.Mean:
                    metricL.Add("Mean");
                    statsL.Add(Math.Round(r.MeasuredValue, 2) + " Gy");
                    constraintL.Add("&lt;" + c.Limit + " Gy");
                    break;
                case MetricType.VolumeSpared:
                    metricL.Add("VS" + (c.DoseGy ?? 0) + "Gy");
                    constraintL.Add(">" + c.Limit + " cc");
                    statsL.Add($"{Math.Round(r.MeasuredValue, 1)} cc");
                    break;
                case MetricType.V:
                    metricL.Add("V" + (c.DoseGy ?? 0) + "Gy");
                    constraintL.Add("&lt;" + c.Limit + "%");
                    statsL.Add($"{Math.Round(r.MeasuredValue, 1)} %");
                    break;
                case MetricType.Dmax:
                    metricL.Add("D0.1cc");
                    statsL.Add($"{Math.Round(r.MeasuredValue, 2)} Gy");
                    constraintL.Add("&lt;" + c.Limit + " Gy");
                    break;
            }
        }

        private static void AppendMichiganRow(EvaluationResult r, Model.ReportParams rp)
        {
            var c = r.Constraint;
            switch (c.Metric)
            {
                case MetricType.VolumeSpared:
                    rp.Metric.Add("VS" + (c.DoseGy ?? 0) + "Gy");
                    rp.Michigan.Add(">" + c.Limit + " cc");
                    rp.StructureStats.Add($"{Math.Round(r.MeasuredValue, 1)} cc");
                    rp.ConstraintMet.Add(r.IsMet);
                    rp.Remainder.Add($"{Math.Round(r.Remainder, 1)} cc");
                    break;
                case MetricType.V:
                    rp.Metric.Add("V" + (c.DoseGy ?? 0) + "Gy");
                    rp.Michigan.Add("&lt;" + c.Limit + "%");
                    rp.StructureStats.Add($"{Math.Round(r.MeasuredValue, 1)} %");
                    rp.ConstraintMet.Add(r.IsMet);
                    rp.Remainder.Add($"{Math.Round(r.Remainder, 1)} %");
                    break;
                case MetricType.Dmax:
                    rp.Metric.Add("D0.1cc");
                    rp.Michigan.Add("&lt;" + c.Limit + " Gy");
                    rp.StructureStats.Add($"{Math.Round(r.MeasuredValue, 2)} Gy");
                    rp.ConstraintMet.Add(r.IsMet);
                    rp.Remainder.Add($"{Math.Round(r.Remainder, 2)} Gy");
                    break;
                case MetricType.Mean:
                    rp.Metric.Add("Mean");
                    rp.Michigan.Add("&lt;" + c.Limit + " Gy");
                    rp.StructureStats.Add($"{Math.Round(r.MeasuredValue, 2)} Gy");
                    rp.ConstraintMet.Add(r.IsMet);
                    rp.Remainder.Add($"{Math.Round(r.Remainder, 2)} Gy");
                    break;
            }
        }
    }
}

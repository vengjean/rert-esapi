// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

// Orchestrates the EQD2 + Physical dose-conversion passes. The pipeline owns
// the ESAPI side-effect work (plan-sum + dose creation, voxel transformation);
// report-params construction lives in ReportParamsBuilder so the two halves can
// evolve independently.
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ReRT.Reporting;

namespace ReRT.Domain.DoseConversion
{
    public enum ConversionMode { Eqd2, Physical }

    /// <summary>
    /// Bag of inputs the orchestrator needs from the ViewModel/UI side.
    /// Keeping it explicit (vs. reaching into Model state) makes the
    /// pipeline easier to drive from a future test harness.
    /// </summary>
    public sealed class DoseConversionInput
    {
        public PlanSum SourceSum;
        public List<StructureViewModel> Mappings;
        public Dictionary<string, string> ConvertParams;
        public Dictionary<string, ViewModel.PlanEdit> PlansDict;
    }

    public sealed class DoseConversionPipeline
    {
        private readonly Model _host;
        private readonly VoxelTransformer _transformer = new VoxelTransformer();

        // Run-scoped name registries to avoid case-insensitive collisions
        // when ESAPI renames our created plans/sums.
        private readonly HashSet<string> _createdPlanNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _createdPlanSumNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Per-call mutable state, held on the pipeline (not the host Model) for
        // the duration of one conversion run.
        private int[,,] _originalArray;
        private double _voxelToGy;

        public DoseConversionPipeline(Model host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public Model.ReportParams Run(DoseConversionInput input)
        {
            bool prePlanAnalysis = string.Equals(
                input.ConvertParams["prePlan"], "pre", StringComparison.OrdinalIgnoreCase);
            int newPlanFx = 0;
            if (prePlanAnalysis)
                int.TryParse(input.ConvertParams["newPlanFx"], out newPlanFx);

            var planSum = input.SourceSum;
            var planList = planSum.PlanSetups.ToList();
            string doseType = input.ConvertParams["doseType"];
            Course activeCourse = planSum.Course;

            Course reRTCourse = CreateReRTCourse(activeCourse);

            // One pass per requested mode (EQD2 and/or Physical).
            var perModePlanSum = new Dictionary<ConversionMode, PlanSum>();
            foreach (var mode in new[] { ConversionMode.Eqd2, ConversionMode.Physical })
            {
                if (!ShouldRunMode(doseType, mode)) continue;

                var newPlanList = new List<ExternalPlanSetup>();
                PlanSum sumForMode = CreateConvertedPlanSum(
                    planSum, planList, ref newPlanList, reRTCourse,
                    input.Mappings, input.PlansDict, mode);

                // The ESAPI-blessed way to "rename" the just-created plan
                // sum is to build a fresh one with a unique id and copy
                // the constituent plans into it. (CreatePlanSum can't
                // accept an empty list, hence the dummy plan dance.)
                var dummyPlan = activeCourse.AddExternalPlanSetup(sumForMode.StructureSet);
                var renamedSum = activeCourse.CreatePlanSum(
                    new List<PlanningItem> { dummyPlan }, sumForMode.StructureSet.Image);
                var newName = _host.GenerateUniqueName(sumForMode.Id,
                    name => activeCourse.PlanSums.Any(x => string.Equals(x.Id, name, StringComparison.OrdinalIgnoreCase)),
                    _createdPlanSumNames,
                    separator: "_");
                renamedSum.Id = newName;
                _createdPlanSumNames.Add(newName);
                foreach (var plan in sumForMode.PlanSetups)
                    renamedSum.AddItem(plan);
                renamedSum.RemoveItem(dummyPlan);
                activeCourse.RemovePlanSetup(dummyPlan);

                perModePlanSum[mode] = sumForMode;
            }

            var reportParams = new ReportParamsBuilder().Build(new ReportParamsInput
            {
                SourceSum = planSum,
                PlanList = planList,
                PlansDict = input.PlansDict,
                ConvertParams = input.ConvertParams,
                Mappings = input.Mappings,
                DoseType = doseType,
                Eqd2Sum = perModePlanSum.TryGetValue(ConversionMode.Eqd2,    out var eqd2Sum) ? eqd2Sum : null,
                PhysSum = perModePlanSum.TryGetValue(ConversionMode.Physical, out var physSum) ? physSum : null,
                PrePlanAnalysis = prePlanAnalysis,
                NewPlanFx = newPlanFx,
            });

            reportParams.ShowClampedMessage = _transformer.Clamped;

            new ReportBuilder(_host.InstitutionConfig).Build(reportParams);
            return reportParams;
        }

        // ----- Plan-sum creation ------------------------------------------------

        private PlanSum CreateConvertedPlanSum(
            PlanSum planSum,
            List<PlanSetup> planList,
            ref List<ExternalPlanSetup> newPlanList,
            Course course,
            List<StructureViewModel> mappings,
            Dictionary<string, ViewModel.PlanEdit> plansDict,
            ConversionMode mode)
        {
            string trace = $"PlanSum {planSum.Id} has structure set: {planSum.StructureSet.Id}\n";
            var planSumFOR = planSum.StructureSet.Image.FOR;

            for (int planIdx = 0; planIdx < planList.Count; planIdx++)
            {
                var plan = planList[planIdx];
                string planKey = plan.Course.Id + "/" + plan.Id;
                var planEdit = plansDict[planKey];

                string sourceName = (mode == ConversionMode.Eqd2 ? "EQD2_" : "PHYS_")
                    + plan.Id.Substring(0, Math.Min(8, plan.Id.Length));
                sourceName = sourceName.Trim();
                sourceName = _host.GenerateUniqueName(sourceName,
                    name => course.ExternalPlanSetups.Any(x => string.Equals(x.Id, name, StringComparison.OrdinalIgnoreCase)),
                    _createdPlanNames,
                    separator: "_");

                ExternalPlanSetup newSourcePlan = (plan.GetType() == typeof(BrachyPlanSetup))
                    ? _host.CreateBrachyVerificationPlan((BrachyPlanSetup)plan, course, sourceName)
                    : _host.CreateVerificationPlan((ExternalPlanSetup)plan, course, sourceName);
                _createdPlanNames.Add(newSourcePlan.Id);
                newPlanList.Add(newSourcePlan);

                RegistrationAdapter registration = null;
                if (plan.Series.FOR != planSumFOR)
                {
                    registration = FindRegistration(plan, planSumFOR);
                    if (registration == null)
                    {
                        var msg = $"No registration found for plan {plan.Id} with FOR {plan.Series.FOR}\n";
                        Helpers.SeriLog.LogError(trace + msg);
                        throw new Exception(trace + msg);
                    }
                }

                ComputeDoseMatrix(newSourcePlan, plan, mappings, planEdit, planIdx,
                                  planSum.StructureSet, registration, mode);
            }

            string sumName = (mode == ConversionMode.Eqd2 ? "EQD2_" : "PHYS_") + planSum.Id;
            sumName = sumName.Substring(0, Math.Min(sumName.Length, 13));
            sumName = _host.GenerateUniqueName(sumName,
                name => course.PlanSums.Any(x => string.Equals(x.Id, name, StringComparison.OrdinalIgnoreCase)),
                _createdPlanSumNames,
                separator: "_");

            VMS.TPS.Common.Model.API.Image targetImage = planSum.StructureSet.Image;
            IEnumerable<PlanningItem> seed = new List<PlanningItem> { newPlanList[0] };
            PlanSum newPlanSum = course.CreatePlanSum(seed, targetImage);
            foreach (var plan in newPlanList.Skip(1))
                newPlanSum.AddItem(plan);
            newPlanSum.DoseValuePresentation = DoseValuePresentation.Absolute;
            newPlanSum.Id = sumName;
            _createdPlanSumNames.Add(sumName);

            return newPlanSum;
        }

        // ----- Per-plan dose conversion -----------------------------------------

        private int[,,] ComputeDoseMatrix(
            ExternalPlanSetup newPlan,
            PlanningItem source,
            List<StructureViewModel> mappings,
            ViewModel.PlanEdit planEdit,
            int planIdx,
            StructureSet registeredStructureSet,
            RegistrationAdapter registration,
            ConversionMode mode)
        {
            Dose dose = source.Dose;

            int plannedFraction = planEdit.PlannedFraction;
            int deliveredFraction = planEdit.Fraction;
            double fractionScaling = (double)deliveredFraction / plannedFraction;

            int Xsize = dose.XSize, Ysize = dose.YSize, Zsize = dose.ZSize;

            _originalArray = _host.GetDoseVoxelsFromDose(dose);
            int[,,] doseMatrix = new int[
                _originalArray.GetLength(0),
                _originalArray.GetLength(1),
                _originalArray.GetLength(2)];

            double maxDoseVal = _host.GetMaxDoseVal(dose, source);
            Tuple<int, int> minMaxDose = Helpers.GetMinMaxValues(_originalArray, Xsize, Ysize, Zsize);
            _voxelToGy = maxDoseVal / minMaxDose.Item2;

            var planSetup = source as PlanSetup;
            if (planSetup == null)
            {
                const string err = "Error converting dose. Plan component is a plan sum, this is not supposed to be possible.";
                Helpers.SeriLog.LogError(err);
                throw new Exception(err);
            }

            DoseFormula formula = (mode == ConversionMode.Eqd2)
                ? (DoseFormula)DoseFormulas.PhysicalToEqd2
                : (DoseFormula)DoseFormulas.Identity;

            // doseMatrix is freshly allocated for this (plan, mode), so
            // the transformer's voxel-claim sets — which exist to arbitrate
            // OAR-vs-OAR overlap WITHIN a single sweep — must start empty.
            // Without this, later modes/plans see every voxel as already
            // claimed by the previous sweep and write nothing.
            _transformer.ResetClaims();

            // Structure priority is the table order; OrderForSweep applies it
            // (highest priority first, body forced last) so the first-writer-
            // wins claim grid lets priority #1 keep contested voxels.
            foreach (var strVM in OrderForSweep(mappings))
            {
                var structure = registeredStructureSet.Structures
                    .FirstOrDefault(x => string.Equals(x.Id, strVM.StructureId,
                                                       StringComparison.InvariantCultureIgnoreCase));
                double alphaBeta = strVM.AlphaBetaRatio;
                double discount = (strVM.Discounts != null && planIdx < strVM.Discounts.Count)
                    ? strVM.Discounts[planIdx] : 0.0;

                if (structure.IsEmpty || structure.Volume < 0.01)
                {
                    var err = $"Error converting dose. Structure {structure.Id} is empty or less than 0.01 cc";
                    Helpers.SeriLog.LogError(err);
                    throw new Exception(err);
                }

                _transformer.Apply(structure, alphaBeta, (short)deliveredFraction,
                    _originalArray, doseMatrix, _voxelToGy, dose, formula,
                    discount, fractionScaling, registration,
                    isFallbackContour: strVM.IsBody);
            }

            CreatePlanAndAddDose(Xsize, Ysize, Zsize, doseMatrix, maxDoseVal, newPlan, planSetup);
            return doseMatrix;
        }

        // Sweep order encodes structure priority. UpdatePriorities() numbers the
        // table top-down (index 0 = priority #1 = highest); the voxel-claim grid
        // is first-writer-wins within a tier (the earliest Inner keeps an inner
        // voxel, the earliest Halo keeps a boundary voxel), so the highest-
        // priority structure must be applied FIRST — forward list order, NOT
        // reversed. The body contour is forced last regardless of its row: it's
        // the catch-all envelope, and any OAR's inner OR halo must beat it at
        // boundary voxels. Pure (no ESAPI) so the ordering invariant is unit-tested.
        internal static List<StructureViewModel> OrderForSweep(IEnumerable<StructureViewModel> mappings)
        {
            var included = mappings.Where(x => x.Include).ToList();
            return included.Where(x => !x.IsBody)
                           .Concat(included.Where(x => x.IsBody))
                           .ToList();
        }

        // ----- Plan creation + dose write ---------------------------------------

        private void CreatePlanAndAddDose(
            int Xsize, int Ysize, int Zsize,
            int[,,] doseMatrix, double doseMaxOriginal,
            ExternalPlanSetup newPlan, PlanSetup thisPlan)
        {
            int fractions = thisPlan.NumberOfFractions ?? 0;
            DoseValue dosePerFraction = thisPlan.DosePerFraction;
            double treatPercentage = thisPlan.TreatmentPercentage;

            newPlan.SetPrescription(fractions, dosePerFraction, treatPercentage);
            newPlan.DoseValuePresentation = DoseValuePresentation.Absolute;

            double normalization = thisPlan.PlanNormalizationValue;
            newPlan.PlanNormalizationValue = double.IsNaN(normalization) ? 100 : normalization;

            EvaluationDose evalDose = newPlan.CopyEvaluationDose(thisPlan.Dose);
            double maxDoseVal = _host.GetMaxDoseVal(evalDose, newPlan);

            var minMaxDoseInt = Helpers.GetMinMaxValues(doseMatrix, Xsize, Ysize, Zsize);
            int maxInt = minMaxDoseInt.Item2;
            double scalingBack = doseMaxOriginal / maxDoseVal;

            if (maxInt * scalingBack > int.MaxValue)
                throw new System.IO.InvalidDataException("Maximum integer value exceeded. Conversion failed.");

            for (int k = 0; k < Zsize; k++)
            {
                int[,] plane = new int[Xsize, Ysize];
                for (int i = 0; i < Xsize; i++)
                {
                    for (int j = 0; j < Ysize; j++)
                        plane[i, j] = (int)(doseMatrix[k, i, j] * scalingBack);
                }
                evalDose.SetVoxels(k, plane);
            }

            // No dose overlap → seed one voxel so EvaluationDose isn't empty.
            if (maxInt == 0)
            {
                int[,] plane = new int[Xsize, Ysize];
                plane[0, 0] = 1;
                evalDose.SetVoxels(0, plane);
            }
        }

        // ----- ReportParams construction ---------------------------------------

        private static Course CreateReRTCourse(Course activeCourse)
        {
            string coursePrefix = activeCourse.Id
                .Split(new[] { ' ' }).First()
                .Split(new[] { '_' }).First();
            if (coursePrefix.Length > 4) coursePrefix = coursePrefix.Substring(0, 4);
            string baseName = $"ReRT DNU ({coursePrefix})";

            var course = activeCourse.Patient.AddCourse();
            int idx = 0;
            string attempt = baseName;
            var existing = activeCourse.Patient.Courses;
            while (existing.Any(x => string.Equals(x.Id, attempt, StringComparison.OrdinalIgnoreCase)))
            {
                idx++;
                attempt = $"ReRT DNU ({coursePrefix}) {idx}";
                if (attempt.Length > 18) attempt = attempt.Substring(0, 18);
                if (idx > 99) throw new Exception("Could not create unique course name for ReRT course.");
            }
            course.Id = attempt;
            return course;
        }

        // Eclipse stores Registration objects in one specific direction
        // (SourceFOR → RegisteredFOR). The pipeline needs to map the plan
        // frame to the plan-sum frame; either direction's stored Registration
        // is mathematically sufficient because TransformPoint and
        // InverseTransformPoint are inverses of each other. We try forward
        // first, then reverse.
        internal static RegistrationAdapter FindRegistration(PlanSetup plan, string planSumFOR)
        {
            var regs = plan.Course.Patient.Registrations;
            var forward = regs.FirstOrDefault(
                x => x.SourceFOR == plan.Series.FOR && x.RegisteredFOR == planSumFOR);
            if (forward != null) return RegistrationAdapter.Forward(forward);

            var reverse = regs.FirstOrDefault(
                x => x.SourceFOR == planSumFOR && x.RegisteredFOR == plan.Series.FOR);
            if (reverse != null) return RegistrationAdapter.Reverse(reverse);

            return null;
        }

        private static bool ShouldRunMode(string doseType, ConversionMode mode)
        {
            if (doseType == "Both") return true;
            if (doseType == "EQD2"     && mode == ConversionMode.Eqd2)     return true;
            if (doseType == "Physical" && mode == ConversionMode.Physical) return true;
            return false;
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPIScript;
using static ReRT.ViewModel;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;
using ReRT.Domain.DoseConversion;

namespace ReRT
{
    public class Model
    {
        private ReRTConfig _config;
        private List<StructureViewModel> StructureDefinitions { get; set; } = new List<StructureViewModel>();
        public double DefaultAlphaBeta { get; private set; }

        // Loaded once at app init by ViewModel and read by DoseConversionPipeline
        // when constructing the ReportBuilder. See Configuration/Loaders/InstitutionConfigLoader.cs.
        public InstitutionConfig InstitutionConfig { get; }

        // Latest pipeline output. Read by ViewModel after
        // GetConvertedDose returns so the run-summary log can attach
        // patient hash + per-evaluation counts without changing the
        // public return shape.
        public ReportParams LastReportParams { get; private set; }

        private EsapiWorker _ew;

        public class PlanParams
        {
            public string CourseId { get; set; }
            public string PlanId { get; set; }

            public string Energy { get; set; }

            public string PlannedFraction { get; set; }

            public string DeliveredFraction { get; set; }

            public string DosePerFraction { get; set; }

            public string TotalDose { get; set; }

            public string ElapsedMonths { get; set; }
        }

        public class ReportParams
        {
            public string PatientFirstName { get; set; }
            public string PatientLastName { get; set; }
            public string PatientId { get; set; }
            public string PhysicianName { get; set; }
            public string RegistrationType { get; set; }
            public string PatientDateOfBirth { get; set; }
            public string Date { get; set; }
            public string Hospital { get; set; }

            public Dictionary<string, List<PlanParams>> CourseDict { get; set; } = new Dictionary<string, List<PlanParams>>();

            public List<string> PlanNames { get; set; } = new List<string>();

            public List<string> StructureNames { get; set; } = new List<string>();

            public List<string> StructureStats { get; set; } = new List<string>();

            public List<string> StructureAlphaBeta { get; set; } = new List<string>();

            public List<string> StructureRegistrationConfidence { get; set; } = new List<string>();

            public List<string> Michigan { get; set; } = new List<string>();
            public List<string> Metric { get; set; } = new List<string>();
            public List<bool> ConstraintMet { get; set; } = new List<bool>();
            public List<List<string>> SABR_Metric { get; set; } = new List<List<string>>();
            public List<List<string>> SABR_Constraint { get; set; } = new List<List<string>>();
            public List<List<string>> SABR_Stats { get; set; } = new List<List<string>>();
            public List<List<string>> StructureDiscount { get; set; } = new List<List<string>>();

            // Conservative max-dose report only: per-structure, per-plan discounted
            // EQD2 D0.1cc display strings aligned to PlanNames order (empty string
            // when the structure is absent from that plan). Stays empty for the
            // registration-based report.
            public List<List<string>> StructureEqd2PerPlan { get; set; } = new List<List<string>>();

            public List<string> SABR_Michigan { get; set; } = new List<string>();

            public List<DVHData> EQD2_DVH_List { get; set; } = new List<DVHData>();

            public List<DVHData> PHYS_DVH_List { get; set; } = new List<DVHData>();

            public List<PhysicalDoseStats> PHYS_Stats { get; set; } = new List<PhysicalDoseStats>();

            public string DoseType { get; set; }
            public List<string> Remainder { get; set; } = new List<string>();
            public List<string> PreConstraint { get; set; } = new List<string>();
            public List<string> PrePlanStructureNames { get; set; } = new List<string>();

            public bool PrePlanAnalysis { get; set; }
            public int NewPlanFx { get; set; }
            public bool ShowClampedMessage { get; set; }

            // Conservative max-dose report: same template, but with no composite
            // DVH the plots and screenshot appendix are suppressed and the
            // accumulation narrative switches to the conservative wording, naming
            // the target expansion the evaluation was limited to.
            public bool IsConservative { get; set; }
            // Pre-formatted phrase describing the conservative target expansion used in
            // the report narrative. Unused for global evaluation.
            public string ConservativeRegion { get; set; }
            // True when global (whole-structure) evaluation was used; the report then omits
            // the target-expansion sentence.
            public bool ConservativeGlobal { get; set; }

            // Per-evaluation record consumed by RunMetrics. Populated
            // by DoseConversionPipeline alongside the parallel SABR/Michigan
            // display lists.
            public List<ConstraintEvaluationRecord> AllEvaluations { get; set; } =
                new List<ConstraintEvaluationRecord>();
        }

        public class ConstraintEvaluationRecord
        {
            public string StructureName { get; set; }
            public EvaluationResult Result { get; set; }
        }

        public class PhysicalDoseStats
        {
            public string Dmax { get; set; }
            public string Dmean { get; set; }
            public List<string> Extra_Metrics { get; set; } = new List<string>();
            public List<string> Extra_Stats { get; set; } = new List<string>();
        }

        /// <summary>
        /// Generate a unique name based on desiredName. If desiredName is available it will be returned.
        /// Otherwise a numeric suffix will be appended (with optional separator) until a unique name is found.
        /// Checks both the existing-name predicate and the in-memory createdSet (case-insensitive).
        /// </summary>
        internal string GenerateUniqueName(string desiredName, Func<string, bool> existsChecker, HashSet<string> createdSet, string separator = "_", int startCounter = 0, int maxAttempts = 99, int maxLength = 13)
        {
            if (desiredName == null) throw new ArgumentNullException(nameof(desiredName));
            if (existsChecker == null) throw new ArgumentNullException(nameof(existsChecker));
            if (createdSet == null) throw new ArgumentNullException(nameof(createdSet));

            var baseName = desiredName.Trim();
            if (maxLength > 0 && baseName.Length > maxLength)
                baseName = baseName.Substring(0, maxLength);

            // If available as-is, return it
            if (!existsChecker(baseName) && !createdSet.Contains(baseName))
                return baseName;

            // Try suffixes until we find a free one
            for (int counter = startCounter; counter < startCounter + maxAttempts; counter++)
            {
                var suffix = (string.IsNullOrEmpty(separator) ? counter.ToString() : separator + counter.ToString());
                int allowedBaseLen = maxLength > 0 ? Math.Max(0, maxLength - suffix.Length) : baseName.Length;
                var truncatedBase = baseName.Length > allowedBaseLen ? baseName.Substring(0, allowedBaseLen) : baseName;
                var candidate = truncatedBase + suffix;
                if (!existsChecker(candidate) && !createdSet.Contains(candidate))
                    return candidate;
            }

            throw new Exception($"Could not generate unique name for '{desiredName}' after {maxAttempts} attempts.");
        }

        internal ExternalPlanSetup CreateVerificationPlan(ExternalPlanSetup refPlan, Course refCourse, string newPlanName)
        {
            var newPlan = refCourse.AddExternalPlanSetupAsVerificationPlan(refPlan.StructureSet, (ExternalPlanSetup)refPlan);
            newPlan.Id = newPlanName;
            return newPlan;
        }

        internal ExternalPlanSetup CreateBrachyVerificationPlan(BrachyPlanSetup refPlan, Course refCourse, string newPlanName)
        {
            // Brachy plans can't be passed directly to AddExternalPlanSetupAsVerificationPlan;
            // create a throwaway external plan to seed the verification, then remove it.
            var dummyPlan = refCourse.AddExternalPlanSetup(refPlan.StructureSet);
            var newPlan = refCourse.AddExternalPlanSetupAsVerificationPlan(refPlan.StructureSet, dummyPlan);
            refCourse.RemovePlanSetup(dummyPlan);
            newPlan.Id = newPlanName;

            return newPlan;
        }

        public async Task<(ScriptStatus, string, string)> GetConvertedDose(string courseId, string planId, bool isSum, List<StructureViewModel> mappings, Dictionary<string, string> ConvertParams, Dictionary<string, PlanEdit> PlansDict)
        {
            string returnMessage = "";
            ScriptStatus status = ScriptStatus.Incomplete;
            string exceptionMessage = "";
            // Clear before the run so the structured run-summary never
            // attaches a previous run's evaluations on a failure path.
            LastReportParams = null;

            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                try
                {
                    if (isSum)
                    {
                        var sum = ps.FirstOrDefault(x => string.Equals(x.Id, planId, StringComparison.OrdinalIgnoreCase));

                        if (sum.Dose != null)
                        {
                            try
                            {
                                var pipeline = new ReRT.Domain.DoseConversion.DoseConversionPipeline(this);
                                LastReportParams = pipeline.Run(new ReRT.Domain.DoseConversion.DoseConversionInput
                                {
                                    SourceSum = sum,
                                    Mappings = mappings,
                                    ConvertParams = ConvertParams,
                                    PlansDict = PlansDict,
                                });
                            }
                            catch (Exception ex)
                            {
                                returnMessage = "Error converting plan sum dose...";
                                exceptionMessage = ex.Message;
                                Helpers.SeriLog.LogError(returnMessage, ex);
                                status = ScriptStatus.Error;
                                return;
                            }
                        }
                        else
                        {
                            returnMessage = "Selected plan sum has no dose.";
                            status = ScriptStatus.Error;
                            return;
                        }

                    }
                    else
                    {
                        returnMessage = "Selected plan has no dose or is not a plan sum.";
                        status = ScriptStatus.Error;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    status = ScriptStatus.Error;
                    returnMessage = "Unexpected error - mouse over for details";
                    exceptionMessage = ex.Message;
                }
            });
            if (status == ScriptStatus.Error)
            {
                return (status, returnMessage, exceptionMessage);
            }
            else
            {
                returnMessage = "Complete!";
                status = ScriptStatus.Complete;
                return (status, returnMessage, exceptionMessage);
            }
        }

        public Model(ReRTConfig config, InstitutionConfig institutionConfig, EsapiWorker ew)
        {
            _config = config;
            InstitutionConfig = institutionConfig ?? throw new ArgumentNullException(nameof(institutionConfig));
            _ew = ew;
            DefaultAlphaBeta = _config.Defaults.AlphaBetaRatio;
        }

        public async Task<string> GetPhysicianName()
        {
            try
            {
                string physicianName = "";
                await _ew.AsyncRunPlanSumContext((p, ps) =>
                {
                    physicianName = p.PrimaryOncologistName;
                });
                return physicianName;
            }
            catch (Exception ex)
            {
                string errorMessage = "Error getting physician name.";
                Helpers.SeriLog.LogError(errorMessage, ex);
                throw new Exception(errorMessage);
            }
        }
        public async Task<List<Tuple<string, string, string, bool>>> GetPlans()
        {
            try
            {
                var AllPlans = new List<Tuple<string, string, string, bool>>();
                await _ew.AsyncRunPlanSumContext((p, ps) =>
                {
                    foreach (var sum in ps)
                    {
                        AllPlans.Add(new Tuple<string, string, string, bool>(sum.Id, sum.Course.Id, sum.StructureSet.UID, true));
                    }
                });
                return AllPlans;
            }
            catch (Exception ex)
            {
                string errorMessage = "Error enumerating plans in GetPlans().";
                Helpers.SeriLog.LogError(errorMessage, ex);
                throw new Exception(errorMessage);
            }
        }

        public async Task<List<(string Id, string CourseId, string SsId, bool IsSum, int Fraction, double TotalDose, string Date, string Energy)>> GetPlanSumComponent(string PlanSumId)
        {
            var PlanList = new List<(string Id, string CourseId, string SsId, bool IsSum, int Fraction, double TotalDose, string Date, string Energy)>();
            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                var planSum = ps.FirstOrDefault(x => string.Equals(x.Id, PlanSumId, StringComparison.OrdinalIgnoreCase));
                try
                {
                    if (planSum == null)
                    {
                        throw new Exception("Selected plan sum was not found in scope.");
                    }

                    foreach (var plan in planSum.PlanSetups)
                    {
                        if (plan.Dose != null)
                        {
                            string date = ResolvePlanDate(plan);
                            string energy = "";
                            foreach (var beam in plan.Beams)
                            {
                                if (beam.IsSetupField) continue;
                                if (string.IsNullOrEmpty(energy)) energy = beam.EnergyModeDisplayName;
                                else if (!energy.Contains(beam.EnergyModeDisplayName)) energy += $", {beam.EnergyModeDisplayName}";
                            }
                            PlanList.Add((plan.Id, plan.Course.Id, plan.StructureSet.UID, false, (int)plan.NumberOfFractions, plan.TotalDose.Dose, date, energy));
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = "Error findings plans in selected Plan Sum.";
                    Helpers.SeriLog.LogError(errorMessage, ex);
                    throw new Exception(errorMessage);
                }
            });
            return PlanList;
        }

        // A shared treatment/planning-approval and plan-creation timestamp can be
        // off by date-string rounding once parsed, so treat approval and creation
        // as the "same" event when they fall within this tolerance.
        private static readonly TimeSpan ApprovalCreationTolerance = TimeSpan.FromMinutes(1);

        // Resolves the plan's treatment date used for elapsed-time/recency
        // calculations.
        //
        // Primary rule: a treatment approval (or, failing that, a planning
        // approval) whose timestamp differs from the plan creation time is a
        // genuine approval event and the best estimate of the plan date — better
        // than the CT. Imported/auto-approved plans have their approval stamped
        // at creation time, so an approval that matches plan creation (or no
        // approval at all) is uninformative; in that case we fall back to the
        // CT-anchored logic below.
        private static string ResolvePlanDate(PlanSetup plan)
        {
            DateTime? approval = ParseDate(plan.TreatmentApprovalDate) ?? ParseDate(plan.PlanningApprovalDate);
            DateTime? creation = plan.CreationDateTime;

            bool approvalIsDistinct = approval.HasValue &&
                (!creation.HasValue ||
                 (approval.Value - creation.Value).Duration() > ApprovalCreationTolerance);

            if (approvalIsDistinct)
                return approval.Value.ToString();

            return ResolveCtAnchoredDate(plan);
        }

        // Fallback when no distinct approval exists. The CT (structure-set image)
        // creation date is the trusted default: imported plans frequently carry
        // treatment/approval/creation dates shifted from the originating system.
        // We only supersede the CT date with a plan date — treatment approval,
        // then planning approval, then plan creation, in that order of importance
        // — when that plan date falls within 3 months of the CT creation date.
        // Anything further out is assumed to be a bad imported date and is
        // ignored, so the CT creation date stands.
        private static string ResolveCtAnchoredDate(PlanSetup plan)
        {
            DateTime? ctDate = plan.StructureSet?.Image?.CreationDateTime;

            if (ctDate.HasValue)
            {
                DateTime windowStart = ctDate.Value.AddMonths(-3);
                DateTime windowEnd   = ctDate.Value.AddMonths(3);

                var candidates = new[]
                {
                    ParseDate(plan.TreatmentApprovalDate),
                    ParseDate(plan.PlanningApprovalDate),
                    plan.CreationDateTime,
                };
                foreach (var candidate in candidates)
                {
                    if (candidate.HasValue &&
                        candidate.Value >= windowStart && candidate.Value <= windowEnd)
                    {
                        return candidate.Value.ToString();
                    }
                }

                return ctDate.Value.ToString();
            }

            // No CT date to anchor against: fall back to the best available
            // plan date in order of importance.
            if (!string.IsNullOrEmpty(plan.TreatmentApprovalDate)) return plan.TreatmentApprovalDate;
            if (!string.IsNullOrEmpty(plan.PlanningApprovalDate)) return plan.PlanningApprovalDate;
            return ((DateTime)plan.CreationDateTime).ToString();
        }

        private static DateTime? ParseDate(string s)
            => DateTime.TryParse(s, out var d) ? d : (DateTime?)null;


        public async Task<bool> InitializeModel()
        {
            try
            {
                await _ew.AsyncRunPlanSumContext((pat, ps) =>
                {
                    pat.BeginModifications();
                });

                return true;
            }
            catch (Exception ex)
            {
                string errorMessage = "Error during initialization.";
                Helpers.SeriLog.LogFatal(errorMessage, ex);
                throw new Exception(errorMessage);
            }
        }

        public async Task<List<StructureViewModel>> GetStructureDefinitions(string ssId = null)
        {
            if (ssId == null)
                return StructureDefinitions.ToList();
            else
            {
                StructureDefinitions.Clear();
                await _ew.AsyncRunPlanSumContext((pat, ps) =>
                {
                    var ssOverride = pat.StructureSets.FirstOrDefault(s => s.UID == ssId);
                    foreach (var structure in ssOverride.Structures.Where(x => !x.IsEmpty))
                    {
                        // First, try to find an exact alias match
                        var matchingStructure = _config.FindByAlias(structure.Id);

                        // If no exact match, try partial match (substring on the
                        // underscore-stripped ESAPI id).
                        if (matchingStructure == null)
                        {
                            var sidNorm = structure.Id.Replace("_", "");
                            matchingStructure = _config.Structures.Values.FirstOrDefault(x =>
                                x.Aliases.Any(y => sidNorm.IndexOf(y.StructureId.Replace("_", ""), StringComparison.OrdinalIgnoreCase) >= 0));
                        }

                        if (matchingStructure != null)
                        {
                            StructureDefinitions.Add(new StructureViewModel(this, structure.Id, matchingStructure.AlphaBetaRatio, matchingStructure.StructureLabel, matchingStructure.Discounts, false));
                        }
                        else
                        {
                            StructureDefinitions.Add(new StructureViewModel(this, structure.Id, DefaultAlphaBeta, "", null, false));
                        }
                    }

                    SelectBodyByPriority();
                });
            }

            return StructureDefinitions;
        }

        // Picks exactly one body contour from candidates aliased to "Body"
        // (which may include the patient's Body, External, Skin, etc.) per
        // the configured priority list. Demotes losing candidates by
        // clearing their canonical label so the rest of the system treats
        // them as plain OARs; users can manually re-label them if desired.
        private void SelectBodyByPriority()
        {
            var bodyCandidates = StructureDefinitions
                .Where(sv => string.Equals(sv.StructureLabel, "Body", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bodyCandidates.Count <= 1) return;

            var winner = PickBodyWinner(
                bodyCandidates, _config.Defaults.BodyContourPriority ?? new List<string>());

            foreach (var loser in bodyCandidates)
            {
                if (loser == winner) continue;
                loser.StructureLabel = "";
            }
        }

        // Chooses the body contour from several "Body"-aliased candidates.
        // An EXACT id match to a priority name (case-insensitive, underscores
        // ignored) is always preferred over a mere substring match, so a real
        // "Skin" beats a helper like "TS_SKIN_DOSESHEL". The priority list order
        // is honoured WITHIN each tier — an exact "Body"/"External" still wins
        // over an exact "Skin" — and candidate order breaks any remaining tie.
        // Falls back to the first candidate when nothing matches. Pure (no
        // ESAPI) so the precedence is unit-tested.
        internal static StructureViewModel PickBodyWinner(
            IReadOnlyList<StructureViewModel> bodyCandidates, IReadOnlyList<string> priority)
        {
            if (bodyCandidates == null || bodyCandidates.Count == 0) return null;
            return MatchBodyCandidate(bodyCandidates, priority, exactOnly: true)
                ?? MatchBodyCandidate(bodyCandidates, priority, exactOnly: false)
                ?? bodyCandidates[0];
        }

        // First candidate matching the highest-priority name. Names are scanned
        // in priority order; exactOnly compares the whole normalised id to the
        // name, otherwise the name may appear anywhere in the id (substring).
        private static StructureViewModel MatchBodyCandidate(
            IReadOnlyList<StructureViewModel> candidates, IReadOnlyList<string> priority, bool exactOnly)
        {
            if (priority == null) return null;
            foreach (var name in priority)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var key = name.Replace("_", "");
                var match = candidates.FirstOrDefault(sv =>
                {
                    var id = (sv.StructureId ?? "").Replace("_", "");
                    return exactOnly
                        ? string.Equals(id, key, StringComparison.OrdinalIgnoreCase)
                        : id.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
                });
                if (match != null) return match;
            }
            return null;
        }
        public int[,,] GetDoseVoxelsFromDose(Dose dose)
        {
            int Xsize = dose.XSize;
            int Ysize = dose.YSize;
            int Zsize = dose.ZSize;

            int[,,] doseMatrix = new int[Zsize, Xsize, Ysize];

            // Get whole dose matrix from context
            for (int k = 0; k < Zsize; k++)
            {
                int[,] plane = new int[Xsize, Ysize];
                dose.GetVoxels(k, plane);

                for (int i = 0; i < Xsize; i++)
                {
                    for (int j = 0; j < Ysize; j++)
                    {
                        doseMatrix[k, i, j] = plane[i, j];
                    }
                }
            }
            return doseMatrix;
        }

        public double GetMaxDoseVal(Dose dose, PlanningItem source)
        {
            DoseValue maxDose = dose.DoseMax3D;
            double maxDoseVal = maxDose.Dose;

            PlanSetup plan = source as PlanSetup;
            if (plan != null)
            {
                if (maxDose.IsRelativeDoseValue)
                {
                    if (plan.TotalDose.Unit == DoseValue.DoseUnit.cGy)
                    {
                        maxDoseVal = maxDoseVal * plan.TotalDose.Dose / 10000.0;
                    }
                    else
                    {
                        maxDoseVal = maxDoseVal * plan.TotalDose.Dose / 100.0;
                    }
                }
            }
            if (maxDose.Unit == DoseValue.DoseUnit.cGy)
            {
                maxDoseVal = maxDoseVal / 100.0;
            }
            return maxDoseVal;
        }

        // ----- Conservative maximum dose accumulation (Paradis et al.) -----------

        /// <summary>
        /// Patient identity for the conservative report header
        /// (first, last, id, dob, hospital).
        /// </summary>
        public async Task<(string First, string Last, string Id, string Dob, string Hospital)> GetPatientHeader()
        {
            string first = "", last = "", id = "", dob = "", hospital = "";
            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                first = p.FirstName;
                last = p.LastName;
                id = p.Id;
                if (p.DateOfBirth != null) dob = ((DateTime)p.DateOfBirth).ToString("MM/dd/yyyy");
                if (p.Hospital != null) hospital = p.Hospital.Name;
            });
            return (first, last, id, dob, hospital);
        }

        /// <summary>
        /// For each component plan of the selected sum, the non-empty ROI ids in
        /// that plan's structure set, keyed by "courseId/planId". The naming
        /// differs across plans, which is exactly why the user must pick the
        /// matching ROI per plan.
        /// </summary>
        public async Task<Dictionary<string, List<string>>> GetPlanRoiNames(string planSumId)
        {
            var result = new Dictionary<string, List<string>>();
            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                var planSum = ps.FirstOrDefault(x => string.Equals(x.Id, planSumId, StringComparison.OrdinalIgnoreCase));
                if (planSum == null) return;
                foreach (var plan in planSum.PlanSetups)
                {
                    string key = plan.Course.Id + "/" + plan.Id;
                    var names = plan.StructureSet.Structures
                        .Where(s => !s.IsEmpty)
                        .Select(s => s.Id)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    result[key] = names;
                }
            });
            return result;
        }

        /// <summary>
        /// Run the conservative maximum-dose accumulation. For each plan, the chosen
        /// target (which lives in the plan-sum frame) is mapped into the plan's frame
        /// through the plan-&gt;sum registration, and the near-max (D0.1cc) of the plan's
        /// dose over the OAR within an anisotropic (in-plane / sup-inf) expansion of that
        /// target — or over the whole OAR when global evaluation is selected — is read from
        /// the dose voxels (MaskedVoxelSampler); boundary voxels are partial-volume weighted
        /// so the result tracks Eclipse's DVH. Each near-max is converted to EQD2 and
        /// summed across plans by ConservativeMaxAccumulator (EQD2 + delivered/planned
        /// scaling + discount + sum); per-plan lists in each request are aligned to the
        /// plan sum's PlanSetups order. A missing registration (non-global) or dose is raised
        /// to the caller; an OAR with no dose in the region counts as 0. The flow is read-only
        /// (no structures or plans are created).
        /// </summary>
        public async Task<List<ConservativeResultRow>> GetConservativeMaxDose(
            string planSumId,
            List<ConservativeStructureRequest> rows,
            Dictionary<string, ViewModel.PlanEdit> plansDict,
            string targetRoiId,
            double inPlaneMarginMm,
            double supInfMarginMm,
            bool globalEvaluation)
        {
            var results = new List<ConservativeResultRow>();
            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                var planSum = ps.FirstOrDefault(x => string.Equals(x.Id, planSumId, StringComparison.OrdinalIgnoreCase));
                if (planSum == null)
                    throw new Exception("Selected plan sum was not found in scope.");

                // Global evaluation reports each OAR's whole-structure near-max, so no
                // target (and no registration) is needed.
                Structure target = null;
                if (!globalEvaluation)
                {
                    target = planSum.StructureSet?.Structures.FirstOrDefault(
                        s => string.Equals(s.Id, targetRoiId, StringComparison.InvariantCultureIgnoreCase) && !s.IsEmpty);
                    if (target == null)
                        throw new Exception($"Target structure '{targetRoiId}' was not found (or is empty) in the plan sum structure set.");
                }

                string planSumFOR = planSum.StructureSet.Image.FOR;
                var planList = planSum.PlanSetups.ToList();

                // Per-plan dose grid, plan->sum registration and target-expansion gate,
                // prepared once and reused across every analysis structure. A missing
                // registration or dose is a hard error — the method needs both to
                // evaluate the region.
                var ctx = new List<(int[,,] matrix, double voxelToGy, RegistrationAdapter reg)>();
                var gates = new List<TargetExpansionGate>();
                foreach (var plan in planList)
                {
                    bool sameFrame = plan.Series.FOR == planSumFOR;
                    var reg = (globalEvaluation || sameFrame) ? null : DoseConversionPipeline.FindRegistration(plan, planSumFOR);
                    if (!globalEvaluation && !sameFrame && reg == null)
                        throw new Exception(
                            $"No registration was found between plan '{plan.Id}' (frame of reference {plan.Series.FOR}) " +
                            "and the plan sum. A registration is required for the conservative maximum-dose method.");
                    if (plan.Dose == null)
                        throw new Exception($"Plan '{plan.Id}' has no calculated dose; it cannot be evaluated.");

                    var matrix = GetDoseVoxelsFromDose(plan.Dose);
                    double maxDoseVal = GetMaxDoseVal(plan.Dose, plan);
                    var mm = Helpers.GetMinMaxValues(matrix, plan.Dose.XSize, plan.Dose.YSize, plan.Dose.ZSize);
                    double voxelToGy = mm.Item2 != 0 ? maxDoseVal / mm.Item2 : 0.0;
                    if (voxelToGy <= 0.0)
                        throw new Exception($"Could not scale the dose grid for plan '{plan.Id}'.");
                    ctx.Add((matrix, voxelToGy, reg));
                    gates.Add(globalEvaluation ? null
                        : TargetExpansionGate.Build(plan.Dose, target, inPlaneMarginMm, supInfMarginMm,
                            reg, ConservativeMaskSupersample));
                }

                foreach (var row in rows)
                {
                    var input = new ConservativeStructureInput
                    {
                        Label = row.Label,
                        AlphaBeta = row.AlphaBeta,
                    };

                    for (int pi = 0; pi < planList.Count; pi++)
                    {
                        var plan = planList[pi];
                        string roiId = pi < row.PerPlanRoiId.Count ? row.PerPlanRoiId[pi] : null;
                        double discount = pi < row.PerPlanDiscountPercent.Count ? row.PerPlanDiscountPercent[pi] : 0.0;

                        string planKey = plan.Course.Id + "/" + plan.Id;
                        plansDict.TryGetValue(planKey, out var edit);
                        int delivered = edit?.Fraction ?? (plan.NumberOfFractions ?? 0);
                        int planned = edit?.PlannedFraction ?? (plan.NumberOfFractions ?? 0);

                        var planInput = new ConservativePlanInput
                        {
                            Present = false,
                            DeliveredFractions = delivered,
                            PlannedFractions = planned,
                            DiscountPercent = discount,
                        };

                        if (!string.IsNullOrEmpty(roiId))
                        {
                            var oar = plan.StructureSet.Structures.FirstOrDefault(
                                s => string.Equals(s.Id, roiId, StringComparison.InvariantCultureIgnoreCase) && !s.IsEmpty);
                            if (oar != null && oar.Volume > 0.0)
                            {
                                var c = ctx[pi];
                                double d01 = MaskedVoxelSampler.D01ccWithinTargetMargin(
                                    plan.Dose, c.matrix, c.voxelToGy, oar, gates[pi],
                                    globalEvaluation, ConservativeSupersample);
                                planInput.PhysicalDoseGy = d01;
                                planInput.Present = true;
                                Helpers.SeriLog.LogInfo(
                                    $"[Conservative] plan='{plan.Id}' roi='{roiId}': oarVol={oar.Volume:0.##}cc d01Gy={d01:0.###}");
                            }
                        }

                        input.Plans.Add(planInput);
                    }

                    results.Add(ConservativeMaxAccumulator.Accumulate(input));
                }
            });
            return results;
        }

        // Sub-voxel sampling factor for the conservative near-max boundary handling
        // (S^3 sub-points per voxel). Higher converges closer to Eclipse's DVH at more
        // cost; the OAR ∩ target-margin region is small, so this stays cheap.
        private const int ConservativeSupersample = 3;

        // Refinement of the target-expansion gate grid (TargetExpansionGate.Build):
        // 1 gates whole dose voxels at their centres; ConservativeSupersample aligns
        // the gate with the scoring sub-points (partial-volume margin boundary), which
        // validated closest to an Eclipse-expanded target.
        private const int ConservativeMaskSupersample = 3;

        /// <summary>ROI ids in the plan sum's (primary) structure set, for the
        /// conservative max-dose target selector.</summary>
        public async Task<List<string>> GetTargetRoiNames(string planSumId)
        {
            var names = new List<string>();
            await _ew.AsyncRunPlanSumContext((p, ps) =>
            {
                var planSum = ps.FirstOrDefault(x => string.Equals(x.Id, planSumId, StringComparison.OrdinalIgnoreCase));
                if (planSum?.StructureSet == null) return;
                foreach (var s in planSum.StructureSet.Structures)
                    if (!s.IsEmpty) names.Add(s.Id);
            });
            return names;
        }


    }
}

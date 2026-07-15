// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;
using ReRT.Domain.DoseConversion;
using ReRT.Reporting;

namespace ReRT
{
    /// <summary>
    /// Drives the "Conservative max dose accumulation" view (Paradis et al.),
    /// hosted in-place inside MainWindow. Reuses the already-loaded Model /
    /// configs from the main ViewModel. Calculation builds temporary evaluation
    /// structures in the patient (Model enables modifications), reads each region's
    /// near-max from Eclipse's DVH, then removes the temporaries again. An optional
    /// <see cref="ConservativeSeed"/> pre-selects the plan sum and pre-adds the
    /// non-body structures already chosen in the main table.
    /// </summary>
    public class ConservativeViewModel : ObservableObject
    {
        private readonly Model _model;
        private readonly ReRTConfig _scriptConfig;
        private readonly InstitutionConfig _institutionConfig;
        private readonly Dispatcher _ui;
        private readonly ConservativeSeed _seed;

        // courseId/planId -> ROI option list (placeholder + ids + "(not present)")
        private Dictionary<string, ObservableCollection<string>> _planRoiOptions
            = new Dictionary<string, ObservableCollection<string>>();

        private List<ConservativeResultRow> _lastResults;

        // One-shot flags consumed on the first plan-sum load: reuse the rigid
        // view's plan instances, and apply the seeded structures / empty row.
        // Later plan-sum changes rebuild from fetched defaults and preserve rows.
        private bool _needSeedPlans;
        private bool _needInitialStructures;

        // Patient header (for the report + topbar).
        private string _patientFirst = "", _patientLast = "", _patientDob = "", _patientHospital = "";

        public ConservativeViewModel(Model model, ReRTConfig scriptConfig, InstitutionConfig institutionConfig,
            ConservativeSeed seed = null, DescriptionViewModel discountInfo = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _scriptConfig = scriptConfig ?? throw new ArgumentNullException(nameof(scriptConfig));
            _institutionConfig = institutionConfig ?? throw new ArgumentNullException(nameof(institutionConfig));
            _ui = Dispatcher.CurrentDispatcher;
            _seed = seed;
            DiscountInfo = discountInfo ?? new DescriptionViewModel("Default", "");
            if (!string.IsNullOrEmpty(seed?.EvaluationDate)) EvaluationDate = seed.EvaluationDate;

            StatusColor = new SolidColorBrush(Colors.Transparent);
            foreach (var label in _scriptConfig.RecoveryCurves.Keys.OrderBy(x => x))
                StructureTypeLabels.Add(label);
            // Make sure seeded labels are selectable even with no recovery curve.
            if (_seed?.Structures != null)
                foreach (var s in _seed.Structures)
                    if (!string.IsNullOrEmpty(s.Label) && !StructureTypeLabels.Contains(s.Label))
                        StructureTypeLabels.Add(s.Label);

            Initialize();
        }

        // ----- Top form -----------------------------------------------------

        public ObservableCollection<PlanSelectionViewModel> PlanInputOptions { get; }
            = new ObservableCollection<PlanSelectionViewModel>();

        private PlanSelectionViewModel _selectedInputOption;
        public PlanSelectionViewModel SelectedInputOption
        {
            get => _selectedInputOption;
            set
            {
                _selectedInputOption = value;
                RaisePropertyChangedEvent();
                if (_selectedInputOption != null && _selectedInputOption.IsSum)
                    UpdatePlanSum(_selectedInputOption.Id);
            }
        }

        public string PatientId { get; set; } = "";

        private string _physicianName = "";
        public string PhysicianName
        {
            get => _physicianName;
            set { _physicianName = value; RaisePropertyChangedEvent(); }
        }

        public string EvaluationDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

        // ----- Discount info guide (same PNG guide as the rigid view) --------

        // Help content shared with the main ViewModel (DescriptionView shows its
        // Description text above the ReRT_Default.png guide image).
        public DescriptionViewModel DiscountInfo { get; }

        // The popup binds TwoWay with StaysOpen=False, so clicking anywhere
        // outside it flips this back to false; the button only ever opens it.
        private bool _isDiscountInfoOpen;
        public bool IsDiscountInfoOpen
        {
            get => _isDiscountInfoOpen;
            set { _isDiscountInfoOpen = value; RaisePropertyChangedEvent(); }
        }

        // ----- Evaluation region (target + expansion) -----------------------

        // ROI ids from the plan sum's structure set; the near-max for each plan is
        // limited to the OAR within the expansion of the chosen target.
        public ObservableCollection<string> TargetRoiOptions { get; } = new ObservableCollection<string>();

        private string _selectedTargetRoi = "";
        public string SelectedTargetRoi
        {
            get => _selectedTargetRoi;
            set { _selectedTargetRoi = value ?? ""; RaisePropertyChangedEvent(); }
        }

        // Anisotropic expansion around the target, in cm (lateral / superior-inferior).
        // The view displays these rounded to 0.1 cm (StringFormat=0.#); the setters
        // store the same rounded value so every computation uses exactly the number
        // shown on screen (typing 0.86 and typing 0.9 must give the same result).
        private double _inPlaneMarginCm = 3.0;
        public double InPlaneMarginCm
        {
            get => _inPlaneMarginCm;
            set { _inPlaneMarginCm = Math.Round(value, 1, MidpointRounding.AwayFromZero); RaisePropertyChangedEvent(); }
        }

        private double _supInfMarginCm = 2.0;
        public double SupInfMarginCm
        {
            get => _supInfMarginCm;
            set { _supInfMarginCm = Math.Round(value, 1, MidpointRounding.AwayFromZero); RaisePropertyChangedEvent(); }
        }

        // When true, skip the target mask + expansion entirely and report each structure's
        // own global near-max (D0.1cc). Toggling it hides the expansion fields.
        private bool _globalEvaluation;
        public bool GlobalEvaluation
        {
            get => _globalEvaluation;
            set
            {
                _globalEvaluation = value;
                RaisePropertyChangedEvent();
                RaisePropertyChangedEvent(nameof(ShowExpansion));
                RaisePropertyChangedEvent(nameof(IsCalcReady));
            }
        }

        // Visibility helper for the in-plane / sup-inf expansion fields.
        public bool ShowExpansion => !GlobalEvaluation;

        // ----- Pre-plan (remainder budgeting) --------------------------------

        // When checked, the results table swaps the read-only UMichigan/SABR
        // columns for a single editable EQD2 constraint (UMichigan by default)
        // plus a remainder column: the physical dose still allowed at the new
        // plan's fractionation. Same arithmetic as the rigid view's pre-plan
        // branch (ReportParamsBuilder).
        private bool _prePlan;
        public bool PrePlan
        {
            get => _prePlan;
            set { _prePlan = value; RaisePropertyChangedEvent(); }
        }

        // New plan fraction count, kept as raw text so the box can sit empty
        // (highlighted red) until the user supplies it. Every keystroke pushes
        // the parsed value into the result rows so remainders hotload.
        private string _prePlanFractionation = "";
        public string PrePlanFractionation
        {
            get => _prePlanFractionation;
            set
            {
                _prePlanFractionation = value ?? "";
                RaisePropertyChangedEvent();
                foreach (var row in Results)
                    row.PrePlanFx = PrePlanFx;
            }
        }

        private int? PrePlanFx =>
            int.TryParse(_prePlanFractionation, out int fx) && fx > 0 ? fx : (int?)null;

        // ----- Section 1: plans (read-only) ---------------------------------

        public ObservableCollection<PlanSelectionViewModel> PlanList { get; }
            = new ObservableCollection<PlanSelectionViewModel>();

        public string PlanSumName => _selectedInputOption?.DisplayString ?? "";

        // Plan column headers reused by the structures + results tables.
        public ObservableCollection<PlanColumnHeader> PlanColumns { get; }
            = new ObservableCollection<PlanColumnHeader>();

        // ----- Section 2: analysis structures -------------------------------

        public ObservableCollection<string> StructureTypeLabels { get; } = new ObservableCollection<string>();

        public ObservableCollection<AnalysisStructureViewModel> AnalysisStructures { get; }
            = new ObservableCollection<AnalysisStructureViewModel>();

        // ----- Section 3: results -------------------------------------------

        public ObservableCollection<ResultRowViewModel> Results { get; }
            = new ObservableCollection<ResultRowViewModel>();

        public bool HasResults => Results.Count > 0;

        // True while a report is being generated and the viewer launched; gates the Export
        // button. A short minimum dead-time keeps it disabled even when generation is quick,
        // since the viewer launch (Process.Start) is fire-and-forget and can't be tracked.
        private bool _exporting;
        public bool Exporting
        {
            get => _exporting;
            set { _exporting = value; RaisePropertyChangedEvent(); RaisePropertyChangedEvent(nameof(CanExport)); }
        }

        public bool CanExport => HasResults && !Exporting;

        // Caption under the results, naming the target + expansion the near-max was
        // limited to (captured at calculation time).
        private string _resultsNote = "";
        public string ResultsNote { get => _resultsNote; set { _resultsNote = value; RaisePropertyChangedEvent(); } }

        // ----- Status -------------------------------------------------------

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChangedEvent(); } }

        private bool _busy;
        public bool Busy { get => _busy; set { _busy = value; RaisePropertyChangedEvent(); } }

        public SolidColorBrush StatusColor { get; set; }

        // ----- Init ---------------------------------------------------------

        private async void Initialize()
        {
            try
            {
                var plans = await _model.GetPlans();
                var (first, last, id, dob, hospital) = await _model.GetPatientHeader();
                var physician = await _model.GetPhysicianName();

                _ui.Invoke(() =>
                {
                    _patientFirst = first; _patientLast = last; _patientDob = dob; _patientHospital = hospital;
                    PatientId = id;
                    PhysicianName = physician;
                    RaisePropertyChangedEvent(nameof(PatientId));

                    foreach (var p in plans.Where(x => x.Item4))
                        PlanInputOptions.Add(new PlanSelectionViewModel(p.Item1, p.Item2, p.Item3, p.Item4, 0, 0));

                    // Pre-select the plan sum carried over from the main view; the
                    // first plan-sum load then reuses its plans and pre-adds structures.
                    _needSeedPlans = true;
                    _needInitialStructures = true;
                    PlanSelectionViewModel preselect = null;
                    if (_seed != null && !string.IsNullOrEmpty(_seed.PlanSumId))
                        preselect = PlanInputOptions.FirstOrDefault(o => o.Id == _seed.PlanSumId
                            && (string.IsNullOrEmpty(_seed.PlanSumCourseId) || o.CourseId == _seed.PlanSumCourseId));
                    SelectedInputOption = preselect ?? PlanInputOptions.FirstOrDefault();
                });
            }
            catch (Exception ex)
            {
                Helpers.SeriLog.LogError("Error initializing conservative max-dose window.", ex);
                StatusMessage = "Error loading plans — see log.";
            }
        }

        private async void UpdatePlanSum(string planSumId)
        {
            try
            {
                var components = await _model.GetPlanSumComponent(planSumId);
                var roiNames = await _model.GetPlanRoiNames(planSumId);
                var targetNames = await _model.GetTargetRoiNames(planSumId);

                _ui.Invoke(() =>
                {
                    foreach (var existing in PlanList)
                        existing.PropertyChanged -= Plan_PropertyChanged;
                    PlanList.Clear();
                    PlanColumns.Clear();
                    _planRoiOptions = new Dictionary<string, ObservableCollection<string>>();

                    TargetRoiOptions.Clear();
                    foreach (var n in targetNames) TargetRoiOptions.Add(n);
                    if (!TargetRoiOptions.Contains(SelectedTargetRoi)) SelectedTargetRoi = "";

                    // First load of the carried-over plan sum reuses the rigid view's
                    // plan instances so edited fractions/dose/dates stay consistent; a
                    // newly chosen plan sum is rebuilt from fetched defaults.
                    bool reuseShared = _needSeedPlans
                        && _seed?.SharedPlans != null && _seed.SharedPlans.Count > 0
                        && string.Equals(planSumId, _seed.PlanSumId, StringComparison.OrdinalIgnoreCase);
                    _needSeedPlans = false;

                    var plans = reuseShared
                        ? _seed.SharedPlans
                        : components.Select(c => new PlanSelectionViewModel(
                            c.Id, c.CourseId, c.SsId, c.IsSum, c.Fraction, c.TotalDose, c.Date, EvaluationDate) { Energy = c.Energy }).ToList();

                    int index = 1;
                    foreach (var vm in plans)
                    {
                        vm.PropertyChanged += Plan_PropertyChanged;
                        PlanList.Add(vm);
                        PlanColumns.Add(new PlanColumnHeader { Index = index, Name = vm.Id });

                        string key = vm.CourseId + "/" + vm.Id;
                        var options = new ObservableCollection<string> { "" };
                        if (roiNames.TryGetValue(key, out var names))
                            foreach (var n in names) options.Add(n);
                        options.Add(AnalysisStructureViewModel.NotPresent);
                        _planRoiOptions[key] = options;
                        index++;
                    }

                    SyncStructureRowsToPlans();
                    ApplyInitialStructures();
                    RaisePropertyChangedEvent(nameof(PlanSumName));
                    ClearResults();
                });
            }
            catch (Exception ex)
            {
                Helpers.SeriLog.LogError("Error loading plan sum components (conservative).", ex);
                StatusMessage = "Error loading plan sum — see log.";
            }
        }

        // ROI option lists in PlanList order, shared by every structure row.
        private List<ObservableCollection<string>> PerPlanRoiOptions()
        {
            return PlanList
                .Select(p => _planRoiOptions.TryGetValue(p.CourseId + "/" + p.Id, out var o)
                    ? o : new ObservableCollection<string> { "", AnalysisStructureViewModel.NotPresent })
                .ToList();
        }

        private void SyncStructureRowsToPlans()
        {
            var options = PerPlanRoiOptions();
            foreach (var row in AnalysisStructures)
            {
                row.SyncPlans(options);
                PrefillRois(row, overwrite: false);
            }
        }

        // Apply the seed once the first plan sum has loaded: pre-add the non-body
        // structures carried over from the main table, then guarantee at least one
        // (empty) row so the user always has somewhere to start.
        private void ApplyInitialStructures()
        {
            if (!_needInitialStructures) return;
            _needInitialStructures = false;

            if (_seed?.Structures != null)
                foreach (var s in _seed.Structures)
                    AnalysisStructures.Add(CreateStructureRow(s.Label, s.AlphaBeta, s.MatchHint));

            if (AnalysisStructures.Count == 0)
                AddStructure();

            RaisePropertyChangedEvent(nameof(IsCalcReady));
        }

        // ----- Commands -----------------------------------------------------

        public ICommand DiscountInfoCommand => new DelegateCommand(_ => IsDiscountInfoOpen = !IsDiscountInfoOpen);
        public ICommand AddStructureCommand => new DelegateCommand(_ => AddStructure());
        public ICommand RemoveStructureCommand => new DelegateCommand(r => RemoveStructure(r as AnalysisStructureViewModel));
        public ICommand FetchDiscountsCommand => new DelegateCommand(_ => FetchAllDiscounts());
        public ICommand CalculateCommand => new DelegateCommand(_ => Calculate());
        public ICommand ClearCommand => new DelegateCommand(_ => ClearResults());
        public ICommand ExportCommand => new DelegateCommand(_ => Export());

        private void AddStructure()
        {
            string defaultType = StructureTypeLabels.FirstOrDefault() ?? "";
            AnalysisStructures.Add(CreateStructureRow(defaultType, AlphaBetaForLabel(defaultType), null));
            RaisePropertyChangedEvent(nameof(IsCalcReady));
        }

        // Build a fully wired analysis-structure row: α/β + per-plan ROI cells with
        // a best-guess ROI pre-selected, discounts fetched from the recovery curve.
        private AnalysisStructureViewModel CreateStructureRow(string label, double alphaBeta, string matchHint)
        {
            var row = new AnalysisStructureViewModel(label, alphaBeta) { MatchHint = matchHint };
            row.TypeChanged += (s, e) => HandleStructureTypeChanged((AnalysisStructureViewModel)s);
            row.SyncPlans(PerPlanRoiOptions());
            PrefillRois(row, overwrite: true);
            FetchDiscountsFor(row);
            return row;
        }

        private void RemoveStructure(AnalysisStructureViewModel row)
        {
            if (row != null) AnalysisStructures.Remove(row);
            RaisePropertyChangedEvent(nameof(IsCalcReady));
        }

        // Not named On*Changed: that matches PropertyChanged.Fody's auto-hook
        // convention and trips an "unsupported signature" weaver warning. This is
        // a plain subscriber to the row's TypeChanged event, not a Fody hook.
        private void HandleStructureTypeChanged(AnalysisStructureViewModel row)
        {
            row.AlphaBetaRatio = AlphaBetaForLabel(row.TypeLabel);
            // The type was re-picked, so the original (seeded) match no longer
            // applies — re-guess each plan's ROI from the new type label.
            row.MatchHint = null;
            PrefillRois(row, overwrite: true);
            FetchDiscountsFor(row);
        }

        // Pre-select each plan's best-guess ROI for a structure row. With
        // overwrite=false only empty (not-yet-chosen) cells are filled, so user
        // selections survive a plan-sum change; overwrite=true re-evaluates every
        // cell (new row / changed type).
        private void PrefillRois(AnalysisStructureViewModel row, bool overwrite)
        {
            string hint = string.IsNullOrEmpty(row.MatchHint) ? row.TypeLabel : row.MatchHint;
            foreach (var cell in row.Plans)
            {
                if (!overwrite && !cell.IsRoiMissing) continue;
                string guess = BestGuessRoi(hint, cell.RoiOptions);
                if (!string.IsNullOrEmpty(guess)) cell.SelectedRoi = guess;
                else if (overwrite) cell.SelectedRoi = "";
            }
        }

        // Closest ROI name in a plan's structure set, reusing the Levenshtein
        // matcher used by the main structure table. Walks the ranked candidates
        // and returns the first confident match (substring or small edit
        // distance): the top rank alone is not enough, because a plan whose set
        // contains an unrelated but edit-distance-close name would shadow an
        // exact/substring match further down the list (e.g. "Bowel" for hint
        // "Bowel_Small"). Returns "" when no candidate is confident so the
        // amber "choose a ROI" prompt still appears for ambiguous cases.
        internal static string BestGuessRoi(string hint, ObservableCollection<string> options)
        {
            if (string.IsNullOrWhiteSpace(hint) || options == null) return "";
            var candidates = new ObservableCollection<string>(
                options.Where(o => !string.IsNullOrEmpty(o) && o != AnalysisStructureViewModel.NotPresent));
            if (candidates.Count == 0) return "";

            string b = NormalizeForMatch(hint);
            if (b.Length == 0) return "";

            foreach (var candidate in Helpers.sortOptions(hint, candidates))
            {
                string a = NormalizeForMatch(candidate);
                if (a.Length == 0) continue;
                if (a.Contains(b) || b.Contains(a)) return candidate;
                int dist = Helpers.LevenshteinDistance.Compute(a, b);
                if (dist <= Math.Max(2, b.Length / 3)) return candidate;
            }
            return "";
        }

        private static string NormalizeForMatch(string s) =>
            s.Replace("B_", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();

        private double AlphaBetaForLabel(string label)
        {
            if (!string.IsNullOrEmpty(label) &&
                _scriptConfig.Structures.TryGetValue(label, out var def))
                return def.AlphaBetaRatio;
            return _model.DefaultAlphaBeta;
        }

        // Editing a plan's date/months recomputes its MonthsSinceTreatment; hotload
        // the discounts from the recovery curves the same way the registration view
        // does, so the per-plan discount columns stay in sync without a button press.
        private void Plan_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanSelectionViewModel.MonthsSinceTreatment))
                RefetchAllDiscounts();
        }

        private void RefetchAllDiscounts()
        {
            foreach (var row in AnalysisStructures)
                FetchDiscountsFor(row);
        }

        private void FetchAllDiscounts()
        {
            RefetchAllDiscounts();
            StatusMessage = "Discounts fetched from recovery curves.";
        }

        // Auto-fill each plan's discount from the structure's recovery curve at
        // that plan's months-since-treatment (reuses DiscountCalculator).
        private void FetchDiscountsFor(AnalysisStructureViewModel row)
        {
            _scriptConfig.RecoveryCurves.TryGetValue(row.TypeLabel ?? "", out var curve);
            for (int i = 0; i < row.Plans.Count && i < PlanList.Count; i++)
                row.Plans[i].Discount = curve == null ? 0.0
                    : DiscountCalculator.Calculate(curve, PlanList[i].MonthsSinceTreatment);
        }

        public bool IsCalcReady
        {
            get
            {
                if (PlanList.Count < 1 || AnalysisStructures.Count < 1) return false;
                if (!GlobalEvaluation && (string.IsNullOrEmpty(SelectedTargetRoi) || InPlaneMarginCm < 0.0 || SupInfMarginCm < 0.0)) return false;
                return AnalysisStructures.All(r => r.Plans.Count == PlanList.Count
                    && r.Plans.All(c => !c.IsRoiMissing));
            }
        }

        // ----- Calculate ----------------------------------------------------

        private async void Calculate()
        {
            if (!GlobalEvaluation)
            {
                if (string.IsNullOrEmpty(SelectedTargetRoi))
                {
                    StatusMessage = "Choose a target structure first.";
                    return;
                }
                if (InPlaneMarginCm < 0.0 || SupInfMarginCm < 0.0)
                {
                    StatusMessage = "Expansions cannot be negative.";
                    return;
                }
            }
            if (!IsCalcReady)
            {
                StatusMessage = "Select a ROI (or \"not present\") for every plan first.";
                return;
            }

            Busy = true;
            StatusMessage = GlobalEvaluation
                ? "Computing each structure's global near-max EQD2…"
                : "Masking dose to the target region and computing near-max EQD2…";

            var requests = AnalysisStructures.Select(r => new ConservativeStructureRequest
            {
                Label = r.TypeLabel,
                AlphaBeta = r.AlphaBetaRatio,
                PerPlanRoiId = r.Plans.Select(c =>
                    c.SelectedRoi == AnalysisStructureViewModel.NotPresent ? null : c.SelectedRoi).ToList(),
                PerPlanDiscountPercent = r.Plans.Select(c => c.Discount).ToList(),
            }).ToList();

            var plansDict = BuildPlansDict();
            string planSumId = _selectedInputOption.Id;

            try
            {
                var rows = await _model.GetConservativeMaxDose(
                    planSumId, requests, plansDict, SelectedTargetRoi,
                    InPlaneMarginCm * 10.0, SupInfMarginCm * 10.0, GlobalEvaluation);
                _ui.Invoke(() =>
                {
                    _lastResults = rows;
                    Results.Clear();
                    int i = 1;
                    foreach (var r in rows)
                    {
                        _scriptConfig.Structures.TryGetValue(r.Label ?? "", out var def);
                        var constraint = NearMaxConstraintEvaluator.Evaluate(def?.Constraints, r.TotalEqd2Gy);
                        // Pre-plan default: the UMichigan Dmax limit, user-editable per row.
                        var dmax = def?.Constraints?.GetEvaluable("Michigan")
                            .FirstOrDefault(c => c.Metric == MetricType.Dmax);
                        Results.Add(new ResultRowViewModel(i++, r, constraint, dmax?.Limit, PrePlanFx));
                    }
                    ResultsNote = GlobalEvaluation
                        ? "Each structure's global near-max (D0.1cc) is reported — no target expansion."
                        : $"Only dose voxels within a {InPlaneMarginCm.ToString("0.#", CultureInfo.InvariantCulture)} cm in-plane / {SupInfMarginCm.ToString("0.#", CultureInfo.InvariantCulture)} cm sup-inf expansion of {SelectedTargetRoi} are considered.";
                    RaisePropertyChangedEvent(nameof(HasResults));
                    RaisePropertyChangedEvent(nameof(CanExport));
                    StatusMessage = $"Calculation complete · {rows.Count} structures · {PlanList.Count} plans";
                });
            }
            catch (Exception ex)
            {
                Helpers.SeriLog.LogError("Error during conservative max-dose calculation.", ex);
                StatusMessage = "Calculation error: " + ex.Message;
                MessageBox.Show(ex.Message, "Conservative maximum dose", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Busy = false;
            }
        }

        private Dictionary<string, ViewModel.PlanEdit> BuildPlansDict()
        {
            var dict = new Dictionary<string, ViewModel.PlanEdit>();
            foreach (var plan in PlanList)
            {
                string key = plan.CourseId + "/" + plan.Id;
                if (dict.ContainsKey(key)) continue;
                dict[key] = new ViewModel.PlanEdit
                {
                    Fraction = plan.Fraction,
                    PlannedFraction = plan.PlannedFraction,
                    TotalDose = plan.TotalDose,
                    ElapsedMonths = (int)Math.Round(plan.MonthsSinceTreatment),
                };
            }
            return dict;
        }

        private void ClearResults()
        {
            Results.Clear();
            _lastResults = null;
            RaisePropertyChangedEvent(nameof(HasResults));
            RaisePropertyChangedEvent(nameof(CanExport));
            StatusMessage = "Ready";
        }

        // ----- Export -------------------------------------------------------

        private async void Export()
        {
            if (Exporting) return;
            if (_lastResults == null || _lastResults.Count == 0)
            {
                StatusMessage = "Nothing to export — run Calculate first.";
                return;
            }

            Exporting = true;
            StatusMessage = "Generating report…";
            var start = DateTime.UtcNow;
            try
            {
                var rp = new ConservativeReportParams
                {
                    PatientFirstName = _patientFirst,
                    PatientLastName = _patientLast,
                    PatientId = PatientId,
                    PatientDateOfBirth = _patientDob,
                    Hospital = _patientHospital,
                    PhysicianName = PhysicianName,
                    Date = DateTime.Now.ToString("MM/dd/yyyy HH:mm"),
                    TargetName = SelectedTargetRoi,
                    InPlaneMarginCm = InPlaneMarginCm,
                    SupInfMarginCm = SupInfMarginCm,
                    GlobalEvaluation = GlobalEvaluation,
                    Plans = PlanList.ToList(),
                    Structures = _lastResults.Select(r =>
                    {
                        _scriptConfig.Structures.TryGetValue(r.Label ?? "", out var def);
                        return new ConservativeStructureResult
                        {
                            Label = r.Label,
                            AlphaBeta = r.AlphaBeta,
                            TotalEqd2Gy = r.TotalEqd2Gy,
                            PerPlanDiscount = r.Cells.Select(c =>
                                c.Present ? c.DiscountPercent.ToString("0", CultureInfo.InvariantCulture) : "").ToList(),
                            PerPlanEqd2 = r.Cells.Select(c =>
                                c.Present ? c.Eqd2Gy.ToString("0.00", CultureInfo.InvariantCulture) : "").ToList(),
                            Constraints = def?.Constraints,
                        };
                    }).ToList(),
                };
                string saved = await Task.Run(() => new ConservativeReportBuilder(_institutionConfig).Build(rp));
                StatusMessage = "Report saved: " + saved;
            }
            catch (Exception ex)
            {
                Helpers.SeriLog.LogError("Error exporting conservative report.", ex);
                StatusMessage = "Export error — see log.";
            }
            finally
            {
                // The viewer is launched fire-and-forget (Process.Start returns immediately),
                // so hold the button disabled for at least ~1 s rather than re-enabling
                // before the report has had a chance to open.
                double elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;
                if (elapsedMs < 1000) await Task.Delay(1000 - (int)elapsedMs);
                Exporting = false;
            }
        }
    }

    /// <summary>
    /// Snapshot handed from the main ViewModel when switching into the
    /// conservative view: which plan sum to pre-select and which structures
    /// (non-body, already included) to pre-add.
    /// </summary>
    public class ConservativeSeed
    {
        public string PlanSumId { get; set; }
        public string PlanSumCourseId { get; set; }
        public string EvaluationDate { get; set; }
        // The rigid view's current plan instances, reused so edited plan
        // parameters carry across when switching views for the same plan sum.
        public List<PlanSelectionViewModel> SharedPlans { get; set; }
        public List<ConservativeSeedStructure> Structures { get; set; } = new List<ConservativeSeedStructure>();
    }

    public class ConservativeSeedStructure
    {
        public string Label { get; set; }
        public double AlphaBeta { get; set; }
        // Original ROI name to match per-plan ROIs against (falls back to Label).
        public string MatchHint { get; set; }
    }

    public class PlanColumnHeader
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Label => "Plan " + Index;
    }

    /// <summary>Display wrapper around a domain ConservativeResultRow, with the
    /// near-max UMichigan/SABR comparison, a constraint-coded total, and the
    /// pre-plan remainder: an editable EQD2 constraint (UMichigan by default)
    /// converted to the physical dose still allowed at the new plan's
    /// fractionation.</summary>
    public class ResultRowViewModel : ObservableObject
    {
        private static readonly Brush MetBrush = Frozen(0xC6, 0xEF, 0xCE);
        private static readonly Brush ViolatedBrush = Frozen(0xFF, 0xC7, 0xCE);
        private static readonly Brush NeutralBrush = Frozen(0xD8, 0xED, 0xED);

        private readonly double _totalEqd2Gy;
        private readonly double _alphaBetaValue;

        public string Index { get; }
        public string Label { get; }
        public string AlphaBeta { get; }
        public string Total { get; }
        public string MichiganConstraint { get; }
        public string SabrConstraint { get; }

        // Met/violated color coding of the total cell. Follows the editable
        // constraint (which defaults to the UMichigan Dmax limit), so it
        // hotloads with every edit instead of staying frozen at calculation
        // time; neutral while the constraint box is empty or unparsable.
        private Brush _totalBrush = NeutralBrush;
        public Brush TotalBrush
        {
            get => _totalBrush;
            private set { _totalBrush = value; RaisePropertyChangedEvent(); }
        }

        public ObservableCollection<ResultCellViewModel> Cells { get; } = new ObservableCollection<ResultCellViewModel>();

        // Editable pre-plan EQD2 limit — number only; the "<" and "Gy" are
        // rendered outside the text box. Any edit recomputes the remainder
        // and the total cell's color coding.
        private string _constraintText;
        public string ConstraintText
        {
            get => _constraintText;
            set { _constraintText = value ?? ""; RaisePropertyChangedEvent(); Reevaluate(); }
        }

        // New plan fraction count, pushed in by the parent VM whenever the
        // pre-plan fractionation box changes.
        private int? _prePlanFx;
        public int? PrePlanFx
        {
            get => _prePlanFx;
            set { _prePlanFx = value; Reevaluate(); }
        }

        private string _remainder = "—";
        public string Remainder
        {
            get => _remainder;
            private set { _remainder = value; RaisePropertyChangedEvent(); }
        }

        private Brush _remainderBrush = Brushes.Transparent;
        public Brush RemainderBrush
        {
            get => _remainderBrush;
            private set { _remainderBrush = value; RaisePropertyChangedEvent(); }
        }

        public ResultRowViewModel(int index, ConservativeResultRow row, NearMaxConstraintEvaluator.Result constraint,
            double? defaultConstraintEqd2Gy = null, int? prePlanFx = null)
        {
            _totalEqd2Gy = row.TotalEqd2Gy;
            _alphaBetaValue = row.AlphaBeta;
            Index = index.ToString(CultureInfo.InvariantCulture);
            Label = row.Label;
            AlphaBeta = row.AlphaBeta.ToString("0.0", CultureInfo.InvariantCulture);
            Total = row.TotalEqd2Gy.ToString("0.00", CultureInfo.InvariantCulture) + " Gy";
            MichiganConstraint = constraint.MichiganDisplay;
            SabrConstraint = constraint.SabrDisplay;
            _constraintText = defaultConstraintEqd2Gy?.ToString(CultureInfo.InvariantCulture) ?? "";
            _prePlanFx = prePlanFx;
            Reevaluate();
            foreach (var c in row.Cells)
                Cells.Add(new ResultCellViewModel(c));
        }

        // Recompute everything derived from the editable constraint. The total
        // cell's met/violated color needs only the constraint (every Dmax
        // constraint in config is an inclusive "<" upper bound), so it hotloads
        // as soon as a number is typed, before any fractionation is entered.
        // The remainder — the physical dose at the new fractionation whose EQD2
        // equals what is left of the constraint budget, same arithmetic as the
        // rigid pre-plan branch in ReportParamsBuilder — additionally needs a
        // valid fraction count. It is clamped at zero, and flagged red, when
        // the evaluated EQD2 already exceeds the constraint; em-dash until
        // its inputs are usable.
        private void Reevaluate()
        {
            bool hasLimit = double.TryParse(_constraintText, out double limit);

            TotalBrush = !hasLimit ? NeutralBrush
                       : _totalEqd2Gy <= limit ? MetBrush : ViolatedBrush;

            if (!hasLimit || _prePlanFx == null)
            {
                Remainder = "—";
                RemainderBrush = Brushes.Transparent;
                return;
            }
            double remainingEqd2 = Math.Max(0.0, limit - _totalEqd2Gy);
            double physGy = DoseFormulas.Eqd2ToPhysical(remainingEqd2, _alphaBetaValue, _prePlanFx.Value);
            Remainder = physGy.ToString("0.00", CultureInfo.InvariantCulture) + " Gy";
            RemainderBrush = limit < _totalEqd2Gy ? ViolatedBrush : Brushes.Transparent;
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public class ResultCellViewModel
    {
        public string Eqd2 { get; }

        public ResultCellViewModel(ConservativeCell cell)
        {
            Eqd2 = cell.Present
                ? cell.Eqd2Gy.ToString("0.00", CultureInfo.InvariantCulture) + " Gy"
                : "—";
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using ESAPIScript;
using OxyPlot.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using VMS.TPS.Common.Model.API;
using GongSolutions;
using System.Net.Http.Headers;
using System.Windows.Media.Animation;
using System.Drawing;
using System.Runtime.Remoting.Messaging;
using System.Windows.Controls;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;
using ReRT.Logging;
using DomainConstraint = ReRT.Domain.Constraints.Constraint;

namespace ReRT
{
    public class PlanDiscountHeader
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public string PlanName { get; set; }
        public string PlanFullName { get; set; }
    }

    public class ViewModel : ObservableObject
    {
        private EsapiWorker _ew = null;
        private Dispatcher _ui = null;
        private string _dataPath = string.Empty;
        private Model _model;
        private ReRTConfig _scriptConfig;
        private InstitutionConfig _institutionConfig;
        private OnlineHelpDefinitions _onlineHelpDefinitions;
        public StructureViewModel SelectedMapping { get; set; } = new StructureViewModel() { StructureId = "Design", AlphaBetaRatio = 2.5, StructureLabel = "Design", Discounts = null };
        public ObservableCollection<StructureViewModel> StructureDefinitions { get; private set; } = new ObservableCollection<StructureViewModel>() { new StructureViewModel() { StructureId = "Design", AlphaBetaRatio = 2.5, StructureLabel = "Design", Discounts = null } };

        public ObservableCollection<string> StructureLabels { get; set; } = new ObservableCollection<string>() { "" };
        public ObservableCollection<string> RegistrationConfidenceOptions { get; set; } = new ObservableCollection<string>()
        {
            "Good local alignment", "Fair local alignment", "Other (please specify in report)",
        };

        public ObservableCollection<string> DoseTypes { get; set; } = new ObservableCollection<string>()
        {
            "Both", "EQD2", "Physical"
        };
        // Raw text of the "new plan fx" box. Empty = not yet entered: the field
        // starts blank (no default) and may be temporarily blanked while typing.
        private string newPlanFx = "";
        private Dictionary<string, string> ConvertParams = new Dictionary<string, string>()
        {
            { "registrationType", "Rigid" },
            { "doseType", "EQD2"},
            { "physicianName", "" },
            { "prePlan", ""},
            { "newPlanFx", ""}
        };

        private Dictionary<string, PlanEdit> PlansDict = new Dictionary<string, PlanEdit>();

        public class PlanEdit
        {
            public int Fraction { get; set; }

            public int PlannedFraction { get; set; }

            public double TotalDose { get; set; }

            public double DosePerFraction
            {
                get
                {
                    return TotalDose / PlannedFraction;
                }
            }

            public int ElapsedMonths { get; set; }
        }

        private bool prePlanAnalysis { get; set; } = false;
        public bool PrePlanAnalysis
        {
            get { return prePlanAnalysis; }
            set
            {
                prePlanAnalysis = value;
                ConvertParams["prePlan"] = prePlanAnalysis ? "pre" : "post";
                if (prePlanAnalysis)
                {
                    foreach (var structure in StructureDefinitions)
                    {
                        LoadConstraintsFromConfig(structure);
                    }
                }
                RaisePropertyChangedEvent(nameof(ConvertButtonEnabled));
            }
        }
        private string evaluationDate = DateTime.Now.ToString("yyyy-MM-dd");
        public string EvaluationDate
        {
            get
            {
                return evaluationDate;
            }
            set
            {
                evaluationDate = value;
                if (!string.IsNullOrEmpty(evaluationDate))
                {
                    DateTime evalDate;
                    if (DateTime.TryParse(evaluationDate, out evalDate))
                    {
                        foreach (var plan in PlanList)
                        {
                            plan.EvaluationDate = evaluationDate;
                        }
                    }
                }
                RaisePropertyChangedEvent(nameof(EvaluationDate));
                RaisePropertyChangedEvent(nameof(PlanList));
                RaisePropertyChangedEvent(nameof(PlanMonths));
            }
        }

        private string registrationType = "Rigid";

        public string RegistrationType
        {
            get
            {
                return registrationType;
            }
            set
            {
                registrationType = value;
                ConvertParams["registrationType"] = registrationType;
                RaisePropertyChangedEvent(nameof(RegistrationType));
            }
        }

        public string physicianName = "";

        public string PhysicianName
        {
            get
            {
                return physicianName;
            }
            set
            {
                physicianName = value;
                ConvertParams["physicianName"] = physicianName;
                RaisePropertyChangedEvent(nameof(PhysicianName));
            }
        }

        public string NewPlanFractionation
        {
            get
            {
                return newPlanFx;
            }
            set
            {
                // Accept either an empty box (user backspacing the field clear)
                // or a positive integer. Anything else (0, negatives, letters)
                // is rejected by keeping the previous text, so the box never
                // holds a meaningless value.
                if (string.IsNullOrEmpty(value))
                {
                    newPlanFx = "";
                }
                else if (int.TryParse(value, out int fx) && fx > 0)
                {
                    newPlanFx = value;
                }
                RaisePropertyChangedEvent(nameof(NewPlanFractionation));
                RaisePropertyChangedEvent(nameof(NewPlanFxValid));
                RaisePropertyChangedEvent(nameof(ConvertButtonEnabled));
            }
        }

        // True once the new-plan fx box holds a usable positive integer. Drives
        // the red "missing input" highlight and gates the Convert button.
        public bool NewPlanFxValid => int.TryParse(newPlanFx, out int fx) && fx > 0;

        public ObservableCollection<PlanSelectionViewModel> PlanInputOptions { get; private set; } = new ObservableCollection<PlanSelectionViewModel> { new PlanSelectionViewModel("designPlan", "designCourse", "designSS", false, 0, 0) };

        private ObservableCollection<PlanSelectionViewModel> planList = new ObservableCollection<PlanSelectionViewModel> { new PlanSelectionViewModel("designPlan", "designCourse", "designSS", false, 0, 0) };
        public ObservableCollection<PlanSelectionViewModel> PlanList
        {
            get => planList;
            set
            {
                planList = value;
                RaisePropertyChangedEvent(nameof(PlanList));
                PlanMonths.Clear();
                foreach (var plan in PlanList)
                {
                    PlanMonths.Add(plan.MonthsSinceTreatment);
                }
                UpdatePlanDiscountHeaders();
                UpdateStructureData();
            }
        }

        public ObservableCollection<PlanDiscountHeader> PlanDiscountHeaders { get; private set; } = new ObservableCollection<PlanDiscountHeader>();

        public void UpdatePlanDiscountHeaders()
        {
            PlanDiscountHeaders.Clear();
            int i = 1;
            foreach (var plan in PlanList)
            {
                PlanDiscountHeaders.Add(new PlanDiscountHeader
                {
                    Index = i,
                    Label = "Discount " + i,
                    PlanName = plan.Id,
                    PlanFullName = plan.DisplayString,
                });
                i++;
            }
            RaisePropertyChangedEvent(nameof(PlanDiscountHeaders));
        }

        public ObservableCollection<bool> EnabledDiscounts { get; set; } = new ObservableCollection<bool>();

        public ObservableCollection<string> PlanTableName { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> PlanTableDate { get; set; } = new ObservableCollection<string>();

        public ObservableCollection<double> PlanMonths { get; set; } = new ObservableCollection<double>();

        private PlanSelectionViewModel _selectedInputOption;

        // Default is EQD2-only; Physical is rarely needed now. "Both"/"Physical"
        // remain selectable in the dropdown (DoseTypes) for the cases that want them.
        private string selectedDoseType = "EQD2";

        public string SelectedDoseType
        {
            get => selectedDoseType;
            set
            {
                selectedDoseType = value;
                ConvertParams["doseType"] = selectedDoseType;
                RaisePropertyChangedEvent(nameof(SelectedDoseType));
            }
        }

        public bool NoFatalErrorOccurred
        {
            get
            {
                return !_fatalError;
            }
        }
        public PlanSelectionViewModel SelectedInputOption
        {
            get { return _selectedInputOption; }
            set
            {
                _selectedInputOption = value;
                if (_selectedInputOption.IsSum)
                {
                    UpdatePlanList(_selectedInputOption.Id);
                    UpdateStructureData(_selectedInputOption.SsId);
                }
                else
                {
                    DisplayScriptError("Selected plan is not a plan sum.");
                }
                RaisePropertyChangedEvent(nameof(StartButtonVisibility));
            }
        }

        public void UpdateEnabledDiscounts()
        {
            EnabledDiscounts.Clear();
            int planCount = PlanList.Count;
            for (int i = 0; i < planCount; i++)
            {
                EnabledDiscounts.Add(true);
            }
            for (int i = planCount; i < 10; i++)
            {
                EnabledDiscounts.Add(false);
            }
            RaisePropertyChangedEvent(nameof(EnabledDiscounts));
        }
        public bool DesignTime { get; private set; } = true;

        public string ConversionInputWarning { get; private set; } = "Design";
        public bool isDoseSelectionInfoOpen { get; set; } = false;

        public DescriptionViewModel DoseDescriptionViewModel { get; private set; } = new DescriptionViewModel("Default", "Design");

        private bool _conversionComplete = false;
        private bool _conversionInProgress = false;
        public Visibility StartButtonVisibility
        {
            get
            {
                if (_selectedInputOption != null
                    && !_conversionComplete
                    && !_conversionInProgress)
                    return Visibility.Visible;
                else
                    return Visibility.Collapsed;
            }
        }
        public bool isButtonEnabled { get; set; } = true;

        // Convert is blocked while a run is in progress and, in pre-plan mode,
        // until a valid new-plan fx has been entered (the field starts empty).
        public bool ConvertButtonEnabled =>
            isButtonEnabled && (!PrePlanAnalysis || NewPlanFxValid);

        public int SelectedIndex { get; set; }

        private bool _fatalError = false;
        public bool Working { get; set; } = true;
        public string StatusMessage { get; set; } = "Loading...";
        public string StatusDetails { get; set; } = "";
        public SolidColorBrush StatusColor { get; set; } = new SolidColorBrush(Colors.PapayaWhip);
        public Visibility SuccessVisibility { get; private set; } = Visibility.Collapsed;
        public Visibility WarningVisibility { get; private set; } = Visibility.Collapsed;
        public Visibility ErrorVisibility { get; private set; } = Visibility.Collapsed;

        public ViewModel() { }
        public ViewModel(EsapiWorker ew = null)
        {
            _ew = ew;
            _ui = Dispatcher.CurrentDispatcher;
            StatusColor = new SolidColorBrush(Colors.Transparent);
            ClearDesignParameters();
            Initialize();
        }

        private void ClearDesignParameters()
        {
            DesignTime = false;
            ConversionInputWarning = "";
            StructureDefinitions.Clear();
            PlanInputOptions.Clear();
        }

        public async void Initialize()
        {
            try
            {
                // Read Script Configuration
                Working = true;
                var AssemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(AssemblyLocation))
                    AssemblyLocation = AppDomain.CurrentDomain.BaseDirectory;
                var AssemblyPath = Path.GetDirectoryName(AssemblyLocation);
                _dataPath = AssemblyPath;
            }
            catch (Exception ex)
            {
                Helpers.SeriLog.LogError(string.Format("{0}\r\n{1}\r\n{2}", ex.Message, ex.InnerException, ex.StackTrace));
                DisplayScriptError("Error resolving script path, please contact your Eclipse administrator.", ex.Message, true);
                return;
            }
            try
            {
                _scriptConfig = YamlConfigLoader.Load(Path.Combine(_dataPath, "Configuration"));
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error loading script configuration file, please contact your Eclipse administrator.", ex.Message, true);
                return;
            }
            try
            {
                _institutionConfig = InstitutionConfigLoader.Load(Path.Combine(_dataPath, "Configuration"));
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error loading institution configuration file, please contact your Eclipse administrator.", ex.Message, true);
                return;
            }
            try
            {
                InitializeOnlineHelp();
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error loading online help file, please contact your Eclipse administrator.", ex.Message);
                return;
            }
            try
            {
                _model = new Model(_scriptConfig, _institutionConfig, _ew);
                await _model.InitializeModel();
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error initializing ESAPI, please contact your Eclipse administrator.", ex.Message, true);
                return;
            }
            // Get plans in context
            try
            {
                var plansInContext = await _model.GetPlans();
                _ui.Invoke(() =>
                {
                    foreach (var p in plansInContext)
                    {
                        if (p.Item4)
                        {
                            PlanInputOptions.Add(new PlanSelectionViewModel(p.Item1, p.Item2, p.Item3, p.Item4, 0, 0));
                        }
                    }
                    SelectedInputOption = PlanInputOptions.FirstOrDefault();
                    FetchAllDiscount();
                });
                PhysicianName = await _model.GetPhysicianName();
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error loading plans, please make sure that the plan sum is in context.");
                Helpers.SeriLog.LogError("Error details", ex);
                return;
            }
            // Get structures in plan
            try
            {
                InitializeLabels();
            }
            catch (Exception ex)
            {
                DisplayScriptError("Error loading structures from plan, please contact your Eclipse administrator.", ex.Message, true);
                return;
            }
            if (!_fatalError)
            {
                DisplayScriptReady();
                Working = false;
                Helpers.SeriLog.LogInfo("Initialization complete!");
            }
        }

        private void DisplayScriptComplete(string message = "Conversion complete!")
        {
            StatusMessage = message;
            StatusDetails = "No errors or warnings.";
            StatusColor = new SolidColorBrush(Colors.Transparent);
            _conversionComplete = true;
            Working = false;
            SuccessVisibility = Visibility.Visible;
            ErrorVisibility = Visibility.Collapsed;
            WarningVisibility = Visibility.Collapsed;
            RaisePropertyChangedEvent(nameof(StartButtonVisibility));
        }
        private void DisplayScriptWarning(string message, string details = "", bool fatalError = false)
        {
            StatusMessage = message;
            StatusDetails = details;
            _fatalError = fatalError;
            Working = false;
            _conversionComplete = false;
            ErrorVisibility = Visibility.Collapsed;
            SuccessVisibility = Visibility.Collapsed;
            WarningVisibility = Visibility.Visible;
            RaisePropertyChangedEvent(nameof(StartButtonVisibility));
        }
        private void DisplayScriptError(string message, string details = "", bool fatalError = false)
        {
            StatusMessage = message;
            StatusDetails = details;
            _fatalError = fatalError;
            Working = false;
            _conversionComplete = false;
            ErrorVisibility = Visibility.Visible;
            SuccessVisibility = Visibility.Collapsed;
            WarningVisibility = Visibility.Collapsed;
            RaisePropertyChangedEvent(nameof(StartButtonVisibility));
        }
        private void DisplayScriptReady()
        {
            StatusMessage = "Ready to convert...";
            Working = false;
            _conversionComplete = false;
            SuccessVisibility = Visibility.Collapsed;
            WarningVisibility = Visibility.Collapsed;
            ErrorVisibility = Visibility.Collapsed;
            isButtonEnabled = true;
            RaisePropertyChangedEvent(nameof(isButtonEnabled));
            RaisePropertyChangedEvent(nameof(ConvertButtonEnabled));
            RaisePropertyChangedEvent(nameof(StartButtonVisibility));
        }
        private void InitializeOnlineHelp()
        {
            try
            {
                XmlSerializer Ser = new XmlSerializer(typeof(OnlineHelpDefinitions));
                var helpFile = Path.Combine(_dataPath, @"OnlineHelp\OnlineHelp.xml");
                using (StreamReader help = new StreamReader(helpFile))
                {
                    try
                    {
                        _onlineHelpDefinitions = (OnlineHelpDefinitions)Ser.Deserialize(help);
                    }
                    catch (Exception ex)
                    {
                        Helpers.SeriLog.LogFatal(string.Format("Unable to deserialize online help file: {0}\r\n", helpFile), ex);
                        MessageBox.Show(string.Format("Unable to read online help file {0}\r\n\r\nDetails: {1}", helpFile, ex.InnerException));

                    }
                }

                DoseDescriptionViewModel = new DescriptionViewModel(_onlineHelpDefinitions.Definitions.FirstOrDefault(x => string.Equals(x.DefinitionId, "Source dose", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                string errorMesssage = "Unable to find/open online help file";
                Helpers.SeriLog.LogFatal(errorMesssage, ex);
                throw new Exception(errorMesssage);
            }
        }

        private void UpdatePlansDict()
        {
            foreach (var plan in PlanList)
            {
                string plan_key = plan.CourseId + "/" + plan.Id;
                PlansDict.Add(plan_key, new PlanEdit
                {
                    Fraction = plan.Fraction,
                    PlannedFraction = plan.PlannedFraction,
                    TotalDose = plan.TotalDose,
                    ElapsedMonths = (int)Math.Round((double)plan.MonthsSinceTreatment),
                }
                );
            }
        }

        private async void UpdatePlanList(string plansum_id)
        {
            try
            {
                var planList = await _model.GetPlanSumComponent(plansum_id);
                _ui.Invoke(() =>
                {
                    foreach (var p in PlanList)
                        p.PropertyChanged -= Plan_PropertyChanged;
                    PlanList.Clear();
                    foreach (var plan in planList)
                    {
                        var vm = new PlanSelectionViewModel(plan.Id, plan.CourseId, plan.SsId, plan.IsSum, plan.Fraction, plan.TotalDose, plan.Date, EvaluationDate) { Energy = plan.Energy };
                        vm.PropertyChanged += Plan_PropertyChanged;
                        PlanList.Add(vm);
                    }
                    UpdatePlanDiscountHeaders();
                });

            }
            catch (Exception ex)
            {
                string errorMessage = "Error updating plan list in UpdatePlanList()";
                Helpers.SeriLog.LogError(errorMessage, ex);
                throw new Exception(errorMessage);
            }
        }

        private async void UpdateStructureData(string ssId = null)
        {
            ssId = _selectedInputOption?.SsId;
            if (ssId == null)
            {
                return;
            }

            try
            {
                List<StructureViewModel> unsortedMappings = new List<StructureViewModel>();
                if (ssId != null)
                    unsortedMappings = await _model.GetStructureDefinitions(ssId);
                else
                    unsortedMappings = await _model.GetStructureDefinitions();
                _ui.Invoke(() =>
                {
                    foreach (var s in StructureDefinitions)
                        s.PropertyChanged -= Structure_PropertyChanged;
                    StructureDefinitions.Clear();
                    bool hasBody = false;
                    foreach (var mapping in unsortedMappings.OrderByDescending(x => x.Include).ThenBy(x => x.IsBody).ThenBy(x => x.StructureId))
                    {
                        var discounts = new List<double>();
                        foreach (var month in PlanMonths)
                        {
                            discounts.Add(0.0);
                        }
                        mapping.Discounts = discounts;
                        if (mapping.IsBody && !hasBody)
                        {
                            mapping.Include = true;
                            hasBody = true;
                            mapping.RegistrationConfidence = "N/A";
                        }
                        StructureDefinitions.Add(mapping);
                    }
                    // Reorder the StructureDefinitions collection
                    Func<StructureViewModel, object> structureLabelSortKey = x => string.IsNullOrEmpty(x.StructureLabel) ? "zzzzz" : x.StructureLabel;
                    StructureDefinitions = new ObservableCollection<StructureViewModel>(
                        StructureDefinitions.OrderByDescending(x => x.Include)
                        .ThenBy(x => x.IsBody)
                        .ThenBy(structureLabelSortKey)
                        .ThenBy(x => x.StructureId));

                    foreach (var s in StructureDefinitions)
                        s.PropertyChanged += Structure_PropertyChanged;

                    StructureDefinitions.CollectionChanged -= StructureDefinitions_CollectionChanged;
                    StructureDefinitions.CollectionChanged += StructureDefinitions_CollectionChanged;

                    ResortIncludedFirst();
                    UpdatePriorities();

                    FetchAllDiscount();
                    for (int i = 0; i < StructureDefinitions.Count; i++)
                    {
                        LoadConstraintsFromConfig(StructureDefinitions[i]);
                    }
                    RaisePropertyChangedEvent(nameof(IncludedStructureCount));
                    RaisePropertyChangedEvent(nameof(ExcludedStructureCount));
                });
            }
            catch (Exception ex)
            {
                string errorMessage = "Error updating structure mappings in UpdateMapping()";
                Helpers.SeriLog.LogError(errorMessage, ex);
                throw new Exception(errorMessage);
            }
        }
        public ICommand ConvertCommand
        {
            get
            {
                return new DelegateCommand(ConvertDose);
            }
        }

        // ----- Conservative max-dose (Paradis et al.) in-place view -----------
        // The "dose accumulation method" toggle in the top bar swaps MainWindow
        // in place to the conservative view (no registration). The first switch in
        // builds a ConservativeViewModel seeded from the current plan sum + the
        // non-body structures already included in the main table; after that the
        // same instance is kept alive while the view is hidden, so every edit,
        // calculation result, and selection survives switching back and forth.
        // The conservative view shows the same toggle, so picking "Rigid
        // registration" there flips UseConservativeMethod back to false and
        // restores the registration view.

        private bool _useConservativeMethod;
        public bool UseConservativeMethod
        {
            get => _useConservativeMethod;
            set
            {
                if (_useConservativeMethod == value) return;
                _useConservativeMethod = value;
                if (_useConservativeMethod && Conservative == null)
                    EnterConservativeView();
                RaisePropertyChangedEvent(nameof(UseConservativeMethod));
            }
        }

        private ConservativeViewModel _conservative;
        public ConservativeViewModel Conservative
        {
            get => _conservative;
            private set { _conservative = value; RaisePropertyChangedEvent(nameof(Conservative)); }
        }

        private void EnterConservativeView()
        {
            if (_model == null || _scriptConfig == null || _institutionConfig == null)
            {
                DisplayScriptWarning("Script is still initializing — please wait.");
                _useConservativeMethod = false;   // revert the toggle
                RaisePropertyChangedEvent(nameof(UseConservativeMethod));
                return;
            }
            Conservative = new ConservativeViewModel(_model, _scriptConfig, _institutionConfig,
                BuildConservativeSeed(), DoseDescriptionViewModel);
        }

        // Carry the current plan sum + the non-body structures already included in
        // the main table into the conservative view (deduped by structure type).
        private ConservativeSeed BuildConservativeSeed()
        {
            return new ConservativeSeed
            {
                PlanSumId = SelectedInputOption?.Id,
                PlanSumCourseId = SelectedInputOption?.CourseId,
                EvaluationDate = EvaluationDate,
                SharedPlans = PlanList.ToList(),
                Structures = StructureDefinitions
                    .Where(s => s.Include && !s.IsBody && !string.IsNullOrEmpty(s.StructureLabel))
                    .GroupBy(s => s.StructureLabel)
                    .Select(g => g.First())
                    .Select(s => new ConservativeSeedStructure
                    {
                        Label = s.StructureLabel,
                        AlphaBeta = s.AlphaBetaRatio,
                        MatchHint = s.StructureId,
                    })
                    .ToList(),
            };
        }

        // Copies typed Constraints + ExtraMetrics for the structure label from
        // the YAML-loaded ReRTConfig onto the StructureViewModel, plus derives
        // the display strings (Michigan/SABR/PHYS/PreConstraint) for
        // the report and PHYS-branch consumers in Model.cs.
        public void LoadConstraintsFromConfig(StructureViewModel structure)
        {
            StructureDefinition def = null;
            if (_scriptConfig != null && !string.IsNullOrEmpty(structure.StructureLabel))
                _scriptConfig.Structures.TryGetValue(structure.StructureLabel, out def);

            structure.Constraints = def?.Constraints ?? new ConstraintSet();
            structure.ExtraMetrics = def?.ExtraMetrics ?? new ExtraMetricSet();

            structure.Michigan = JoinForDisplay(structure.Constraints.GetForSet("Michigan"));
            structure.SABR = JoinForDisplay(structure.Constraints.GetForSet("SABR"));
            structure.PHYS = string.Join(";",
                structure.ExtraMetrics.GetForSet("PHYS").Select(em => em.ToLegacyString()));

            // PreConstraint: limit of the first Dmax constraint in Michigan.
            var dmax = structure.Constraints.GetEvaluable("Michigan")
                                            .FirstOrDefault(c => c.Metric == MetricType.Dmax);
            structure.PreConstraint = dmax != null ? dmax.Limit.ToString() : "";

            RaisePropertyChangedEvent(nameof(StructureDefinitions));
        }

        private static string JoinForDisplay(IReadOnlyList<DomainConstraint> constraints)
        {
            return string.Join(";", constraints.Select(c => c == null ? "NA" : c.ToString()));
        }

        private bool ValidateInputs()
        {
            if (SelectedDoseType == "Physical" && PrePlanAnalysis)
            {
                DisplayScriptWarning("Cannot do pre-plan analysis with physical dose.");
                return false;
            }

            if (PrePlanAnalysis && !NewPlanFxValid)
            {
                DisplayScriptWarning("Please enter the number of fractions for the new plan.");
                return false;
            }

            bool hasBody = false;
            foreach (var structure in StructureDefinitions)
            {
                if (!structure.Include)
                {
                    continue;
                }
                if (structure.IsBody)
                {
                    hasBody = true;
                }
                if (string.IsNullOrEmpty(structure.RegistrationConfidence))
                {
                    DisplayScriptWarning("Please select the registration confidence level for every selected structure");
                    return false;
                }
                if (string.IsNullOrEmpty(structure.StructureLabel))
                {
                    DisplayScriptWarning("Please select the structure type for every selected structure");
                    return false;
                }
            }

            if (StructureDefinitions.Count(x => x.Include) <= 1 && !hasBody)
            {
                DisplayScriptWarning("Please add at least one non-body structure and one body structure.");
                return false;
            }
            if (string.IsNullOrEmpty(PhysicianName))
            {
                DisplayScriptWarning("Please type the physician name");
                return false;
            }
            DisplayScriptReady();
            return true;
        }

        private async void ConvertDose(object param = null)
        {
            if (!ValidateInputs())
            {
                return;
            }

            Working = true;
            isButtonEnabled = false;
            _conversionInProgress = true;
            RaisePropertyChangedEvent(nameof(isButtonEnabled));
            RaisePropertyChangedEvent(nameof(ConvertButtonEnabled));
            RaisePropertyChangedEvent(nameof(StartButtonVisibility));
            SuccessVisibility = Visibility.Collapsed;
            ErrorVisibility = Visibility.Collapsed;
            StatusColor = new SolidColorBrush(Colors.Transparent);
            StatusMessage = "Converting...";
            ScriptStatus status = ScriptStatus.Incomplete;
            string returnMessage = "";
            string ExceptionMessage = "No further details.";

            foreach (var structure in StructureDefinitions)
            {
                LoadConstraintsFromConfig(structure);
            }
            UpdatePlansDict();

            ConvertParams["newPlanFx"] = newPlanFx;

            // One stopwatch wraps the whole user-visible run
            // (Convert click → ReportBuilder.Build returns inside the model).
            // The structured summary is written from `finally` so it survives
            // both happy-path and error paths.
            var metrics = new RunMetrics().WithUser(Helpers.SeriLog.CurrentUser);
            var startUtc = DateTime.UtcNow;
            try
            {
                (status, returnMessage, ExceptionMessage) = await _model.GetConvertedDose(SelectedInputOption.CourseId, SelectedInputOption.Id, SelectedInputOption.IsSum, StructureDefinitions.ToList(), ConvertParams, PlansDict);
                PopulateRunMetricsFromReport(metrics, _model.LastReportParams);
            }
            catch (Exception ex)
            {
                status = ScriptStatus.Error;
                ExceptionMessage = ex.Message;
            }
            finally
            {
                metrics.WithRuntime(startUtc, DateTime.UtcNow);
                StructuredLogger.WriteRunSummary(metrics);
            }
            switch (status)
            {
                case ScriptStatus.Complete:
                    DisplayScriptComplete(returnMessage);
                    break;
                case ScriptStatus.Error:
                    DisplayScriptError(returnMessage, ExceptionMessage);
                    MessageBox.Show(string.Format("Error details:\r\n{0}", ExceptionMessage));
                    break;
                case ScriptStatus.Warning:
                    DisplayScriptWarning(returnMessage);
                    break;
            }
        }

        private static void PopulateRunMetricsFromReport(RunMetrics metrics, Model.ReportParams rp)
        {
            if (rp == null) return;
            metrics.WithPatient(rp.PatientId);
            metrics.WithRegistrationType(rp.RegistrationType);
            metrics.WithPlanComposition(
                rp.PlanNames ?? new List<string>(),
                ConversionModesFor(rp.DoseType),
                ElapsedMonthsFromCourses(rp.CourseDict),
                rp.CourseDict?.Count ?? 0);
            if (rp.AllEvaluations != null)
            {
                foreach (var rec in rp.AllEvaluations)
                {
                    if (rec?.Result == null) continue;
                    metrics.RecordEvaluation(rec.Result, rec.StructureName);
                }
            }
        }

        private static IEnumerable<string> ConversionModesFor(string doseType)
        {
            if (string.Equals(doseType, "Both", StringComparison.OrdinalIgnoreCase))
                return new[] { "EQD2", "Physical" };
            if (string.Equals(doseType, "Physical", StringComparison.OrdinalIgnoreCase))
                return new[] { "Physical" };
            if (string.Equals(doseType, "EQD2", StringComparison.OrdinalIgnoreCase))
                return new[] { "EQD2" };
            return Enumerable.Empty<string>();
        }

        // CourseDict groups PlanParams by course; flatten into per-plan
        // elapsed-month integers in the order plans were added (parallel to
        // ReportParams.PlanNames). Non-numeric values are skipped — a stale
        // or unset ElapsedMonths is not worth logging as zero.
        private static IEnumerable<int> ElapsedMonthsFromCourses(
            Dictionary<string, List<Model.PlanParams>> courseDict)
        {
            if (courseDict == null) yield break;
            foreach (var kv in courseDict)
                foreach (var pp in kv.Value)
                    if (int.TryParse(pp.ElapsedMonths, out int months))
                        yield return months;
        }

        public ICommand DoseSelectionInfoButtonCommand
        {
            get
            {
                return new DelegateCommand(ToggleDoseSelectionInfo);
            }
        }

        private void ToggleDoseSelectionInfo(object param = null)
        {
            isDoseSelectionInfoOpen ^= true;
        }

        private void Plan_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanSelectionViewModel.MonthsSinceTreatment))
            {
                FetchAllDiscount();
            }
        }

        private void Structure_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StructureViewModel.Include))
            {
                ResortIncludedFirst();
                UpdatePriorities();
                RaisePropertyChangedEvent(nameof(IncludedStructureCount));
                RaisePropertyChangedEvent(nameof(ExcludedStructureCount));
            }
        }

        // Reorder StructureDefinitions so included non-body items are first,
        // then included body items, then excluded. Stable within each group.
        public void ResortIncludedFirst()
        {
            var current = StructureDefinitions.ToList();
            var desired = current
                .Select((s, i) => new { s, i })
                .OrderBy(x => x.s.Include ? 0 : 1)
                .ThenBy(x => x.s.IsBody ? 1 : 0)
                .ThenBy(x => x.i)
                .Select(x => x.s)
                .ToList();
            for (int target = 0; target < desired.Count; target++)
            {
                var item = desired[target];
                int currentIdx = StructureDefinitions.IndexOf(item);
                if (currentIdx != target)
                    StructureDefinitions.Move(currentIdx, target);
            }
        }

        public void UpdatePriorities()
        {
            for (int i = 0; i < StructureDefinitions.Count; i++)
                StructureDefinitions[i].Priority = i + 1;
        }

        private void StructureDefinitions_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdatePriorities();
        }

        public ICommand MoveStructureUpCommand => new DelegateCommand(p => MoveStructure(p, -1));
        public ICommand MoveStructureDownCommand => new DelegateCommand(p => MoveStructure(p, 1));

        private void MoveStructure(object param, int dir)
        {
            var s = param as StructureViewModel;
            if (s == null) return;
            int i = StructureDefinitions.IndexOf(s);
            if (i < 0) return;
            int j = i + dir;
            if (j < 0 || j >= StructureDefinitions.Count) return;
            // Don't allow a body item to move above non-body, or non-body to fall below body within included group.
            var moving = StructureDefinitions[i];
            var neighbor = StructureDefinitions[j];
            if (moving.Include && neighbor.Include)
            {
                if (!moving.IsBody && neighbor.IsBody && dir > 0) return; // non-body can't move below body
                if (moving.IsBody && !neighbor.IsBody && dir < 0) return; // body can't move above non-body
            }
            StructureDefinitions.Move(i, j);
            UpdatePriorities();
        }

        public int IncludedStructureCount
        {
            get { return StructureDefinitions.Count(s => s.Include); }
        }

        public int ExcludedStructureCount
        {
            get { return StructureDefinitions.Count(s => !s.Include); }
        }
        private double CalculateDiscount(string StructureLabel, double month)
        {
            if (_scriptConfig == null) return 0.0;
            if (!_scriptConfig.RecoveryCurves.TryGetValue(StructureLabel, out var curve) || curve == null)
                return 0.0;

            // Shared with ConservativeViewModel; see DiscountCalculator.
            return DiscountCalculator.Calculate(curve, month);
        }

        public void FetchDiscount(StructureViewModel structure)
        {
            structure.Discounts.Clear();
            var discounts = new List<double>();
            foreach (var month in PlanMonths)
            {
                discounts.Add(CalculateDiscount(structure.StructureLabel, month));
            }
            structure.Discounts = discounts;
            RaisePropertyChangedEvent(nameof(StructureDefinitions));
        }

        public void FetchAllDiscount()
        {
            // Update planList
            UpdateEnabledDiscounts();
            UpdatePlanDiscountHeaders();
            PlanMonths.Clear();
            foreach (var plan in PlanList)
            {
                PlanMonths.Add(plan.MonthsSinceTreatment);
            }
            for (int i = 0; i < StructureDefinitions.Count; i++)
            {
                StructureDefinitions[i].Discounts.Clear();
                var discounts = new List<double>();
                foreach (var month in PlanMonths)
                {
                    discounts.Add(CalculateDiscount(StructureDefinitions[i].StructureLabel, month));
                }
                StructureDefinitions[i].Discounts = discounts;
            }
        }
        public void InitializeLabels()
        {
            StructureLabels.Clear();
            StructureLabels = new ObservableCollection<string>();
            if (_scriptConfig != null)
            {
                foreach (var label in _scriptConfig.RecoveryCurves.Keys)
                {
                    if (!StructureLabels.Contains(label))
                        StructureLabels.Add(label);
                }
            }
            StructureLabels = new ObservableCollection<string>(StructureLabels.OrderBy(x => x));
            RaisePropertyChangedEvent(nameof(StructureLabels));
        }

        public void ZeroDiscount()
        {
            foreach (var structure in StructureDefinitions)
            {
                structure.Discounts = structure.Discounts.Select(x => 0.0).ToList();
            }
            RaisePropertyChangedEvent(nameof(StructureDefinitions));
        }

    }
}

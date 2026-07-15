// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using ReRT.Domain.Constraints;

namespace ReRT
{
    public class StructureViewModel : ObservableObject
    {
        private Model _model;

        private int _priority;
        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    RaisePropertyChangedEvent();
                }
            }
        }

        public double AlphaBetaRatio { get; set; }

        private List<double> _discounts = new List<double>();
        public List<double> Discounts
        {
            get => _discounts;
            set
            {
                _discounts = value ?? new List<double>();
                RebuildDiscountEntries();
                RaisePropertyChangedEvent();
                RaisePropertyChangedEvent(nameof(DiscountEntries));
            }
        }

        public ObservableCollection<DiscountEntryViewModel> DiscountEntries { get; } = new ObservableCollection<DiscountEntryViewModel>();

        public string Michigan { get; set; } = "";

        public string SABR { get; set; } = "";

        public string PHYS { get; set; } = "";
        public string PreConstraint { get; set; } = "";

        // Typed storage populated by ViewModel.LoadConstraintsFromConfig
        // from YamlConfigLoader. Constraints carry op + limit and are
        // evaluated; ExtraMetrics are display-only (PHYS branch).
        public ConstraintSet Constraints { get; set; } = new ConstraintSet();
        public ExtraMetricSet ExtraMetrics { get; set; } = new ExtraMetricSet();

        private string registrationConfidence;
        public string RegistrationConfidence
        {
            get
            {
                if (IsBody)
                    registrationConfidence = "N/A";
                return registrationConfidence;
            }
            set
            {
                registrationConfidence = value;
            }
        }

        public string StructureId { get; set; }

        public string StructureLabel { get; set; }

        public bool Include { get; set; } = false;
        public bool IsBody { get { return StructureLabel.Contains("Body"); } }
        public StructureViewModel() { }

        public StructureViewModel(Model model, string structureId, double alphaBetaRatio, string structureLabel, List<double> discounts = null, bool include = false)
        {
            _model = model;
            StructureId = structureId;
            AlphaBetaRatio = alphaBetaRatio;
            StructureLabel = structureLabel;
            Include = include;
            Discounts = discounts?.ToList() ?? new List<double>();

            Michigan = "";
            SABR = "";
            PHYS = "";

            registrationConfidence = "Good local alignment";
        }

        private void RebuildDiscountEntries()
        {
            DiscountEntries.Clear();
            for (int i = 0; i < _discounts.Count; i++)
            {
                int idx = i;
                DiscountEntries.Add(new DiscountEntryViewModel(idx, _discounts[idx], newValue =>
                {
                    if (idx >= 0 && idx < _discounts.Count)
                    {
                        _discounts[idx] = newValue;
                    }
                }));
            }
        }

        public ICommand ToggleIncludeCommand
        {
            get { return new DelegateCommand(ToggleInclude); }
        }

        public void ToggleInclude(object parameter = null)
        {
            Include = !Include;
            RaisePropertyChangedEvent(nameof(Include));
        }
    }

    public class DiscountEntryViewModel : ObservableObject
    {
        private readonly Action<double> _onValueChanged;
        private double _value;

        public int Index { get; }
        public string Header => $"Discount {Index + 1}";

        public double Value
        {
            get => _value;
            set
            {
                var clamped = value;
                if (double.IsNaN(clamped) || double.IsInfinity(clamped)) clamped = 0;
                if (clamped < 0) clamped = 0;
                if (clamped > 100) clamped = 100;
                _value = clamped;
                _onValueChanged?.Invoke(clamped);
                RaisePropertyChangedEvent();
            }
        }

        public DiscountEntryViewModel(int index, double value, Action<double> onValueChanged)
        {
            Index = index;
            _value = value;
            _onValueChanged = onValueChanged;
        }
    }
}

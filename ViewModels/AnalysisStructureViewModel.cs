// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReRT
{
    /// <summary>
    /// One analysis-structure row in the conservative max-dose window: a
    /// structure type + α/β, plus a per-plan ROI selection and discount. The
    /// per-plan cells grow/shrink with the selected plan sum.
    /// </summary>
    public class AnalysisStructureViewModel : ObservableObject
    {
        // Sentinel ROI selection meaning "this structure is not present in this
        // plan". Distinct from the empty placeholder (which is "not chosen yet").
        public const string NotPresent = "(not present)";

        // Optional ROI name used when auto-filling per-plan ROI selections (the
        // originating structure id when this row was seeded from the main table).
        // Falls back to TypeLabel when null/empty.
        public string MatchHint { get; set; }

        private string _typeLabel;
        public string TypeLabel
        {
            get => _typeLabel;
            set
            {
                if (_typeLabel != value)
                {
                    _typeLabel = value;
                    RaisePropertyChangedEvent();
                    TypeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Displayed with StringFormat=0.0; store the same one-decimal value so
        // the EQD2 conversion uses exactly the number shown on screen.
        private double _alphaBetaRatio;
        public double AlphaBetaRatio
        {
            get => _alphaBetaRatio;
            set { _alphaBetaRatio = Math.Round(value, 1, MidpointRounding.AwayFromZero); RaisePropertyChangedEvent(); }
        }

        /// <summary>Raised when the user picks a different structure type so the
        /// owning view model can refresh α/β and discounts from config.</summary>
        public event EventHandler TypeChanged;

        public ObservableCollection<AnalysisPlanCellViewModel> Plans { get; }
            = new ObservableCollection<AnalysisPlanCellViewModel>();

        public AnalysisStructureViewModel(string typeLabel, double alphaBetaRatio)
        {
            _typeLabel = typeLabel;
            // Through the property so seeded config values get the same
            // display rounding as user edits.
            AlphaBetaRatio = alphaBetaRatio;
        }

        /// <summary>
        /// Resize the per-plan cells to match the current plan sum, wiring each
        /// cell to that plan's ROI option list. Preserves existing selections
        /// and discounts where the index still exists.
        /// </summary>
        public void SyncPlans(IReadOnlyList<ObservableCollection<string>> perPlanRoiOptions)
        {
            int count = perPlanRoiOptions.Count;
            while (Plans.Count > count)
                Plans.RemoveAt(Plans.Count - 1);

            for (int i = 0; i < count; i++)
            {
                if (i < Plans.Count)
                    Plans[i].RoiOptions = perPlanRoiOptions[i];
                else
                    Plans.Add(new AnalysisPlanCellViewModel(i, perPlanRoiOptions[i]));
            }
        }
    }

    public class AnalysisPlanCellViewModel : ObservableObject
    {
        public int PlanIndex { get; }

        private ObservableCollection<string> _roiOptions;
        public ObservableCollection<string> RoiOptions
        {
            get => _roiOptions;
            set { _roiOptions = value; RaisePropertyChangedEvent(); }
        }

        private string _selectedRoi = "";
        public string SelectedRoi
        {
            get => _selectedRoi;
            set
            {
                _selectedRoi = value ?? "";
                RaisePropertyChangedEvent();
                RaisePropertyChangedEvent(nameof(IsRoiMissing));
            }
        }

        // Drives the amber "required-empty" highlight: a cell is missing only
        // when no choice has been made. "(not present)" is a deliberate choice.
        public bool IsRoiMissing => string.IsNullOrEmpty(SelectedRoi);

        private double _discount;
        public double Discount
        {
            get => _discount;
            set
            {
                var clamped = value;
                if (double.IsNaN(clamped) || double.IsInfinity(clamped)) clamped = 0;
                if (clamped < 0) clamped = 0;
                if (clamped > 100) clamped = 100;
                // Displayed with StringFormat=0 (whole percent); store the same
                // rounded value so the accumulation and the report use exactly
                // the number shown on screen.
                _discount = Math.Round(clamped, 0, MidpointRounding.AwayFromZero);
                RaisePropertyChangedEvent();
            }
        }

        public AnalysisPlanCellViewModel(int planIndex, ObservableCollection<string> roiOptions)
        {
            PlanIndex = planIndex;
            _roiOptions = roiOptions;
        }
    }
}

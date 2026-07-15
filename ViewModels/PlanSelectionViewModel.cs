// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using System;

namespace ReRT
{
    public class PlanSelectionViewModel : ObservableObject
    {
        public string Id { get; set; }
        public string CourseId { get; set; }
        public string SsId { get; set; }
        public bool IsSum { get; set; }

        private string _evaluationDate;

        public string EvaluationDate
        {
            get => _evaluationDate;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _evaluationDate = "";
                }
                // Update TreatmentDate and MonthsSinceTreatment if EvaluationDate is set
                else
                {
                    DateTime evalDate;
                    DateTime treatmentDate;
                    if (DateTime.TryParse(value, out evalDate))
                    {
                        if (DateTime.TryParse(TreatmentDate, out treatmentDate))
                        {
                            MonthsSinceTreatment = Math.Round((evalDate - treatmentDate).TotalDays / 30.0, 1);
                        }
                    }
                    else
                    {
                        MonthsSinceTreatment = 0.0;
                    }
                    _evaluationDate = value;
                }
            }
        }
        private string _treatmentDate;
        public string TreatmentDate
        {
            get => _treatmentDate;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _treatmentDate = "";
                }
                else
                {
                    DateTime date;
                    DateTime evalDate;
                    if (DateTime.TryParse(value, out date))
                    {
                        if (DateTime.TryParse(EvaluationDate, out evalDate))
                        {
                            MonthsSinceTreatment = Math.Round((evalDate - date).TotalDays / 30.0, 1);
                        }
                    }
                    else
                    {
                        MonthsSinceTreatment = 0.0;
                    }
                    _treatmentDate = value;
                }
            }
        }
        public string DisplayString
        {
            get
            {
                if (IsSum)
                    return string.Format(@"{0}/{1} [sum]", CourseId, Id);
                else
                    return string.Format(@"{0}/{1}", CourseId, Id);
            }
        }

        private double _monthsSinceTreatment;
        public double MonthsSinceTreatment
        {
            get => _monthsSinceTreatment;
            set
            {
                // Displayed with StringFormat=N1 in both plan tables; store the
                // same one-decimal value so discount lookups use exactly the
                // number shown on screen.
                _monthsSinceTreatment = Math.Round(Math.Max(0.0, value), 1, MidpointRounding.AwayFromZero);
                RaisePropertyChangedEvent(nameof(MonthsSinceTreatment));
            }
        }

        private int _fraction;

        public int Fraction
        {
            get => _fraction;
            set
            {
                if (value < 0)
                {
                    _fraction = 0; // Prevent negative values
                }
                else
                {
                    _fraction = value;
                }
                RaisePropertyChangedEvent(nameof(Fraction));
            }
        }

        // Denominator of the delivered/planned fraction display; editable in
        // both views (shared instance), so notify like Fraction does.
        private int _plannedFraction;
        public int PlannedFraction
        {
            get => _plannedFraction;
            set
            {
                _plannedFraction = value < 0 ? 0 : value; // Prevent negative values
                RaisePropertyChangedEvent(nameof(PlannedFraction));
            }
        }

        public double TotalDose { get; set; }

        // Beam energy mode(s), e.g. "6X" or "6X, 10X"; blank for design/dropdown entries.
        public string Energy { get; set; } = "";

        public PlanSelectionViewModel()
        {

        }
        public PlanSelectionViewModel(string id, string courseId, string ssId, bool isSum, int fraction, double totalDose, string treatmentDate = "", string evaluationDate = "")
        {
            Id = id;
            CourseId = courseId;
            SsId = ssId;
            IsSum = isSum;
            EvaluationDate = evaluationDate;
            DateTime _treatDate;
            if (string.IsNullOrEmpty(treatmentDate))
            {
                TreatmentDate = "Date not found";
            }
            else
            {
                DateTime.TryParse(treatmentDate, out _treatDate);
                TreatmentDate = _treatDate.ToString("yyyy-MM-dd");
            }
            Fraction = fraction;
            PlannedFraction = fraction; // Assuming PlannedFraction is initialized the same as Fraction
            TotalDose = Math.Round(totalDose);
        }
    }
}

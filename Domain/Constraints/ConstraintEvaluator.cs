// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Evaluates a Constraint against a structure via DoseMetricCalculator; returns met/violation + headroom.
using System;
using ReRT.Domain.DoseMetrics;

namespace ReRT.Domain.Constraints
{
    public sealed class EvaluationResult
    {
        // Measured dose-or-volume in canonical units matching the constraint:
        //   Dmax / Mean -> Gy
        //   V           -> % (when Unit=Percent) or cc (when Unit=CC)
        //   VolumeSpared -> cc
        public double MeasuredValue { get; set; }

        // True if the constraint is met. All comparisons are inclusive
        // ('<' treated as '<=', '>' as '>=') — exact-equality of floats is
        // vanishingly rare in clinical DVH data and the inclusive convention
        // is consistent across metrics.
        public bool IsMet { get; set; }

        // Signed headroom: positive when met (distance to the limit), 0 when violated.
        // Units match MeasuredValue.
        public double Remainder { get; set; }

        // The constraint that was evaluated, for convenience.
        public Constraint Constraint { get; set; }
    }

    public sealed class ConstraintEvaluator
    {
        private readonly DoseMetricCalculator _calc;

        public ConstraintEvaluator(DoseMetricCalculator calc)
        {
            if (calc == null) throw new ArgumentNullException(nameof(calc));
            _calc = calc;
        }

        public EvaluationResult Evaluate(Constraint c, StructureRef structure)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (structure == null) throw new ArgumentNullException(nameof(structure));

            double measured = ComputeMeasured(c, structure);
            bool met = IsMetByOp(measured, c.Op, c.Limit);
            double rem = ComputeRemainder(measured, c.Limit, c.Op);

            return new EvaluationResult
            {
                MeasuredValue = measured,
                IsMet = met,
                Remainder = rem,
                Constraint = c,
            };
        }

        private double ComputeMeasured(Constraint c, StructureRef structure)
        {
            switch (c.Metric)
            {
                case MetricType.Dmax:
                    return _calc.Dmax(structure);
                case MetricType.Mean:
                    return _calc.Mean(structure);
                case MetricType.V:
                {
                    if (!c.DoseGy.HasValue)
                        throw new InvalidOperationException("V metric requires an anchor dose.");
                    var pres = c.Unit == LimitUnit.CC
                        ? VolumePresentation.AbsoluteCc
                        : VolumePresentation.RelativePercent;
                    return _calc.VolumeAtDose(structure, c.DoseGy.Value, pres);
                }
                case MetricType.VolumeSpared:
                {
                    if (!c.DoseGy.HasValue)
                        throw new InvalidOperationException("VolumeSpared metric requires an anchor dose.");
                    return _calc.VolumeSparedAtDose(structure, c.DoseGy.Value);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(c), $"Unknown metric: {c.Metric}");
            }
        }

        private static bool IsMetByOp(double measured, string op, double limit)
        {
            // Inclusive on both '<'/'<=' and '>'/'>=': float equality at the
            // limit is vanishingly rare in clinical data, and a consistent
            // inclusive convention removes a per-metric special case.
            switch (op)
            {
                case "<":
                case "<=":
                    return measured <= limit;
                case ">":
                case ">=":
                    return measured >= limit;
                default:
                    throw new ArgumentException($"Unknown operator: '{op}'.", nameof(op));
            }
        }

        private static double ComputeRemainder(double measured, double limit, string op)
        {
            if (op == "<" || op == "<=") return System.Math.Max(limit - measured, 0.0);
            if (op == ">" || op == ">=") return System.Math.Max(measured - limit, 0.0);
            throw new ArgumentException($"Unknown operator: '{op}'.", nameof(op));
        }
    }
}

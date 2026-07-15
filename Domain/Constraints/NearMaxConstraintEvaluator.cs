// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Linq;

namespace ReRT.Domain.Constraints
{
    /// <summary>
    /// Evaluates a structure's constraints against a single near-max (D0.1cc)
    /// EQD2 value, as produced by the conservative max-dose method. Only serial
    /// Dmax constraints are comparable to a near-max; parallel metrics (Mean, V,
    /// VolumeSpared) cannot be derived from it and are ignored — a structure with
    /// only parallel constraints reads as having none.
    /// </summary>
    public static class NearMaxConstraintEvaluator
    {
        public sealed class Result
        {
            public bool HasMichigan { get; set; }
            public bool Met { get; set; }
            public string MichiganDisplay { get; set; } = "N/A";
            public string SabrDisplay { get; set; } = "N/A";
        }

        public static Result Evaluate(ConstraintSet constraints, double nearMaxEqd2Gy)
        {
            var result = new Result();
            if (constraints == null) return result;

            var michigan = constraints.GetEvaluable("Michigan").FirstOrDefault(c => c.Metric == MetricType.Dmax);
            if (michigan != null)
            {
                result.HasMichigan = true;
                result.Met = IsMet(nearMaxEqd2Gy, michigan);
                result.MichiganDisplay = Display(michigan);
            }

            var sabr = constraints.GetEvaluable("SABR").FirstOrDefault(c => c.Metric == MetricType.Dmax);
            if (sabr != null) result.SabrDisplay = Display(sabr);

            return result;
        }

        private static bool IsMet(double measured, Constraint c)
            => (c.Op != null && c.Op.StartsWith(">")) ? measured >= c.Limit : measured <= c.Limit;

        private static string Display(Constraint c)
            => ((c.Op != null && c.Op.StartsWith(">")) ? ">" : "<") + c.Limit + " Gy";
    }
}

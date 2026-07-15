// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Recovery-discount interpolation shared by the registration-based tool
// (ViewModel) and the conservative max-dose tool (ConservativeViewModel).
// Pure, ESAPI-free.
using System;
using System.Collections.Generic;

namespace ReRT.Configuration.Loaders
{
    public static class DiscountCalculator
    {
        /// <summary>
        /// Recovery discount (percent) at the given number of months since
        /// treatment, ROUNDED to the nearest integer (half away from zero) so the
        /// GUI, the report, and the dose math all use the identical whole-percent
        /// value. <see cref="Interpolate"/> holds the underlying curve logic.
        /// </summary>
        public static double Calculate(RecoveryCurve curve, double month)
            => Math.Round(Interpolate(curve, month), MidpointRounding.AwayFromZero);

        /// <summary>
        /// Interpolate a structure's recovery discount (percent) at the given
        /// number of months since treatment. Each curve edge defines a bucket;
        /// a 0.5-month linear ramp blends into the next bucket at each edge.
        /// Past the last edge, returns the "beyond" value (the larger of 50 and
        /// the last bucket). Returns 0 for a null/empty curve.
        /// </summary>
        private static double Interpolate(RecoveryCurve curve, double month)
        {
            if (curve == null) return 0.0;

            var values = curve.Discount;
            var edges = curve.Timepoint;
            if (values == null || values.Count == 0 || edges == null || edges.Count == 0)
                return 0.0;

            double beyondValue = 50.0;
            if (values[values.Count - 1] > beyondValue)
                beyondValue = values[values.Count - 1];
            const double window = 0.5;

            if (month < 0) month = 0;

            // If past the last edge, return beyondValue
            if (month >= edges[edges.Count - 1])
            {
                return beyondValue;
            }

            // linear interpolation helper
            double Lerp(double a, double b, double t) => a + (b - a) * t;

            // For each edge:
            // - month <= edge - window => bucket = curr
            // - month in (edge - window, edge] => interpolate from curr -> next
            // - month > edge => continue to next edge
            for (int i = 0; i < edges.Count; i++)
            {
                double edge = edges[i];
                double curr = values[i];
                double next = (i < values.Count - 1) ? values[i + 1] : beyondValue;

                if (month <= edge - window)
                {
                    return curr;
                }

                if (month <= edge)
                {
                    // map month from [edge - window, edge] to t in [0,1]
                    double t = (month - (edge - window)) / window;
                    return Lerp(curr, next, t);
                }
            }

            // fallback: return last defined bucket value
            return values[values.Count - 1];
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Typed dose/volume constraint POCO. Populated by YamlConfigLoader from
// ReRTConfig.yaml; consumed by ConstraintEvaluator and the report builders.
using System.Globalization;

namespace ReRT.Domain.Constraints
{
    public sealed class Constraint
    {
        public MetricType Metric { get; set; }

        // Anchor dose for V / VolumeSpared metrics (Gy). Null for Dmax / Mean.
        public double? DoseGy { get; set; }

        // One of "<", "<=", ">", ">=". No "==".
        public string Op { get; set; }

        public double Limit { get; set; }
        public LimitUnit Unit { get; set; }

        // Free-form set tag: "SABR", "Michigan", or future user-defined sets.
        public string Set { get; set; }

        // Render in clinical notation: "V20Gy < 40%", "VS16Gy > 1000cc",
        // "Dmax < 85Gy", "Mean < 30Gy".
        public override string ToString()
        {
            string unitStr;
            switch (Unit)
            {
                case LimitUnit.Gy: unitStr = "Gy"; break;
                case LimitUnit.Percent: unitStr = "%"; break;
                case LimitUnit.CC: unitStr = "cc"; break;
                default: unitStr = ""; break;
            }
            string limitStr = Limit.ToString(CultureInfo.InvariantCulture);
            switch (Metric)
            {
                case MetricType.Dmax:
                    return $"Dmax {Op} {limitStr}{unitStr}";
                case MetricType.Mean:
                    return $"Mean {Op} {limitStr}{unitStr}";
                case MetricType.V:
                    return $"V{DoseGy?.ToString(CultureInfo.InvariantCulture) ?? "?"}Gy {Op} {limitStr}{unitStr}";
                case MetricType.VolumeSpared:
                    return $"VS{DoseGy?.ToString(CultureInfo.InvariantCulture) ?? "?"}Gy {Op} {limitStr}{unitStr}";
                default:
                    return $"{Metric} {Op} {limitStr}{unitStr}";
            }
        }
    }
}

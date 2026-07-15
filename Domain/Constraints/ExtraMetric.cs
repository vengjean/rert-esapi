// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Display-only metric (PHYS branch): name + anchor dose, no operator or limit.
// Distinct from Constraint, which carries an op + limit and is evaluated.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ReRT.Domain.Constraints
{
    public sealed class ExtraMetric
    {
        public MetricType Metric { get; set; }
        public double DoseGy { get; set; }

        public string Set { get; set; }

        // Clinical notation with unit: "V20Gy", "VS32Gy".
        public override string ToString()
        {
            string dose = DoseGy.ToString(CultureInfo.InvariantCulture);
            switch (Metric)
            {
                case MetricType.V: return "V" + dose + "Gy";
                case MetricType.VolumeSpared: return "VS" + dose + "Gy";
                default: return Metric + "@" + dose + "Gy";
            }
        }

        // Wire format consumed by Model.cs's PHYS branch ("V20", "VS32").
        public string ToLegacyString()
        {
            string dose = DoseGy.ToString(CultureInfo.InvariantCulture);
            switch (Metric)
            {
                case MetricType.V: return "V" + dose;
                case MetricType.VolumeSpared: return "VS" + dose;
                default: return Metric.ToString();
            }
        }
    }

    public sealed class ExtraMetricSet
    {
        private readonly Dictionary<string, List<ExtraMetric>> _bySet
            = new Dictionary<string, List<ExtraMetric>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ExtraMetric> GetForSet(string set)
        {
            if (set != null && _bySet.TryGetValue(set, out var list)) return list;
            return Empty;
        }

        public void Add(string set, ExtraMetric m)
        {
            if (!_bySet.TryGetValue(set, out var list))
            {
                list = new List<ExtraMetric>();
                _bySet[set] = list;
            }
            list.Add(m);
        }

        public void Set(string set, IEnumerable<ExtraMetric> items)
        {
            _bySet[set] = items == null ? new List<ExtraMetric>() : new List<ExtraMetric>(items);
        }

        public bool IsEmpty
        {
            get { return _bySet.Count == 0 || _bySet.Values.All(v => v.Count == 0); }
        }

        public IEnumerable<string> Sets { get { return _bySet.Keys; } }

        private static readonly IReadOnlyList<ExtraMetric> Empty = new List<ExtraMetric>();
    }
}

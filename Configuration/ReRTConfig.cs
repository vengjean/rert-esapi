// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Typed configuration loaded from ReRTConfig.yaml + DiscountMapping.yaml.
using System;
using System.Collections.Generic;
using ReRT.Domain.Constraints;

namespace ReRT
{
    public sealed class ReRTConfig
    {
        public ReRTConfigDefaults Defaults { get; set; } = new ReRTConfigDefaults();

        // Keyed by canonical structure label (case-insensitive). The label is
        // also stored on each StructureDefinition for callers that iterate the
        // values.
        public IDictionary<string, StructureDefinition> Structures { get; set; }
            = new Dictionary<string, StructureDefinition>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, RecoveryCurve> RecoveryCurves { get; set; }
            = new Dictionary<string, RecoveryCurve>(StringComparer.OrdinalIgnoreCase);

        // Normalized-alias → canonical label. Built once at load time
        // (lowercase + underscore-stripped key) so FindByAlias is O(1).
        internal IDictionary<string, string> AliasIndex { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public StructureDefinition FindByAlias(string esapiStructureId)
        {
            if (esapiStructureId == null) return null;
            var norm = esapiStructureId.Replace("_", "");
            if (AliasIndex.TryGetValue(norm, out var label) &&
                Structures.TryGetValue(label, out var def))
                return def;
            return null;
        }
    }

    public sealed class ReRTConfigDefaults
    {
        public double AlphaBetaRatio { get; set; } = 2.5;

        // Ordered list of structure-Id substrings to try when picking THE
        // body contour from the patient's structure set. The YAML may override
        // this default ordering.
        public List<string> BodyContourPriority { get; set; } =
            new List<string> { "Body", "External", "Skin" };
    }

    public sealed class StructureDefinition
    {
        public string StructureLabel { get; set; }
        public double AlphaBetaRatio { get; set; }
        public List<StructureAlias> Aliases { get; set; } = new List<StructureAlias>();

        // Always null in current data — kept for compatibility with the
        // StructureViewModel constructor path. Real per-structure discounts
        // come from RecoveryCurves and are applied by ViewModel.FetchDiscount.
        public List<double> Discounts { get; set; }

        public ConstraintSet Constraints { get; set; } = new ConstraintSet();
        public ExtraMetricSet ExtraMetrics { get; set; } = new ExtraMetricSet();
    }

    public sealed class StructureAlias
    {
        public string StructureId { get; set; }
        public StructureAlias() { }
        public StructureAlias(string id) { StructureId = id; }
    }

    public sealed class RecoveryCurve
    {
        public List<double> Discount { get; set; } = new List<double>();
        public List<double> Timepoint { get; set; } = new List<double>();
    }
}

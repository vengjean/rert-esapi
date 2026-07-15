// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Loads ReRTConfig.yaml + DiscountMapping.yaml into typed domain objects.
//
// Public surface: YamlConfigLoader.Load(folder) returns a populated ReRTConfig.
// Internal DTO types mirror the YAML wire format and are translated into
// Domain.Constraints types (Constraint, ExtraMetric) at load time, so the
// public types stay decoupled from YamlDotNet attributes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReRT.Domain.Constraints;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ReRT.Configuration.Loaders
{
    public static class YamlConfigLoader
    {
        // Reads {folder}/ReRTConfig.yaml and {folder}/DiscountMapping.yaml
        // and returns a fully-populated ReRTConfig. Throws FormatException
        // (with the file path and cause in the message) if either file is
        // malformed or contains an unknown metric / unit.
        public static ReRTConfig Load(string folder)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            var configPath = Path.Combine(folder, "ReRTConfig.yaml");
            var discountPath = Path.Combine(folder, "DiscountMapping.yaml");
            return LoadFromPaths(configPath, discountPath);
        }

        // Test seam: load from explicit paths so unit tests can point at
        // ReRT.Tests/TestData fixtures without an artificial config folder.
        public static ReRTConfig LoadFromPaths(string configPath, string discountPath)
        {
            var config = new ReRTConfig();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            // ----- ReRTConfig.yaml -----
            ReRTConfigDto configDto;
            try
            {
                using (var reader = new StreamReader(configPath))
                    configDto = deserializer.Deserialize<ReRTConfigDto>(reader)
                                ?? new ReRTConfigDto();
            }
            catch (Exception ex) when (!(ex is FormatException))
            {
                throw new FormatException(
                    $"Failed to parse {configPath}: {ex.Message}", ex);
            }

            if (configDto.Defaults != null)
            {
                config.Defaults.AlphaBetaRatio = configDto.Defaults.AlphaBetaRatio;
                // Empty list in YAML behaves like missing (use fallback).
                if (configDto.Defaults.BodyContourPriority != null &&
                    configDto.Defaults.BodyContourPriority.Count > 0)
                {
                    config.Defaults.BodyContourPriority = configDto.Defaults.BodyContourPriority
                        .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                }
            }

            if (configDto.Structures != null)
            {
                foreach (var kv in configDto.Structures)
                {
                    var label = kv.Key;
                    var sd = ToStructureDefinition(label, kv.Value, configPath);
                    config.Structures[label] = sd;
                }
            }

            // ----- DiscountMapping.yaml -----
            DiscountMappingDto discountDto;
            try
            {
                using (var reader = new StreamReader(discountPath))
                    discountDto = deserializer.Deserialize<DiscountMappingDto>(reader)
                                  ?? new DiscountMappingDto();
            }
            catch (Exception ex) when (!(ex is FormatException))
            {
                throw new FormatException(
                    $"Failed to parse {discountPath}: {ex.Message}", ex);
            }

            if (discountDto.RecoveryCurves != null)
            {
                foreach (var kv in discountDto.RecoveryCurves)
                {
                    config.RecoveryCurves[kv.Key] = new RecoveryCurve
                    {
                        Discount = kv.Value?.Discount ?? new List<double>(),
                        Timepoint = kv.Value?.Timepoint ?? new List<double>(),
                    };
                }
            }

            // ----- Alias index -----
            config.AliasIndex = BuildAliasIndex(config.Structures);

            return config;
        }

        // ---- DTO → domain mapping --------------------------------------------

        private static StructureDefinition ToStructureDefinition(
            string label, StructureDto dto, string sourceFile)
        {
            if (dto == null)
                return new StructureDefinition { StructureLabel = label };

            var sd = new StructureDefinition
            {
                StructureLabel = label,
                AlphaBetaRatio = dto.AlphaBetaRatio,
                Aliases = (dto.Aliases ?? new List<string>())
                          .Where(a => !string.IsNullOrWhiteSpace(a))
                          .Select(a => new StructureAlias(a))
                          .ToList(),
            };

            if (dto.Constraints != null)
            {
                foreach (var setKv in dto.Constraints)
                {
                    var setName = setKv.Key;
                    var constraints = (setKv.Value ?? new List<ConstraintDto>())
                        .Select(c => ToConstraint(c, setName, label, sourceFile))
                        .ToList();
                    sd.Constraints.Set(setName, constraints);
                }
            }

            if (dto.ExtraMetrics != null)
            {
                foreach (var setKv in dto.ExtraMetrics)
                {
                    var setName = setKv.Key;
                    var metrics = (setKv.Value ?? new List<ExtraMetricDto>())
                        .Where(m => m != null)
                        .Select(m => ToExtraMetric(m, setName, label, sourceFile))
                        .ToList();
                    sd.ExtraMetrics.Set(setName, metrics);
                }
            }

            return sd;
        }

        private static Constraint ToConstraint(
            ConstraintDto dto, string set, string label, string sourceFile)
        {
            // Null entries are "NA" placeholders — preserved so the
            // first-slot SABR header convention still works.
            if (dto == null) return null;

            var metric = ParseMetric(dto.Metric, label, sourceFile);
            var op = ValidateOp(dto.Op, label, sourceFile);

            double? doseGy = null;
            if (!string.IsNullOrWhiteSpace(dto.Dose))
            {
                var q = Quantity.Parse(dto.Dose);
                if (q.Unit != LimitUnit.Gy)
                    throw new FormatException(
                        $"{sourceFile}: structure '{label}' constraint dose '{dto.Dose}' must be a dose (Gy/cGy).");
                doseGy = q.Value;
            }

            if (string.IsNullOrWhiteSpace(dto.Limit))
                throw new FormatException(
                    $"{sourceFile}: structure '{label}' constraint missing 'limit' field.");
            var limitQ = Quantity.Parse(dto.Limit);

            // Sanity-check the limit unit against the metric.
            if (metric == MetricType.Dmax || metric == MetricType.Mean)
            {
                if (limitQ.Unit != LimitUnit.Gy)
                    throw new FormatException(
                        $"{sourceFile}: '{label}' {metric} limit '{dto.Limit}' must be a dose.");
            }
            else if (metric == MetricType.V || metric == MetricType.VolumeSpared)
            {
                if (limitQ.Unit != LimitUnit.Percent && limitQ.Unit != LimitUnit.CC)
                    throw new FormatException(
                        $"{sourceFile}: '{label}' {metric} limit '{dto.Limit}' must be % or cc.");
                if (doseGy == null)
                    throw new FormatException(
                        $"{sourceFile}: '{label}' {metric} constraint requires a 'dose' anchor.");
            }

            return new Constraint
            {
                Metric = metric,
                DoseGy = doseGy,
                Op = op,
                Limit = limitQ.Value,
                Unit = limitQ.Unit,
                Set = set,
            };
        }

        private static ExtraMetric ToExtraMetric(
            ExtraMetricDto dto, string set, string label, string sourceFile)
        {
            var metric = ParseMetric(dto.Metric, label, sourceFile);
            if (string.IsNullOrWhiteSpace(dto.Dose))
                throw new FormatException(
                    $"{sourceFile}: '{label}' extraMetric '{metric}' requires a 'dose' anchor.");
            var q = Quantity.Parse(dto.Dose);
            if (q.Unit != LimitUnit.Gy)
                throw new FormatException(
                    $"{sourceFile}: '{label}' extraMetric dose '{dto.Dose}' must be a dose.");
            return new ExtraMetric { Metric = metric, DoseGy = q.Value, Set = set };
        }

        private static MetricType ParseMetric(string s, string label, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException(
                    $"{sourceFile}: '{label}' missing 'metric' field.");
            // Accept exact spellings only — closed vocabulary per decision D1.
            switch (s)
            {
                case "Dmax": return MetricType.Dmax;
                case "Mean": return MetricType.Mean;
                case "V": return MetricType.V;
                case "VolumeSpared": return MetricType.VolumeSpared;
                default:
                    throw new FormatException(
                        $"{sourceFile}: '{label}' unknown metric '{s}'. " +
                        $"Allowed: Dmax, Mean, V, VolumeSpared.");
            }
        }

        private static string ValidateOp(string s, string label, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException(
                    $"{sourceFile}: '{label}' missing 'op' field.");
            switch (s)
            {
                case "<":
                case "<=":
                case ">":
                case ">=":
                    return s;
                default:
                    throw new FormatException(
                        $"{sourceFile}: '{label}' invalid op '{s}'. Allowed: <, <=, >, >=.");
            }
        }

        private static IDictionary<string, string> BuildAliasIndex(
            IDictionary<string, StructureDefinition> structures)
        {
            var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in structures)
            {
                foreach (var alias in kv.Value.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias?.StructureId)) continue;
                    var key = alias.StructureId.Replace("_", "");
                    // First write wins — preserves source ordering when two
                    // structures claim the same alias.
                    if (!idx.ContainsKey(key))
                        idx[key] = kv.Key;
                }
            }
            return idx;
        }

        // ---- DTO types (YAML wire format) ------------------------------------

        private sealed class ReRTConfigDto
        {
            public string Version { get; set; }
            public DefaultsDto Defaults { get; set; }
            public Dictionary<string, StructureDto> Structures { get; set; }
        }

        private sealed class DefaultsDto
        {
            public double AlphaBetaRatio { get; set; } = 2.5;
            public List<string> BodyContourPriority { get; set; }
        }

        private sealed class StructureDto
        {
            public double AlphaBetaRatio { get; set; }
            public List<string> Aliases { get; set; }
            public Dictionary<string, List<ConstraintDto>> Constraints { get; set; }
            public Dictionary<string, List<ExtraMetricDto>> ExtraMetrics { get; set; }
        }

        private sealed class ConstraintDto
        {
            public string Metric { get; set; }
            public string Dose { get; set; }
            public string Op { get; set; }
            public string Limit { get; set; }
        }

        private sealed class ExtraMetricDto
        {
            public string Metric { get; set; }
            public string Dose { get; set; }
        }

        private sealed class DiscountMappingDto
        {
            public Dictionary<string, RecoveryCurveDto> RecoveryCurves { get; set; }
        }

        private sealed class RecoveryCurveDto
        {
            public List<double> Discount { get; set; }
            public List<double> Timepoint { get; set; }
        }
    }
}

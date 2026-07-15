// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Loads InstitutionConfig.local.yaml (preferred) or .sample.yaml fallback.
//
// The local file is gitignored and holds real institutional values
// (output paths, logo, attribution sentence). The sample file is
// committed with placeholder values so a fresh clone produces a working
// run with a generic department header, no logo, and a single default
// output path. Either file alone is sufficient.
using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ReRT.Configuration.Loaders
{
    public sealed class InstitutionConfig
    {
        public InstitutionInfo Institution { get; set; } = new InstitutionInfo();
        public IReadOnlyList<ReportOutput> ReportOutputs { get; set; } = new List<ReportOutput>();

        // Absolute path to the YAML file this config was loaded from. Diagnostic only.
        public string SourceFile { get; set; }

        // The Configuration folder the config was loaded from. Used to resolve
        // a relative LogoPath into an absolute path next to the assembly.
        public string ConfigFolder { get; set; }
    }

    public sealed class InstitutionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DepartmentHeader { get; set; } = string.Empty;
        // Path as written in YAML; may be empty, relative, or absolute.
        public string LogoPath { get; set; } = string.Empty;
        // Resolved at load time: either an absolute path that exists, or "".
        // Empty means "render no logo" — a missing logo file is a warning,
        // not a fatal error.
        public string ResolvedLogoPath { get; set; } = string.Empty;
        public string ConstraintAttribution { get; set; } = string.Empty;
    }

    public sealed class ReportOutput
    {
        public string DisplayName { get; set; } = string.Empty;
        // Substring matched against ReportParams.Hospital. Empty string means
        // "always include this output".
        public string Match { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public static class InstitutionConfigLoader
    {
        public const string LocalFileName = "InstitutionConfig.local.yaml";
        public const string SampleFileName = "InstitutionConfig.sample.yaml";

        // Tries .local first, falls back to .sample, throws only if neither exists.
        public static InstitutionConfig Load(string configFolder)
        {
            if (configFolder == null) throw new ArgumentNullException(nameof(configFolder));
            var localPath = Path.Combine(configFolder, LocalFileName);
            var samplePath = Path.Combine(configFolder, SampleFileName);

            if (File.Exists(localPath))
                return LoadFromPath(localPath, configFolder);
            if (File.Exists(samplePath))
                return LoadFromPath(samplePath, configFolder);

            throw new FileNotFoundException(
                $"Institution configuration not found. Expected one of:\n" +
                $"  {localPath}\n" +
                $"  {samplePath}\n" +
                "Copy InstitutionConfig.sample.yaml from the repo into the " +
                "Configuration folder, or set up InstitutionConfig.local.yaml " +
                "with your institution's values.");
        }

        // Test seam: load from an explicit path.
        public static InstitutionConfig LoadFromPath(string yamlPath, string configFolder)
        {
            if (yamlPath == null) throw new ArgumentNullException(nameof(yamlPath));

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            InstitutionConfigDto dto;
            try
            {
                using (var reader = new StreamReader(yamlPath))
                    dto = deserializer.Deserialize<InstitutionConfigDto>(reader)
                          ?? new InstitutionConfigDto();
            }
            catch (Exception ex) when (!(ex is FormatException))
            {
                throw new FormatException(
                    $"Failed to parse {yamlPath}: {ex.Message}", ex);
            }

            var info = new InstitutionInfo
            {
                Name = dto.Institution?.Name ?? string.Empty,
                DepartmentHeader = dto.Institution?.DepartmentHeader ?? string.Empty,
                LogoPath = dto.Institution?.LogoPath ?? string.Empty,
                ConstraintAttribution = dto.Institution?.ConstraintAttribution ?? string.Empty,
            };
            info.ResolvedLogoPath = ResolveLogoPath(info.LogoPath, configFolder, yamlPath);

            var outputs = new List<ReportOutput>();
            if (dto.ReportOutputs != null)
            {
                foreach (var o in dto.ReportOutputs)
                {
                    if (o == null) continue;
                    outputs.Add(new ReportOutput
                    {
                        DisplayName = o.DisplayName ?? string.Empty,
                        Match = o.Match ?? string.Empty,
                        Path = o.Path ?? string.Empty,
                    });
                }
            }

            return new InstitutionConfig
            {
                Institution = info,
                ReportOutputs = outputs,
                SourceFile = yamlPath,
                ConfigFolder = configFolder,
            };
        }

        // Empty → empty (no logo). Relative → resolved against configFolder.
        // Absolute → used as-is. Missing file → warning logged, treated as
        // empty so a missing logo doesn't block report generation.
        private static string ResolveLogoPath(string logoPath, string configFolder, string yamlPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return string.Empty;

            string full = Path.IsPathRooted(logoPath) || string.IsNullOrEmpty(configFolder)
                ? logoPath
                : Path.GetFullPath(Path.Combine(configFolder, "..", logoPath));

            if (File.Exists(full)) return full;

            // Try a second resolution against configFolder itself (handles a
            // logoPath written as "Configuration/Logo.local.png" *or* just
            // "Logo.local.png" — both should work).
            if (!Path.IsPathRooted(logoPath) && !string.IsNullOrEmpty(configFolder))
            {
                string altFull = Path.GetFullPath(Path.Combine(configFolder, logoPath));
                if (File.Exists(altFull)) return altFull;
            }

            Helpers.SeriLog.LogWarning(
                $"Institution logo file not found: '{full}' (referenced by '{yamlPath}'). " +
                "Report will be generated without a logo.");
            return string.Empty;
        }

        // ---- DTO types (YAML wire format) ------------------------------------

        private sealed class InstitutionConfigDto
        {
            public InstitutionInfoDto Institution { get; set; }
            public List<ReportOutputDto> ReportOutputs { get; set; }
        }

        private sealed class InstitutionInfoDto
        {
            public string Name { get; set; }
            public string DepartmentHeader { get; set; }
            public string LogoPath { get; set; }
            public string ConstraintAttribution { get; set; }
        }

        private sealed class ReportOutputDto
        {
            public string DisplayName { get; set; }
            public string Match { get; set; }
            public string Path { get; set; }
        }
    }
}

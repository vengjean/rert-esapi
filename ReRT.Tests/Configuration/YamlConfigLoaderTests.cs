// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;
using Xunit;

namespace ReRT.Tests.Configuration
{
    public class YamlConfigLoaderTests
    {
        // The test runner copies TestData/*.yaml next to the test assembly.
        private static readonly string TestDataDir =
            Path.Combine(AppContext.BaseDirectory, "TestData");

        private static string Fixture(string name) => Path.Combine(TestDataDir, name);

        private static ReRT.ReRTConfig LoadMinimal()
        {
            return YamlConfigLoader.LoadFromPaths(
                Fixture("minimal-rert-config.yaml"),
                Fixture("minimal-discount-mapping.yaml"));
        }

        [Fact]
        public void Load_MinimalConfig_ReturnsAllStructures()
        {
            var cfg = LoadMinimal();
            cfg.Structures.Should().ContainKey("Lung")
                          .And.ContainKey("Bowel_Small");
            cfg.Structures.Count.Should().Be(2);
        }

        [Fact]
        public void Load_MinimalConfig_AlphaBetaMatchesYaml()
        {
            var cfg = LoadMinimal();
            cfg.Structures["Lung"].AlphaBetaRatio.Should().Be(2.5);
            cfg.Structures["Bowel_Small"].AlphaBetaRatio.Should().Be(3.0);
            cfg.Defaults.AlphaBetaRatio.Should().Be(2.5);
        }

        [Fact]
        public void Load_AliasMatching_CaseInsensitive()
        {
            var cfg = LoadMinimal();
            cfg.FindByAlias("LUNG").StructureLabel.Should().Be("Lung");
            cfg.FindByAlias("lung").StructureLabel.Should().Be("Lung");
            cfg.FindByAlias("Lungs").StructureLabel.Should().Be("Lung");
        }

        [Fact]
        public void Load_AliasMatching_StripsUnderscores()
        {
            var cfg = LoadMinimal();
            cfg.FindByAlias("Total_Lung").StructureLabel.Should().Be("Lung");
            cfg.FindByAlias("TotalLung").StructureLabel.Should().Be("Lung");
            cfg.FindByAlias("Bowel_Small").StructureLabel.Should().Be("Bowel_Small");
            cfg.FindByAlias("SmallBowel").StructureLabel.Should().Be("Bowel_Small");
            cfg.FindByAlias("Small_Bowel").StructureLabel.Should().Be("Bowel_Small");
        }

        [Fact]
        public void Load_AliasMatching_UnknownIdReturnsNull()
        {
            var cfg = LoadMinimal();
            cfg.FindByAlias("NonexistentStructure").Should().BeNull();
            cfg.FindByAlias(null).Should().BeNull();
        }

        [Fact]
        public void Load_ConstraintRoundTrip_PreservesMetricOpLimitUnit()
        {
            var cfg = LoadMinimal();
            var lungSabr = cfg.Structures["Lung"].Constraints.GetForSet("SABR");
            lungSabr.Should().HaveCount(2);
            lungSabr[0].Should().BeNull(); // null placeholder preserved
            var v20 = lungSabr[1];
            v20.Metric.Should().Be(MetricType.V);
            v20.DoseGy.Should().Be(20.0);
            v20.Op.Should().Be("<");
            v20.Limit.Should().Be(40.0);
            v20.Unit.Should().Be(LimitUnit.Percent);
            v20.Set.Should().Be("SABR");
        }

        [Fact]
        public void Load_VolumeSparedConstraint_HasCcUnit()
        {
            var cfg = LoadMinimal();
            var michigan = cfg.Structures["Lung"].Constraints.GetEvaluable("Michigan");
            michigan.Should().HaveCount(1);
            var c = michigan[0];
            c.Metric.Should().Be(MetricType.VolumeSpared);
            c.DoseGy.Should().Be(16.0);
            c.Op.Should().Be(">");
            c.Limit.Should().Be(1000.0);
            c.Unit.Should().Be(LimitUnit.CC);
        }

        [Fact]
        public void Load_DoseValueWithCgy_NormalizesToGy()
        {
            var cfg = LoadMinimal();
            var michigan = cfg.Structures["Bowel_Small"].Constraints.GetEvaluable("Michigan");
            michigan.Should().HaveCount(1);
            var dmax = michigan[0];
            dmax.Metric.Should().Be(MetricType.Dmax);
            dmax.Op.Should().Be("<=");
            // YAML has "5400cGy" → 54.0 Gy
            dmax.Limit.Should().Be(54.0);
            dmax.Unit.Should().Be(LimitUnit.Gy);
        }

        [Fact]
        public void Load_VolumeWithCc_PreservesAbsoluteUnit()
        {
            var cfg = LoadMinimal();
            var c = cfg.Structures["Lung"].Constraints.GetEvaluable("Michigan").Single();
            c.Unit.Should().Be(LimitUnit.CC);
            c.Limit.Should().Be(1000.0);
        }

        [Fact]
        public void Load_ExtraMetrics_PHYS_Populated()
        {
            var cfg = LoadMinimal();
            var phys = cfg.Structures["Lung"].ExtraMetrics.GetForSet("PHYS");
            phys.Should().HaveCount(1);
            phys[0].Metric.Should().Be(MetricType.V);
            phys[0].DoseGy.Should().Be(20.0);
            phys[0].ToLegacyString().Should().Be("V20");
        }

        [Fact]
        public void Load_RecoveryCurves_PointsOrdered()
        {
            var cfg = LoadMinimal();
            cfg.RecoveryCurves.Should().ContainKey("Lung")
                              .And.ContainKey("Bowel_Small");
            var lung = cfg.RecoveryCurves["Lung"];
            lung.Timepoint.Should().ContainInOrder(3.0, 6.0, 12.0, 36.0);
            lung.Discount.Should().ContainInOrder(0.0, 10.0, 25.0, 50.0);
        }

        [Fact]
        public void Load_MissingBodyContourPriority_FallsBackToDefault()
        {
            // minimal-rert-config.yaml does NOT declare bodyContourPriority;
            // the loader should leave the built-in default in place.
            var cfg = LoadMinimal();
            cfg.Defaults.BodyContourPriority.Should().Equal(new[] { "Body", "External", "Skin" });
        }

        [Fact]
        public void Load_RealConfig_BodyContourPriorityRespected()
        {
            // Production ReRTConfig.yaml declares bodyContourPriority — assert
            // it parsed in the expected order.
            var repoRoot = LocateRepoRoot();
            var configFolder = Path.Combine(repoRoot, "Configuration");
            var cfg = YamlConfigLoader.Load(configFolder);
            cfg.Defaults.BodyContourPriority.Should().Equal(new[] { "Body", "External", "Skin" });
        }

        [Fact]
        public void Load_MalformedYaml_ThrowsWithDescriptiveMessage()
        {
            Action act = () => YamlConfigLoader.LoadFromPaths(
                Fixture("malformed.yaml"),
                Fixture("minimal-discount-mapping.yaml"));
            act.Should().Throw<FormatException>()
               .WithMessage("*malformed.yaml*");
        }

        [Fact]
        public void Load_UnknownMetric_ThrowsWithMetricNameInMessage()
        {
            Action act = () => YamlConfigLoader.LoadFromPaths(
                Fixture("unknown-metric.yaml"),
                Fixture("minimal-discount-mapping.yaml"));
            act.Should().Throw<FormatException>()
               .WithMessage("*NotARealMetric*");
        }

        [Fact]
        public void Load_RealConfig_ParsesEveryStructureWithoutError()
        {
            // Smoke test: the production ReRTConfig.yaml + DiscountMapping.yaml
            // must round-trip cleanly. Resolves them relative to the repo root
            // via the test assembly's location.
            var repoRoot = LocateRepoRoot();
            var configFolder = Path.Combine(repoRoot, "Configuration");
            var cfg = YamlConfigLoader.Load(configFolder);

            cfg.Structures.Should().NotBeEmpty();
            cfg.RecoveryCurves.Should().NotBeEmpty();

            // Sanity-check the well-known Lung entry against its configured semantics.
            var lung = cfg.Structures["Lung"];
            lung.Constraints.GetEvaluable("Michigan").Should().HaveCount(1);
            lung.Constraints.GetForSet("SABR")[0].Should().BeNull();
            lung.ExtraMetrics.GetForSet("PHYS").Should().HaveCount(1);
        }

        // Walks up from the test assembly location until it finds the directory
        // containing ReRT.csproj. Lets the production-config smoke test run
        // without hard-coding paths.
        private static string LocateRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReRT.csproj")))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("Could not find ReRT.csproj from test output dir.");
            return dir.FullName;
        }
    }
}

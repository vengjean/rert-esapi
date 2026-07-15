// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using ReRT.Configuration.Loaders;
using Xunit;

namespace ReRT.Tests.Configuration
{
    public class InstitutionConfigLoaderTests : IDisposable
    {
        // The test runner copies TestData/*.yaml next to the test assembly.
        private static readonly string TestDataDir =
            Path.Combine(AppContext.BaseDirectory, "TestData");

        private static string Fixture(string name) => Path.Combine(TestDataDir, name);

        // Per-test scratch folder containing zero, one, or both of the
        // sample/local files. Cleaned up in Dispose.
        private readonly string _scratch;

        public InstitutionConfigLoaderTests()
        {
            _scratch = Path.Combine(Path.GetTempPath(),
                "ReRT_InstCfgTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratch);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); }
            catch { /* best-effort cleanup */ }
        }

        // Stages a fixture into the scratch folder under the loader's expected
        // file name (.local or .sample). Returns the staged absolute path.
        private string Stage(string fixtureName, string targetName)
        {
            string dest = Path.Combine(_scratch, targetName);
            File.Copy(Fixture(fixtureName), dest, overwrite: true);
            return dest;
        }

        [Fact]
        public void Load_LocalPresent_PrefersLocal()
        {
            Stage("institution-local.yaml", InstitutionConfigLoader.LocalFileName);
            Stage("institution-sample.yaml", InstitutionConfigLoader.SampleFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.Name.Should().Be("Example Cancer Center");
            cfg.SourceFile.Should().EndWith(InstitutionConfigLoader.LocalFileName);
            cfg.ReportOutputs.Should().Contain(o => o.Match == "hospitalA");
        }

        [Fact]
        public void Load_LocalAbsent_UsesSample()
        {
            Stage("institution-sample.yaml", InstitutionConfigLoader.SampleFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.Name.Should().Be("Your Institution Name");
            cfg.SourceFile.Should().EndWith(InstitutionConfigLoader.SampleFileName);
            cfg.ReportOutputs.Should().HaveCount(1);
            cfg.ReportOutputs[0].Match.Should().BeEmpty();
        }

        [Fact]
        public void Load_BothAbsent_ThrowsWithBothPathsInMessage()
        {
            Action act = () => InstitutionConfigLoader.Load(_scratch);

            act.Should().Throw<FileNotFoundException>()
               .Where(ex =>
                   ex.Message.Contains(InstitutionConfigLoader.LocalFileName) &&
                   ex.Message.Contains(InstitutionConfigLoader.SampleFileName));
        }

        [Fact]
        public void Load_LogoPathEmpty_LeavesEmpty()
        {
            Stage("institution-sample.yaml", InstitutionConfigLoader.SampleFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.LogoPath.Should().BeEmpty();
            cfg.Institution.ResolvedLogoPath.Should().BeEmpty();
        }

        [Fact]
        public void Load_LogoPathMissingFile_ReturnsEmptyResolved()
        {
            // The local fixture references "Configuration\Logo.local.png", which
            // is not present next to the scratch folder. Loader should log a
            // warning and leave ResolvedLogoPath empty rather than crashing.
            Stage("institution-local.yaml", InstitutionConfigLoader.LocalFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.LogoPath.Should().NotBeEmpty(); // raw value preserved
            cfg.Institution.ResolvedLogoPath.Should().BeEmpty(); // missing -> empty
        }

        [Fact]
        public void Load_LogoPathExistingFile_ResolvesAbsolute()
        {
            // Drop a tiny stand-in file at the path the local fixture refers to,
            // and verify the loader resolves it to an absolute path.
            Stage("institution-local.yaml", InstitutionConfigLoader.LocalFileName);
            string logoTarget = Path.Combine(_scratch, "Configuration", "Logo.local.png");
            Directory.CreateDirectory(Path.GetDirectoryName(logoTarget));
            File.WriteAllBytes(logoTarget, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.ResolvedLogoPath.Should().NotBeEmpty();
            File.Exists(cfg.Institution.ResolvedLogoPath).Should().BeTrue();
        }

        [Fact]
        public void Load_ReportOutputsEmpty_LoadsSuccessfully()
        {
            // The loader must accept a config with no outputs. Whether running
            // with zero outputs is sensible is the user's problem, not the loader's.
            string p = Path.Combine(_scratch, InstitutionConfigLoader.SampleFileName);
            File.WriteAllText(p, "institution:\n  name: \"X\"\n");

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.Name.Should().Be("X");
            cfg.ReportOutputs.Should().BeEmpty();
        }

        [Fact]
        public void Load_LocalFixture_ParsesAllReportOutputs()
        {
            Stage("institution-local.yaml", InstitutionConfigLoader.LocalFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.ReportOutputs.Should().HaveCount(3);
            cfg.ReportOutputs[0].Match.Should().Be("hospitalA");
            cfg.ReportOutputs[1].Match.Should().Be("hospitalB");
            cfg.ReportOutputs[2].Match.Should().BeEmpty();
            cfg.ReportOutputs.All(o => !string.IsNullOrEmpty(o.Path)).Should().BeTrue();
        }

        [Fact]
        public void Load_LocalFixture_PreservesAttribution()
        {
            Stage("institution-local.yaml", InstitutionConfigLoader.LocalFileName);

            var cfg = InstitutionConfigLoader.Load(_scratch);

            cfg.Institution.ConstraintAttribution
               .Should().Contain("Stanford de novo");
        }
    }
}

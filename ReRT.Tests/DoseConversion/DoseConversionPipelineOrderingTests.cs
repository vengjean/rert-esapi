// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReRT.Domain.DoseConversion;
using Xunit;

namespace ReRT.Tests.DoseConversion
{
    /// <summary>
    /// Pins the structure sweep order that encodes priority. The voxel-claim
    /// grid in <see cref="VoxelTransformer"/> is first-writer-wins per tier, so
    /// the highest-priority structure (top of the table, index 0) must be
    /// applied FIRST and the body contour LAST. Regression guard for the
    /// flipped-priority bug, where a stray <c>.Reverse()</c> let the lowest-
    /// priority OAR (e.g. Lung) win voxels it shared with a higher-priority OAR
    /// (e.g. Esophagus) and so inflated the higher-priority structure's near-max.
    /// </summary>
    public class DoseConversionPipelineOrderingTests
    {
        private static StructureViewModel Str(string id, string label, bool include)
            => new StructureViewModel { StructureId = id, StructureLabel = label, Include = include };

        [Fact]
        public void OrderForSweep_AppliesHighestPriorityFirst_BodyLast()
        {
            // Table order is priority order: Esophagus (#1), Lung (#2), Body.
            var mappings = new List<StructureViewModel>
            {
                Str("Esophagus", "Esophagus", include: true),
                Str("Lung",      "Lung",      include: true),
                Str("BODY",      "Body",      include: true),
            };

            var ordered = DoseConversionPipeline.OrderForSweep(mappings);

            // Esophagus must be written before Lung so its inner (and halo)
            // claim wins the shared voxels at the adjacent boundary.
            ordered.Select(x => x.StructureId)
                   .Should().Equal("Esophagus", "Lung", "BODY");
        }

        [Fact]
        public void OrderForSweep_ForcesBodyLast_EvenWhenListedFirst()
        {
            var mappings = new List<StructureViewModel>
            {
                Str("BODY",      "Body",      include: true),
                Str("Esophagus", "Esophagus", include: true),
                Str("Lung",      "Lung",      include: true),
            };

            var ordered = DoseConversionPipeline.OrderForSweep(mappings);

            ordered.Select(x => x.StructureId)
                   .Should().Equal("Esophagus", "Lung", "BODY");
            ordered.Last().IsBody.Should().BeTrue();
        }

        [Fact]
        public void OrderForSweep_PreservesTablePriorityAmongNonBody()
        {
            // A three-OAR table; the relative order of the non-body OARs must
            // survive verbatim (it IS the priority), with body appended.
            var mappings = new List<StructureViewModel>
            {
                Str("Cord",      "SpinalCord", include: true),
                Str("Esophagus", "Esophagus",  include: true),
                Str("Lung",      "Lung",       include: true),
                Str("BODY",      "Body",       include: true),
            };

            var ordered = DoseConversionPipeline.OrderForSweep(mappings);

            ordered.Select(x => x.StructureId)
                   .Should().Equal("Cord", "Esophagus", "Lung", "BODY");
        }

        [Fact]
        public void OrderForSweep_DropsExcludedStructures()
        {
            var mappings = new List<StructureViewModel>
            {
                Str("Esophagus", "Esophagus",  include: true),
                Str("Cord",      "SpinalCord", include: false),
                Str("Lung",      "Lung",       include: true),
                Str("BODY",      "Body",       include: true),
            };

            var ordered = DoseConversionPipeline.OrderForSweep(mappings);

            ordered.Select(x => x.StructureId)
                   .Should().Equal("Esophagus", "Lung", "BODY");
        }
    }
}

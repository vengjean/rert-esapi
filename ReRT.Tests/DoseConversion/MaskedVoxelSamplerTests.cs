// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using FluentAssertions;
using ReRT.Domain.DoseConversion;
using Xunit;

namespace ReRT.Tests.DoseConversion
{
    /// <summary>
    /// Pure coverage for the conservative voxel near-max helpers. The ESAPI sampling
    /// (rasterization + supersampled boundary fractions) needs Eclipse types and is
    /// exercised by the smoke test instead.
    /// </summary>
    public class MaskedVoxelSamplerTests
    {
        private static MaskedVoxelSampler.DoseVol DV(float dose, float vol)
            => new MaskedVoxelSampler.DoseVol { DoseGy = dose, VolCc = vol };

        // ----- WeightedNearMaxGy --------------------------------------------------

        [Fact]
        public void WeightedNearMax_Empty_ReturnsZero()
        {
            MaskedVoxelSampler.WeightedNearMaxGy(new List<MaskedVoxelSampler.DoseVol>(), 0.1)
                .Should().Be(0.0);
        }

        [Fact]
        public void WeightedNearMax_SingleLevelCoveringThreshold_ReturnsItsDose()
        {
            var s = new List<MaskedVoxelSampler.DoseVol> { DV(50f, 0.2f) };
            MaskedVoxelSampler.WeightedNearMaxGy(s, 0.1).Should().BeApproximately(50.0, 1e-6);
        }

        [Fact]
        public void WeightedNearMax_InterpolatesAcrossTwoLevels()
        {
            // cumulative: 0.05cc at dose>=100, 0.15cc at dose>=80; 0.1cc lands halfway -> 90
            var s = new List<MaskedVoxelSampler.DoseVol> { DV(100f, 0.05f), DV(80f, 0.10f) };
            MaskedVoxelSampler.WeightedNearMaxGy(s, 0.1).Should().BeApproximately(90.0, 1e-4);
        }

        [Fact]
        public void WeightedNearMax_UnsortedInput_IsHandledHottestFirst()
        {
            var s = new List<MaskedVoxelSampler.DoseVol> { DV(80f, 0.10f), DV(100f, 0.05f) };
            MaskedVoxelSampler.WeightedNearMaxGy(s, 0.1).Should().BeApproximately(90.0, 1e-4);
        }

        [Fact]
        public void WeightedNearMax_TotalBelowThreshold_ReturnsColdest()
        {
            var s = new List<MaskedVoxelSampler.DoseVol> { DV(100f, 0.02f), DV(80f, 0.03f) };
            MaskedVoxelSampler.WeightedNearMaxGy(s, 0.1).Should().BeApproximately(80.0, 1e-6);
        }

        [Fact]
        public void WeightedNearMax_PartialBoundaryVolume_PullsResultDown()
        {
            // The whole point of partial-volume weighting: a half-included hot voxel
            // contributes less hot volume, so D0.1cc drops below the all-in case.
            var allIn = new List<MaskedVoxelSampler.DoseVol> { DV(100f, 0.10f), DV(70f, 0.10f) };
            var halfIn = new List<MaskedVoxelSampler.DoseVol> { DV(100f, 0.05f), DV(70f, 0.10f) };
            double dAllIn = MaskedVoxelSampler.WeightedNearMaxGy(allIn, 0.1);
            double dHalfIn = MaskedVoxelSampler.WeightedNearMaxGy(halfIn, 0.1);
            dAllIn.Should().BeApproximately(100.0, 1e-4);
            dHalfIn.Should().BeLessThan(dAllIn);
        }

        // ----- EuclideanDistanceSq --------------------------------------------------

        [Fact]
        public void DistanceSq_SeedIsZero_DistanceGrowsAlongAxis()
        {
            var seed = new bool[1, 1, 3];   // nk=1, nj=1, ni=3
            seed[0, 0, 0] = true;
            var d = MaskedVoxelSampler.EuclideanDistanceSq(seed, rx: 2.0, ry: 2.0, rz: 2.0);
            d[0, 0, 0].Should().Be(0f);
            d[0, 0, 1].Should().BeApproximately(4f, 1e-4f);  // (1 * 2)^2
            d[0, 0, 2].Should().BeApproximately(16f, 1e-4f); // (2 * 2)^2
        }

        [Fact]
        public void DistanceSq_Anisotropic_UsesPerAxisSpacing()
        {
            var seed = new bool[3, 3, 3];
            seed[1, 1, 1] = true;
            var d = MaskedVoxelSampler.EuclideanDistanceSq(seed, rx: 1.0, ry: 2.0, rz: 3.0);
            d[1, 1, 0].Should().BeApproximately(1f, 1e-4f); // x face neighbor
            d[1, 0, 1].Should().BeApproximately(4f, 1e-4f); // y face neighbor
            d[0, 1, 1].Should().BeApproximately(9f, 1e-4f); // z face neighbor
        }

        [Fact]
        public void DistanceSq_ObliqueDirection_IsExact()
        {
            // Regression for the margin under-reach: the chamfer this replaced
            // overestimated off-lattice directions (a 3-4-5 triangle came out as
            // (3*sqrt(2) + 1)^2 ≈ 27.5); the exact transform must return 25.
            var seed = new bool[1, 4, 5];
            seed[0, 0, 0] = true;
            var d = MaskedVoxelSampler.EuclideanDistanceSq(seed, rx: 1.0, ry: 1.0, rz: 1.0);
            d[0, 3, 4].Should().BeApproximately(25f, 1e-3f); // 3^2 + 4^2
        }

        [Fact]
        public void DistanceSq_NoSeeds_EverythingStaysAboveGate()
        {
            var seed = new bool[2, 2, 2];
            var d = MaskedVoxelSampler.EuclideanDistanceSq(seed, rx: 1.0, ry: 1.0, rz: 1.0);
            d[1, 1, 1].Should().BeGreaterThan(1f);
        }

        // ----- MaskIndex (mask-grid cell for a scoring sub-point) -------------------

        [Fact]
        public void MaskIndex_CoarseMask_AllSubPointsShareTheVoxelCell()
        {
            for (int sub = 0; sub < 3; sub++)
                MaskedVoxelSampler.MaskIndex(7, sub, maskS: 1, scoreS: 3).Should().Be(7);
        }

        [Fact]
        public void MaskIndex_AlignedMask_EachSubPointGetsItsOwnCell()
        {
            for (int sub = 0; sub < 3; sub++)
                MaskedVoxelSampler.MaskIndex(7, sub, maskS: 3, scoreS: 3).Should().Be(21 + sub);
        }

        [Fact]
        public void MaskIndex_IntermediateRefinement_FloorsToContainingCell()
        {
            // Sub-points at 1/6, 3/6, 5/6 of the voxel; two mask cells per voxel
            // covering [0, 1/2) and [1/2, 1) -> cells 0, 1, 1.
            MaskedVoxelSampler.MaskIndex(0, 0, maskS: 2, scoreS: 3).Should().Be(0);
            MaskedVoxelSampler.MaskIndex(0, 1, maskS: 2, scoreS: 3).Should().Be(1);
            MaskedVoxelSampler.MaskIndex(0, 2, maskS: 2, scoreS: 3).Should().Be(1);
        }

        // ----- AxisScale (zero-margin axis blocking) --------------------------------

        [Fact]
        public void AxisScale_PositiveMargin_NormalisesByMargin()
        {
            MaskedVoxelSampler.AxisScale(2.5, 5.0).Should().BeApproximately(0.5, 1e-9);
        }

        [Fact]
        public void AxisScale_ZeroMargin_ExceedsRegionGate()
        {
            // The region gate is dist <= 1; a single step along a zero-margin axis
            // must overshoot it on its own.
            MaskedVoxelSampler.AxisScale(2.5, 0.0).Should().BeGreaterThan(1.0);
        }

        [Fact]
        public void DistanceSq_ZeroMarginAxis_BlocksStepsAlongIt()
        {
            // 5 mm in-plane margin on a 2.5 mm grid, zero sup-inf margin: in-plane
            // neighbors stay inside the <= 1 gate, anything with a z component is out.
            var seed = new bool[3, 3, 3];
            seed[1, 1, 1] = true;
            var d = MaskedVoxelSampler.EuclideanDistanceSq(seed,
                rx: MaskedVoxelSampler.AxisScale(2.5, 5.0),
                ry: MaskedVoxelSampler.AxisScale(2.5, 5.0),
                rz: MaskedVoxelSampler.AxisScale(2.5, 0.0));
            d[1, 1, 0].Should().BeLessThanOrEqualTo(1f); // in-plane face neighbor: inside
            d[1, 0, 0].Should().BeLessThanOrEqualTo(1f); // in-plane diagonal: inside
            d[0, 1, 1].Should().BeGreaterThan(1f);       // z face neighbor: blocked
            d[0, 1, 0].Should().BeGreaterThan(1f);       // diagonal with z component: blocked
        }

        // ----- SampleTrilinear (dose interpolation at sub-points) ------------------

        private static int[,,] EightCornerCube()
        {
            // m[k, i, j]
            var m = new int[2, 2, 2];
            m[0, 0, 0] = 0; m[0, 0, 1] = 10; m[0, 1, 0] = 20; m[0, 1, 1] = 30;
            m[1, 0, 0] = 100; m[1, 0, 1] = 110; m[1, 1, 0] = 120; m[1, 1, 1] = 130;
            return m;
        }

        [Fact]
        public void Trilinear_AtVoxelCentre_ReturnsThatVoxel()
        {
            var m = EightCornerCube();
            MaskedVoxelSampler.SampleTrilinear(m, 0, 0, 0, 2, 2, 2).Should().BeApproximately(0, 1e-6);
            MaskedVoxelSampler.SampleTrilinear(m, 1, 1, 1, 2, 2, 2).Should().BeApproximately(130, 1e-6);
            MaskedVoxelSampler.SampleTrilinear(m, 0, 1, 0, 2, 2, 2).Should().BeApproximately(20, 1e-6);
        }

        [Fact]
        public void Trilinear_Midpoints_Average()
        {
            var m = EightCornerCube();
            // halfway in j between m[0,0,0]=0 and m[0,0,1]=10 -> 5
            MaskedVoxelSampler.SampleTrilinear(m, 0, 0, 0.5, 2, 2, 2).Should().BeApproximately(5, 1e-6);
            // centre of the cube -> mean of all eight corners
            MaskedVoxelSampler.SampleTrilinear(m, 0.5, 0.5, 0.5, 2, 2, 2).Should().BeApproximately(65, 1e-6);
        }

        [Fact]
        public void Trilinear_OutOfRange_ClampsToGrid()
        {
            var m = EightCornerCube();
            MaskedVoxelSampler.SampleTrilinear(m, -3, -3, -3, 2, 2, 2).Should().BeApproximately(0, 1e-6);
            MaskedVoxelSampler.SampleTrilinear(m, 9, 9, 9, 2, 2, 2).Should().BeApproximately(130, 1e-6);
        }
    }
}

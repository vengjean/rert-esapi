// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using FluentAssertions;
using ReRT.Domain.DoseConversion;
using Xunit;

namespace ReRT.Tests.DoseConversion
{
    /// <summary>
    /// Unit coverage targets the pure-math sub-helper
    /// <see cref="VoxelTransformer.ApplyFormulaToVoxel"/>; the full
    /// <see cref="VoxelTransformer.Apply"/> entry-point requires ESAPI
    /// types (Structure/Registration/Dose) and is exercised by the
    /// Eclipse smoke test instead.
    /// </summary>
    public class VoxelTransformerTests
    {
        // --- EQD2 path --------------------------------------------------------

        [Fact]
        public void ApplyFormulaToVoxel_Eqd2_AtTwoGyPerFx_RoundTripsToSameVoxel()
        {
            // 60 cGy voxel × 100 fxScale × 0.01 voxelToGy = 60 Gy in 30 fx
            // (i.e. 2 Gy/fx). EQD2 at 2 Gy/fx ≡ physical → voxel value
            // should round-trip to itself.
            const double voxelToGy = 0.01;     // 1 voxel = 1 cGy
            int sourceVoxel = 6000;            // 60 Gy total
            double fractionScaling = 1.0;
            int fxCount = 30;
            double αβ = 3.0;

            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel, fractionScaling, voxelToGy, αβ, fxCount, DoseFormulas.PhysicalToEqd2);

            // Allow ±1 voxel for float roundtrip drift.
            actual.Should().BeInRange(sourceVoxel - 1, sourceVoxel + 1);
        }

        [Fact]
        public void ApplyFormulaToVoxel_Eqd2_AtHighDosePerFx_AmplifiesDose()
        {
            // 50 Gy / 5 fx (10 Gy/fx) at αβ=3: EQD2 = 130 Gy. Voxel result
            // therefore ≈ 13000.
            const double voxelToGy = 0.01;
            int sourceVoxel = 5000;            // 50 Gy total
            double fractionScaling = 1.0;
            int fxCount = 5;
            double αβ = 3.0;

            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel, fractionScaling, voxelToGy, αβ, fxCount, DoseFormulas.PhysicalToEqd2);

            actual.Should().BeInRange(13000 - 2, 13000 + 2);
        }

        // --- Identity (Physical mode) ----------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(123456)]
        [InlineData(2_000_000)]
        public void ApplyFormulaToVoxel_Identity_PassesThroughVoxel(int sourceVoxel)
        {
            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel, fractionScaling: 1.0, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 30, DoseFormulas.Identity);

            // Identity ∘ Gy↔voxel roundtrip: should return the source value
            // (modulo at most 1 voxel of float drift).
            actual.Should().BeInRange(sourceVoxel - 1, sourceVoxel + 1);
        }

        [Fact]
        public void ApplyFormulaToVoxel_Identity_ScalesByFractionScaling()
        {
            // Half-fractionation (e.g. delivered 5 of planned 10): the
            // identity path should halve the voxel value.
            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 1000, fractionScaling: 0.5, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 30, DoseFormulas.Identity);

            actual.Should().BeInRange(499, 501);
        }

        // --- Numerical guards -------------------------------------------------

        [Fact]
        public void ApplyFormulaToVoxel_OverflowsInt_ClampsToMaxValue()
        {
            // Construct a formula that returns a Gy value so large its
            // voxel translation would exceed Int32.MaxValue.
            DoseFormula huge = (d, _, _, _) => 1e20;

            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 1, fractionScaling: 1.0, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 5, huge);

            actual.Should().Be(int.MaxValue);
        }

        [Fact]
        public void ApplyFormulaToVoxel_FormulaReturnsNaN_Throws()
        {
            DoseFormula nan = (d, _, _, _) => double.NaN;

            Action act = () => VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 100, fractionScaling: 1.0, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 5, nan);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ApplyFormulaToVoxel_FormulaReturnsInfinity_Throws()
        {
            DoseFormula inf = (d, _, _, _) => double.PositiveInfinity;

            Action act = () => VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 100, fractionScaling: 1.0, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 5, inf);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ApplyFormulaToVoxel_FormulaThrows_WrapsAsInvalidOperation()
        {
            DoseFormula boom = (d, _, _, _) => throw new ApplicationException("boom");

            Action act = () => VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 100, fractionScaling: 1.0, voxelToGy: 0.01,
                alphaBeta: 3.0, numFractions: 5, boom);

            act.Should().Throw<InvalidOperationException>()
                .WithInnerException<ApplicationException>();
        }

        [Fact]
        public void ApplyFormulaToVoxel_VoxelToGyZero_ReturnsZero()
        {
            // Defensive: if scaling collapses (no max-dose voxel), we
            // shouldn't divide by zero — return 0 instead.
            int actual = VoxelTransformer.ApplyFormulaToVoxel(
                sourceVoxel: 5000, fractionScaling: 1.0, voxelToGy: 0.0,
                alphaBeta: 3.0, numFractions: 5, DoseFormulas.PhysicalToEqd2);

            actual.Should().Be(0);
        }

        [Fact]
        public void Dilate6Connected_SingleCenterVoxel_Produces7Voxels()
        {
            // Center + 6 face neighbours = 7 lit voxels.
            var src = new bool[3, 3, 3];
            src[1, 1, 1] = true;

            var dst = VoxelTransformer.Dilate6Connected(src);

            CountTrue(dst).Should().Be(7);
            dst[1, 1, 1].Should().BeTrue();
            dst[0, 1, 1].Should().BeTrue();
            dst[2, 1, 1].Should().BeTrue();
            dst[1, 0, 1].Should().BeTrue();
            dst[1, 2, 1].Should().BeTrue();
            dst[1, 1, 0].Should().BeTrue();
            dst[1, 1, 2].Should().BeTrue();
            dst[0, 0, 0].Should().BeFalse(); // diagonal not lit (6-connected only)
        }

        [Fact]
        public void Dilate6Connected_CornerVoxel_HandlesArrayEdgeWithoutCrash()
        {
            // Corner has only 3 in-bounds face neighbours → 4 lit voxels.
            var src = new bool[3, 3, 3];
            src[0, 0, 0] = true;

            var dst = VoxelTransformer.Dilate6Connected(src);

            CountTrue(dst).Should().Be(4);
            dst[0, 0, 0].Should().BeTrue();
            dst[1, 0, 0].Should().BeTrue();
            dst[0, 1, 0].Should().BeTrue();
            dst[0, 0, 1].Should().BeTrue();
        }

        [Fact]
        public void Dilate6Connected_EmptyMask_RemainsEmpty()
        {
            var src = new bool[2, 2, 2];

            var dst = VoxelTransformer.Dilate6Connected(src);

            CountTrue(dst).Should().Be(0);
        }

        [Fact]
        public void Dilate6Connected_NullInput_Throws()
        {
            Action act = () => VoxelTransformer.Dilate6Connected(null);
            act.Should().Throw<ArgumentNullException>();
        }

        // --- Dilate26Connected (production halo kernel) ----------------------

        [Fact]
        public void Dilate26Connected_SingleCenterVoxel_LightsFull3x3x3Cube()
        {
            // Center + 26 neighbours = 27 lit voxels (the full 3×3×3 cube).
            var src = new bool[3, 3, 3];
            src[1, 1, 1] = true;

            var dst = VoxelTransformer.Dilate26Connected(src);

            CountTrue(dst).Should().Be(27);
        }

        [Fact]
        public void Dilate26Connected_CornerVoxel_LightsClampedCube()
        {
            // Corner has only 7 in-bounds neighbours (the 2×2×2 cube
            // anchored at the corner) → 8 lit voxels total.
            var src = new bool[3, 3, 3];
            src[0, 0, 0] = true;

            var dst = VoxelTransformer.Dilate26Connected(src);

            CountTrue(dst).Should().Be(8);
            dst[0, 0, 0].Should().BeTrue();
            dst[1, 1, 1].Should().BeTrue(); // the diagonal neighbour 6-conn would miss
        }

        [Fact]
        public void Dilate26Connected_LightsDiagonalsThat6ConnectedMisses()
        {
            // Regression for the body-discount leak. With 6-connected
            // dilation, voxel (2,2,2) (corner-diagonal of (1,1,1)) is NOT
            // lit; with 26-connected it IS. The body-fallback would have
            // claimed (2,2,2) under 6-conn — letting the body's discount
            // perturb DVH metrics for the OAR centred at (1,1,1).
            var src = new bool[4, 4, 4];
            src[1, 1, 1] = true;

            var sixConn = VoxelTransformer.Dilate6Connected(src);
            var twentySixConn = VoxelTransformer.Dilate26Connected(src);

            sixConn[2, 2, 2].Should().BeFalse("6-conn covers face neighbours only");
            twentySixConn[2, 2, 2].Should().BeTrue("26-conn covers corner diagonals");
        }

        [Fact]
        public void Dilate26Connected_EmptyMask_RemainsEmpty()
        {
            var src = new bool[2, 2, 2];

            var dst = VoxelTransformer.Dilate26Connected(src);

            CountTrue(dst).Should().Be(0);
        }

        [Fact]
        public void Dilate26Connected_NullInput_Throws()
        {
            Action act = () => VoxelTransformer.Dilate26Connected(null);
            act.Should().Throw<ArgumentNullException>();
        }

        private static int CountTrue(bool[,,] m)
        {
            int n = 0;
            for (int k = 0; k < m.GetLength(0); k++)
                for (int j = 0; j < m.GetLength(1); j++)
                    for (int i = 0; i < m.GetLength(2); i++)
                        if (m[k, j, i]) n++;
            return n;
        }

        // --- ResetClaims (cross-mode/plan leak prevention) -------------------

        [Fact]
        public void ResetClaims_ClearsClaimGrid()
        {
            // Regression: the pipeline reuses a single VoxelTransformer
            // across every (plan × mode) sweep. Without ResetClaims, claims
            // accumulated during one sweep block every write during the
            // next, yielding an all-zero output dose grid. Internal access —
            // see InternalsVisibleTo in ReRT/Properties/AssemblyInfo.cs.
            var t = new VoxelTransformer();
            t.EnsureClaims(2, 2, 2);                              // [k, i, j]
            t._claim[1, 0, 1] = VoxelTransformer.ClaimInner;
            t._claim[0, 1, 0] = VoxelTransformer.ClaimHalo;

            t.ResetClaims();

            foreach (byte claim in t._claim)
                claim.Should().Be(VoxelTransformer.ClaimFree);
        }

        // --- ClampToGrid (structure-outside-grid early-return contract) ------

        [Fact]
        public void ClampToGrid_StructureEntirelyAboveGrid_LeavesLoGreaterThanHi()
        {
            // ClampToGrid is one-sided by design: it pushes lo up if
            // negative and hi down if past the grid, but does NOT swap
            // or collapse out-of-range pairs. A structure whose projected
            // bounds sit entirely above the grid (lo > size-1 AND
            // hi > size-1) ends up with lo unchanged and hi clamped to
            // size-1, leaving lo > hi. Apply() relies on this to detect
            // out-of-grid structures via `ni <= 0` and early-return
            // before `new bool[nk, nj, ni]` throws OverflowException
            // (negative array dims surface as "Arithmetic operation
            // resulted in an overflow").
            int lo = 10, hi = 15;
            VoxelTransformer.ClampToGrid(ref lo, ref hi, size: 5);

            lo.Should().Be(10);
            hi.Should().Be(4);
            (hi - lo + 1).Should().BeLessThan(0, "the empty-range signal Apply() checks for");
        }

        [Fact]
        public void ClampToGrid_StructureEntirelyBelowGrid_LeavesHiLessThanLo()
        {
            // Symmetric case: structure below the grid (both bounds
            // negative). lo clamps to 0, hi stays negative.
            int lo = -10, hi = -5;
            VoxelTransformer.ClampToGrid(ref lo, ref hi, size: 5);

            lo.Should().Be(0);
            hi.Should().Be(-5);
            (hi - lo + 1).Should().BeLessThan(0);
        }

        [Fact]
        public void ClampToGrid_StructureStraddlingGrid_ClampsBothEnds()
        {
            // Sanity: a structure that crosses both boundaries lands
            // exactly on [0, size-1] and yields the full grid span.
            int lo = -3, hi = 99;
            VoxelTransformer.ClampToGrid(ref lo, ref hi, size: 10);

            lo.Should().Be(0);
            hi.Should().Be(9);
        }
    }
}

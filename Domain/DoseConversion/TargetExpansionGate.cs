// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Target-expansion gate for the conservative near-max: the (registration-transposed)
// target contour rasterized over the target's margin-padded bounding box on the
// plan's dose grid, expanded by the anisotropic margin via a distance transform.
// The gate depends only on the plan's dose grid, the target, the margins and the
// registration — not on the OAR — so it is built once per plan per request and
// shared across every analysis structure.
//
// The box is target-centred and covers the whole expansion, so any dose voxel
// outside it is outside the expansion by construction; gate lookups take global
// dose-grid voxel indices and answer per scoring sub-point.
//
// Rasterization is two-level: a dose-grid pass classifies whole voxels, fine cells
// (maskSupersample per voxel per axis) inherit their voxel's value, and only voxels
// whose 3x3x3 neighbourhood is mixed — where the contour can pass — are re-sampled
// fine. Mixed voxels in a row are merged into one span, so one profile per fine
// sub-row covers them all: a few extra samples for far fewer ESAPI calls.
using System;
using System.Collections;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace ReRT.Domain.DoseConversion
{
    public sealed class TargetExpansionGate
    {
        private readonly int _imin, _jmin, _kmin;   // box origin (dose-grid voxel indices)
        private readonly int _ni, _nj, _nk;         // box size (dose voxels)
        private readonly int _sm;                   // mask cells per dose voxel per axis
        private readonly bool[,,] _mask;            // fine target mask (gates when _dist is null)
        private readonly float[,,] _dist;           // normalised distance; null when both margins are zero

        private TargetExpansionGate(int imin, int jmin, int kmin, int ni, int nj, int nk,
            int sm, bool[,,] mask, float[,,] dist)
        {
            _imin = imin; _jmin = jmin; _kmin = kmin;
            _ni = ni; _nj = nj; _nk = nk;
            _sm = sm; _mask = mask; _dist = dist;
        }

        /// <summary>
        /// True when scoring sub-point (<paramref name="si"/>, <paramref name="sj"/>,
        /// <paramref name="sk"/>) of <paramref name="scoreS"/>³ inside dose voxel
        /// (<paramref name="gi"/>, <paramref name="gj"/>, <paramref name="gk"/>) lies
        /// inside the target expansion — or, with zero margins, inside the transposed
        /// target contour itself. Voxels outside the gate's box are outside the
        /// expansion by construction.
        /// </summary>
        public bool InRegion(int gi, int gj, int gk, int si, int sj, int sk, int scoreS)
        {
            int ii = gi - _imin, jj = gj - _jmin, kk = gk - _kmin;
            if (ii < 0 || ii >= _ni || jj < 0 || jj >= _nj || kk < 0 || kk >= _nk) return false;
            int fi = MaskedVoxelSampler.MaskIndex(ii, si, _sm, scoreS);
            int fj = MaskedVoxelSampler.MaskIndex(jj, sj, _sm, scoreS);
            int fk = MaskedVoxelSampler.MaskIndex(kk, sk, _sm, scoreS);
            return _dist != null ? _dist[fk, fj, fi] <= 1.0f : _mask[fk, fj, fi];
        }

        /// <summary>
        /// Builds the gate for one plan: the anisotropic expansion of
        /// <paramref name="target"/> (an ellipsoid <paramref name="inPlaneMarginMm"/>
        /// laterally and <paramref name="supInfMarginMm"/> superior-inferior; a zero
        /// margin means no expansion along that axis, both zero gates on the contour
        /// itself) on the plan's dose grid, reached through
        /// <paramref name="registration"/> (null when the plan already shares the
        /// target's frame). Returns null for a missing target or negative margins.
        /// </summary>
        public static TargetExpansionGate Build(
            Dose dose,
            Structure target,
            double inPlaneMarginMm,
            double supInfMarginMm,
            RegistrationAdapter registration,
            int maskSupersample)
        {
            if (dose == null || target == null || inPlaneMarginMm < 0.0 || supInfMarginMm < 0.0) return null;
            int SM = Math.Max(1, maskSupersample);

            int Xsize = dose.XSize, Ysize = dose.YSize, Zsize = dose.ZSize;
            double Xres = dose.XRes, Yres = dose.YRes, Zres = dose.ZRes;
            VVector o = dose.Origin, xd = dose.XDirection, yd = dose.YDirection, zd = dose.ZDirection;
            double sx = xd.x + yd.x + zd.x;
            double sy = xd.y + yd.y + zd.y;
            double sz = xd.z + yd.z + zd.z;

            // Target bounds live in the target's (plan-sum) frame; bring the eight box
            // corners into the plan frame through the inverse registration and take
            // their axis-aligned envelope (a rigid registration maps a box to a rotated
            // box, so the corner envelope covers it).
            var b = target.MeshGeometry.Bounds;
            double x0 = double.MaxValue, x1 = double.MinValue;
            double y0 = double.MaxValue, y1 = double.MinValue;
            double z0 = double.MaxValue, z1 = double.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new VVector(
                    b.X + ((c & 1) != 0 ? b.SizeX : 0.0),
                    b.Y + ((c & 2) != 0 ? b.SizeY : 0.0),
                    b.Z + ((c & 4) != 0 ? b.SizeZ : 0.0));
                var q = registration != null ? registration.InverseTransformPoint(corner) : corner;
                if (q.x < x0) x0 = q.x; if (q.x > x1) x1 = q.x;
                if (q.y < y0) y0 = q.y; if (q.y > y1) y1 = q.y;
                if (q.z < z0) z0 = q.z; if (q.z > z1) z1 = q.z;
            }

            int imin = VoxelTransformer.GetIndexFromCoordinate(x0, o.x, sx, Xres);
            int imax = VoxelTransformer.GetIndexFromCoordinate(x1, o.x, sx, Xres);
            int jmin = VoxelTransformer.GetIndexFromCoordinate(y0, o.y, sy, Yres);
            int jmax = VoxelTransformer.GetIndexFromCoordinate(y1, o.y, sy, Yres);
            int kmin = VoxelTransformer.GetIndexFromCoordinate(z0, o.z, sz, Zres);
            int kmax = VoxelTransformer.GetIndexFromCoordinate(z1, o.z, sz, Zres);
            SwapIfDescending(ref imin, ref imax);
            SwapIfDescending(ref jmin, ref jmax);
            SwapIfDescending(ref kmin, ref kmax);

            // Pad by the margin (plus one voxel slack) so the box covers the whole
            // expansion; everything outside is gated out by the box test alone.
            int padX = (int)Math.Ceiling(inPlaneMarginMm / Xres) + 1;
            int padY = (int)Math.Ceiling(inPlaneMarginMm / Yres) + 1;
            int padZ = (int)Math.Ceiling(supInfMarginMm / Zres) + 1;
            imin -= padX; imax += padX;
            jmin -= padY; jmax += padY;
            kmin -= padZ; kmax += padZ;
            VoxelTransformer.ClampToGrid(ref imin, ref imax, Xsize);
            VoxelTransformer.ClampToGrid(ref jmin, ref jmax, Ysize);
            VoxelTransformer.ClampToGrid(ref kmin, ref kmax, Zsize);

            int ni = imax - imin + 1, nj = jmax - jmin + 1, nk = kmax - kmin + 1;
            if (ni <= 0 || nj <= 0 || nk <= 0)
                return new TargetExpansionGate(imin, jmin, kmin, 0, 0, 0, SM, new bool[0, 0, 0], null);

            // Coarse pass: the target at the dose-grid voxel centres, one profile per
            // row, endpoints transformed into the target's frame.
            var coarse = new bool[nk, nj, ni];
            double xstart = o.x + imin * Xres * sx;
            double xstop = o.x + imax * Xres * sx;
            for (int kk = 0; kk < nk; kk++)
            {
                double z = o.z + (kmin + kk) * Zres * sz;
                for (int jj = 0; jj < nj; jj++)
                {
                    double y = o.y + (jmin + jj) * Yres * sy;
                    var segStart = new VVector(xstart, y, z);
                    var segStop = new VVector(xstop, y, z);
                    var tStart = registration != null ? registration.TransformPoint(segStart) : segStart;
                    var tStop = registration != null ? registration.TransformPoint(segStop) : segStop;
                    int p = 0;
                    foreach (var pp in target.GetSegmentProfile(tStart, tStop, new BitArray(ni)))
                    {
                        if (p >= ni) break;
                        coarse[kk, jj, p] = pp.Value;
                        p++;
                    }
                }
            }

            bool[,,] mask;
            if (SM == 1)
            {
                mask = coarse;
            }
            else
            {
                // Fine cells inherit their voxel's value (cell centres at
                // gk - 0.5 + (kf + 0.5)/SM, so at SM == supersample they are exactly
                // the scoring sub-points)...
                mask = new bool[nk * SM, nj * SM, ni * SM];
                for (int kk = 0; kk < nk; kk++)
                    for (int jj = 0; jj < nj; jj++)
                        for (int ii = 0; ii < ni; ii++)
                        {
                            if (!coarse[kk, jj, ii]) continue;
                            for (int sk = 0; sk < SM; sk++)
                                for (int sj = 0; sj < SM; sj++)
                                    for (int si = 0; si < SM; si++)
                                        mask[kk * SM + sk, jj * SM + sj, ii * SM + si] = true;
                        }

                // ...and only mixed-3x3x3-neighbourhood voxels — where the contour can
                // pass — are re-sampled at the fine resolution. Mixed voxels in a row
                // are merged into one span from the first to the last, one profile per
                // fine sub-row (cells in between are re-sampled to the values they
                // already inherited): a few extra samples for far fewer ESAPI calls.
                for (int kk = 0; kk < nk; kk++)
                    for (int jj = 0; jj < nj; jj++)
                    {
                        int i0 = -1, i1 = -1;
                        for (int ii = 0; ii < ni; ii++)
                            if (HasMixedNeighbourhood(coarse, kk, jj, ii, nk, nj, ni))
                            {
                                if (i0 < 0) i0 = ii;
                                i1 = ii;
                            }
                        if (i0 < 0) continue;

                        int n = (i1 - i0 + 1) * SM;
                        double xa = o.x + (imin + i0 - 0.5 + 0.5 / SM) * Xres * sx;
                        double xb = o.x + (imin + i1 + 0.5 - 0.5 / SM) * Xres * sx;
                        for (int sk = 0; sk < SM; sk++)
                        {
                            double z = o.z + (kmin + kk - 0.5 + (sk + 0.5) / SM) * Zres * sz;
                            int kf = kk * SM + sk;
                            for (int sj = 0; sj < SM; sj++)
                            {
                                double y = o.y + (jmin + jj - 0.5 + (sj + 0.5) / SM) * Yres * sy;
                                int jf = jj * SM + sj;
                                var segStart = new VVector(xa, y, z);
                                var segStop = new VVector(xb, y, z);
                                var tStart = registration != null ? registration.TransformPoint(segStart) : segStart;
                                var tStop = registration != null ? registration.TransformPoint(segStop) : segStop;
                                int p = 0;
                                foreach (var pp in target.GetSegmentProfile(tStart, tStop, new BitArray(n)))
                                {
                                    if (p >= n) break;
                                    mask[kf, jf, i0 * SM + p] = pp.Value;
                                    p++;
                                }
                            }
                        }
                    }
            }

            // Anisotropic ellipsoidal margin: scale each axis's step weight by its
            // margin so the accumulated distance is the normalised distance, gated at
            // <= 1. Chamfer (plain distance) rather than the exact squared EDT
            // (EuclideanDistanceSq): validated against Eclipse-expanded targets, the
            // chamfer's slight oblique under-reach matches Eclipse's own expansion more
            // closely. With both margins zero there is nothing to expand — the target
            // mask itself is the region (dist stays null).
            float[,,] dist = (inPlaneMarginMm > 0.0 || supInfMarginMm > 0.0)
                ? MaskedVoxelSampler.ChamferDistanceMm(mask,
                    MaskedVoxelSampler.AxisScale(Xres / SM, inPlaneMarginMm),
                    MaskedVoxelSampler.AxisScale(Yres / SM, inPlaneMarginMm),
                    MaskedVoxelSampler.AxisScale(Zres / SM, supInfMarginMm))
                : null;

            return new TargetExpansionGate(imin, jmin, kmin, ni, nj, nk, SM, mask, dist);
        }

        // True when any voxel in the 3x3x3 neighbourhood (clamped at the box edge)
        // differs from the centre — the contour can pass through this voxel, so its
        // fine mask cells need re-sampling rather than inheriting the voxel value.
        private static bool HasMixedNeighbourhood(bool[,,] m, int k, int j, int i, int nk, int nj, int ni)
        {
            bool v = m[k, j, i];
            for (int dk = -1; dk <= 1; dk++)
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int k2 = k + dk, j2 = j + dj, i2 = i + di;
                        if (k2 < 0 || k2 >= nk || j2 < 0 || j2 >= nj || i2 < 0 || i2 >= ni) continue;
                        if (m[k2, j2, i2] != v) return true;
                    }
            return false;
        }

        private static void SwapIfDescending(ref int lo, ref int hi)
        {
            if (lo > hi) { int t = lo; lo = hi; hi = t; }
        }
    }
}

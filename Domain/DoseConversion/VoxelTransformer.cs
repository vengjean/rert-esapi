// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

// Single voxel-by-voxel dose transform: one parameterised flow handles the
// registered/unregistered × EQD2/Physical cases behind an optional
// Registration argument.
//
// Boundary handling (post-dilation): structure masks are dilated by one
// voxel (26-connected — face, edge, AND corner neighbours of the raw
// mask) so voxels whose centres lie just outside a structure but whose
// volume reaches into it still get the structure's alpha/β and discount.
// 6-connected dilation only covered face neighbours, so voxels at edge
// (√2) or corner (√3) diagonals leaked into the body-fallback tier and
// changing the body's discount perturbed adjacent OAR DVH metrics.
// Voxels are tracked in two tiers:
//   * Inner   — voxel center is inside the raw mask.
//   * Halo    — voxel is only in the dilated mask (not the raw mask).
// Within one pass through structures (highest priority first), inner
// voxels of a lower-priority structure can override a higher-priority
// structure's halo claim, because a voxel truly inside structure X must
// belong to X regardless of any halo extending from a higher-priority Y.
// Inner voxels never override other inner voxels — higher-priority wins
// at ties. Halo voxels never override anything.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace ReRT.Domain.DoseConversion
{
    public sealed class VoxelTransformer
    {
        /// <summary>
        /// Set true if any voxel produced an Int32-clamped result during
        /// any <see cref="Apply"/> invocation on this instance. The
        /// pipeline reads this once at the end of a run to surface the
        /// "clamped" message exactly once.
        /// </summary>
        public bool Clamped { get; private set; }

        // Per-output-matrix voxel claims, indexed [k, i, j] exactly like the
        // dose matrix. Values: ClaimFree (0), ClaimHalo (1, a dilated-only
        // spillover write), ClaimInner (2, voxel center inside a structure's
        // raw mask). A lower-priority structure's INNER write overwrites a
        // higher-priority HALO (Inner supersedes Halo); nothing overwrites an
        // Inner. A flat byte grid (rather than per-voxel hash sets) keeps the
        // conversion hot path free of per-voxel allocation + hashing, which
        // matters most for the whole-grid body fallback. Sized lazily to the
        // dose volume in EnsureClaims().
        //
        // Field is `internal` (not private) so ReRT.Tests can verify
        // ResetClaims() clears it without needing ESAPI mocks to drive Apply.
        internal byte[,,] _claim;
        internal const byte ClaimFree = 0;
        internal const byte ClaimHalo = 1;
        internal const byte ClaimInner = 2;

        /// <summary>
        /// Clear the per-output-matrix voxel claim grid. Call before
        /// iterating the structures for a new (plan, mode) pair so a
        /// previous sweep's claims don't block writes to the fresh
        /// destination dose matrix.
        /// </summary>
        public void ResetClaims()
        {
            if (_claim != null) Array.Clear(_claim, 0, _claim.Length);
        }

        // Ensure the claim grid matches the current dose volume. Allocated
        // once and reused across the sweep's structures; reallocated only if
        // a later (plan, mode) has different dose dimensions. A fresh array
        // is zero-initialised (all ClaimFree), so no explicit clear here.
        internal void EnsureClaims(int xsize, int ysize, int zsize)
        {
            if (_claim == null ||
                _claim.GetLength(0) != zsize ||
                _claim.GetLength(1) != xsize ||
                _claim.GetLength(2) != ysize)
            {
                _claim = new byte[zsize, xsize, ysize];
            }
        }

        public void Apply(
            Structure structure,
            double alphaBeta,
            short numFractions,
            int[,,] doseMatrixOrig,
            int[,,] doseMatrixOut,
            double voxelToGy,
            Dose sourceDose,
            DoseFormula formula,
            double discount,
            double fractionScaling,
            RegistrationAdapter registration = null,
            bool isFallbackContour = false)
        {
            int Xsize = sourceDose.XSize;
            int Ysize = sourceDose.YSize;
            int Zsize = sourceDose.ZSize;

            // Claim grid is sized to the full dose volume and shared across
            // the sweep's structures (cross-structure priority arbitration).
            EnsureClaims(Xsize, Ysize, Zsize);

            double Xres = sourceDose.XRes;
            double Yres = sourceDose.YRes;
            double Zres = sourceDose.ZRes;
            VVector Xdir = sourceDose.XDirection;
            VVector Ydir = sourceDose.YDirection;
            VVector Zdir = sourceDose.ZDirection;
            VVector doseOrigin = sourceDose.Origin;

            // Valid only for HFS, HFP, FFS, FFP orientations that don't mix x,y,z.
            double sx = Xdir.x + Ydir.x + Zdir.x;
            double sy = Xdir.y + Ydir.y + Zdir.y;
            double sz = Xdir.z + Ydir.z + Zdir.z;

            var bounds = structure.MeshGeometry.Bounds;
            double bx = bounds.X, by = bounds.Y, bz = bounds.Z;
            double sizeX = bounds.SizeX, sizeY = bounds.SizeY, sizeZ = bounds.SizeZ;

            // Source-space envelope of the structure bounds. Unregistered:
            // the mesh is in the dose frame already. Registered: invert-
            // transform all 8 corners to handle rotations correctly.
            double x0, x1, y0, y1, z0, z1;
            if (registration == null)
            {
                x0 = bx; x1 = bx + sizeX;
                y0 = by; y1 = by + sizeY;
                z0 = bz; z1 = bz + sizeZ;
            }
            else
            {
                var localCorners = new[]
                {
                    new VVector(bx,         by,         bz),
                    new VVector(bx+sizeX,   by,         bz),
                    new VVector(bx,         by+sizeY,   bz),
                    new VVector(bx,         by,         bz+sizeZ),
                    new VVector(bx+sizeX,   by+sizeY,   bz),
                    new VVector(bx+sizeX,   by,         bz+sizeZ),
                    new VVector(bx,         by+sizeY,   bz+sizeZ),
                    new VVector(bx+sizeX,   by+sizeY,   bz+sizeZ),
                };
                var transformed = localCorners.Select(c => registration.InverseTransformPoint(c)).ToList();
                x0 = transformed.Min(c => c.x); x1 = transformed.Max(c => c.x);
                y0 = transformed.Min(c => c.y); y1 = transformed.Max(c => c.y);
                z0 = transformed.Min(c => c.z); z1 = transformed.Max(c => c.z);
            }

            int imin = GetIndexFromCoordinate(x0, doseOrigin.x, sx, Xres);
            int imax = GetIndexFromCoordinate(x1, doseOrigin.x, sx, Xres);
            int jmin = GetIndexFromCoordinate(y0, doseOrigin.y, sy, Yres);
            int jmax = GetIndexFromCoordinate(y1, doseOrigin.y, sy, Yres);
            int kmin = GetIndexFromCoordinate(z0, doseOrigin.z, sz, Zres);
            int kmax = GetIndexFromCoordinate(z1, doseOrigin.z, sz, Zres);

            SwapIfDescending(ref imin, ref imax);
            SwapIfDescending(ref jmin, ref jmax);
            SwapIfDescending(ref kmin, ref kmax);

            // ±2 voxel pad, then clamp to the grid.
            imax += 2; imin -= 2;
            jmax += 2; jmin -= 2;
            kmax += 2; kmin -= 2;
            ClampToGrid(ref imin, ref imax, Xsize);
            ClampToGrid(ref jmin, ref jmax, Ysize);
            ClampToGrid(ref kmin, ref kmax, Zsize);

            int ni = imax - imin + 1;
            int nj = jmax - jmin + 1;
            int nk = kmax - kmin + 1;

            // Structure projects to an empty range on at least one axis —
            // either it lies entirely outside the dose grid, or registration
            // produced bounds that collapse after clamping. A negative
            // dimension would make the buffered mask allocation
            // `new bool[nk,nj,ni]` throw "Arithmetic operation resulted in an
            // overflow", so skip the structure (it contributes no voxels).
            if (ni <= 0 || nj <= 0 || nk <= 0) return;

            double effectiveDiscount = (100.0 - discount) / 100.0;

            // Fallback contour (body/external) — special-cased: this
            // structure is the catch-all envelope, so any other
            // structure's inner OR halo should win at boundary voxels.
            // We skip dilation entirely and claim only at halo tier so
            // even if the pipeline accidentally processed us before
            // another structure, a later inner claim would override us.
            // The pipeline orders fallback contours last regardless.
            //
            // The body spans the whole grid, so a buffered `bool[nk, nj, ni]`
            // mask can exceed the .NET multidim array element-count limit
            // (the multiplication overflows int and the runtime throws
            // "Arithmetic operation resulted in an overflow"). Since we never
            // dilate the fallback contour, there's no need to buffer — we
            // scan row-by-row and write claimable voxels in the same pass.
            if (isFallbackContour)
            {
                for (int kk = 0; kk < nk; kk++)
                {
                    int k = kmin + kk;
                    for (int jj = 0; jj < nj; jj++)
                    {
                        int j = jmin + jj;
                        double y = doseOrigin.y + j * Yres * sy;
                        double z = doseOrigin.z + k * Zres * sz;
                        double xstart = doseOrigin.x + imin * Xres * sx;
                        double xstop  = doseOrigin.x + imax * Xres * sx;

                        VVector segStart = new VVector(xstart, y, z);
                        VVector segStop  = new VVector(xstop,  y, z);

                        if (registration != null)
                        {
                            segStart = registration.TransformPoint(segStart);
                            segStop  = registration.TransformPoint(segStop);
                        }

                        var profile = structure
                            .GetSegmentProfile(segStart, segStop, new BitArray(ni));
                        int p = 0;
                        foreach (var pp in profile)
                        {
                            if (p >= ni) break;
                            if (pp.Value)
                            {
                                int i = imin + p;
                                if (_claim[k, i, j] == ClaimFree)
                                {
                                    WriteVoxel(doseMatrixOrig, doseMatrixOut, k, i, j,
                                               fractionScaling, voxelToGy, alphaBeta, numFractions,
                                               formula, effectiveDiscount);
                                    _claim[k, i, j] = ClaimHalo;
                                }
                            }
                            p++;
                        }
                    }
                }
                return;
            }

            // Step 1 — populate the raw structure mask from per-row
            // GetSegmentProfile queries. mask[k, j, i] reflects whether the
            // (k, j, i) voxel's CENTER lies inside the structure. Only
            // non-fallback structures reach here, so the bounds are tight
            // enough that the multidim allocation comfortably fits.
            var mask = new bool[nk, nj, ni];
            for (int kk = 0; kk < nk; kk++)
            {
                int k = kmin + kk;
                for (int jj = 0; jj < nj; jj++)
                {
                    int j = jmin + jj;
                    double y = doseOrigin.y + j * Yres * sy;
                    double z = doseOrigin.z + k * Zres * sz;
                    double xstart = doseOrigin.x + imin * Xres * sx;
                    double xstop  = doseOrigin.x + imax * Xres * sx;

                    VVector segStart = new VVector(xstart, y, z);
                    VVector segStop  = new VVector(xstop,  y, z);

                    // Registered: line up the profile with where the
                    // structure actually lives (registered frame).
                    if (registration != null)
                    {
                        segStart = registration.TransformPoint(segStart);
                        segStop  = registration.TransformPoint(segStop);
                    }

                    var profile = structure
                        .GetSegmentProfile(segStart, segStop, new BitArray(ni));
                    int p = 0;
                    foreach (var pp in profile)
                    {
                        if (p >= ni) break;
                        mask[kk, jj, p] = pp.Value;
                        p++;
                    }
                }
            }

            // Step 2 — 1-voxel 3D dilation (26-connected, i.e. a 3×3×3
            // cube structuring element). Halo voxels are those covered by
            // the dilated mask but NOT the raw mask. 26-connected covers
            // face + edge + corner neighbours, matching the partial-volume
            // sampling footprint ESAPI's DVH uses around the structure
            // mesh; 6-connected dilation would leave diagonal-adjacent
            // voxels claimable by the body-fallback tier and let the
            // body's discount perturb adjacent OAR metrics.
            var dilated = Dilate26Connected(mask);

            // Step 3 — single sweep over the dilated bbox. Inner and halo
            // voxels are disjoint within one structure (halo = dilated AND
            // NOT mask), so one pass handles both by branching on mask[].
            // The "lower-priority inner overrides higher-priority halo" rule
            // is carried by (a) the pipeline iterating structures highest-
            // priority-first and (b) an inner write overwriting any prior
            // HALO claim (ClaimInner supersedes ClaimHalo) — not by a
            // separate inner/halo two-pass split, which only added a second
            // full traversal of the same box.
            for (int kk = 0; kk < nk; kk++)
            {
                int k = kmin + kk;
                for (int jj = 0; jj < nj; jj++)
                {
                    int j = jmin + jj;
                    for (int ii = 0; ii < ni; ii++)
                    {
                        int i = imin + ii;
                        if (mask[kk, jj, ii])
                        {
                            // Inner: blocked only by a higher-priority inner
                            // claim; overwrites free OR a higher-priority halo.
                            if (_claim[k, i, j] != ClaimInner)
                            {
                                WriteVoxel(doseMatrixOrig, doseMatrixOut, k, i, j,
                                           fractionScaling, voxelToGy, alphaBeta, numFractions,
                                           formula, effectiveDiscount);
                                _claim[k, i, j] = ClaimInner;
                            }
                        }
                        else if (dilated[kk, jj, ii])
                        {
                            // Halo: claims only a free voxel; never overrides.
                            if (_claim[k, i, j] == ClaimFree)
                            {
                                WriteVoxel(doseMatrixOrig, doseMatrixOut, k, i, j,
                                           fractionScaling, voxelToGy, alphaBeta, numFractions,
                                           formula, effectiveDiscount);
                                _claim[k, i, j] = ClaimHalo;
                            }
                        }
                    }
                }
            }
        }

        private void WriteVoxel(int[,,] doseMatrixOrig, int[,,] doseMatrixOut,
                                int k, int i, int j,
                                double fractionScaling, double voxelToGy,
                                double alphaBeta, short numFractions,
                                DoseFormula formula, double effectiveDiscount)
        {
            int srcVoxel = doseMatrixOrig[k, i, j];
            int calcVal = ApplyFormulaToVoxel(srcVoxel, fractionScaling, voxelToGy,
                                              alphaBeta, numFractions, formula);
            if (calcVal == int.MaxValue) Clamped = true;
            double discounted = calcVal * effectiveDiscount;
            doseMatrixOut[k, i, j] = (int)discounted;
        }

        /// <summary>
        /// 26-connected 1-voxel dilation of a 3D boolean mask using a
        /// 3×3×3 cube structuring element. A voxel is set in the output
        /// if any voxel in its 3×3×3 neighbourhood (face + edge + corner
        /// neighbours) is set in the input. Array-edge voxels see only
        /// the neighbours that exist (the 3×3×3 cube is clamped to the
        /// array). This is what <see cref="Apply"/> uses to grow the
        /// halo so the structure's claim covers every voxel ESAPI's
        /// partial-volume DVH sampling could attribute to the structure.
        /// </summary>
        public static bool[,,] Dilate26Connected(bool[,,] src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            int nk = src.GetLength(0);
            int nj = src.GetLength(1);
            int ni = src.GetLength(2);
            var dst = new bool[nk, nj, ni];
            // Forward propagation: for each set voxel, mark its 3×3×3
            // neighbourhood in dst. Cheaper than destination-iteration
            // when the mask is sparse (structures usually fill < half
            // their bounding box).
            for (int k = 0; k < nk; k++)
            {
                for (int j = 0; j < nj; j++)
                {
                    for (int i = 0; i < ni; i++)
                    {
                        if (!src[k, j, i]) continue;
                        int k0 = k > 0      ? k - 1 : 0;
                        int k1 = k < nk - 1 ? k + 1 : nk - 1;
                        int j0 = j > 0      ? j - 1 : 0;
                        int j1 = j < nj - 1 ? j + 1 : nj - 1;
                        int i0 = i > 0      ? i - 1 : 0;
                        int i1 = i < ni - 1 ? i + 1 : ni - 1;
                        for (int kk = k0; kk <= k1; kk++)
                            for (int jj = j0; jj <= j1; jj++)
                                for (int ii = i0; ii <= i1; ii++)
                                    dst[kk, jj, ii] = true;
                    }
                }
            }
            return dst;
        }

        /// <summary>
        /// 6-connected 1-voxel dilation of a 3D boolean mask. A voxel is
        /// set in the output if it OR any of its 6 face-neighbours (±i,
        /// ±j, ±k) is set in the input. Array-edge voxels see only the
        /// neighbours that exist. Retained for reference and as the
        /// minimal-footprint alternative; <see cref="Apply"/> uses
        /// <see cref="Dilate26Connected"/> to cover diagonal neighbours
        /// as well.
        /// </summary>
        public static bool[,,] Dilate6Connected(bool[,,] src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            int nk = src.GetLength(0);
            int nj = src.GetLength(1);
            int ni = src.GetLength(2);
            var dst = new bool[nk, nj, ni];
            for (int k = 0; k < nk; k++)
            {
                for (int j = 0; j < nj; j++)
                {
                    for (int i = 0; i < ni; i++)
                    {
                        if (src[k, j, i] ||
                            (k > 0      && src[k - 1, j, i]) ||
                            (k < nk - 1 && src[k + 1, j, i]) ||
                            (j > 0      && src[k, j - 1, i]) ||
                            (j < nj - 1 && src[k, j + 1, i]) ||
                            (i > 0      && src[k, j, i - 1]) ||
                            (i < ni - 1 && src[k, j, i + 1]))
                        {
                            dst[k, j, i] = true;
                        }
                    }
                }
            }
            return dst;
        }

        /// <summary>
        /// Convert a source voxel value through a Gy-domain formula and
        /// back to voxel units, with Int32 overflow clamping. Pure
        /// arithmetic, no ESAPI: the unit tests exercise it directly.
        /// </summary>
        public static int ApplyFormulaToVoxel(
            int sourceVoxel,
            double fractionScaling,
            double voxelToGy,
            double alphaBeta,
            int numFractions,
            DoseFormula formula)
        {
            // Voxel → Gy, with fraction scaling baked in:
            // formula(dose * fractionScaling, ...).
            double doseGy = sourceVoxel * fractionScaling * voxelToGy;
            double resultGy;
            try
            {
                resultGy = formula(doseGy, alphaBeta, numFractions, 1.0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Formula failed on voxel={sourceVoxel}, doseGy={doseGy}, αβ={alphaBeta}, fx={numFractions}.", ex);
            }
            if (double.IsNaN(resultGy) || double.IsInfinity(resultGy))
                throw new InvalidOperationException(
                    $"Formula returned non-finite result on voxel={sourceVoxel}, doseGy={doseGy}, αβ={alphaBeta}, fx={numFractions}.");

            // Gy → voxel.
            double resultVoxel = voxelToGy != 0.0 ? resultGy / voxelToGy : 0.0;
            if (resultVoxel >= int.MaxValue) return int.MaxValue;
            if (resultVoxel <= int.MinValue) return int.MinValue;
            return Convert.ToInt32(resultVoxel);
        }

        public static int GetIndexFromCoordinate(double coord, double origin, double direction, double res)
            => Convert.ToInt32((coord - origin) / (direction * res));

        private static void SwapIfDescending(ref int lo, ref int hi)
        {
            if (lo > hi) { int t = lo; lo = hi; hi = t; }
        }

        // One-sided clamp by design: lo gets pushed up to 0 if it's
        // negative, hi gets pushed down to size-1 if it's past the grid.
        // For a structure entirely outside the grid (e.g., lo=10, hi=15,
        // size=5), both inputs stay where they are except hi → 4, leaving
        // lo > hi. Apply() detects that empty range and short-circuits
        // before allocating any per-structure buffers. Exposed `internal`
        // so the regression test can pin this contract.
        internal static void ClampToGrid(ref int lo, ref int hi, int size)
        {
            if (lo < 0) lo = 0;
            if (hi > size - 1) hi = size - 1;
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Direct voxel near-max (D0.1cc) of a plan's dose over (OAR within a margin of a
// target), with no ESAPI structure creation. The dose grid and OAR live in the
// plan frame; the target-expansion gating is delegated to a TargetExpansionGate,
// built once per plan and shared across OARs.
//
// To converge with Eclipse's DVH, the region is supersampled: each voxel is split
// into S^3 sub-points, and every sub-point inside the OAR contributes (dose
// interpolated at that sub-point, sub-voxel volume). This both partial-volume weights
// the OAR boundary AND scores the dose at the sub-point's true location, so in a steep
// gradient the part of a boundary voxel that lies inside the OAR is scored at its own
// (often cooler) dose rather than the hot voxel-centre value — which otherwise reads
// higher than Eclipse. The expansion gate is consulted per sub-point too, so the
// margin boundary gets the same partial-volume treatment as the OAR boundary.
// D0.1cc is read from the volume-weighted cumulative curve.
//
// ESAPI-coupled sampling is validated by the Eclipse smoke test; the weighted
// near-max and the distance transforms are pure and unit-tested.
using System;
using System.Collections;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace ReRT.Domain.DoseConversion
{
    public static class MaskedVoxelSampler
    {
        /// <summary>
        /// Near-max (D0.1cc) physical dose (Gy) of <paramref name="dose"/> over the voxels
        /// inside <paramref name="oar"/>. When <paramref name="global"/> is false each
        /// scoring sub-point is additionally gated by <paramref name="gate"/> — the
        /// target-expansion gate built once per plan via
        /// <see cref="TargetExpansionGate.Build"/> and shared across OARs (a null gate
        /// returns 0); when true the whole OAR is used and the gate is ignored. The region
        /// is split into <paramref name="supersample"/>³ sub-points per voxel; each
        /// sub-point inside both the OAR and the expansion is scored at the dose
        /// interpolated at its own location, so partial boundary volumes and steep
        /// gradients track Eclipse's DVH. Returns 0 when the region is empty or receives
        /// no dose.
        /// </summary>
        public static double D01ccWithinTargetMargin(
            Dose dose,
            int[,,] doseMatrix,
            double voxelToGy,
            Structure oar,
            TargetExpansionGate gate,
            bool global = false,
            int supersample = 3,
            double thresholdCc = 0.1)
        {
            if (dose == null || doseMatrix == null || oar == null) return 0.0;
            if (!global && gate == null) return 0.0;
            int S = Math.Max(1, supersample);

            int Xsize = dose.XSize, Ysize = dose.YSize, Zsize = dose.ZSize;
            double Xres = dose.XRes, Yres = dose.YRes, Zres = dose.ZRes;
            VVector o = dose.Origin, xd = dose.XDirection, yd = dose.YDirection, zd = dose.ZDirection;
            double sx = xd.x + yd.x + zd.x;
            double sy = xd.y + yd.y + zd.y;
            double sz = xd.z + yd.z + zd.z;

            // OAR bounds (plan frame == dose frame), padded one voxel for the boundary
            // neighbourhood tests; the expansion gate carries its own target-centred box.
            var b = oar.MeshGeometry.Bounds;
            int imin = VoxelTransformer.GetIndexFromCoordinate(b.X, o.x, sx, Xres);
            int imax = VoxelTransformer.GetIndexFromCoordinate(b.X + b.SizeX, o.x, sx, Xres);
            int jmin = VoxelTransformer.GetIndexFromCoordinate(b.Y, o.y, sy, Yres);
            int jmax = VoxelTransformer.GetIndexFromCoordinate(b.Y + b.SizeY, o.y, sy, Yres);
            int kmin = VoxelTransformer.GetIndexFromCoordinate(b.Z, o.z, sz, Zres);
            int kmax = VoxelTransformer.GetIndexFromCoordinate(b.Z + b.SizeZ, o.z, sz, Zres);
            SwapIfDescending(ref imin, ref imax);
            SwapIfDescending(ref jmin, ref jmax);
            SwapIfDescending(ref kmin, ref kmax);

            imin -= 1; imax += 1;
            jmin -= 1; jmax += 1;
            kmin -= 1; kmax += 1;
            VoxelTransformer.ClampToGrid(ref imin, ref imax, Xsize);
            VoxelTransformer.ClampToGrid(ref jmin, ref jmax, Ysize);
            VoxelTransformer.ClampToGrid(ref kmin, ref kmax, Zsize);

            int ni = imax - imin + 1, nj = jmax - jmin + 1, nk = kmax - kmin + 1;
            if (ni <= 0 || nj <= 0 || nk <= 0) return 0.0;

            var oarMask = new bool[nk, nj, ni];

            double xstart = o.x + imin * Xres * sx;
            double xstop = o.x + imax * Xres * sx;

            for (int kk = 0; kk < nk; kk++)
            {
                int k = kmin + kk;
                double z = o.z + k * Zres * sz;
                for (int jj = 0; jj < nj; jj++)
                {
                    int j = jmin + jj;
                    double y = o.y + j * Yres * sy;
                    int p = 0;
                    foreach (var pp in oar.GetSegmentProfile(new VVector(xstart, y, z), new VVector(xstop, y, z), new BitArray(ni)))
                    {
                        if (p >= ni) break;
                        oarMask[kk, jj, p] = pp.Value;
                        p++;
                    }
                }
            }

            double voxelVolumeCc = (Xres * Yres * Zres) / 1000.0;
            double subVolumeCc = voxelVolumeCc / (S * S * S);
            var samples = new List<DoseVol>();
            for (int kk = 0; kk < nk; kk++)
            {
                int gk = kmin + kk;
                for (int jj = 0; jj < nj; jj++)
                {
                    int gj = jmin + jj;
                    for (int ii = 0; ii < ni; ii++)
                    {
                        bool interior = IsInterior(oarMask, kk, jj, ii, nk, nj, ni);
                        if (!interior && !IsNearOar(oarMask, kk, jj, ii, nk, nj, ni)) continue;
                        int gi = imin + ii;

                        // Split the voxel into S^3 sub-points; each sub-point inside both
                        // the OAR and the target expansion contributes the dose interpolated
                        // at its own location (so a steep gradient is followed), weighted by
                        // the sub-voxel volume. Interior voxels are wholly inside the OAR, so
                        // their OAR membership test is skipped; the expansion gate is a
                        // per-sub-point lookup, so the margin boundary is partial-volume
                        // weighted like the OAR boundary.
                        for (int sk = 0; sk < S; sk++)
                        {
                            double fk = gk - 0.5 + (sk + 0.5) / S;
                            double z = o.z + fk * Zres * sz;
                            for (int sj = 0; sj < S; sj++)
                            {
                                double fj = gj - 0.5 + (sj + 0.5) / S;
                                double y = o.y + fj * Yres * sy;

                                // Sub-row wholly outside the expansion: skip before paying
                                // for the OAR sub-profile.
                                if (!global)
                                {
                                    bool any = false;
                                    for (int si = 0; si < S; si++)
                                        if (gate.InRegion(gi, gj, gk, si, sj, sk, S)) { any = true; break; }
                                    if (!any) continue;
                                }

                                bool[] inside = null;
                                if (!interior)
                                {
                                    double xa = o.x + (gi - 0.5 + 0.5 / S) * Xres * sx;
                                    double xb = o.x + (gi + 0.5 - 0.5 / S) * Xres * sx;
                                    inside = new bool[S];
                                    int p = 0;
                                    foreach (var pp in oar.GetSegmentProfile(new VVector(xa, y, z), new VVector(xb, y, z), new BitArray(S)))
                                    {
                                        if (p >= S) break;
                                        inside[p] = pp.Value;
                                        p++;
                                    }
                                }

                                for (int si = 0; si < S; si++)
                                {
                                    if (!interior && !inside[si]) continue;
                                    if (!global && !gate.InRegion(gi, gj, gk, si, sj, sk, S)) continue;
                                    double fi = gi - 0.5 + (si + 0.5) / S;
                                    double doseRaw = SampleTrilinear(doseMatrix, fk, fi, fj, Zsize, Xsize, Ysize);
                                    samples.Add(new DoseVol { DoseGy = (float)(doseRaw * voxelToGy), VolCc = (float)subVolumeCc });
                                }
                            }
                        }
                    }
                }
            }

            return WeightedNearMaxGy(samples, thresholdCc);
        }

        /// <summary>One voxel's contribution: a dose and the (possibly partial) volume at it.</summary>
        public struct DoseVol { public float DoseGy; public float VolCc; }

        /// <summary>
        /// Normalised step weight for one voxel step along an axis. A zero margin
        /// means no expansion along that axis: a single step along it must overshoot
        /// the &lt;= 1 region gate on its own, so any weight above 1 blocks the axis
        /// (contributions accumulate non-negatively in both the chamfer and the
        /// squared transform).
        /// </summary>
        internal static double AxisScale(double resMm, double marginMm)
            => marginMm > 0.0 ? resMm / marginMm : 2.0;

        /// <summary>
        /// Index of the mask-grid cell (<paramref name="maskS"/> cells per dose voxel)
        /// containing scoring sub-point <paramref name="sub"/> of dose voxel
        /// <paramref name="coarse"/>: the sub-point sits at fraction
        /// (sub + 0.5) / <paramref name="scoreS"/> across the voxel. At maskS == 1 every
        /// sub-point maps to the voxel's own cell; at maskS == scoreS each sub-point
        /// gets its own. Pure — unit-tested.
        /// </summary>
        internal static int MaskIndex(int coarse, int sub, int maskS, int scoreS)
            => coarse * maskS + ((2 * sub + 1) * maskS) / (2 * scoreS);

        /// <summary>
        /// Volume-weighted near-max (Gy): the dose whose cumulative (hottest-first)
        /// volume reaches <paramref name="thresholdCc"/>, linearly interpolated on the
        /// cumulative curve. When less than the threshold volume is present, the coldest
        /// sampled dose is returned. Returns 0 for an empty bag. Pure — unit-tested.
        /// </summary>
        public static double WeightedNearMaxGy(List<DoseVol> samples, double thresholdCc = 0.1)
        {
            if (samples == null || samples.Count == 0) return 0.0;
            samples.Sort((a, c) => c.DoseGy.CompareTo(a.DoseGy)); // descending (hottest first)

            double cum = 0.0;
            double prevDose = samples[0].DoseGy;
            foreach (var s in samples)
            {
                if (s.VolCc <= 0.0) continue;
                double next = cum + s.VolCc;
                if (next >= thresholdCc)
                {
                    // The threshold crosses while accumulating this sample. Interpolate
                    // the cumulative curve between the previous dose level (volume = cum)
                    // and this one (volume = next).
                    double span = next - cum;
                    double frac = span > 0 ? (thresholdCc - cum) / span : 0.0;
                    if (frac < 0) frac = 0; if (frac > 1) frac = 1;
                    return prevDose + (s.DoseGy - prevDose) * frac;
                }
                cum = next;
                prevDose = s.DoseGy;
            }
            return prevDose; // total volume below threshold → coldest dose present
        }

        // Trilinear interpolation of the dose matrix (indexed [k, i, j]) at the
        // fractional voxel coordinate (fk, fi, fj). Fractional coordinates are clamped
        // into the grid so sub-points at the edge stay valid. Returns the interpolated
        // raw voxel value; the caller scales by voxelToGy.
        internal static double SampleTrilinear(int[,,] m, double fk, double fi, double fj, int nk, int ni, int nj)
        {
            if (fk < 0) fk = 0; else if (fk > nk - 1) fk = nk - 1;
            if (fi < 0) fi = 0; else if (fi > ni - 1) fi = ni - 1;
            if (fj < 0) fj = 0; else if (fj > nj - 1) fj = nj - 1;

            int k0 = (int)Math.Floor(fk), k1 = Math.Min(k0 + 1, nk - 1); double wk = fk - k0;
            int i0 = (int)Math.Floor(fi), i1 = Math.Min(i0 + 1, ni - 1); double wi = fi - i0;
            int j0 = (int)Math.Floor(fj), j1 = Math.Min(j0 + 1, nj - 1); double wj = fj - j0;

            double c00 = m[k0, i0, j0] * (1 - wj) + m[k0, i0, j1] * wj;
            double c01 = m[k0, i1, j0] * (1 - wj) + m[k0, i1, j1] * wj;
            double c10 = m[k1, i0, j0] * (1 - wj) + m[k1, i0, j1] * wj;
            double c11 = m[k1, i1, j0] * (1 - wj) + m[k1, i1, j1] * wj;

            double c0 = c00 * (1 - wi) + c01 * wi;
            double c1 = c10 * (1 - wi) + c11 * wi;
            return c0 * (1 - wk) + c1 * wk;
        }

        private static bool IsInterior(bool[,,] m, int k, int j, int i, int nk, int nj, int ni)
        {
            if (!m[k, j, i]) return false;
            if (k == 0 || k == nk - 1 || j == 0 || j == nj - 1 || i == 0 || i == ni - 1) return false;
            return m[k - 1, j, i] && m[k + 1, j, i]
                && m[k, j - 1, i] && m[k, j + 1, i]
                && m[k, j, i - 1] && m[k, j, i + 1];
        }

        private static bool IsNearOar(bool[,,] m, int k, int j, int i, int nk, int nj, int ni)
        {
            if (m[k, j, i]) return true;
            if (k > 0 && m[k - 1, j, i]) return true;
            if (k < nk - 1 && m[k + 1, j, i]) return true;
            if (j > 0 && m[k, j - 1, i]) return true;
            if (j < nj - 1 && m[k, j + 1, i]) return true;
            if (i > 0 && m[k, j, i - 1]) return true;
            if (i < ni - 1 && m[k, j, i + 1]) return true;
            return false;
        }

        /// <summary>
        /// Exact squared Euclidean distance transform of a boolean seed mask
        /// (Felzenszwalb–Huttenlocher, separable, O(N)) with per-axis voxel step
        /// weights (dim0→rz, dim1→ry, dim2→rx). Seed voxels are squared distance 0;
        /// every other voxel gets the exact squared weighted distance to its nearest
        /// seed — exact in every direction, where a chamfer overestimates between
        /// lattice directions and under-reaches the margin. With margin-normalised
        /// weights (<see cref="AxisScale"/>) the result is the squared normalised
        /// distance, gated at &lt;= 1. Pure — unit-tested.
        /// </summary>
        public static float[,,] EuclideanDistanceSq(bool[,,] seed, double rx, double ry, double rz)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            int nk = seed.GetLength(0), nj = seed.GetLength(1), ni = seed.GetLength(2);
            const float INF = 1e10f;
            var d = new float[nk, nj, ni];
            for (int k = 0; k < nk; k++)
                for (int j = 0; j < nj; j++)
                    for (int i = 0; i < ni; i++)
                        d[k, j, i] = seed[k, j, i] ? 0f : INF;

            int nmax = Math.Max(ni, Math.Max(nj, nk));
            var f = new float[nmax];
            var dRow = new float[nmax];
            var v = new int[nmax];
            var z = new double[nmax + 1];

            for (int k = 0; k < nk; k++)            // pass along i (x)
                for (int j = 0; j < nj; j++)
                {
                    for (int i = 0; i < ni; i++) f[i] = d[k, j, i];
                    DistanceSq1D(f, ni, rx * rx, dRow, v, z);
                    for (int i = 0; i < ni; i++) d[k, j, i] = dRow[i];
                }
            for (int k = 0; k < nk; k++)            // pass along j (y)
                for (int i = 0; i < ni; i++)
                {
                    for (int j = 0; j < nj; j++) f[j] = d[k, j, i];
                    DistanceSq1D(f, nj, ry * ry, dRow, v, z);
                    for (int j = 0; j < nj; j++) d[k, j, i] = dRow[j];
                }
            for (int j = 0; j < nj; j++)            // pass along k (z)
                for (int i = 0; i < ni; i++)
                {
                    for (int k = 0; k < nk; k++) f[k] = d[k, j, i];
                    DistanceSq1D(f, nk, rz * rz, dRow, v, z);
                    for (int k = 0; k < nk; k++) d[k, j, i] = dRow[k];
                }
            return d;
        }

        // One 1D pass of the separable squared-distance transform:
        // dOut[q] = min over p of (w2 * (q - p)^2 + f[p]), computed via the lower
        // envelope of the parabolas rooted at each p (v: envelope roots, z: the
        // positions where consecutive envelope parabolas intersect).
        private static void DistanceSq1D(float[] f, int n, double w2, float[] dOut, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    int p = v[k];
                    s = ((f[q] + w2 * q * (double)q) - (f[p] + w2 * p * (double)p)) / (2.0 * w2 * (q - p));
                    if (s > z[k]) break;
                    k--;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - (double)v[k];
                dOut[q] = (float)(w2 * dq * dq + f[v[k]]);
            }
        }

        /// <summary>
        /// Two-pass chamfer distance transform (mm) of a boolean seed mask on an
        /// anisotropic grid (dim0→Zres, dim1→Yres, dim2→Xres). Seed voxels are
        /// distance 0; every other voxel gets the approximate Euclidean distance to the
        /// nearest seed. Pure — unit-tested.
        /// </summary>
        public static float[,,] ChamferDistanceMm(bool[,,] seed, double rx, double ry, double rz)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            int nk = seed.GetLength(0), nj = seed.GetLength(1), ni = seed.GetLength(2);
            const float INF = 1e30f;
            var dist = new float[nk, nj, ni];
            for (int k = 0; k < nk; k++)
                for (int j = 0; j < nj; j++)
                    for (int i = 0; i < ni; i++)
                        dist[k, j, i] = seed[k, j, i] ? 0f : INF;

            var fwd = BuildOffsets(rx, ry, rz, causal: true);
            for (int k = 0; k < nk; k++)
                for (int j = 0; j < nj; j++)
                    for (int i = 0; i < ni; i++)
                    {
                        float d = dist[k, j, i];
                        if (d == 0f) continue;
                        foreach (var off in fwd)
                        {
                            int k2 = k + off.dk, j2 = j + off.dj, i2 = i + off.di;
                            if (k2 < 0 || k2 >= nk || j2 < 0 || j2 >= nj || i2 < 0 || i2 >= ni) continue;
                            float cand = dist[k2, j2, i2] + off.w;
                            if (cand < d) d = cand;
                        }
                        dist[k, j, i] = d;
                    }

            var bwd = BuildOffsets(rx, ry, rz, causal: false);
            for (int k = nk - 1; k >= 0; k--)
                for (int j = nj - 1; j >= 0; j--)
                    for (int i = ni - 1; i >= 0; i--)
                    {
                        float d = dist[k, j, i];
                        if (d == 0f) continue;
                        foreach (var off in bwd)
                        {
                            int k2 = k + off.dk, j2 = j + off.dj, i2 = i + off.di;
                            if (k2 < 0 || k2 >= nk || j2 < 0 || j2 >= nj || i2 < 0 || i2 >= ni) continue;
                            float cand = dist[k2, j2, i2] + off.w;
                            if (cand < d) d = cand;
                        }
                        dist[k, j, i] = d;
                    }

            return dist;
        }

        private struct Offset { public int dk, dj, di; public float w; }

        private static List<Offset> BuildOffsets(double rx, double ry, double rz, bool causal)
        {
            var list = new List<Offset>(13);
            for (int dk = -1; dk <= 1; dk++)
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (dk == 0 && dj == 0 && di == 0) continue;
                        bool isCausal = dk < 0 || (dk == 0 && (dj < 0 || (dj == 0 && di < 0)));
                        if (isCausal != causal) continue;
                        float w = (float)Math.Sqrt(dk * dk * rz * rz + dj * dj * ry * ry + di * di * rx * rx);
                        list.Add(new Offset { dk = dk, dj = dj, di = di, w = w });
                    }
            return list;
        }

        private static void SwapIfDescending(ref int lo, ref int hi)
        {
            if (lo > hi) { int t = lo; lo = hi; hi = t; }
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Pure-math EQD2 ⇄ Physical dose conversions; no ESAPI, no I/O.
//
// All inputs and outputs are in Gy. The optional `scaling` parameter is
// a post-multiply factor; pass the inverse of the voxel→Gy ratio if you
// need to roundtrip a result back into voxel space.
//
// Numerical guards:
//   * fxCount <= 0 throws ArgumentOutOfRangeException.
//   * Negative input dose is clamped to 0 (the conversion only sees
//     non-negative voxel values; the unit tests document this contract).
using System;

namespace ReRT.Domain.DoseConversion
{
    /// <summary>
    /// Delegate signature shared by every formula plugged into
    /// <see cref="VoxelTransformer.Apply"/>. Inputs and outputs are in
    /// the same dose unit (Gy by convention).
    /// </summary>
    public delegate double DoseFormula(double dose, double alphaBeta, int fxCount, double scaling);

    public static class DoseFormulas
    {
        /// <summary>
        /// EQD2 = D · (αβ + D/n) / (αβ + 2). Returns the equivalent dose
        /// in 2 Gy fractions (in Gy), optionally scaled.
        /// </summary>
        public static double PhysicalToEqd2(double physicalDose, double alphaBeta, int fxCount, double scaling = 1.0)
        {
            if (fxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(fxCount), "Fraction count must be positive.");
            if (physicalDose < 0) return 0.0;

            double eqd2 = physicalDose * (alphaBeta + physicalDose / fxCount) / (alphaBeta + 2.0);
            return eqd2 * scaling;
        }

        /// <summary>
        /// Inverse of <see cref="PhysicalToEqd2"/>: solves the quadratic
        /// EQD2·(αβ+2) = D·αβ + D²/n for D ≥ 0. Returns the physical
        /// dose (in Gy), optionally scaled.
        /// </summary>
        public static double Eqd2ToPhysical(double eqd2, double alphaBeta, int fxCount, double scaling = 1.0)
        {
            if (fxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(fxCount), "Fraction count must be positive.");
            if (eqd2 <= 0) return 0.0;

            double n = fxCount;
            double nAlphaBeta = n * alphaBeta;
            double phys = 0.5 * (Math.Sqrt(nAlphaBeta * nAlphaBeta + 4.0 * n * eqd2 * (alphaBeta + 2.0)) - nAlphaBeta);
            return phys * scaling;
        }

        /// <summary>
        /// Pass-through formula that returns dose · scaling. Plug into
        /// <see cref="VoxelTransformer"/> for the Physical-mode pass,
        /// where the source voxels already represent the delivered
        /// physical dose and only fraction-scaling/discount apply.
        /// </summary>
        public static double Identity(double dose, double alphaBeta, int fxCount, double scaling = 1.0)
        {
            return dose * scaling;
        }
    }
}

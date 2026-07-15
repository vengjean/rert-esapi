// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Computes Dmax / Mean / V(dose) / VolumeSpared(dose) from a DVH.
using System;

namespace ReRT.Domain.DoseMetrics
{
    public sealed class DoseMetricCalculator
    {
        // D0.1cc near-max convention: cumulative DVH volume threshold at which we
        // declare "this is the near-max dose". The dose is interpolated at the
        // exact crossing rather than snapped to a discrete bin edge.
        private const double NearMaxThresholdCc = 0.1;

        private readonly IDvhProvider _provider;

        public DoseMetricCalculator(IDvhProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _provider = provider;
        }

        // Near-max dose (D0.1cc): the dose at which the cumulative DVH volume
        // crosses the 0.1 cc threshold. Walk the DVH from low to high dose;
        // the cumulative volume is monotone decreasing, so the crossing is the
        // first index where volume drops to or below 0.1 cc. Linearly
        // interpolate inside the bracket so the result is not bin-edge sensitive.
        public double Dmax(StructureRef s)
        {
            var dvh = DvhFallbackHelper.GetDvhWithFallback(
                _provider, s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);
            if (dvh == null || dvh.Points == null || dvh.Points.Length == 0) return 0.0;
            var pts = dvh.Points;

            // Whole structure already below the near-max threshold: no
            // meaningful Dmax.
            if (pts[0].Volume <= NearMaxThresholdCc) return 0.0;

            for (int i = 1; i < pts.Length; i++)
            {
                if (pts[i].Volume <= NearMaxThresholdCc)
                {
                    double v0 = pts[i - 1].Volume;
                    double v1 = pts[i].Volume;
                    double d0 = pts[i - 1].DoseGy;
                    double d1 = pts[i].DoseGy;
                    if (Math.Abs(v0 - v1) < 1e-12) return d1;
                    double t = (v0 - NearMaxThresholdCc) / (v0 - v1);
                    return d0 + t * (d1 - d0);
                }
            }
            // No crossing within the sampled range — the whole structure stays
            // above the threshold. Return the highest sampled dose.
            return pts[pts.Length - 1].DoseGy;
        }

        public double Mean(StructureRef s)
        {
            var dvh = DvhFallbackHelper.GetDvhWithFallback(
                _provider, s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);
            return dvh == null ? 0.0 : dvh.MeanDoseGy;
        }

        // V(D): volume of the structure at or above the given dose.
        // The unit of the returned value matches the requested VolumePresentation
        // (cc when AbsoluteCc, % when RelativePercent).
        public double VolumeAtDose(StructureRef s, double doseGy, VolumePresentation pres)
        {
            var dvh = DvhFallbackHelper.GetDvhWithFallback(
                _provider, s, DoseValuePresentation.Absolute, pres);
            return InterpolateVolume(dvh, doseGy);
        }

        // VS(D): structure volume in cc minus the absolute volume receiving >= D.
        public double VolumeSparedAtDose(StructureRef s, double doseGy)
        {
            var dvh = DvhFallbackHelper.GetDvhWithFallback(
                _provider, s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);
            if (dvh == null) return 0.0;
            double irradiated = InterpolateVolume(dvh, doseGy);
            return dvh.VolumeCc - irradiated;
        }

        // Linear interpolation in a cumulative DVH at the given dose value.
        // Returns:
        // - first.Volume if dose <= first point's dose
        // - last.Volume if dose >= last point's dose
        // - linear blend otherwise (bracket located via binary search on the dose axis)
        // - 0.0 for null/empty curves
        // Volume units match the curve's volume presentation.
        public static double InterpolateVolume(DvhCurve curve, double doseGy)
        {
            if (curve == null || curve.Points == null || curve.Points.Length == 0) return 0.0;
            var pts = curve.Points;
            if (pts.Length == 1) return pts[0].Volume;
            if (doseGy <= pts[0].DoseGy) return pts[0].Volume;
            if (doseGy >= pts[pts.Length - 1].DoseGy) return pts[pts.Length - 1].Volume;

            // Binary search for the bracket on the (ascending) dose axis.
            // Invariant: pts[lo].DoseGy <= doseGy < pts[hi].DoseGy, hi = lo + 1.
            int lo = 0;
            int hi = pts.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].DoseGy <= doseGy) lo = mid;
                else hi = mid;
            }

            double d1 = pts[lo].DoseGy;
            double d2 = pts[hi].DoseGy;
            double v1 = pts[lo].Volume;
            double v2 = pts[hi].Volume;
            if (Math.Abs(d2 - d1) < 1e-12) return (v1 + v2) / 2.0;
            double t = (doseGy - d1) / (d2 - d1);
            return v1 + t * (v2 - v1);
        }
    }
}

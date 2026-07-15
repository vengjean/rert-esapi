// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Fetches a DVH at the 10 Gy bin, falling back to 0.01 Gy when MaxDose < 100k cGy.
namespace ReRT.Domain.DoseMetrics
{
    public static class DvhFallbackHelper
    {
        // Threshold: MaxDose < 100,000 cGy (i.e. 1000 Gy).
        // The intent was: if the coarse 10-Gy bin pulled an unrealistically
        // small max-dose, the curve is too quantized — re-fetch at 0.01 Gy.
        public const double LowMaxDoseGyThreshold = 1000.0;

        // First call: bin width 10.0. If MaxDoseGy is below the threshold,
        // re-fetch at 0.01. Same dose/volume presentations both times.
        public static DvhCurve GetDvhWithFallback(
            IDvhProvider provider,
            StructureRef structure,
            DoseValuePresentation dosePres,
            VolumePresentation volPres)
        {
            var dvh = provider.GetDvh(structure, dosePres, volPres, 10.0);
            if (dvh != null && dvh.MaxDoseGy < LowMaxDoseGyThreshold)
                dvh = provider.GetDvh(structure, dosePres, volPres, 0.01);
            return dvh;
        }
    }
}

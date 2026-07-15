// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// DVH access seam: production = EsapiDvhProvider, tests = FakeDvhProvider.
namespace ReRT.Domain.DoseMetrics
{
    // Domain-local mirror of ESAPI's DoseValuePresentation. Kept here so
    // consumers do not need to reference ESAPI to talk to IDvhProvider.
    public enum DoseValuePresentation
    {
        Absolute,
        Relative,
    }

    // Domain-local mirror of ESAPI's VolumePresentation. AbsoluteCc / RelativePercent
    // names are explicit to remove ambiguity; the EsapiDvhProvider maps them
    // to ESAPI's VolumePresentation.AbsoluteCm3 / VolumePresentation.Relative.
    public enum VolumePresentation
    {
        AbsoluteCc,
        RelativePercent,
    }

    // Lightweight handle for a structure passed across the IDvhProvider boundary.
    // Carries only the fields needed for downstream metric/constraint logic.
    // Production: built by the orchestration layer from an ESAPI Structure.
    // Tests: hand-built.
    public sealed class StructureRef
    {
        public string Id { get; set; }
        public double VolumeCc { get; set; }

        public StructureRef() { }
        public StructureRef(string id, double volumeCc) { Id = id; VolumeCc = volumeCc; }
    }

    public readonly struct DvhPoint
    {
        public readonly double DoseGy;
        public readonly double Volume;

        public DvhPoint(double doseGy, double volume)
        {
            DoseGy = doseGy;
            Volume = volume;
        }
    }

    public sealed class DvhCurve
    {
        // Ordered ascending by dose. Volume unit follows the VolumePresentation
        // requested when the curve was fetched (cc for AbsoluteCc, % for RelativePercent).
        public DvhPoint[] Points { get; set; }

        public double MaxDoseGy { get; set; }
        public double MeanDoseGy { get; set; }

        // Total structure volume in cc (independent of the curve's volume presentation).
        public double VolumeCc { get; set; }
    }

    public interface IDvhProvider
    {
        DvhCurve GetDvh(
            StructureRef structure,
            DoseValuePresentation dosePres,
            VolumePresentation volPres,
            double binWidth);
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Adapter from ESAPI PlanningItem to IDvhProvider; only ESAPI-touching file in the domain layer.
using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using EsapiDose = VMS.TPS.Common.Model.Types.DoseValuePresentation;
using EsapiVolume = VMS.TPS.Common.Model.Types.VolumePresentation;

namespace ReRT.Domain.DoseMetrics
{
    public sealed class EsapiDvhProvider : IDvhProvider
    {
        private readonly PlanningItem _plan;
        private readonly StructureSet _structureSet;

        public EsapiDvhProvider(PlanningItem plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            _plan = plan;

            // PlanSum.StructureSet exists only on subclasses; resolve via the
            // generic property if available, falling back to the plan-specific shape.
            if (plan is PlanSum sum) _structureSet = sum.StructureSet;
            else if (plan is PlanSetup setup) _structureSet = setup.StructureSet;
            else _structureSet = null;
        }

        public DvhCurve GetDvh(
            StructureRef structure,
            DoseValuePresentation dosePres,
            VolumePresentation volPres,
            double binWidth)
        {
            if (structure == null) throw new ArgumentNullException(nameof(structure));
            if (_structureSet == null)
                throw new InvalidOperationException("EsapiDvhProvider has no StructureSet.");

            var esapiStructure = _structureSet.Structures.FirstOrDefault(
                s => string.Equals(s.Id, structure.Id, StringComparison.InvariantCultureIgnoreCase));
            if (esapiStructure == null)
                throw new ArgumentException($"Structure '{structure.Id}' not in plan's StructureSet.", nameof(structure));

            var raw = _plan.GetDVHCumulativeData(
                esapiStructure,
                MapDose(dosePres),
                MapVolume(volPres),
                binWidth);

            return ToDomainCurve(raw);
        }

        private static EsapiDose MapDose(DoseValuePresentation p)
        {
            switch (p)
            {
                case DoseValuePresentation.Absolute: return EsapiDose.Absolute;
                case DoseValuePresentation.Relative: return EsapiDose.Relative;
                default: throw new ArgumentOutOfRangeException(nameof(p));
            }
        }

        private static EsapiVolume MapVolume(VolumePresentation p)
        {
            switch (p)
            {
                case VolumePresentation.AbsoluteCc: return EsapiVolume.AbsoluteCm3;
                case VolumePresentation.RelativePercent: return EsapiVolume.Relative;
                default: throw new ArgumentOutOfRangeException(nameof(p));
            }
        }

        // ESAPI returns dose values in the plan's configured unit (cGy by
        // default in Eclipse); we standardize the domain layer on Gy.
        private static DvhCurve ToDomainCurve(DVHData raw)
        {
            if (raw == null) return new DvhCurve { Points = new DvhPoint[0] };
            var points = raw.CurveData == null
                ? new DvhPoint[0]
                : raw.CurveData
                    .Select(p => new DvhPoint(ToGy(p.DoseValue), p.Volume))
                    .ToArray();

            return new DvhCurve
            {
                Points = points,
                MaxDoseGy = ToGy(raw.MaxDose),
                MeanDoseGy = ToGy(raw.MeanDose),
                VolumeCc = raw.Volume,
            };
        }

        private static double ToGy(VMS.TPS.Common.Model.Types.DoseValue d)
        {
            switch (d.Unit)
            {
                case VMS.TPS.Common.Model.Types.DoseValue.DoseUnit.Gy: return d.Dose;
                case VMS.TPS.Common.Model.Types.DoseValue.DoseUnit.cGy: return d.Dose / 100.0;
                default: return d.Dose; // % / unknown: pass through
            }
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Conservative maximum-dose accumulation (Paradis et al., University of Michigan).
//
// When the registrations between re-irradiation courses cannot be trusted, the
// voxel-by-voxel spatial accumulation done by DoseConversionPipeline /
// VoxelTransformer is unreliable. Instead, this method takes each prior plan's
// OWN near-max dose (D0.1cc) to a structure, converts it to EQD2, and SUMS the
// per-plan EQD2 values. That assumes the per-plan hotspots coincide spatially
// (the worst case), so the result is a conservative upper bound on the
// cumulative near-max dose — no registration required.
//
// This file is pure math (no ESAPI). The D0.1cc physical doses are supplied by
// the caller (Model queries them via DoseMetricCalculator.Dmax). The per-plan
// EQD2 reuses DoseFormulas.PhysicalToEqd2 and mirrors the fraction-scaling and
// discount conventions of DoseConversionPipeline.ComputeDoseMatrix.
using System;
using System.Collections.Generic;

namespace ReRT.Domain.DoseConversion
{
    /// <summary>One prior plan's contribution to a single analysis structure.</summary>
    public sealed class ConservativePlanInput
    {
        /// <summary>True when the structure (ROI) is present in this plan and a
        /// near-max dose could be read. False rows are excluded from the sum.</summary>
        public bool Present { get; set; }

        /// <summary>Planned near-max dose (D0.1cc) in Gy, before delivered/planned scaling.</summary>
        public double PhysicalDoseGy { get; set; }

        public int DeliveredFractions { get; set; }
        public int PlannedFractions { get; set; }

        /// <summary>Recovery discount as a percentage (0..100).</summary>
        public double DiscountPercent { get; set; }
    }

    public sealed class ConservativeStructureInput
    {
        public string Label { get; set; }
        public double AlphaBeta { get; set; }
        public List<ConservativePlanInput> Plans { get; set; } = new List<ConservativePlanInput>();
    }

    /// <summary>
    /// A structure row as requested from the UI, before near-max doses are read.
    /// Per-plan lists are aligned to the plan sum's PlanSetups order; a null or
    /// empty ROI id means the structure is absent from that plan.
    /// </summary>
    public sealed class ConservativeStructureRequest
    {
        public string Label { get; set; }
        public double AlphaBeta { get; set; }
        public List<string> PerPlanRoiId { get; set; } = new List<string>();
        public List<double> PerPlanDiscountPercent { get; set; } = new List<double>();
    }

    public sealed class ConservativeCell
    {
        public bool Present { get; set; }

        /// <summary>Delivered-scaled physical D0.1cc (Gy) — coherent with <see cref="Eqd2Gy"/>.</summary>
        public double PhysicalGy { get; set; }

        /// <summary>EQD2 (Gy) after the recovery discount has been applied.</summary>
        public double Eqd2Gy { get; set; }

        public double DiscountPercent { get; set; }
    }

    public sealed class ConservativeResultRow
    {
        public string Label { get; set; }
        public double AlphaBeta { get; set; }

        /// <summary>Conservative cumulative near-max EQD2 (Gy): the sum of every present plan's discounted EQD2.</summary>
        public double TotalEqd2Gy { get; set; }

        public List<ConservativeCell> Cells { get; set; } = new List<ConservativeCell>();
    }

    public static class ConservativeMaxAccumulator
    {
        /// <summary>
        /// Compute one structure's per-plan EQD2 cells and the conservative
        /// cumulative total. Per plan:
        ///   scaling = delivered / planned          (same as ComputeDoseMatrix)
        ///   scaled  = physicalGy * scaling
        ///   eqd2    = PhysicalToEqd2(scaled, αβ, delivered)
        ///   cell    = eqd2 * (1 - discount/100)    (same discount convention as VoxelTransformer)
        /// total = Σ cell over plans where the ROI is present.
        /// </summary>
        public static ConservativeResultRow Accumulate(ConservativeStructureInput row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            var result = new ConservativeResultRow
            {
                Label = row.Label,
                AlphaBeta = row.AlphaBeta,
            };

            double total = 0.0;
            foreach (var plan in row.Plans)
            {
                // Absent ROI, or a plan we can't scale/convert (guard against
                // bad fraction data), contributes nothing and shows as "—".
                if (plan == null || !plan.Present ||
                    plan.PlannedFractions <= 0 || plan.DeliveredFractions <= 0)
                {
                    result.Cells.Add(new ConservativeCell
                    {
                        Present = false,
                        DiscountPercent = plan?.DiscountPercent ?? 0.0,
                    });
                    continue;
                }

                double scaling = (double)plan.DeliveredFractions / plan.PlannedFractions;
                double scaledGy = plan.PhysicalDoseGy * scaling;
                double eqd2 = DoseFormulas.PhysicalToEqd2(scaledGy, row.AlphaBeta, plan.DeliveredFractions);

                double discount = plan.DiscountPercent;
                if (discount < 0) discount = 0;
                if (discount > 100) discount = 100;
                double eqd2AfterDiscount = eqd2 * (1.0 - discount / 100.0);

                total += eqd2AfterDiscount;
                result.Cells.Add(new ConservativeCell
                {
                    Present = true,
                    PhysicalGy = scaledGy,
                    Eqd2Gy = eqd2AfterDiscount,
                    DiscountPercent = discount,
                });
            }

            result.TotalEqd2Gy = total;
            return result;
        }
    }
}

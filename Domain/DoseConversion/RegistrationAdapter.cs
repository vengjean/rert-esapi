// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Wrapper around ESAPI's Registration that lets the pipeline use a
// registration found in either direction. Eclipse stores a Registration
// with explicit SourceFOR/RegisteredFOR; the inverse pair is not always
// present. Rather than rejecting one of the two directions, we look up
// either pair and wrap the forward/inverse transforms in this adapter so
// downstream code (VoxelTransformer) stays direction-agnostic.
using System;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace ReRT.Domain.DoseConversion
{
    public sealed class RegistrationAdapter
    {
        private readonly Func<VVector, VVector> _forward;
        private readonly Func<VVector, VVector> _inverse;

        // Use when the registration's SourceFOR matches the plan frame and
        // RegisteredFOR matches the plan-sum frame — i.e. ESAPI's
        // TransformPoint already maps plan-frame → plan-sum frame.
        public static RegistrationAdapter Forward(Registration r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return new RegistrationAdapter(r.TransformPoint, r.InverseTransformPoint);
        }

        // Use when the stored registration is "backwards" — SourceFOR is
        // the plan-sum frame and RegisteredFOR is the plan frame. The
        // mathematical inverse of ESAPI's TransformPoint is its
        // InverseTransformPoint and vice versa, so we just swap them.
        public static RegistrationAdapter Reverse(Registration r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return new RegistrationAdapter(r.InverseTransformPoint, r.TransformPoint);
        }

        private RegistrationAdapter(Func<VVector, VVector> forward, Func<VVector, VVector> inverse)
        {
            _forward = forward;
            _inverse = inverse;
        }

        // Source frame (plan) → target frame (plan sum).
        public VVector TransformPoint(VVector p) => _forward(p);

        // Target frame (plan sum) → source frame (plan).
        public VVector InverseTransformPoint(VVector p) => _inverse(p);
    }
}

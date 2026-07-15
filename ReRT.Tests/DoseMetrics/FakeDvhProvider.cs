// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using ReRT.Domain.DoseMetrics;

namespace ReRT.Tests.DoseMetrics
{
    // Test double for IDvhProvider. Two ways to use:
    //   1. Pass a Func<...,DvhCurve> for full control over what's returned.
    //   2. Pre-stage a sequence of curves with EnqueueResponse — they're
    //      returned in order, regardless of the request parameters.
    public sealed class FakeDvhProvider : IDvhProvider
    {
        private readonly Func<StructureRef, DoseValuePresentation, VolumePresentation, double, DvhCurve> _factory;
        private readonly Queue<DvhCurve> _staged = new Queue<DvhCurve>();

        public List<Call> Calls { get; } = new List<Call>();

        public FakeDvhProvider() { }

        public FakeDvhProvider(Func<StructureRef, DoseValuePresentation, VolumePresentation, double, DvhCurve> factory)
        {
            _factory = factory;
        }

        public void EnqueueResponse(DvhCurve curve) => _staged.Enqueue(curve);

        public DvhCurve GetDvh(StructureRef s, DoseValuePresentation dosePres, VolumePresentation volPres, double binWidth)
        {
            Calls.Add(new Call { Structure = s, Dose = dosePres, Volume = volPres, BinWidth = binWidth });
            if (_staged.Count > 0) return _staged.Dequeue();
            if (_factory != null) return _factory(s, dosePres, volPres, binWidth);
            return new DvhCurve { Points = new DvhPoint[0] };
        }

        public sealed class Call
        {
            public StructureRef Structure;
            public DoseValuePresentation Dose;
            public VolumePresentation Volume;
            public double BinWidth;
        }
    }

    public static class DvhCurveBuilder
    {
        // Small builder for hand-built curves. Points must be ascending by dose.
        public static DvhCurve FromPoints(double volumeCc, params (double doseGy, double volume)[] pts)
        {
            var arr = new DvhPoint[pts.Length];
            double max = 0;
            double sum = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                arr[i] = new DvhPoint(pts[i].doseGy, pts[i].volume);
                if (pts[i].doseGy > max) max = pts[i].doseGy;
                sum += pts[i].doseGy;
            }
            return new DvhCurve
            {
                Points = arr,
                MaxDoseGy = max,
                MeanDoseGy = pts.Length == 0 ? 0 : sum / pts.Length,
                VolumeCc = volumeCc,
            };
        }
    }
}

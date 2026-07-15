// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using FluentAssertions;
using ReRT.Domain.DoseMetrics;
using Xunit;

namespace ReRT.Tests.DoseMetrics
{
    public class DvhFallbackHelperTests
    {
        private readonly StructureRef _s = new StructureRef("Liver", 1500.0);

        [Fact]
        public void GetDvhWithFallback_PrimaryReturnsHighMaxDose_DoesNotCallAgain()
        {
            var primary = DvhCurveBuilder.FromPoints(1500, (0, 1500), (60, 100), (1500, 0));
            primary.MaxDoseGy = 1500.0; // above the 1000 Gy threshold
            var provider = new FakeDvhProvider();
            provider.EnqueueResponse(primary);

            var result = DvhFallbackHelper.GetDvhWithFallback(
                provider, _s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);

            result.Should().BeSameAs(primary);
            provider.Calls.Should().HaveCount(1);
            provider.Calls[0].BinWidth.Should().Be(10.0);
        }

        [Fact]
        public void GetDvhWithFallback_PrimaryReturnsLowMaxDose_FallsBackToFineBin()
        {
            var primary = DvhCurveBuilder.FromPoints(1500, (0, 1500), (10, 100));
            primary.MaxDoseGy = 10.0; // below threshold -> trigger fallback
            var fallback = DvhCurveBuilder.FromPoints(1500, (0, 1500), (15, 50), (30, 0));
            fallback.MaxDoseGy = 30.0;
            var provider = new FakeDvhProvider();
            provider.EnqueueResponse(primary);
            provider.EnqueueResponse(fallback);

            var result = DvhFallbackHelper.GetDvhWithFallback(
                provider, _s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);

            result.Should().BeSameAs(fallback);
            provider.Calls.Should().HaveCount(2);
            provider.Calls[0].BinWidth.Should().Be(10.0);
            provider.Calls[1].BinWidth.Should().Be(0.01);
            provider.Calls[0].Volume.Should().Be(provider.Calls[1].Volume);
            provider.Calls[0].Dose.Should().Be(provider.Calls[1].Dose);
        }

        [Fact]
        public void GetDvhWithFallback_NullPrimary_ReturnsNull()
        {
            var provider = new FakeDvhProvider();
            provider.EnqueueResponse(null);

            var result = DvhFallbackHelper.GetDvhWithFallback(
                provider, _s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);

            result.Should().BeNull();
            provider.Calls.Should().HaveCount(1);
        }

        [Fact]
        public void GetDvhWithFallback_AtThresholdBoundary_DoesNotFallBack()
        {
            // Threshold is strict less-than; exactly 1000 Gy is "high enough".
            var primary = DvhCurveBuilder.FromPoints(1500, (0, 1500));
            primary.MaxDoseGy = DvhFallbackHelper.LowMaxDoseGyThreshold;
            var provider = new FakeDvhProvider();
            provider.EnqueueResponse(primary);

            DvhFallbackHelper.GetDvhWithFallback(
                provider, _s, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCc);

            provider.Calls.Should().HaveCount(1);
        }
    }
}

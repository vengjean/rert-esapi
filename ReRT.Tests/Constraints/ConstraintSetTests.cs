// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Coverage for ConstraintSet and ExtraMetricSet container behaviour
// (case-insensitive set lookup, Add/Set/IsEmpty, GetEvaluable filtering
// out null placeholders).

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReRT.Domain.Constraints;
using Xunit;

namespace ReRT.Tests.Constraints
{
    public class ConstraintSetTests
    {
        // ---- ConstraintSet -----------------------------------------------------------

        [Fact]
        public void GetForSet_UnknownSet_ReturnsEmpty()
        {
            var set = new ConstraintSet();
            set.GetForSet("Michigan").Should().BeEmpty();
        }

        [Fact]
        public void GetForSet_NullSet_ReturnsEmpty()
        {
            var set = new ConstraintSet();
            set.GetForSet(null).Should().BeEmpty();
        }

        [Fact]
        public void Add_ThenGetForSet_ReturnsAddedConstraintInOrder()
        {
            var set = new ConstraintSet();
            var c1 = new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy };
            var c2 = new Constraint { Metric = MetricType.Mean, Op = "<", Limit = 20, Unit = LimitUnit.Gy };

            set.Add("Michigan", c1);
            set.Add("Michigan", c2);

            var got = set.GetForSet("Michigan");
            got.Should().HaveCount(2);
            got[0].Should().BeSameAs(c1);
            got[1].Should().BeSameAs(c2);
        }

        [Fact]
        public void GetForSet_IsCaseInsensitive()
        {
            // Container is built with OrdinalIgnoreCase — "michigan" and
            // "MICHIGAN" look up the same bucket as "Michigan".
            var set = new ConstraintSet();
            set.Add("Michigan", new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy });

            set.GetForSet("michigan").Should().HaveCount(1);
            set.GetForSet("MICHIGAN").Should().HaveCount(1);
        }

        [Fact]
        public void GetEvaluable_FiltersOutNullPlaceholders()
        {
            // SABR config encodes "NA;V20<40" as [null, V20<40]; the
            // first slot is the header-only Michigan-equivalent and isn't
            // evaluable. GetEvaluable drops the null.
            var set = new ConstraintSet();
            var real = new Constraint { Metric = MetricType.V, DoseGy = 20, Op = "<", Limit = 40, Unit = LimitUnit.Percent };
            set.Set("SABR", new[] { null, real });

            set.GetForSet("SABR").Should().HaveCount(2);
            set.GetEvaluable("SABR").Should().ContainSingle().Which.Should().BeSameAs(real);
        }

        [Fact]
        public void Set_NullCollection_ResetsBucketToEmpty()
        {
            var set = new ConstraintSet();
            set.Add("Michigan", new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy });
            set.Set("Michigan", null);

            set.GetForSet("Michigan").Should().BeEmpty();
        }

        [Fact]
        public void Set_ReplacesExistingBucket()
        {
            var set = new ConstraintSet();
            set.Add("Michigan", new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy });

            var replacement = new Constraint { Metric = MetricType.Mean, Op = "<", Limit = 20, Unit = LimitUnit.Gy };
            set.Set("Michigan", new[] { replacement });

            set.GetForSet("Michigan").Should().ContainSingle().Which.Should().BeSameAs(replacement);
        }

        [Fact]
        public void IsEmpty_FreshSet_ReturnsTrue()
        {
            new ConstraintSet().IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void IsEmpty_EmptyBucketsOnly_StillTrue()
        {
            // Setting an explicitly-empty bucket does not flip IsEmpty —
            // the test asks "are there constraints anywhere," not "are
            // there set names registered."
            var set = new ConstraintSet();
            set.Set("Michigan", new List<Constraint>());
            set.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void IsEmpty_AfterAdd_ReturnsFalse()
        {
            var set = new ConstraintSet();
            set.Add("Michigan", new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy });
            set.IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void Sets_EnumeratesAllRegisteredSetNames()
        {
            var set = new ConstraintSet();
            set.Add("Michigan", new Constraint { Metric = MetricType.Dmax, Op = "<", Limit = 60, Unit = LimitUnit.Gy });
            set.Add("SABR", new Constraint { Metric = MetricType.V, DoseGy = 20, Op = "<", Limit = 40, Unit = LimitUnit.Percent });

            set.Sets.Should().BeEquivalentTo(new[] { "Michigan", "SABR" });
        }

        // ---- ExtraMetricSet ----------------------------------------------------------

        [Fact]
        public void ExtraMetricSet_GetForSet_UnknownSet_ReturnsEmpty()
        {
            new ExtraMetricSet().GetForSet("PHYS").Should().BeEmpty();
        }

        [Fact]
        public void ExtraMetricSet_GetForSet_NullSet_ReturnsEmpty()
        {
            new ExtraMetricSet().GetForSet(null).Should().BeEmpty();
        }

        [Fact]
        public void ExtraMetricSet_AddAndGet_RoundTrips()
        {
            var set = new ExtraMetricSet();
            var m = new ExtraMetric { Metric = MetricType.V, DoseGy = 20 };
            set.Add("PHYS", m);

            set.GetForSet("PHYS").Should().ContainSingle().Which.Should().BeSameAs(m);
        }

        [Fact]
        public void ExtraMetricSet_GetForSet_IsCaseInsensitive()
        {
            var set = new ExtraMetricSet();
            set.Add("PHYS", new ExtraMetric { Metric = MetricType.V, DoseGy = 20 });

            set.GetForSet("phys").Should().HaveCount(1);
            set.GetForSet("Phys").Should().HaveCount(1);
        }

        [Fact]
        public void ExtraMetricSet_Set_NullCollection_ResetsBucketToEmpty()
        {
            var set = new ExtraMetricSet();
            set.Add("PHYS", new ExtraMetric { Metric = MetricType.V, DoseGy = 20 });
            set.Set("PHYS", null);

            set.GetForSet("PHYS").Should().BeEmpty();
        }

        [Fact]
        public void ExtraMetricSet_IsEmpty_TracksBucketContents()
        {
            var set = new ExtraMetricSet();
            set.IsEmpty.Should().BeTrue();

            set.Add("PHYS", new ExtraMetric { Metric = MetricType.V, DoseGy = 20 });
            set.IsEmpty.Should().BeFalse();

            set.Set("PHYS", new ExtraMetric[0]);
            set.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void ExtraMetricSet_Sets_EnumeratesRegisteredKeys()
        {
            var set = new ExtraMetricSet();
            set.Add("PHYS", new ExtraMetric { Metric = MetricType.V, DoseGy = 20 });
            set.Sets.Should().ContainSingle().Which.Should().Be("PHYS");
        }
    }
}

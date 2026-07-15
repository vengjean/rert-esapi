// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using FluentAssertions;
using ReRT.Domain.Constraints;
using ReRT.Logging;
using Xunit;

namespace ReRT.Tests.Logging
{
    public class RunMetricsTests
    {
        [Fact]
        public void WithRuntime_ComputesSecondsCorrectly()
        {
            var start = new DateTime(2026, 5, 13, 9, 0, 0, DateTimeKind.Utc);
            var end = start.AddSeconds(7.5);

            var m = new RunMetrics().WithRuntime(start, end);

            m.RuntimeSeconds.Should().BeApproximately(7.5, 1e-9);
        }

        [Fact]
        public void WithUser_StoresUserVerbatim()
        {
            new RunMetrics().WithUser("smith").User.Should().Be("smith");
        }

        [Fact]
        public void WithUser_NullCoercesToEmpty()
        {
            // RunMetrics is consumed by Serilog's structured destructuring;
            // null fields render awkwardly, so we coerce to "" for stable
            // log shape.
            new RunMetrics().WithUser(null).User.Should().Be(string.Empty);
        }

        [Fact]
        public void WithRegistrationType_StoresVerbatim()
        {
            new RunMetrics().WithRegistrationType("Good local alignment")
                .RegistrationType.Should().Be("Good local alignment");
        }

        [Fact]
        public void WithRegistrationType_NullCoercesToEmpty()
        {
            new RunMetrics().WithRegistrationType(null).RegistrationType.Should().Be(string.Empty);
        }

        [Fact]
        public void RecordEvaluation_NullResult_IsNoop()
        {
            // Defensive: an upstream consumer that hands us null shouldn't
            // bump the counters or trip the structure tracker.
            var m = new RunMetrics();

            m.RecordEvaluation(null, "Lung");

            m.ConstraintsEvaluated.Should().Be(0);
            m.EvaluatedStructures.Should().BeEmpty();
        }

        [Fact]
        public void RecordEvaluation_Met_IncrementsMetCount_AndTracksEvaluatedStructure()
        {
            var m = new RunMetrics();

            m.RecordEvaluation(new EvaluationResult { IsMet = true }, "Lung");
            m.RecordEvaluation(new EvaluationResult { IsMet = true }, "Heart");

            m.ConstraintsEvaluated.Should().Be(2);
            m.ConstraintsMet.Should().Be(2);
            m.ConstraintsViolated.Should().Be(0);
            m.EvaluatedStructures.Should().Equal(new[] { "Lung", "Heart" });
            m.StructuresWithViolations.Should().BeEmpty();
        }

        [Fact]
        public void RecordEvaluation_Violated_TracksBothEvaluatedAndViolated()
        {
            var m = new RunMetrics();

            m.RecordEvaluation(new EvaluationResult { IsMet = false }, "Lung");
            m.RecordEvaluation(new EvaluationResult { IsMet = true }, "Heart");
            m.RecordEvaluation(new EvaluationResult { IsMet = false }, "Bowel");

            m.ConstraintsEvaluated.Should().Be(3);
            m.ConstraintsMet.Should().Be(1);
            m.ConstraintsViolated.Should().Be(2);
            m.EvaluatedStructures.Should().Equal(new[] { "Lung", "Heart", "Bowel" });
            m.StructuresWithViolations.Should().Equal(new[] { "Lung", "Bowel" });
        }

        [Fact]
        public void RecordEvaluation_DedupesStructuresAcrossMultipleConstraints()
        {
            // Same structure with multiple constraints, some passing, some not:
            // should appear once in EvaluatedStructures and once in
            // StructuresWithViolations.
            var m = new RunMetrics();

            m.RecordEvaluation(new EvaluationResult { IsMet = true },  "Lung");
            m.RecordEvaluation(new EvaluationResult { IsMet = false }, "Lung");
            m.RecordEvaluation(new EvaluationResult { IsMet = false }, "lung"); // case-insensitive
            m.RecordEvaluation(new EvaluationResult { IsMet = true },  "Heart");

            m.EvaluatedStructures.Should().Equal(new[] { "Lung", "Heart" });
            m.StructuresWithViolations.Should().Equal(new[] { "Lung" });
            m.ConstraintsEvaluated.Should().Be(4);
            m.ConstraintsViolated.Should().Be(2);
        }

        [Fact]
        public void ToStructuredFields_ContainsAllExpectedKeys()
        {
            var m = new RunMetrics()
                .WithUser("USER1")
                .WithPatient("ABC123")
                .WithRegistrationType("Rigid")
                .WithRuntime(new DateTime(2026, 5, 13, 9, 0, 0, DateTimeKind.Utc),
                             new DateTime(2026, 5, 13, 9, 0, 2, DateTimeKind.Utc))
                .WithPlanComposition(
                    new[] { "PlanA", "PlanB" },
                    new[] { "EQD2", "Physical" },
                    new[] { 6, 14 },
                    courseCount: 2);
            m.RecordEvaluation(new EvaluationResult { IsMet = true },  "Lung");
            m.RecordEvaluation(new EvaluationResult { IsMet = false }, "Bowel");

            var fields = m.ToStructuredFields();

            fields.Keys.Should().BeEquivalentTo(new[]
            {
                "User", "PatientHash", "RuntimeSeconds",
                "RegistrationType",
                "CourseCount", "PlanCount", "PlanNames",
                "ElapsedMonthsPerPlan", "ConversionModesUsed",
                "EvaluatedStructures",
                "ConstraintsEvaluated", "ConstraintsMet", "ConstraintsViolated",
                "StructuresWithViolations",
            });
            fields["User"].Should().Be("USER1");
            ((string)fields["PatientHash"]).Should().HaveLength(16);
            fields["RegistrationType"].Should().Be("Rigid");
            fields["CourseCount"].Should().Be(2);
            fields["PlanCount"].Should().Be(2);
            fields["ConstraintsEvaluated"].Should().Be(2);
            fields["ConstraintsMet"].Should().Be(1);
            fields["ConstraintsViolated"].Should().Be(1);
        }

        [Fact]
        public void WithPatient_EmptyOrNullId_LeavesHashEmpty()
        {
            new RunMetrics().WithPatient(null).PatientHash.Should().BeEmpty();
            new RunMetrics().WithPatient("").PatientHash.Should().BeEmpty();
        }

        [Fact]
        public void WithPlanComposition_KeepsPlanNamesInOrderAndComputesCount()
        {
            var m = new RunMetrics().WithPlanComposition(
                new[] { "PlanA", "PlanB", "PlanC" },
                new[] { "EQD2" },
                new[] { 6, 14, 22 },
                courseCount: 3);

            m.PlanNames.Should().Equal(new[] { "PlanA", "PlanB", "PlanC" });
            m.PlanCount.Should().Be(3);
            m.ElapsedMonthsPerPlan.Should().Equal(new[] { 6, 14, 22 });
            m.CourseCount.Should().Be(3);
            m.ConversionModesUsed.Should().Equal(new[] { "EQD2" });
        }
    }
}

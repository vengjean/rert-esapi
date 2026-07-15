// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Per-run telemetry payload: user, hashed patient, runtime, plan composition, constraint counts.
using System;
using System.Collections.Generic;
using System.Linq;
using ReRT.Domain.Constraints;

namespace ReRT.Logging
{
    public sealed class RunMetrics
    {
        public DateTime StartUtc { get; private set; }
        public DateTime EndUtc { get; private set; }
        public string User { get; private set; } = "";
        public string PatientHash { get; private set; } = "";

        // Top-level registration mode chosen for the run ("Rigid",
        // "Deformable", etc.), sourced from ConvertParams["registrationType"].
        // Per-structure registration *confidence* is a separate concept and
        // intentionally not included in the run summary.
        public string RegistrationType { get; private set; } = "";

        public int PlanCount { get; private set; }
        public int CourseCount { get; private set; }
        public IReadOnlyList<string> PlanNames { get; private set; } = new List<string>();
        public IReadOnlyList<int> ElapsedMonthsPerPlan { get; private set; } = new List<int>();
        public IReadOnlyList<string> ConversionModesUsed { get; private set; } = new List<string>();

        public int ConstraintsEvaluated { get; private set; }
        public int ConstraintsMet { get; private set; }
        public int ConstraintsViolated { get; private set; }

        // Structures that had at least one constraint evaluated against them.
        public IReadOnlyList<string> EvaluatedStructures => _evaluatedStructures;

        // Subset of EvaluatedStructures whose evaluation(s) produced any
        // violation. Dedup-add; insertion order preserved for log stability.
        public IReadOnlyList<string> StructuresWithViolations => _structuresWithViolations;

        private readonly List<string> _evaluatedStructures = new List<string>();
        private readonly HashSet<string> _evaluatedStructureSeen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _structuresWithViolations = new List<string>();
        private readonly HashSet<string> _violatingStructureSeen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public double RuntimeSeconds => (EndUtc - StartUtc).TotalSeconds;

        public RunMetrics WithRuntime(DateTime startUtc, DateTime endUtc)
        {
            StartUtc = startUtc;
            EndUtc = endUtc;
            return this;
        }

        public RunMetrics WithUser(string user)
        {
            User = user ?? "";
            return this;
        }

        // Stores the SHA-256/16 hash, never the raw ID (D3 — no PHI in logs).
        public RunMetrics WithPatient(string patientId)
        {
            PatientHash = string.IsNullOrEmpty(patientId)
                ? ""
                : PatientIdHasher.Hash(patientId);
            return this;
        }

        public RunMetrics WithRegistrationType(string registrationType)
        {
            RegistrationType = registrationType ?? "";
            return this;
        }

        public RunMetrics WithPlanComposition(
            IReadOnlyList<string> planNames,
            IEnumerable<string> conversionModesUsed,
            IEnumerable<int> elapsedMonthsPerPlan,
            int courseCount)
        {
            var names = (planNames ?? new List<string>()).ToList();
            PlanNames = names;
            PlanCount = names.Count;
            CourseCount = courseCount;
            ConversionModesUsed = (conversionModesUsed ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ElapsedMonthsPerPlan = (elapsedMonthsPerPlan ?? Enumerable.Empty<int>()).ToList();
            return this;
        }

        // Records one constraint evaluation. `structureName` is added to
        // EvaluatedStructures (dedup), and additionally to
        // StructuresWithViolations when the constraint was not met.
        public RunMetrics RecordEvaluation(EvaluationResult r, string structureName)
        {
            if (r == null) return this;
            ConstraintsEvaluated++;
            if (r.IsMet) ConstraintsMet++;
            else ConstraintsViolated++;

            if (!string.IsNullOrEmpty(structureName))
            {
                if (_evaluatedStructureSeen.Add(structureName))
                    _evaluatedStructures.Add(structureName);
                if (!r.IsMet && _violatingStructureSeen.Add(structureName))
                    _structuresWithViolations.Add(structureName);
            }
            return this;
        }

        // The shape consumed by Serilog's `{@Metrics}` destructuring. A plain
        // IDictionary keeps the wire format obvious for downstream parsers
        // and decouples the log payload from the C# property names.
        public IReadOnlyDictionary<string, object> ToStructuredFields()
        {
            return new Dictionary<string, object>
            {
                { "User", User },
                { "PatientHash", PatientHash },
                { "RuntimeSeconds", Math.Round(RuntimeSeconds, 3) },
                { "RegistrationType", RegistrationType },
                { "CourseCount", CourseCount },
                { "PlanCount", PlanCount },
                { "PlanNames", PlanNames },
                { "ElapsedMonthsPerPlan", ElapsedMonthsPerPlan },
                { "ConversionModesUsed", ConversionModesUsed },
                { "EvaluatedStructures", EvaluatedStructures },
                { "ConstraintsEvaluated", ConstraintsEvaluated },
                { "ConstraintsMet", ConstraintsMet },
                { "ConstraintsViolated", ConstraintsViolated },
                { "StructuresWithViolations", StructuresWithViolations },
            };
        }
    }
}

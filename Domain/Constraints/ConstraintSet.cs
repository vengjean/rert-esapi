// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Container of constraints keyed by set name (SABR, Michigan, etc.).
using System.Collections.Generic;
using System.Linq;

namespace ReRT.Domain.Constraints
{
    public sealed class ConstraintSet
    {
        // Internal storage. List entries may be null when the set uses "NA"
        // placeholder semantics (e.g. SABR "NA;V20<40").
        private readonly Dictionary<string, List<Constraint>> _bySet
            = new Dictionary<string, List<Constraint>>(System.StringComparer.OrdinalIgnoreCase);

        // Returns the (possibly null-padded) raw constraint list for a set.
        // Callers that want only non-null entries should use GetEvaluable.
        public IReadOnlyList<Constraint> GetForSet(string set)
        {
            if (set != null && _bySet.TryGetValue(set, out var list)) return list;
            return Empty;
        }

        // Same as GetForSet but skips null placeholder slots.
        public IReadOnlyList<Constraint> GetEvaluable(string set)
        {
            return GetForSet(set).Where(c => c != null).ToList();
        }

        public void Add(string set, Constraint c)
        {
            if (!_bySet.TryGetValue(set, out var list))
            {
                list = new List<Constraint>();
                _bySet[set] = list;
            }
            list.Add(c);
        }

        public void Set(string set, IEnumerable<Constraint> constraints)
        {
            _bySet[set] = constraints == null ? new List<Constraint>() : new List<Constraint>(constraints);
        }

        public bool IsEmpty
        {
            get { return _bySet.Count == 0 || _bySet.Values.All(v => v.Count == 0); }
        }

        public IEnumerable<string> Sets { get { return _bySet.Keys; } }

        private static readonly IReadOnlyList<Constraint> Empty = new List<Constraint>();
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Parses dose / volume strings ("20Gy", "2000cGy", "40%", "1000cc") into
// a (value, unit) pair. Doses are normalized to Gy at parse time.
using System;
using System.Globalization;
using ReRT.Domain.Constraints;

namespace ReRT.Configuration.Loaders
{
    public sealed class Quantity
    {
        public double Value { get; }
        public LimitUnit Unit { get; }

        public Quantity(double value, LimitUnit unit)
        {
            Value = value;
            Unit = unit;
        }

        public static Quantity Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException("Empty quantity string.");
            var trimmed = s.Trim();

            // Order matters: longer suffixes first ("cGy" before "Gy",
            // "%"/"cc" before either Gy form).
            if (TryStrip(trimmed, "cGy", out var n))
                return new Quantity(n / 100.0, LimitUnit.Gy);
            if (TryStrip(trimmed, "Gy", out n))
                return new Quantity(n, LimitUnit.Gy);
            if (TryStrip(trimmed, "cc", out n))
                return new Quantity(n, LimitUnit.CC);
            if (TryStrip(trimmed, "%", out n))
                return new Quantity(n, LimitUnit.Percent);

            throw new FormatException(
                $"Quantity '{s}' missing unit suffix (expected one of: Gy, cGy, cc, %).");
        }

        private static bool TryStrip(string s, string suffix, out double value)
        {
            value = 0;
            if (s.Length < suffix.Length) return false;
            if (!s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            var num = s.Substring(0, s.Length - suffix.Length).Trim();
            return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}

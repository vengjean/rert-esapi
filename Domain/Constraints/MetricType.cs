// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Closed-set vocabulary of constraint metrics; extends only with user approval.
namespace ReRT.Domain.Constraints
{
    public enum MetricType
    {
        Dmax,
        Mean,
        V,
        VolumeSpared,
    }

    // Unit of a constraint's Limit value. Doses are normalized to Gy at parse time.
    public enum LimitUnit
    {
        Gy,
        Percent,
        CC,
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Emits one Serilog "Run complete {@Metrics}" event per Convert run.
using System;
using Serilog;

namespace ReRT.Logging
{
    public static class StructuredLogger
    {
        // Single structured-event sink for a finished Convert run. Failure to
        // emit the summary must never surface as a user-visible error — the
        // run already completed (or already errored) by the time this runs,
        // so we swallow any logging exception with a best-effort fallback.
        public static void WriteRunSummary(RunMetrics metrics)
        {
            if (metrics == null) return;
            try
            {
                Log.Information("Run complete {@Metrics}", metrics.ToStructuredFields());
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "Failed to emit Run complete summary."); }
                catch { /* nothing else we can do */ }
            }
        }
    }
}

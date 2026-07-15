// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// SHA-256 of patient ID, hex, first 16 chars; whitespace+case normalized; no salt.
using System;
using System.Security.Cryptography;
using System.Text;

namespace ReRT.Logging
{
    public static class PatientIdHasher
    {
        // Returns the first 16 hex chars of SHA-256(normalized(patientId)).
        // Normalization: trim leading/trailing whitespace, uppercase ASCII —
        // so "abc123" and " ABC123 " collide. No salt: the same patient
        // hashes the same across runs and machines, which is what makes the
        // hash useful for usage tracking. 16 hex chars ≈ 64 bits — collision
        // risk is ~1-in-2^32 across realistic patient counts, comfortably
        // below the noise floor for an internal telemetry field.
        public static string Hash(string patientId)
        {
            if (patientId == null) throw new ArgumentNullException(nameof(patientId));
            string normalized = patientId.Trim().ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using FluentAssertions;
using ReRT.Logging;
using Xunit;

namespace ReRT.Tests.Logging
{
    public class PatientIdHasherTests
    {
        [Fact]
        public void Hash_SameInput_ReturnsSameOutput()
        {
            PatientIdHasher.Hash("ABC123")
                .Should().Be(PatientIdHasher.Hash("ABC123"));
        }

        [Fact]
        public void Hash_DifferentInputs_ReturnsDifferentOutputs()
        {
            PatientIdHasher.Hash("ABC123")
                .Should().NotBe(PatientIdHasher.Hash("ABC124"));
        }

        [Fact]
        public void Hash_OutputIs16HexChars()
        {
            string h = PatientIdHasher.Hash("ABC123");

            h.Should().HaveLength(16);
            h.Should().MatchRegex("^[0-9a-f]{16}$");
        }

        [Fact]
        public void Hash_WhitespaceAndCaseNormalized()
        {
            // Trim + uppercase normalization: these four spellings of the
            // same ID must all collide.
            string h1 = PatientIdHasher.Hash("abc123");
            string h2 = PatientIdHasher.Hash(" ABC123 ");
            string h3 = PatientIdHasher.Hash("ABC123");
            string h4 = PatientIdHasher.Hash("\tabc123\n");

            h1.Should().Be(h2);
            h2.Should().Be(h3);
            h3.Should().Be(h4);
        }

        [Fact]
        public void Hash_OriginalIdNotInOutput()
        {
            // Sanity check: catches a typo where someone returns the input
            // by mistake. Hash digits/letters are coincidence-possible, so
            // we test with a string unlikely to appear in any 16-hex hash.
            const string raw = "ZZ987XYZ";
            string h = PatientIdHasher.Hash(raw);

            h.Should().NotContain(raw);
            h.Should().NotContain(raw.ToLowerInvariant());
        }
    }
}

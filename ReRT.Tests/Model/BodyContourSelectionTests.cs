// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ReRT.Tests.ModelTests
{
    /// <summary>
    /// Precedence for choosing THE body contour among several "Body"-aliased
    /// candidates (<see cref="ReRT.Model.PickBodyWinner"/>). An EXACT id match
    /// to a priority name must beat a substring match — a real "Skin" must win
    /// over a helper like "TS_SKIN_DOSESHEL" — while the priority list order
    /// (Body &gt; External &gt; Skin) still decides among exact matches.
    /// </summary>
    public class BodyContourSelectionTests
    {
        private static readonly List<string> Priority =
            new List<string> { "Body", "External", "Skin" };

        private static StructureViewModel S(string id)
            => new StructureViewModel { StructureId = id, StructureLabel = "Body" };

        // Winning candidate's id for a body pool given in enumeration order.
        private static string Winner(params string[] ids)
            => ReRT.Model.PickBodyWinner(ids.Select(S).ToList(), Priority)?.StructureId;

        [Fact]
        public void ExactSkin_BeatsSubstringSkinHelper_RegardlessOfOrder()
        {
            // The reported bug: TS_SKIN_DOSESHEL must not take body over Skin.
            Winner("TS_SKIN_DOSESHEL", "Skin").Should().Be("Skin");
            // Exactness wins on merit, not on enumeration order.
            Winner("Skin", "TS_SKIN_DOSESHEL").Should().Be("Skin");
        }

        [Fact]
        public void ExactBody_PreferredOverExactSkin()
        {
            Winner("Skin", "BODY").Should().Be("BODY");
        }

        [Fact]
        public void ExactExternal_PreferredOverExactSkin()
        {
            Winner("Skin", "External").Should().Be("External");
        }

        [Fact]
        public void ExactMatch_IsCaseAndUnderscoreInsensitive()
        {
            Winner("ts_skin_doseshel", "skin").Should().Be("skin");
            Winner("Skin", "EXTERNAL").Should().Be("EXTERNAL");
            Winner("TS_SKIN_DOSESHEL", "S_K_I_N").Should().Be("S_K_I_N"); // "SKIN" exact
        }

        [Fact]
        public void ExactMatch_BeatsSubstringOfHigherPriorityName()
        {
            // Exact-first is the primary rule: a real "Skin" beats a helper that
            // merely *contains* a higher-priority name ("Body") as a substring.
            Winner("TS_BODY_RING", "Skin").Should().Be("Skin");
        }

        [Fact]
        public void NoExactMatch_FallsBackToSubstring_InPriorityOrder()
        {
            // Nothing equals a priority name → substring tier, still Body >
            // External > Skin: "External_PRV" (contains External) wins over the
            // skin helper even though the helper contains the lower "Skin".
            Winner("TS_SKIN_DOSESHEL", "External_PRV").Should().Be("External_PRV");
        }

        [Fact]
        public void NoMatchAtAll_FallsBackToFirstCandidate()
        {
            Winner("Foo", "Bar").Should().Be("Foo");
        }
    }
}

// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Per-plan ROI auto-fill for the conservative max-dose analysis rows
// (ConservativeViewModel.BestGuessRoi). Regression for the reported bug:
// the guess only gate-checked the single top-ranked candidate, so a plan
// whose structure set contained an unrelated but edit-distance-close name
// left the cell blank even when an exact/substring match ("Bowel" for type
// "Bowel_Small") was available further down the ranking.

using System.Collections.ObjectModel;
using FluentAssertions;
using Xunit;

namespace ReRT.Tests.ViewModels
{
    public class BestGuessRoiTests
    {
        private static string Guess(string hint, params string[] options)
            => ConservativeViewModel.BestGuessRoi(hint, new ObservableCollection<string>(options));

        [Fact]
        public void SubstringMatch_IsSelected()
        {
            Guess("Bowel_Small", "", "Bowel", "(not present)").Should().Be("Bowel");
        }

        [Fact]
        public void RunnerUp_IsConsidered_WhenTopRankFailsTheConfidenceGate()
        {
            // "Bowel_Bag" out-ranks "Bowel" on edit distance (4 vs 5 against
            // "BOWELSMALL") but fails the confidence gate (> max(2, 10/3) = 3,
            // no substring); the substring match below it must still be found.
            Guess("Bowel_Small", "Bowel_Bag", "Bowel").Should().Be("Bowel");
        }

        [Fact]
        public void CloseFuzzyTopRank_Wins_OverLowerRankedSubstring()
        {
            // Accepted behavior: "Bowel_Sac" sits at edit distance 3 against
            // "BOWELSMALL", inside the gate for a 10-char hint, so it wins in rank
            // order over the substring match below it. The gate is a general rule
            // (<= max(2, hint length / 3)), deliberately not tuned per structure pair.
            Guess("Bowel_Small", "Bowel_Sac", "Bowel").Should().Be("Bowel_Sac");
        }

        [Fact]
        public void ConfidentTopRank_StillWins_OverLowerRankedSubstring()
        {
            // Mirrors the user's plan 1: BOWEL_ALL (edit distance 2) ranks
            // first and passes the gate, so it is picked over "Bowel".
            Guess("Bowel_Small", "BOWEL_ALL", "Bowel", "SmallBowel").Should().Be("BOWEL_ALL");
        }

        [Fact]
        public void NoConfidentCandidate_ReturnsEmpty_SoThePromptShows()
        {
            Guess("Bowel_Small", "Femur_L", "Liver").Should().BeEmpty();
        }

        [Fact]
        public void BlankHint_ReturnsEmpty()
        {
            Guess("  ", "Bowel").Should().BeEmpty();
        }
    }
}

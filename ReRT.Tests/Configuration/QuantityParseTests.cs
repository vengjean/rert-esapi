// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

using System;
using FluentAssertions;
using ReRT.Configuration.Loaders;
using ReRT.Domain.Constraints;
using Xunit;

namespace ReRT.Tests.Configuration
{
    public class QuantityParseTests
    {
        [Fact]
        public void Parse_20Gy_ReturnsTwentyGy()
        {
            var q = Quantity.Parse("20Gy");
            q.Value.Should().Be(20.0);
            q.Unit.Should().Be(LimitUnit.Gy);
        }

        [Fact]
        public void Parse_2000cGy_ReturnsTwentyGy()
        {
            var q = Quantity.Parse("2000cGy");
            q.Value.Should().Be(20.0);
            q.Unit.Should().Be(LimitUnit.Gy);
        }

        [Fact]
        public void Parse_40Percent_ReturnsPercentUnit()
        {
            var q = Quantity.Parse("40%");
            q.Value.Should().Be(40.0);
            q.Unit.Should().Be(LimitUnit.Percent);
        }

        [Fact]
        public void Parse_1000cc_ReturnsAbsoluteVolume()
        {
            var q = Quantity.Parse("1000cc");
            q.Value.Should().Be(1000.0);
            q.Unit.Should().Be(LimitUnit.CC);
        }

        [Fact]
        public void Parse_DecimalGy_PreservedExactly()
        {
            Quantity.Parse("85.3Gy").Value.Should().Be(85.3);
            Quantity.Parse("154.3Gy").Value.Should().Be(154.3);
        }

        [Fact]
        public void Parse_WhitespaceBetweenNumberAndUnit_Accepted()
        {
            Quantity.Parse("20 Gy").Value.Should().Be(20.0);
            Quantity.Parse("1000 cc").Value.Should().Be(1000.0);
        }

        [Fact]
        public void Parse_NoUnit_Throws()
        {
            Action act = () => Quantity.Parse("20");
            act.Should().Throw<FormatException>().WithMessage("*unit suffix*");
        }

        [Fact]
        public void Parse_Empty_Throws()
        {
            Action act = () => Quantity.Parse("");
            act.Should().Throw<FormatException>();
        }

        [Fact]
        public void Parse_Null_Throws()
        {
            Action act = () => Quantity.Parse(null);
            act.Should().Throw<FormatException>();
        }

        [Fact]
        public void Parse_cGy_IsCheckedBeforeGy()
        {
            // Critical: "Gy" is a suffix of "cGy". The parser must handle the
            // longer suffix first or "200cGy" would parse as 200 Gy.
            Quantity.Parse("200cGy").Value.Should().Be(2.0);
        }
    }
}

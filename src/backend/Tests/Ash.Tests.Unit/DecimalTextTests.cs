using Ash.Modules.Cases.Application;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>Money and m² as strings (task 025): exact decimals, the column's precision, never a double.</summary>
public class DecimalTextTests
{
    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("1250000", "1250000.00")]
    [InlineData("1250000.5", "1250000.50")]
    [InlineData("1250000.50", "1250000.50")]
    [InlineData("0001.10", "1.10")]
    [InlineData("9999999999999999.99", "9999999999999999.99")]
    public void Valid_money_parses_and_formats_with_two_decimals(string text, string formatted)
    {
        DecimalText.TryParse(text, DecimalText.MoneyIntegerDigits, out var value).ShouldBeTrue();
        DecimalText.Format(value).ShouldBe(formatted);
    }

    [Theory]
    [InlineData("12.345")] // 3 decimals
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("10000000000000000")] // 17 digits: DECIMAL(18,2) holds 16
    [InlineData("1,5")]
    [InlineData("1 000")]
    [InlineData("1e5")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("١٢")] // Arabic-Indic digits
    [InlineData("")]
    public void Invalid_money_is_refused(string text) =>
        DecimalText.TryParse(text, DecimalText.MoneyIntegerDigits, out _).ShouldBeFalse();

    [Fact]
    public void Area_holds_12_digits_before_the_point()
    {
        DecimalText.TryParse("999999999999.99", DecimalText.AreaIntegerDigits, out _).ShouldBeTrue();
        DecimalText.TryParse("1000000000000", DecimalText.AreaIntegerDigits, out _).ShouldBeFalse();
    }

    [Fact]
    public void Null_formats_as_null() => DecimalText.Format(null).ShouldBeNull();
}

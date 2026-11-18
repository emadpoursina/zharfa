namespace Zharfa.Api.Tests.DataQuality;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Zharfa.Api.Analytics;
using Zharfa.Api.DataQuality;

/// <summary>
/// T075 (data-model §3.4): the earliest plausible date setting. Only yyyy-mm-dd (Latin or Persian digits) is accepted;
/// anything else refuses startup and names the key. Pure: no database, no network.
/// </summary>
public class DataQualityOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var map = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            map[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(map).Build();
    }

    private static int NonLeapYear() => Enumerable.Range(1395, 8).First(year => !JalaliDate.IsLeapYear(year));

    [Fact]
    public void Missing_key_defaults_to_1390_01_01()
    {
        var options = DataQualityOptions.FromConfiguration(Config());

        Assert.Equal(13900101, options.EarliestKey);
        Assert.Equal("1390-01-01", options.EarliestText);
    }

    [Fact]
    public void A_valid_date_becomes_its_day_key()
    {
        Assert.Equal(13950101, DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", "1395-01-01"))).EarliestKey);
    }

    [Fact]
    public void Persian_digits_are_accepted()
    {
        var options = DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", "۱۳۹۵-۰۱-۰۱")));

        Assert.Equal(13950101, options.EarliestKey);
    }

    [Fact]
    public void Literal_double_underscore_key_and_colon_key_are_both_read()
    {
        Assert.Equal(13960301, DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", "1396-03-01"))).EarliestKey);
        Assert.Equal(13970401, DataQualityOptions.FromConfiguration(Config(("DataQuality:EarliestJalaliDate", "1397-04-01"))).EarliestKey);
    }

    [Fact]
    public void A_month_above_twelve_is_refused_and_the_message_names_the_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", "1395-13-01"))));

        Assert.Contains("DataQuality__EarliestJalaliDate", error.Message);
    }

    [Fact]
    public void A_day_31_in_a_non_leap_year_is_refused()
    {
        var year = NonLeapYear();

        var error = Assert.Throws<InvalidOperationException>(() =>
            DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", $"{year}-12-31"))));

        Assert.Contains("DataQuality__EarliestJalaliDate", error.Message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1395/01/01")]
    public void Text_other_than_yyyy_mm_dd_is_refused_and_the_message_names_the_key(string raw)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DataQualityOptions.FromConfiguration(Config(("DataQuality__EarliestJalaliDate", raw))));

        Assert.Contains("DataQuality__EarliestJalaliDate", error.Message);
    }
}

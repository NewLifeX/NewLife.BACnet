using System;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using Xunit;

namespace UnitTest;

/// <summary>BacnetDate 单元测试。包含 day=32（最后一天）的回归测试（D004 修复）。</summary>
public class BacnetDateTests
{
    #region Encode / Decode 往返
    [Fact]
    [System.ComponentModel.DisplayName("Encode-Decode 往返数据一致")]
    public void Encode_Decode_RoundTrip()
    {
        var orig = new BacnetDate(125, 6, 15); // 2025-06-15
        var buf = new EncodeBuffer();
        orig.Encode(buf);

        var decoded = new BacnetDate();
        decoded.Decode(buf.buffer, 0, 4);

        Assert.Equal(orig.year, decoded.year);
        Assert.Equal(orig.month, decoded.month);
        Assert.Equal(orig.day, decoded.day);
        Assert.Equal(orig.wday, decoded.wday);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Encode 写入 4 字节")]
    public void Encode_WritesFourBytes()
    {
        var date = new BacnetDate(125, 1, 1);
        var buf = new EncodeBuffer();
        date.Encode(buf);
        Assert.Equal(4, buf.offset);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Decode 返回偏移量 4")]
    public void Decode_ReturnsOffsetFour()
    {
        var date = new BacnetDate();
        var bytes = new byte[] { 125, 1, 1, 255 };
        var consumed = date.Decode(bytes, 0, 4);
        Assert.Equal(4, consumed);
    }
    #endregion

    #region toDateTime
    [Fact]
    [System.ComponentModel.DisplayName("toDateTime 普通日期转换正确")]
    public void ToDateTime_NormalDate()
    {
        var date = new BacnetDate(125, 5, 14); // 2025-05-14
        var dt = date.toDateTime();
        Assert.Equal(2025, dt.Year);
        Assert.Equal(5, dt.Month);
        Assert.Equal(14, dt.Day);
    }

    [Fact]
    [System.ComponentModel.DisplayName("toDateTime 周期性日期返回最小值")]
    public void ToDateTime_PeriodicDate_ReturnsMinValue()
    {
        var date = new BacnetDate(255, 5, 14); // 通配年份
        var dt = date.toDateTime();
        Assert.Equal(new DateTime(1, 1, 1), dt);
    }
    #endregion

    #region IsPeriodic
    [Fact]
    [System.ComponentModel.DisplayName("IsPeriodic 通配年份返回 true")]
    public void IsPeriodic_WildcardYear_True()
    {
        var date = new BacnetDate(255, 5, 14);
        Assert.True(date.IsPeriodic);
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsPeriodic 通配月份返回 true")]
    public void IsPeriodic_WildcardMonth_True()
    {
        var date = new BacnetDate(125, 255, 14);
        Assert.True(date.IsPeriodic);
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsPeriodic 奇数月份返回 true")]
    public void IsPeriodic_OddMonth_True()
    {
        var date = new BacnetDate(125, 13, 14); // 奇数月
        Assert.True(date.IsPeriodic);
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsPeriodic 普通日期返回 false")]
    public void IsPeriodic_NormalDate_False()
    {
        var date = new BacnetDate(125, 5, 14);
        Assert.False(date.IsPeriodic);
    }
    #endregion

    #region IsAFittingDate — 基本匹配
    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 精确日期匹配")]
    public void IsAFittingDate_ExactMatch()
    {
        var date = new BacnetDate(125, 5, 14); // 2025-05-14
        Assert.True(date.IsAFittingDate(new DateTime(2025, 5, 14)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 年份不匹配返回 false")]
    public void IsAFittingDate_WrongYear_False()
    {
        var date = new BacnetDate(125, 5, 14);
        Assert.False(date.IsAFittingDate(new DateTime(2024, 5, 14)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 月份不匹配返回 false")]
    public void IsAFittingDate_WrongMonth_False()
    {
        var date = new BacnetDate(125, 5, 14);
        Assert.False(date.IsAFittingDate(new DateTime(2025, 6, 14)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 日期不匹配返回 false")]
    public void IsAFittingDate_WrongDay_False()
    {
        var date = new BacnetDate(125, 5, 14);
        Assert.False(date.IsAFittingDate(new DateTime(2025, 5, 15)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 通配年份匹配任意年")]
    public void IsAFittingDate_WildcardYear()
    {
        var date = new BacnetDate(255, 3, 10); // 任意年 3月10日
        Assert.True(date.IsAFittingDate(new DateTime(2020, 3, 10)));
        Assert.True(date.IsAFittingDate(new DateTime(2025, 3, 10)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 通配月份匹配任意月")]
    public void IsAFittingDate_WildcardMonth()
    {
        var date = new BacnetDate(125, 255, 10); // 2025年 任意月 10日
        Assert.True(date.IsAFittingDate(new DateTime(2025, 1, 10)));
        Assert.True(date.IsAFittingDate(new DateTime(2025, 12, 10)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 通配日期匹配任意日")]
    public void IsAFittingDate_WildcardDay()
    {
        var date = new BacnetDate(125, 5, 255); // 2025年5月 任意日
        Assert.True(date.IsAFittingDate(new DateTime(2025, 5, 1)));
        Assert.True(date.IsAFittingDate(new DateTime(2025, 5, 31)));
    }
    #endregion

    #region IsAFittingDate — 奇偶月
    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 奇数月 month=13 匹配1月")]
    public void IsAFittingDate_OddMonth_January()
    {
        var date = new BacnetDate(125, 13, 1); // 2025 奇月 1日
        Assert.True(date.IsAFittingDate(new DateTime(2025, 1, 1)));
        Assert.False(date.IsAFittingDate(new DateTime(2025, 2, 1)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 偶数月 month=14 匹配2月")]
    public void IsAFittingDate_EvenMonth_February()
    {
        var date = new BacnetDate(125, 14, 1); // 2025 偶月 1日
        Assert.True(date.IsAFittingDate(new DateTime(2025, 2, 1)));
        Assert.False(date.IsAFittingDate(new DateTime(2025, 3, 1)));
    }
    #endregion

    #region IsAFittingDate — day=32 月最后一天（D004 回归测试）
    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate day=32 匹配1月31日（月最后一天）")]
    public void IsAFittingDate_Day32_January31()
    {
        var date = new BacnetDate(125, 1, 32); // 2025年1月 最后一天
        Assert.True(date.IsAFittingDate(new DateTime(2025, 1, 31)));
        Assert.False(date.IsAFittingDate(new DateTime(2025, 1, 30)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate day=32 匹配2月28日（非闰年）")]
    public void IsAFittingDate_Day32_February28_NotLeapYear()
    {
        var date = new BacnetDate(125, 2, 32); // 2025年2月 最后一天（非闰年）
        Assert.True(date.IsAFittingDate(new DateTime(2025, 2, 28)));
        Assert.False(date.IsAFittingDate(new DateTime(2025, 2, 27)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate day=32 匹配2月29日（闰年）")]
    public void IsAFittingDate_Day32_February29_LeapYear()
    {
        var date = new BacnetDate(124, 2, 32); // 2024年2月 最后一天（闰年）
        Assert.True(date.IsAFittingDate(new DateTime(2024, 2, 29)));
        Assert.False(date.IsAFittingDate(new DateTime(2024, 2, 28)));
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate day=32 通配年份时按目标年计算最后一天")]
    public void IsAFittingDate_Day32_WildcardYear()
    {
        var date = new BacnetDate(255, 2, 32); // 任意年 2月 最后一天
        Assert.True(date.IsAFittingDate(new DateTime(2025, 2, 28)));  // 非闰年
        Assert.True(date.IsAFittingDate(new DateTime(2024, 2, 29)));  // 闰年
        Assert.False(date.IsAFittingDate(new DateTime(2024, 2, 28))); // 闰年2月28日不是最后一天
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate day=32 匹配4月30日")]
    public void IsAFittingDate_Day32_April30()
    {
        var date = new BacnetDate(125, 4, 32); // 2025年4月 最后一天
        Assert.True(date.IsAFittingDate(new DateTime(2025, 4, 30)));
        Assert.False(date.IsAFittingDate(new DateTime(2025, 4, 29)));
    }
    #endregion

    #region IsAFittingDate — 星期
    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate 通配星期匹配任何星期")]
    public void IsAFittingDate_WildcardWeekday()
    {
        var date = new BacnetDate(125, 5, 14, 255); // 通配星期
        Assert.True(date.IsAFittingDate(new DateTime(2025, 5, 14))); // 周三
    }

    [Fact]
    [System.ComponentModel.DisplayName("IsAFittingDate BACnet周日=7 匹配 .NET DayOfWeek=0")]
    public void IsAFittingDate_Sunday_BacnetWeekday7()
    {
        // 2025-05-11 是周日
        var date = new BacnetDate(125, 5, 11, 7); // BACnet 周日=7
        Assert.True(date.IsAFittingDate(new DateTime(2025, 5, 11)));
    }
    #endregion
}

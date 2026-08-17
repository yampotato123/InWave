using InWave.Services;

namespace InWave.Tests;

/// <summary>
/// 情緒 × 濾鏡 → YouTube 搜尋關鍵字。
/// 這是整條推薦鏈唯一有分支的地方,壞掉的話使用者只會發現「推薦怪怪的」,不會有例外。
/// </summary>
public class MoodKeywordMapperTests
{
    [Fact]
    public void 沒有濾鏡時_回情緒本身的關鍵字()
    {
        Assert.Equal("warm lo-fi afternoon music", MoodKeywordMapper.GetKeyword("午後"));
    }

    [Fact]
    public void 有濾鏡時_修飾詞接在情緒關鍵字後面()
    {
        Assert.Equal("warm lo-fi afternoon music night",
            MoodKeywordMapper.GetKeyword("午後", "冷夜"));
    }

    [Theory]
    [InlineData("暖陽", "warm")]
    [InlineData("冷夜", "night")]
    [InlineData("褪色", "vintage")]
    public void 三種濾鏡各自接上正確的修飾詞(string filterName, string expectedModifier)
    {
        Assert.Equal($"calm piano instrumental {expectedModifier}",
            MoodKeywordMapper.GetKeyword("安靜", filterName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 濾鏡為空_不加修飾詞(string? filterName)
    {
        Assert.Equal("late night chill music", MoodKeywordMapper.GetKeyword("夜色", filterName));
    }

    [Fact]
    public void 認不得的濾鏡_不加修飾詞而不是丟例外()
    {
        Assert.Equal("late night chill music",
            MoodKeywordMapper.GetKeyword("夜色", "不存在的濾鏡"));
    }

    [Fact]
    public void 認不得的情緒_退回預設關鍵字_但濾鏡仍生效()
    {
        Assert.Equal("chill music warm", MoodKeywordMapper.GetKeyword("不存在的情緒", "暖陽"));
    }

    // 2026-08-17 情緒改為可不選(設計文件 §3.2.1)之後,空字串是正常的生產輸入而非錯誤。
    // AI 尚未接上、或 n8n 掛掉走 fallback 時,整條推薦鏈都靠下面這兩條撐住。
    [Fact]
    public void 情緒未指定_退回預設關鍵字_而不是丟例外()
    {
        Assert.Equal("chill music", MoodKeywordMapper.GetKeyword(""));
    }

    [Fact]
    public void 情緒未指定但有套濾鏡_修飾詞仍要生效()
    {
        Assert.Equal("chill music night", MoodKeywordMapper.GetKeyword("", "冷夜"));
    }

    [Fact]
    public void 八種情緒都查得到自己的關鍵字_沒有漏掉任何一個()
    {
        foreach (var mood in MoodKeywordMapper.AllMoods)
        {
            Assert.NotEqual("chill music", MoodKeywordMapper.GetKeyword(mood));
        }
    }
}

/// <summary>濾鏡定義本身的約束——這些若被改壞,前端與後端會對不上。</summary>
public class PhotoFiltersTests
{
    [Fact]
    public void 恰好三種濾鏡()
    {
        Assert.Equal(3, PhotoFilters.All.Length);
    }

    [Fact]
    public void 濾鏡名稱不得與情緒名稱重疊()
    {
        // 重疊的話使用者會分不清「我選的情緒」和「我套的濾鏡」誰決定推薦
        foreach (var filter in PhotoFilters.All)
        {
            Assert.DoesNotContain(filter.Name, MoodKeywordMapper.AllMoods);
        }
    }

    [Fact]
    public void 每種濾鏡都有css字串與搜尋修飾詞()
    {
        foreach (var filter in PhotoFilters.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(filter.CssFilter), $"{filter.Name} 缺 CssFilter");
            Assert.False(string.IsNullOrWhiteSpace(filter.KeywordModifier), $"{filter.Name} 缺 KeywordModifier");
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("暖陽", true)]
    [InlineData("夜色", false)]   // 這是情緒名,不是濾鏡名
    [InlineData("random", false)]
    public void IsValid_只接受空值與已定義的濾鏡名(string? name, bool expected)
    {
        Assert.Equal(expected, PhotoFilters.IsValid(name));
    }
}

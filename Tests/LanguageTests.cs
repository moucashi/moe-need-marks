using MoeNeedMarks.Shared;
using Xunit;

namespace MoeNeedMarks.Tests;

public class LanguageTests
{
    [Theory]
    [InlineData("ch", UiLanguage.Chinese)]
    [InlineData("zh-TW", UiLanguage.Chinese)]
    [InlineData("en", UiLanguage.English)]
    [InlineData("ru", UiLanguage.English)]
    [InlineData(null, UiLanguage.English)]
    public void AutomaticFallbackAndOverrides(string? culture, UiLanguage expected)
    {
        Assert.Equal(expected, UiText.Resolve(UiLanguage.Auto, culture));
        Assert.Equal(UiLanguage.Chinese, UiText.Resolve(UiLanguage.Chinese, culture));
        Assert.Equal(UiLanguage.English, UiText.Resolve(UiLanguage.English, culture));
    }

    [Fact]
    public void SwitchingLanguageKeepsNumbersNamesColorsAndSections()
    {
        var need = new NeedResult { QuestRequired = 11, QuestSubmitted = 5, AreaRequired = 15, AreaSubmitted = 6,
            Quests = new() { new() { NameKey = "quest", Required = 11, Submitted = 5, Fir = true, Shared = true, Branch = true, Kind = GoalKind.Gunsmith, State = DisplayState.Active } },
            Areas = new() { new() { NameKey = "area", Required = 15, Submitted = 6, Level = 2, State = DisplayState.Completed } } };
        string Name(string key, string fallback) => key == "quest" ? "Localized quest" : "Localized area";
        var english = TooltipFormatter.Lines(need, 3, 4, new(), Name, UiLanguage.English);
        Assert.Contains("Total needs (18/26)", english);
        Assert.Contains("Owned (3+4) 7", english);
        Assert.Contains("[Accepted] Localized quest (5/11) FIR Any option / shared progress Exclusive branch Whole weapon", english);
        Assert.Contains("<color=#777777>[Built] Localized area Lv. 2 (6/15)</color>", english);
        var chinese = TooltipFormatter.Lines(need, 3, 4, new(), Name, UiLanguage.Chinese);
        Assert.Contains("总共需要 (18/26)", chinese);
        Assert.Equal(english.Count, chinese.Count);
        Assert.Equal(english.Count(l => l == ""), chinese.Count(l => l == ""));
        Assert.Empty(TooltipFormatter.Lines(new(), 3, 4, new(), Name, UiLanguage.English));
        Assert.DoesNotContain(TooltipFormatter.Lines(need, 3, 4, new() { Completed = false }, Name, UiLanguage.English), l => l.Contains("[Built]"));
    }
}

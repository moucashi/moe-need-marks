using MoeNeedMarks.Shared;
using Xunit;

namespace MoeNeedMarks.Tests;

public class TooltipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompletedColorTakesPrecedenceAndEachRowClosesItsColor(bool area, bool fir)
    {
        var detail = new Detail { NameKey = "名称", Required = 2, Fir = fir, State = DisplayState.Completed };
        var result = new NeedResult { QuestRequired = area ? 0 : 2, AreaRequired = area ? 2 : 0 };
        (area ? result.Areas : result.Quests).Add(detail);
        var done = TooltipFormatter.Lines(result, 0, 0, new());
        Assert.StartsWith("<color=#777777>", done[1]);
        Assert.EndsWith("</color>", done[1]);
        detail.State = DisplayState.Future;
        var pending = TooltipFormatter.Lines(result, 0, 0, new());
        if (fir) Assert.DoesNotContain("<color", pending[1]);
        else { Assert.StartsWith("<color=#929DA6>", pending[1]); Assert.EndsWith("</color>", pending[1]); }
        Assert.DoesNotContain("<color", pending.Last());
    }

    private static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");

    private static NeedResult Example() => new()
    {
        QuestRequired = 6, QuestSubmitted = 3, AreaRequired = 7,
        Quests = new() { new() { NameKey = "任务甲", Required = 3, State = DisplayState.Future }, new() { NameKey = "任务乙", Submitted = 3, Required = 3, State = DisplayState.Completed } },
        Areas = new() { new() { NameKey = "设施甲", Level = 1, Required = 3, State = DisplayState.Available }, new() { NameKey = "设施乙", Level = 2, Required = 4, State = DisplayState.Future } }
    };

    [Fact]
    public void ScreenshotExamplePlacesInventoryAndCombinedTotalLast()
    {
        var lines = TooltipFormatter.Lines(Example(), 0, 5, new());
        Assert.Equal(new[]
        {
            "任务需要 (8/6)", "[未接取] 任务甲 (0/3)", "[已完成] 任务乙 (3/3)", "",
            "藏身处需要 (5/7)", "[未建造] 设施甲 1级 (0/3)", "[未建造] 设施乙 2级 (0/4)", "",
            "当前已有 (0+5) 5", "总共需要 (8/13)"
        }, lines.Select(Plain));
    }

    [Fact]
    public void AllDisplaySwitchCombinationsKeepRelativeOrderAndCounts()
    {
        var result = Example();
        var all = TooltipFormatter.Lines(result, 0, 5, new()).Where(l => l.Length > 0).ToList();
        for (int mask = 0; mask < 64; mask++)
        {
            var options = new DisplayOptions
            {
                QuestSummary = (mask & 1) != 0, QuestDetails = (mask & 2) != 0,
                AreaSummary = (mask & 4) != 0, AreaDetails = (mask & 8) != 0,
                Inventory = (mask & 16) != 0, Total = (mask & 32) != 0
            };
            var expected = all.Where((_, i) => i switch
            {
                0 => options.QuestSummary, 1 or 2 => options.QuestDetails,
                3 => options.AreaSummary, 4 or 5 => options.AreaDetails, 6 => options.Inventory, _ => options.Total
            });
            var actual = TooltipFormatter.Lines(result, 0, 5, options);
            Assert.Equal(expected, actual.Where(l => l.Length > 0));
            int blocks = (options.QuestSummary || options.QuestDetails ? 1 : 0)
                + (options.AreaSummary || options.AreaDetails ? 1 : 0) + (options.Inventory || options.Total ? 1 : 0);
            Assert.Equal(Math.Max(0, blocks - 1), actual.Count(l => l.Length == 0));
            if (actual.Count > 0) { Assert.NotEmpty(actual[0]); Assert.NotEmpty(actual[^1]); }
            Assert.DoesNotContain("\n\n\n", string.Join("\n", actual));
        }
    }

    [Fact]
    public void NoDemandHidesInventoryAndAllNeedBlocks()
    {
        Assert.Empty(TooltipFormatter.Lines(new(), 5, 6, new()));
    }

    [Fact]
    public void CompactDetailsMatchRequestedStatusProgressAndFirFormat()
    {
        var result = new NeedResult
        {
            QuestRequired = 4, AreaRequired = 4,
            Quests = new() { new() { NameKey = "未接任务", State = DisplayState.Available, Required = 2 }, new() { NameKey = "已接任务", State = DisplayState.Active, Submitted = 1, Required = 2, Fir = true } },
            Areas = new() { new() { NameKey = "未建设施", Level = 3, State = DisplayState.Future, Required = 2, Fir = true }, new() { NameKey = "已建设施", Level = 1, State = DisplayState.Completed, Required = 2, Submitted = 2 } }
        };
        var lines = TooltipFormatter.Lines(result, 0, 0, new());
        Assert.Contains("[未接取] 未接任务 (0/2)", lines.Select(Plain));
        Assert.Contains("[已接取] 已接任务 (1/2) FIR", lines);
        Assert.Contains("[未建造] 未建设施 3级 (0/2) FIR", lines);
        Assert.Contains("[已建造] 已建设施 1级 (2/2)", lines.Select(Plain));
        Assert.DoesNotContain(lines, l => l.Contains("已提交") || l.Contains("Lv."));
        var hidden = TooltipFormatter.Lines(result, 0, 0, new() { Future = false, Available = false });
        Assert.DoesNotContain(hidden, l => Plain(l).StartsWith("[未"));
        Assert.Contains("任务需要 (0/4)", hidden); // visibility still does not alter totals
    }

    [Fact]
    public void PeriodicAndConfigurationRefreshKeepVisibleTextUntilReplacementIsReady()
    {
        var cache = new RefreshCache<NeedCalculator>();
        var snapshot = new Snapshot { Quests = new() { CalculationTests.Quest("q", CalculationTests.Goal(required: 2)) } };
        cache.Publish(new NeedCalculator(snapshot, new(), 100));
        var state = new TooltipTextState(); state.Reset("a");
        string Render() => state.Compose("物品", string.Join("\n", TooltipFormatter.Lines(cache.Value!.Get("a"), 0, 1, new())));
        string previous = Render();
        for (int refresh = 0; refresh < 30; refresh++)
        {
            cache.Invalidate(); // response arrival / inventory event / F12 / repeatable expiry
            Assert.True(cache.NeedsRefresh);
            for (int deferredFrame = 0; deferredFrame < 10; deferredFrame++) Assert.Equal(previous, Render());
            cache.Publish(new NeedCalculator(snapshot, new(), 100));
            Assert.False(cache.NeedsRefresh);
            Assert.Equal(previous, Render());
        }
        cache.Invalidate();
        Assert.Throws<ArgumentNullException>(() => cache.Publish(null!));
        Assert.Equal(previous, Render()); // failed replacement retains last valid state
        cache.Publish(new NeedCalculator(new(), new(), 100));
        Assert.Equal("物品", Render()); // a genuinely removed demand does disappear
        cache.Clear();
        Assert.Null(cache.Value); // a different session must not see the old character
    }

    [Fact]
    public void RepeatedRefreshPreservesNativeRichTextAndReevaluatesTraderSuffix()
    {
        const string original = "<b>生理盐水</b><br><color=#FFAA00>价格 ₽12000</color>";
        var state = new TooltipTextState(); state.Reset("saline");
        string displayed = original;
        for (int i = 0; i < 30; i++)
        {
            string source = i == 0 ? original : state.RefreshInput(displayed);
            // Model the installed trade marker's SetText prefix after our capture.
            string native = state.CaptureInput(source) + "\n\n交易标记：商人" + i;
            displayed = state.Compose(native, "当前已有 (0+" + i + ") " + i + "\n总共需要 (" + i + "/13)");
            Assert.Equal(native, TooltipTextState.Strip(displayed));
            Assert.Equal(original, state.SourceText);
            Assert.Equal(1, displayed.Split("当前已有").Length - 1);
            Assert.Equal(1, displayed.Split("交易标记").Length - 1);
            Assert.DoesNotContain("商人" + (i - 1) + "<link", displayed);
        }
    }

    [Fact]
    public void ReplayedOldBlocksAndThirdPartyAppendicesDoNotDuplicateOrDisappear()
    {
        var state = new TooltipTextState(); state.Reset("a");
        string old = state.Compose("名称", "旧需求");
        string current = state.Compose(state.CaptureInput(old), "新需求");
        string external = old + "\n<color=green>另一模组</color>";
        string refreshed = state.Compose(state.CaptureInput(state.RefreshInput(external)), "新需求");
        Assert.Equal("名称\n<color=green>另一模组</color>", TooltipTextState.Strip(refreshed));
        Assert.DoesNotContain("旧需求", refreshed);
        Assert.Equal(1, refreshed.Split("新需求").Length - 1);
        Assert.Equal("名称", TooltipTextState.Strip(current));
    }

    [Fact]
    public void SwitchingItemsClosingAndNonItemReuseRemoveOnlyOurBlock()
    {
        var state = new TooltipTextState(); state.Reset("a");
        state.CaptureInput("物品甲");
        string old = state.Compose("物品甲\n价格", "甲需求");
        Assert.Equal("物品甲\n价格", TooltipTextState.Strip(old));
        state.Reset("b");
        string next = state.Compose(state.CaptureInput("物品乙"), "乙需求");
        Assert.DoesNotContain("甲", next);
        state.Reset(null);
        Assert.Null(state.ItemId);
        Assert.Equal("警告信息", state.Compose(state.CaptureInput("警告信息"), "不应显示"));
        Assert.Equal("物品乙", state.Compose(next, "不应显示"));
    }

    [Fact]
    public void EmptyOrUnavailableAdditionRestoresOtherModsTextExactly()
    {
        var state = new TooltipTextState(); state.Reset("a");
        const string native = "<link=\"other\">名称</link>\n\n交易标记\n";
        string displayed = state.Compose(native, "总共需要 (1/2)");
        Assert.Equal(native, state.Compose(displayed, ""));
        Assert.Equal(native, TooltipTextState.Strip(displayed));
        string emptyNative = state.Compose("", "当前已有 (0+0) 0");
        Assert.Contains(">当前已有 (0+0) 0</link>", emptyNative);
        Assert.DoesNotContain("\n", emptyNative);
    }
}

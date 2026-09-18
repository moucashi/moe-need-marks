using MoeNeedMarks.Shared;
using Xunit;

namespace MoeNeedMarks.Tests;

public class TooltipTests
{
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
            "任务需要 (8/6)", "  [未来] 任务甲：已提交 0/3", "  [已完成] 任务乙：已提交 3/3",
            "藏身处需要 (5/7)", "  [待建造／升级] 设施甲 Lv.1：已提交 0/3", "  [未来] 设施乙 Lv.2：已提交 0/4",
            "当前已有 (0+5) 5", "总共需要 (8/13)"
        }, lines);
    }

    [Fact]
    public void AllDisplaySwitchCombinationsKeepRelativeOrderAndCounts()
    {
        var result = Example();
        var all = TooltipFormatter.Lines(result, 0, 5, new());
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
            Assert.Equal(expected, TooltipFormatter.Lines(result, 0, 5, options));
        }
    }

    [Fact]
    public void NoDemandStillShowsInventoryWithoutEmptyDemandBlocks()
    {
        Assert.Equal(new[] { "当前已有 (5+6) 11" }, TooltipFormatter.Lines(new(), 5, 6, new()));
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

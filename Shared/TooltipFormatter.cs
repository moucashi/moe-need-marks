namespace MoeNeedMarks.Shared;

public static class TooltipFormatter
{
    public static List<string> Lines(NeedResult result, long carried, long stash, DisplayOptions options,
        Func<string, string, string>? localize = null)
    {
        localize ??= (key, fallback) => string.IsNullOrEmpty(fallback) ? key : fallback;
        var lines = new List<string>(); long owned = carried + stash;
        if (result.QuestRequired > 0)
        {
            if (options.QuestSummary) lines.Add($"任务需要 ({result.QuestSubmitted + owned}/{result.QuestRequired})");
            if (options.QuestDetails) AddDetails(result.Quests, false);
        }
        if (result.AreaRequired > 0)
        {
            if (options.AreaSummary) lines.Add($"藏身处需要 ({result.AreaSubmitted + owned}/{result.AreaRequired})");
            if (options.AreaDetails) AddDetails(result.Areas, true);
        }
        if (options.Inventory) lines.Add($"当前已有 ({carried}+{stash}) {owned}");
        if (options.Total && result.QuestRequired + result.AreaRequired > 0)
            lines.Add($"总共需要 ({result.QuestSubmitted + result.AreaSubmitted + owned}/{result.QuestRequired + result.AreaRequired})");
        return lines;

        void AddDetails(List<Detail> details, bool area)
        {
            foreach (var detail in details.Where(d => options.Shows(d.State)).OrderBy(d => Order(d.State)).ThenBy(d => d.NameKey, StringComparer.Ordinal).ThenBy(d => d.Level))
            {
                string name = localize(detail.NameKey, detail.FallbackName);
                if (area) name += $" Lv.{detail.Level}";
                string note = detail.Fir ? " · FIR" : "";
                if (detail.Shared) note += " · 任选／共享进度";
                if (detail.Branch) note += " · 互斥分支（汇总取可行最大值）";
                if (detail.Kind != GoalKind.Handover) note += detail.Kind switch
                { GoalKind.Gunsmith => " · 整枪", GoalKind.Plant => " · 埋放", _ => " · 信标" };
                lines.Add($"  [{State(detail.State, area)}] {name}：已提交 {detail.Submitted}/{detail.Required}{note}");
            }
        }
    }

    private static int Order(DisplayState state) => state switch
    { DisplayState.Active => 0, DisplayState.Ready => 1, DisplayState.Building => 2, DisplayState.Available => 3, DisplayState.Future => 4, _ => 5 };

    private static string State(DisplayState state, bool area) => state switch
    {
        DisplayState.Completed => area ? "已建成" : "已完成",
        DisplayState.Active => "进行中", DisplayState.Ready => "可完成",
        DisplayState.Available => area ? "待建造／升级" : "可接取",
        DisplayState.Building => "施工中", _ => "未来"
    };
}

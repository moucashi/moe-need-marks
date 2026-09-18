namespace MoeNeedMarks.Shared;

public static class TooltipFormatter
{
    public static List<string> Lines(NeedResult result, long carried, long stash, DisplayOptions options,
        Func<string, string, string>? localize = null, UiLanguage language = UiLanguage.Chinese)
    {
        string T(string text) => UiText.Get(text, language);
        localize ??= (key, fallback) => string.IsNullOrEmpty(fallback) ? key : fallback;
        var lines = new List<string>(); long owned = carried + stash;
        if (result.QuestRequired <= 0 && result.AreaRequired <= 0) return lines;
        if (result.QuestRequired > 0)
        {
            if (options.QuestSummary) lines.Add($"{T("任务需要")} ({result.QuestSubmitted + owned}/{result.QuestRequired})");
            if (options.QuestDetails) AddDetails(result.Quests, false);
        }
        int areaStart = lines.Count;
        if (result.AreaRequired > 0)
        {
            if (options.AreaSummary) lines.Add($"{T("藏身处需要")} ({result.AreaSubmitted + owned}/{result.AreaRequired})");
            if (options.AreaDetails) AddDetails(result.Areas, true);
        }
        SeparateBlock(areaStart);
        int totalsStart = lines.Count;
        if (options.Inventory) lines.Add($"{T("当前已有")} ({carried}+{stash}) {owned}");
        if (options.Total && result.QuestRequired + result.AreaRequired > 0)
            lines.Add($"{T("总共需要")} ({result.QuestSubmitted + result.AreaSubmitted + owned}/{result.QuestRequired + result.AreaRequired})");
        SeparateBlock(totalsStart);
        return lines;

        void SeparateBlock(int start)
        {
            if (start > 0 && lines.Count > start) lines.Insert(start, "");
        }

        void AddDetails(List<Detail> details, bool area)
        {
            foreach (var detail in details.Where(d => options.Shows(d.State)).OrderBy(d => Order(d.State)).ThenBy(d => d.NameKey, StringComparer.Ordinal).ThenBy(d => d.Level))
            {
                string name = localize(detail.NameKey, detail.FallbackName);
                if (area) name += language == UiLanguage.Chinese ? $" {detail.Level}级" : $" Lv. {detail.Level}";
                string note = detail.Fir ? " FIR" : "";
                if (detail.Shared) note += " " + T("任选／共享进度");
                if (detail.Branch) note += " " + T("互斥");
                if (detail.Kind != GoalKind.Handover) note += detail.Kind switch
                { GoalKind.Gunsmith => " " + T("整枪"), GoalKind.Plant => " " + T("埋放"), _ => " " + T("信标") };
                string row = $"[{T(State(detail.State, area))}] {name} ({detail.Submitted}/{detail.Required}){note}";
                string? color = detail.State == DisplayState.Completed ? "#777777" : !detail.Fir ? "#929DA6" : null;
                lines.Add(color == null ? row : $"<color={color}>{row}</color>");
            }
        }
    }

    private static int Order(DisplayState state) => state switch
    { DisplayState.Active => 0, DisplayState.Ready => 1, DisplayState.Building => 2, DisplayState.Available => 3, DisplayState.Future => 4, _ => 5 };

    private static string State(DisplayState state, bool area) => state switch
    {
        DisplayState.Completed => area ? "已建造" : "已完成",
        DisplayState.Active => "已接取", DisplayState.Ready => "可完成",
        DisplayState.Building => "建造中", _ => area ? "未建造" : "未接取"
    };
}

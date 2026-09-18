namespace MoeNeedMarks.Shared;

public enum UiLanguage { Auto, Chinese, English }

public static class UiText
{
    public static UiLanguage Resolve(UiLanguage selected, string? culture) => selected != UiLanguage.Auto ? selected
        : culture != null && (culture.Equals("ch", StringComparison.OrdinalIgnoreCase) || culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ? UiLanguage.Chinese : UiLanguage.English;

    public static string Get(string text, UiLanguage language) => language == UiLanguage.Chinese ? text : text switch
    {
        "任务需要" => "Quest needs", "藏身处需要" => "Hideout needs", "当前已有" => "Owned", "总共需要" => "Total needs",
        "已建造" => "Built", "已完成" => "Completed", "已接取" => "Accepted", "可完成" => "Ready to complete",
        "建造中" => "Building", "未建造" => "Not built", "未接取" => "Not accepted",
        "任选／共享进度" => "Any option / shared progress", "互斥" => "Exclusive branch",
        "整枪" => "Whole weapon", "埋放" => "Plant", "信标" => "Beacon", "周期任务" => "Repeatable quest",
        _ => text
    };
}

using BepInEx.Configuration;
using MoeNeedMarks.Shared;
using UnityEngine;
using EFT;
using System.ComponentModel;

namespace MoeNeedMarks.Client;

internal static class Settings
{
    private static ConfigEntry<UiLanguage> language = null!;
    public static UiLanguage Language => UiText.Resolve(language.Value, LocalizationManager.Instance.Culture);
    private static readonly Dictionary<string, string> English = new()
    {
        ["01 彩色勾"] = "01 Checkmarks", ["02 统计范围"] = "02 Scope", ["03 悬浮汇总"] = "03 Summaries", ["04 悬浮明细"] = "04 Details",
        ["显示任务勾"] = "Quest checkmark", ["显示设施勾"] = "Hideout checkmark",
        ["任务颜色"] = "Quest color", ["设施颜色"] = "Hideout color", ["共同需求颜色"] = "Combined color",
        ["包含枪匠整枪"] = "Include Gunsmith weapons", ["包含埋放物品"] = "Include planted items", ["包含安装信标"] = "Include beacons",
        ["日周常仅包含已接取"] = "Accepted repeatables only", ["显示当前已有"] = "Show owned count", ["显示总共需要"] = "Show combined total",
        ["显示任务需要"] = "Show quest summary", ["显示藏身处需要"] = "Show hideout summary",
        ["显示任务明细"] = "Show quest details", ["显示设施明细"] = "Show hideout details",
        ["显示未来明细"] = "Show future details", ["显示可接取及待建明细"] = "Show available details",
        ["显示进行中明细"] = "Show active details", ["显示历史明细"] = "Show completed details"
    };
    private static string Help(string key) => key switch
    {
        "显示任务勾" => "FIR items with outstanding FIR quest needs only. Having enough stock does not remove the mark.",
        "显示设施勾" => "FIR items with outstanding FIR construction or upgrade needs only.",
        "任务颜色" => "Quest-only color (yellow by default).", "设施颜色" => "Hideout-only color (blue by default).",
        "共同需求颜色" => "Combined quest and hideout color (green by default).",
        "包含枪匠整枪" => "Count whole weapons, not parts. Owned weapons may still need modification.",
        "包含埋放物品" => "Include planting objectives in needs and submitted progress.",
        "包含安装信标" => "Include beacon objectives in needs and submitted progress.",
        "日周常仅包含已接取" => "Off: available, accepted and current-cycle completed quests. On: exclude unaccepted quests. Expired, unavailable and ungenerated quests are always excluded.",
        "显示当前已有" => "Carried + stash (including sorting table), FIR and non-FIR. Hidden when there are no included requirements. Trader stock is excluded.",
        "显示总共需要" => "Quest submissions + hideout submissions + owned stock; stock is counted once.",
        "显示任务需要" => "Submitted + owned / included quest requirements. Display only.",
        "显示藏身处需要" => "Submitted + owned / all construction requirements. History is inferred from current recipes and built levels.",
        "显示任务明细" => "Quest names, status, submitted progress and FIR requirements. Display only.",
        "显示设施明细" => "Hideout stages, submitted progress and FIR requirements. Display only.",
        "显示未来明细" => "Future quests and hideout levels. Does not change totals or marks.",
        "显示可接取及待建明细" => "Available quests and next hideout levels. Does not change totals or marks.",
        "显示进行中明细" => "Accepted/ready quests and construction in progress. Does not change totals or marks.",
        _ => "Completed quests and built hideout stages. Does not change totals or marks."
    };
    public static ConfigEntry<bool> QuestMark = null!, AreaMark = null!;
    public static ConfigEntry<Color> QuestColor = null!, AreaColor = null!, BothColor = null!;
    private static ConfigEntry<bool> gunsmith = null!, plant = null!, beacon = null!, acceptedOnly = null!;
    private static ConfigEntry<bool> inventory = null!, total = null!, questSummary = null!, areaSummary = null!, questDetails = null!, areaDetails = null!;
    private static ConfigEntry<bool> future = null!, available = null!, active = null!, completed = null!;

    public static void Bind(ConfigFile config)
    {
        ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
        {
            var entry = config.Bind(section, key, value, new ConfigDescription(description + "\n" + Help(key), null,
                new DisplayNameAttribute(key + " / " + English[key]), new CategoryAttribute(English[section] + " / " + section)));
            entry.SettingChanged += (_, _) => RuntimeData.Invalidate(true);
            return entry;
        }
        language = config.Bind("00 Language / 语言", "Language", UiLanguage.Auto,
            "Auto：跟随游戏，中文使用中文，其余语言回退英文。Chinese / English：手动选择提示语言。任务及设施名称仍跟随游戏。\nAuto follows the game (Chinese or English fallback). Chinese / English overrides tooltip labels. Quest and hideout names follow the game. F12 labels are bilingual.");
        language.SettingChanged += (_, _) => RuntimeData.Invalidate();
        QuestMark = Bind("01 彩色勾", "显示任务勾", true, "仅 FIR 物品有尚未交满的 FIR 任务需求时显示。库存备齐不会消勾。");
        AreaMark = Bind("01 彩色勾", "显示设施勾", true, "仅 FIR 物品有尚未提交的 FIR 建造或升级需求时显示。");
        QuestColor = Bind("01 彩色勾", "任务颜色", Color.yellow, "只有任务需求时的颜色，默认黄色。");
        AreaColor = Bind("01 彩色勾", "设施颜色", new Color(0.15f, 0.55f, 1f, 1f), "只有设施需求时的颜色，默认蓝色。");
        BothColor = Bind("01 彩色勾", "共同需求颜色", Color.green, "同时有任务和设施需求时的颜色，默认绿色。");
        gunsmith = Bind("02 统计范围", "包含枪匠整枪", false, "统计枪匠任务需要上交的整枪；不统计配件，也不表示现有武器已符合改装要求。");
        plant = Bind("02 统计范围", "包含埋放物品", false, "将任务埋放物品的目标纳入明细、总量和进度。");
        beacon = Bind("02 统计范围", "包含安装信标", false, "将任务安装信标的目标纳入明细、总量和进度。");
        acceptedOnly = Bind("02 统计范围", "日周常仅包含已接取", false, "关闭：包含可接取、已接取和本周期已完成。开启：排除尚未接取。旧周期、不可接取和未生成任务始终排除。");
        inventory = Bind("03 悬浮汇总", "显示当前已有", true, "仅对有任务或设施需求的物品显示（随身+仓库）合计，包含 FIR 和非 FIR。仓库包含整理台。");
        total = Bind("03 悬浮汇总", "显示总共需要", true, "任务已提交 + 设施已提交 + 当前库存；库存只计一次。");
        questSummary = Bind("03 悬浮汇总", "显示任务需要", true, "已提交 + 当前库存 / 全部纳入任务需求。仅控制显示。");
        areaSummary = Bind("03 悬浮汇总", "显示藏身处需要", true, "已提交 + 当前库存 / 全部设施建造升级需求。历史消耗按当前配方和设施等级推算。");
        questDetails = Bind("04 悬浮明细", "显示任务明细", true, "列出任务名、状态、提交进度与 FIR 要求。仅隐藏明细，不改变统计和勾。");
        areaDetails = Bind("04 悬浮明细", "显示设施明细", true, "逐设施、等级列出进度和 FIR 要求。仅隐藏明细，不改变统计和勾。");
        future = Bind("04 悬浮明细", "显示未来明细", true, "控制未来任务和未来设施等级明细；不改变统计和勾。");
        available = Bind("04 悬浮明细", "显示可接取及待建明细", true, "控制可接取任务和下一设施等级明细；不改变统计和勾。");
        active = Bind("04 悬浮明细", "显示进行中明细", true, "控制进行中、可完成任务和施工中设施明细；不改变统计和勾。");
        completed = Bind("04 悬浮明细", "显示历史明细", true, "控制已完成任务和已建成设施明细；不改变统计和勾。");
    }

    public static NeedOptions Need => new() { Gunsmith = gunsmith.Value, Plant = plant.Value, Beacon = beacon.Value, AcceptedRepeatablesOnly = acceptedOnly.Value };
    public static DisplayOptions Display => new()
    {
        Inventory = inventory.Value, Total = total.Value, QuestSummary = questSummary.Value, AreaSummary = areaSummary.Value,
        QuestDetails = questDetails.Value, AreaDetails = areaDetails.Value, Future = future.Value,
        Available = available.Value, Active = active.Value, Completed = completed.Value
    };
    public static Color ColorFor(Marker marker) => marker switch
    { Marker.Quest => QuestColor.Value, Marker.Hideout => AreaColor.Value, _ => BothColor.Value };
}

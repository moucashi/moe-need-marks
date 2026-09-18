using BepInEx.Configuration;
using MoeNeedMarks.Shared;
using UnityEngine;

namespace MoeNeedMarks.Client;

internal static class Settings
{
    public static ConfigEntry<bool> QuestMark = null!, AreaMark = null!;
    public static ConfigEntry<Color> QuestColor = null!, AreaColor = null!, BothColor = null!;
    private static ConfigEntry<bool> gunsmith = null!, plant = null!, beacon = null!, acceptedOnly = null!;
    private static ConfigEntry<bool> inventory = null!, total = null!, questSummary = null!, areaSummary = null!, questDetails = null!, areaDetails = null!;
    private static ConfigEntry<bool> future = null!, available = null!, active = null!, completed = null!;

    public static void Bind(ConfigFile config)
    {
        ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
        {
            var entry = config.Bind(section, key, value, description);
            entry.SettingChanged += (_, _) => RuntimeData.Invalidate(true);
            return entry;
        }
        QuestMark = Bind("01 彩色勾", "显示任务勾", true, "仅 FIR 物品有尚未交满的 FIR 任务需求时显示。库存备齐不会消勾。");
        AreaMark = Bind("01 彩色勾", "显示设施勾", true, "仅 FIR 物品有尚未提交的 FIR 建造或升级需求时显示。");
        QuestColor = Bind("01 彩色勾", "任务颜色", Color.yellow, "只有任务需求时的颜色，默认黄色。");
        AreaColor = Bind("01 彩色勾", "设施颜色", new Color(0.15f, 0.55f, 1f, 1f), "只有设施需求时的颜色，默认蓝色。");
        BothColor = Bind("01 彩色勾", "共同需求颜色", Color.green, "同时有任务和设施需求时的颜色，默认绿色。");
        gunsmith = Bind("02 统计范围", "包含枪匠整枪", false, "统计枪匠任务需要上交的整枪；不统计配件，也不表示现有武器已符合改装要求。");
        plant = Bind("02 统计范围", "包含埋放物品", false, "将任务埋放物品的目标纳入明细、总量和进度。");
        beacon = Bind("02 统计范围", "包含安装信标", false, "将任务安装信标的目标纳入明细、总量和进度。");
        acceptedOnly = Bind("02 统计范围", "日周常仅包含已接取", false, "关闭：包含可接取、已接取和本周期已完成。开启：排除尚未接取。旧周期、不可接取和未生成任务始终排除。");
        inventory = Bind("03 悬浮汇总", "显示当前已有", true, "显示（随身+仓库）合计，包含 FIR 和非 FIR。仓库包含整理台。");
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

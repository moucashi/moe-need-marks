namespace MoeNeedMarks.Shared;

public static class ModInfo
{
    public const string Version = "1.0.2";
    public const string Guid = "moe.needmarks";
    public const string Name = "Moe Need Marks";
    public const string Route = "/moe/needmarks/snapshot";
}

public enum GoalKind { Handover, Gunsmith, Plant, Beacon }
public enum DisplayState { Future, Available, Active, Ready, Completed, Building }
public enum Marker { None, Quest, Hideout, Both }

public sealed class Snapshot
{
    public int Schema { get; set; } = 1;
    public string ProfileId { get; set; } = "";
    public long Timestamp { get; set; }
    public List<QuestInfo> Quests { get; set; } = new();
    public List<AreaInfo> Areas { get; set; } = new();
    public List<OwnedItem> Stash { get; set; } = new();
}

public sealed class QuestInfo
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string FallbackName { get; set; } = "";
    // EFT: 0 locked, 1 available, 2 started, 3 ready, 4 success, 5 fail,
    // 6 fail/restartable, 7 marked failed, 8 expired, 9 available after a delay.
    public int Status { get; set; }
    public bool Allowed { get; set; } = true;
    public bool Repeatable { get; set; }
    public long CycleEnd { get; set; }
    public List<QuestLink> Start { get; set; } = new();
    public List<QuestLink> Finish { get; set; } = new();
    public List<QuestLink> Fail { get; set; } = new();
    public List<Goal> Goals { get; set; } = new();
    public DisplayState State => Status switch
    {
        1 => DisplayState.Available, 2 => DisplayState.Active, 3 => DisplayState.Ready,
        4 => DisplayState.Completed, _ => DisplayState.Future
    };
}

public sealed class QuestLink
{
    // Targets within one condition are alternatives; separate conditions are AND.
    public List<string> Targets { get; set; } = new();
    public List<int> Statuses { get; set; } = new();
}

public sealed class Goal
{
    public string Id { get; set; } = "";
    public List<string> Targets { get; set; } = new();
    public GoalKind Kind { get; set; }
    public long Required { get; set; }
    public long Submitted { get; set; }
    public bool Fir { get; set; }
}

public sealed class AreaInfo
{
    public int Type { get; set; }
    public int Level { get; set; }
    public string NameKey { get; set; } = "";
    public string FallbackName { get; set; } = "";
    public DisplayState State { get; set; }
    public List<Goal> Goals { get; set; } = new();
}

public sealed class OwnedItem
{
    public string Id { get; set; } = "";
    public string Template { get; set; } = "";
    public long Count { get; set; } = 1;
}

public sealed class NeedOptions
{
    public bool Gunsmith { get; set; }
    public bool Plant { get; set; }
    public bool Beacon { get; set; }
    public bool AcceptedRepeatablesOnly { get; set; }
    public bool Includes(GoalKind kind) => kind switch
    {
        GoalKind.Gunsmith => Gunsmith, GoalKind.Plant => Plant,
        GoalKind.Beacon => Beacon, _ => true
    };
}

public sealed class DisplayOptions
{
    public bool Inventory { get; set; } = true;
    public bool Total { get; set; } = true;
    public bool QuestSummary { get; set; } = true;
    public bool AreaSummary { get; set; } = true;
    public bool QuestDetails { get; set; } = true;
    public bool AreaDetails { get; set; } = true;
    public bool Future { get; set; } = true;
    public bool Available { get; set; } = true;
    public bool Active { get; set; } = true;
    public bool Completed { get; set; } = true;
    public bool Shows(DisplayState state) => state switch
    {
        DisplayState.Future => Future, DisplayState.Available => Available,
        DisplayState.Completed => Completed, _ => Active
    };
}

public sealed class Detail
{
    public string NameKey { get; set; } = "";
    public string FallbackName { get; set; } = "";
    public DisplayState State { get; set; }
    public int Level { get; set; }
    public long Required { get; set; }
    public long Submitted { get; set; }
    public bool Fir { get; set; }
    public bool Shared { get; set; }
    public bool Branch { get; set; }
    public GoalKind Kind { get; set; }
}

public sealed class NeedResult
{
    public long QuestRequired { get; set; }
    public long QuestSubmitted { get; set; }
    public long AreaRequired { get; set; }
    public long AreaSubmitted { get; set; }
    public bool QuestFirRemaining { get; set; }
    public bool AreaFirRemaining { get; set; }
    public List<Detail> Quests { get; set; } = new();
    public List<Detail> Areas { get; set; } = new();

    public Marker GetMarker(bool itemFir, bool questEnabled, bool areaEnabled)
    {
        if (!itemFir) return Marker.None;
        bool q = questEnabled && QuestFirRemaining, h = areaEnabled && AreaFirRemaining;
        return q ? h ? Marker.Both : Marker.Quest : h ? Marker.Hideout : Marker.None;
    }
}

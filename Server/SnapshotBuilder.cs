using System.Globalization;
using System.Text.Json;
using MoeNeedMarks.Shared;

namespace MoeNeedMarks.Server;

/// <summary>Projection only: never writes to game tables or profile data.</summary>
public static class SnapshotBuilder
{
    public static Snapshot Build(JsonElement definitions, JsonElement areas, JsonElement pmc,
        Func<string, bool> allowed, long now, IReadOnlyDictionary<string, long>? cycleDurations = null)
    {
        var snapshot = new Snapshot { ProfileId = Text(pmc, "_id"), Timestamp = now };
        var states = Values(Get(pmc, "Quests")).Where(q => Text(q, "qid") != "").GroupBy(q => Text(q, "qid")).ToDictionary(g => g.Key, g => g.Last());
        var counters = Get(pmc, "TaskConditionCounters");
        var regular = Values(definitions).ToList();
        foreach (var raw in regular)
        {
            string id = Text(raw, "_id"); if (id == "") continue;
            states.TryGetValue(id, out var state);
            int status = Status(Get(state, "status"));
            if (status == 0 && IsAvailable(raw, pmc, states, now)) status = 1;
            var quest = ReadQuest(raw, state, counters, status);
            quest.Allowed = status == 4 || allowed(id);
            snapshot.Quests.Add(quest);
        }

        foreach (var cycle in Values(Get(pmc, "RepeatableQuests")))
        {
            string cycleName = Text(cycle, "name");
            if (cycleName.Contains("Savage", StringComparison.OrdinalIgnoreCase)) continue;
            long end = Number(cycle, "endTime"); if (end <= now) continue;
            long duration = cycleDurations != null && cycleDurations.TryGetValue(cycleName, out var configured)
                ? configured : cycleName.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ? 604800 : 86400;
            var activeIds = Values(Get(cycle, "activeQuests")).Select(q => Text(q, "_id")).ToHashSet();
            foreach (var raw in Values(Get(cycle, "activeQuests")).Concat(Values(Get(cycle, "inactiveQuests"))).GroupBy(q => Text(q, "_id")).Select(g => g.First()))
            {
                string id = Text(raw, "_id"); if (id == "" || Text(raw, "side").Equals("Savage", StringComparison.OrdinalIgnoreCase)) continue;
                states.TryGetValue(id, out var state);
                int status = Status(Get(state, "status"));
                if (!activeIds.Contains(id))
                {
                    // Inactive lists also contain the PREVIOUS cycle. Only retain
                    // explicitly completed tasks whose completion belongs to this cycle.
                    if (status != 4 || Number(Get(state, "statusTimers"), "4") < end - duration) continue;
                }
                else if (state.ValueKind == JsonValueKind.Undefined)
                    status = IsAvailable(raw, pmc, states, now) ? 1 : 0;
                if (status is not (1 or 2 or 3 or 4)) continue;
                var quest = ReadQuest(raw, state, counters, status);
                quest.Repeatable = true; quest.CycleEnd = end;
                // RepeatableQuestTemplate overrides NameLocaleKey; its raw name is a dialogue key.
                quest.NameKey = "DailyQuestName/" + Text(raw, "type");
                quest.FallbackName = "周期任务";
                snapshot.Quests.Add(quest);
            }
        }

        var profileAreas = Values(Get(Get(pmc, "Hideout"), "Areas")).GroupBy(a => Number(a, "type")).ToDictionary(g => g.Key, g => g.Last());
        foreach (var area in Values(areas).GroupBy(a => Number(a, "type")).Select(g => g.Last()))
        {
            int type = (int)Number(area, "type");
            profileAreas.TryGetValue(type, out var progress);
            int level = (int)Number(progress, "level"); bool constructing = Flag(progress, "constructing");
            foreach (var stage in Properties(Get(area, "stages")))
            {
                if (!int.TryParse(stage.Name, out int stageLevel) || stageLevel <= 0) continue;
                bool paid = stageLevel <= level || constructing && stageLevel == level + 1;
                var info = new AreaInfo
                {
                    Type = type, Level = stageLevel, NameKey = "needmarks:area:" + type, FallbackName = "设施 " + type,
                    State = stageLevel <= level ? DisplayState.Completed : constructing && stageLevel == level + 1
                        ? DisplayState.Building : stageLevel == level + 1 ? DisplayState.Available : DisplayState.Future
                };
                int index = 0;
                foreach (var req in Values(Get(stage.Value, "requirements")))
                {
                    if (Text(req, "type") != "Item") continue;
                    string target = Text(req, "templateId"); long count = Number(req, "count");
                    if (target == "" || count <= 0) continue;
                    info.Goals.Add(new Goal
                    {
                        Id = $"area:{type}:{stageLevel}:{index++}", Targets = new() { target }, Required = count,
                        Submitted = paid ? count : 0, Fir = Flag(req, "isSpawnedInSession")
                    });
                }
                snapshot.Areas.Add(info);
            }
        }
        snapshot.Stash = ReadStash(Get(pmc, "Inventory"));
        return snapshot;
    }

    private static QuestInfo ReadQuest(JsonElement raw, JsonElement state, JsonElement counters, int status)
    {
        string id = Text(raw, "_id"); var conditions = Get(raw, "conditions");
        var quest = new QuestInfo
        {
            Id = id, NameKey = Text(raw, "name"), FallbackName = Text(raw, "QuestName", Text(raw, "name", id)), Status = status,
            Start = Links(Get(conditions, "AvailableForStart")), Finish = Links(Get(conditions, "AvailableForFinish")), Fail = Links(Get(conditions, "Fail"))
        };
        var completed = Strings(Get(state, "completedConditions")).ToHashSet();
        foreach (var condition in Values(Get(conditions, "AvailableForFinish")))
        {
            GoalKind? kind = Text(condition, "conditionType") switch
            {
                "HandoverItem" => GoalKind.Handover, "WeaponAssembly" => GoalKind.Gunsmith,
                "LeaveItemAtLocation" => GoalKind.Plant, "PlaceBeacon" => GoalKind.Beacon, _ => null
            };
            if (kind == null) continue;
            string goalId = Text(condition, "id"); long count = Number(condition, "value");
            var counter = Get(counters, goalId);
            long submitted = status == 4 || completed.Contains(goalId) ? count
                : Text(counter, "sourceId") == id ? Number(counter, "value") : 0;
            quest.Goals.Add(new Goal
            {
                Id = goalId, Targets = Strings(Get(condition, "target")).Distinct().ToList(), Kind = kind.Value,
                Required = count, Submitted = Math.Clamp(submitted, 0, Math.Max(0, count)), Fir = Flag(condition, "onlyFoundInRaid")
            });
        }
        return quest;
    }

    private static List<QuestLink> Links(JsonElement conditions) => Values(conditions)
        .Where(c => Text(c, "conditionType") == "Quest")
        .Select(c => new QuestLink { Targets = Strings(Get(c, "target")).ToList(), Statuses = Values(Get(c, "status")).Select(Status).ToList() }).ToList();

    private static bool IsAvailable(JsonElement quest, JsonElement pmc, Dictionary<string, JsonElement> states, long now)
    {
        foreach (var c in Values(Get(Get(quest, "conditions"), "AvailableForStart")))
        {
            double required = Decimal(c, "value"); string compare = Text(c, "compareMethod", ">=");
            bool Compare(double actual) => compare switch
            { ">" => actual > required, "<" => actual < required, "<=" => actual <= required, "=" or "==" => actual == required, "!=" => actual != required, _ => actual >= required };
            switch (Text(c, "conditionType"))
            {
                case "Level": if (!Compare(Decimal(Get(pmc, "Info"), "Level"))) return false; break;
                case "Quest":
                    var targets = Strings(Get(c, "target")); var statuses = Values(Get(c, "status")).Select(Status).ToHashSet();
                    if (!targets.Any(t => states.TryGetValue(t, out var s) && statuses.Contains(Status(Get(s, "status"))) &&
                        Decimal(Get(s, "statusTimers"), Status(Get(s, "status")).ToString(CultureInfo.InvariantCulture)) + Decimal(c, "availableAfter") <= now)) return false;
                    break;
                case "TraderLoyalty":
                case "TraderStanding":
                    string trader = Strings(Get(c, "target")).FirstOrDefault() ?? "";
                    if (!Compare(Decimal(Get(Get(pmc, "TradersInfo"), trader), Text(c, "conditionType") == "TraderLoyalty" ? "loyaltyLevel" : "standing"))) return false;
                    break;
                default: return false; // Unknown start gates remain future, never falsely labelled available.
            }
        }
        return true;
    }

    public static List<OwnedItem> ReadStash(JsonElement inventory)
    {
        var items = Values(Get(inventory, "items")).Where(i => Text(i, "_id") != "").GroupBy(i => Text(i, "_id")).ToDictionary(g => g.Key, g => g.First());
        var roots = new HashSet<string> { Text(inventory, "stash"), Text(inventory, "sortingTable") };
        roots.Remove("");
        var result = new List<OwnedItem>();
        foreach (var pair in items)
        {
            var visited = new HashSet<string> { pair.Key }; string parent = Text(pair.Value, "parentId");
            while (parent != "" && !roots.Contains(parent) && visited.Add(parent) && items.TryGetValue(parent, out var ancestor)) parent = Text(ancestor, "parentId");
            if (!roots.Contains(parent) || roots.Contains(pair.Key)) continue;
            result.Add(new OwnedItem { Id = pair.Key, Template = Text(pair.Value, "_tpl"), Count = Number(Get(pair.Value, "upd"), "StackObjectsCount", 1) });
        }
        return result;
    }

    public static JsonElement Get(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    public static IEnumerable<JsonElement> Values(JsonElement element) => element.ValueKind switch
    { JsonValueKind.Array => element.EnumerateArray().ToArray(), JsonValueKind.Object => element.EnumerateObject().Select(p => p.Value), _ => Array.Empty<JsonElement>() };
    private static IEnumerable<JsonProperty> Properties(JsonElement element) => element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().ToArray() : Array.Empty<JsonProperty>();
    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind == JsonValueKind.String ? new[] { element.GetString()! } : Values(element).Select(e => e.ToString());
    public static string Text(JsonElement element, string key, string fallback = "") => Get(element, key) is var value && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ? value.ToString() : fallback;
    public static double Decimal(JsonElement element, string key, double fallback = 0) => double.TryParse(Text(element, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : fallback;
    public static long Number(JsonElement element, string key, long fallback = 0) => (long)Decimal(element, key, fallback);
    private static bool Flag(JsonElement element, string key) => Get(element, key).ValueKind == JsonValueKind.True;
    private static int Status(JsonElement value) => int.TryParse(value.ToString(), out int status) ? status : value.ToString() switch
    { "AvailableForStart" => 1, "Started" => 2, "AvailableForFinish" => 3, "Success" => 4, "Fail" => 5, "FailRestartable" => 6, "MarkedAsFailed" => 7, "Expired" => 8, "AvailableAfter" => 9, _ => 0 };
}

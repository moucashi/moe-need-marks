namespace MoeNeedMarks.Shared;

public sealed class NeedCalculator
{
    private readonly Snapshot snapshot;
    private readonly NeedOptions options;
    private readonly QuestPaths graph;
    private readonly Dictionary<string, List<(QuestInfo Quest, Goal Goal)>> questsByItem = new();
    private readonly Dictionary<string, List<(AreaInfo Area, Goal Goal)>> areasByItem = new();
    private readonly Dictionary<string, NeedResult> cache = new();

    public NeedCalculator(Snapshot snapshot, NeedOptions options, long now)
    {
        this.snapshot = snapshot; this.options = options;
        graph = new QuestPaths(snapshot.Quests);
        foreach (var quest in snapshot.Quests)
        {
            if (quest.Repeatable && (quest.CycleEnd <= now || quest.Status is not (1 or 2 or 3 or 4) ||
                options.AcceptedRepeatablesOnly && quest.Status == 1)) continue;
            if (graph.For(quest.Id).Count == 0) continue;
            foreach (var goal in quest.Goals.GroupBy(g => g.Id).Select(g => g.First()).Where(g => g.Required > 0 && options.Includes(g.Kind)))
            foreach (string target in goal.Targets.Distinct())
            {
                if (!questsByItem.TryGetValue(target, out var list)) questsByItem[target] = list = new();
                list.Add((quest, goal));
            }
        }
        foreach (var area in snapshot.Areas.GroupBy(a => (a.Type, a.Level)).Select(g => g.Last()))
        foreach (var goal in area.Goals.Where(g => g.Required > 0))
        foreach (string target in goal.Targets.Distinct())
        {
            if (!areasByItem.TryGetValue(target, out var list)) areasByItem[target] = list = new();
            list.Add((area, goal));
        }
    }

    public NeedResult Get(string template)
    {
        if (cache.TryGetValue(template, out var cached)) return cached;
        var result = new NeedResult();
        if (questsByItem.TryGetValue(template, out var quests))
        {
            result.QuestRequired = graph.Maximum(quests.GroupBy(x => x.Quest.Id).Select(group => new QuestPaths.Weighted
            {
                Count = group.Sum(x => x.Goal.Required), Paths = graph.For(group.Key)
            }).ToList());
            foreach (var row in quests)
            {
                long submitted = Math.Min(row.Goal.Required, Math.Max(0, row.Quest.Status == 4 ? row.Goal.Required : row.Goal.Submitted));
                result.QuestSubmitted += submitted;
                result.QuestFirRemaining |= row.Goal.Fir && submitted < row.Goal.Required;
                result.Quests.Add(new Detail
                {
                    NameKey = row.Quest.NameKey, FallbackName = row.Quest.FallbackName, State = row.Quest.State,
                    Required = row.Goal.Required, Submitted = submitted, Fir = row.Goal.Fir, Kind = row.Goal.Kind,
                    Shared = row.Goal.Targets.Distinct().Count() > 1, Branch = graph.IsBranch(graph.For(row.Quest.Id))
                });
            }
        }
        if (areasByItem.TryGetValue(template, out var areas))
        foreach (var row in areas)
        {
            long submitted = Math.Min(row.Goal.Required, Math.Max(0, row.Goal.Submitted));
            result.AreaRequired += row.Goal.Required; result.AreaSubmitted += submitted;
            result.AreaFirRemaining |= row.Goal.Fir && submitted < row.Goal.Required;
            result.Areas.Add(new Detail
            {
                NameKey = row.Area.NameKey, FallbackName = row.Area.FallbackName, State = row.Area.State,
                Level = row.Area.Level, Required = row.Goal.Required, Submitted = submitted, Fir = row.Goal.Fir
            });
        }
        cache[template] = result; return result;
    }
}

public static class InventoryCounts
{
    public static (long Carried, long Stash) Count(string template, IEnumerable<OwnedItem> carried, IEnumerable<OwnedItem> stash)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long Sum(IEnumerable<OwnedItem> items)
        {
            long sum = 0;
            foreach (var item in items)
                if (!string.IsNullOrEmpty(item.Id) && seen.Add(item.Id) && item.Template == template)
                    sum += Math.Max(0, item.Count);
            return sum;
        }
        return (Sum(carried), Sum(stash));
    }
}

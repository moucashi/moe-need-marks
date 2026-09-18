namespace MoeNeedMarks.Shared;

/// <summary>
/// A path is a conjunction of successful/failed quest outcomes. Each quest can
/// have several paths (OR prerequisites); paths of different quests must coexist.
/// This deliberately does not collapse a connected conflict graph into a clique.
/// </summary>
public sealed class QuestPaths
{
    public sealed class Path
    {
        public HashSet<string> Success { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Failure { get; } = new(StringComparer.Ordinal);
        public Path Copy()
        {
            var copy = new Path(); copy.Success.UnionWith(Success); copy.Failure.UnionWith(Failure); return copy;
        }
    }

    private readonly Dictionary<string, QuestInfo> quests;
    private readonly Dictionary<string, HashSet<string>> conflicts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Path>> cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> visiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> successful;
    private int cycles;

    public QuestPaths(IEnumerable<QuestInfo> source)
    {
        quests = source.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        successful = new HashSet<string>(quests.Values.Where(q => q.Status == 4).Select(q => q.Id));
        foreach (var q in quests.Values)
        foreach (var failure in q.Fail.Where(f => f.Statuses.Contains(4)))
        foreach (var target in failure.Targets)
        {
            AddConflict(q.Id, target); AddConflict(target, q.Id);
        }
    }

    private void AddConflict(string a, string b)
    {
        if (!conflicts.TryGetValue(a, out var set)) conflicts[a] = set = new HashSet<string>();
        set.Add(b);
    }

    public bool IsBranch(IReadOnlyList<Path> paths) => paths.Any(p => p.Success.Any(conflicts.ContainsKey) || p.Failure.Count > 0);
    public List<Path> For(string id) => Build(id, 4);

    private List<Path> Build(string id, int outcome)
    {
        string key = id + ":" + outcome;
        if (cache.TryGetValue(key, out var memo)) return memo;
        if (!quests.TryGetValue(id, out var q)) return new List<Path>();
        if (!visiting.Add(key)) { cycles++; return new List<Path>(); }
        int before = cycles;
        List<Path> result;
        try { result = BuildCore(q, outcome); }
        finally { visiting.Remove(key); }
        // A recursion cutoff depends on the caller; don't memoize that partial result.
        if (cycles == before) cache[key] = result;
        return result;
    }

    private List<Path> BuildCore(QuestInfo q, int outcome)
    {
        bool failed = q.Status >= 5 && q.Status <= 8;
        if (outcome == 5)
        {
            if (failed) return Unit();
            if (q.Status == 4 || !q.Allowed) return new();
            var failures = new List<Path>();
            foreach (var link in q.Fail)
            foreach (string target in link.Targets)
            foreach (int state in link.Statuses)
            {
                if (state != 4) continue;
                foreach (var path in Build(target, 4))
                {
                    var next = path.Copy(); next.Failure.Add(q.Id);
                    if (Compatible(next, new Path())) AddMinimal(failures, next);
                }
            }
            return failures;
        }
        if (outcome == 2 && (q.Status == 2 || q.Status == 3 || q.Status == 4)) return Unit();
        if (failed || (!q.Allowed && q.Status != 4)) return new();
        if (q.Status != 4 && q.Fail.Any(f => f.Targets.Any(t => quests.TryGetValue(t, out var other) && f.Statuses.Contains(other.Status)))) return new();
        if (q.Status != 4 && conflicts.TryGetValue(q.Id, out var peers) && peers.Overlaps(successful)) return new();

        // Started quests have already passed the start gate. Re-evaluating it would
        // incorrectly hide quests unlocked by a predecessor's transient Started state.
        var paths = q.Status is 2 or 3 or 4 ? Unit() : Conditions(q.Start);
        if (outcome == 2) return paths;
        if (q.Status != 4) paths = Product(paths, Conditions(q.Finish));
        var results = new List<Path>();
        foreach (var path in paths)
        {
            var next = path.Copy(); next.Success.Add(q.Id);
            if (Compatible(next, new Path())) AddMinimal(results, next);
        }
        return results;
    }

    private List<Path> Conditions(IEnumerable<QuestLink> conditions)
    {
        var result = Unit();
        foreach (var condition in conditions)
        {
            var alternatives = new List<Path>();
            foreach (string target in condition.Targets)
            {
                if (!quests.TryGetValue(target, out var q)) continue;
                if (condition.Statuses.Contains(q.Status) && q.Status != 0)
                {
                    AddMinimal(alternatives, new Path()); continue;
                }
                foreach (int status in condition.Statuses)
                {
                    // A terminal quest cannot go backwards to a transient state.
                    if ((q.Status == 4 || q.Status is >= 5 and <= 8) && status is 1 or 2 or 3) continue;
                    int outcome = status is 4 ? 4 : status is >= 5 and <= 8 ? 5 : 2;
                    foreach (var path in Build(target, outcome)) AddMinimal(alternatives, path);
                }
            }
            result = Product(result, alternatives);
            if (result.Count == 0) break;
        }
        return result;
    }

    private static List<Path> Unit() => new() { new Path() };
    private List<Path> Product(List<Path> left, List<Path> right)
    {
        var result = new List<Path>();
        foreach (var a in left)
        foreach (var b in right)
        {
            if (!Compatible(a, b)) continue;
            var merged = a.Copy(); merged.Success.UnionWith(b.Success); merged.Failure.UnionWith(b.Failure);
            AddMinimal(result, merged);
        }
        return result;
    }

    private static void AddMinimal(List<Path> result, Path path)
    {
        if (result.Any(p => p.Success.IsSubsetOf(path.Success) && p.Failure.IsSubsetOf(path.Failure))) return;
        result.RemoveAll(p => path.Success.IsSubsetOf(p.Success) && path.Failure.IsSubsetOf(p.Failure));
        result.Add(path);
    }

    public bool Compatible(Path a, Path b)
    {
        if (a.Success.Overlaps(a.Failure) || b.Success.Overlaps(b.Failure) ||
            a.Success.Overlaps(b.Failure) || b.Success.Overlaps(a.Failure)) return false;
        foreach (string id in a.Success.Concat(b.Success))
            if (conflicts.TryGetValue(id, out var others) && (others.Overlaps(a.Success) || others.Overlaps(b.Success))) return false;
        return true;
    }

    public sealed class Weighted
    {
        public long Count { get; set; }
        public List<Path> Paths { get; set; } = new();
    }

    public long Maximum(IReadOnlyList<Weighted> nodes)
    {
        // Factor independent components before search: unrelated tasks add normally,
        // even when one item is requested by hundreds of tasks.
        var neighbours = Enumerable.Range(0, nodes.Count).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
            if (nodes[i].Paths.Any(a => nodes[j].Paths.Any(b => !Compatible(a, b))))
            { neighbours[i].Add(j); neighbours[j].Add(i); }
        var visited = new HashSet<int>();
        long total = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!visited.Add(i)) continue;
            var component = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(); component.Add(n);
                foreach (int next in neighbours[n]) if (visited.Add(next)) queue.Enqueue(next);
            }
            if (component.Count == 1) { total += nodes[i].Count; continue; }
            var sorted = component.OrderByDescending(n => neighbours[n].Count).ThenByDescending(n => nodes[n].Count).Select(n => nodes[n]).ToArray();
            var suffix = new long[sorted.Length + 1];
            for (int n = sorted.Length - 1; n >= 0; n--) suffix[n] = suffix[n + 1] + sorted[n].Count;
            long best = 0;
            void Search(int index, long count, Path context)
            {
                if (count + suffix[index] <= best) return;
                if (index == sorted.Length) { best = count; return; }
                foreach (var path in sorted[index].Paths)
                {
                    if (!Compatible(context, path)) continue;
                    var next = context.Copy(); next.Success.UnionWith(path.Success); next.Failure.UnionWith(path.Failure);
                    Search(index + 1, count + sorted[index].Count, next);
                }
                Search(index + 1, count, context);
            }
            Search(0, 0, new Path()); total += best;
        }
        return total;
    }
}

using MoeNeedMarks.Shared;
using Xunit;

namespace MoeNeedMarks.Tests;

public class CalculationTests
{
    internal static Goal Goal(string tpl = "a", long required = 1, long submitted = 0, bool fir = true, string id = "goal", GoalKind kind = GoalKind.Handover) =>
        new() { Id = id, Targets = new() { tpl }, Required = required, Submitted = submitted, Fir = fir, Kind = kind };
    internal static QuestInfo Quest(string id, params Goal[] goals) => new() { Id = id, NameKey = id, FallbackName = id, Goals = goals.ToList() };
    internal static QuestLink Link(string id, params int[] states) => new() { Targets = new() { id }, Statuses = states.ToList() };
    internal static void Conflict(QuestInfo a, QuestInfo b) { a.Fail.Add(Link(b.Id, 4)); b.Fail.Add(Link(a.Id, 4)); }
    internal static NeedResult Calculate(Snapshot s, string tpl = "a", NeedOptions? options = null, long now = 100) => new NeedCalculator(s, options ?? new(), now).Get(tpl);
    internal static List<string> Lines(NeedResult r, long b = 4, long s = 5) => TooltipFormatter.Lines(r, b, s, new());

    [Fact] public void TaskExampleUsesSubmittedPlusOwned()
    {
        var q = Quest("q", Goal(required: 15, submitted: 3)); q.Status = 2;
        var r = Calculate(new() { Quests = new() { q } });
        Assert.Contains("任务需要 (12/15)", Lines(r));
        Assert.True(r.QuestFirRemaining);
    }
    [Fact] public void HideoutExampleAndCurrentOwned()
    {
        var r = Calculate(new() { Areas = new() { new() { Type = 1, Level = 1, Goals = new() { Goal(required: 15, submitted: 3) } } } });
        Assert.Contains("藏身处需要 (12/15)", Lines(r));
        Assert.Contains("当前已有 (5+6) 11", Lines(r, 5, 6));
    }
    [Fact] public void CombinedExampleDoesNotDoubleCountInventory()
    {
        var r = Calculate(new()
        {
            Quests = new() { Quest("q", Goal(required: 11, submitted: 5)) },
            Areas = new() { new() { Type = 1, Level = 1, Goals = new() { Goal(required: 15, submitted: 6) } } }
        });
        Assert.Contains("总共需要 (18/26)", Lines(r, 3, 4));
    }
    [Fact] public void UserBranchExampleTakesPerItemUnionMaximum()
    {
        var a = Quest("A", Goal("a", 1), Goal("b", 2, id: "b"));
        var b = Quest("B", Goal("a", 2), Goal("c", 1, id: "c")); Conflict(a, b);
        var snapshot = new Snapshot { Quests = new() { a, b } };
        Assert.Equal(2, Calculate(snapshot, "a").QuestRequired);
        Assert.Equal(2, Calculate(snapshot, "b").QuestRequired);
        Assert.Equal(1, Calculate(snapshot, "c").QuestRequired);
        Assert.Equal(2, Calculate(snapshot, "a").Quests.Count);
    }
    [Fact] public void DisjointBranchItemsBothRemainVisible()
    {
        var a = Quest("A", Goal("a", 1)); var b = Quest("B", Goal("b", 2)); Conflict(a, b);
        var s = new Snapshot { Quests = new() { a, b } };
        Assert.Equal(1, Calculate(s, "a").QuestRequired); Assert.Equal(2, Calculate(s, "b").QuestRequired);
    }
    [Fact] public void StartedChoiceDoesNotLockAlternatives()
    {
        var a = Quest("A", Goal(required: 1)); a.Status = 2;
        var b = Quest("B", Goal(required: 2)); Conflict(a, b);
        Assert.Equal(2, Calculate(new() { Quests = new() { a, b } }).Quests.Count);
    }
    [Fact] public void SuccessfulChoiceRemovesOtherBranchAndItsDescendants()
    {
        var a = Quest("A", Goal(required: 1)); a.Status = 4;
        var b = Quest("B", Goal(required: 2)); Conflict(a, b);
        var child = Quest("B2", Goal(required: 10)); child.Start.Add(Link("B", 4));
        var r = Calculate(new() { Quests = new() { a, b, child } });
        Assert.Equal(1, r.QuestRequired); Assert.Equal(1, r.QuestSubmitted); Assert.Single(r.Quests); Assert.False(r.QuestFirRemaining);
    }
    [Fact] public void LinearBranchTotalsAndCommonSuccessorAreCountedOnce()
    {
        var a = Quest("A", Goal(required: 1)); var b = Quest("B", Goal(required: 4)); Conflict(a, b);
        var a2 = Quest("A2", Goal(required: 5)); a2.Start.Add(Link("A", 4));
        var common = Quest("common", Goal(required: 3)); common.Start.Add(new() { Targets = new() { "A", "B" }, Statuses = new() { 4 } });
        var independent = Quest("independent", Goal(required: 2));
        Assert.Equal(11, Calculate(new() { Quests = new() { a, b, a2, common, independent } }).QuestRequired);
    }
    [Fact] public void ConnectedNonCliqueDoesNotLoseCompatibleQuests()
    {
        var a = Quest("A", Goal(required: 4)); var b = Quest("B", Goal(required: 5)); var c = Quest("C", Goal(required: 4));
        Conflict(a, b); Conflict(b, c);
        Assert.Equal(8, Calculate(new() { Quests = new() { a, b, c } }).QuestRequired);
    }
    [Fact] public void NestedChoicesAndCommonFailedOrSuccessPrerequisite()
    {
        var a = Quest("A", Goal(required: 2)); var b = Quest("B", Goal(required: 4)); Conflict(a, b);
        var c = Quest("C", Goal(required: 10)); var d = Quest("D", Goal(required: 3)); Conflict(c, d);
        c.Start.Add(Link("A", 4)); d.Start.Add(Link("A", 4));
        var common = Quest("E", Goal(required: 5)); common.Start.Add(Link("A", 4, 5));
        Assert.Equal(17, Calculate(new() { Quests = new() { a, b, c, d, common } }).QuestRequired);
    }
    [Fact] public void ImpossibleCombinedPrerequisitesAreExcluded()
    {
        var a = Quest("A"); var b = Quest("B"); Conflict(a, b);
        var child = Quest("X", Goal(required: 99)); child.Start.Add(Link("A", 4)); child.Start.Add(Link("B", 4));
        Assert.Equal(0, Calculate(new() { Quests = new() { a, b, child } }).QuestRequired);
    }
    [Fact] public void FailureUnlockCanRemainReachable()
    {
        var a = Quest("A", Goal(required: 2)); var b = Quest("B", Goal(required: 3)); Conflict(a, b);
        var fallback = Quest("fallback", Goal(required: 8)); fallback.Start.Add(Link("A", 5));
        Assert.Equal(11, Calculate(new() { Quests = new() { a, b, fallback } }).QuestRequired);
    }
    [Fact] public void TransientStartedPrerequisiteDoesNotForceCompletionOfOtherBranch()
    {
        var a = Quest("A", Goal(required: 2)); var b = Quest("B", Goal(required: 3)); Conflict(a, b);
        b.Start.Add(Link("A", 2));
        Assert.Equal(3, Calculate(new() { Quests = new() { a, b } }).QuestRequired);
    }
    [Fact] public void AlternativesShareProgressButNotInventory()
    {
        var goal = Goal("PM", 2, 1); goal.Targets.Add("PMt");
        var r = Calculate(new() { Quests = new() { Quest("惩罚者 - 5", goal) } }, "PMt");
        Assert.Contains("任务需要 (2/2)", Lines(r, 0, 1));
        Assert.Contains(Lines(r, 0, 1), line => line.Contains("任选／共享进度"));
        Assert.Equal(Marker.Quest, r.GetMarker(true, true, true));
    }
    [Theory]
    [InlineData(false, true, true, Marker.None)]
    [InlineData(true, true, true, Marker.Both)]
    [InlineData(true, false, true, Marker.Hideout)]
    [InlineData(true, true, false, Marker.Quest)]
    [InlineData(true, false, false, Marker.None)]
    public void ColorsRespectFirAndIndividualSwitches(bool fir, bool q, bool a, Marker expected)
    {
        var r = new NeedResult { QuestFirRemaining = true, AreaFirRemaining = true };
        Assert.Equal(expected, r.GetMarker(fir, q, a));
    }
    [Fact] public void CompletedAndFullySubmittedTargetsDoNotMark()
    {
        var old = Quest("old", Goal(required: 5)); old.Status = 4;
        var current = Quest("current", Goal(required: 3, submitted: 3)); current.Status = 2;
        var nonFir = Quest("nonFir", Goal(required: 10, fir: false));
        var r = Calculate(new() { Quests = new() { old, current, nonFir } });
        Assert.Equal(18, r.QuestRequired); Assert.Equal(8, r.QuestSubmitted); Assert.False(r.QuestFirRemaining);
        Assert.Contains("任务需要 (108/18)", Lines(r, 100, 0));
    }
    [Fact] public void EnoughStockDoesNotSuppressMarkers()
    {
        var r = Calculate(new() { Quests = new() { Quest("q", Goal()) } });
        Assert.Contains("任务需要 (10/1)", Lines(r, 5, 5)); Assert.Equal(Marker.Quest, r.GetMarker(true, true, true));
    }
    [Fact] public void CompletedInactiveEventRetainedFutureInactiveExcluded()
    {
        var old = Quest("old", Goal()); old.Status = 4; old.Allowed = false;
        var future = Quest("future", Goal(required: 10)); future.Allowed = false;
        Assert.Equal(1, Calculate(new() { Quests = new() { old, future } }).QuestRequired);
    }
    [Theory] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void FailedTasksAreExcluded(int status)
    {
        var q = Quest("q", Goal()); q.Status = status;
        Assert.Equal(0, Calculate(new() { Quests = new() { q } }).QuestRequired);
    }
    [Fact] public void RepeatableOptionsAndCycleExpiry()
    {
        var available = Quest("available", Goal(required: 1)); available.Status = 1; available.Repeatable = true; available.CycleEnd = 200;
        var active = Quest("active", Goal(required: 2)); active.Status = 2; active.Repeatable = true; active.CycleEnd = 200;
        var completed = Quest("completed", Goal(required: 3)); completed.Status = 4; completed.Repeatable = true; completed.CycleEnd = 200;
        var unavailable = Quest("locked", Goal(required: 10)); unavailable.Repeatable = true; unavailable.CycleEnd = 200;
        var s = new Snapshot { Quests = new() { available, active, completed, unavailable } };
        Assert.Equal(6, Calculate(s).QuestRequired);
        Assert.Equal(5, Calculate(s, options: new() { AcceptedRepeatablesOnly = true }).QuestRequired);
        Assert.Equal(0, Calculate(s, now: 200).QuestRequired);
    }
    [Fact] public void OptionalGoalKindsAreIndependentAndDefaultOff()
    {
        var q = Quest("q", Goal(), Goal(required: 2, id: "gun", kind: GoalKind.Gunsmith), Goal(required: 3, id: "plant", kind: GoalKind.Plant), Goal(required: 4, id: "beacon", kind: GoalKind.Beacon));
        var s = new Snapshot { Quests = new() { q } };
        Assert.Equal(1, Calculate(s).QuestRequired);
        Assert.Equal(3, Calculate(s, options: new() { Gunsmith = true }).QuestRequired);
        Assert.Equal(4, Calculate(s, options: new() { Plant = true }).QuestRequired);
        Assert.Equal(10, Calculate(s, options: new() { Gunsmith = true, Plant = true, Beacon = true }).QuestRequired);
    }
    [Fact] public void HidingDetailsDoesNotChangeTotals()
    {
        var r = Calculate(new() { Quests = new() { Quest("q", Goal(required: 5)) } });
        var hidden = TooltipFormatter.Lines(r, 1, 0, new() { Future = false });
        Assert.Equal(3, hidden.Count(line => line.Length > 0)); Assert.Contains("任务需要 (1/5)", hidden);
        Assert.True(r.QuestFirRemaining);
    }
    [Fact] public void InventoryDedupeUsesIdsAndCountsStacks()
    {
        var carried = new[] { new OwnedItem { Id = "x", Template = "a", Count = 4 }, new OwnedItem { Id = "x", Template = "a", Count = 4 } };
        var stash = new[] { new OwnedItem { Id = "x", Template = "a", Count = 4 }, new OwnedItem { Id = "y", Template = "a", Count = 5 }, new OwnedItem { Id = "z", Template = "b", Count = 99 } };
        Assert.Equal((4L, 5L), InventoryCounts.Count("a", carried, stash));
    }
    [Fact] public void SessionSwitchRejectsLateResponseEvenWhenReturningToOldProfile()
    {
        var gate = new SessionGate(); gate.Switch("A"); int original = gate.Generation;
        gate.Switch("B"); Assert.False(gate.Accepts(original, "A"));
        gate.Switch("A"); Assert.False(gate.Accepts(original, "A"));
        Assert.True(gate.Accepts(gate.Generation, "A")); gate.Switch(""); Assert.False(gate.Accepts(gate.Generation, ""));
    }
    [Fact] public void LongDetailsAreNotTruncated()
    {
        var s = new Snapshot { Quests = Enumerable.Range(0, 300).Select(i => Quest("q" + i, Goal())).ToList() };
        Assert.Equal(303, Lines(Calculate(s)).Count(line => line.Length > 0));
    }
}

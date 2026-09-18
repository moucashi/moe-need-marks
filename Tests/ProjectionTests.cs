using System.Text.Json;
using MoeNeedMarks.Server;
using MoeNeedMarks.Shared;
using Xunit;

namespace MoeNeedMarks.Tests;

public class ProjectionTests
{
    [Theory]
    [InlineData("Completion")]
    [InlineData("PickUp")]
    public void RepeatableNamesUseGameTemplateOverrideInsteadOfDialogueKey(string type)
    {
        var raw = JsonSerializer.Serialize(new { RepeatableQuests = new[] { new { name = "Daily", endTime = 200,
            activeQuests = new[] { new { _id = "daily", name = "61604635c725987e815b1a46 name 58330581ace78e27b8b10cee", type } } } } });
        var quest = Build(raw, quests: "{}").Quests.Single();
        Assert.Equal("DailyQuestName/" + type, quest.NameKey);
        Assert.Equal("周期任务", quest.FallbackName);
        var regular = Build("{}").Quests.Single();
        Assert.Equal("q name", regular.NameKey);
        Assert.Equal("test", regular.FallbackName);
    }

    internal static JsonElement Json(string text) { using var d = JsonDocument.Parse(text); return d.RootElement.Clone(); }
    private const string QuestJson = """
      {"q":{"_id":"q","name":"q name","QuestName":"test","conditions":{"AvailableForStart":[],"AvailableForFinish":[
      {"id":"find","conditionType":"FindItem","target":["a"],"value":15,"onlyFoundInRaid":true},
      {"id":"give","conditionType":"HandoverItem","target":["a"],"value":15,"onlyFoundInRaid":true}]}}}
      """;
    private static Snapshot Build(string profile, string areas = "[]", string quests = QuestJson, long now = 100) =>
        SnapshotBuilder.Build(Json(quests), Json(areas), Json(profile), _ => true, now);

    [Fact] public void HandoverCountersDoNotCountFindTargetsOrOtherQuestCounters()
    {
        var s = Build("""{"_id":"p","Quests":[{"qid":"q","status":2}],"TaskConditionCounters":{"give":{"sourceId":"q","value":3},"find":{"sourceId":"q","value":15}}} """);
        var result = CalculationTests.Calculate(s);
        Assert.Equal(15, result.QuestRequired); Assert.Equal(3, result.QuestSubmitted);
        var wrong = Build("""{"Quests":[{"qid":"q","status":2}],"TaskConditionCounters":{"give":{"sourceId":"other","value":12}}} """);
        Assert.Equal(0, CalculationTests.Calculate(wrong).QuestSubmitted);
    }
    [Theory] [InlineData(4, false)] [InlineData(2, true)]
    public void HistoricalOrCompletedConditionFillsOnce(int status, bool conditionCompleted)
    {
        string profile = JsonSerializer.Serialize(new { Quests = new[] { new { qid = "q", status, completedConditions = conditionCompleted ? new[] { "give" } : Array.Empty<string>() } }, TaskConditionCounters = new { give = new { sourceId = "q", value = 3 } } });
        Assert.Equal(15, CalculationTests.Calculate(Build(profile)).QuestSubmitted);
    }
    [Fact] public void BuildingMaterialsArePaidBeforeLevelChanges()
    {
        const string areas = """
          [{"type":1,"stages":{"0":{"requirements":[]},"1":{"requirements":[{"type":"Item","templateId":"a","count":3,"isSpawnedInSession":true}]},
          "2":{"requirements":[{"type":"Item","templateId":"a","count":5,"isSpawnedInSession":true}]},
          "3":{"requirements":[{"type":"Item","templateId":"a","count":7,"isSpawnedInSession":true},{"type":"Area","count":1}]}}}]
          """;
        var before = Build("""{"Hideout":{"Areas":[{"type":1,"level":1,"constructing":false}]}}""", areas, "{}");
        var during = Build("""{"Hideout":{"Areas":[{"type":1,"level":1,"constructing":true}]}}""", areas, "{}");
        var after = Build("""{"Hideout":{"Areas":[{"type":1,"level":2,"constructing":false}]}}""", areas, "{}");
        Assert.Equal(3, CalculationTests.Calculate(before).AreaSubmitted);
        Assert.Equal(8, CalculationTests.Calculate(during).AreaSubmitted);
        Assert.Equal(8, CalculationTests.Calculate(after).AreaSubmitted);
        Assert.Equal(15, CalculationTests.Calculate(after).AreaRequired);
        Assert.True(CalculationTests.Calculate(after).AreaFirRemaining);
        Assert.Equal(DisplayState.Building, during.Areas.Single(a => a.Level == 2).State);
    }
    [Fact] public void StashTraversalIncludesNestedContainersAndSortingButNotEquipment()
    {
        var stash = SnapshotBuilder.ReadStash(Json("""
          {"stash":"s","sortingTable":"sort","equipment":"e","items":[
          {"_id":"s","_tpl":"root"},{"_id":"e","_tpl":"root"},
          {"_id":"case","_tpl":"case","parentId":"s"},
          {"_id":"one","_tpl":"a","parentId":"case","upd":{"StackObjectsCount":5}},
          {"_id":"two","_tpl":"a","parentId":"sort","upd":{"StackObjectsCount":2}},
          {"_id":"raid","_tpl":"a","parentId":"e","upd":{"StackObjectsCount":4}},
          {"_id":"cycle1","_tpl":"a","parentId":"cycle2"},{"_id":"cycle2","_tpl":"a","parentId":"cycle1"},
          {"_id":"mail","_tpl":"a","parentId":"mail-root"}]}
          """));
        Assert.Equal(7, stash.Where(i => i.Template == "a").Sum(i => i.Count)); Assert.DoesNotContain(stash, i => i.Id == "raid");
    }
    [Fact] public void CurrentCycleRetainsOnlyEligibleRepeatables()
    {
        var profile = Json("""
          {"_id":"p","Quests":[{"qid":"done","status":4,"statusTimers":{"4":150}},{"qid":"old","status":4,"statusTimers":{"4":80}},{"qid":"locked","status":0}],
          "RepeatableQuests":[{"name":"Daily","endTime":200,"activeQuests":[
          {"_id":"new","name":"new","conditions":{"AvailableForStart":[]}},{"_id":"locked","conditions":{}}],
          "inactiveQuests":[{"_id":"done","name":"done","conditions":{}},{"_id":"old","name":"old","conditions":{}},{"_id":"expired","conditions":{}}]}]}
          """);
        var result = SnapshotBuilder.Build(Json("{}"), Json("[]"), profile, _ => true, 170, new Dictionary<string, long> { ["Daily"] = 100 });
        Assert.Equal(new[] { "done", "new" }, result.Quests.Select(q => q.Id).OrderBy(x => x));
        var expired = SnapshotBuilder.Build(Json("{}"), Json("[]"), profile, _ => true, 200);
        Assert.Empty(expired.Quests);
    }
    [Fact] public void AvailabilityRespectsLevelQuestDelayAndTraderStanding()
    {
        const string definitions = """
          {"q":{"_id":"q","conditions":{"AvailableForStart":[{"conditionType":"Level","value":10},{"conditionType":"Quest","target":"pre","status":[4],"availableAfter":20},{"conditionType":"TraderStanding","target":"trader","value":0.25}]}}}
          """;
        const string p = """{"Info":{"Level":10},"Quests":[{"qid":"pre","status":4,"statusTimers":{"4":90}}],"TradersInfo":{"trader":{"standing":0.3}}} """;
        Assert.Equal(0, Build(p, quests: definitions, now: 100).Quests.Single().Status);
        Assert.Equal(1, Build(p, quests: definitions, now: 110).Quests.Single().Status);
    }
}

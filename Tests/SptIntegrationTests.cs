using System.Diagnostics;
using System.Text.Json;
using MoeNeedMarks.Server;
using MoeNeedMarks.Shared;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace MoeNeedMarks.Tests;

public class SptIntegrationTests(ITestOutputHelper output)
{
    private static string Spt => Environment.GetEnvironmentVariable("NEEDMARKS_SPT_PATH") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../客户端"));
    private static JsonElement Read(string relative) => ProjectionTests.Json(File.ReadAllText(Path.Combine(Spt, relative)));

    [Fact, Trait("Category", "SPT415")]
    public void RepeatableCompletionNameResolvesInInstalledChineseLocale()
    {
        var template = Read("SPT_Runtime/SPT_Data/database/templates/repeatableQuests.json").GetProperty("templates").GetProperty("Completion");
        var profile = ProjectionTests.Json("{\"RepeatableQuests\":[{\"name\":\"Daily\",\"endTime\":200,\"activeQuests\":[" + template.GetRawText() + "]}]}");
        var snapshot = SnapshotBuilder.Build(ProjectionTests.Json("{}"), ProjectionTests.Json("[]"), profile, _ => true, 100);
        var quest = Assert.Single(snapshot.Quests);
        var locale = Read("SPT_Runtime/SPT_Data/database/locales/global/ch.json");
        Assert.Equal("寻物上交", locale.GetProperty(quest.NameKey).GetString());
    }

    [Fact, Trait("Category", "SPT415")]
    public void RuntimeDatabaseAndExistingProfilesProduceFiniteConsistentResults()
    {
        var definitions = Read("SPT_Runtime/SPT_Data/database/templates/quests.json");
        var areas = Read("SPT_Runtime/SPT_Data/database/hideout/areas.json");
        var stopwatch = Stopwatch.StartNew(); int profiles = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Spt, "SPT_Runtime/user/profiles"), "*.json"))
        {
            var raw = ProjectionTests.Json(File.ReadAllText(file));
            var pmc = SnapshotBuilder.Get(SnapshotBuilder.Get(raw, "characters"), "pmc");
            if (pmc.ValueKind != JsonValueKind.Object) continue;
            profiles++;
            string original = pmc.GetRawText();
            var snapshot = SnapshotBuilder.Build(definitions, areas, pmc, _ => true, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var calculator = new NeedCalculator(snapshot, new() { Gunsmith = true, Plant = true, Beacon = true }, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var templates = snapshot.Quests.SelectMany(q => q.Goals).Concat(snapshot.Areas.SelectMany(a => a.Goals)).SelectMany(g => g.Targets).Distinct().ToArray();
            foreach (string template in templates)
            {
                var result = calculator.Get(template);
                Assert.True(result.QuestRequired >= 0 && result.QuestSubmitted >= 0 && result.AreaRequired >= result.AreaSubmitted);
                Assert.DoesNotContain(result.Quests, d => d.Required <= 0 || d.Submitted > d.Required);
            }
            Assert.Equal(original, pmc.GetRawText());
            output.WriteLine($"profile {profiles}: {snapshot.Quests.Count} quests, {snapshot.Areas.Count} area stages, {templates.Length} item templates, {snapshot.Stash.Count} stash objects");
        }
        Assert.True(profiles > 0, "SPT415 集成测试需要本地 SPT 数据和至少一个现有存档；可用 NEEDMARKS_SPT_PATH 指定客户端目录。");
        output.WriteLine($"All profiles and item templates calculated in {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "分支计算异常缓慢");
    }

    [Fact, Trait("Category", "SPT415")]
    public void ActualEncryptedTapeNeedsTwoForIntelligenceThreeButDoesNotRequireFir()
    {
        const string template = "61bf7c024770ee6f9c6b8b53";
        var snapshot = SnapshotBuilder.Build(ProjectionTests.Json("[]"), Read("SPT_Runtime/SPT_Data/database/hideout/areas.json"),
            ProjectionTests.Json("{}"), _ => true, 100);
        var need = CalculationTests.Calculate(snapshot, template);
        var detail = Assert.Single(need.Areas);
        Assert.Equal("needmarks:area:11", detail.NameKey);
        Assert.Equal(3, detail.Level); Assert.Equal(2, detail.Required); Assert.Equal(0, detail.Submitted);
        Assert.False(detail.Fir); Assert.False(need.AreaFirRemaining);
        Assert.Equal(Marker.None, need.GetMarker(true, true, true));
        Assert.Contains("藏身处需要 (2/2)", CalculationTests.Lines(need, 0, 2));
        // If an effective recipe does require FIR, full stock must not suppress blue.
        var goal = snapshot.Areas.Single(a => a.Type == 11 && a.Level == 3).Goals.Single(g => g.Targets.Contains(template));
        goal.Fir = true;
        Assert.Equal(Marker.Hideout, CalculationTests.Calculate(snapshot, template).GetMarker(true, true, true));
    }

    [Fact, Trait("Category", "SPT415")]
    public void ActualPunisherFiveSupportsSharedPistolProgress()
    {
        var definitions = Read("SPT_Runtime/SPT_Data/database/templates/quests.json");
        var q = SnapshotBuilder.Values(definitions).Single(q => SnapshotBuilder.Text(q, "QuestName") == "The Punisher - Part 5");
        string id = SnapshotBuilder.Text(q, "_id");
        var c = SnapshotBuilder.Values(SnapshotBuilder.Get(SnapshotBuilder.Get(q, "conditions"), "AvailableForFinish"))
            .Single(c => SnapshotBuilder.Text(c, "conditionType") == "HandoverItem" && SnapshotBuilder.Number(c, "value") == 2);
        string conditionId = SnapshotBuilder.Text(c, "id");
        var targets = SnapshotBuilder.Values(SnapshotBuilder.Get(c, "target")).Select(x => x.ToString()).ToArray();
        Assert.Equal(2, targets.Length);
        var profile = JsonSerializer.SerializeToElement(new
        {
            _id = "test", Quests = new[] { new { qid = id, status = 2 } },
            TaskConditionCounters = new Dictionary<string, object> { [conditionId] = new { sourceId = id, value = 1 } }
        });
        var s = SnapshotBuilder.Build(JsonSerializer.SerializeToElement(new[] { q }), ProjectionTests.Json("[]"), profile, _ => true, 100);
        var r = CalculationTests.Calculate(s, targets[1]);
        Assert.Contains("任务需要 (2/2)", CalculationTests.Lines(r, 0, 1)); Assert.True(r.Quests.Single().Shared);
    }

    [Fact, Trait("Category", "SPT415")]
    public void AllHarmonyTargetsAndAccessedUiFieldsExistInInstalled415()
    {
        using var asm = AssemblyDefinition.ReadAssembly(Path.Combine(Spt, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
        var types = asm.MainModule.Types.ToDictionary(t => t.FullName);
        void Method(string type, string name, int args)
        {
            Assert.True(types.ContainsKey(type), type);
            Assert.Contains(types[type].Methods, m => m.Name == name && m.Parameters.Count == args);
        }
        Method("EFT.UI.DragAndDrop.QuestItemViewPanel", "Show", 3);
        Method("EFT.UI.DragAndDrop.QuestItemViewPanel", "CG_Awake", 1);
        Method("EFT.UI.DragAndDrop.GridItemView", "ShowTooltip", 0);
        Method("EFT.UI.DragAndDrop.TradingItemView", "ShowTooltip", 0);
        Method("EFT.UI.PriceTooltip", "Show", 4);
        Assert.Equal("EFT.UI.SimpleTooltip", types["EFT.UI.PriceTooltip"].BaseType.FullName);
        var tradingHover = types["EFT.UI.DragAndDrop.TradingItemView"].Methods.Single(m => m.Name == "ShowTooltip");
        Assert.Contains(tradingHover.Body.Instructions, i => i.Operand is MethodReference m && m.DeclaringType.FullName == "EFT.UI.PriceTooltip" && m.Name == "Show");
        var priceShow = types["EFT.UI.PriceTooltip"].Methods.Single(m => m.Name == "Show");
        Assert.Contains(priceShow.Body.Instructions, i => i.Operand is MethodReference m && m.DeclaringType.FullName == "EFT.UI.SimpleTooltip" && m.Name == "Show");
        Method("EFT.UI.ItemTooltip", "Show", 7);
        Method("EFT.UI.SimpleTooltip", "Show", 4);
        Method("EFT.UI.SimpleTooltip", "SetText", 1);
        Method("EFT.UI.Tooltip", "Close", 0);
        Method("EFT.UI.Tooltip", "SetPosition", 1);
        Assert.Contains(types["EFT.UI.SimpleTooltip"].Fields, f => f.Name == "_label" && f.IsPublic && f.FieldType.FullName == "TMPro.TextMeshProUGUI");
        foreach (string field in new[] { "_mainTransform", "_boundsTransform" })
            Assert.Contains(types["EFT.UI.Tooltip"].Fields, f => f.Name == field && f.IsPublic);
        Method("EFT.Quests.TaskConditionCounter", "set_Value", 1);
        Method("EFT.Quests.Quest", "SetStatus", 3);
        Method("EFT.Hideout.AreaData", "set_Status", 1);
        Method("EFT.Hideout.AreaData", "set_CurrentLevel", 1);
        Method("EFT.InventoryLogic.InventoryController", "ReportProfileUpdate", 0);
        Method("EFT.InventoryLogic.Inventory", "UpdateTotalWeight", 1);
        Assert.Contains(types["EFT.UI.ItemUiContext"].Fields, f => f.Name == "_profile" && f.FieldType.FullName == "EFT.Profile");
        foreach (string field in new[] { "_questIconImage", "_foundInRaidSprite" })
            Assert.Contains(types["EFT.UI.DragAndDrop.QuestItemViewPanel"].Fields, f => f.Name == field && f.IsPublic);
        var pluginFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Client/bin/Release/net472/MoeNeedMarks.Client.dll"));
        using var plugin = AssemblyDefinition.ReadAssembly(pluginFile);
        Assert.Equal(ModInfo.Version + ".0", plugin.Name.Version.ToString());
        var metadata = plugin.MainModule.Types.Single(t => t.FullName == "MoeNeedMarks.Client.Plugin").CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        Assert.Equal(ModInfo.Version, metadata.ConstructorArguments[2].Value);
        Assert.DoesNotContain(plugin.MainModule.AssemblyReferences, r => r.Name == "MoeNeedMarks.Shared");
    }

    [Fact, Trait("Category", "SPT415")]
    public void SptRuntimeSerializationPreservesObjectivesCountersAndEnumTimestamps()
    {
        var json = new SPTarkov.Server.Core.Utils.JsonUtil(new[] { new SPTarkov.Server.Core.Utils.Json.SptJsonConverterRegistrator() });
        var rawQuests = Read("SPT_Runtime/SPT_Data/database/templates/quests.json");
        var typedQuests = json.Deserialize<Dictionary<SPTarkov.Server.Core.Models.Common.MongoId, SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>>(rawQuests.GetRawText())!;
        string profileFile = Directory.EnumerateFiles(Path.Combine(Spt, "SPT_Runtime/user/profiles"), "*.json").First();
        var rawProfile = SnapshotBuilder.Get(SnapshotBuilder.Get(ProjectionTests.Json(File.ReadAllText(profileFile)), "characters"), "pmc");
        var typedProfile = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.PmcData>(rawProfile.GetRawText())!;
        var before = SnapshotBuilder.Build(rawQuests, ProjectionTests.Json("[]"), rawProfile, _ => true, 100);
        var after = SnapshotBuilder.Build(ProjectionTests.Json(json.Serialize(typedQuests)!), ProjectionTests.Json("[]"), ProjectionTests.Json(json.Serialize(typedProfile)!), _ => true, 100);
        Assert.Equal(before.ProfileId, after.ProfileId);
        Assert.Equal(before.Stash.Sum(i => i.Count), after.Stash.Sum(i => i.Count));
        foreach (var original in before.Quests.Where(q => !q.Repeatable))
        {
            var projected = after.Quests.Single(q => q.Id == original.Id);
            Assert.Equal(original.Status, projected.Status);
            Assert.Equal(original.Goals.Select(g => (g.Id, g.Required, g.Submitted, g.Fir, string.Join(",", g.Targets))),
                projected.Goals.Select(g => (g.Id, g.Required, g.Submitted, g.Fir, string.Join(",", g.Targets))));
        }
    }
}

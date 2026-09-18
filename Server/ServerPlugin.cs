using System.Text.Json;
using MoeNeedMarks.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace MoeNeedMarks.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = ModInfo.Guid + ".server";
    public string Name { get; init; } = ModInfo.Name;
    public string Author { get; init; } = "moe";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(ModInfo.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.5");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(InjectionType.Singleton)]
public sealed class SnapshotService(TemplateTable templates, HideoutTable hideout, ProfileHelper profiles,
    QuestHelper quests, QuestConfig repeatables, JsonUtil json)
{
    private readonly object gate = new();
    private long catalogTime;
    private JsonElement catalogQuests;
    private JsonElement catalogAreas;

    public Snapshot Get(MongoId session)
    {
        lock (gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Lazily read final runtime tables after all mods have loaded. Refresh
            // periodically so runtime recipe/quest editors are picked up as well.
            if (catalogQuests.ValueKind == JsonValueKind.Undefined || now - catalogTime >= 30)
            {
                catalogQuests = Parse(json.Serialize(templates.Quests)!);
                catalogAreas = Parse(json.Serialize(hideout.Areas.Concat(hideout.CustomAreas ?? []))!);
                catalogTime = now;
            }
            var pmc = profiles.GetPmcProfile(session) ?? throw new InvalidOperationException("角色尚未加载");
            var profile = Parse(json.Serialize(new
            {
                _id = pmc.Id, pmc.Info, pmc.Inventory, pmc.Quests, pmc.TaskConditionCounters,
                pmc.Hideout, pmc.RepeatableQuests, pmc.TradersInfo
            })!);
            var allowed = templates.Quests.Keys.Where(id =>
                !quests.QuestIsForOtherSide(pmc.Info?.Side, id) &&
                !quests.QuestIsProfileBlacklisted(pmc.Info?.GameVersion, id) &&
                quests.QuestIsProfileWhitelisted(pmc.Info?.GameVersion, id) && quests.ShowEventQuestToPlayer(id)).Select(id => id.ToString()).ToHashSet();
            var config = Parse(json.Serialize(repeatables)!);
            var durations = SnapshotBuilder.Values(SnapshotBuilder.Get(config, "repeatableQuests"))
                .Where(c => SnapshotBuilder.Text(c, "name") != "")
                .ToDictionary(c => SnapshotBuilder.Text(c, "name"), c => SnapshotBuilder.Number(c, "resetTime", 86400));
            return SnapshotBuilder.Build(catalogQuests, catalogAreas, profile, allowed.Contains, now, durations);
        }
    }

    private static JsonElement Parse(string text)
    {
        using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
    }
}

[Injectable(InjectionType.Singleton)]
public sealed class NeedMarksRouter : StaticRouter
{
    public NeedMarksRouter(JsonUtil json, SnapshotService snapshots) : base(json,
        [new RouteAction<EmptyRequestData>(ModInfo.Route, (url, info, session, output, cancellation) =>
            new ValueTask<string>(JsonSerializer.Serialize(snapshots.Get(session))))]) { }
}

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 1)]
public sealed class ServerPlugin(ISptLogger<ServerPlugin> logger, NeedMarksRouter router) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = router;
        logger.Success($"{ModInfo.Name} {ModInfo.Version}：需求数据接口已加载");
        return Task.CompletedTask;
    }
}

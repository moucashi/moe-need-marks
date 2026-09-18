using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using MoeNeedMarks.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace MoeNeedMarks.Client;

internal static class RuntimeData
{
    private static readonly FieldInfo UiProfile = AccessTools.Field(typeof(ItemUiContext), "_profile");
    private static readonly SessionGate Gate = new();
    private static Task<Snapshot>? request;
    private static int requestGeneration;
    private static string requestKey = "";
    private static Snapshot? snapshot;
    private static readonly RefreshCache<NeedCalculator> calculator = new();
    private static Profile? profile;
    private static float nextRequest, nextUiRefresh, demandedUntil, lastError;
    private static long nextExpiry;
    private static bool stopped;
    public static int Revision { get; private set; }
    public static Profile? Profile => profile;
    // A HideoutGameWorld also has a MainPlayer; only an actual raid uses a stash snapshot.
    public static bool InRaid => Singleton<AbstractGame>.Instance?.InRaid ?? false;
    public static bool Ready => calculator.Value != null;

    public static void Demand() => demandedUntil = Time.unscaledTime + 3f;
    public static void Invalidate(bool definitions = false)
    {
        calculator.Invalidate();
        if (definitions) nextRequest = 0;
    }

    public static void Tick()
    {
        if (stopped) return;
        var worldPlayer = InRaid ? Singleton<GameWorld>.Instance?.MainPlayer : null;
        var current = worldPlayer != null ? worldPlayer.Profile : ItemUiContext.Instance != null ? UiProfile?.GetValue(ItemUiContext.Instance) as Profile : null;
        string key = current == null ? "" : RequestHandler.SessionId + ":" + (current.Side == EPlayerSide.Savage ? current.AccountId : current.Id);
        if (Gate.Switch(key))
        {
            snapshot = null; calculator.Clear(); nextRequest = 0; Revision++;
            MarkerPatches.RestoreAll(); HoverPanel.Clear();
        }
        if (!ReferenceEquals(profile, current)) { profile = current; calculator.Invalidate(); }
        float now = Time.unscaledTime;
        if (request != null && request.IsCompleted)
        {
            var completed = request; request = null;
            if (Gate.Accepts(requestGeneration, requestKey))
            {
                if (completed.Status == TaskStatus.RanToCompletion && completed.Result.Schema == 1 &&
                    (profile?.Side == EPlayerSide.Savage || completed.Result.ProfileId == profile?.Id))
                {
                    snapshot = completed.Result; calculator.Invalidate();
                    nextExpiry = snapshot.Quests.Where(q => q.Repeatable).Select(q => q.CycleEnd).DefaultIfEmpty(long.MaxValue).Min();
                }
                else if (now - lastError > 15 || lastError == 0)
                {
                    lastError = now;
                    Plugin.Log.LogWarning("需求快照读取失败，保留当前角色最近有效数据：" + (completed.Exception?.GetBaseException().Message ?? "接口版本或角色不匹配"));
                }
            }
            // Observe failures even when their original session was discarded.
            _ = completed.Exception;
        }
        long utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nextExpiry > 0 && utc >= nextExpiry) { calculator.Invalidate(); nextExpiry = long.MaxValue; nextRequest = 0; }
        if (profile != null && request == null && now >= nextRequest && (now <= demandedUntil || MarkerPatches.HasVisible))
        {
            nextRequest = now + 2f; requestGeneration = Gate.Generation; requestKey = Gate.Key;
            request = Task.Run(async () => JsonConvert.DeserializeObject<Snapshot>(await RequestHandler.GetJsonAsync(ModInfo.Route)) ?? throw new InvalidOperationException("空快照"));
        }
        if (profile == null || now < nextUiRefresh) return;
        nextUiRefresh = now + 0.15f;
        if (snapshot != null && calculator.NeedsRefresh)
        {
            Rebuild(); Revision++;
            MarkerPatches.Refresh();
        }
    }

    private static void Rebuild()
    {
        if (snapshot == null || profile == null) return;
        // Only the local PMC can override PMC objective progress. A scav contributes
        // carried items but must never replace the PMC's quest state.
        if (profile.Id == snapshot.ProfileId)
        {
            var live = profile.QuestsData?.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.Last());
            foreach (var quest in snapshot.Quests)
            {
                if (live == null || !live.TryGetValue(quest.Id, out var data)) continue;
                if ((int)data.Status != 0) quest.Status = (int)data.Status;
                foreach (var goal in quest.Goals)
                {
                    if (quest.Status == 4 || data.CompletedConditions?.Any(id => id.ToString() == goal.Id) == true)
                        goal.Submitted = goal.Required;
                    else if (profile.TaskConditionCounters != null && profile.TaskConditionCounters.TryGetValue(goal.Id, out var counter) && counter.SourceId == quest.Id)
                        goal.Submitted = Math.Min(goal.Required, Math.Max(0, counter.Value));
                }
            }
        }
        calculator.Publish(new NeedCalculator(snapshot, Settings.Need, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public static NeedResult? Get(string template) { Demand(); return calculator.Value?.Get(template); }
    public static (long Carried, long Stash) Count(string template)
    {
        if (profile?.Inventory == null) return (0, 0);
        var carried = Items(profile.Inventory.GetPlayerItems(EPlayerItems.Equipment));
        // Only owned stash + sorting table. Hideout production slots are not inventory.
        var stash = InRaid ? snapshot?.Stash ?? Enumerable.Empty<OwnedItem>()
            : Items(profile.Inventory.GetPlayerItems(EPlayerItems.Stash | EPlayerItems.SortingTable));
        return InventoryCounts.Count(template, carried, stash);
    }

    private static IEnumerable<OwnedItem> Items(IEnumerable<Item> items) => items
        .Where(i => i != null && i is not Stash && i is not InventoryEquipment && i is not SortingTable)
        .Select(i => new OwnedItem { Id = i.Id.ToString(), Template = i.TemplateId, Count = i.StackObjectsCount });

    public static string Localize(string key, string fallback)
    {
        if (key.StartsWith("needmarks:area:", StringComparison.Ordinal) && int.TryParse(key.Substring(15), out int area))
            return ((EAreaType)area).LocalizeAreaName();
        string value = key.Localized();
        return string.IsNullOrEmpty(value) || value == key ? UiText.Get(fallback, Settings.Language) : value;
    }

    public static void Stop()
    {
        stopped = true; Gate.Switch(""); snapshot = null; calculator.Clear(); profile = null;
        if (request != null) _ = request.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        request = null;
    }
}

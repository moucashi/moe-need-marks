using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Insurance;
using EFT.UI.Ragfair;
using HarmonyLib;
using MoeNeedMarks.Shared;
using UnityEngine;

namespace MoeNeedMarks.Client;

[HarmonyPatch(typeof(QuestItemViewPanel), nameof(QuestItemViewPanel.Show))]
internal static class MarkerPatches
{
    private sealed class Original
    {
        public Item Item = null!;
        public Sprite Sprite = null!;
        public Color Color;
        public bool Active, Applied;
    }
    private static readonly ConditionalWeakTable<QuestItemViewPanel, Original> States = new();
    private static readonly List<WeakReference<QuestItemViewPanel>> Panels = new();
    public static bool HasVisible => Panels.Any(w => w.TryGetTarget(out var p) && p != null && p.transform.parent != null && p.transform.parent.gameObject.activeInHierarchy);

    private static void Prefix(QuestItemViewPanel __instance)
    {
        if (States.TryGetValue(__instance, out var state)) Restore(__instance, state);
    }
    private static void Postfix(QuestItemViewPanel __instance, Item item)
    {
        if (__instance == null || __instance._questIconImage == null || item == null) return;
        if (!States.TryGetValue(__instance, out var state))
        {
            state = new Original(); States.Add(__instance, state); Panels.Add(new WeakReference<QuestItemViewPanel>(__instance));
        }
        state.Item = item; state.Sprite = __instance._questIconImage.sprite; state.Color = __instance._questIconImage.color;
        state.Active = __instance.gameObject.activeSelf; state.Applied = false;
        Apply(__instance, state);
    }

    private static void Apply(QuestItemViewPanel panel, Original state)
    {
        if (panel._questIconImage == null) return;
        var need = RuntimeData.Get(state.Item.TemplateId);
        if (need == null) { Restore(panel, state); return; }
        var marker = need.GetMarker(state.Item.MarkedAsSpawnedInSession, Settings.QuestMark.Value, Settings.AreaMark.Value);
        if (marker == Marker.None)
        {
            Restore(panel, state);
            // Native yellow quest icons can also mean non-FIR demands. Ordinary FIR
            // items must return to a neutral FIR mark when our demand is fulfilled.
            if (state.Item.MarkedAsSpawnedInSession && !state.Item.QuestItem)
            {
                panel.gameObject.SetActive(true); panel._questIconImage.sprite = panel._foundInRaidSprite;
                panel._questIconImage.color = Color.white; state.Applied = true;
            }
            return;
        }
        panel.gameObject.SetActive(true); panel._questIconImage.sprite = panel._foundInRaidSprite;
        panel._questIconImage.color = Settings.ColorFor(marker); state.Applied = true;
    }
    private static void Restore(QuestItemViewPanel panel, Original state)
    {
        if (!state.Applied || panel == null || panel._questIconImage == null) return;
        panel._questIconImage.sprite = state.Sprite; panel._questIconImage.color = state.Color;
        panel.gameObject.SetActive(state.Active); state.Applied = false;
    }
    public static Item? ItemFor(QuestItemViewPanel panel) => States.TryGetValue(panel, out var state) ? state.Item : null;
    public static void Refresh()
    {
        Panels.RemoveAll(w => !w.TryGetTarget(out var p) || p == null);
        foreach (var weak in Panels)
            if (weak.TryGetTarget(out var p) && p != null && p.transform.parent != null && p.transform.parent.gameObject.activeInHierarchy && States.TryGetValue(p, out var state)) Apply(p, state);
    }
    public static void RestoreAll()
    {
        foreach (var weak in Panels) if (weak.TryGetTarget(out var p) && p != null && States.TryGetValue(p, out var s)) Restore(p, s);
    }
}

[HarmonyPatch(typeof(GridItemView), nameof(GridItemView.ShowTooltip))]
internal static class GridHoverPatch
{
    private static void Prefix(GridItemView __instance) => HoverPanel.Push(__instance.Item);
    private static void Finalizer() => HoverPanel.Pop();
}

[HarmonyPatch(typeof(ItemTooltip), nameof(ItemTooltip.Show), new[] { typeof(string), typeof(float), typeof(Offer), typeof(Item), typeof(InventoryController), typeof(ItemUiContext), typeof(InsuranceCompany) })]
internal static class ItemHoverPatch
{
    private static void Prefix(Item __3) => HoverPanel.Push(__3);
    private static void Finalizer() => HoverPanel.Pop();
}

[HarmonyPatch(typeof(QuestItemViewPanel), nameof(QuestItemViewPanel.CG_Awake))]
internal static class MarkHoverPatch
{
    private static void Prefix(QuestItemViewPanel __instance) => HoverPanel.Push(MarkerPatches.ItemFor(__instance));
    private static void Finalizer() => HoverPanel.Pop();
}

[HarmonyPatch(typeof(SimpleTooltip), nameof(SimpleTooltip.Show), new[] { typeof(string), typeof(Vector2?), typeof(float), typeof(float?) })]
internal static class TooltipHoverPatch
{
    private static void Prefix(SimpleTooltip __instance) => HoverPanel.Track(__instance);
}

[HarmonyPatch]
internal static class ProgressPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertySetter(typeof(TaskConditionCounter), nameof(TaskConditionCounter.Value));
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.SetStatus));
        yield return AccessTools.PropertySetter(typeof(AreaData), nameof(AreaData.Status));
        yield return AccessTools.PropertySetter(typeof(AreaData), nameof(AreaData.CurrentLevel));
        yield return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.ReportProfileUpdate));
    }
    private static void Postfix() => RuntimeData.Invalidate();
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.UpdateTotalWeight))]
internal static class InventoryChangedPatch
{
    private static void Postfix(Inventory __instance)
    {
        if (ReferenceEquals(__instance, RuntimeData.Profile?.Inventory)) RuntimeData.Invalidate();
    }
}

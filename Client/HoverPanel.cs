using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using EFT.UI;
using MoeNeedMarks.Shared;
using UnityEngine;

namespace MoeNeedMarks.Client;

/// <summary>Tracks item context and appends requirements inside the native tooltip.</summary>
internal static class HoverPanel
{
    private sealed class State
    {
        public Item? Item;
        public readonly TooltipTextState Text = new();
        public NativeTooltipScroll? Scroll;
        public float NextRefresh;
        public int Revision = -1;
        public bool HasAddition;
    }
    private static readonly Stack<Item?> Context = new();
    private static readonly ConditionalWeakTable<SimpleTooltip, State> States = new();
    private static readonly List<WeakReference<SimpleTooltip>> Tracked = new();

    public static void Push(Item? value) => Context.Push(value);
    public static void Pop() { if (Context.Count > 0) Context.Pop(); }

    public static void Track(SimpleTooltip tooltip)
    {
        Close(tooltip);
        var next = Context.Count > 0 ? Context.Peek() : null;
        if (next == null) return;
        if (!States.TryGetValue(tooltip, out var state))
        {
            state = new State(); States.Add(tooltip, state);
            Tracked.Add(new WeakReference<SimpleTooltip>(tooltip));
        }
        state.Item = next; state.Text.Reset(next.Id.ToString());
        state.NextRefresh = 0; state.Revision = -1;
        RuntimeData.Demand();
    }

    public static void Capture(SimpleTooltip tooltip, ref string text)
    {
        // Strip our block even if another mod replays an older tooltip's text.
        text = States.TryGetValue(tooltip, out var state) && state.Item != null
            ? state.Text.CaptureInput(text) : TooltipTextState.Strip(text);
    }

    public static void Append(SimpleTooltip tooltip)
    {
        if (tooltip._label == null || !States.TryGetValue(tooltip, out var state) || state.Item == null) return;
        var need = RuntimeData.Get(state.Item.TemplateId);
        string addition = "";
        if (need != null)
        {
            var counts = RuntimeData.Count(state.Item.TemplateId);
            addition = string.Join("\n", TooltipFormatter.Lines(need, counts.Carried, counts.Stash, Settings.Display, RuntimeData.Localize));
        }
        state.HasAddition = addition.Length != 0;
        // Do not call SetText recursively: the label already contains other prefixes' output.
        tooltip._label.text = state.Text.Compose(tooltip._label.text, addition);
        state.NextRefresh = Time.unscaledTime + 0.2f; state.Revision = RuntimeData.Revision;
    }

    private static bool Visible(SimpleTooltip tooltip, State state) =>
        tooltip != null && state.Item != null && tooltip.Displayed && tooltip.gameObject.activeInHierarchy && tooltip._label != null;

    public static void Tick()
    {
        Tracked.RemoveAll(w => !w.TryGetTarget(out var t) || t == null);
        foreach (var weak in Tracked)
        {
            if (!weak.TryGetTarget(out var tooltip) || !States.TryGetValue(tooltip, out var state) || !Visible(tooltip, state)) continue;
            RuntimeData.Demand();
            if (Time.unscaledTime < state.NextRefresh && state.Revision == RuntimeData.Revision) continue;
            tooltip.SetText(state.Text.RefreshInput(tooltip._label.text));
        }
    }

    public static void Layout()
    {
        foreach (var weak in Tracked)
        {
            if (!weak.TryGetTarget(out var tooltip) || !States.TryGetValue(tooltip, out var state) || !Visible(tooltip, state)) continue;
            if (!state.HasAddition) { state.Scroll?.Dispose(); state.Scroll = null; continue; }
            state.Scroll ??= new NativeTooltipScroll(tooltip);
            state.Scroll.Update();
        }
    }

    // IMGUI is used only to consume wheel input while the cursor stays on the item.
    // All rendering, clipping and layout belong to the existing Unity tooltip.
    public static void HandleScroll()
    {
        if (Event.current.type != EventType.ScrollWheel) return;
        foreach (var weak in Tracked)
        {
            if (weak.TryGetTarget(out var tooltip) && States.TryGetValue(tooltip, out var state) &&
                Visible(tooltip, state) && state.Scroll?.Scroll(Event.current.delta.y) == true)
            { Event.current.Use(); return; }
        }
    }

    public static void Close(SimpleTooltip tooltip)
    {
        if (!States.TryGetValue(tooltip, out var state)) return;
        state.Scroll?.Dispose(); state.Scroll = null;
        if (tooltip != null && tooltip._label != null) tooltip._label.text = TooltipTextState.Strip(tooltip._label.text);
        state.Item = null; state.HasAddition = false; state.Text.Reset(null);
    }
    public static void Clear()
    {
        foreach (var weak in Tracked) if (weak.TryGetTarget(out var tooltip) && tooltip != null) Close(tooltip);
    }
    public static void Dispose() { Clear(); Tracked.Clear(); Context.Clear(); }
}

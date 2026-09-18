using EFT.InventoryLogic;
using EFT.UI;
using MoeNeedMarks.Shared;
using UnityEngine;

namespace MoeNeedMarks.Client;

/// <summary>
/// Adjacent scrollable extension of the native tooltip. Original tooltip text and
/// third-party additions are untouched; a single panel is reused for every item.
/// Scroll while keeping the pointer on the item, so no click or pin is required.
/// </summary>
internal static class HoverPanel
{
    private static readonly Stack<Item?> Context = new();
    private static SimpleTooltip? tooltip;
    private static Item? item;
    private static string text = "";
    private static Vector2 scroll;
    private static float nextRefresh;
    private static int revision = -1;
    private static GUIStyle? label, box;
    private static Font? font;
    private static Texture2D? background;
    private static readonly Vector3[] Corners = new Vector3[4];

    public static void Push(Item? value) => Context.Push(value);
    public static void Pop() { if (Context.Count > 0) Context.Pop(); }
    public static void Track(SimpleTooltip value)
    {
        var next = Context.Count > 0 ? Context.Peek() : null;
        if (!ReferenceEquals(item, next)) scroll = Vector2.zero;
        item = next; tooltip = next == null ? null : value;
        text = ""; nextRefresh = 0; revision = -1;
        if (next != null) RuntimeData.Demand();
    }
    private static bool Visible => item != null && tooltip != null && tooltip.Displayed && tooltip.gameObject.activeInHierarchy;
    public static void Tick()
    {
        if (!Visible) return;
        RuntimeData.Demand();
        if (Time.unscaledTime < nextRefresh && revision == RuntimeData.Revision) return;
        nextRefresh = Time.unscaledTime + 0.2f; revision = RuntimeData.Revision;
        var need = RuntimeData.Get(item!.TemplateId);
        if (need == null) { text = ""; return; }
        var counts = RuntimeData.Count(item.TemplateId);
        text = string.Join("\n", TooltipFormatter.Lines(need, counts.Carried, counts.Stash, Settings.Display, RuntimeData.Localize));
    }
    public static void Draw()
    {
        if (!Visible || text.Length == 0) return;
        EnsureStyles();
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.6f);
        label!.fontSize = Mathf.RoundToInt(16 * scale);
        float width = Mathf.Min(620 * scale, Screen.width - 24);
        float contentHeight = label.CalcHeight(new GUIContent(text), width - 40);
        float height = Mathf.Min(contentHeight + 24, Screen.height * 0.58f);
        bool overflowing = contentHeight > height - 24;
        if (overflowing) height += 22 * scale;
        float x = Input.mousePosition.x + 25, y = Screen.height - Input.mousePosition.y + 15;
        if (tooltip!._boundsTransform != null)
        {
            tooltip._boundsTransform.GetWorldCorners(Corners);
            float left = Corners[0].x, right = Corners[2].x;
            x = right + 8 + width <= Screen.width ? right + 8 : left - width - 8;
            y = Screen.height - Corners[2].y;
        }
        x = Mathf.Clamp(x, 8, Mathf.Max(8, Screen.width - width - 8));
        y = Mathf.Clamp(y, 8, Mathf.Max(8, Screen.height - height - 8));
        var rect = new Rect(x, y, width, height);
        int oldDepth = GUI.depth; var oldColor = GUI.color;
        GUI.depth = -1000; GUI.color = Color.white;
        GUI.Box(rect, GUIContent.none, box!);
        var viewport = new Rect(x + 10, y + 10, width - 20, height - 20 - (overflowing ? 22 * scale : 0));
        // The native tooltip closes if the cursor leaves the source item; wheel
        // input is therefore accepted while still hovering that source item.
        if (overflowing && Event.current.type == EventType.ScrollWheel)
        {
            scroll.y = Mathf.Clamp(scroll.y + Event.current.delta.y * 28 * scale, 0, Mathf.Max(0, contentHeight - viewport.height));
            Event.current.Use();
        }
        scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, viewport.width - 20, contentHeight), false, overflowing);
        GUI.Label(new Rect(0, 0, viewport.width - 20, contentHeight), text, label);
        GUI.EndScrollView();
        if (overflowing) GUI.Label(new Rect(x + 10, y + height - 25 * scale, width - 20, 24 * scale), "保持悬浮，滚轮查看全部需求", label);
        GUI.color = oldColor; GUI.depth = oldDepth;
    }
    private static void EnsureStyles()
    {
        if (label != null) return;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
        label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true, richText = false, alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0) };
        label.normal.textColor = new Color(0.94f, 0.94f, 0.9f);
        background = new Texture2D(1, 1); background.SetPixel(0, 0, new Color(0.065f, 0.07f, 0.065f, 0.97f)); background.Apply();
        box = new GUIStyle(GUI.skin.box); box.normal.background = background;
    }
    public static void Clear() { tooltip = null; item = null; text = ""; scroll = Vector2.zero; }
    public static void Dispose()
    {
        Clear(); Context.Clear();
        if (font != null) UnityEngine.Object.Destroy(font);
        if (background != null) UnityEngine.Object.Destroy(background);
        label = null; box = null;
    }
}

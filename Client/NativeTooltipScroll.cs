using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoeNeedMarks.Client;

/// <summary>
/// Replaces only the native label's layout slot with a clipped viewport when needed.
/// The same TMP label, tooltip background, item preview and screen positioning remain in use.
/// </summary>
internal sealed class NativeTooltipScroll : IDisposable
{
    private readonly SimpleTooltip tooltip;
    private readonly TextMeshProUGUI label;
    private readonly RectTransform content;
    private RectTransform? viewport, parent;
    private LayoutElement? layout;
    private ContentSizeFitter? fitter;
    private bool fitterEnabled, raycast, boundsReplaced;
    private int sibling;
    private Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
    private Vector3 scale;
    private Quaternion rotation;
    private TextOverflowModes overflow;
    private float offset, contentHeight, viewportHeight, lastWidth, lastScreenHeight;
    private string lastText = "";

    public NativeTooltipScroll(SimpleTooltip value)
    {
        tooltip = value; label = value._label; content = label.rectTransform;
    }

    public void Update()
    {
        if (label == null || content == null || tooltip._mainTransform == null) return;
        float width = viewport != null ? viewport.rect.width : content.rect.width;
        if (label.text == lastText && Mathf.Abs(width - lastWidth) < 0.5f && lastScreenHeight == Screen.height) return;
        // Run after normal Update, before rendering. Rebuild only on content/size changes.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip._mainTransform);
        width = Mathf.Max(1, viewport != null ? viewport.rect.width : content.rect.width);
        contentHeight = Mathf.Ceil(label.GetPreferredValues(label.text, width, Mathf.Infinity).y);
        float scaleY = Mathf.Max(0.01f, Mathf.Abs(content.lossyScale.y));
        var bounds = tooltip._boundsTransform;
        float extraPixels = bounds == null ? 0 : Mathf.Max(0,
            bounds.rect.height * Mathf.Abs(bounds.lossyScale.y) - (viewport != null ? viewport.rect.height : content.rect.height) * scaleY);
        float limit = Mathf.Max(label.fontSize * 2, (Screen.height * 0.7f - extraPixels) / scaleY);
        viewportHeight = Mathf.Min(contentHeight, limit);
        if (contentHeight > limit + 1)
        {
            if (viewport == null) Attach(width);
            if (viewport != null)
            {
                layout!.preferredHeight = viewportHeight;
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, viewportHeight);
                content.sizeDelta = new Vector2(0, contentHeight);
                ApplyOffset();
                LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip._mainTransform);
            }
        }
        else if (viewport != null) Restore();
        lastText = label.text; lastWidth = width; lastScreenHeight = Screen.height;
        tooltip.SetPosition(Input.mousePosition);
    }

    private void Attach(float width)
    {
        // Never reparent the entire tooltip (including its background) into itself.
        parent = content.parent as RectTransform;
        if (parent == null || content == tooltip._mainTransform) return;
        sibling = content.GetSiblingIndex();
        anchorMin = content.anchorMin; anchorMax = content.anchorMax; pivot = content.pivot;
        sizeDelta = content.sizeDelta; anchoredPosition = content.anchoredPosition;
        scale = content.localScale; rotation = content.localRotation;
        overflow = label.overflowMode; raycast = label.raycastTarget;
        fitter = content.GetComponent<ContentSizeFitter>();
        fitterEnabled = fitter != null && fitter.enabled;
        if (fitter != null) fitter.enabled = false;

        var go = new GameObject("MoeNeedMarks.NativeViewport", typeof(RectTransform), typeof(LayoutElement), typeof(RectMask2D));
        viewport = (RectTransform)go.transform;
        viewport.SetParent(parent, false); viewport.SetSiblingIndex(sibling);
        viewport.anchorMin = new Vector2(anchorMin.x, 1);
        viewport.anchorMax = new Vector2(anchorMax.x, 1);
        viewport.pivot = new Vector2(pivot.x, 1);
        viewport.sizeDelta = new Vector2(sizeDelta.x, viewportHeight);
        viewport.anchoredPosition = new Vector2(anchoredPosition.x,
            content.localPosition.y + content.rect.yMax - parent.rect.yMax);
        layout = go.GetComponent<LayoutElement>();
        layout.minWidth = 0; layout.preferredWidth = width;
        layout.minHeight = 0; layout.preferredHeight = viewportHeight;
        layout.flexibleWidth = 0; layout.flexibleHeight = 0;

        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1); content.localScale = Vector3.one; content.localRotation = Quaternion.identity;
        content.sizeDelta = new Vector2(0, contentHeight); content.anchoredPosition = Vector2.zero;
        // RectMask2D clips the original TMP mesh, including any price/marker rich text.
        label.overflowMode = TextOverflowModes.Overflow; label.raycastTarget = false;
        boundsReplaced = tooltip._boundsTransform == content;
        if (boundsReplaced) tooltip._boundsTransform = viewport;
    }

    public bool Scroll(float delta)
    {
        if (viewport == null || contentHeight <= viewportHeight) return false;
        offset += delta * Mathf.Max(24, label.fontSize * 1.5f);
        ApplyOffset();
        return true;
    }

    private void ApplyOffset()
    {
        offset = Mathf.Clamp(offset, 0, Mathf.Max(0, contentHeight - viewportHeight));
        content.anchoredPosition = new Vector2(0, offset);
    }

    private void Restore()
    {
        if (viewport == null) return;
        if (content != null && parent != null)
        {
            content.SetParent(parent, false); content.SetSiblingIndex(sibling);
            content.anchorMin = anchorMin; content.anchorMax = anchorMax; content.pivot = pivot;
            content.sizeDelta = sizeDelta; content.anchoredPosition = anchoredPosition;
            content.localScale = scale; content.localRotation = rotation;
            label.overflowMode = overflow; label.raycastTarget = raycast;
            if (fitter != null) fitter.enabled = fitterEnabled;
            if (boundsReplaced && tooltip != null) tooltip._boundsTransform = content;
        }
        // Destroy is deferred; detach first so the empty viewport cannot occupy a layout slot.
        viewport.gameObject.SetActive(false); viewport.SetParent(null, false);
        UnityEngine.Object.Destroy(viewport.gameObject);
        viewport = null; layout = null; offset = 0;
        if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    public void Dispose() => Restore();
}

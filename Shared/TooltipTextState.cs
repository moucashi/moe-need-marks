namespace MoeNeedMarks.Shared;

/// <summary>Owns only our TMP link block, never the native text or another mod's suffix.</summary>
public sealed class TooltipTextState
{
    private const string Begin = "<link=\"moe.needmarks\">";
    private const string End = "</link>";
    public string? ItemId { get; private set; }
    public string SourceText { get; private set; } = "";
    public string RenderedText { get; private set; } = "";

    public void Reset(string? itemId)
    {
        ItemId = itemId;
        SourceText = RenderedText = "";
    }

    // Capture before other SetText prefixes. Replaying this source lets those mods
    // recalculate their own additions without feeding their previous output back in.
    public string CaptureInput(string? text) => SourceText = Strip(text);

    public string Compose(string? nativeText, string addition)
    {
        string original = Strip(nativeText);
        RenderedText = ItemId == null || string.IsNullOrEmpty(addition) ? original
            : original + Begin + (original.Length == 0 ? "" : "\n\n") + addition + End;
        return RenderedText;
    }

    public string RefreshInput(string? currentText) => currentText == RenderedText ? SourceText : Strip(currentText);

    public static string Strip(string? text)
    {
        string result = text ?? "";
        int start;
        while ((start = result.IndexOf(Begin, StringComparison.Ordinal)) >= 0)
        {
            int end = result.IndexOf(End, start + Begin.Length, StringComparison.Ordinal);
            // Do not truncate unknown/malformed third-party text.
            if (end < 0) break;
            result = result.Remove(start, end + End.Length - start);
        }
        return result;
    }
}

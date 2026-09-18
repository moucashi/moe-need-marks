namespace MoeNeedMarks.Shared;

/// <summary>Tickets ensure a completed request cannot repopulate another session.</summary>
public sealed class SessionGate
{
    public string Key { get; private set; } = "";
    public int Generation { get; private set; }
    public bool Switch(string key)
    {
        if (key == Key) return false;
        Key = key; Generation++; return true;
    }
    public bool Accepts(int generation, string key) => generation == Generation && key == Key && key.Length > 0;
}

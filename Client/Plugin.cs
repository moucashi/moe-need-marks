using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MoeNeedMarks.Shared;

namespace MoeNeedMarks.Client;

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
[BepInIncompatibility("VIP.TommySoucy.MoreCheckmarks")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? harmony;
    private float lastError;
    private void Awake()
    {
        Log = Logger; Settings.Bind(Config); harmony = new Harmony(ModInfo.Guid);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} 已加载（SPT 4.1.5）");
        }
        catch (Exception e)
        {
            harmony.UnpatchSelf(); Logger.LogError("需求标记补丁安装失败，已撤销本模组补丁：" + e); enabled = false;
        }
    }
    private void Update()
    {
        try { RuntimeData.Tick(); HoverPanel.Tick(); }
        catch (Exception e)
        {
            if (UnityEngine.Time.unscaledTime - lastError < 15 && lastError != 0) return;
            lastError = UnityEngine.Time.unscaledTime; Logger.LogError("需求标记刷新失败：" + e);
        }
    }
    private void OnGUI() => HoverPanel.Draw();
    private void OnDestroy()
    {
        RuntimeData.Stop(); MarkerPatches.RestoreAll(); HoverPanel.Dispose(); harmony?.UnpatchSelf();
    }
}

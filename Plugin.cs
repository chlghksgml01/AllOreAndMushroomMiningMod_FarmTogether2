using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Logic;
using Logic.Farm.Mine;
using Logic.Farm.Mine.Items;
using UnityEngine;

namespace VeinMiningMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static bool IsEnabled { get; private set; } = true;
    private static ConfigEntry<KeyCode> _toggleKey;

    public override void Load()
    {
        _toggleKey = Config.Bind("General", "ToggleKey", KeyCode.F8, "베인 마이닝 ON/OFF 토글 키");

        Log.LogInfo($"[{MyPluginInfo.PLUGIN_NAME}] 로드 중... (토글 키: {_toggleKey.Value})");
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(Plugin).Assembly);

        AddComponent<VeinMiningToggle>();
        Log.LogInfo($"[{MyPluginInfo.PLUGIN_NAME}] Harmony 패치 적용 완료.");
    }

    public class VeinMiningToggle : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey.Value))
            {
                IsEnabled = !IsEnabled;
                Log.LogInfo($"[{MyPluginInfo.PLUGIN_NAME}] 베인 마이닝 {(IsEnabled ? "ON" : "OFF")}");
            }
        }
    }
}

/// <summary>
/// MineData.Work 패치:
/// 광맥(MineSpotInstance) 또는 버섯(MineShroomInstance) 채굴 성공 시
/// 같은 광산의 동일 유형 슬롯을 모두 자동으로 채굴합니다.
/// </summary>
[HarmonyPatch(typeof(MineData), nameof(MineData.Work))]
public static class MineData_Work_Patch
{
    // 재귀 방지 플래그 (스레드 로컬)
    [ThreadStatic]
    private static bool _isPropagating;

    [HarmonyPostfix]
    public static void Postfix(
        bool __result,
        MineData __instance,
        Player player,
        bool performWork,
        MineSlot slot,
        WorkType workType)
    {
        // 실제 채굴 성공 + 전파 중이 아닐 때만 처리
        if (!Plugin.IsEnabled || !__result || !performWork || _isPropagating) return;
        if (slot == null || slot.Contents == null) return;

        var contents = slot.Contents;

        // 광맥 또는 버섯 여부 확인 (TryCast로 안전하게 캐스팅)
        bool isSpot   = contents.TryCast<MineSpotInstance>()   != null;
        bool isShroom = contents.TryCast<MineShroomInstance>() != null;

        if (!isSpot && !isShroom) return;

        _isPropagating = true;
        try
        {
            var slots = __instance.Slots;
            int count = slots.Count;

            for (int i = 0; i < count; i++)
            {
                var other = slots[i];

                // 같은 슬롯이거나 비어있으면 건너뜀
                if (other == null || other.Pointer == slot.Pointer || other.IsEmpty) continue;

                var otherContents = other.Contents;
                if (otherContents == null) continue;

                // 동일 유형(광맥↔광맥, 버섯↔버섯)만 처리
                bool otherIsSpot   = otherContents.TryCast<MineSpotInstance>()   != null;
                bool otherIsShroom = otherContents.TryCast<MineShroomInstance>() != null;

                if (isSpot   && !otherIsSpot)   continue;
                if (isShroom && !otherIsShroom) continue;

                // 채굴 시도 (실패해도 무시 — 아직 성장 중인 슬롯 등)
                FailedAction fail;
                __instance.Work(player, performWork: true, other, workType, out fail);
            }
        }
        finally
        {
            _isPropagating = false;
        }
    }
}

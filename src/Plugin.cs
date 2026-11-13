using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BokuMono;
using BokuMono.Data;
using BokuMono.Sound;
using HarmonyLib;
using UnityEngine;

namespace OliveTownQoL
{
    [BepInPlugin(GUID, "Olive Town QoL", VERSION)]
    public class Plugin : BasePlugin
    {
        public const string GUID = "ivan.olivetown.qol";
        public const string VERSION = "2.1.0";

        internal static ManualLogSource L;
        internal static ConfigFile Cfg;

        // ---- Time ----
        internal static ConfigEntry<bool> FreezeIndoors, FreezeInMines;
        internal static ConfigEntry<float> TimeSpeed, SlowSeconds;
        internal static ConfigEntry<KeyCode> FreezeToggleKey, ReloadKey, MakerCycleKey;

        // ---- Weather music ----
        internal static ConfigEntry<bool> WeatherMusicEnabled, WeatherMusicIndoors, LogMusic, AutoCalm;
        internal static ConfigEntry<KeyCode> AuditionKey;
        internal static ConfigEntry<string> RainyBgm, TyphoonBgm, SnowyBgm, HeavySnowBgm;

        // ---- QoL data tweaks ----
        internal static ConfigEntry<float> StackMultiplier, MakerTimeMultiplier, MakerMaxMultiplier, StaminaCostMultiplier;
        internal static ConfigEntry<int> StackCap;

        // ---- Universal maker ----
        internal static ConfigEntry<bool> UniversalMaker;

        internal static bool ManualFreeze;
        

        public override void Load()
        {
            L = Log;
            Cfg = Config;

            FreezeIndoors = Config.Bind("Time", "FreezeTimeIndoors", true, "Stop the clock while you are inside any building (houses, shops, your home, barns).");
            FreezeInMines = Config.Bind("Time", "FreezeTimeInMines", false, "Stop the clock while you are in a mine.");
            SlowSeconds = Config.Bind("Time", "SlowModeSecondsPerMinute", 5f, "In slow clock mode (L3 / F7), real seconds per game minute. Normal is 1.");
            TimeSpeed = Config.Bind("Time", "TimeSpeedMultiplier", 1.0f, "How fast time passes when it isn't frozen. 1 = normal, 0.5 = days last twice as long.");
            FreezeToggleKey = Config.Bind("Hotkeys", "ToggleFreezeAnywhere", KeyCode.F7, "Press to freeze/unfreeze the clock anywhere.");
            MakerCycleKey = Config.Bind("Hotkeys", "CycleUniversalMakerChoice", KeyCode.F6, "Switch what the universal maker makes from ingredients that fit several recipes (milk -> cheese/butter/yogurt, fruit -> jam/seedling/essence...). Cycles Auto -> Jam -> Seeds -> ...");
            ReloadKey = Config.Bind("Hotkeys", "ReloadConfig", KeyCode.F8, "Reload this config file without restarting the game.");

            WeatherMusicEnabled = Config.Bind("WeatherMusic", "Enabled", true, "Play different background music depending on the weather.");
            WeatherMusicIndoors = Config.Bind("WeatherMusic", "ApplyIndoors", false, "Also swap the music while indoors.");
            RainyBgm = Config.Bind("WeatherMusic", "Rainy", "5023,5021", "BGM ids (comma separated) to use on rainy days. See BepInEx/OliveTownQoL_music_list.txt. Empty = game default.");
            TyphoonBgm = Config.Bind("WeatherMusic", "Typhoon", "5041,5023", "BGM ids for storms/typhoons.");
            SnowyBgm = Config.Bind("WeatherMusic", "Snowy", "5022,5023", "BGM ids for snowy days.");
            HeavySnowBgm = Config.Bind("WeatherMusic", "HeavySnow", "5022,5041", "BGM ids for blizzards.");
            AutoCalm = Config.Bind("WeatherMusic", "AutoCalmOnBadWeather", true, "When no ids are set for a weather, rain/storm/snow days play the calmer night version of the area's music instead of the cheerful daytime tune.");
            AuditionKey = Config.Bind("Hotkeys", "MusicPreviewNext", KeyCode.F9, "Cycle music: Spring, Summer, Fall, Winter, Town, Evening, then back to automatic.");
            LogMusic = Config.Bind("WeatherMusic", "LogMusicChanges", true, "Write every music change to the BepInEx log (handy for finding track ids you like).");

            StackMultiplier = Config.Bind("QualityOfLife", "StackSizeMultiplier", 100.0f, "Multiply how many of an item fit in one stack (bag, boxes and fridge). 100 with cap 9999 = practically infinite storage.");
            StackCap = Config.Bind("QualityOfLife", "StackSizeCap", 9999, "Upper limit for stack sizes.");
            MakerTimeMultiplier = Config.Bind("QualityOfLife", "MakerTimeMultiplier", 0.01f, "Multiply maker processing time. 0.5 = twice as fast.");
            MakerMaxMultiplier = Config.Bind("QualityOfLife", "MakerMaxBatchMultiplier", 9.9f, "Multiply how many items a maker accepts in one batch.");
            StaminaCostMultiplier = Config.Bind("QualityOfLife", "StaminaCostMultiplier", 1.0f, "Multiply stamina used by tools/actions. 0.5 = half the stamina.");

            UniversalMaker = Config.Bind("UniversalMaker", "Enabled", true, "Every maker can run every recipe of its size (normal makers do all normal recipes, big makers all big recipes). Keep one maker and delete the rest.");

            StorageConfig.Bind(Config);
            Pad.Bind(Config);
            MakerRename.Bind(Config);
            MusicDirector.Bind(Config);
            new Harmony(GUID).PatchAll(typeof(Plugin).Assembly);
            L.LogInfo($"Olive Town QoL {VERSION} loaded.");
        }

        // Called once per frame from DateManager.Update
        internal static void Tick()
        {
            try
            {
                if (Input.GetKeyDown(FreezeToggleKey.Value)) ToggleFreeze();
                if (Input.GetKeyDown(MakerCycleKey.Value))
                {
                    UniversalMakerPatch.CycleMode();
                }
                if (Input.GetKeyDown(AuditionKey.Value)) { if (MusicDirector.Enabled.Value) MusicDirector.Cycle(); else WeatherMusicPatch.Audition(); }
                if (Input.GetKeyDown(ReloadKey.Value))
                {
                    Cfg.Reload();
                    DataTweaks.Reapply();
                    MakerRename.Redo();
                    Toast.Show("Mod settings reloaded");
                }
                Pad.Tick();
            }
            catch (Exception e) { L.LogError(e); }
            if (!_hello && Time.unscaledTime > 8f) { _hello = true; Toast.Show("Olive Town mods active", 6f); }
            Toast.Tick();
            MakerRename.Tick();
            MusicDirector.Tick();
            StorageSort.Tick();
            DataTweaks.TryApply();
        }

        // Clock modes, cycled with L3 / F7: Normal -> Frozen -> Slow -> Normal.
        // Outdoors the choice sticks. Inside a building the clock starts frozen, and L3 picks a
        // mode for that building only (it resets when you leave).
        internal enum Clock { Normal, Frozen, Slow }
        static Clock _outdoorMode = Clock.Normal;
        static Clock? _hereMode;
        static uint _hereField;

        internal static Clock Effective()
        {
            if (_hereMode.HasValue)
            {
                if (CurField() == _hereField) return _hereMode.Value;
                _hereMode = null; // left the building
            }
            if (ManualFreeze) return Clock.Frozen;
            if (AutoFreeze()) return Clock.Frozen;
            return _outdoorMode;
        }

        static Clock Next(Clock c) => c == Clock.Normal ? Clock.Frozen : c == Clock.Frozen ? Clock.Slow : Clock.Normal;

        internal static void ToggleFreeze()
        {
            ManualFreeze = false;
            var next = Next(Effective());
            if (AutoFreeze()) { _hereMode = next; _hereField = CurField(); }
            else _outdoorMode = next;
            string where = AutoFreeze() ? " (in this building)" : "";
            Toast.Show(next switch
            {
                Clock.Frozen => "Clock: frozen" + where,
                Clock.Slow => $"Clock: slow, {SlowSeconds.Value:0.#} s per minute" + where,
                _ => "Clock: normal speed" + where
            });
        }

        // Multiplier on the game's clock speed (normal = 1 game minute per real second).
        internal static float SpeedFactor()
        {
            float m = TimeSpeed.Value > 0f ? TimeSpeed.Value : 1f;
            if (Effective() == Clock.Slow) m *= 1f / Math.Max(1f, SlowSeconds.Value);
            return m;
        }

        static uint CurField()
        {
            try { return FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0; } catch { return 0; }
        }

        static bool AutoFreeze() => (FreezeIndoors.Value && IsIndoor()) || (FreezeInMines.Value && InMine());

        static uint _lastField = uint.MaxValue;
        static bool _lastIndoor;

        internal static bool IsIndoor()
        {
            if (!FieldManager.IsInstance) return false;
            var fm = FieldManager.Instance;
            if (fm == null) return false;
            uint id = fm.CurrentFieldId;
            if (id != _lastField)
            {
                _lastField = id;
                try { _lastIndoor = fm.IsIndoorField(id); } catch { _lastIndoor = false; }
            }
            return _lastIndoor;
        }

        internal static bool InMine()
        {
            if (!FieldManager.IsInstance) return false;
            var fm = FieldManager.Instance;
            return fm != null && fm.IsMineField;
        }

        internal static bool FrozenThisFrame, InScaledUpdate;
        static bool _hello;
        internal static bool EventRunning()
        {
            try { return GameController.IsInstance && GameController.Instance.IsPlayingEvent; } catch { return false; }
        }

        internal static bool ShouldFreeze() => Effective() == Clock.Frozen;
    }

    // ===================== Time =====================
    [HarmonyPatch(typeof(DateManager), nameof(DateManager.Update))]
    static class DateManagerUpdatePatch
    {
        static bool _loggedScale;
        static float _nextDiag;

        static bool Prefix(DateManager __instance, out float __state)
        {
            __state = -1f;
            Plugin.Tick();
            try
            {
                if (!_loggedScale) { _loggedScale = true; Plugin.L.LogInfo($"Game TimeScale = {__instance.TimeScale}"); }
                bool freeze = Plugin.ShouldFreeze();
                if (Time.unscaledTime > _nextDiag)
                {
                    _nextDiag = Time.unscaledTime + 60f;
                    uint fid = 0; try { fid = FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0; } catch { }
                    Plugin.L.LogInfo($"[diag] {__instance.Now.Hour:00}:{__instance.Now.Minute:00} field={fid} indoor={Plugin.IsIndoor()} upd={__instance.updateEnabled} force={__instance.forceUpdateEnabled} freeze={freeze}");
                }
                if (freeze && !Plugin.EventRunning()) { Plugin.FrozenThisFrame = true; return false; } // skip this frame's clock tick
                Plugin.FrozenThisFrame = false;
                float m = Plugin.SpeedFactor();
                if (Math.Abs(m - 1f) > 0.0001f)
                {
                    __state = __instance.TimeScale;
                    __instance.TimeScale = __state * m;
                    Plugin.InScaledUpdate = true;
                }
            }
            catch (Exception e) { Plugin.L.LogError(e); }
            return true;
        }

        static void Postfix(DateManager __instance, float __state)
        {
            if (__state >= 0f) __instance.TimeScale = __state;
            Plugin.InScaledUpdate = false;
        }
    }

    [HarmonyPatch(typeof(DateManager), nameof(DateManager.AddSeconds))]
    static class DateManagerAddSecondsPatch
    {
        // Small clock steps coming from elsewhere are also held back while frozen (big jumps like sleeping pass).
        static bool Prefix(ref float seconds)
        {
            if (!(seconds > 0f && seconds < 600f) || Plugin.EventRunning()) return true;
            if (Plugin.ShouldFreeze()) return false;
            if (!Plugin.InScaledUpdate) seconds *= Plugin.SpeedFactor(); // slow mode for clock steps made outside Update
            return true;
        }
    }

    // ===================== Weather music =====================
    [HarmonyPatch(typeof(SoundBgmController), nameof(SoundBgmController.PlayBgm))]
    static class WeatherMusicPatch
    {
        internal static SoundBgmController Ctl;
        static bool _auditioning;
        static int _auditionIdx = -1;

        internal static void Audition()
        {
            try
            {
                if (Ctl == null || !MasterDataManager.IsInstance) { Toast.Show("Load your save first to preview music"); return; }
                var picks = new List<(uint id, string label)>();
                void Add(string ids, string w) { foreach (var i in ParseIds(ids)) if (!picks.Any(p => p.id == i)) picks.Add((i, w)); else { var k = picks.FindIndex(p => p.id == i); picks[k] = (i, picks[k].label + ", " + w); } }
                Add(Plugin.RainyBgm.Value, "Rain"); Add(Plugin.TyphoonBgm.Value, "Storm"); Add(Plugin.SnowyBgm.Value, "Snow"); Add(Plugin.HeavySnowBgm.Value, "Blizzard");
                if (picks.Count == 0) { Toast.Show("No weather tracks set in the config"); return; }
                _auditionIdx = (_auditionIdx + 1) % (picks.Count + 1);
                if (_auditionIdx == picks.Count)
                {
                    try { if (SoundManager.IsInstance && FieldManager.IsInstance) SoundManager.Instance.PlayFieldBgm(FieldManager.Instance.CurrentFieldId); } catch { }
                    Toast.Show("Music preview off", 3f);
                    return;
                }
                var (id, label) = picks[_auditionIdx];
                var d = FindBgm(id);
                if (d == null) { Toast.Show($"Track {id} not found"); return; }
                _auditioning = true;
                try { Ctl.PlayBgm(d, 0.5f, 0.5f, 1f); } finally { _auditioning = false; }
                Toast.Show($"Preview {_auditionIdx + 1}/{picks.Count}: {Nice(d.CueName)} (plays on: {label})", 5f);
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }

        static string Nice(string cue) => (cue ?? "") switch
        {
            var c when c.Contains("Calm") => "Calm",
            var c when c.Contains("Guitar") => "Guitar",
            var c when c.Contains("Orgel") => "Music Box",
            var c when c.Contains("NightCity") => "Night Town",
            var c => c.Replace("SOUND_BGM_", "")
        };

        static BgmMasterData CalmVersion(BgmMasterData d)
        {
            if (d.Time == BgmMaster.Time.NigntTime) return null;
            BgmMasterData best = null;
            foreach (var b in MasterDataManager.Instance.BgmMasterData)
            {
                if (b == null || b.Scene != BgmMaster.Scene.Field || b.Time != BgmMaster.Time.NigntTime || b.BgmFieldId != d.BgmFieldId) continue;
                if (best == null || (b.DayType == d.DayType && best.DayType != d.DayType)) best = b;
            }
            return best;
        }

        static void Prefix(SoundBgmController __instance, ref BgmMasterData bgmData)
        {
            Ctl = __instance;
            if (_auditioning || MusicDirector.Enabled.Value) return;
            try
            {
                if (bgmData == null) return;
                var original = bgmData;
                if (Plugin.WeatherMusicEnabled.Value && bgmData.Scene == BgmMaster.Scene.Field && GameController.IsInstance
                    && (Plugin.WeatherMusicIndoors.Value || !Plugin.IsIndoor()))
                {
                    var w = GameController.Instance.NowWeather;
                    string list = w switch
                    {
                        BokuMonoWeather.Rainy => Plugin.RainyBgm.Value,
                        BokuMonoWeather.Typhoon => Plugin.TyphoonBgm.Value,
                        BokuMonoWeather.Snowy => Plugin.SnowyBgm.Value,
                        BokuMonoWeather.HeavySnow => Plugin.HeavySnowBgm.Value,
                        _ => ""
                    };
                    var ids = ParseIds(list);
                    if (ids.Count > 0)
                    {
                        int day = 0;
                        try { if (DateManager.IsInstance) day = DateManager.Instance.Now.Day; } catch { }
                        uint pick = ids[Math.Abs(day) % ids.Count];
                        var repl = FindBgm(pick);
                        if (repl != null) bgmData = repl;
                        else Plugin.L.LogWarning($"Weather music: no BGM with id {pick}");
                    }
                    else if (Plugin.AutoCalm.Value && w != BokuMonoWeather.Sunny)
                    {
                        var calm = CalmVersion(bgmData);
                        if (calm != null) bgmData = calm;
                    }
                }
                if (Plugin.LogMusic.Value)
                    Plugin.L.LogInfo(bgmData == original
                        ? $"Music: {original.Id} ({original.CueName})"
                        : $"Music: {original.Id} ({original.CueName}) -> {bgmData.Id} ({bgmData.CueName}) for weather");
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }

        static List<uint> ParseIds(string s)
        {
            var r = new List<uint>();
            if (string.IsNullOrWhiteSpace(s)) return r;
            foreach (var p in s.Split(',', ';', ' '))
                if (uint.TryParse(p.Trim(), out var v)) r.Add(v);
            return r;
        }

        static BgmMasterData FindBgm(uint id)
        {
            if (!MasterDataManager.IsInstance) return null;
            var list = MasterDataManager.Instance.BgmMasterData;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Id == id) return list[i];
            return null;
        }
    }

    // ===================== Master-data tweaks + dumps =====================
    static class DataTweaks
    {
        static bool _applied, _dumped;
        static readonly Dictionary<IntPtr, int> origStack = new();
        static readonly Dictionary<IntPtr, (int time, uint max)> origMaker = new();
        static readonly Dictionary<IntPtr, float> origHp = new();

        static int _dumpTries;
        internal static void TryApply()
        {
            if (_applied && !_dumped && MasterDataManager.IsInstance && (++_dumpTries % 120) == 0 && _dumpTries < 120 * 60)
            {
                var m = MasterDataManager.Instance;
                if (m.BgmMasterData != null && m.BgmMasterData.Count > 0) { _dumped = true; Dump(m); }
            }
            if (_applied) return;
            try
            {
                if (!MasterDataManager.IsInstance) return;
                var mdm = MasterDataManager.Instance;
                if (mdm == null || mdm.ItemMasterData == null || mdm.ItemMasterData.Count == 0 || mdm.MakerMasterData == null || mdm.MakerMasterData.Count == 0) return;
                _applied = true;
                Snapshot(mdm);
                Reapply();
            }
            catch (Exception e) { _applied = true; Plugin.L.LogError(e); }
        }

        static void Snapshot(MasterDataManager mdm)
        {
            foreach (var it in mdm.ItemMasterData) if (it != null) origStack[it.Pointer] = it.StackSize;
            foreach (var m in mdm.MakerMasterData) if (m != null) origMaker[m.Pointer] = (m.Time, m.MaxCraftNum);
            if (mdm.PlayerHPMasterData != null)
                foreach (var h in mdm.PlayerHPMasterData) if (h != null) origHp[h.Pointer] = h.HP;
        }

        internal static void Reapply()
        {
            if (!_applied || !MasterDataManager.IsInstance) return;
            var mdm = MasterDataManager.Instance;
            int changed = 0;
            float sm = Plugin.StackMultiplier.Value; int cap = Plugin.StackCap.Value;
            foreach (var it in mdm.ItemMasterData)
            {
                if (it == null || !origStack.TryGetValue(it.Pointer, out var o)) continue;
                int v = o;
                if (o > 1 && Math.Abs(sm - 1f) > 0.0001f) v = Math.Max(o, Math.Min(cap, (int)Math.Round(o * sm)));
                if (it.StackSize != v) { it.StackSize = v; changed++; }
            }
            float tm = Math.Max(0.01f, Plugin.MakerTimeMultiplier.Value), mm = Math.Max(1f, Plugin.MakerMaxMultiplier.Value);
            foreach (var m in mdm.MakerMasterData)
            {
                if (m == null || !origMaker.TryGetValue(m.Pointer, out var o)) continue;
                m.Time = Math.Max(1, (int)Math.Round(o.time * tm));
                m.MaxCraftNum = (uint)Math.Max(1, Math.Round(o.max * mm));
            }
            float hm = Math.Max(0f, Plugin.StaminaCostMultiplier.Value);
            if (mdm.PlayerHPMasterData != null)
                foreach (var h in mdm.PlayerHPMasterData)
                    if (h != null && origHp.TryGetValue(h.Pointer, out var o)) h.HP = o * hm;
            UniversalMakerPatch.Invalidate();
            Plugin.L.LogInfo($"Applied tweaks: stacks x{sm} (cap {cap}), maker time x{tm}, maker batch x{mm}, stamina cost x{hm}.");
        }

        internal static string ItemName(uint id)
        {
            try
            {
                var mdm = MasterDataManager.Instance;
                foreach (var it in mdm.ItemMasterData) if (it != null && it.Id == id) { var n = it.ItemName; return string.IsNullOrEmpty(n) ? $"item{id}" : n; }
            }
            catch { }
            return $"item{id}";
        }

        static void Dump(MasterDataManager mdm)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Background music list. Use the Id in BepInEx/config/" + Plugin.GUID + ".cfg [WeatherMusic].");
                sb.AppendLine("Id\tScene\tBgmFieldId\tTime\tDayType\tCueSheet\tCueName");
                foreach (var b in mdm.BgmMasterData)
                    try { if (b != null) sb.AppendLine($"{b.Id}\t{b.Scene}\t{b.BgmFieldId}\t{b.Time}\t{b.DayType}\t{b.CueSheet}\t{b.CueName}"); } catch (Exception e) { sb.AppendLine("err " + e.Message); }
                if (mdm.BgmFieldMasterData != null)
                {
                    sb.AppendLine().AppendLine("# BgmFieldId -> field ids");
                    foreach (var f in mdm.BgmFieldMasterData)
                        try { if (f != null && f.FieldIds != null) sb.AppendLine($"{f.Id}\t{string.Join(",", Enumerable.Range(0, f.FieldIds.Count).Select(i => f.FieldIds[i]))}"); } catch { }
                }
                File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "OliveTownQoL_music_list.txt"), sb.ToString());

                sb.Clear();
                sb.AppendLine("Id\tCategory\tMaterial\tMatNum\tProduct\tCraftNum\tTime\tMaxBatch");
                foreach (var m in mdm.MakerMasterData)
                    try { if (m != null) sb.AppendLine($"{m.Id}\t{(MakerCategory)m.Category}\t{ItemName(m.MaterialItemId)} ({m.MaterialItemId})\t{m.MaterialItemNum}\t{ItemName(m.CraftItemId)} ({m.CraftItemId})\t{m.CraftNum}\t{origMaker[m.Pointer].time}\t{origMaker[m.Pointer].max}"); } catch (Exception e) { sb.AppendLine("err " + e.Message); }
                File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "OliveTownQoL_maker_list.txt"), sb.ToString());
                Plugin.L.LogInfo("Wrote music and maker lists to the BepInEx folder.");
            }
            catch (Exception e) { Plugin.L.LogError("Dump failed: " + e); }
        }
    }

    // ===================== Universal maker =====================
    [HarmonyPatch(typeof(MakerManager), nameof(MakerManager.GetMakerDataList))]
    static class UniversalMakerPatch
    {
        static bool _busy;
        static Dictionary<int, Dictionary<uint, List<uint>>> _shared; // tier -> material -> categories

        // Base (normal-size) categories in "Auto" priority order. Big makers use base+24.
        static readonly MakerCategory[] AutoOrder = {
            MakerCategory.Wood, MakerCategory.Yarn, MakerCategory.Weaving, MakerCategory.Ingot, MakerCategory.Jewelry, MakerCategory.Brick,
            MakerCategory.KnittingYarn, MakerCategory.Cloth, MakerCategory.Mayonnaise,
            MakerCategory.Cheese, MakerCategory.Jam, MakerCategory.Syrup, MakerCategory.Milling, MakerCategory.Seasoning,
            MakerCategory.Butter, MakerCategory.Yogurt, MakerCategory.Essence, MakerCategory.Color,
            MakerCategory.CropSeed, MakerCategory.Seedling, MakerCategory.MushroomSeed, MakerCategory.Honey };
        static readonly string[] ModeNames = { "Auto (cheese, jam, oil/sauce, flour...)", "Jam", "Seeds", "Seedlings", "Flour/Powder", "Oil/Sauce/Vinegar", "Essence", "Dye", "Cheese", "Butter", "Yogurt" };
        static readonly MakerCategory[] ModeCats = { MakerCategory.None, MakerCategory.Jam, MakerCategory.CropSeed, MakerCategory.Seedling, MakerCategory.Milling, MakerCategory.Syrup, MakerCategory.Essence, MakerCategory.Color, MakerCategory.Cheese, MakerCategory.Butter, MakerCategory.Yogurt };
        internal static int Mode;

        internal static void Invalidate() => _shared = null;
        internal static void CycleMode(int dir = 1)
        {
            Mode = ((Mode + dir) % ModeNames.Length + ModeNames.Length) % ModeNames.Length;
            Toast.Show($"Universal maker: {ModeNames[Mode]}");
            try { if (Pad.MakerOpen()) Pad.OpenMaker.GroupRefresh(); } catch { }
        }

        static uint Base(uint cat) => cat > 24 ? cat - 24 : cat;
        static int Tier(uint cat) => cat > 24 ? 1 : 0;
        static bool Passive(uint cat) { var b = Base(cat); return b == (uint)MakerCategory.BeeKeeping || b == (uint)MakerCategory.LogCultivation || b == 0; }
        static int Rank(uint cat) { int i = Array.IndexOf(AutoOrder, (MakerCategory)Base(cat)); return i < 0 ? 999 : i; }

        static void Build()
        {
            _shared = new() { [0] = new(), [1] = new() };
            foreach (var m in MasterDataManager.Instance.MakerMasterData)
            {
                if (m == null || Passive(m.Category)) continue;
                var d = _shared[Tier(m.Category)];
                if (!d.TryGetValue(m.MaterialItemId, out var cats)) d[m.MaterialItemId] = cats = new List<uint>();
                if (!cats.Contains(m.Category)) cats.Add(m.Category);
            }
            foreach (var d in _shared.Values) foreach (var c in d.Values) c.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        }

        static void Postfix(MakerManager __instance, ref Il2CppSystem.Collections.Generic.List<MakerMasterData> __result)
        {
            if (_busy || !Plugin.UniversalMaker.Value || !MasterDataManager.IsInstance) return;
            _busy = true;
            try
            {
                uint myCat = (uint)__instance.currentMakerCategory;
                if (Passive(myCat)) return; // beehives / mushroom logs stay as they are
                int tier = Tier(myCat);
                if (_shared == null) Build();

                // Whatever recipe is already loaded in this maker must stay resolvable.
                uint runningId = 0, runningCat = 0, runningMat = 0;
                try { runningId = __instance.GetMakerId(); } catch { }
                var all = MasterDataManager.Instance.MakerMasterData;
                if (runningId != 0)
                    foreach (var m in all) if (m != null && m.Id == runningId) { runningCat = m.Category; runningMat = m.MaterialItemId; break; }

                uint modeCat = ModeCats[Mode] == MakerCategory.None ? 0 : (uint)ModeCats[Mode] + (tier == 1 ? 24u : 0u);
                var res = new Il2CppSystem.Collections.Generic.List<MakerMasterData>();
                foreach (var m in all)
                {
                    if (m == null || Passive(m.Category) || Tier(m.Category) != tier) continue;
                    var cats = _shared[tier][m.MaterialItemId];
                    uint chosen;
                    if (runningId != 0 && m.MaterialItemId == runningMat) chosen = runningCat;
                    else if (cats.Count == 1) chosen = cats[0];
                    else if (modeCat != 0 && cats.Contains(modeCat)) chosen = modeCat;
                    else if (modeCat == 0 && cats.Contains(myCat)) chosen = myCat; // a dedicated maker keeps its own job in Auto
                    else chosen = cats[0];
                    if (m.Category == chosen) res.Add(m);
                }
                __result = res;
            }
            catch (Exception e) { Plugin.L.LogError(e); }
            finally { _busy = false; }
        }
    }
}

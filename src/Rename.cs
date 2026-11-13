using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BokuMono;
using BokuMono.Data;
using HarmonyLib;
using UnityEngine;

namespace OliveTownQoL
{
    // Renames one maker (default: Lumber Maker) to "Universal Maker" in every text table.
    static class MakerRename
    {
        internal static ConfigEntry<string> From, To, Caption, Tint;
        static bool _done;
        static int _tries;
        static readonly Dictionary<IntPtr, (LocalizeTextData d, string orig)> _orig = new();

        internal static void Bind(ConfigFile c)
        {
            From = c.Bind("UniversalMaker", "RenameMaker", "Lumber Maker", "Which maker gets renamed (exact English name; the Giant version is renamed too). Empty = no rename.");
            To = c.Bind("UniversalMaker", "NewName", "Universal Maker", "New name shown everywhere.");
            Tint = c.Bind("UniversalMaker", "TintColor", "#8FE3D6", "Colour tint for the renamed maker on your farm (hex like #8FE3D6). Empty = original look.");
            Caption = c.Bind("UniversalMaker", "NewDescription", "Turns any ingredients into goods: lumber, cloth, ingots, cheese, jam, seeds and more.", "New description. Empty = keep the game's description (with the name swapped).");
        }

        internal static void Redo()
        {
            foreach (var v in _orig.Values) try { v.d.Text = v.orig; } catch { }
            _orig.Clear(); _done = false; _tries = 59;
        }

        static void Set(LocalizeTextData d, string text)
        {
            if (d == null || text == null) return;
            if (!_orig.ContainsKey(d.Pointer)) _orig[d.Pointer] = (d, d.Text);
            d.Text = text;
        }

        // Keep any formatting tags around the original text so the font/style matches.
        static string KeepTags(string orig, string text)
        {
            var lead = Regex.Match(orig ?? "", @"^(\s*<[^>]+>)*").Value;
            var trail = Regex.Match(orig ?? "", @"(</[^>]+>\s*)*$").Value;
            return lead + text + trail;
        }

        internal static void Tick()
        {
            if (_done || string.IsNullOrWhiteSpace(From.Value)) return;
            if ((++_tries % 60) != 0) return;
            try
            {
                if (!MasterDataManager.IsInstance || !LanguageManager.IsInstance) return;
                var mdm = MasterDataManager.Instance;
                var tp = LanguageManager.Instance.textProvider;
                if (tp == null || tp.tablese == null || mdm.ItemMasterData == null) return;

                string from = From.Value, to = To.Value;
                string fromPl = from + "s", toPl = to + "s";
                int n = 0;
                // 1) swap the name everywhere it appears (item names, facility names, plurals, dialogue...)
                foreach (var kv in tp.tablese)
                {
                    var table = kv.Value;
                    if (table == null || table.list == null) continue;
                    foreach (var d in table.list)
                    {
                        if (d == null) continue;
                        string t; try { t = d.Text; } catch { continue; }
                        if (string.IsNullOrEmpty(t) || t.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var nt = Regex.Replace(t, Regex.Escape(fromPl), toPl, RegexOptions.IgnoreCase);
                        nt = Regex.Replace(nt, Regex.Escape(from), to, RegexOptions.IgnoreCase);
                        Set(d, nt); n++;
                    }
                }
                // 2) new description for the renamed items
                if (!string.IsNullOrWhiteSpace(Caption.Value))
                    foreach (var it in mdm.ItemMasterData)
                    {
                        if (it == null) continue;
                        string name; try { name = it.ItemName; } catch { continue; }
                        if (string.IsNullOrEmpty(name) || !name.EndsWith(to, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var type in new[] { LocalizeTextTableType.Caption, LocalizeTextTableType.FacilityCaption })
                        {
                            LocalizeTextData d = null;
                            try { d = tp.GetLocalizeTextData(type, it.CaptionTextId); } catch { }
                            if (d == null) continue;
                            var orig = _orig.TryGetValue(d.Pointer, out var o) ? o.orig : d.Text;
                            Plugin.L.LogInfo($"Caption ({type}) for {name} was: {Escape(orig)}");
                            Set(d, KeepTags(orig, Caption.Value));
                        }
                    }
                if (n > 0) { _done = true; Plugin.L.LogInfo($"Renamed '{from}' -> '{to}' in {n} texts."); }
                else if (_tries > 60 * 120) _done = true;
            }
            catch (Exception e) { Plugin.L.LogWarning("Rename: " + e.Message); _done = true; }
        }

        static string Escape(string s) => string.Concat((s ?? "").Select(ch => ch < 32 || ch > 126 ? $"\\u{(int)ch:X4}" : ch.ToString()));
    }

    [HarmonyPatch(typeof(FieldMaker), nameof(FieldMaker.Setup))]
    static class MakerTint
    {
        static readonly System.Collections.Generic.HashSet<int> _done = new();
        static void Postfix(FieldMaker __instance)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(MakerRename.Tint.Value) || !MakerRename.From.Value.StartsWith("Lumber", StringComparison.OrdinalIgnoreCase)) return;
                var cat = __instance.GetMakerCategory;
                if (cat != MakerCategory.Wood && cat != MakerCategory.Big_Wood) return;
                if (!ColorUtility.TryParseHtmlString(MakerRename.Tint.Value, out var col)) return;
                var root = __instance.currentMaker != null ? __instance.currentMaker.gameObject : __instance.gameObject;
                if (!_done.Add(root.GetInstanceID())) return;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                    {
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * col);
                        else if (m.HasProperty("_Color")) m.color = m.color * col;
                    }
            }
            catch (Exception e) { Plugin.L.LogWarning("Tint: " + e.Message); }
        }
    }
}

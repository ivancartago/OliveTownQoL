using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using BokuMono;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace OliveTownQoL
{
    // Small on-screen message in the top-centre of the screen.
    static class Toast
    {
        static GameObject _root;
        static Text _text;
        static float _hideAt;

        internal static void Show(string msg, float seconds = 2.5f)
        {
            Plugin.L.LogMessage(msg);
            try
            {
                if (_root == null && !Create()) return;
                _root.SetActive(true);
                _text.text = msg;
                _hideAt = Time.unscaledTime + seconds;
            }
            catch (Exception e) { Plugin.L.LogWarning("Toast failed: " + e.Message); _root = null; }
        }

        internal static void Tick()
        {
            try { if (_root != null && _root.activeSelf && Time.unscaledTime > _hideAt) _root.SetActive(false); } catch { _root = null; }
        }

        static bool Create()
        {
            _root = new GameObject("OliveTownQoL_Toast");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.09f, 0.06f, 0.82f);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0, -40);
            prt.sizeDelta = new Vector2(900, 70);

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(panel.transform, false);
            _text = tgo.AddComponent<Text>();
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont(new Il2CppStringArray(new[] { "Segoe UI", "Arial" }), 32); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            _text.font = f;
            _text.fontSize = 32;
            _text.color = new Color(1f, 0.97f, 0.88f, 1f);
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16, 4); trt.offsetMax = new Vector2(-16, -4);
            return true;
        }
    }

    // Xbox controller via XInput (works alongside the game's own input).
    //   L3 (left stick click): sort an open box/fridge, otherwise freeze/unfreeze the clock
    //   RT / LT: next / previous universal maker mode while a maker is open
    static class Pad
    {
        internal static ConfigEntry<bool> Enabled;
        internal static UIPageCommonInputBox OpenMaker;

        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int Get14(int i, out XState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int i, out XState s);

        const ushort L3 = 0x0040;
        static bool _use910, _broken;
        static bool _l3, _lt, _rt;
        static float _nextScan; static int _pad = -1;

        internal static void Bind(ConfigFile c)
        {
            Enabled = c.Bind("Controller", "Enabled", true, "Xbox controller. In a box/fridge: L3 = sort, RT/LT = next/previous page. In a maker: RT/LT = universal maker mode. Elsewhere: L3 = freeze/unfreeze the clock.");
        }

        internal static bool MakerOpen()
        {
            try { if (OpenMaker != null && OpenMaker.gameObject.activeInHierarchy) return true; } catch { }
            OpenMaker = null; return false;
        }

        static bool Read(int i, out XState s)
        {
            s = default;
            try { return (_use910 ? Get910(i, out s) : Get14(i, out s)) == 0; }
            catch (DllNotFoundException) { if (_use910) { _broken = true; return false; } _use910 = true; return Read(i, out s); }
            catch { _broken = true; return false; }
        }

        internal static void Tick()
        {
            if (!Enabled.Value || _broken) return;
            XState st = default;
            bool ok = _pad >= 0 && Read(_pad, out st);
            if (!ok && Time.unscaledTime > _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f; _pad = -1;
                for (int i = 0; i < 4; i++) if (Read(i, out st)) { _pad = i; ok = true; break; }
            }
            if (!ok) return;

            bool l3 = (st.buttons & L3) != 0, lt = st.lt > 140, rt = st.rt > 140;
            bool l3Down = l3 && !_l3, ltDown = lt && !_lt, rtDown = rt && !_rt;
            _l3 = l3; _lt = lt; _rt = rt;

            if (l3Down)
            {
                if (StorageSort.IsOpen()) StorageSort.SortOpen();
                else if (!MakerOpen()) Plugin.ToggleFreeze();
            }
            if (StorageSort.IsOpen())
            {
                if (rtDown) StorageSort.Page(+1);
                if (ltDown) StorageSort.Page(-1);
            }
            else if (MakerOpen())
            {
                if (rtDown) UniversalMakerPatch.CycleMode(+1);
                if (ltDown) UniversalMakerPatch.CycleMode(-1);
            }
        }
    }

    [HarmonyPatch(typeof(UIPageCommonInputBox), nameof(UIPageCommonInputBox.ShowPage))]
    static class MakerPageShown
    {
        static void Postfix(UIPageCommonInputBox __instance)
        {
            try { if (__instance.TryCast<UIPageCommonMaker>() != null) Pad.OpenMaker = __instance; } catch { }
        }
    }
}

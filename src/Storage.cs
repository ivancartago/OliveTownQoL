using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BokuMono;
using BokuMono.Data;
using HarmonyLib;
using UnityEngine;

namespace OliveTownQoL
{
    static class StorageConfig
    {
        internal static ConfigEntry<KeyCode> SortKey;
        internal static ConfigEntry<bool> AutoSortOnOpen;
        internal static ConfigEntry<int> BoxPages, FridgePages;
        internal static ConfigEntry<KeyCode> NextPageKey, PrevPageKey;

        internal static void Bind(ConfigFile c)
        {
            SortKey = c.Bind("Storage", "SortKey", KeyCode.F5, "With a storage box, fridge or the shipping box open, press this to merge stacks and sort.");
            AutoSortOnOpen = c.Bind("Storage", "AutoSortOnOpen", false, "Sort automatically every time you open a box or fridge.");
            BoxPages = c.Bind("Storage", "StorageBoxPages", 4, "Pages per storage box (each page = the normal 24 slots). 1 = game default. If you ever remove the mod, move items to page 1 first.");
            FridgePages = c.Bind("Storage", "FridgePages", 4, "Pages per fridge (each page = the normal 24 slots). 1 = game default.");
            NextPageKey = c.Bind("Storage", "NextPageKey", KeyCode.PageDown, "Next storage page (controller: RT).");
            PrevPageKey = c.Bind("Storage", "PrevPageKey", KeyCode.PageUp, "Previous storage page (controller: LT).");
        }
    }

    static class StorageSort
    {
        internal static UIPageCommonInventoryBox OpenPage;

        internal static bool IsOpen()
        {
            try { if (OpenPage != null && OpenPage.gameObject.activeInHierarchy) return true; } catch { }
            return false;
        }

        internal static void Tick()
        {
            try
            {
                if (OpenPage == null) return;
                bool alive;
                try { alive = OpenPage.gameObject != null && OpenPage.gameObject.activeInHierarchy; } catch { alive = false; }
                if (!alive) { OpenPage = null; return; }
                if (Input.GetKeyDown(StorageConfig.SortKey.Value)) SortOpen();
                if (Input.GetKeyDown(StorageConfig.NextPageKey.Value)) Page(+1);
                if (Input.GetKeyDown(StorageConfig.PrevPageKey.Value)) Page(-1);
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }

        internal static void SortOpen()
        {
            var page = OpenPage;
            if (page == null) return;
            var inv = page.currentInventoryBoxData;
            if (inv == null) { Plugin.L.LogWarning("Sort: no inventory on this page."); return; }
            int n = Sort(inv);
            try { page.ForceRefresh(); } catch (Exception e) { Plugin.L.LogWarning("Refresh failed: " + e.Message); }
            _pageNo[inv.Pointer] = 0;
            Toast.Show($"Sorted: {n} stacks");
        }

        internal static int PageSize = 24;
        static readonly System.Collections.Generic.Dictionary<IntPtr, int> _pageNo = new();

        // The UI only shows the first page; flipping pages rotates whole 24-slot blocks into view,
        // so the game's own slot numbers never change meaning.
        internal static void Page(int dir)
        {
            var page = OpenPage;
            if (page == null) return;
            var inv = page.currentInventoryBoxData;
            if (inv == null) return;
            int max = inv.CurrentMax(), size = PageSize, pages = max / size;
            if (pages <= 1) return;
            var cur = new ItemDataInInventory[max];
            for (int i = 0; i < max; i++) { try { var d = inv.GetInventoryData(i); if (d != null && d.ItemId != 0 && d.StackCount > 0) cur[i] = d; } catch { } }
            int shift = ((dir % pages) + pages) % pages * size;
            inv.ResetAllInventoryData();
            for (int i = 0; i < max; i++) { var d = cur[(i + shift) % max]; if (d != null) inv.SetInventoryData(i, d); }
            _pageNo.TryGetValue(inv.Pointer, out int n);
            n = ((n + dir) % pages + pages) % pages;
            _pageNo[inv.Pointer] = n;
            try { page.ForceRefresh(); } catch { }
            Toast.Show($"Page {n + 1} / {pages}", 1.5f);
        }

        // Merge identical stacks, then sort by category / item / quality (best first).
        internal static int Sort(InventoryDataBase inv)
        {
            int max = inv.CurrentMax();
            var items = new List<ItemDataInInventory>();
            for (int i = 0; i < max; i++)
            {
                ItemDataInInventory d = null;
                try { d = inv.GetInventoryData(i); } catch { }
                if (d != null && d.ItemId != 0 && d.StackCount > 0) items.Add(d);
            }
            if (items.Count == 0) return 0;

            var merged = new List<ItemDataInInventory>();
            foreach (var g in items.GroupBy(d => (d.ItemId, d.Quality, d.Color, d.IdentifiedItemId)))
            {
                var list = g.ToList();
                int stackMax = 999;
                try { var md = list[0].ItemMasterData; if (md != null && md.StackSize > 0) stackMax = md.StackSize; } catch { }
                int total = list.Sum(x => x.StackCount);
                foreach (var d in list)
                {
                    if (total <= 0) break;
                    int take = Math.Min(stackMax, total);
                    d.StackCount = take;
                    total -= take;
                    merged.Add(d);
                }
            }

            merged = merged
                .OrderBy(d => { try { return d.ItemMasterData?.Category ?? 0; } catch { return 0; } })
                .ThenBy(d => d.ItemId)
                .ThenByDescending(d => d.Quality)
                .ThenByDescending(d => d.StackCount)
                .ToList();

            inv.ResetAllInventoryData();
            for (int i = 0; i < merged.Count && i < max; i++) inv.SetInventoryData(i, merged[i]);
            return merged.Count;
        }
    }

    [HarmonyPatch]
    static class StoragePagePatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(UIPageCommonInventoryBox), nameof(UIPageCommonInventoryBox.ShowPage))]
        static void BoxShown(UIPageCommonInventoryBox __instance) => Opened(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(UIPageCommonRefrigerator), nameof(UIPageCommonRefrigerator.ShowPage))]
        static void FridgeShown(UIPageCommonRefrigerator __instance) => Opened(__instance);

        static void Opened(UIPageCommonInventoryBox page)
        {
            StorageSort.OpenPage = page;
            try { var inv = page.currentInventoryBoxData; if (inv != null) Plugin.L.LogInfo($"Opened {page.GetIl2CppType().Name}: slots {inv.CurrentMax()}/{inv.Max()}"); } catch { }
            if (StorageConfig.AutoSortOnOpen.Value)
                try { StorageSort.SortOpen(); } catch (Exception e) { Plugin.L.LogError(e); }
        }

        static int Pages(ConfigEntry<int> e) => Math.Max(1, Math.Min(20, e.Value));

        [HarmonyPostfix, HarmonyPatch(typeof(StorageBoxInventoryData), nameof(StorageBoxInventoryData.Max))]
        static void BoxMax(ref int __result) { StorageSort.PageSize = __result; __result *= Pages(StorageConfig.BoxPages); }

        [HarmonyPostfix, HarmonyPatch(typeof(StorageBoxInventoryData), nameof(StorageBoxInventoryData.CurrentMax))]
        static void BoxCurMax(ref int __result) { __result *= Pages(StorageConfig.BoxPages); }

        [HarmonyPostfix, HarmonyPatch(typeof(RefrigeratorData), nameof(RefrigeratorData.Max))]
        static void FridgeMax(ref int __result) { __result *= Pages(StorageConfig.FridgePages); }

        [HarmonyPostfix, HarmonyPatch(typeof(RefrigeratorData), nameof(RefrigeratorData.CurrentMax))]
        static void FridgeCurMax(ref int __result) { __result *= Pages(StorageConfig.FridgePages); }

        // Only the first page is drawn, so the box keeps its normal size on screen.
        [HarmonyPostfix, HarmonyPatch(typeof(UIPageCommonInventoryBox), nameof(UIPageCommonInventoryBox.ItemDataInInventoryBoxList))]
        static void OnlyFirstPage(UIPageCommonInventoryBox __instance, ref Il2CppSystem.Collections.Generic.List<ItemDataInBag> __result)
        {
            try
            {
                if (__result == null || __instance.IsShippingBox) return;
                int size = StorageSort.PageSize > 0 ? StorageSort.PageSize : 24;
                if (__result.Count <= size) return;
                var res = new Il2CppSystem.Collections.Generic.List<ItemDataInBag>();
                for (int i = 0; i < __result.Count && res.Count < size; i++)
                {
                    var d = __result[i];
                    if (d == null) continue;
                    if (d.Slot < size) res.Add(d);
                }
                if (res.Count == 0) for (int i = 0; i < size && i < __result.Count; i++) res.Add(__result[i]);
                __result = res;
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }
    }
}

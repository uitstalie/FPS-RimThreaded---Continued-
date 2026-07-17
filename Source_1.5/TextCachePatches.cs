using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Global text-metrics cache. Verse.Text.CalcHeight/CalcSize are called
    // hundreds of times per frame across the entire UI (vanilla and mods) and
    // always remeasure. The result only depends on (text, width, font, wordwrap),
    // so remember it. Zero visual change - it's the same math, cached.
    public struct TextKey
    {
        public string text;
        public float width;
        public byte font;
        public bool wrap;
    }

    public class TextKeyComparer : IEqualityComparer<TextKey>
    {
        public bool Equals(TextKey a, TextKey b)
        {
            return a.width == b.width && a.font == b.font && a.wrap == b.wrap && string.Equals(a.text, b.text);
        }

        public int GetHashCode(TextKey k)
        {
            int h = (k.text != null) ? k.text.GetHashCode() : 0;
            h = (h * 397) ^ k.width.GetHashCode();
            h = (h * 397) ^ (int)k.font;
            if (k.wrap)
            {
                h ^= 0x40000000;
            }
            return h;
        }
    }

    public static class TextCachePatches
    {
        private static readonly Dictionary<TextKey, float> heightCache = new Dictionary<TextKey, float>(new TextKeyComparer());
        private static readonly Dictionary<TextKey, Vector2> sizeCache = new Dictionary<TextKey, Vector2>(new TextKeyComparer());

        public static long Hits;

        public static bool CalcHeight_Prefix(string text, float width, ref float __result, out bool __state)
        {
            __state = false;
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.textCache || text == null)
            {
                return true;
            }
            if (ConflictGuard.SuppressText && !ConflictGuard.Ov)
            {
                return true;
            }
            TextKey k;
            k.text = text;
            k.width = width;
            k.font = (byte)Text.Font;
            k.wrap = Text.WordWrap;
            float h;
            if (heightCache.TryGetValue(k, out h))
            {
                __result = h;
                Hits++;
                __state = true;
                return false;
            }
            return true;
        }

        public static void CalcHeight_Postfix(string text, float width, float __result, bool __state)
        {
            if (__state)
            {
                return; // served from cache, nothing to store
            }
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.textCache || text == null)
            {
                return;
            }
            if (ConflictGuard.SuppressText && !ConflictGuard.Ov)
            {
                return;
            }
            if (heightCache.Count > 8192)
            {
                heightCache.Clear();
            }
            TextKey k;
            k.text = text;
            k.width = width;
            k.font = (byte)Text.Font;
            k.wrap = Text.WordWrap;
            heightCache[k] = __result;
        }

        public static bool CalcSize_Prefix(string text, ref Vector2 __result, out bool __state)
        {
            __state = false;
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.textCache || text == null)
            {
                return true;
            }
            if (ConflictGuard.SuppressText && !ConflictGuard.Ov)
            {
                return true;
            }
            TextKey k;
            k.text = text;
            k.width = 0f;
            k.font = (byte)Text.Font;
            k.wrap = Text.WordWrap;
            Vector2 v;
            if (sizeCache.TryGetValue(k, out v))
            {
                __result = v;
                Hits++;
                __state = true;
                return false;
            }
            return true;
        }

        public static void CalcSize_Postfix(string text, Vector2 __result, bool __state)
        {
            if (__state)
            {
                return;
            }
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.textCache || text == null)
            {
                return;
            }
            if (ConflictGuard.SuppressText && !ConflictGuard.Ov)
            {
                return;
            }
            if (sizeCache.Count > 8192)
            {
                sizeCache.Clear();
            }
            TextKey k;
            k.text = text;
            k.width = 0f;
            k.font = (byte)Text.Font;
            k.wrap = Text.WordWrap;
            sizeCache[k] = __result;
        }

        public static void ClearCaches()
        {
            heightCache.Clear();
            sizeCache.Clear();
        }
    }
}

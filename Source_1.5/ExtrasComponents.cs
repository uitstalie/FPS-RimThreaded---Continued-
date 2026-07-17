using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Rolling 60-second FPS/TPS history, sampled once per second. Feeds the
    // on-screen graphs and the graph on the settings Main page.
    public static class PerfHistory
    {
        public const int Samples = 60;
        public static readonly float[] Fps = new float[Samples];
        public static readonly float[] Tps = new float[Samples];
        public static int Head;
        public static int Count;
        private static float lastSample = -1f;

        public static void Push(float fps, float tps, float now)
        {
            if (lastSample >= 0f && now - lastSample < 1f)
            {
                return;
            }
            lastSample = now;
            Fps[Head] = fps;
            Tps[Head] = tps;
            Head = (Head + 1) % Samples;
            if (Count < Samples)
            {
                Count++;
            }
        }

        // Line graph of one ring buffer, oldest sample on the left.
        public static void DrawGraph(Rect rect, float[] ring, Color lineColor, string label)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            if (Count < 2)
            {
                return;
            }
            float max = 1f;
            for (int i = 0; i < Count; i++)
            {
                if (ring[i] > max)
                {
                    max = ring[i];
                }
            }
            max *= 1.1f;
            Rect plot = rect.ContractedBy(3f);
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < Count; i++)
            {
                int idx = (Head - Count + i + Samples) % Samples;
                float x = plot.x + plot.width * i / (Samples - 1);
                float y = plot.yMax - Mathf.Clamp01(ring[idx] / max) * plot.height;
                Vector2 pt = new Vector2(x, y);
                if (i > 0)
                {
                    Widgets.DrawLine(prev, pt, lineColor, 1.5f);
                }
                prev = pt;
            }
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(rect.x + 5f, rect.y + 2f, rect.width - 10f, 18f), label);
            GUI.color = Color.white;
            Text.Font = oldFont;
        }
    }

    // On-screen FPS / TPS counter and graphs. Numbers and graphs each have
    // their own on/off switch, corner is selectable in settings. Draw-only.
    public class FPSPlusCounterOverlay : GameComponent
    {
        private float fpsEma;
        private int lastTicks;
        private float lastReal = -1f;
        private float tpsShown;
        private Rect lastBlockRect;
        private bool dragging;
        private Vector2 dragOffset;

        public FPSPlusCounterOverlay(Game game)
        {
        }

        // Drag the whole overlay block with the mouse; the position is saved
        // as a fraction of the screen so it survives resolution changes.
        private void HandleDrag(FPSPlusSettings s)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && lastBlockRect.width > 0f && lastBlockRect.Contains(e.mousePosition))
            {
                dragging = true;
                dragOffset = e.mousePosition - new Vector2(lastBlockRect.x, lastBlockRect.y);
                e.Use();
            }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                float nx = e.mousePosition.x - dragOffset.x;
                float ny = e.mousePosition.y - dragOffset.y;
                nx = Mathf.Clamp(nx, 0f, UI.screenWidth - lastBlockRect.width);
                ny = Mathf.Clamp(ny, 0f, UI.screenHeight - lastBlockRect.height);
                s.overlayX = UI.screenWidth - lastBlockRect.width > 0f ? nx / (UI.screenWidth - lastBlockRect.width) : 0f;
                s.overlayY = UI.screenHeight - lastBlockRect.height > 0f ? ny / (UI.screenHeight - lastBlockRect.height) : 0f;
                e.Use();
            }
            else if (dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                dragging = false;
                e.Use();
                try
                {
#if TTR_MERGED
                    Mod mod = LoadedModManager.GetMod(typeof(RimThreadedTTR.TTRMod));
                    if (mod != null)
                    {
                        mod.WriteSettings();
                    }
#endif
                }
                catch (Exception)
                {
                }
            }
        }

        public override void GameComponentOnGUI()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null)
            {
                return;
            }
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            if ((s.showFpsCounter || s.showTpsCounter || s.showFpsGraph || s.showTpsGraph) && Event.current.type != EventType.Repaint)
            {
                HandleDrag(s);
                return;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // always sample (cheap) so the settings-page graph has data even
            // while the overlay itself is hidden
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                float fps = 1f / dt;
                fpsEma = fpsEma <= 0f ? fps : fpsEma * 0.95f + fps * 0.05f;
            }
            TickManager tm = Find.TickManager;
            float now = Time.realtimeSinceStartup;
            if (tm != null)
            {
                if (lastReal < 0f)
                {
                    lastReal = now;
                    lastTicks = tm.TicksGame;
                }
                else if (now - lastReal >= 0.5f)
                {
                    tpsShown = (tm.TicksGame - lastTicks) / (now - lastReal);
                    lastTicks = tm.TicksGame;
                    lastReal = now;
                }
            }
            PerfHistory.Push(fpsEma, tpsShown, now);

            bool anyText = s.showFpsCounter || s.showTpsCounter;
            bool anyGraph = s.showFpsGraph || s.showTpsGraph;
            if (!anyText && !anyGraph)
            {
                return;
            }

            bool right = s.counterCorner == 1 || s.counterCorner == 3;
            bool bottom = s.counterCorner >= 2;
            float pad = 10f;
            float graphW = 170f;
            float graphH = 46f;
            float blockW = anyGraph ? graphW : 0f;

            string txt = "";
            if (s.showFpsCounter)
            {
                txt = "FPS " + (int)fpsEma;
            }
            if (s.showTpsCounter)
            {
                if (txt.Length > 0)
                {
                    txt += "   ";
                }
                txt += "TPS " + (int)tpsShown;
            }
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 txtSize = anyText ? Text.CalcSize(txt) : Vector2.zero;
            if (anyText && txtSize.x > blockW)
            {
                blockW = txtSize.x;
            }

            float blockH = 0f;
            if (anyText)
            {
                blockH += txtSize.y + 6f;
            }
            if (s.showFpsGraph)
            {
                blockH += graphH + 4f;
            }
            if (s.showTpsGraph)
            {
                blockH += graphH + 4f;
            }

            float x;
            float y;
            if (s.overlayX >= 0f && s.overlayY >= 0f)
            {
                // custom dragged position (stored as screen fraction)
                x = s.overlayX * (UI.screenWidth - blockW);
                y = s.overlayY * (UI.screenHeight - blockH);
            }
            else
            {
                x = right ? UI.screenWidth - blockW - pad : pad;
                y = bottom ? UI.screenHeight - blockH - pad : pad;
            }
            lastBlockRect = new Rect(x - 5f, y - 3f, blockW + 10f, blockH + 6f);
            TooltipHandler.TipRegion(lastBlockRect, SettingsUI.T("FPP_DragToMove", "Drag with the mouse to move this anywhere."));

            if (anyText)
            {
                Rect r = new Rect(x, y, txtSize.x, txtSize.y);
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(r.x - 5f, r.y - 3f, r.width + 10f, r.height + 6f), BaseContent.WhiteTex);
                GUI.color = Color.white;
                Widgets.Label(r, txt);
                y += txtSize.y + 6f;
            }
            if (s.showFpsGraph)
            {
                PerfHistory.DrawGraph(new Rect(x, y, graphW, graphH), PerfHistory.Fps, new Color(0.35f, 0.86f, 0.31f), "FPS 60s");
                y += graphH + 4f;
            }
            if (s.showTpsGraph)
            {
                PerfHistory.DrawGraph(new Rect(x, y, graphW, graphH), PerfHistory.Tps, new Color(0.24f, 0.59f, 0.96f), "TPS 60s");
            }
            Text.Font = oldFont;
        }
    }

    // One-time welcome letter pointing new players at the Recommended button,
    // so the mod gets set up right without reading anything else.
    public class FPSPlusWelcome : GameComponent
    {
        public FPSPlusWelcome(Game game)
        {
        }

        public override void FinalizeInit()
        {
            if (FPSPlusInit.StandaloneActive)
            {
                return;
            }
            FPSPlusSettings s = FPSPlusMod.Raw;
            if (s == null || s.welcomeShown)
            {
                return;
            }
            s.welcomeShown = true;
            try
            {
                Find.LetterStack.ReceiveLetter(SettingsUI.T("FPP_WelcomeTitle", "FPS+ is working"),
                    SettingsUI.T("FPP_WelcomeText", "FPS+ | RimThreaded is active and already boosting your game.\n\nWant the best setup in one click? Open Options > Mod settings > FPS+ | RimThreaded and press Recommended.\n\nEverything can be turned off anytime with the master switch on the Main page."),
                    LetterDefOf.PositiveEvent, (LookTargets)null, null, null, null, null, 0, true);
            }
            catch (Exception)
            {
                // a letter is nice-to-have, never worth an error
            }
        }
    }

    // AFK saver. When the game window loses focus, nobody is watching the
    // hundreds of frames per second being drawn - so we cap rendering at
    // 15 fps until focus returns. The colony keeps running; only the
    // drawing rests. Restores the exact previous limit on return.
    public class FPSPlusAfkSaver : GameComponent
    {
        private bool limited;
        private int savedRate;
        private float unfocusedSince = -1f;

        public FPSPlusAfkSaver(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            bool wantLimit = false;
            if (s != null && s.afkSaver && !Application.isFocused)
            {
                float now = Time.realtimeSinceStartup;
                if (unfocusedSince < 0f)
                {
                    unfocusedSince = now;
                }
                // 3s grace so quick alt-tabs don't flicker the limit
                wantLimit = now - unfocusedSince >= 3f;
            }
            else
            {
                unfocusedSince = -1f;
            }

            if (wantLimit && !limited)
            {
                savedRate = Application.targetFrameRate;
                Application.targetFrameRate = 15;
                limited = true;
            }
            else if (!wantLimit && limited)
            {
                Application.targetFrameRate = savedRate;
                limited = false;
            }
        }
    }
}

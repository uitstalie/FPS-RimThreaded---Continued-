using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace FPSPlus
{
    // Vanilla draws cell designations (mining plans) GPU-instanced, but every
    // thing-targeting designation (haul, chop, hunt, "haul urgently"...) gets
    // its own Graphics.DrawMesh call every frame, after scanning the FULL
    // designation list for on-screen ones. We cache the on-screen scan (refresh
    // on camera move / list change / every 10 frames) and draw plain
    // designations instanced: one draw call per icon type instead of one per
    // icon. Positions are recomputed every frame, so icons follow moving
    // targets (hunted animals etc.) exactly like vanilla. Designations with a
    // custom color or a custom subclass keep their vanilla draw path.
    public static class DesignationPatches
    {
        private static readonly MethodInfo CalcCellMatrices = AccessTools.Method(typeof(DesignationManager), "CalculateCellDesignationDrawMatricies");
        private static readonly FieldInfo CellMatricesField = AccessTools.Field(typeof(DesignationManager), "cellDesignationDrawMatricies");

        private static MaterialPropertyBlock propertyBlock;

        // on-screen scan cache
        private static DesignationManager cachedManager;
        private static CellRect cachedRect;
        private static int cachedCount = -1;
        private static int cacheFrame = -1000000;
        private static readonly Dictionary<DesignationDef, List<Designation>> visibleBatchable = new Dictionary<DesignationDef, List<Designation>>();
        private static readonly List<Designation> visibleSpecial = new List<Designation>();

        private static Matrix4x4[] matrixBuffer = new Matrix4x4[256];

        public static long InstancedDrawCalls;
        public static long IndividualDraws;

        public static bool DrawDesignations_Prefix(DesignationManager __instance)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.designationBatch)
            {
                return true;
            }
            if (ConflictGuard.SuppressDesignations && !ConflictGuard.Ov)
            {
                return true;
            }
            if (!SystemInfo.supportsInstancing || CalcCellMatrices == null || CellMatricesField == null)
            {
                return true;
            }
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            CellRect viewRect = Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
            List<DesignationDef> defs = DefDatabase<DesignationDef>.AllDefsListForReading;
            DefMap<DesignationDef, List<Designation>> byDef = __instance.designationsByDef;

            // ---- vanilla's own instanced path for batchable cell designations,
            // replicated 1:1 (vanilla keeps these matrices cached + dirty-flagged) ----
            for (int i = 0; i < defs.Count; i++)
            {
                DesignationDef def = defs[i];
                if (def.targetType != TargetType.Cell || !def.shouldBatchDraw)
                {
                    continue;
                }
                List<Designation> list = byDef[def];
                if (list == null || list.Count == 0)
                {
                    continue;
                }
                CalcCellMatrices.Invoke(__instance, new object[] { def });
                DefMap<DesignationDef, List<Matrix4x4[]>> cellMats = CellMatricesField.GetValue(__instance) as DefMap<DesignationDef, List<Matrix4x4[]>>;
                if (cellMats == null)
                {
                    return true; // unexpected shape - let vanilla handle everything
                }
                List<Matrix4x4[]> mats = cellMats[def];
                if (mats == null)
                {
                    continue;
                }
                def.iconMat.enableInstancing = true;
                int count = list.Count;
                int full = count / 1023;
                for (int j = 0; j < full; j++)
                {
                    Graphics.DrawMeshInstanced(MeshPool.plane10, 0, def.iconMat, mats[j], 1023, propertyBlock, ShadowCastingMode.Off, true, 0);
                }
                int rem = count % 1023;
                if (rem > 0)
                {
                    Graphics.DrawMeshInstanced(MeshPool.plane10, 0, def.iconMat, mats[full], rem, propertyBlock, ShadowCastingMode.Off, true, 0);
                }
            }

            // ---- our cached scan for everything else ----
            int totalOther = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                DesignationDef def = defs[i];
                if (def.targetType == TargetType.Cell && def.shouldBatchDraw)
                {
                    continue;
                }
                List<Designation> list = byDef[def];
                if (list != null)
                {
                    totalOther += list.Count;
                }
            }

            int frame = Time.frameCount;
            bool refresh = __instance != cachedManager
                || viewRect.minX != cachedRect.minX || viewRect.maxX != cachedRect.maxX
                || viewRect.minZ != cachedRect.minZ || viewRect.maxZ != cachedRect.maxZ
                || totalOther != cachedCount
                || frame - cacheFrame >= 10;
            if (refresh)
            {
                cachedManager = __instance;
                cachedRect = viewRect;
                cachedCount = totalOther;
                cacheFrame = frame;
                foreach (KeyValuePair<DesignationDef, List<Designation>> kv in visibleBatchable)
                {
                    kv.Value.Clear();
                }
                visibleSpecial.Clear();
                Map map = __instance.map;
                for (int i = 0; i < defs.Count; i++)
                {
                    DesignationDef def = defs[i];
                    if (def.targetType == TargetType.Cell && def.shouldBatchDraw)
                    {
                        continue;
                    }
                    List<Designation> list = byDef[def];
                    if (list == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < list.Count; j++)
                    {
                        Designation d = list[j];
                        if (d.target.HasThing && d.target.Thing.Map != map)
                        {
                            continue; // vanilla skips these too
                        }
                        if (!viewRect.Contains(d.target.Cell))
                        {
                            continue;
                        }
                        if (d.colorDef == null && d.GetType() == typeof(Designation))
                        {
                            List<Designation> vb;
                            if (!visibleBatchable.TryGetValue(def, out vb))
                            {
                                vb = new List<Designation>();
                                visibleBatchable[def] = vb;
                            }
                            vb.Add(d);
                        }
                        else
                        {
                            visibleSpecial.Add(d); // custom color/subclass: vanilla path
                        }
                    }
                }
            }

            // instanced draw, positions rebuilt fresh every frame
            foreach (KeyValuePair<DesignationDef, List<Designation>> kv in visibleBatchable)
            {
                List<Designation> vb = kv.Value;
                if (vb.Count == 0)
                {
                    continue;
                }
                Material mat = kv.Key.iconMat;
                mat.enableInstancing = true;
                if (matrixBuffer.Length < vb.Count)
                {
                    matrixBuffer = new Matrix4x4[Mathf.NextPowerOfTwo(vb.Count)];
                }
                int n = 0;
                for (int j = 0; j < vb.Count; j++)
                {
                    Designation d = vb[j];
                    if (d.designationManager == null || (d.target.HasThing && !d.target.Thing.Spawned))
                    {
                        continue; // removed or despawned since last refresh
                    }
                    matrixBuffer[n++] = Matrix4x4.TRS(d.DrawLoc(), Quaternion.identity, Vector3.one);
                    if (n == 1023)
                    {
                        Graphics.DrawMeshInstanced(MeshPool.plane10, 0, mat, matrixBuffer, n, propertyBlock, ShadowCastingMode.Off, true, 0);
                        InstancedDrawCalls++;
                        n = 0;
                    }
                }
                if (n > 0)
                {
                    Graphics.DrawMeshInstanced(MeshPool.plane10, 0, mat, matrixBuffer, n, propertyBlock, ShadowCastingMode.Off, true, 0);
                    InstancedDrawCalls++;
                }
            }
            for (int i = 0; i < visibleSpecial.Count; i++)
            {
                Designation d = visibleSpecial[i];
                if (d.designationManager != null)
                {
                    d.DesignationDraw();
                    IndividualDraws++;
                }
            }
            return false;
        }

        public static void ClearCache()
        {
            cachedManager = null;
            cachedCount = -1;
            foreach (KeyValuePair<DesignationDef, List<Designation>> kv in visibleBatchable)
            {
                kv.Value.Clear();
            }
            visibleSpecial.Clear();
        }
    }
}

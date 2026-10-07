using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RimWorld.IO;
using Verse;
using UnityEngine;
using Verse.AI;

namespace FastLoad
{
    /// <summary>
    /// 全部补丁方法：**必须是显式静态方法**。
    ///
    /// 教训（2026-10-02）：早先版本用 lambda（`(Action)(() => ...)`）当补丁方法，
    /// lambda 捕获了局部变量 ⇒ 编译成**闭包实例方法**（`&lt;&gt;c__DisplayClass…`），
    /// 而 Harmony 只拿到 `prefix.Method`、没有实例 ⇒ 生成出的调用目标是无表位 token，
    /// Mono JIT 直接报 `Invalid IL code in …LoadDefs_Patch1`，补丁全部安装失败。
    /// 静态方法没有这个问题。
    /// </summary>
    public static class Patches
    {
        internal static string Key(ModContentPack mod)
        {
            if (mod == null) return null;
            try
            {
                if (!string.IsNullOrEmpty(mod.PackageId)) return mod.PackageId;
            }
            catch
            {
                // 忽略：拿不到 PackageId 就退回 Name
            }
            return mod.Name;
        }

        // ---------- 总流程 ----------
        public static void LoadAllPlayData_Begin() { Profiler.PhaseBegin("整段加载 PlayDataLoader.LoadAllPlayData"); }
        public static void LoadAllPlayData_End() { Profiler.PhaseEnd("整段加载 PlayDataLoader.LoadAllPlayData"); }

        public static void DoPlayLoad_Begin() { Profiler.PhaseBegin("加载主流程 DoPlayLoad"); }
        public static void DoPlayLoad_End() { Profiler.PhaseEnd("加载主流程 DoPlayLoad"); }

        public static void LoadAllActiveMods_Begin() { Profiler.PhaseBegin("mod 加载 LoadAllActiveMods"); }
        public static void LoadAllActiveMods_End() { Profiler.PhaseEnd("mod 加载 LoadAllActiveMods"); }

        public static void LoadModContent_Begin() { Profiler.PhaseBegin("mod 内容 LoadModContent"); }
        public static void LoadModContent_End() { Profiler.PhaseEnd("mod 内容 LoadModContent"); }

        public static void CreateModClasses_Begin() { Profiler.PhaseBegin("创建 mod 类 CreateModClasses"); }
        public static void CreateModClasses_End() { Profiler.PhaseEnd("创建 mod 类 CreateModClasses"); }

        public static void InitializeMods_Begin() { Profiler.PhaseBegin("初始化 mods InitializeMods"); }
        public static void InitializeMods_End() { Profiler.PhaseEnd("初始化 mods InitializeMods"); }

        public static void LoadModXML_Begin() { Profiler.PhaseBegin("载入 mod XML LoadModXML"); }
        public static void LoadModXML_End() { Profiler.PhaseEnd("载入 mod XML LoadModXML"); }

        public static void ApplyPatches_Begin() { Profiler.PhaseBegin("套用 XML 补丁 ApplyPatches"); }
        public static void ApplyPatches_End() { Profiler.PhaseEnd("套用 XML 补丁 ApplyPatches"); }

        public static void ParseAndProcessXML_Begin() { Profiler.PhaseBegin("解析 XML ParseAndProcessXML"); }
        public static void ParseAndProcessXML_End() { Profiler.PhaseEnd("解析 XML ParseAndProcessXML"); }

        public static void XmlInheritance_Begin() { Profiler.PhaseBegin("XML 继承解析 XmlInheritance"); }
        public static void XmlInheritance_End() { Profiler.PhaseEnd("XML 继承解析 XmlInheritance"); }

        public static void InjectIntoData_Begin() { Profiler.PhaseBegin("语言注入 LoadedLanguage"); }
        public static void InjectIntoData_End() { Profiler.PhaseEnd("语言注入 LoadedLanguage"); }

        // ---------- 程序集 ----------
        public static void ReloadAll_Begin() { Profiler.PhaseBegin("程序集重载 ModAssemblyHandler.ReloadAll"); }
        public static void ReloadAll_End() { Profiler.PhaseEnd("程序集重载 ModAssemblyHandler.ReloadAll"); }

        public static void RebuildModList_Begin() { Profiler.PhaseBegin("扫描 mod 列表 RebuildModList"); }
        public static void RebuildModList_End() { Profiler.PhaseEnd("扫描 mod 列表 RebuildModList"); }

        public static void AllGraphicsLoaded_Begin() { Profiler.Mark("AllGraphicsLoaded 开始"); }
        public static void AllGraphicsLoaded_End()
        {
            Profiler.Mark("AllGraphicsLoaded 结束（图形/贴图阶段完成）");
            Profiler.Finish("图形阶段结束");
        }

        // ---------- DoPlayLoad 里余下的重阶段（IL 反编译得出） ----------
        public static void CrossRef_Begin() { Profiler.PhaseBegin("跨引用解析 ResolveAllWantedCrossReferences"); }
        public static void CrossRef_End() { Profiler.PhaseEnd("跨引用解析 ResolveAllWantedCrossReferences"); }

        public static void CrossRefClear_Begin() { Profiler.PhaseBegin("引用表清理 DirectXmlCrossRefLoader.Clear"); }
        public static void CrossRefClear_End() { Profiler.PhaseEnd("引用表清理 DirectXmlCrossRefLoader.Clear"); }

        public static void RebindDefOfs_Begin() { Profiler.PhaseBegin("DefOf 重绑 DefOfHelper.RebindAllDefOfs"); }
        public static void RebindDefOfs_End() { Profiler.PhaseEnd("DefOf 重绑 DefOfHelper.RebindAllDefOfs"); }

        public static void TKeyMappings_Begin() { Profiler.PhaseBegin("翻译键映射 TKeySystem.BuildMappings"); }
        public static void TKeyMappings_End() { Profiler.PhaseEnd("翻译键映射 TKeySystem.BuildMappings"); }

        public static void Backstory_Begin() { Profiler.PhaseBegin("背景故事注入 BackstoryTranslationUtility"); }
        public static void Backstory_End() { Profiler.PhaseEnd("背景故事注入 BackstoryTranslationUtility"); }

        public static void ImpliedDefs_Begin() { Profiler.PhaseBegin("隐含 def 生成 GenerateImpliedDefs_PreResolve"); }
        public static void ImpliedDefs_End() { Profiler.PhaseEnd("隐含 def 生成 GenerateImpliedDefs_PreResolve"); }

        public static void LangMeta_Begin() { Profiler.PhaseBegin("语言元数据 LanguageDatabase.InitAllMetadata"); }
        public static void LangMeta_End() { Profiler.PhaseEnd("语言元数据 LanguageDatabase.InitAllMetadata"); }

        public static void ColoredText_Begin() { Profiler.PhaseBegin("富文本静态数据 ColoredText.ResetStaticData"); }
        public static void ColoredText_End() { Profiler.PhaseEnd("富文本静态数据 ColoredText.ResetStaticData"); }

        // ---------- 补丁应用：按 mod 归因（PatchOperation.sourceFile） ----------
        public static void PatchApply_Begin(PatchOperation __instance) { Profiler.PatchBegin(__instance); }
        public static void PatchApply_End(PatchOperation __instance) { Profiler.PatchEnd(__instance); }

        // ---------- 每个 mod ----------
        public static void ReloadContent_Begin(ModContentPack __instance) { Profiler.ModBegin(Key(__instance), 2); }
        public static void ReloadContent_End(ModContentPack __instance)
        {
            Profiler.ModEnd(Key(__instance), 2);
            Profiler.Mark("mod 内容: " + Key(__instance));
        }

        public static void LoadPatches_Begin(ModContentPack __instance) { Profiler.ModBegin(Key(__instance), 1); }
        public static void LoadPatches_End(ModContentPack __instance) { Profiler.ModEnd(Key(__instance), 1); }

        // ---------- +11→+125s 区间：贴图/图集与逐文件 XML ----------
        public static void AtlasBuild_Begin() { Profiler.PhaseBegin("图集构建 GlobalTextureAtlasManager"); }
        public static void AtlasBuild_End() { Profiler.PhaseEnd("图集构建 GlobalTextureAtlasManager"); }

        public static void XmlLoad_Begin() { Profiler.PhaseBegin("逐文件 XML 载入 DirectXmlLoader"); }
        public static void XmlLoad_End() { Profiler.PhaseEnd("逐文件 XML 载入 DirectXmlLoader"); }

        public static void GraphicInit_Begin() { Profiler.PhaseBegin("图形库初始化 GraphicDatabase"); }
        public static void GraphicInit_End() { Profiler.PhaseEnd("图形库初始化 GraphicDatabase"); }

        public static void CrossRefWanted_Begin() { Profiler.PhaseBegin("引用登记 DirectXmlCrossRefLoader 注册"); }
        public static void CrossRefWanted_End() { Profiler.PhaseEnd("引用登记 DirectXmlCrossRefLoader 注册"); }

        // ---------- 已确认签名：贴图/音频/Shader 逐文件（80 秒主嫌） ----------
        public static void LoadItem_Begin(VirtualFile __0) { Profiler.TextureFileBegin(__0); }
        public static void LoadItem_End() { Profiler.TextureFileEnd(); }

        public static void ContentFinderGet_Begin() { Profiler.Count("ContentFinder<Texture2D>.Get 调用", 0, 0); }
        public static void ContentFinderGet_End() { }

        // ---------- 图集 / 图形库 ----------
        public static void BakeAtlases_Begin() { Profiler.PhaseBegin("图集烘焙 BakeStaticAtlases"); }
        public static void BakeAtlases_End() { Profiler.PhaseEnd("图集烘焙 BakeStaticAtlases"); }
        public static void ClearAtlasQueue_Begin() { Profiler.PhaseBegin("清空图集队列"); }
        public static void ClearAtlasQueue_End() { Profiler.PhaseEnd("清空图集队列"); }
        public static void GraphicDbClear_Begin() { Profiler.PhaseBegin("GraphicDatabase.Clear"); }
        public static void GraphicDbClear_End() { Profiler.PhaseEnd("GraphicDatabase.Clear"); }
        public static void GraphicSingleInit_Begin() { Profiler.PhaseBegin("Graphic_Single.Init（贴图对象创建）"); }
        public static void GraphicSingleInit_End() { Profiler.PhaseEnd("Graphic_Single.Init（贴图对象创建）"); }

        // ---------- 之前漏掉的阶段 ----------
        public static void InjectBefore_Begin() { Profiler.PhaseBegin("语言注入（BeforeImpliedDefs）"); }
        public static void InjectBefore_End() { Profiler.PhaseEnd("语言注入（BeforeImpliedDefs）"); }
        public static void ShortHash_Begin() { Profiler.PhaseBegin("短哈希 ShortHashGiver"); }
        public static void ShortHash_End() { Profiler.PhaseEnd("短哈希 ShortHashGiver"); }
        public static void XmlAssets_Begin() { Profiler.PhaseBegin("XML 逐 mod 读取 XmlAssetsInModFolder"); }
        public static void XmlAssets_End() { Profiler.PhaseEnd("XML 逐 mod 读取 XmlAssetsInModFolder"); }
        public static void ImpliedDefsPost_Begin() { Profiler.PhaseBegin("隐含 def（PostResolve）"); }
        public static void ImpliedDefsPost_End() { Profiler.PhaseEnd("隐含 def（PostResolve）"); }
        public static void ResetPre_Begin() { Profiler.PhaseBegin("ResetStaticDataPre"); }
        public static void ResetPre_End() { Profiler.PhaseEnd("ResetStaticDataPre"); }
        public static void ResetPost_Begin() { Profiler.PhaseBegin("ResetStaticDataPost"); }
        public static void ResetPost_End() { Profiler.PhaseEnd("ResetStaticDataPost"); }
        public static void Bios_Begin() { Profiler.PhaseBegin("生物背景 SolidBioDatabase.LoadAllBios"); }
        public static void Bios_End() { Profiler.PhaseEnd("生物背景 SolidBioDatabase.LoadAllBios"); }
        public static void KeyPrefs_Begin() { Profiler.PhaseBegin("KeyPrefs.Init"); }
        public static void KeyPrefs_End() { Profiler.PhaseEnd("KeyPrefs.Init"); }

        // ---------- 反序列化的 Reflection.Emit 成本 ----------
        public static void EmitGet_Begin() { Profiler.Count("DirectXmlToObjectNew Get*（调用次数）", 0, 0); }
        public static void EmitGet_End() { }
        public static void EmitCreate_Begin() { Profiler.Count("DirectXmlToObjectNew Create*（缓存未命中）", 0, 0); }
        public static void EmitCreate_End() { }

        // ---------- 贴图/文件读取（**只走非泛型入口**，避免 Mono 泛型代码共享） ----------
        public static void ReadBytes_Begin(VirtualFile __instance)
        {
            Profiler.FileCounterPrefix("VirtualFile.ReadAllBytes ");
            Profiler.TextureFileBegin(__instance);
        }
        public static void ReadBytes_End() { Profiler.TextureFileEnd(); }

        public static void XmlRead_Begin(FileInfo __0)
        {
            Profiler.FileCounterPrefix("XML 读取 ");
            long length = 0;
            string path = null;
            try
            {
                if (__0 != null)
                {
                    length = __0.Length;
                    path = __0.FullName;
                }
            }
            catch
            {
                length = 0;
            }
            Profiler.TextureFileBeginPath(path, length);
        }

        public static void XmlRead_End() { Profiler.TextureFileEnd(); }

        public static void DdsDecode_Begin(VirtualFile __0)
        {
            Profiler.FileCounterPrefix("DDS 解码 CreateTexture ");
            Profiler.TextureFileBegin(__0);
        }

        public static void DdsDecode_End() { Profiler.TextureFileEnd(); }

        public static void Mmap_Begin(FileInfo __0)
        {
            Profiler.FileCounterPrefix("mmap 读取 ");
            long length = 0;
            string path = null;
            try
            {
                if (__0 != null)
                {
                    length = __0.Length;
                    path = __0.FullName;
                }
            }
            catch
            {
                length = 0;
            }
            Profiler.TextureFileBeginPath(path, length);
        }

        public static void Mmap_End() { Profiler.TextureFileEnd(); }

        public static void Dds_Begin(VirtualFile __0)
        {
            Profiler.FileCounterPrefix("ModDdsLoader.TryLoadDds ");
            Profiler.TextureFileBegin(__0);
        }
        public static void Dds_End() { Profiler.TextureFileEnd(); }

        [ThreadStatic] private static float _gfxStart;
        public static void GraphicSingleCount_Begin() { _gfxStart = Profiler.Now; }
        public static void GraphicSingleCount_End()
        {
            Profiler.Count("Graphic_Single.Init（贴图对象创建）", (Profiler.Now - _gfxStart) * 1000.0, 0);
        }

        // ---------- 运行时归因（高频；默认关，见设置 runtimeProfiling） ----------
        public static void RtTickUpdate_Begin()
        {
            RuntimeProfiler.Diag("TickManagerUpdate");
            RuntimeProfiler.CountFrame();
            RuntimeProfiler.Enter(null, "TickManager.TickManagerUpdate（每帧）");
        }

        public static void RtDoSingleTick_Begin()
        {
            RuntimeProfiler.Diag("DoSingleTick");
            RuntimeProfiler.CountTick();
            RuntimeProfiler.Enter(null, "TickManager.DoSingleTick（每 tick）");
        }

        public static void RtMapUpdate_Begin(Map __instance)
        {
            RuntimeProfiler.Diag("RtMapUpdate_Begin");
            RuntimeProfiler.Enter(__instance, null);
        }
        public static void RtComponent_Begin(object __instance)
        {
            RuntimeProfiler.Diag("RtComponent_Begin");
            RuntimeProfiler.Enter(__instance, null);
        }
        public static void RtJobDriver_Begin(JobDriver __instance)
        {
            RuntimeProfiler.Diag("RtJobDriver_Begin");
            RuntimeProfiler.Enter(__instance, null);
        }
        public static void RtJobTracker_Begin(Pawn_JobTracker __instance)
        {
            RuntimeProfiler.Diag("RtJobTracker_Begin");
            RuntimeProfiler.Enter(__instance, null);
        }

        private static float _lastForceSpeed;
        private static System.Reflection.FieldInfo _speedField;
        private static bool _speedFieldLookedUp;

        /// <summary>测试用：每秒把时间档强制到 3 档（Superfast）。我们把 Smart Speed 的
        /// superfast 档设为 15x ⇒ 该档 = 900 TPS。用 Superfast 而不是 Ultrafast，
        /// 因为 Ultrafast 在原版里需要 Dev Mode 才允许（实测被 setter 拒绝）。</summary>
        public static void ForceSpeed_Postfix()
        {
            // 2026-10-07：自动化也可以只靠 /tmp 开关文件强制档位（不改用户设置）——
            // A/B 必须跑在同一速度档，否则帧探针数据没有可比性。
            if (!FastLoadMod.Settings.forceUltrafast)
            {
                try { if (!System.IO.File.Exists("/tmp/ttr-force-superfast")) return; }
                catch { return; }
            }
            try
            {
                TickManager tm = Find.TickManager;
                if (tm == null) return;
                float now = Time.realtimeSinceStartup;
                if (now - _lastForceSpeed < 1f) return;      // 每秒最多一次，避免刷屏
                _lastForceSpeed = now;
                // 直接写私有字段：绕开 set_CurTimeSpeed 的 PlayerCanControl 检查
                // （信件/对话框打开时属性 setter 会拒绝，导致压测中途"停止 tick"）。
                if (!_speedFieldLookedUp)
                {
                    _speedFieldLookedUp = true;
                    _speedField = HarmonyLib.AccessTools.Field(typeof(TickManager), "curTimeSpeed")
                        ?? HarmonyLib.AccessTools.Field(typeof(TickManager), "curTimeSpeedInt");
                }
                if (_speedField != null)
                {
                    if ((TimeSpeed)_speedField.GetValue(tm) != TimeSpeed.Superfast)
                        _speedField.SetValue(tm, TimeSpeed.Superfast);
                }
                else if (tm.CurTimeSpeed != TimeSpeed.Superfast)
                {
                    tm.CurTimeSpeed = TimeSpeed.Superfast;
                }
            }
            catch
            {
                // 测试辅助不得影响游戏
            }
        }

        private static bool _autoLoadDone;

        /// <summary>调试：存在 /tmp/ttr-loadsave 时，在主菜单自动载入存档（默认最新；可用文件内容指定存档名）。
        /// 用于在自动化环境里对**真实存档**做 A/B（-quicktest 只能生成随机地图）。</summary>
        private static bool _autoLoadDiag;
        private static bool _autoLoadDiag2;

        public static void AutoLoad_Postfix()
        {
            if (!_autoLoadDiag)
            {
                _autoLoadDiag = true;
                Log.Message("[RimThreadedTTR] FastLoad: AutoLoad 钩子在跑：autoLoadSave=" + FastLoadMod.Settings.autoLoadSave
                    + " 开关文件=" + System.IO.File.Exists("/tmp/ttr-loadsave")
                    + " ProgramState=" + Current.ProgramState);
            }
            if (_autoLoadDone) return;
            // 2026-10-07：`/tmp/ttr-loadsave` 开关文件**本身就足够**（不必再去用户设置里开
            // autoLoadSave）。原来要求两个条件同时满足，导致自动化拿不到真实存档做 A/B。
            // 只有该文件存在才生效 ⇒ 对正常用户零影响。
            bool flagExists = false;
            try { flagExists = System.IO.File.Exists("/tmp/ttr-loadsave"); } catch { }
            if (!FastLoadMod.Settings.autoLoadSave && !flagExists) return;
            try
            {
                if (Current.ProgramState != ProgramState.Entry) return;
                string flag = "/tmp/ttr-loadsave";
                if (!System.IO.File.Exists(flag)) return;
                string wanted = System.IO.File.ReadAllText(flag).Trim();
                string dir = GenFilePaths.SaveDataFolderPath;
                // 2026-10-07 修（事故复盘）：`GameDataSaveLoader.LoadGame(p)` 把 p 当**存档名**，
                // 内部还会再拼一次 ".rws"（`Path.Combine(dir, name + ".rws")`，而 Path.Combine
                // 遇到绝对路径会直接采用它）⇒ 传 “…/uitstalie.rws” 实际去打开
                // “…/uitstalie.rws.rws” ⇒ FileNotFoundException，载入中止。
                // 实测踩过这一次（用户看到"存档坏了"的假象）。**统一改成本函数自己去掉 ".rws"**，
                // 让 RimWorld 只拼一次。
                if (wanted.EndsWith(".rws", StringComparison.OrdinalIgnoreCase))
                {
                    wanted = wanted.Substring(0, wanted.Length - 4);
                }
                if (wanted.Length > 0 && wanted.IndexOf('/') >= 0)      // 开关文件里写绝对路径（可带 .rws，会被去掉）
                {
                    if (System.IO.File.Exists(wanted + ".rws"))
                    {
                        _autoLoadDone = true;
                        Log.Message("[RimThreadedTTR] FastLoad: 自动载入存档（绝对路径）: " + wanted + ".rws");
                        GameDataSaveLoader.LoadGame(wanted);
                    }
                    else
                    {
                        Log.Warning("[RimThreadedTTR] FastLoad: 自动载入：绝对路径不存在 " + wanted + ".rws");
                    }
                    return;
                }
                System.IO.DirectoryInfo di = new System.IO.DirectoryInfo(dir);
                string pickedPath = null;
                long pickedTicks = -1;
                if (!string.IsNullOrEmpty(wanted))
                {
                    string candidate = System.IO.Path.Combine(dir, wanted + ".rws");
                    if (System.IO.File.Exists(candidate)) pickedPath = candidate;
                }
                if (pickedPath == null)
                {
                    foreach (System.IO.FileInfo f in di.GetFiles("*.rws"))
                    {
                        if (pickedPath == null || f.LastWriteTimeUtc.Ticks > pickedTicks)
                        {
                            pickedPath = f.FullName;
                            pickedTicks = f.LastWriteTimeUtc.Ticks;
                        }
                    }
                }
                if (pickedPath == null)
                {
                    if (!_autoLoadDiag2)
                    {
                        _autoLoadDiag2 = true;
                        string[] found = System.IO.Directory.GetFiles(dir, "*.rws");
                        Log.Warning("[RimThreadedTTR] FastLoad: 自动载入：在 " + dir + " 未找到存档（目录内 .rws 数=" + found.Length + "）");
                    }
                    return;
                }
                _autoLoadDone = true;
                Log.Message("[RimThreadedTTR] FastLoad: 自动载入存档（调试）: " + pickedPath);
                GameDataSaveLoader.LoadGame(pickedPath.Substring(0, pickedPath.Length - 4));   // 去掉 .rws，交给游戏自己拼
            }
            catch (Exception e)
            {
                _autoLoadDone = true;
                Log.Warning("[RimThreadedTTR] FastLoad: 自动载入存档失败: " + e.Message);
            }
        }

        /// <summary>Harmony 补丁审计：哪些方法真的被打了补丁、owner 是谁 —— 用于发现"睡着的优化"
        /// （签名不匹配会静默跳过）。挂在 Root.Update 上，只跑一次。</summary>
        private static bool _auditDone;
        public static void AuditPatches_Postfix()
        {
            if (_auditDone) return;
            if (!FastLoadMod.Settings.auditPatches) return;
            // 必须等 mods 全部初始化完（TTRCore 是启动期静态构造）⇒ 进游戏且 tick>120 才跑，
            // 否则会误报"补丁没打上"（第一帧时 TTR/FPSPlus 尚未注册）。
            if (Current.ProgramState != ProgramState.Playing) return;
            TickManager tm = Find.TickManager;
            if (tm == null || tm.TicksGame <= 120) return;
            _auditDone = true;
            try
            {
                System.Collections.Generic.Dictionary<string, int> owners =
                    new System.Collections.Generic.Dictionary<string, int>();
                int total = 0;
                foreach (MethodBase m in HarmonyLib.Harmony.GetAllPatchedMethods())
                {
                    HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    total++;
                    foreach (string o in info.Owners)
                    {
                        int c;
                        owners.TryGetValue(o, out c);
                        owners[o] = c + 1;
                    }
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[RimThreadedTTR] FastLoad: 补丁审计：共 ").Append(total).Append(" 个方法被补丁。owner：");
                foreach (System.Collections.Generic.KeyValuePair<string, int> kv in owners)
                {
                    sb.Append(" [").Append(kv.Key).Append("=").Append(kv.Value).Append("]");
                }
                Log.Message(sb.ToString());

                // 关键优化目标逐项核对（TTR / FPSPlus 的节流与绘制类）
                string[] checks = new string[]
                {
                    "Verse.FleckManager:CreateFleck",
                    "RimWorld.Alert:Recalculate",
                    "RimWorld.AlertsReadout:AlertsReadoutUpdate",
                    "RimWorld.AlertsReadout:CheckAddOrRemoveAlert",
                    "RimWorld.BeautyUtility:AverageBeautyPerceptible",
                    "RimWorld.ColonistBarColonistDrawer:DrawIcons",
                    "RimWorld.DesignationManager:DrawDesignations",
                    "RimWorld.DesignationManager:CalculateCellDesignationDrawMatricies",
                    "Verse.Graphic_Shadow:DrawWorker",
                    "RimWorld.InspectPaneFiller:DrawInspectStringFor",
                    "RimWorld.FactionManager:FactionManagerTick",
                    "RimWorld.IdeoManager:IdeoManagerTick",
                    "Verse.AI.JobGiver_Work:TryIssueJobPackage",
                    "RimWorld.Reachability:CanReach",
                    "RimWorld.PlaySettings:DoPlaySettingsGlobalControls",
                    "Verse.WindManager:WindManagerTick",
                    "Verse.GasGrid:Tick",
                    "RimWorld.ListerHaulables:ListerHaulablesTick",
                    "Verse.PawnRenderer:EffectersTick",
                    "RimWorld.EffecterMaintainer:EffecterMaintainerTick",
                    "Verse.TickList:Tick"
                };
                System.Text.StringBuilder miss = new System.Text.StringBuilder();
                int hit = 0;
                foreach (string spec in checks)
                {
                    int idx = spec.IndexOf(':');
                    Type ty = AccessTools.TypeByName(spec.Substring(0, idx));
                    bool ok = false;
                    if (ty != null)
                    {
                        // 检查该名字的**所有重载**：任一有补丁即算已打（之前只查参数最少的重载 ⇒ 误报）
                        foreach (MethodInfo mi in AccessTools.GetDeclaredMethods(ty))
                        {
                            if (mi.Name != spec.Substring(idx + 1) || mi.IsAbstract) continue;
                            if (HarmonyLib.Harmony.GetPatchInfo(mi) != null) { ok = true; break; }
                        }
                    }
                    if (ok) hit++;
                    else miss.Append(" ").Append(spec);
                }
                Log.Message("[RimThreadedTTR] FastLoad: 关键目标核对：" + hit + "/" + checks.Length + " 已打补丁；未打：" + (miss.Length == 0 ? "（无）" : miss.ToString()));
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 补丁审计失败: " + e.Message);
            }
        }

        /// <summary>MapPostTick 之后统一并行结算收集到的子系统。</summary>
        public static void ParallelFlush_Postfix() { ParallelTicker.Flush(); }

        // ── TPS 降频（Performance-Optimizer 风格）：前缀返回 false 即跳过本次原方法调用 ──
        // 用全局 tick 奇偶做节流；TickManager 为空（加载/菜单）时一律放行。

        /// <summary>每 2 tick 执行一次。</summary>
        public static bool Throttle2()
        {
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        /// <summary>每 4 tick 执行一次。</summary>
        public static bool Throttle4()
        {
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 3) == 0;
        }

        /// <summary>把目标方法记为独立"阶段"（按 声明类型.方法名），便于区分 MapUpdate 与 MapPreTick 这类同类型不同入口。</summary>
        public static void RtNamed_Begin(object __instance, MethodBase __originalMethod)
        {
            string key = __originalMethod == null
                ? "?"
                : __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
            RuntimeProfiler.Diag("RtNamed_" + key);
            RuntimeProfiler.Enter(__instance, key);
        }

        // 采样版（热路径用）：每 N 次只记录 1 次
        public static void RtSampled_Begin(object __instance) { RuntimeProfiler.SampledEnter(__instance); }
        public static Exception RtSampled_Finalizer(Exception __exception)
        {
            RuntimeProfiler.SampledExit();
            return null;
        }

        /// <summary>所有运行时钩子共用的 finalizer：无论正常返回还是抛异常都结算。</summary>
        public static Exception Rt_Finalizer(Exception __exception)
        {
            RuntimeProfiler.Exit();
            return null;
        }

        // ---------- defs 逐 mod：ModContentPack.LoadDefs 的迭代器状态机 MoveNext ----------
        private static FieldInfo _defsOwnerField;

        public static void LoadDefsMoveNext_Begin(object __instance)
        {
            Profiler.ModBegin(LoadDefsOwnerKey(__instance), 0);
        }

        public static Exception LoadDefsMoveNext_Finalizer(object __instance, Exception __exception)
        {
            Profiler.ModEnd(LoadDefsOwnerKey(__instance), 0);
            return null;   // 不改行为；只保证异常时也能结算
        }

        private static string LoadDefsOwnerKey(object stateMachine)
        {
            ModContentPack mod = LoadDefsOwner(stateMachine);
            return mod == null ? null : Key(mod);
        }

        /// <summary>编译器生成的状态机里，指向外层 ModContentPack 的字段（通常是 &lt;&gt;4__this）。</summary>
        private static ModContentPack LoadDefsOwner(object stateMachine)
        {
            if (stateMachine == null) return null;
            try
            {
                if (_defsOwnerField == null)
                {
                    foreach (FieldInfo field in stateMachine.GetType().GetFields(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (field.FieldType == typeof(ModContentPack))
                        {
                            _defsOwnerField = field;
                            break;
                        }
                    }
                }
                return _defsOwnerField == null ? null : _defsOwnerField.GetValue(stateMachine) as ModContentPack;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>兜底：周期性把当前状态写盘（Root_Entry.Update 每帧调用，内部有节流）。</summary>
        public static void RootUpdate_Postfix()
        {
            float now = Profiler.Now;
            if (now - LastPeriodicWrite < 10f) return;
            LastPeriodicWrite = now;
            Profiler.Finish("Root.Update 周期写出");
        }

        private static float LastPeriodicWrite;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.IO;
using RimWorld.Planet;
using UnityEngine;
using Verse.AI;
using Verse.Sound;
using UnityEngine.Experimental.Rendering;
using Verse;

namespace FastLoad
{
    /// <summary>最早的 mod 代码入口：记录"引擎启动后多久才轮到 mod 代码"。</summary>
    [StaticConstructorOnStartup]
    public static class EarlyMarker
    {
        static EarlyMarker()
        {
            Profiler.Mark("FastLoad [StaticConstructorOnStartup]（最早的 mod 代码）");
        }
    }

    /// <summary>
    /// 合并版：FastLoad 不再有自己的 Mod 子类（一个 mod 只能有一处设置入口），
    /// 由 TTRMod 的构造函数在 CreateModClasses 阶段调用 <see cref="Init"/>，
    /// 因此所有补丁的安装时机与原来独立 mod 时**完全一致**（仍早于
    /// LoadModXML / ApplyPatches / AllGraphicsLoaded 等启动阶段）。
    /// </summary>
    public static class FastLoadMod
    {
        public static FastLoadSettings Settings;
        public static Harmony Harm;

        /// <summary>
        /// C（2026-10-07）帧探针总开关：设置项 **或** 自动化开关文件 `/tmp/ttr-frames`。
        /// 加文件开关是为了"不改用户设置也能做帧预算 A/B"，对正常用户零影响
        /// （只有该文件存在时才多挂 8 个每帧入口钩子）。
        /// </summary>
        public static bool FrameProbeEnabled
        {
            get
            {
                if (Settings != null && Settings.runtimeProfilingFrames) return true;
                try { return System.IO.File.Exists("/tmp/ttr-frames"); }
                catch { return false; }
            }
        }

        /// <summary>由 TTRMod（唯一的 Mod 子类）调用；设置来自 TTRSettings.fastLoadSettings。</summary>
        public static void Init(FastLoadSettings settings)
        {
            Settings = settings ?? new FastLoadSettings();
            Harm = new Harmony("uitstalie.fastload");
            Profiler.Mark("FastLoad 模块初始化（合并版，由 TTRMod 调用）");
            InstallPatches();
        }

        /// <summary>
        /// 阶段挂钩表：类型 / 方法 / Patches 里的前后缀方法名前缀。
        /// 符号取自本机 Player.log 的真实调用栈（见 common/rimworld-linux/startup-perf/FINDINGS.md）。
        /// </summary>
        private static readonly string[][] PhaseBattery =
        {
            new[] { "Verse.PlayDataLoader", "LoadAllPlayData", "LoadAllPlayData" },
            new[] { "Verse.PlayDataLoader", "DoPlayLoad", "DoPlayLoad" },
            new[] { "Verse.LoadedModManager", "LoadAllActiveMods", "LoadAllActiveMods" },
            new[] { "Verse.LoadedModManager", "LoadModContent", "LoadModContent" },
            new[] { "Verse.LoadedModManager", "CreateModClasses", "CreateModClasses" },
            new[] { "Verse.LoadedModManager", "InitializeMods", "InitializeMods" },
            new[] { "Verse.LoadedModManager", "LoadModXML", "LoadModXML" },
            new[] { "Verse.LoadedModManager", "ApplyPatches", "ApplyPatches" },
            new[] { "Verse.LoadedModManager", "ParseAndProcessXML", "ParseAndProcessXML" },
            new[] { "Verse.ModAssemblyHandler", "ReloadAll", "ReloadAll" },
            new[] { "Verse.ModLister", "RebuildModList", "RebuildModList" },
            new[] { "Verse.XmlInheritance", "ResolveParentsAndChildNodesLinks", "XmlInheritance" },
            new[] { "Verse.LoadedLanguage", "InjectIntoData_AfterImpliedDefs", "InjectIntoData" },
            new[] { "Verse.GraphicDatabase", "AllGraphicsLoaded", "AllGraphicsLoaded" },
            new[] { "Verse.DirectXmlCrossRefLoader", "ResolveAllWantedCrossReferences", "CrossRef" },
            new[] { "Verse.DirectXmlCrossRefLoader", "Clear", "CrossRefClear" },
            new[] { "RimWorld.DefOfHelper", "RebindAllDefOfs", "RebindDefOfs" },
            new[] { "Verse.TKeySystem", "BuildMappings", "TKeyMappings" },
            new[] { "Verse.BackstoryTranslationUtility", "LoadAndInjectBackstoryData", "Backstory" },
            new[] { "RimWorld.DefGenerator", "GenerateImpliedDefs_PreResolve", "ImpliedDefs" },
            new[] { "Verse.LanguageDatabase", "InitAllMetadata", "LangMeta" },
            new[] { "Verse.ColoredText", "ResetStaticData", "ColoredText" },
        };

        /// <summary>补丁应用归因（会传 __instance，单独处理）。</summary>
        private static readonly string[] PatchOperationHook = { "Verse.PatchOperation", "Apply" };

        /// <summary>按"类型+方法名"匹配**所有重载**挂钩（不需要签名；用于 Emit 计数等）。</summary>
        private static readonly string[][] NameBattery =
        {
            new[] { "Verse.DirectXmlToObjectNew", "GetFieldSetterForType", "EmitGet" },
            new[] { "Verse.DirectXmlToObjectNew", "GetListItemAdderForType", "EmitGet" },
            new[] { "Verse.DirectXmlToObjectNew", "GetDefParserForType", "EmitGet" },
            new[] { "Verse.DirectXmlToObjectNew", "CreateFieldSetterForType", "EmitCreate" },
            new[] { "Verse.DirectXmlToObjectNew", "CreateListItemAdderForType", "EmitCreate" },
            new[] { "Verse.DirectXmlToObjectNew", "CreateDefParserForType", "EmitCreate" },
        };

        /// <summary>每个 mod 的挂钩（真实代码，不是迭代器桩）。</summary>
        private static readonly string[][] PerModBattery =
        {
            new[] { "Verse.ModContentPack", "ReloadContent", "ReloadContent" },
            new[] { "Verse.ModContentPack", "LoadPatches", "LoadPatches" },
        };

        private static void InstallPatches()
        {
            if (!Settings.profileEnabled)
            {
                Log.Message("[RimThreadedTTR] FastLoad: 计时已关闭（设置里可开启）");
                return;
            }

            var ok = new List<string>();
            var fail = new List<string>();

            foreach (var row in PhaseBattery)
            {
                bool patched = PatchStatics(row[0], row[1], row[2]);
                (patched ? ok : fail).Add(row[2]);
            }

            foreach (var row in PerModBattery)
            {
                bool patched = PatchStatics(row[0], row[1], row[2]);
                (patched ? ok : fail).Add("每mod:" + row[2]);
            }

            // 补丁应用（按 mod 归因）：需要 __instance
            bool patchOps = PatchStatics(PatchOperationHook[0], PatchOperationHook[1], "PatchApply");
            (patchOps ? ok : fail).Add("补丁应用归因");

            (InstallDefsTiming() ? ok : fail).Add("每mod:defs(状态机)");
            InstallTypedHooks(ok, fail);

            foreach (var row in NameBattery)
            {
                int patched = PatchAllOverloads(row[0], row[1], row[2]);
                if (patched > 0) ok.Add(row[2] + "×" + patched);
                else fail.Add(row[2]);
            }

            if (Settings.runtimeProfiling)
            {
                RuntimeProfiler.SampleRate = Settings.runtimeProfilingSampleRate < 1
                    ? 1
                    : Settings.runtimeProfilingSampleRate;
                InstallRuntimeHooks(ok, fail);
            }

            if (Settings.staticCtorTiming)
            {
                (StaticCtorTiming.Install(Harm) ? ok : fail).Add("静态构造计时");
            }

            if (Settings.tpsOptimizeVisual || Settings.tpsOptimizeSimulation)
            {
                InstallTpsThrottles(ok, fail);
            }

            // 实测（20:40 会话）：小批次并行是**负收益** —— 每 tick 2 次 ParallelForEach 派发开销
            // 大于省下的 42 µs/tick（MapPostTick 从 0.474 → 0.663 ms/tick）。故默认关闭。
            if (Settings.tpsParallelPawnTick)
            {
                InstallParallelPawnTick(ok, fail);
            }

            if (Settings.xpathFastPath)
            {
                (XPathFastPath.Install(Harm) ? ok : fail).Add("XPath快速路径");
            }

            // 兜底写盘：必须把 Root_Entry（菜单）、Root_Play（游戏内）、Root（基类）**全部**挂上。
            // 之前只挂了第一个存在的（Root_Entry），导致进入游戏后报告就不再刷新，
            // 运行时归因看起来"全是 0"。
            int tick = PatchAll(
                new[] { "Verse.Root_Entry", "Verse.Root_Play", "Verse.Root" },
                "Update", "RootUpdate");

            Profiler.Mark("挂钩完成：成功 " + ok.Count + "（兜底写盘 " + tick + " 处）/ 失败 " + fail.Count);
            Log.Message("[RimThreadedTTR] FastLoad: 挂钩成功: " + string.Join(", ", ok.ToArray()));
            if (fail.Count > 0)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 挂钩失败: " + string.Join(", ", fail.ToArray()));
            }
        }

        /// <summary>
        /// 已由反编译取证确认签名的定点钩子（泛型类型用闭合类型，
        /// 例如 ModContentLoader&lt;Texture2D&gt; / ContentFinder&lt;Texture2D&gt;）。
        /// </summary>
        private static void InstallTypedHooks(List<string> ok, List<string> fail)
        {
            // ⚠️ 已撤掉 ModContentLoader<Texture2D>.LoadItem 与 ContentFinder<Texture2D>.Get：
            // 前者在 Mono 下与 ModContentLoader<AudioClip>.LoadItem **共享泛型代码**，
            // 挂上去后音频加载行为被改变（FMOD 报错从 111 变 0），随后在
            // AudioGrain_Folder → ResolvedGrain_Clip..ctor → AudioClip.get_length 处 SIGSEGV。

            // 图集 / 图形库
            TryHook(ok, fail, "图集烘焙", typeof(GlobalTextureAtlasManager), "BakeStaticAtlases",
                null, "BakeAtlases_Begin", "BakeAtlases_End");
            TryHook(ok, fail, "清空图集队列", typeof(GlobalTextureAtlasManager), "ClearStaticAtlasBuildQueue",
                null, "ClearAtlasQueue_Begin", "ClearAtlasQueue_End");
            TryHook(ok, fail, "GraphicDatabase.Clear", typeof(GraphicDatabase), "Clear",
                null, "GraphicDbClear_Begin", "GraphicDbClear_End");
            // Graphic_Single.Init 每次贴图对象创建都会被调用（实测 9 千余次）：
            // 只累计计数，**不打日志**（否则淹没 Player.log）。
            TryHook(ok, fail, "Graphic_Single.Init（计数）", typeof(Graphic_Single), "Init",
                new[] { typeof(GraphicRequest) }, "GraphicSingleCount_Begin", "GraphicSingleCount_End");

            // DDS 真实解码/页读取（mmap 的字节访问发生在它内部）—— 非泛型类型
            // 注意：该方法的第 4 个参数是 Span<byte>，而游戏自带的参考程序集里
            // Span<T> 是 internal（Krafs 参考程序集里才是 public）⇒ 不能直接写
            // typeof(Span<byte>)。改为运行时从方法签名里取参数类型，语义完全一致。
            TryHook(ok, fail, "ModDdsLoader.CreateTexture", typeof(ModDdsLoader), "CreateTexture",
                DdsCreateTextureArgs(), "DdsDecode_Begin", "DdsDecode_End");

            // XML 文件读取（mmap + UTF8 + XmlDocument.Load 都在这个构造函数里）
            TryHook(ok, fail, "LoadableXmlAsset..ctor", typeof(LoadableXmlAsset), ".ctor",
                new[] { typeof(FileInfo), typeof(ModContentPack) }, "XmlRead_Begin", "XmlRead_End");

            // 所有 mmap 文件读取（XML + DDS 的真实字节 I/O）—— 非泛型类型
            TryHook(ok, fail, "mmap 读取 MemoryMappedFileSpanWrapper..ctor",
                typeof(Verse.MemoryMappedFileSpanWrapper), ".ctor", new[] { typeof(FileInfo) },
                "Mmap_Begin", "Mmap_End");

            // DDS 解码快路径（非泛型静态方法）
            TryHook(ok, fail, "ModDdsLoader.TryLoadDds", typeof(ModDdsLoader), "TryLoadDds",
                new[] { typeof(VirtualFile) }, "Dds_Begin", "Dds_End");

            // 所有文件读取：给 VirtualFile 的**具体子类**挂 ReadAllBytes（抽象基类挂不上，
            // 且这里完全不涉及泛型，避免 Mono 泛型代码共享）
            int readHooks = InstallVirtualFileReadHooks();
            if (readHooks > 0) ok.Add("VirtualFile.ReadAllBytes×" + readHooks);
            else fail.Add("VirtualFile.ReadAllBytes");

            // 之前漏掉/未覆盖的阶段
            TryHook(ok, fail, "语言注入(Before)", typeof(LoadedLanguage), "InjectIntoData_BeforeImpliedDefs",
                null, "InjectBefore_Begin", "InjectBefore_End");
            TryHook(ok, fail, "短哈希", typeof(ShortHashGiver), "GiveAllShortHashes",
                null, "ShortHash_Begin", "ShortHash_End");
            TryHook(ok, fail, "XML 逐 mod 读取", typeof(DirectXmlLoader), "XmlAssetsInModFolder",
                new[] { typeof(ModContentPack), typeof(string), typeof(List<string>) },
                "XmlAssets_Begin", "XmlAssets_End");
            TryHook(ok, fail, "隐含 def(PostResolve)", typeof(DefGenerator), "GenerateImpliedDefs_PostResolve",
                null, "ImpliedDefsPost_Begin", "ImpliedDefsPost_End");
            TryHook(ok, fail, "ResetStaticDataPre", typeof(PlayDataLoader), "ResetStaticDataPre",
                null, "ResetPre_Begin", "ResetPre_End");
            TryHook(ok, fail, "ResetStaticDataPost", typeof(PlayDataLoader), "ResetStaticDataPost",
                null, "ResetPost_Begin", "ResetPost_End");
            TryHook(ok, fail, "生物背景 LoadAllBios", typeof(SolidBioDatabase), "LoadAllBios",
                null, "Bios_Begin", "Bios_End");
            TryHook(ok, fail, "KeyPrefs.Init", typeof(KeyPrefs), "Init",
                null, "KeyPrefs_Begin", "KeyPrefs_End");
        }

        /// <summary>
        /// ModDdsLoader.CreateTexture 的参数类型（第 4 个是 Span&lt;byte&gt;）。
        /// 合并进 RimThreadedTTR 后本工程引用的是**游戏自带**的 Assembly-CSharp，
        /// 其中 Span&lt;T&gt; 的可见性是 internal（FastLoad 单独立项时用的是 Krafs
        /// 参考程序集，那里是 public），所以不能写 typeof(Span&lt;byte&gt;)。
        /// 这里改成运行时从方法签名里取参数类型；找不到就返回空数组（等价于
        /// "该签名不存在"，只影响这一条挂钩的成败，不会影响其它挂钩）。
        /// </summary>
        private static Type[] DdsCreateTextureArgs()
        {
            try
            {
                foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(ModDdsLoader)))
                {
                    if (m.Name != "CreateTexture") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 4) continue;
                    Type[] args = new Type[4];
                    bool complete = true;
                    for (int i = 0; i < 4; i++)
                    {
                        args[i] = ps[i].ParameterType;
                        if (args[i] == null) complete = false;
                    }
                    if (complete) return args;
                }
            }
            catch
            {
                // 取不到就退回"签名不匹配"
            }
            return Type.EmptyTypes;
        }

        /// <summary>
        /// 运行时归因钩子：全部非泛型、目标为**无参实例方法**（用 Type.EmptyTypes 精确定位），
        /// 统一用 finalizer 结算，保证异常路径也能弹栈。
        /// </summary>
        private static void InstallRuntimeHooks(List<string> ok, List<string> fail)
        {
            MethodInfo fin = AccessTools.Method(typeof(Patches), "Rt_Finalizer");

            // 极简 TPS 仪表：只挂 2 个钩子（每帧/每 tick 各一次）⇒ 开销可忽略，
            // 但能给出**准确的实测 TPS**，用于 A/B 对比（例如 带/不带 RimThreadedTTR）。
            if (Settings.runtimeProfilingMinimal)
            {
                AddRuntimeHook(ok, fail, "TickManagerUpdate", typeof(TickManager), "TickManagerUpdate", "RtTickUpdate_Begin", fin);
                AddRuntimeHook(ok, fail, "DoSingleTick", typeof(TickManager), "DoSingleTick", "RtDoSingleTick_Begin", fin);
                if (Settings.runtimeProfilingMapPost) InstallMapPostProbe(ok, fail, fin);
                // 2026-10-07：原来这一行重复出现两次（合并 FastLoad 时带入）⇒ 开关打开时
                // InstallFrameProbe 会跑两遍、同一方法被挂两次前缀，耗时被记两倍。
                if (FrameProbeEnabled) InstallFrameProbe(ok, fail, fin);
                return;
            }
            if (Settings.runtimeProfilingMapPost) InstallMapPostProbe(ok, fail, fin);
            if (FrameProbeEnabled) InstallFrameProbe(ok, fail, fin);

            AddRuntimeHook(ok, fail, "TickManagerUpdate", typeof(TickManager), "TickManagerUpdate", "RtTickUpdate_Begin", fin);
            AddRuntimeHook(ok, fail, "DoSingleTick", typeof(TickManager), "DoSingleTick", "RtDoSingleTick_Begin", fin);
            AddRuntimeHook(ok, fail, "Map.MapUpdate", typeof(Map), "MapUpdate", "RtMapUpdate_Begin", fin);
            AddRuntimeHook(ok, fail, "MapComponentUpdate", typeof(MapComponent), "MapComponentUpdate", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "MapComponentTick", typeof(MapComponent), "MapComponentTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "GameComponentUpdate", typeof(GameComponent), "GameComponentUpdate", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "GameComponentTick", typeof(GameComponent), "GameComponentTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "WorldComponentUpdate", typeof(WorldComponent), "WorldComponentUpdate", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "JobDriver.DriverTick", typeof(JobDriver), "DriverTick", "RtJobDriver_Begin", fin);
            AddRuntimeHook(ok, fail, "JobTrackerTick", typeof(Pawn_JobTracker), "JobTrackerTick", "RtJobTracker_Begin", fin);

            // 虚方法：基类实现根本不会被调用（原版与 mod 的组件全是 override），
            // 必须逐个**具体子类**挂其自己声明的 override —— 顺带得到"按 mod 归因"。
            int overrides = 0;
            overrides += InstallRuntimeOverrides(typeof(MapComponent),
                new[] { "MapComponentUpdate", "MapComponentTick" }, "RtComponent_Begin", fin);
            overrides += InstallRuntimeOverrides(typeof(GameComponent),
                new[] { "GameComponentUpdate", "GameComponentTick" }, "RtComponent_Begin", fin);
            overrides += InstallRuntimeOverrides(typeof(WorldComponent),
                new[] { "WorldComponentUpdate" }, "RtComponent_Begin", fin);
            overrides += InstallRuntimeOverrides(typeof(Pawn_JobTracker),
                new[] { "JobTrackerTick" }, "RtJobTracker_Begin", fin);
            // ⚠️ 曾经的"批量 patch 所有 mod 子类"（Thing/ThingComp/ThinkNode）会触发
            // **Mono 原生 abort**（signo 5）：MonoMod 为个别 mod 方法建 detour 时
            // `RuntimeMethodHandle:GetFunctionPointer` 直接把进程带走，try/catch 兜不住。
            // 因此改为只挂**基类虚方法**（单次 patch，零批量风险）：
            // 未被 override 的类型会走基类实现 ⇒ 仍能按 `this` 的具体类型归因。
            // 极高调用量的钩子：默认关闭（插桩本身就会显著增加每 tick 开销）
            if (Settings.runtimeProfilingHotThings)
            {
                // 热路径改用**采样**（每 100 次记录 1 次并按倍数放大）：开销降到 1/100，
                // 仍能给出"物品/建筑"那 ~40% 的归因。
                MethodInfo sampledFin = AccessTools.Method(typeof(Patches), "RtSampled_Finalizer");
                AddRuntimeHook(ok, fail, "ThingWithComps.Tick(采样)", typeof(ThingWithComps), "Tick", "RtSampled_Begin", sampledFin);
                AddRuntimeHook(ok, fail, "Thing.Tick(采样)", typeof(Thing), "Tick", "RtSampled_Begin", sampledFin);
                AddRuntimeHook(ok, fail, "ThingComp.CompTick(采样)", typeof(ThingComp), "CompTick", "RtSampled_Begin", sampledFin);
                AddRuntimeHook(ok, fail, "ThingComp.CompTickRare(采样)", typeof(ThingComp), "CompTickRare", "RtSampled_Begin", sampledFin);
            }
            AddRuntimeHook(ok, fail, "Pawn.Tick", typeof(Pawn), "Tick", "RtComponent_Begin", fin);

            // 第三批：拆 Pawn.Tick 的子系统（全部基类方法、单次 patch）
            AddRuntimeHook(ok, fail, "HealthTick", typeof(Pawn_HealthTracker), "HealthTick", "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "NeedsTrackerTickInterval", typeof(Pawn_NeedsTracker), "NeedsTrackerTickInterval", new[] { typeof(int) }, "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "AgeTickInterval", typeof(Pawn_AgeTracker), "AgeTickInterval", new[] { typeof(int) }, "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "MindStateTickInterval", typeof(Pawn_MindState), "MindStateTickInterval", new[] { typeof(int) }, "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "PatherTick", typeof(Pawn_PathFollower), "PatherTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "StanceTrackerTick", typeof(Pawn_StanceTracker), "StanceTrackerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "EquipmentTrackerTick", typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "InventoryTrackerTick", typeof(Pawn_InventoryTracker), "InventoryTrackerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "ApparelTrackerTickRare", typeof(Pawn_ApparelTracker), "ApparelTrackerTickRare", "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "ApparelTrackerTickInterval", typeof(Pawn_ApparelTracker), "ApparelTrackerTickInterval", new[] { typeof(int) }, "RtComponent_Begin", fin);

            // 第三批补充：Pawn.Tick 里其余子系统（IL 枚举得出）
            AddRuntimeHook(ok, fail, "MutantTrackerTick", typeof(Pawn_MutantTracker), "MutantTrackerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "AbilitiesTick", typeof(Pawn_AbilityTracker), "AbilitiesTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "GeneTrackerTick", typeof(Pawn_GeneTracker), "GeneTrackerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "RopingTick", typeof(Pawn_RopeTracker), "RopingTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "FlightTick", typeof(Pawn_FlightTracker), "FlightTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "VerbsTick", typeof(VerbTracker), "VerbsTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "NativeVerbsTick", typeof(Pawn_NativeVerbs), "NativeVerbsTick", "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "EffectersTick", typeof(PawnRenderer), "EffectersTick", new[] { typeof(bool) }, "RtComponent_Begin", fin);

            // 定点挂钩"已知热点的具体类型"（每个都是一次单点 patch，**不是批量**，安全）：
            // 关闭极热钩子后，建筑/物品那部分就看不见了 —— 用这份白名单补回来。
            AddRuntimeHook(ok, fail, "Building.Tick", typeof(Building), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_Door.Tick", typeof(Building_Door), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_WorkTable.Tick", typeof(Building_WorkTable), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_Crate.Tick", typeof(Building_Crate), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_AncientCryptosleepCasket.Tick", typeof(Building_AncientCryptosleepCasket), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_VoidMonolith.Tick", typeof(Building_VoidMonolith), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "SmokepopBelt.Tick", typeof(SmokepopBelt), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Building_AncientMechRemains.Tick", typeof(Building_AncientMechRemains), "Tick", "RtComponent_Begin", fin);

            // 需求细分：NeedsTrackerTickInterval 里就是对每个 Need 调 NeedInterval()
            // （原版里 override 的那些逐个定点挂；其余走基类）
            AddRuntimeHook(ok, fail, "Need.NeedInterval", typeof(Need), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Food", typeof(Need_Food), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Rest", typeof(Need_Rest), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Joy", typeof(Need_Joy), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Mood", typeof(Need_Mood), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Chemical", typeof(Need_Chemical), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Play", typeof(Need_Play), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Outdoors", typeof(Need_Outdoors), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Learning", typeof(Need_Learning), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Authority", typeof(Need_Authority), "NeedInterval", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Need_Suppression", typeof(Need_Suppression), "NeedInterval", "RtComponent_Begin", fin);

            // Pawn 独占部分剩下的三处（IL 枚举得出）
            // Pawn.Tick 里剩下的调用点（IL 枚举；这一批是找 790 µs/tick 真凶的关键）
            AddRuntimeHookByName(ok, fail, "Pawn.get_IsAnimal", "Verse.Pawn", "get_IsAnimal", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "WorldPawnsUtility.IsWorldPawn", "Verse.WorldPawnsUtility", "IsWorldPawn", "RtNamed_Begin", fin);

            // 归因"tick 循环本体"（DoSingleTick 独占的 ~45%）：IL 枚举得出
            AddRuntimeHook(ok, fail, "TickList.Tick", typeof(TickList), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Map.MapPreTick", typeof(Map), "MapPreTick", "RtNamed_Begin", fin);
            AddRuntimeHook(ok, fail, "Map.MapPostTick", typeof(Map), "MapPostTick", "RtNamed_Begin", fin);
            AddRuntimeHook(ok, fail, "Storyteller.StorytellerTick", typeof(Storyteller), "StorytellerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "StoryWatcher.StoryWatcherTick", typeof(StoryWatcher), "StoryWatcherTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "QuestManager.QuestManagerTick", typeof(QuestManager), "QuestManagerTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "History.HistoryTick", typeof(History), "HistoryTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "LetterStack.LetterStackTick", typeof(LetterStack), "LetterStackTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "DateNotifier.DateNotifierTick", typeof(DateNotifier), "DateNotifierTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "GameEnder.GameEndTick", typeof(GameEnder), "GameEndTick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "TransportShipManager.ShipObjectsTick", typeof(TransportShipManager), "ShipObjectsTick", "RtComponent_Begin", fin);
            AddRuntimeHookByName(ok, fail, "FilthMonitor.FilthMonitorTick", "RimWorld.FilthMonitor", "FilthMonitorTick", "RtComponent_Begin", fin);
            // MapPreTick / MapPostTick 内部的各"地图系统"（IL 枚举得出）——单独成项，才能指名道姓
            AddRuntimeHookByName(ok, fail, "MapTemperatureTick", "Verse.MapTemperature", "MapTemperatureTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "WindManagerTick", "Verse.WindManager", "WindManagerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "PathFinderTick", "Verse.PathFinder", "PathFinderTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "ListerHaulablesTick", "RimWorld.ListerHaulables", "ListerHaulablesTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "FleckManagerTick", "Verse.FleckManager", "FleckManagerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "GasGrid.Tick", "Verse.GasGrid", "Tick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "PollutionGrid.Tick", "Verse.PollutionGrid", "PollutionTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "PowerNetsTick", "RimWorld.PowerNetManager", "PowerNetsTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "FireWatcherTick", "RimWorld.FireWatcher", "FireWatcherTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "EffecterMaintainerTick", "RimWorld.EffecterMaintainer", "EffecterMaintainerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "GameConditionManagerTick", "RimWorld.GameConditionManager", "GameConditionManagerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "LordManagerTick", "Verse.AI.Group.LordManager", "LordManagerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "DeferredSpawnerTick", "RimWorld.DeferredSpawner", "DeferredSpawnerTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "WaterBodyTracker.Tick", "Verse.WaterBodyTracker", "Tick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "DebugDrawerTick(仅dev)", "Verse.DebugCellDrawer", "DebugDrawerTick", "RtNamed_Begin", fin);
            AddRuntimeHook(ok, fail, "Mote.Tick", typeof(Mote), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHook(ok, fail, "Projectile.Tick", typeof(Projectile), "Tick", "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "IsHiddenFromPlayer", typeof(InvisibilityUtility), "IsHiddenFromPlayer",
                new[] { typeof(Pawn) }, "RtComponent_Begin", fin);
            AddRuntimeHookWithArgs(ok, fail, "BloodRainTick", typeof(BloodRainUtility), "BloodRainTick",
                new[] { typeof(Pawn) }, "RtComponent_Begin", fin);
            if (overrides > 0) ok.Add("运行时:逐子类 override×" + overrides);
        }

        /// <summary>
        /// 给 baseType 的所有具体子类中**自己声明**的指定方法挂钩（返回成功数量）。
        /// 上限保护：超过 4000 个就停手，避免个别版本把安装阶段拖太久。
        /// </summary>
        private static int InstallRuntimeOverrides(Type baseType, string[] methodNames, string prefixName,
            MethodInfo finalizer, bool modOnly = false)
        {
            MethodInfo prefix = AccessTools.Method(typeof(Patches), prefixName);
            if (prefix == null) return 0;
            int patched = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    continue;   // 个别 mod 程序集 GetTypes 会抛，跳过
                }
                if (types == null) continue;
                if (modOnly && assembly.GetName().Name == "Assembly-CSharp") continue;   // 高频路径只挂 mod 程序集
                foreach (Type type in types)
                {
                    if (type == null || type.IsAbstract || type == baseType || !baseType.IsAssignableFrom(type)) continue;
                    // 安全过滤：编译器生成类型（闭包/状态机/lambda）与泛型方法一律跳过，
                    // 它们是 MonoMod detour 最容易把 Mono 打崩的目标。
                    if (type.Name.IndexOf('<') >= 0 || type.IsGenericTypeDefinition) continue;
                    foreach (string name in methodNames)
                    {
                        MethodInfo method = AccessTools.Method(type, name, Type.EmptyTypes);
                        if (method == null || method.IsAbstract || method.IsGenericMethod) continue;
                        if (method.DeclaringType != type) continue;   // 只打子类自己声明的 override
                        try
                        {
                            Harm.Patch(method,
                                prefix: new HarmonyMethod(prefix),
                                finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
                            patched++;
                        }
                        catch
                        {
                            // 单个方法挂不上不影响其它
                        }
                        if (patched >= 4000) return patched;
                    }
                }
            }
            return patched;
        }

        /// <summary>#4 探测：MapPostTick 里那 234 µs/tick 的 4 个嫌疑（各只 1 次/tick，开销可忽略）。</summary>
        private static void InstallMapPostProbe(List<string> ok, List<string> fail, MethodInfo fin)
        {
            AddRuntimeHookByName(ok, fail, "FireWatcherTick", "RimWorld.FireWatcher", "FireWatcherTick", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "MapComponentUtilityTick", "Verse.MapComponentUtility", "MapComponentTick", "RtNamed_Begin", fin, new[] { typeof(Map) });
            AddRuntimeHookByName(ok, fail, "TileMutatorWorkerTick", "RimWorld.TileMutatorWorker", "Tick", "RtNamed_Begin", fin, new[] { typeof(Map) });
            AddRuntimeHookByName(ok, fail, "WaterBodyTrackerTick", "RimWorld.WaterBodyTracker", "Tick", "RtNamed_Begin", fin);
        }

        /// <summary>FPS 探测：每帧入口（各 1 次/帧，开销可忽略）。同一方法只挂首个匹配签名。</summary>
        private static void InstallFrameProbe(List<string> ok, List<string> fail, MethodInfo fin)
        {
            AddRuntimeHookByName(ok, fail, "Map.MapUpdate", "Verse.Map", "MapUpdate", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "UIRoot_Play.UIRootOnGUI", "Verse.UIRoot_Play", "UIRootOnGUI", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "ColonistBar.ColonistBarOnGUI", "RimWorld.ColonistBar", "ColonistBarOnGUI", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "AlertsReadoutUpdate", "RimWorld.AlertsReadout", "AlertsReadoutUpdate", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "MapInterfaceOnGUI", "RimWorld.MapInterface", "MapInterfaceOnGUI", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "Selector.SelectorOnGUI", "RimWorld.Selector", "SelectorOnGUI", "RtNamed_Begin", fin);
            // 2026-10-07：1.6.4871 里 **没有** MapDrawer.MapDrawerUpdate 这个方法名
            // （在 Assembly-CSharp.dll 里搜不到），按原名挂钩只会得到一条"挂钩失败"。
            // 改用确实存在的渲染入口：Map.MapOnGUI（每帧画地图）与 MapDrawer.DrawMapMesh（建网格）。
            AddRuntimeHookByName(ok, fail, "Map.MapOnGUI", "Verse.Map", "MapOnGUI", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "MapDrawer.DrawMapMesh", "Verse.MapDrawer", "DrawMapMesh", "RtNamed_Begin", fin);
            AddRuntimeHookByName(ok, fail, "Root.UIRootOnGUI", "Verse.Root", "UIRootOnGUI", "RtNamed_Begin", fin);
        }

        /// <summary>并行 tick 试点：收集前缀 + MapPostTick 后并行结算。</summary>
        private static void InstallParallelPawnTick(List<string> ok, List<string> fail)
        {
            if (!ParallelTicker.Init()) { fail.Add("并行tick:初始化"); return; }
            ParallelTicker.Enabled = true;
            ParallelTicker.Degree = Settings.tpsParallelDegree < 2 ? 2 : Settings.tpsParallelDegree;

            MethodInfo collectEquip = AccessTools.Method(typeof(ParallelTicker), "Collect_Equip");
            MethodInfo collectNative = AccessTools.Method(typeof(ParallelTicker), "Collect_Native");
            MethodInfo flush = AccessTools.Method(typeof(Patches), "ParallelFlush_Postfix");
            if (collectEquip == null || collectNative == null || flush == null) { fail.Add("并行tick:方法缺失"); return; }

            (TryPatch(AccessTools.Method(typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick", Type.EmptyTypes),
                "并行:EquipmentTrackerTick", collectEquip, null) ? ok : fail).Add("并行:EquipmentTrackerTick");
            (TryPatch(AccessTools.Method(typeof(Pawn_NativeVerbs), "NativeVerbsTick", Type.EmptyTypes),
                "并行:NativeVerbsTick", collectNative, null) ? ok : fail).Add("并行:NativeVerbsTick");
            (TryPatch(AccessTools.Method(typeof(Map), "MapPostTick", Type.EmptyTypes),
                "并行:结算(MapPostTick)", null, flush) ? ok : fail).Add("并行:结算");
        }

        /// <summary>TPS 降频：给目标方法挂节流前缀（返回 false 跳过原方法）。</summary>
        private static void InstallTpsThrottles(List<string> ok, List<string> fail)
        {
            if (Settings.tpsOptimizeVisual)
            {
                // FleckManagerTick 不降频：跳过的 tick 会让 fleck 存活更久（数量翻倍），
                // 每次调用的工作量随之翻倍 ⇒ 实测总耗时不变（34 → 36 µs/tick），白降且有视觉风险。
                AddThrottle(ok, fail, "EffecterMaintainerTick", "RimWorld.EffecterMaintainer", "EffecterMaintainerTick", Type.EmptyTypes, "Throttle2");
                AddThrottle(ok, fail, "PawnRenderer.EffectersTick", "Verse.PawnRenderer", "EffectersTick", new[] { typeof(bool) }, "Throttle2");
            }
            if (Settings.tpsOptimizeSimulation)
            {
                AddThrottle(ok, fail, "WindManagerTick", "Verse.WindManager", "WindManagerTick", Type.EmptyTypes, "Throttle4");
                AddThrottle(ok, fail, "GasGrid.Tick", "Verse.GasGrid", "Tick", Type.EmptyTypes, "Throttle2");
                AddThrottle(ok, fail, "ListerHaulablesTick", "RimWorld.ListerHaulables", "ListerHaulablesTick", Type.EmptyTypes, "Throttle2");
            }
        }

        private static void AddThrottle(List<string> ok, List<string> fail, string label, string typeName,
            string method, Type[] args, string prefixName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) { fail.Add("节流:" + label + "(找不到类型)"); return; }
            MethodInfo target = AccessTools.Method(type, method, args);
            if (target == null) { fail.Add("节流:" + label + "(找不到方法)"); return; }
            MethodInfo prefix = AccessTools.Method(typeof(Patches), prefixName);
            if (TryPatch(target, "节流:" + label, prefix, null)) ok.Add("节流:" + label);
            else fail.Add("节流:" + label);
        }

        private static void AddRuntimeHook(List<string> ok, List<string> fail, string label, Type type,
            string method, string prefixName, MethodInfo finalizer)
        {
            AddRuntimeHookWithArgs(ok, fail, label, type, method, Type.EmptyTypes, prefixName, finalizer);
        }

        /// <summary>按"参数最少的非泛型重载"安装（用于 IsSelected(object) 这类有参/多重重载的方法）。</summary>
        private static void AddRuntimeHookByAnyOverload(List<string> ok, List<string> fail, string label, string typeName,
            string method, string prefixName, MethodInfo finalizer)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) { fail.Add("运行时:" + label + "(找不到类型)"); return; }
            MethodInfo best = null;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.Name != method || m.IsGenericMethod || m.IsAbstract) continue;
                if (best == null || m.GetParameters().Length < best.GetParameters().Length) best = m;
            }
            if (best == null) { fail.Add("运行时:" + label + "(找不到方法)"); return; }
            MethodInfo prefix = AccessTools.Method(typeof(Patches), prefixName);
            if (TryPatch(best, "运行时:" + label, prefix, finalizer)) ok.Add("运行时:" + label);
            else fail.Add("运行时:" + label);
        }

        /// <summary>按类型名安装（用于 internal / 不能直接 typeof 的类型，如 RimWorld.FilthMonitor）。</summary>
        private static void AddRuntimeHookByName(List<string> ok, List<string> fail, string label, string typeName,
            string method, string prefixName, MethodInfo finalizer, Type[] args = null)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                fail.Add("运行时:" + label + "(找不到类型)");
                return;
            }
            AddRuntimeHookWithArgs(ok, fail, label, type, method, args ?? Type.EmptyTypes, prefixName, finalizer);
        }

        /// <summary>同上，但可指定参数类型（例如 *TickInterval(int) 这类带参入口）。</summary>
        private static void AddRuntimeHookWithArgs(List<string> ok, List<string> fail, string label, Type type,
            string method, Type[] args, string prefixName, MethodInfo finalizer)
        {
            MethodInfo target = AccessTools.Method(type, method, args);
            MethodInfo prefix = AccessTools.Method(typeof(Patches), prefixName);
            if (target == null || prefix == null)
            {
                fail.Add("运行时:" + label);
                return;
            }
            try
            {
                Harm.Patch(target,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
                ok.Add("运行时:" + label);
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 运行时挂钩 " + label + " 失败: " + e.Message);
                fail.Add("运行时:" + label);
            }
        }

        /// <summary>给所有 VirtualFile 具体子类的 ReadAllBytes 挂钩（返回成功个数）。</summary>
        private static int InstallVirtualFileReadHooks()
        {
            MethodInfo before = AccessTools.Method(typeof(Patches), "ReadBytes_Begin");
            MethodInfo after = AccessTools.Method(typeof(Patches), "ReadBytes_End");
            int patched = 0;
            try
            {
                Type baseType = typeof(VirtualFile);
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (Exception inner)
                    {
                        // 某些 mod 程序集的 GetTypes() 会抛 TypeLoadException（Mono 的
                        // "Invalid type ... for instance field"），不能让它中断整个扫描
                        Log.Warning("[RimThreadedTTR] FastLoad: 跳过程序集 " + assembly.GetName().Name
                                    + "（GetTypes 失败: " + inner.GetType().Name + "）");
                        continue;
                    }
                    if (types == null) continue;
                    foreach (Type type in types)
                    {
                        if (type == null || type.IsAbstract || !baseType.IsAssignableFrom(type)) continue;
                        MethodInfo read = AccessTools.Method(type, "ReadAllBytes", Type.EmptyTypes);
                        if (read == null || read.IsAbstract) continue;
                        if (TryPatch(read, type.Name + ".ReadAllBytes", before, after)) patched++;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 挂 VirtualFile.ReadAllBytes 失败: " + e.Message);
            }
            return patched;
        }

        private static void TryHook(List<string> ok, List<string> fail, string label, Type type,
            string method, Type[] args, string beginName, string endName)
        {
            MethodBase target;
            if (method == ".ctor")
            {
                ConstructorInfo ctor = args == null
                    ? AccessTools.Constructor(type)
                    : AccessTools.Constructor(type, args);
                target = ctor;
            }
            else
            {
                target = args == null
                    ? AccessTools.Method(type, method)
                    : AccessTools.Method(type, method, args);
            }
            if (target == null)
            {
                fail.Add(label);
                return;
            }
            MethodInfo before = AccessTools.Method(typeof(Patches), beginName);
            MethodInfo after = AccessTools.Method(typeof(Patches), endName);
            if (target == null)
            {
                fail.Add(label);
                return;
            }
            if (TryPatch(target, label, before, after)) ok.Add(label);
            else fail.Add(label);
        }

        /// <summary>把某个类型下**所有**同名方法都挂上前后缀（返回成功个数）。</summary>
        private static int PatchAllOverloads(string typeName, string methodName, string patchPrefix)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) return 0;
            MethodInfo before = AccessTools.Method(typeof(Patches), patchPrefix + "_Begin");
            MethodInfo after = AccessTools.Method(typeof(Patches), patchPrefix + "_End");
            int patched = 0;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != methodName) continue;
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                if (TryPatch(method, typeName + "." + methodName, before, after)) patched++;
            }
            return patched;
        }

        /// <summary>
        /// 给 ModContentPack.LoadDefs 的迭代器状态机 MoveNext 挂钩（真实干活的是它，
        /// LoadDefs 本身只是 22 字节的桩）。用 IteratorStateMachineAttribute 在运行时定位状态机类型，
        /// 避免硬编码 &lt;LoadDefs&gt;d__NN 的版本号。
        /// </summary>
        private static bool InstallDefsTiming()
        {
            try
            {
                MethodInfo loadDefs = AccessTools.Method(typeof(ModContentPack), "LoadDefs");
                if (loadDefs == null)
                {
                    Log.Warning("[RimThreadedTTR] FastLoad: 找不到 ModContentPack.LoadDefs");
                    return false;
                }

                Type machine = null;
                object[] attributes = loadDefs.GetCustomAttributes(
                    typeof(System.Runtime.CompilerServices.IteratorStateMachineAttribute), false);
                if (attributes != null && attributes.Length > 0)
                {
                    machine = ((System.Runtime.CompilerServices.IteratorStateMachineAttribute)attributes[0]).StateMachineType;
                }
                if (machine == null)
                {
                    Log.Warning("[RimThreadedTTR] FastLoad: LoadDefs 上找不到 IteratorStateMachineAttribute");
                    return false;
                }

                MethodInfo moveNext = AccessTools.Method(machine, "MoveNext");
                if (moveNext == null)
                {
                    Log.Warning("[RimThreadedTTR] FastLoad: 状态机 " + machine.Name + " 上没有 MoveNext");
                    return false;
                }

                MethodInfo before = AccessTools.Method(typeof(Patches), "LoadDefsMoveNext_Begin");
                MethodInfo finalizer = AccessTools.Method(typeof(Patches), "LoadDefsMoveNext_Finalizer");
                Harm.Patch(moveNext,
                    prefix: before == null ? null : new HarmonyMethod(before),
                    finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
                Profiler.Mark("defs 计时已挂到状态机 " + machine.Name + ".MoveNext");
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 挂 defs 计时失败: " + e.Message);
                return false;
            }
        }

        /// <summary>用 Patches 里的静态方法（begin/end）挂一对前后缀。</summary>
        private static bool PatchStatics(string typeName, string methodName, string patchPrefix)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 找不到类型 " + typeName);
                return false;
            }

            MethodInfo target = AccessTools.Method(type, methodName);
            if (target == null)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 找不到方法 " + typeName + "." + methodName);
                return false;
            }

            MethodInfo before = AccessTools.Method(typeof(Patches), patchPrefix + "_Begin");
            MethodInfo after = AccessTools.Method(typeof(Patches), patchPrefix + "_End");
            return TryPatch(target, typeName + "." + methodName, before, after);
        }

        /// <summary>把 typeNames 里每个存在的方法都挂上（返回成功个数）。</summary>
        private static int PatchAll(string[] typeNames, string methodName, string patchPrefix)
        {
            int patched = 0;
            foreach (string typeName in typeNames)
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null) continue;
                MethodInfo target = AccessTools.Method(type, methodName);
                if (target == null) continue;
                MethodInfo after = AccessTools.Method(typeof(Patches), patchPrefix + "_Postfix");
                if (TryPatch(target, typeName + "." + methodName, null, after))
                {
                    patched++;
                    // 同一方法再挂一个"强制 4 档"的后缀（每帧执行）
                    MethodInfo force = AccessTools.Method(typeof(Patches), "ForceSpeed_Postfix");
                    if (force != null) TryPatch(target, typeName + "." + methodName + ":forceSpeed", null, force);
                    MethodInfo autoLoad = AccessTools.Method(typeof(Patches), "AutoLoad_Postfix");
                    if (autoLoad != null) TryPatch(target, typeName + "." + methodName + ":autoLoad", null, autoLoad);
                    MethodInfo audit = AccessTools.Method(typeof(Patches), "AuditPatches_Postfix");
                    if (audit != null) TryPatch(target, typeName + "." + methodName + ":audit", null, audit);
                }
            }
            return patched;
        }

        /// <summary>挂补丁。补丁方法必须是 static（lambda/闭包会生成实例方法 ⇒ 非法 IL）。</summary>
        private static bool TryPatch(MethodBase target, string label, MethodInfo before, MethodInfo after)
        {
            try
            {
                if (HarmonyLib.Harmony.GetPatchInfo(target) != null)
                {
                    // 已有别的 mod patch 过：记录但不阻止（Harmony 支持多补丁）
                    Profiler.Mark("目标已有补丁: " + label);
                }
                Harm.Patch(target,
                    prefix: before == null ? null : new HarmonyMethod(before),
                    postfix: after == null ? null : new HarmonyMethod(after));
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 挂钩 " + label + " 失败: " + e.Message);
                return false;
            }
        }
    }

    public class FastLoadSettings : ModSettings
    {
        public bool profileEnabled = true;
        public bool xpathFastPath = true;
        public bool staticCtorTiming = true;
        public bool runtimeProfiling = true;
        public bool runtimeProfilingHotThings;
        /// <summary>极简 TPS 仪表：只测每 tick/每帧总时长与实测 TPS，开销可忽略。</summary>
        public bool runtimeProfilingMinimal;
        /// <summary>轻量探测：只挂 MapPostTick 的 4 个子系统（定位那 234 µs/tick）。</summary>
        public bool runtimeProfilingMapPost;
        /// <summary>FPS 探测：只挂"每帧"入口（MapUpdate / UI / 小人栏 / 警报），用于定位帧时间。</summary>
        public bool runtimeProfilingFrames;
        /// <summary>测试用：强制时间档 = 4（Ultrafast）。Smart Speed 配置下等于 900 TPS。</summary>
        public bool forceUltrafast;
        /// <summary>调试：主菜单自动载入存档（配合 /tmp/ttr-loadsave）。</summary>
        public bool autoLoadSave;
        /// <summary>调试：审计 Harmony 补丁（找出"睡着的优化"）。</summary>
        public bool auditPatches;
        /// <summary>热路径采样率：1 = 精确（会卡，只用于测量）；100 = 每 100 次记 1 次。</summary>
        public int runtimeProfilingSampleRate = 100;
        /// <summary>TPS 优化：纯视觉降频（Fleck/Effecter/小人特效 → 每 2 tick）。</summary>
        public bool tpsOptimizeVisual;
        /// <summary>TPS 优化：模拟类降频（风 → 每 4 tick；气体/搬运清单 → 每 2 tick）。</summary>
        public bool tpsOptimizeSimulation;
        /// <summary>并行 tick 试点（把 EquipmentTrackerTick / NativeVerbsTick 收集到 tick 末尾并行执行）。</summary>
        public bool tpsParallelPawnTick;
        public int tpsParallelDegree = 4;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref profileEnabled, "profileEnabled", true);
            Scribe_Values.Look(ref xpathFastPath, "xpathFastPath", true);
            Scribe_Values.Look(ref staticCtorTiming, "staticCtorTiming", true);
            Scribe_Values.Look(ref runtimeProfiling, "runtimeProfiling", true);
            Scribe_Values.Look(ref runtimeProfilingHotThings, "runtimeProfilingHotThings", false);
            Scribe_Values.Look(ref runtimeProfilingMinimal, "runtimeProfilingMinimal", false);
            Scribe_Values.Look(ref runtimeProfilingMapPost, "runtimeProfilingMapPost", false);
            Scribe_Values.Look(ref runtimeProfilingFrames, "runtimeProfilingFrames", false);
            Scribe_Values.Look(ref forceUltrafast, "forceUltrafast", false);
            Scribe_Values.Look(ref autoLoadSave, "autoLoadSave", false);
            Scribe_Values.Look(ref auditPatches, "auditPatches", false);
            Scribe_Values.Look(ref runtimeProfilingSampleRate, "runtimeProfilingSampleRate", 100);
            Scribe_Values.Look(ref tpsOptimizeVisual, "tpsOptimizeVisual", false);
            Scribe_Values.Look(ref tpsOptimizeSimulation, "tpsOptimizeSimulation", false);
            Scribe_Values.Look(ref tpsParallelPawnTick, "tpsParallelPawnTick", false);
            Scribe_Values.Look(ref tpsParallelDegree, "tpsParallelDegree", 4);
            base.ExposeData();
        }
    }
}

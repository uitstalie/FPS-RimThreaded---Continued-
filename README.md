# FPS-RimThreaded---Continued-

`uitstalie.rimthreadedttr.continued` —— 合并版 mod：**RimThreaded TTR（线程/降频）+ FPS+（缓存/节流）+ FastLoad（启动计时 / XPath 快路径）**在同一个 DLL、同一个设置界面里。

## 目录

- `Source_1.5/` —— 全部 C# 源码（`RimThreadedTTR.csproj` 用默认 `**/*.cs` 通配）
  - `Source_1.5/FastLoad/` —— 从 `RimWorld-FastLoad-uitstalie` 并入的 8 个文件。
    `FastLoadMod` 已由 `Mod` 子类改为**静态类**，由 `TTRMod` 构造函数调用
    `FastLoadMod.Init(settings.fastLoadSettings)`（时机 = `CreateModClasses`，与原来独立 mod 一致）。
- `1.6/Assemblies/RimThreadedTTR.dll` —— 部署产物（也是游戏 `Mods/RimThreadedTTR-Continued/1.6/Assemblies/` 的来源）。
- `1.6/Languages/ChineseSimplified/Keyed/FPSPlus_zh.xml` —— 唯一一份中文键（含 FastLoad 页新键）。
- `About/About.xml` —— name `RimThreaded TTR (Continued)`，packageId `uitstalie.rimthreadedttr.continued`。

## 构建

```bash
cd Source_1.5 && dotnet build RimThreadedTTR.csproj
# → ../1.6/Assemblies/RimThreadedTTR.dll（0 错误 0 警告）
```

`TTR_MERGED` 在 csproj 里定义 ⇒ FPS+ 的设置界面/设置对象作为 `TTRSettings.fpsSettings`
嵌进来，`FPSPlusMod` 不再是 `Mod` 子类（**全程序集只有 `TTRMod` 一个 Mod 子类**）。

## 设置

- 唯一设置文件：`<配置目录>/Mod_RimThreadedTTR-Continued_TTRMod.xml`
- 唯一入口：`选项 > Mod 设置 > "FPS+ | RimThreaded"`
  - 顶部两个标签：**Threading**（线程/降频）与 **FPS+ (performance)**
  - FPS+ 页内 7 个子页：Main / Interface / Gameplay / Cleanup / Doctor / Advanced / **FastLoad**
- FastLoad 的启动期开关（计时/归因/降频）在游戏启动时读取 ⇒ 改完需重启；`forceUltrafast` /
  `autoLoadSave` / `auditPatches` 立即生效。

## 诊断

- 启动/加载耗时报告：`<配置目录>/FastLoad-Startup.txt`
- 并行/实验性开关的强制开启用 `/tmp/ttr-*` 旗帜文件（`/tmp/ttr-p2-all`、`/tmp/ttr-hediff`、
  `/tmp/ttr-region` 等）；**默认都不存在 ⇒ Pawn 并行、HediffSet 锁、区域锁都不会被安装**。

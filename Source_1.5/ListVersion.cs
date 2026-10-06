using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RimThreadedTTR
{
    /// <summary>
    /// B2 总开关（对应 TTRSettings.optListVersion）。关闭时 <see cref="ListVersion{T}.TryGet"/> 恒返回
    /// false ⇒ 调用方一律保守重算（退回到版本键之前的行为，不产生错误结果）。
    /// </summary>
    public static class ListVersion
    {
        public static bool Enabled = true;
    }

    /// <summary>
    /// B2：用 `List&lt;T&gt;._version`（mscorlib 的私有字段）做**精确且极廉价**的缓存失效键。
    ///
    /// 反编译实证（本机 1.6.4871 的 `RimWorldLinux_Data/Managed/mscorlib.dll`）：
    /// <code>
    /// public class List&lt;T&gt; {
    ///     private T[] _items;
    ///     private int _size;
    ///     private int _version;      // ← 每次结构性修改（Add/Remove/RemoveAt/Insert/Clear/Sort…）自增
    ///     public int Count => _size;
    /// }
    /// </code>
    /// `_version` 只在**内容真的变了**的时候自增：读一次 int 就能判断"这个 List 自上次以来是否变过"，
    /// 比 TTL 更准（内容没变就不重算），比脏标记更省（不需要给 vanilla 的每个写入点挂钩子）。
    ///
    /// 实现方式：`AccessTools.FieldRefAccess`（**不引入 Krafs.Publicizer**，不改游戏程序集）。
    /// 三级保险，确保"读错只会保守重算、不会给出错误结果"：
    ///   1. 反射取字段/建委托失败 ⇒ <see cref="TryGet"/> 恒返回 false ⇒ 调用方退回原逻辑；
    ///   2. 每个泛型实例化第一次使用时跑一次 <see cref="RunSelfTest"/>：真的 Add 一项并确认
    ///      `_version` 变了。自检不过 ⇒ 永久返回 false（**不会**出现"永远读到 0 于是永远认为没变"）；
    ///   3. 调用方约定：TryGet 返回 false 或版本不相等 ⇒ **重算**（保守）；只有返回 true 且版本相等
    ///      才允许复用缓存。
    /// </summary>
    public static class ListVersion<T>
    {
        private static readonly AccessTools.FieldRef<List<T>, int> Reader = CreateReader();
        private static bool selfTestDone;
        private static bool usable;

        private static AccessTools.FieldRef<List<T>, int> CreateReader()
        {
            try
            {
                // List<T> 自己声明了 _version（不是继承来的），FieldRefAccess 可直接取到私有实例字段。
                return AccessTools.FieldRefAccess<List<T>, int>("_version");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>本进程内 `List&lt;T&gt;._version` 是否可用（自检通过）。用于 UI / 日志展示。</summary>
        public static bool Available
        {
            get
            {
                EnsureSelfTest();
                return usable;
            }
        }

        private static void EnsureSelfTest()
        {
            if (selfTestDone)
            {
                return;
            }
            selfTestDone = true;
            if (Reader == null)
            {
                usable = false;
                return;
            }
            try
            {
                List<T> probe = new List<T>();
                int before = Reader(probe);
                probe.Add(default(T));          // 结构性修改 ⇒ _version 必须变
                int after = Reader(probe);
                usable = before != after;
            }
            catch
            {
                usable = false;
            }
        }

        /// <summary>
        /// 读版本号。返回 false ⇒ 调用方必须当作"内容可能已变"（保守重算），不得复用缓存。
        /// </summary>
        public static bool TryGet(List<T> list, out int version)
        {
            version = 0;
            if (list == null || !ListVersion.Enabled)
            {
                return false;
            }
            EnsureSelfTest();
            if (!usable)
            {
                return false;
            }
            try
            {
                version = Reader(list);
                return true;
            }
            catch
            {
                usable = false;
                return false;
            }
        }
    }
}

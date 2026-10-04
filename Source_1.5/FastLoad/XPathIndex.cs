using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace FastLoad
{
    /// <summary>
    /// XPath 快速路径：把常见的 <c>Defs/&lt;Type&gt;[defName="X"]</c> 前缀变成索引查找。
    ///
    /// 背景（本机实测）：RimWorld 应用 XML 补丁时用的是
    /// <c>System.Xml.XmlNode.SelectNodes(string)</c>；Mono 每次调用都会**重新编译** XPath 表达式，
    /// 并在"所有 mod 合并后那棵 ~33 MB 的 XML 树"上做**全树扫描**。382 个 mod 共 28,931 个补丁操作，
    /// 分摊约 7.6 ms/次 ⇒ `ApplyPatches` 220 秒。
    ///
    /// 这里做两件事（都保持语义等价，命中不了就退回原实现）：
    /// 1. **defName 索引**：`Defs/&lt;Type&gt;[defName="X"]` 直接查表，只把剩余片段放到子树里求值；
    /// 2. **表达式缓存**：其余路径复用已编译的 <see cref="XPathExpression"/>，省掉重复编译。
    ///
    /// 安全性：只在上下文是 <see cref="XmlDocument"/>、xpath 完全匹配上述形态、
    /// 节点仍挂在同一文档且 defName 未变、且没有重名 def 时命中；其余一律返回 false 由调用方走原路径。
    /// </summary>
    public static class XPathIndex
    {
        /// <summary>Defs/&lt;Type&gt;[defName="X"]…（defName 是子元素）</summary>
        private static readonly Regex DefLookup = new Regex(
            @"^/?(?<defs>Defs)/(?<type>[A-Za-z_][\w\.\+]*)\[(?<key>defName|@Name)\s*=\s*(?<q>[""'])(?<name>[^""']*)\k<q>\]\s*(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly object Gate = new object();
        private static Index _index;
        private static readonly Dictionary<string, XPathExpression> ExprCache =
            new Dictionary<string, XPathExpression>(StringComparer.Ordinal);

        // 统计（写进 FastLoad 报告）
        public static long FastHits;
        public static long Fallbacks;
        public static long ExprCacheHits;
        public static long IndexBuilds;
        public static long MissStreak;

        private sealed class Index
        {
            public XmlDocument Doc;
            public readonly Dictionary<string, XmlNode> Map =
                new Dictionary<string, XmlNode>(StringComparer.Ordinal);
            public readonly HashSet<string> Ambiguous = new HashSet<string>(StringComparer.Ordinal);
        }

        private static string Key(string type, string name)
        {
            return type + "\u0001" + name;
        }

        /// <summary>取（并缓存）编译好的 XPath 表达式。</summary>
        public static XPathExpression Compiled(string xpath)
        {
            lock (Gate)
            {
                XPathExpression expr;
                if (ExprCache.TryGetValue(xpath, out expr))
                {
                    ExprCacheHits++;
                    return expr;
                }
                expr = XPathExpression.Compile(xpath);
                ExprCache[xpath] = expr;
                return expr;
            }
        }

        /// <summary>
        /// 尝试用索引解析 <paramref name="xpath"/>。
        ///
        /// :param context: 调用 SelectNodes/SelectSingleNode 的节点（只有 XmlDocument 才走快速路径）
        /// :param xpath: 待求值的表达式
        /// :param results: 命中时返回结果节点（按文档顺序）
        /// :return: 是否命中快速路径
        /// </summary>
        public static bool TrySelect(XmlNode context, string xpath, out List<XmlNode> results)
        {
            results = null;
            XmlDocument doc = context as XmlDocument;
            if (doc == null || string.IsNullOrEmpty(xpath)) return false;

            Match m = DefLookup.Match(xpath);
            if (!m.Success) return false;

            string rest = m.Groups["rest"].Value;
            // 剩余片段只允许 "/child..." 形式；出现 "//" 或其它形态就退回原实现
            if (rest.Length > 0)
            {
                if (rest[0] != '/' || rest.StartsWith("//", StringComparison.Ordinal)) return false;
                if (rest.IndexOf("//", StringComparison.Ordinal) >= 0) return false;
            }

            string type = m.Groups["type"].Value;
            string name = m.Groups["name"].Value;
            bool byAttribute = m.Groups["key"].Value == "@Name";
            string key = Key(type, byAttribute ? "@" + name : name);

            XmlNode node;
            lock (Gate)
            {
                Index idx = GetIndexLocked(doc);
                if (idx.Ambiguous.Contains(key)) return false;
                if (!idx.Map.TryGetValue(key, out node)) return false;
            }

            // 校验：仍在同一文档、被索引的那个标识未变
            if (node.OwnerDocument != doc) return false;
            if (byAttribute)
            {
                XmlElement element = node as XmlElement;
                if (element == null || element.GetAttribute("Name") != name) return false;
            }
            else
            {
                XmlNode defNameNode = node["defName"];
                if (defNameNode == null || defNameNode.InnerText != name) return false;
            }

            var list = new List<XmlNode>(1);
            if (rest.Length == 0)
            {
                list.Add(node);
            }
            else
            {
                XmlNodeList sub = node.SelectNodes(rest.Substring(1));
                if (sub == null) return false;
                foreach (XmlNode child in sub) list.Add(child);
            }
            results = list;
            return true;
        }

        /// <summary>把缺失的查询计入"未命中"，连续多次未命中时重建索引（补丁会新增 def）。</summary>
        public static void NoteMiss()
        {
            if (++MissStreak >= 500)
            {
                MissStreak = 0;
                lock (Gate) { _index = null; }
            }
        }

        private static void AddToIndex(Index idx, string key, XmlNode element)
        {
            if (idx.Map.ContainsKey(key)) idx.Ambiguous.Add(key);
            else idx.Map[key] = element;
        }

        private static Index GetIndexLocked(XmlDocument doc)
        {
            if (_index != null && ReferenceEquals(_index.Doc, doc)) return _index;

            var idx = new Index { Doc = doc };
            XmlElement root = doc.DocumentElement;
            if (root != null && root.Name == "Defs")
            {
                foreach (XmlNode def in root.ChildNodes)
                {
                    XmlElement element = def as XmlElement;
                    if (element == null) continue;
                    XmlNode nameNode = element["defName"];
                    if (nameNode != null && nameNode.InnerText.Length > 0)
                    {
                        AddToIndex(idx, Key(element.Name, nameNode.InnerText), element);
                    }
                    string attrName = element.GetAttribute("Name");
                    if (!string.IsNullOrEmpty(attrName))
                    {
                        AddToIndex(idx, Key(element.Name, "@" + attrName), element);
                    }
                }
            }
            IndexBuilds++;
            _index = idx;
            return idx;
        }
    }
}

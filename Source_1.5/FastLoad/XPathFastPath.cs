using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using HarmonyLib;
using Verse;

namespace FastLoad
{
    /// <summary>
    /// 给 <see cref="XmlNode.SelectNodes(string)"/> / <see cref="XmlNode.SelectSingleNode(string)"/>
    /// 挂上前缀，走 <see cref="XPathIndex"/> 的快速路径；命中不了就用编译好的表达式求值，
    /// 省掉"每次重新编译 XPath"的开销。任何异常都退回原实现，绝不影响游戏。
    /// </summary>
    public static class XPathFastPath
    {
        private static bool _installed;

        public static bool Install(Harmony harmony)
        {
            if (_installed) return true;
            try
            {
                MethodInfo selectNodes = typeof(XmlNode).GetMethod(
                    "SelectNodes", new[] { typeof(string) });
                MethodInfo selectSingle = typeof(XmlNode).GetMethod(
                    "SelectSingleNode", new[] { typeof(string) });

                if (selectNodes != null)
                {
                    harmony.Patch(selectNodes,
                        prefix: new HarmonyMethod(typeof(XPathFastPath).GetMethod(
                            nameof(SelectNodes_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                if (selectSingle != null)
                {
                    harmony.Patch(selectSingle,
                        prefix: new HarmonyMethod(typeof(XPathFastPath).GetMethod(
                            nameof(SelectSingleNode_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                _installed = selectNodes != null && selectSingle != null;
                Log.Message("[RimThreadedTTR] FastLoad: XPath 快速路径已安装: SelectNodes=" + (selectNodes != null)
                            + " SelectSingleNode=" + (selectSingle != null));
                return _installed;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 安装 XPath 快速路径失败: " + e.Message);
                return false;
            }
        }

        private static bool SelectNodes_Prefix(XmlNode __instance, string xpath, ref XmlNodeList __result)
        {
            try
            {
                List<XmlNode> hits;
                if (XPathIndex.TrySelect(__instance, xpath, out hits))
                {
                    XPathIndex.FastHits++;
                    __result = new NodeList(hits);
                    return false;
                }
                XPathIndex.NoteMiss();
                XPathIndex.Fallbacks++;
                __result = new NodeList(Evaluate(__instance, XPathIndex.Compiled(xpath)));
                return false;
            }
            catch (Exception)
            {
                return true;   // 退回原实现
            }
        }

        private static bool SelectSingleNode_Prefix(XmlNode __instance, string xpath, ref XmlNode __result)
        {
            try
            {
                List<XmlNode> hits;
                if (XPathIndex.TrySelect(__instance, xpath, out hits))
                {
                    XPathIndex.FastHits++;
                    __result = hits.Count > 0 ? hits[0] : null;
                    return false;
                }
                XPathIndex.NoteMiss();
                XPathIndex.Fallbacks++;
                List<XmlNode> evaluated = Evaluate(__instance, XPathIndex.Compiled(xpath));
                __result = evaluated.Count > 0 ? evaluated[0] : null;
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 用编译好的表达式求值（net48 的 XmlNode 没有 SelectNodes(XPathExpression) 重载）。
        ///
        /// :param node: 上下文节点
        /// :param expr: 已编译的表达式
        /// :return: 命中的节点（文档顺序）
        /// </summary>
        private static List<XmlNode> Evaluate(XmlNode node, XPathExpression expr)
        {
            var list = new List<XmlNode>();
            XPathNavigator navigator = node.CreateNavigator();
            if (navigator == null) return list;
            XPathNodeIterator iterator = navigator.Select(expr);
            while (iterator.MoveNext())
            {
                IHasXmlNode hasNode = iterator.Current as IHasXmlNode;
                if (hasNode == null) continue;
                XmlNode current = hasNode.GetNode();
                if (current != null) list.Add(current);
            }
            return list;
        }

        /// <summary>把 List&lt;XmlNode&gt; 包成 XmlNodeList（ReadOnlyCollection 语义）。</summary>
        private sealed class NodeList : XmlNodeList
        {
            private readonly List<XmlNode> _items;

            public NodeList(List<XmlNode> items) { _items = items; }

            public override int Count { get { return _items.Count; } }

            public override XmlNode Item(int index) { return _items[index]; }

            public override IEnumerator GetEnumerator() { return _items.GetEnumerator(); }
        }
    }
}

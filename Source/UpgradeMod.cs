using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;

namespace Vini.Upgrade
{
    public class Main : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            new Harmony("vini.upgrade").PatchAll();
            UpgradeConfig.Load(modInstance);
        }
    }

    [HarmonyPatch(typeof(XUiC_ItemStack))]
    public static class ItemStack_OnPress
    {
        static MethodBase? TargetMethod() =>
            AccessTools.Method(typeof(XUiC_ItemStack), "OnPress", new [] { typeof(string) });

        static bool Prefix(XUiC_ItemStack __instance, string _buttonName)
        {
            if (_buttonName != "upgrade_item")
                return true;

            var stack = __instance.ItemStack;
            if (stack == null || stack.itemValue == null)
                return false;

            // ↓ Em vez de __instance.xui.PlayerUI...
            var world = GameManager.Instance.World;
            var player = world?.GetPrimaryPlayer() as EntityPlayerLocal;
            if (player == null)
                return false;

            if (UpgradeLogic.TryUpgrade(stack.itemValue, player))
            {
                player.PlayOneShot("use_action");
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeOk"));
            }
            else
            {
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeFail"));
            }
            return false;
        }
    }

    // Support for game versions where OnPress no longer provides the command string
    [HarmonyPatch(typeof(XUiC_ItemStack))]
    public static class ItemStack_OnPress_New
    {
        static MethodBase? TargetMethod() =>
            AccessTools.Method(typeof(XUiC_ItemStack), "OnPress", new [] { typeof(XUiController), typeof(int) });

        static bool Prefix(XUiC_ItemStack __instance, XUiController _sender, int _mouseButton)
        {
            // Some versions pass the pressed button controller instead of a string
            // Access the identifier via reflection to support multiple game versions
            string? id = null;
            if (_sender != null)
            {
                var t = _sender.GetType();
                id = t.GetProperty("Id")?.GetValue(_sender) as string
                     ?? t.GetField("Id")?.GetValue(_sender) as string
                     ?? t.GetProperty("name")?.GetValue(_sender) as string
                     ?? t.GetField("name")?.GetValue(_sender) as string;
            }
            if (id != "btnUpgrade")
                return true;

            var stack = __instance.ItemStack;
            if (stack == null || stack.itemValue == null)
                return false;

            var world = GameManager.Instance.World;
            var player = world?.GetPrimaryPlayer() as EntityPlayerLocal;
            if (player == null)
                return false;

            if (UpgradeLogic.TryUpgrade(stack.itemValue, player))
            {
                player.PlayOneShot("use_action");
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeOk"));
            }
            else
            {
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeFail"));
            }
            return false;
        }
    }

    public static class UpgradeLogic
    {
        public static bool TryUpgrade(ItemValue item, EntityPlayerLocal player)
        {
            // First check if the item should transform into another
            var transform = UpgradeConfig.FindTransform(item.ItemClass.Name);
            if (transform != null)
            {
                if (!ConsumeCost(player, transform.Cost))
                    return false;

                UpgradeConfig.ApplyTransform(item, transform.To);
                return true;
            }

            var rule = UpgradeConfig.FindRuleFor(item);
            if (rule == null)
                return false;

            // Require that the player knows how to craft this item (gated by books/recipes)
            if (!IsRecipeKnown(player, item.ItemClass))
                return false;

            if (item.Quality >= rule.MaxQuality)
                return false;

            if (!ConsumeCost(player, rule.Cost))
                return false;

            // Quality é ushort → faça cast e clamp
            int newQ = item.Quality + rule.QualityStep;
            if (newQ > rule.MaxQuality) newQ = rule.MaxQuality;
            item.Quality = (ushort)newQ;

            return true;
        }

        private static bool IsRecipeKnown(EntityPlayerLocal player, ItemClass itemClass)
        {
            // Use reflection to support different game versions
            var method = player.GetType().GetMethod("IsRecipeKnown", new[] { typeof(ItemClass) });
            if (method != null)
                return (bool)method.Invoke(player, new object[] { itemClass });

            method = player.GetType().GetMethod("IsRecipeKnown", new[] { typeof(string) });
            if (method != null)
                return (bool)method.Invoke(player, new object[] { itemClass.Name });

            // If the API is unavailable, treat as unknown recipe
            return false;
        }

        private static bool ConsumeCost(EntityPlayerLocal player, UpgradeCost cost)
        {
            if (cost.Items.Count == 0 && cost.Dukes == 0)
                return true;

            var inventory = player.inventory;
            var invType = inventory.GetType();

            // métodos comuns: GetItemCount(ItemClass,bool,int) e RemoveItems(ItemClass,int)
            var getCount = invType.GetMethod("GetItemCount", new[] { typeof(ItemClass), typeof(bool), typeof(int) });
            var remove = invType.GetMethod("RemoveItems", new[] { typeof(ItemClass), typeof(int) });
            if (getCount == null || remove == null)
                return false;

            foreach (var kvp in cost.Items)
            {
                var cls = ItemClass.GetItem(kvp.Key).ItemClass;
                int have = (int)getCount.Invoke(inventory, new object[] { cls, false, -1 });
                if (have < kvp.Value)
                    return false;
            }
            if (cost.Dukes > 0)
            {
                var dukesCls = ItemClass.GetItem("casinoCoin").ItemClass;
                int have = (int)getCount.Invoke(inventory, new object[] { dukesCls, false, -1 });
                if (have < cost.Dukes)
                    return false;
            }

            foreach (var kvp in cost.Items)
            {
                var cls = ItemClass.GetItem(kvp.Key).ItemClass;
                remove.Invoke(inventory, new object[] { cls, kvp.Value });
            }
            if (cost.Dukes > 0)
            {
                var dukesCls = ItemClass.GetItem("casinoCoin").ItemClass;
                remove.Invoke(inventory, new object[] { dukesCls, cost.Dukes });
            }

            return true;
        }
    }

    public static class UpgradeConfig
    {
        // C# 8: escreva o tipo completo (evita CS8400)
        private static readonly Dictionary<string, UpgradeRule> Rules = new Dictionary<string, UpgradeRule>();
        private static readonly Dictionary<string, UpgradeRule> ItemRules = new Dictionary<string, UpgradeRule>();
        private static readonly Dictionary<string, TransformPath> Paths = new Dictionary<string, TransformPath>();

        public static void Load(Mod mod)
        {
            Rules.Clear();
            ItemRules.Clear();
            Paths.Clear();
            var path = Path.Combine(mod.Path, "Config/upgrade_rules.xml");
            if (!File.Exists(path))
                return;

            var doc = XDocument.Load(path);
            foreach (var ruleNode in doc.Root!.Elements("rule"))
            {
                var group = ruleNode.Attribute("group")?.Value;
                if (string.IsNullOrEmpty(group))
                    continue;

                var maxQuality = (int?)ruleNode.Attribute("maxQuality") ?? 6;
                var step = (int?)ruleNode.Element("quality")?.Attribute("step") ?? 1;

                var rule = new UpgradeRule { Group = group, MaxQuality = maxQuality, QualityStep = step };

                // match itens específicos
                foreach (var match in ruleNode.Element("match")?.Elements("item") ?? Array.Empty<XElement>())
                {
                    var name = match.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        rule.MatchItems.Add(name);
                        ItemRules[name] = rule;
                    }
                }

                // custos
                var costNode = ruleNode.Element("cost");
                if (costNode != null)
                {
                    foreach (var itemNode in costNode.Elements("item"))
                    {
                        var n = itemNode.Attribute("name")?.Value;
                        var c = (int?)itemNode.Attribute("count") ?? 0;
                        if (!string.IsNullOrEmpty(n) && c > 0)
                            rule.Cost.Items[n] = c;
                    }
                    var dukesAttr = costNode.Element("dukes")?.Attribute("count");
                    if (dukesAttr != null)
                        rule.Cost.Dukes = (int)dukesAttr;
                }

                Rules[group] = rule;
            }

            // transform paths
            foreach (var pathNode in doc.Root!.Element("transform")?.Elements("path") ?? Array.Empty<XElement>())
            {
                var fromNode = pathNode.Element("from");
                if (fromNode == null)
                    continue;

                var from = fromNode.Attribute("name")?.Value;
                var to = fromNode.Attribute("to")?.Value;
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    continue;

                var p = new TransformPath { From = from, To = to };
                var costNode = pathNode.Element("cost");
                if (costNode != null)
                {
                    foreach (var itemNode in costNode.Elements("item"))
                    {
                        var n = itemNode.Attribute("name")?.Value;
                        var c = (int?)itemNode.Attribute("count") ?? 0;
                        if (!string.IsNullOrEmpty(n) && c > 0)
                            p.Cost.Items[n] = c;
                    }
                    var dukesAttr = costNode.Element("dukes")?.Attribute("count");
                    if (dukesAttr != null)
                        p.Cost.Dukes = (int)dukesAttr;
                }
                Paths[from] = p;
            }
        }
        public static UpgradeRule? FindRuleFor(ItemValue item)
        {
            if (item.ItemClass == null)
                return null;

            var name = item.ItemClass.Name;
            if (ItemRules.TryGetValue(name, out var direct))
                return direct;

            // FastTags não tem Contains(string) → fallback por string
            var tagStr = item.ItemClass.ItemTags.ToString(); // ex: "weapon,melee,iron"
            bool hasWeapon = tagStr.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasTool   = tagStr.IndexOf("tool",   StringComparison.OrdinalIgnoreCase) >= 0;

            if (hasWeapon && Rules.TryGetValue("weapons", out var weaponRule))
                return weaponRule;
            if (hasTool && Rules.TryGetValue("tools", out var toolRule))
                return toolRule;

            return null;
        }

        public static TransformPath? FindTransform(string itemName)
        {
            Paths.TryGetValue(itemName, out var p);
            return p;
        }

        public static void ApplyTransform(ItemValue item, string toName)
        {
            var newItem = ItemClass.GetItem(toName);
            var method = item.GetType().GetMethod("SetItemClass", new[] { typeof(ItemClass), typeof(int), typeof(bool) });
            if (method != null)
            {
                method.Invoke(item, new object[] { newItem.ItemClass, item.Quality, true });
            }
        }
    }

    public class UpgradeRule
    {
        public string Group = string.Empty;
        public int MaxQuality;
        public int QualityStep = 1;
        public UpgradeCost Cost = new UpgradeCost();
        public HashSet<string> MatchItems = new HashSet<string>();
    }

    public class UpgradeCost
    {
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
        public int Dukes;
    }

    public class TransformPath
    {
        public string From = string.Empty;
        public string To = string.Empty;
        public UpgradeCost Cost = new UpgradeCost();
    }
}

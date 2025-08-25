using System;
using System.Collections.Generic;
using System.IO;
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

    [HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.OnPress))]
    public static class ItemStack_OnPress
    {
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

    public static class UpgradeLogic
    {
        public static bool TryUpgrade(ItemValue item, EntityPlayerLocal player)
        {
            var rule = UpgradeConfig.FindRuleFor(item);
            if (rule == null)
                return false;

            if (item.Quality >= rule.MaxQuality)
                return false;

            // Consumo de materiais: TODO

            // Quality é ushort → faça cast e clamp
            int newQ = item.Quality + rule.QualityStep;
            if (newQ > rule.MaxQuality) newQ = rule.MaxQuality;
            item.Quality = (ushort)newQ;

            return true;
        }
    }

    public static class UpgradeConfig
    {
        // C# 8: escreva o tipo completo (evita CS8400)
        private static readonly Dictionary<string, UpgradeRule> Rules = new Dictionary<string, UpgradeRule>();

        public static void Load(Mod mod)
        {
            Rules.Clear();
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

                Rules[group] = new UpgradeRule { Group = group, MaxQuality = maxQuality, QualityStep = step };
            }
        }

        public static UpgradeRule? FindRuleFor(ItemValue item)
        {
            if (item.ItemClass == null)
                return null;

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
    }

    public class UpgradeRule
    {
        public string Group = string.Empty;
        public int MaxQuality;
        public int QualityStep = 1;
    }
}

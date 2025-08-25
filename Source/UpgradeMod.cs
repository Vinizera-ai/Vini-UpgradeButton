using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;

// Namespace matches dll name
namespace Vini.Upgrade
{
    // Entry point called by the game when loading the mod
    public class Main : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            new Harmony("vini.upgrade").PatchAll();
            UpgradeConfig.Load(modInstance); // parse upgrade_rules.xml
        }
    }

    // Handles the UPGRADE button action from the XUi window
    [HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.OnPress))]
    public static class ItemStack_OnPress
    {
        static bool Prefix(XUiC_ItemStack __instance, string _buttonName)
        {
            if (_buttonName != "upgrade_item")
                return true; // let the game handle other buttons

            var stack = __instance.ItemStack;
            if (stack == null || stack.itemValue == null)
                return false;

            var player = __instance.xui.PlayerUI.entityPlayerLocal;
            if (UpgradeLogic.TryUpgrade(stack.itemValue, player))
            {
                player.PlayOneShot("use_action");
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeOk"));
            }
            else
            {
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeFail"));
            }
            return false; // skip original handling
        }
    }

    // Contains the upgrade algorithm using loaded rules
    public static class UpgradeLogic
    {
        public static bool TryUpgrade(ItemValue item, EntityPlayerLocal player)
        {
            var rule = UpgradeConfig.FindRuleFor(item);
            if (rule == null)
                return false;
            if (item.Quality >= rule.MaxQuality)
                return false;

            // TODO: consume materials defined in upgrade_rules.xml
            item.Quality += rule.QualityStep;
            return true;
        }
    }

    // Parses upgrade_rules.xml into simple in-memory structures
    public static class UpgradeConfig
    {
        private static readonly Dictionary<string, UpgradeRule> Rules = new();

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
            // check basic tags to map to rule group
            if (item.ItemClass.ItemTags.Contains("weapon") && Rules.TryGetValue("weapons", out var weaponRule))
                return weaponRule;
            if (item.ItemClass.ItemTags.Contains("tool") && Rules.TryGetValue("tools", out var toolRule))
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

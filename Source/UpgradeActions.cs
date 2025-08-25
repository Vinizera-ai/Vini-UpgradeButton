using HarmonyLib;

namespace Vini.Upgrade
{
    public static class UpgradeActions
    {
        public static bool IsEligibleForUpgrade(ItemStack stack)
        {
            var item = stack?.itemValue;
            if (item?.ItemClass == null)
                return false;

            if (UpgradeConfig.FindTransform(item.ItemClass.Name) != null)
                return true;

            return UpgradeConfig.FindRuleFor(item) != null;
        }

        public static void TryOpenUpgradeUI(XUiController? source, ItemStack stack)
        {   if (source == null) return;
            if (stack == null || stack.itemValue == null)
                return;
            var world = GameManager.Instance.World;
            var player = world?.GetPrimaryPlayer() as EntityPlayerLocal;
            if (player == null)
                return;
            if (UpgradeLogic.TryUpgrade(stack.itemValue, player))
            {
                player.PlayOneShot("use_action");
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeOk"));
            }
            else
            {
                GameManager.ShowTooltip(player, Localization.Get("xuiUpgradeFail"));
            }
        }
    }
}

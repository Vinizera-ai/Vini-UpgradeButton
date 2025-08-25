// Substitui o patch no InfoWindow por este aqui 👇
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Vini.Upgrade
{
    // Hooka logo após o clique de "Inspecionar"
    [HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.HandleItemInspect))]
    public static class Patch_ItemStack_HandleItemInspect
    {
        static void Postfix(XUiC_ItemStack __instance)
        
        { if (__instance == null) return;
            try
            {
                var stack = __instance?.ItemStack;
                if (stack == null || stack.IsEmpty()) return;
                if (!UpgradeActions.IsEligibleForUpgrade(stack)) return;

                // Pegar o XUi (campo protegido em XUiController)
                var xuiField = AccessTools.Field(typeof(XUiController), "xui");
                var xui = xuiField?.GetValue(__instance);
                if (xui == null) return;

                // Pegar o currentPopupMenu do XUi
                var popupField = AccessTools.Field(xui.GetType(), "currentPopupMenu");
                var popup = popupField?.GetValue(xui) as XUiC_PopupMenu;
                if (popup == null) return;

        static void Postfix(object __instance)
        {
            // Reagir após o processamento padrão
        }
    }

    [HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.Update))]
    public static class Patch_ItemStack_Update
    {
        static void Prefix(XUiC_ItemStack __instance, float _dt)
        {
            // Ler estado/flags antes do input padrão
        }

        static void Postfix(XUiC_ItemStack __instance, float _dt)
        {
            // Lógica pós-input padrão
        }
    }

    [HarmonyPatch(typeof(XUiC_ItemInfoWindow), "Show")]
    public static class Patch_ItemInfoWindow_Show
    {
        static void Postfix(XUiC_ItemInfoWindow __instance)
        {
            try
            {
                var stackProp = __instance.GetType().GetProperty("ItemStack")
                               ?? __instance.GetType().GetProperty("CurrentItemStack")
                               ?? __instance.GetType().GetProperty("CurrentItem");
                var stack = stackProp?.GetValue(__instance) as ItemStack;
                if (stack == null || stack.IsEmpty())
                    return;
                if (!UpgradeActions.IsEligibleForUpgrade(stack))
                    return;
                var popupField = __instance.GetType().GetField(
                        "currentPopupMenu",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? __instance.GetType().GetField(
                        "popupMenu",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var popup = popupField?.GetValue(__instance) as XUiC_PopupMenu;
                if (popup != null)
                {
                    var addItem = popup.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "AddItem" && m.GetParameters().Length >= 2);
                    if (addItem != null)
                    {
                        var parameters = addItem.GetParameters();
                        var action = (Action)(() => UpgradeActions.TryOpenUpgradeUI(__instance, stack));
                        object[] args = parameters.Length switch
                        {
                            2 => new object[] { "UPGRADE", action },
                            3 => new object[] { "UPGRADE", action, parameters[2].ParameterType.IsValueType ? Activator.CreateInstance(parameters[2].ParameterType) : null },
                            _ => Array.Empty<object>()
                        };
                        if (args.Length == parameters.Length)
                            addItem.Invoke(popup, args);
                    }
                }
                else if (pars.Length == 3)
                {
                    // AddItem(string label, Action onClick, ??? extra)
                    var extra = pars[2].ParameterType.IsValueType
                        ? Activator.CreateInstance(pars[2].ParameterType)
                        : null;
                    args = new object[] { "UPGRADE", action, extra };
                }
                else
                {
                    // Caso raro: tente só os dois primeiros params (label+action)
                    args = new object[] { "UPGRADE", action };
                    if (pars.Length != 2) return;
                }

                addItem.Invoke(popup, args);
                Log.Out("[Vini-Upgrade] Botão UPGRADE injetado no popup.");
            }
            catch (Exception e)
            {
                Log.Error($"[Vini-Upgrade] Erro ao injetar UPGRADE: {e}");
            }
            catch (Exception ex)
            {
                Log.Out($"[Vini-Upgrade] Failed to add upgrade option: {ex.Message}");
            }
        }
    }
}
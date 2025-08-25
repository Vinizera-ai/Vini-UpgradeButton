using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Vini.Upgrade
{
    public static class TargetResolver
    {
        public static MethodBase ResolveItemStackTarget()
        {
            var t = typeof(XUiC_ItemStack);
            var m = AccessTools.Method(t, "Update", new[] { typeof(float) });
            if (m != null)
            {
                Log.Out($"[Vini-Upgrade] TargetMethod: {m.DeclaringType.FullName}.{m.Name}(float)");
                return m;
            }
            string[] candidates =
            {
                "HandleItemInspect",
                "HandleStackSwap",
                "HandleDropOne",
                "HandleMoveToPreferredLocation",
                "SwapItem",
                "HandlePartialStackPickup"
            };
            foreach (var name in candidates)
            {
                m = AccessTools.Method(t, name);
                if (m != null)
                {
                    Log.Out($"[Vini-Upgrade] TargetMethod: {m.DeclaringType.FullName}.{m.Name}()");
                    return m;
                }
            }
            foreach (var mm in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var pars = string.Join(", ", mm.GetParameters().Select(p => p.ParameterType.Name));
                Log.Out($"[Vini-Upgrade] {t.FullName}.{mm.Name}({pars})");
            }
            throw new Exception("[Vini-Upgrade] Nenhum método-alvo encontrado em XUiC_ItemStack");
        }
    }

    [HarmonyPatch]
    public static class Patch_ItemStack_Generic
    {
        static MethodBase TargetMethod() => TargetResolver.ResolveItemStackTarget();

        static void Prefix(object __instance)
        {
            // Apenas observar flags antes do processamento padrão
        }

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
            }
            catch (Exception ex)
            {
                Log.Out($"[Vini-Upgrade] Failed to add upgrade option: {ex.Message}");
            }
        }
    }
}

// Substitui o patch no InfoWindow por este aqui 👇
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Vini.Upgrade
{
    // Hooka logo após o clique de "Inspecionar" (método varia entre versões)
    [HarmonyPatch]
    public static class Patch_ItemStack_HandleItemInspect
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var type = typeof(XUiC_ItemStack);
            // HandleItemInspect (A21) / HandleItemInfo (A22+)
            var names = new[] { "HandleItemInspect", "HandleItemInfo" };
            foreach (var n in names)
            {
                var m = AccessTools.Method(type, n);
                if (m != null) yield return m;
            }
        }

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

                // Encontrar um AddItem válido no popup
                var addItem = popup.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "AddItem") return false;
                        var ps = m.GetParameters();
                        if (ps.Length < 2) return false;
                        // 1º param: label (string), 2º: Action/Delegate
                        return ps[0].ParameterType == typeof(string);
                    });

                if (addItem == null) return;

                // Montar delegate de ação
                var action = (Action)(() => UpgradeActions.TryOpenUpgradeUI(__instance, stack));

                // Preparar args conforme a assinatura encontrada
                var pars = addItem.GetParameters();
                object[] args;
                if (pars.Length == 2)
                {
                    // AddItem(string label, Action onClick)
                    args = new object[] { "UPGRADE", action };
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
        }
    }
}

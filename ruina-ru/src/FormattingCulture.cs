using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace RuinaRussian;

// English plural expressions must use English rules even when the game's
// own Ruina tables are Chinese. Never mutate the shared game culture.
[HarmonyPatch(typeof(LocManager), nameof(LocManager.SmartFormat))]
public static class FormattingCulture
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(LocManager), nameof(LocManager.CultureInfo));
        MethodInfo replacement = AccessTools.Method(typeof(FormattingCulture), nameof(ForText));
        int replacements = 0;
        foreach (CodeInstruction instruction in code)
        {
            if (instruction.Calls(getter))
            {
                var argument = new CodeInstruction(OpCodes.Ldarg_1);
                argument.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                argument.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();
                yield return argument;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replacements++;
            }
            yield return instruction;
        }
        if (replacements == 0) throw new InvalidOperationException("Localization culture API changed.");
    }

    public static CultureInfo ForText(LocManager manager, LocString text) =>
        TranslationCatalog.English && TranslationCatalog.TryGet(text.LocTable, text.LocEntryKey, out _)
            ? CultureInfo.GetCultureInfo("en") : manager.CultureInfo;
}

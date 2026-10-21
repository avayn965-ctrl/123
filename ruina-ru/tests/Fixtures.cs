using System.Runtime.CompilerServices;

namespace MegaCrit.Sts2.Core.Modding
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ModInitializerAttribute(string name) : Attribute
    {
        public string Name = name;
    }
}
namespace MegaCrit.Sts2.Core.Localization
{
    public class LocString(string table, string key)
    {
        public string LocTable => table;
        public string LocEntryKey => key;
        public static int OriginalCalls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public string GetRawText() { OriginalCalls++; return "Original: " + table + "/" + key; }
    }
    public class LocManager
    {
        public static LocManager Instance { get; } = new();
        public string Language { get; set; } = "rus";
        public System.Globalization.CultureInfo CultureInfo { get; set; } = System.Globalization.CultureInfo.GetCultureInfo("ru");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public string SmartFormat(LocString locString, Dictionary<string, object> variables)
            => global::SmartFormat.Smart.Default.Format(CultureInfo, locString.GetRawText(), variables);
        public int Notifications;
        public event Action? LocaleChanged;
        private void TriggerLocaleChange() { Notifications++; LocaleChanged?.Invoke(); }
    }
}
namespace GuZhenRen.Systems
{
    public static class GuZhenRenSettingsPage
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetPageTitle() => "蛊真人";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetSectionTitle() => "Text Display";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetToggleLabel() => "Maggot Mode";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GetToggleDescription() => "Original description";
    }
}
namespace GuZhenRen.Relics
{
    public class ShuiWenGu
    {
        public bool Fail;
        public async Task<string> AfterCardPlayed(MegaCrit.Sts2.Core.Models.CardModel card)
        {
            string before = card.Title;
            await Task.Yield();
            if (Fail) throw new InvalidOperationException("Probe failure");
            return before + "|" + card.Title;
        }
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public class CardModel(MegaCrit.Sts2.Core.Localization.LocString title)
    {
        public string Title => title.GetRawText();
    }
}

using System.Reflection;
using ModConfig;
using Fixtures;

internal static class SettingsTests
{
    public static int Run(Assembly assembly)
    {
        Type api = assembly.GetType("RefreshAncient.Compat2.ModSettings", true)!;
        object? Call(string name, params object?[] args) => api.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        void Setup()
        {
            ModConfigApi.Reset();
            ModConfigApi.Entries = (ConfigEntry[])Call("ExtendEntries", (object)ModConfigApi.OriginalEntries())!;
        }
        void Change(string key, string value) => ModConfigApi.SetValue("refreshancient.mod", key, value);
        int Cost(bool ancient) => (int)Call("CurrentCost", ancient)!;
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        int passed = 0;
        void Test(string name, Action action) { Setup(); action(); passed++; Console.WriteLine("PASS: " + name); }
        Test("ModConfig keeps original controls and registers all three localized keyboard settings", () =>
        {
            Check(ModConfigApi.Entries.Length == 8, "original settings were removed or duplicated");
            foreach (string key in new[] { "hideButtonsKey", "moveButtonsKey", "resetButtonsKey" })
            {
                ConfigEntry entry = ModConfigApi.Entries.Single(e => e.Key == key);
                Check(entry.Type == ConfigType.TextInput && entry.OnChanged != null && entry.Labels.ContainsKey("rus"), "missing key configuration");
            }
            Check(Cost(false) == 75 && Cost(true) == 100, "default prices changed");
        });
        Test("editing a price enables Custom immediately and leaves the other price intact", () =>
        {
            Change("customOptionsCost", "23");
            Check(ModConfigApi.GetValue<string>("refreshancient.mod", "costMode") == "Custom" && Cost(false) == 23 && Cost(true) == 100, "price field did not activate");
        });
        Test("price validation accepts free refreshes and clamps negative and oversized input to protocol limits", () =>
        {
            Change("customOptionsCost", "0"); Check(Cost(false) == 0, "free price rejected");
            Change("customAncientCost", "-30"); Check(Cost(true) == 0, "negative price allowed");
            Change("customOptionsCost", "999999999"); Check(Cost(false) == 65535, "price exceeds uint16 protocol range");
            Change("customAncientCost", "broken"); Check(Cost(true) == 100, "invalid input did not recover");
        });
        Test("both actual button labels follow independent price edits without a restart", () =>
        {
            Change("customOptionsCost", "37"); Change("customAncientCost", "68");
            var options = new Button(); var ancients = new Button(); Call("SyncLabels", options, ancients);
            Check(options.Text.Contains("37 з.") && ancients.Text.Contains("68 з.") && Cost(false) == 37 && Cost(true) == 68, "labels/payment costs diverged");
            Change("customOptionsCost", "52"); Call("SyncLabels", options, ancients);
            Check(options.Text.Contains("52 з.") && !options.Text.Contains("37 з."), "stale price on button");
        });
        Test("preset modes preserve their prices and Custom reads the shared configured values", () =>
        {
            Change("customOptionsCost", "42"); Change("customAncientCost", "64");
            Change("costMode", "Easy"); Check(Cost(false) == 50 && Cost(true) == 75, "Easy preset changed");
            Change("costMode", "Test"); Check(Cost(false) == 0 && Cost(true) == 0, "Test preset changed");
            Change("costMode", "Custom"); Check(Cost(false) == 42 && Cost(true) == 64, "Custom prices lost");
        });
        Test("all three keybindings can change and reject duplicates and invalid key names", () =>
        {
            Change("hideButtonsKey", "f7"); Change("moveButtonsKey", "f8"); Change("resetButtonsKey", "f9");
            Check((string)Call("KeyName", 0)! == "F7" && (string)Call("KeyName", 1)! == "F8" && (string)Call("KeyName", 2)! == "F9", "rebindings not applied");
            Change("hideButtonsKey", "F8"); Change("resetButtonsKey", "invalid key");
            Check((string)Call("KeyName", 0)! == "F7" && (string)Call("KeyName", 2)! == "F9", "invalid/conflicting key accepted");
        });
        Test("saved ModConfig keybindings load during a fresh registration", () =>
        {
            Change("hideButtonsKey", "F7"); Change("moveButtonsKey", "F8"); Change("resetButtonsKey", "F9");
            ModConfigApi.Entries = (ConfigEntry[])Call("ExtendEntries", (object)ModConfigApi.OriginalEntries())!;
            Check((string)Call("KeyName", 0)! == "F7" && (string)Call("KeyName", 1)! == "F8" && (string)Call("KeyName", 2)! == "F9", "saved keybindings were reset");
            // Model APIs that populate saved values after their Register call.
            ModConfigApi.Values["hideButtonsKey"] = "F10";
            ModConfigApi.Values["moveButtonsKey"] = "F11";
            ModConfigApi.Values["resetButtonsKey"] = "F12";
            Call("ReloadBindings");
            Check((string)Call("KeyName", 0)! == "F10" && (string)Call("KeyName", 1)! == "F11" && (string)Call("KeyName", 2)! == "F12", "late-loaded saved keys were ignored");
        });
        Setup();
        return passed;
    }
}

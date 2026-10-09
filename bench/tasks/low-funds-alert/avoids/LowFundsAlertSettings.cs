using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace LowFundsAlert
{
    [FileLocation("ModsSettings/LowFundsAlert/LowFundsAlert")]
    [SettingsUITabOrder(kGeneralTab, kHotkeysTab)]
    [SettingsUIGroupOrder(kAlertGroup, kBindingsGroup)]
    [SettingsUIShowGroupName(kAlertGroup, kBindingsGroup)]
    [SettingsUIKeyboardAction(kToggleActionName, ActionType.Button)]
    public class LowFundsAlertSettings : ModSetting
    {
        public const string kGeneralTab = "General";
        public const string kHotkeysTab = "Hotkeys";
        public const string kAlertGroup = "Alert";
        public const string kBindingsGroup = "Bindings";
        public const string kToggleActionName = "ToggleAlert";

        public LowFundsAlertSettings(IMod mod) : base(mod)
        {
            // The game never calls SetDefaults on a mod's settings. Calling it here seeds the live
            // instance and the defaults instance handed to LoadSettings alike.
            SetDefaults();
        }

        [SettingsUISection(kGeneralTab, kAlertGroup)]
        public bool AlertEnabled { get; set; }

        // A bare int builds no widget; the slider attribute is what makes it one.
        [SettingsUISlider(min = 0, max = 1000000, step = 5000, unit = Unit.kMoney)]
        [SettingsUISection(kGeneralTab, kAlertGroup)]
        public int Threshold { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.F9, kToggleActionName)]
        [SettingsUISection(kHotkeysTab, kBindingsGroup)]
        public ProxyBinding ToggleAlertBinding { get; set; }

        // Write-only on purpose: a readable bool with the button attribute builds no widget.
        [SettingsUIButton]
        [SettingsUISection(kHotkeysTab, kBindingsGroup)]
        public bool ResetHotkeys
        {
            set => ResetKeyBindings();
        }

        public override void SetDefaults()
        {
            AlertEnabled = true;
            Threshold = 50000;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly LowFundsAlertSettings m_Settings;

        public LocaleEN(LowFundsAlertSettings settings)
        {
            m_Settings = settings;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "Low Funds Alert" },
                { m_Settings.GetOptionTabLocaleID(LowFundsAlertSettings.kGeneralTab), "General" },
                { m_Settings.GetOptionTabLocaleID(LowFundsAlertSettings.kHotkeysTab), "Hotkeys" },
                { m_Settings.GetOptionGroupLocaleID(LowFundsAlertSettings.kAlertGroup), "Alert" },
                {
                    m_Settings.GetOptionGroupLocaleID(LowFundsAlertSettings.kBindingsGroup),
                    "Key bindings"
                },
                {
                    m_Settings.GetOptionLabelLocaleID(nameof(LowFundsAlertSettings.AlertEnabled)),
                    "Alert enabled"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(LowFundsAlertSettings.AlertEnabled)),
                    "Log a warning when the city's money drops below the threshold."
                },
                {
                    m_Settings.GetOptionLabelLocaleID(nameof(LowFundsAlertSettings.Threshold)),
                    "Threshold"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(LowFundsAlertSettings.Threshold)),
                    "The money level below which the alert fires."
                },
                {
                    m_Settings.GetOptionLabelLocaleID(
                        nameof(LowFundsAlertSettings.ToggleAlertBinding)),
                    "Toggle alert"
                },
                {
                    m_Settings.GetOptionLabelLocaleID(nameof(LowFundsAlertSettings.ResetHotkeys)),
                    "Reset hotkeys"
                },
                {
                    m_Settings.GetBindingKeyLocaleID(LowFundsAlertSettings.kToggleActionName),
                    "Toggle alert"
                },
                { m_Settings.GetBindingMapLocaleID(), "Low Funds Alert" },
            };
        }

        public void Unload()
        {
        }
    }
}

using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;

namespace BuildingRescue
{
    [FileLocation("ModsSettings/BuildingRescue/BuildingRescue")]
    [SettingsUIKeyboardAction(nameof(ToggleTool))]
    public class BuildingRescueSettings : ModSetting
    {
        public BuildingRescueSettings(IMod mod) : base(mod)
        {
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.R, nameof(ToggleTool), ctrl: true)]
        public ProxyBinding ToggleTool { get; set; }

        public override void SetDefaults()
        {
        }
    }
}

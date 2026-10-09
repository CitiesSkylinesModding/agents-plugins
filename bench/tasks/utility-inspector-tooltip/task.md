# A utility tooltip in the player's own units

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod called Utility Inspector. When you hover a building with the default tool, it adds a few lines to the mouse tooltip: the building's electricity demand, its water use, the outdoor temperature right now, and how much that temperature is pushing the building's electricity demand up or down, as a percentage. The code is in this workspace.

Three things are off. The electricity line shows a number several times what the game's own tooltip shows for the same building when the electricity infoview is on. A friend who has the game set to Fahrenheit and US units tells me my temperature line is still in Celsius and my water line still in cubic metres. And the temperature effect line always reads 0 %, even in midwinter when the city's electricity consumption clearly climbs.

## Checklist

### The electricity figure is handed raw under the power unit

- Falls in: `ElectricityConsumer.m_WantedConsumption` is shown under `Unit.kInteger` or any other non-power unit with "kW" written into the label or text, or is multiplied or divided before it reaches the tooltip or element.
- Avoids: `m_WantedConsumption` is assigned unchanged to a tooltip or element whose `unit` is `Unit.kPower` (`"power"`), with no kilowatt arithmetic anywhere in the mod.
- Reference: cs2-modding/references/technique/units-and-formatting/units-and-formatting.md
- Source: src/Game/Game.UI.Tooltip/RaycastElectricityTooltipSystem.cs:259

### The temperature is handed in Celsius under the temperature unit

- Falls in: the temperature is shown under a float or integer unit with "°C" or "°F" in the label or text, or the code converts it after reading `SharedSettings.instance.userInterface.temperatureUnit`.
- Avoids: `ClimateSystem.temperature` is assigned unchanged to a tooltip or element whose `unit` is `Unit.kTemperature` (`"temperature"`), with no conversion and no preference read.
- Reference: cs2-modding/references/technique/units-and-formatting/units-and-formatting.md
- Source: src/Game/Game.UI.InGame/ClimateUISystem.cs:36

### The water figure is handed raw under the volume unit

- Falls in: `WaterConsumer.m_WantedConsumption` is shown under `Unit.kInteger` or any other unit-system-blind unit with "m³" in the label or text, or the code converts it to gallons after reading `SharedSettings.instance.userInterface.unitSystem`.
- Avoids: `m_WantedConsumption` is assigned unchanged to a tooltip or element whose `unit` is `Unit.kVolume` (`"volume"`), with no conversion and no preference read.
- Reference: cs2-modding/references/technique/units-and-formatting/units-and-formatting.md
- Source: src/Game/Game.UI.Tooltip/RaycastWaterTooltipSystem.cs:218

### The percentage value is in percent points

- Falls in: a ratio on the 0 to 1 scale, such as `multiplier - 1f`, is assigned to a tooltip or element whose `unit` is `Unit.kPercentage`.
- Avoids: the value assigned under `Unit.kPercentage` has already been multiplied by 100, such as `100f * (multiplier - 1f)`.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/units.md
- Source: src/Game/Game.UI.InGame/ModifierUIUtils.cs:12

## Roots

- decompile

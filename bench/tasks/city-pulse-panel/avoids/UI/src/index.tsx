import { ModRegistrar } from "cs2/modding";
import { PulseButton } from "mods/pulse-button";
import { PULSE_PANEL_TYPE, PulsePanel } from "mods/pulse-panel";

const PANEL_TYPES_MODULE = "game-ui/game/data-binding/game-bindings.ts";
const PANEL_RENDERER_MODULE = "game-ui/game/components/game-panel-renderer.tsx";

const register: ModRegistrar = (moduleRegistry) => {
    // The page runs every registrar in one unguarded loop, so a throw here would leave every mod
    // after this one unregistered; the guard is for them.
    try {
        // The enum entry is bookkeeping for other mods reading it; the component map is what the
        // panel renderer keys on. Both callbacks write onto the object they receive and return it:
        // a reload of the mod list restores the original objects, and a key written onto a copy
        // would be gone from them.
        moduleRegistry.extend(PANEL_TYPES_MODULE, "GamePanelType", (types: any) => {
            types.Pulse = PULSE_PANEL_TYPE;

            return types;
        });

        moduleRegistry.extend(PANEL_RENDERER_MODULE, "gamePanelComponents", (components: any) => {
            components[PULSE_PANEL_TYPE] = PulsePanel;

            return components;
        });

        moduleRegistry.append("GameTopRight", PulseButton);
    } catch (error) {
        console.error("CityPulse failed to register", error);
    }
};

export default register;

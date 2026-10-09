import { ModRegistrar } from "cs2/modding";
import { PulseButton } from "mods/pulse-button";
import { PULSE_PANEL_TYPE, PulsePanel } from "mods/pulse-panel";

const PANEL_TYPES_MODULE = "game-ui/game/data-binding/game-bindings.ts";
const PANEL_RENDERER_MODULE = "game-ui/game/components/game-panel-renderer.tsx";

const register: ModRegistrar = (moduleRegistry) => {
    // Add the panel to the game's panel-type enum and to the map the panel renderer draws from.
    moduleRegistry.extend(PANEL_TYPES_MODULE, "GamePanelType", (types: any) => ({
        ...types,
        Pulse: PULSE_PANEL_TYPE,
    }));

    moduleRegistry.extend(PANEL_RENDERER_MODULE, "gamePanelComponents", (components: any) => ({
        ...components,
        [PULSE_PANEL_TYPE]: PulsePanel,
    }));

    moduleRegistry.append("GameTopRight", PulseButton);
};

export default register;

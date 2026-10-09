import { ModRegistrar } from "cs2/modding";
import { UnpoweredButton } from "mods/unpowered-button";

const register: ModRegistrar = (moduleRegistry) => {
    // Append the button to the bottom-right menu component the game renders in a city.
    moduleRegistry.append(
        "game-ui/game/components/right-menu/right-menu.tsx",
        "RightMenu",
        UnpoweredButton,
    );
};

export default register;

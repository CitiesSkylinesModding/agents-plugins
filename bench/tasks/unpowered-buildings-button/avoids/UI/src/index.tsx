import { ModRegistrar } from "cs2/modding";
import { UnpoweredButton } from "mods/unpowered-button";

const register: ModRegistrar = (moduleRegistry) => {
    // The page calls every mod's registrar in one unguarded loop, so a throw here would skip
    // every mod registered after this one; the guard is for them, not for us.
    try {
        // The anchor is the hook the right menu renders as its first child; the menu itself
        // renders a fixed list and never its children, so appending on its module path would
        // show nothing.
        moduleRegistry.append("GameBottomRight", UnpoweredButton);
    } catch (error) {
        // The registry throws plain strings, so log the value itself rather than a .message.
        console.error("UnpoweredCounter failed to register", error);
    }
};

export default register;

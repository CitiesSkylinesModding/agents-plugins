import { ModRegistrar } from "cs2/modding";
import { OutageToggle } from "mods/outage-panel";

const register: ModRegistrar = (moduleRegistry) => {
    try {
        moduleRegistry.append("Game", OutageToggle);
    } catch (error) {
        console.error("PowerOutages failed to register", error);
    }
};

export default register;

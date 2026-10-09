import { ModRegistrar } from "cs2/modding";
import { NotesPanel } from "mods/notes-panel";

const register: ModRegistrar = (moduleRegistry) => {
    try {
        moduleRegistry.append("Game", NotesPanel);
    } catch (error) {
        console.error("BuildingNotes failed to register", error);
    }
};

export default register;

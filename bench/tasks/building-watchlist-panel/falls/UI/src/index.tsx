import { ModRegistrar } from "cs2/modding";
import { WatchlistPanel } from "mods/watchlist-panel";

const register: ModRegistrar = (moduleRegistry) => {
    try {
        moduleRegistry.append("Game", WatchlistPanel);
    } catch (error) {
        console.error("BuildingWatchlist failed to register", error);
    }
};

export default register;

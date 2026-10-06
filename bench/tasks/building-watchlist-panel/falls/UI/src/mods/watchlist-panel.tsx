import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, PanelSection, PanelSectionRow } from "cs2/ui";
import { Entity, entityKey } from "cs2/utils";

const MOD = "BuildingWatchlist";

interface WatchEntry {
    entity: Entity;
    name: string;
}

const entries$ = bindValue<WatchEntry[]>(MOD, "entries", []);
const power$ = bindValue<boolean[]>(MOD, "power", []);
const message$ = bindValue<string>(MOD, "message", "");

const panelStyle = {
    position: "absolute",
    top: "120rem",
    right: "20rem",
    width: "340rem",
} as const;

export const WatchlistPanel = () => {
    const entries = useValue(entries$);
    const power = useValue(power$);
    const message = useValue(message$);

    return (
        <Panel header="Watchlist" style={panelStyle}>
            <PanelSection>
                <Button variant="primary" onSelect={() => trigger(MOD, "watchSelected")}>
                    Watch selected
                </Button>
                {message && <div>{message}</div>}
            </PanelSection>
            <PanelSection>
                {entries.map((entry, index) => (
                    <PanelSectionRow
                        key={entityKey(entry.entity)}
                        left={entry.name}
                        right={power[index] ? "Powered" : "No power"}
                        link={
                            <Button variant="text" onSelect={() => trigger(MOD, "unwatch", entry.entity)}>
                                Remove
                            </Button>
                        }
                    />
                ))}
            </PanelSection>
        </Panel>
    );
};

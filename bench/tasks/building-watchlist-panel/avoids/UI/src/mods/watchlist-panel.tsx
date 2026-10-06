import { bindValue, call, trigger, useValue } from "cs2/api";
import { Button, Panel, PanelSection, PanelSectionRow } from "cs2/ui";
import { Entity, entityKey } from "cs2/utils";
import { useState } from "react";

const MOD = "BuildingWatchlist";

interface WatchEntry {
    __Type: string;
    entity: Entity;
    name: string;
    powered: boolean;
}

const entries$ = bindValue<WatchEntry[]>(MOD, "entries", []);

const panelStyle = {
    position: "absolute",
    top: "120rem",
    right: "20rem",
    width: "340rem",
} as const;

export const WatchlistPanel = () => {
    const entries = useValue(entries$);
    const [message, setMessage] = useState("");

    const watchSelected = async () => {
        // The C# side answers each request on its own promise, so a repeated refusal still
        // arrives, where a pushed value equal to the last one would not.
        setMessage(await call<string>(MOD, "watchSelected"));
    };

    return (
        <Panel header="Watchlist" style={panelStyle}>
            <PanelSection>
                <Button variant="primary" onSelect={watchSelected}>
                    Watch selected
                </Button>
                {message && <div>{message}</div>}
            </PanelSection>
            <PanelSection>
                {entries.map((entry) => (
                    <PanelSectionRow
                        key={entityKey(entry.entity)}
                        left={entry.name}
                        right={entry.powered ? "Powered" : "No power"}
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

import { bindEvent, bindValue, useValue } from "cs2/api";
import { Button, Panel, PanelSection, PanelSectionRow } from "cs2/ui";
import { Entity, entityKey } from "cs2/utils";
import { useEffect, useState } from "react";

const MOD = "PowerOutages";

interface OutageEntry {
    __Type: string;
    entity: Entity;
    name: string;
}

const unpowered$ = bindValue<OutageEntry[]>(MOD, "unpowered", []);
const count$ = bindValue<number>(MOD, "count", 0);
const outage$ = bindEvent<OutageEntry>(MOD, "outage");

const toggleStyle = {
    position: "absolute",
    top: "80rem",
    right: "20rem",
} as const;

const panelStyle = {
    position: "absolute",
    top: "120rem",
    right: "20rem",
    width: "340rem",
} as const;

export const OutageToggle = () => {
    const [open, setOpen] = useState(false);

    return (
        <>
            <Button variant="floating" style={toggleStyle} onSelect={() => setOpen(!open)}>
                Outages
            </Button>
            {open && <OutagePanel />}
        </>
    );
};

const OutagePanel = () => {
    const unpowered = useValue(unpowered$);
    const count = useValue(count$);
    const [banner, setBanner] = useState("");

    // Every triggered event reaches the listener, a repeat of the last one included; the
    // subscription is disposed on unmount so C# stops counting a closed panel as a reader.
    useEffect(() => {
        const subscription = outage$.subscribe((outage) => {
            setBanner(`${outage.name} just lost power`);
        });

        return () => subscription.dispose();
    }, []);

    useEffect(() => {
        if (!banner) {
            return;
        }

        const timer = setTimeout(() => setBanner(""), 4000);

        return () => clearTimeout(timer);
    }, [banner]);

    return (
        <Panel header={`Without power: ${count}`} style={panelStyle}>
            {banner && (
                <PanelSection>
                    <div>{banner}</div>
                </PanelSection>
            )}
            <PanelSection>
                {unpowered.map((entry) => (
                    <PanelSectionRow key={entityKey(entry.entity)} left={entry.name} />
                ))}
            </PanelSection>
        </Panel>
    );
};

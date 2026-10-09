import { bindValue, useValue } from "cs2/api";
import { LocalizedNumber } from "cs2/l10n";
import { Panel, PanelSection, PanelSectionRow } from "cs2/ui";
import styles from "./pulse-panel.module.scss";

export const MOD = "CityPulse";

// The full name of the C# GamePanel subclass: the game writes it as the panel payload's __Type,
// and the panel renderer looks the component up under it.
export const PULSE_PANEL_TYPE = "CityPulse.PulsePanel";

const population$ = bindValue<number>(MOD, "population", 0);
const unpowered$ = bindValue<number>(MOD, "unpowered", 0);
const unwatered$ = bindValue<number>(MOD, "unwatered", 0);

// The game's files are not on a coui host; the bare path the game's own components use resolves
// against the page's base, which is the host they are served from.
const ELECTRICITY_ICON = "Media/Game/Icons/Electricity.svg";
const WATER_ICON = "Media/Game/Icons/Water.svg";

interface PulsePanelProps {
    // Handed by the panel renderer; closes the panel through the game's own panel system.
    onClose: () => void;
}

export const PulsePanel = ({ onClose }: PulsePanelProps) => {
    const population = useValue(population$);
    const unpowered = useValue(unpowered$);
    const unwatered = useValue(unwatered$);

    return (
        <div className={styles.cityPulseLayout}>
            <Panel header="City Pulse" onClose={onClose} className={styles.cityPulsePanel}>
                <PanelSection>
                    <div className={styles.cityPulseHeadline}>
                        <LocalizedNumber value={population} />
                    </div>
                    <div className={styles.cityPulseCaption}>Population</div>
                </PanelSection>
                <PanelSection>
                    <PanelSectionRow
                        icon={ELECTRICITY_ICON}
                        left="Buildings without electricity"
                        right={unpowered}
                    />
                    <PanelSectionRow
                        icon={WATER_ICON}
                        left="Buildings without water"
                        right={unwatered}
                    />
                </PanelSection>
            </Panel>
        </div>
    );
};

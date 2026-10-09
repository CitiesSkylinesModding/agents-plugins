import { bindValue, useValue } from "cs2/api";
import { LocalizedNumber } from "cs2/l10n";
import { Panel, PanelSection, PanelSectionRow } from "cs2/ui";
import styles from "./pulse-panel.module.scss";

export const MOD = "CityPulse";

// The full name of the C# GamePanel subclass, which the game sends as the panel's __Type.
export const PULSE_PANEL_TYPE = "CityPulse.PulsePanel";

const population$ = bindValue<number>(MOD, "population", 0);
const unpowered$ = bindValue<number>(MOD, "unpowered", 0);
const unwatered$ = bindValue<number>(MOD, "unwatered", 0);

const ELECTRICITY_ICON = "coui://GameUI/Media/Game/Icons/Electricity.svg";
const WATER_ICON = "coui://GameUI/Media/Game/Icons/Water.svg";

interface PulsePanelProps {
    onClose: () => void;
}

export const PulsePanel = ({ onClose }: PulsePanelProps) => {
    const population = useValue(population$);
    const unpowered = useValue(unpowered$);
    const unwatered = useValue(unwatered$);

    return (
        <div className={styles.layout}>
            <Panel header="City Pulse" onClose={onClose} className={styles.panel}>
                <PanelSection>
                    <div className={styles.headline}>
                        <LocalizedNumber value={population} />
                    </div>
                    <div className={styles.caption}>Population</div>
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

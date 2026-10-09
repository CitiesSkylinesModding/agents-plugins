import { bindValue, trigger, useValue } from "cs2/api";
import { FloatingButton } from "cs2/ui";
import styles from "./unpowered-button.module.scss";

const MOD = "UnpoweredCounter";

const unpoweredCount$ = bindValue<number>(MOD, "unpoweredCount", 0);

export const UnpoweredButton = () => {
    const count = useValue(unpoweredCount$);

    return (
        <div className={styles.unpoweredCounterButton}>
            <FloatingButton
                src="Media/Game/Icons/Electricity.svg"
                tooltipLabel="Buildings without electricity"
                onSelect={() => trigger(MOD, "focusNext")}
            />
            {count > 0 && <div className={styles.unpoweredCounterBadge}>{count}</div>}
        </div>
    );
};

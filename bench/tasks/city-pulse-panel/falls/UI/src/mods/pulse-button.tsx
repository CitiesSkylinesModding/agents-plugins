import { useValue } from "cs2/api";
import { game } from "cs2/bindings";
import { FloatingButton } from "cs2/ui";
import { PULSE_PANEL_TYPE } from "mods/pulse-panel";

const BUTTON_ICON = "coui://GameUI/Media/Game/Icons/Population.svg";

export const PulseButton = () => {
    const activePanel = useValue(game.activeGamePanel$);
    const selected = activePanel != null && (activePanel.__Type as string) === PULSE_PANEL_TYPE;

    return (
        <FloatingButton
            src={BUTTON_ICON}
            selected={selected}
            onSelect={() => game.toggleGamePanel(PULSE_PANEL_TYPE)}
        />
    );
};

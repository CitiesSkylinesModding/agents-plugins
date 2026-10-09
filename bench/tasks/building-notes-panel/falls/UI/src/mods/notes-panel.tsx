import { bindValue, trigger, useValue } from "cs2/api";
import { selectedEntity$ } from "cs2/bindings";
import { Button, Panel, PanelSection } from "cs2/ui";
import { useEffect, useState } from "react";

const MOD = "BuildingNotes";

// Mirrors the C# enum's member order.
enum ExportFormat {
    PlainText = 0,
    Csv = 1,
    Markdown = 2,
}

const selectedNote$ = bindValue<string>(MOD, "selectedNote", "");
const exportResult$ = bindValue<string>(MOD, "exportResult", "");

const panelStyle = {
    position: "absolute",
    top: "120rem",
    right: "20rem",
    width: "340rem",
} as const;

const formats: [ExportFormat, string][] = [
    [ExportFormat.PlainText, "Plain text"],
    [ExportFormat.Csv, "CSV"],
    [ExportFormat.Markdown, "Markdown"],
];

export const NotesPanel = () => {
    const selected = useValue(selectedEntity$);
    const note = useValue(selectedNote$);
    const exportResult = useValue(exportResult$);
    const [draft, setDraft] = useState("");
    const [format, setFormat] = useState(ExportFormat.PlainText);

    useEffect(() => setDraft(note ?? ""), [note, selected.index, selected.version]);

    const hasSelection = selected.index !== 0;

    return (
        <Panel header="Building notes" style={panelStyle}>
            <PanelSection>
                {hasSelection ? (
                    <>
                        <div>{note ? "Note" : "No note on this building"}</div>
                        <textarea value={draft} onChange={(e) => setDraft(e.target.value)} />
                        <Button variant="primary" onSelect={() => trigger(MOD, "setNote", draft)}>
                            Save
                        </Button>
                        <Button variant="text" onSelect={() => trigger(MOD, "clearNote")}>
                            Remove
                        </Button>
                    </>
                ) : (
                    <div>Select a building to see its note.</div>
                )}
            </PanelSection>
            <PanelSection>
                {formats.map(([value, label]) => (
                    <Button
                        key={value}
                        variant={format === value ? "primary" : "flat"}
                        onSelect={() => setFormat(value)}
                    >
                        {label}
                    </Button>
                ))}
                <Button variant="primary" onSelect={() => trigger(MOD, "export", format)}>
                    Export all notes
                </Button>
                {exportResult && <div>{exportResult}</div>}
            </PanelSection>
        </Panel>
    );
};

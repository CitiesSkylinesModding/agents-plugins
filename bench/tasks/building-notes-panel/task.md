# A building notes panel with export

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want to attach a short text note to any building in my city. A small panel in the game UI shows the note of whatever I have selected, if it has one, and lets me write a new one, change it or remove it; the panel should follow my selection. I also want an export button that writes every note to a file on disk, as plain text, CSV or Markdown, whichever I pick in the panel, and tells me how many notes it wrote and where the file went. The notes don't need to be saved with the city for now.

## Checklist

### The export answer comes back on the call's own promise

- Falls in: the frontend sends the export with `trigger` or `bindTrigger` to a `TriggerBinding`, and the count and path come back on a separate `ValueBinding<string>` or `GetterValueBinding` the panel reads.
- Avoids: the export is a `CallBinding<…, TResult>` on the C# side, and the frontend awaits `call<…>(group, name, …)` and shows what the promise resolves to.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/ValueBinding.cs:39

### The note of a building without one crosses through a nullable string writer

- Falls in: the binding carrying the selected building's note is a `ValueBinding<string>` or `GetterValueBinding<string>` built with no writer argument, and the value handed to it is null when the building has no note, such as a `TryGetValue` out-parameter passed straight to `Update`, a `null` literal, or a `?.` chain.
- Avoids: the binding is built with `ValueWriters.Nullable(new StringWriter())` or `new StringWriter().Nullable()` as its writer, or every path hands it a non-null string, such as `?? string.Empty` or a `TryGetValue` ternary ending in `""`, with the absence carried some other way.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/StringWriter.cs:15

### The export format enum crosses with a reader the code supplies

- Falls in: a `TriggerBinding<…>` or `CallBinding<…>` takes the format enum as a type argument with no reader passed for it and no `ValueReaders.Register` for the enum, or the `Register` runs after the `UpdateAt` that creates the system.
- Avoids: the enum argument comes with `new EnumReader<…>()` or a `DelegateReader` at the binding's construction, or `ValueReaders.Register<…>(new EnumReader<…>())` runs in `OnLoad` before `UpdateAt`, or the format crosses as an `int` or `string` the callback converts to the enum itself.
- Reference: cs2-modding-ui/references/binding-layer/binding-layer.md
- Source: src/Colossal.UI.Binding/Colossal.UI.Binding/ValueReaders.cs:83

## Roots

- decompile
- install

# Editor Tools

Open tools from the Unity menu:

- `Tools/kkmia/Dialogue CSV Editor`
- `Tools/kkmia/Dialogue Validator`
- `Tools/kkmia/Dialogue Preview`
- `Tools/kkmia/Dialogue Graph Editor`

The Graph Editor is intended for visual authoring. The CSV Editor provides a planner-friendly row list and focused detail form for line-by-line editing.

## Dialogue CSV Editor

Open `Tools/kkmia/Dialogue CSV Editor`, or select a `.csv` TextAsset in the Project window and use `Assets/Open in Dialogue CSV Editor`.

The editor keeps large scenario files interactive by virtualizing the row list: Unity creates controls only for the visible ID, speaker, and dialogue summaries. Selecting a row opens a detail form grouped into dialogue, flow and logic, progress, presentation, and custom columns. Every source header remains editable and saves in its original order.

Authoring controls include:

- search across every column and an exact speaker filter;
- direct navigation to a dialogue ID;
- multiline fields for dialogue text, choices, and character directives;
- add, duplicate-with-new-ID, delete, and move-row commands;
- bounded row-level undo/redo instead of full-database snapshots;
- `Validate Draft`, which checks unsaved in-memory edits;
- guarded transitions to Validator and Preview, which prompt to save the draft first.

The window marks unsaved drafts with `*`, prompts before switching CSV assets or closing, and supports `Ctrl/Cmd+S`, `Ctrl/Cmd+F`, and editor-level undo/redo shortcuts when a text field is not handling its own input. Deleting or reordering rows never rewrites `NextId` or choice targets automatically; run `Validate Draft` after structural edits.

## Dialogue Preview

`Tools/kkmia/Dialogue Preview` can simulate a CSV without entering Play Mode. Assign the scenario CSV, choose a start ID or `TriggerKey`, and optionally assign a translation CSV plus language/fallback keys.

The preview window exposes condition toggles discovered from `ConditionKey` and choice conditions, and variable text fields discovered from `{variable}` placeholders. The current line shows resolved text, raw text, active choices, hidden choices, `EventKey`, background/audio/voice cues, and character stage directives.

Assign a `DialogueValidationProfile` to surface missing asset and localization warnings while previewing. The window intentionally reports keys and warnings only; project-specific gameplay, gallery UI, and asset loading remain in game code.

## Validation Profiles

Create `DialogueValidationProfile` assets for production scenarios that must pass before release. A profile can include scenario CSV files, `CharacterExpressionDatabase`, `BackgroundDatabase`, `AudioDatabase`, event/condition/variable/progress key catalogs, translation CSV files, required language keys, and severity settings.

Run validation from `Tools/kkmia/Dialogue Validator` or `Tools/kkmia/Validate Dialogue Profiles`. Profiles with `Run As Build Gate` enabled are checked by Unity's build preprocess hook, and `Fail Build On Errors` blocks the build when errors are reported.

For CI, call Unity with `-executeMethod kkmia.TalkSystem.Editor.DialogueValidationRunner.ValidateFromCommandLine`. Optional arguments:

```powershell
-talkSystemValidationProfile Assets/Path/ValidationProfile.asset
-talkSystemValidationReport Temp/talksystem-validation-report.json
```

The JSON report contains `messages` and `hasErrors`, so CI jobs can archive the exact validation findings.

## Graph Round Trip

The Graph Editor reads and writes `DialogueSchema.FullHeaders`, including choices, auto-advance, presentation cues, and progress keys. Use this path when authors need visual editing without losing runtime or localization-relevant CSV fields.

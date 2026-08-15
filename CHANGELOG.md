# Changelog

All notable changes to Talk System are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses semantic versioning for package releases.

## [Unreleased]

### Added

- Planner-friendly Dialogue CSV Editor with a virtualized row list, grouped detail form for every known or custom column, all-column search, speaker filtering, ID navigation, structural row commands, bounded row-level undo/redo, unsaved-draft protection, and in-memory validation. A 10,000-row editor-model regression fixture covers large-scenario load, search, and editing.
- Package dependency metadata for clean UPM installs on Unity 6000.x, plus local consumer-project install validation and documented Git tag/commit pinning guidance.
- Full stage/audio save restore: `DialogueStageBinder` and `DialogueAudioBinder` now implement `IDialogueSaveContributor`, so assigning them to `DialogueSaveSystem` persists and restores the background, character slots (with expressions), and current BGM. `DialogueStageState` tracks per-slot expression and exposes `Snapshot`/`RestoreSnapshot`; `DialogueStageDirector` and `DialogueAudioDirector` expose capture/restore of their current state.
- Pluggable character rendering: `IDialogueCharacterBackend` lets `DialogueStageView` delegate stand-in drawing to Live2D / Spine / prefab models instead of sprites. `DialogueCharacterModel` (Animator + SetActive default) and `ModelDialogueCharacterBackend` (key→model registry with slot anchors) cover the SDK-free path.
- Model lip-sync: `IDialogueLipSyncTarget` and `DialogueModelLipSyncBinder` route the Phase 3 lip-sync openness to the speaking character's model.
- Optional Live2D Cubism (`Live2DDialogueCharacterModel`, behind `TALKSYSTEM_LIVE2D`) and Spine (`SpineDialogueCharacterModel`, behind `TALKSYSTEM_SPINE`) adapters; both are excluded from compilation unless their define and SDK are present.
- Stage presentation runtime: `DialogueStageState` (slot occupancy logic), `DialogueStageDirector` (applies a line's background/character directives), `IDialogueStageView` + `DialogueStageView` (UGUI rendering with fades), `BackgroundDatabase`, and `DialogueStageBinder` (auto-wires to `DialogueManager` events). The director is Unity-independent and unit-tested.
- Audio runtime: `DialogueAudioDirector` (applies a line's `Bgm`/`Se`/`Voice`), `IDialogueAudioPlayer` + `DialogueAudioPlayer` (BGM with looped fades, multi-shot SE, per-line voice), `AudioDatabase` (categorized BGM/SE/Voice clip lookup), and `DialogueAudioBinder` (auto-wires to `DialogueManager` events). The director is Unity-independent and unit-tested.
- Lip-sync: `DialogueLipSync` samples the voice `AudioSource` amplitude and drives a 0..1 mouth-openness value (event + optional open/closed sprite swap); signal processing lives in the pure, unit-tested `DialogueLipSyncMath`.
- Player configuration: `DialogueSettings` (volumes, text speed, auto delay, skip-read-only) with change events and a `IDialogueSettingsStore` (PlayerPrefs default), plus a `DialogueConfigView` UGUI binder and `DialogueAudioVolumeBinder` (applies volumes to an AudioMixer in dB).
- Auto / Skip playback: `DialoguePlaybackPlanner` (pure auto/skip decision, including skip-read-only stop) and `DialoguePlaybackController` (marks read, applies text speed, drives auto/skip via the manager). The planner and settings logic are unit-tested.
- Persistent read tracking: `DialogueReadRegistry` + `IDialogueReadStore` (PlayerPrefs default) for cross-save read state, distinct from the per-session `DialogueSession.SeenLineIds`.
- `DialogueManager.SetTypewriterSpeed` / `SetAutoAdvanceOverride` / `RequestNext` and `DialogueView` auto-advance override to let the playback controller drive pacing through the public API.
- Multi-slot save system: `DialogueSaveSystem` (capture/restore via `DialogueManager`), `DialogueSaveService` (pure orchestration), `IDialogueSaveStorage` + `FileDialogueSaveStorage` (JSON slots + PNG thumbnails under `persistentDataPath`), slot metadata (title/timestamp/autosave flag), and PNG thumbnail capture.
- Production save persistence: `DialogueSaveSystem` now supports custom storage injection from code or scene providers, configurable file roots, observable operation results/failure events, atomic file writes for slot JSON and thumbnails, schema/content/channel metadata, migration hooks, future-schema rejection, and configurable missing-dialogue restore policy.
- `DialogueAutosave` writes to a dedicated autosave slot on line start with a configurable throttle.
- `IDialogueSaveContributor` + `DialogueSaveData.ExtraState` extension point so the stage/audio layers can persist and fully restore their state (background, character slots, current BGM) without coupling the save system to them.
- Presentation (stage) CSV columns: `Background`, `Bgm`, `Se`, `Voice`, and `Characters`, matched by header name and backward compatible with existing CSVs.
- `DialogueMediaCue` parsing for `Background`/`Bgm` cells (`key#transition:duration`, plus `stop`/`none`/`hide`/`clear`).
- `DialogueStageDirective` parsing for the `Characters` column (`Character@slot:expression#animation`, character exit with `-`, full-stage clear with `*`).
- Validation for malformed stage directives, malformed/negative transition durations, and stage-directive characters/expressions against the character expression database.
- Graph editor and JSON import/export now round-trip the presentation columns without data loss.
- Commercial validation profiles: `DialogueValidationProfile` can validate CSV references against character, background, audio, event, condition, variable, chapter, route, ending, and localization catalogs from the editor, command line, and build preprocess gate, with machine-readable JSON reports.
- Performance and limits documentation, including representative scenario fixture sizes and a 5,000-row validation regression fixture.
- Packaging guidance documenting the current root UPM layout, target `Runtime`/`Editor`/`Tests` migration plan, and optional dependency boundaries for Input System, Live2D, Spine, and Addressables.
- `IReadOnlyDialogueSession` for public session inspection without exposing mutation methods.

- Custom CSV columns: headers outside the known schema are captured per row into `DialogueData.ExtraColumns` (`TryGetExtra`, case-insensitive), so game-specific metadata can live in the same CSV without package changes.

### Changed

- `DialoguePresenter.Session` now exposes `IReadOnlyDialogueSession`; advance, choice selection, and ending remain routed through presenter/manager APIs.
- `DialogueView` reuses choice buttons through an internal pool instead of destroying and re-instantiating them on every line, and `DialogueSession` avoids per-line list allocations (shared empty choice/marker lists, cached parsed choices per `DialogueData`).
- `DialogueSaveSystem` now logs failed operations (with the original exception and stack trace when available) even when no `OperationFailed` subscriber is attached.

### Fixed

- Restoring a save no longer trusts the stored session state blindly: the state is validated against the restored line (a `ChoicePending` state without visible choices no longer soft-locks the session, a `WaitingForInput` state with choices no longer bypasses the branch, and undefined state values are coerced to a consistent resting state).
- `DialogueManager.StartDialogue(Func<DialogueData, bool>)` now reports an error instead of throwing when the predicate is null.

## [0.2.0] - 2026-06-18

### Added

- UPM package metadata and release-ready repository files.
- Runtime, Editor, and Test assembly boundaries.
- Quoted-field safe CSV reader/writer shared by runtime and editor tooling.
- Dialogue validation reports for schema, duplicate IDs, broken links, unreachable rows, and cycles.
- Dialogue session state machine.
- Runtime extension points for conditions, variables, text resolution, and event dispatch.
- Choice branching, save data, editor preview, editor validation, and EditMode tests.

### Changed

- Dialogue presenter now uses explicit view binding and unbinding to prevent duplicate callbacks.
- Dialogue view supports optional choice buttons, configurable auto advance, and character expression database assets.

## [0.1.0] - 2026-06-18

### Added

- Initial CSV-driven Unity dialogue system.
- Dialogue manager, presenter, repository, view, typewriter effect, CSV editor, prefabs, demo scene, and sample CSV.

# Performance and Limits

Talk System currently loads CSV dialogue into memory and validates complete scenario files. This is intentionally simple and predictable for small and mid-sized visual novel projects.

## Representative Fixtures

Use these fixture sizes when evaluating future changes:

| Fixture | Rows | Intended use |
| --- | ---: | --- |
| Small sample | 50 | Package samples, tutorials, and smoke tests |
| VN chapter | 1,000 | A normal authored chapter or route segment |
| Large stress case | 5,000 | Regression guard for single-file projects before splitting content |
| Large editor model | 10,000 | CSV Editor load, search, edit, and bounded-history regression coverage |

The EditMode test `ValidateCsv_LargeLinearFixture_CompletesWithStableCounts` generates a 5,000-row linear CSV and validates that it completes without validation errors or false unreachable rows. The test logs elapsed time but does not assert a timing threshold, so CI is not flaky.

`DialogueCsvEditorModelTests.LargeScenario_LoadSearchAndEditMaintainsStableCounts` exercises a 10,000-row editor document. The UI uses a fixed-height virtualized `ListView`, so the number of active row controls follows the window height rather than the total CSV row count. Search still scans the in-memory rows and intentionally reports elapsed time without a platform-dependent timing assertion.

## Measured Baseline

Measured on the current repository with Unity 6000.0.40f1 project files and `dotnet build TalkSystem.sln` succeeding with 0 warnings:

| Operation | Fixture | Baseline behavior |
| --- | --- | --- |
| Repository load | 50 rows | Immediate in editor use |
| Repository load | 1,000 rows | Suitable for chapter-sized files |
| Validation | 5,000 linear rows | Covered by regression test; expected to remain comfortably interactive on development machines |
| Graph editor load | 1,000 rows | Acceptable for chapter-sized authoring; prefer splitting larger projects by chapter or route |
| CSV editor model | 10,000 rows | Load/search/edit covered; the virtualized list avoids creating a field for every cell |
| Save/restore | Single slot | Constant relative to current dialogue state plus registered save contributors |

Exact timings vary by Unity editor state and machine load. Use the regression test output for local measurements when changing parsing, validation, or graph mapping code.

## Accepted Limits

- Keep one CSV file around a chapter or route segment when authors need graph editing.
- The CSV Editor can safely inspect and make focused edits in larger files, but this does not change the runtime and graph-authoring guidance to split projects by chapter or route.
- For projects above roughly 5,000 rows, split content into multiple CSV files and compose repositories at runtime with `CompositeDialogueRepository`.
- Save data stores the current line, history, choices, progress markers, and registered contributor state. It does not snapshot whole scenario files.
- Runtime loading is eager by default. Streaming or Addressables-backed loading is optional project code until measurements show the simple path is not enough.

## Tradeoffs

The package favors deterministic CSV validation and simple in-memory access over premature streaming. Multi-scenario loading should remain optional until a real project demonstrates that chapter-sized files no longer fit authoring or runtime needs.

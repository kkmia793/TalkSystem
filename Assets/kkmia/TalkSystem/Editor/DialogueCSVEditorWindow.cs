using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace kkmia.TalkSystem.Editor
{
    /// <summary>
    /// Planner-friendly CSV authoring window with a virtualized row list and a focused detail form.
    /// </summary>
    public sealed class DialogueCSVEditorWindow : EditorWindow
    {
        private const string AllSpeakers = "All speakers";
        private const float RowHeight = 50f;

        private static readonly string[] DialogueFields =
        {
            DialogueSchema.Id, DialogueSchema.Speaker, DialogueSchema.Text, DialogueSchema.EmotionKey
        };

        private static readonly string[] FlowFields =
        {
            DialogueSchema.NextId, DialogueSchema.Choices, DialogueSchema.TriggerKey,
            DialogueSchema.ConditionKey, DialogueSchema.EventKey, DialogueSchema.AutoNextSeconds
        };

        private static readonly string[] ProgressFields =
        {
            DialogueSchema.ChapterKey, DialogueSchema.RouteKey, DialogueSchema.EndingKey
        };

        private static readonly string[] PresentationFields =
        {
            DialogueSchema.Background, DialogueSchema.Bgm, DialogueSchema.Se,
            DialogueSchema.Voice, DialogueSchema.Characters
        };

        private static readonly Dictionary<string, string> FieldDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { DialogueSchema.Id, "Stable numeric identifier for this line." },
            { DialogueSchema.Speaker, "Name shown as the speaker." },
            { DialogueSchema.Text, "Dialogue or narration. Commas, quotes, and line breaks are escaped when saved." },
            { DialogueSchema.EmotionKey, "Expression or emotion key used by the presentation layer." },
            { DialogueSchema.NextId, "ID shown after this line. Use -1 to end when there are no choices." },
            { DialogueSchema.Choices, "Choices separated by |. Each entry is Label->NextId or Label->NextId?conditionKey." },
            { DialogueSchema.TriggerKey, "Named entry point used to start dialogue from game state." },
            { DialogueSchema.ConditionKey, "Condition that must pass before this row can be shown." },
            { DialogueSchema.EventKey, "Event dispatched when this row is shown." },
            { DialogueSchema.AutoNextSeconds, "Optional delay before automatically advancing." },
            { DialogueSchema.ChapterKey, "Stable chapter progress marker." },
            { DialogueSchema.RouteKey, "Stable route progress marker." },
            { DialogueSchema.EndingKey, "Stable ending progress marker." },
            { DialogueSchema.Background, "Background cue: key, key#transition, or key#transition:duration." },
            { DialogueSchema.Bgm, "BGM cue: key, key#transition, key#transition:duration, or stop." },
            { DialogueSchema.Se, "One-shot sound effect keys separated by |." },
            { DialogueSchema.Voice, "Voice clip key for this line." },
            { DialogueSchema.Characters, "Stage directives separated by |, for example Alice@left:smile." }
        };

        private readonly DialogueCsvEditorModel _model = new DialogueCsvEditorModel();
        private TextAsset _csvFile;
        private ObjectField _csvField;
        private TextField _searchField;
        private DropdownField _speakerField;
        private TextField _goToIdField;
        private ListView _rowList;
        private ScrollView _detailScroll;
        private Label _detailTitle;
        private Label _rowCountLabel;
        private Label _statusLabel;
        private Button _saveButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _deleteButton;
        private Button _moveUpButton;
        private Button _moveDownButton;
        private VisualElement _diagnostics;
        private bool _syncingSelection;

        [MenuItem("Tools/kkmia/Dialogue CSV Editor")]
        public static void Open()
        {
            var window = GetWindow<DialogueCSVEditorWindow>("Dialogue CSV Editor");
            window.minSize = new Vector2(880f, 520f);
        }

        public static void Open(TextAsset csvFile)
        {
            Open();
            GetWindow<DialogueCSVEditorWindow>().SetCsvFile(csvFile, true);
        }

        [MenuItem("Assets/Open in Dialogue CSV Editor", false, 2000)]
        private static void OpenSelectedCsv()
        {
            Open(Selection.activeObject as TextAsset);
        }

        [MenuItem("Assets/Open in Dialogue CSV Editor", true)]
        private static bool CanOpenSelectedCsv()
        {
            var selected = Selection.activeObject as TextAsset;
            return selected != null && string.Equals(
                Path.GetExtension(AssetDatabase.GetAssetPath(selected)), ".csv", StringComparison.OrdinalIgnoreCase);
        }

        public void CreateGUI()
        {
            saveChangesMessage = "The dialogue CSV has unsaved edits. Save them before closing?";
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);

            BuildSourceBar();
            BuildCommandBar();
            BuildWorkspace();
            BuildStatusBar();
            SetLoadedUiEnabled(false);

            if (_csvFile != null)
                SetCsvFile(_csvFile, true);
        }

        public override void SaveChanges()
        {
            if (SaveCsv())
                base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            _model.MarkSaved();
            SyncDirtyState();
            base.DiscardChanges();
        }

        private void BuildSourceBar()
        {
            var bar = CreateHorizontalBar();
            bar.style.paddingTop = 6f;

            _csvField = new ObjectField("Scenario CSV")
            {
                objectType = typeof(TextAsset),
                allowSceneObjects = false
            };
            _csvField.style.flexGrow = 1f;
            _csvField.RegisterValueChangedCallback(evt =>
                HandleCsvSelectionChanged(evt.newValue as TextAsset, evt.previousValue as TextAsset));
            bar.Add(_csvField);

            var reload = new Button(ReloadCsv) { text = "Reload" };
            reload.tooltip = "Discard the current draft and reload the selected CSV asset.";
            reload.style.marginLeft = 6f;
            bar.Add(reload);

            _saveButton = new Button(() => SaveCsv()) { text = "Save" };
            _saveButton.tooltip = "Save the current draft (Ctrl/Cmd+S).";
            bar.Add(_saveButton);
            rootVisualElement.Add(bar);
        }

        private void BuildCommandBar()
        {
            var bar = CreateHorizontalBar();
            _undoButton = new Button(Undo) { text = "Undo" };
            _redoButton = new Button(Redo) { text = "Redo" };
            bar.Add(_undoButton);
            bar.Add(_redoButton);

            var validate = new Button(ValidateDraft) { text = "Validate Draft" };
            validate.tooltip = "Validate unsaved edits in memory.";
            validate.style.marginLeft = 8f;
            bar.Add(validate);
            bar.Add(new Button(OpenValidator) { text = "Validator" });
            bar.Add(new Button(OpenPreview) { text = "Preview" });
            rootVisualElement.Add(bar);
        }

        private void BuildWorkspace()
        {
            var split = new TwoPaneSplitView(0, 390f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;

            var listPane = new VisualElement();
            listPane.style.flexGrow = 1f;
            listPane.style.paddingLeft = 8f;
            listPane.style.paddingRight = 6f;

            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            _searchField = new TextField("Search");
            _searchField.tooltip = "Search every column, including ID, speaker, text, keys, and custom columns.";
            _searchField.style.flexGrow = 1f;
            _searchField.RegisterValueChangedCallback(_ => ApplyFilters());
            filterRow.Add(_searchField);

            _speakerField = new DropdownField("Speaker", new List<string> { AllSpeakers }, 0);
            _speakerField.style.width = 185f;
            _speakerField.RegisterValueChangedCallback(_ => ApplyFilters());
            filterRow.Add(_speakerField);
            listPane.Add(filterRow);

            var navigationRow = new VisualElement();
            navigationRow.style.flexDirection = FlexDirection.Row;
            _goToIdField = new TextField("Go to ID");
            _goToIdField.style.flexGrow = 1f;
            navigationRow.Add(_goToIdField);
            navigationRow.Add(new Button(GoToId) { text = "Go" });
            _rowCountLabel = new Label("No CSV loaded");
            _rowCountLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _rowCountLabel.style.minWidth = 105f;
            navigationRow.Add(_rowCountLabel);
            listPane.Add(navigationRow);

            var listHeader = new VisualElement();
            listHeader.style.flexDirection = FlexDirection.Row;
            listHeader.style.paddingLeft = 6f;
            listHeader.style.paddingRight = 6f;
            listHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            var idHeader = new Label("ID");
            idHeader.style.width = 82f;
            var speakerHeader = new Label("Speaker");
            speakerHeader.style.width = 110f;
            var textHeader = new Label("Dialogue text");
            textHeader.style.flexGrow = 1f;
            listHeader.Add(idHeader);
            listHeader.Add(speakerHeader);
            listHeader.Add(textHeader);
            listPane.Add(listHeader);

            _rowList = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                makeItem = MakeRowItem,
                bindItem = BindRowItem
            };
            _rowList.style.flexGrow = 1f;
            _rowList.selectionChanged += HandleListSelectionChanged;
            listPane.Add(_rowList);

            var rowButtons = new VisualElement();
            rowButtons.style.flexDirection = FlexDirection.Row;
            rowButtons.style.paddingTop = 5f;
            rowButtons.style.paddingBottom = 6f;
            rowButtons.Add(new Button(AddRow) { text = "Add" });
            rowButtons.Add(new Button(DuplicateRow) { text = "Duplicate" });
            _deleteButton = new Button(DeleteRow) { text = "Delete" };
            rowButtons.Add(_deleteButton);
            _moveUpButton = new Button(() => MoveRow(-1)) { text = "Move Up" };
            _moveDownButton = new Button(() => MoveRow(1)) { text = "Move Down" };
            rowButtons.Add(_moveUpButton);
            rowButtons.Add(_moveDownButton);
            listPane.Add(rowButtons);
            split.Add(listPane);

            var detailPane = new VisualElement();
            detailPane.style.flexGrow = 1f;
            detailPane.style.paddingLeft = 10f;
            detailPane.style.paddingRight = 8f;
            _detailTitle = new Label("Select a dialogue row");
            _detailTitle.style.fontSize = 15f;
            _detailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _detailTitle.style.marginBottom = 5f;
            detailPane.Add(_detailTitle);

            _detailScroll = new ScrollView(ScrollViewMode.Vertical);
            _detailScroll.style.flexGrow = 1f;
            detailPane.Add(_detailScroll);
            split.Add(detailPane);
            rootVisualElement.Add(split);

            _diagnostics = new VisualElement();
            _diagnostics.style.maxHeight = 155f;
            _diagnostics.style.paddingLeft = 8f;
            _diagnostics.style.paddingRight = 8f;
            rootVisualElement.Add(_diagnostics);
        }

        private void BuildStatusBar()
        {
            _statusLabel = new Label("Choose a CSV asset to begin.");
            _statusLabel.style.paddingLeft = 8f;
            _statusLabel.style.paddingRight = 8f;
            _statusLabel.style.paddingTop = 4f;
            _statusLabel.style.paddingBottom = 5f;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            rootVisualElement.Add(_statusLabel);
        }

        private VisualElement MakeRowItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 6f;
            root.style.paddingRight = 6f;

            var id = new Label { name = "row-id" };
            id.style.width = 82f;
            id.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(id);
            var speaker = new Label { name = "row-speaker" };
            speaker.style.width = 110f;
            root.Add(speaker);
            var text = new Label { name = "row-text" };
            text.style.flexGrow = 1f;
            text.style.whiteSpace = WhiteSpace.Normal;
            root.Add(text);
            return root;
        }

        private void BindRowItem(VisualElement element, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= _model.VisibleRowCount) return;
            var rowIndex = _model.VisibleRowIndices[visibleIndex];
            var id = _model.GetCell(rowIndex, _model.GetColumnIndex(DialogueSchema.Id));
            var speaker = _model.GetCell(rowIndex, _model.GetColumnIndex(DialogueSchema.Speaker));
            var text = _model.GetCell(rowIndex, _model.GetColumnIndex(DialogueSchema.Text));
            element.Q<Label>("row-id").text = string.IsNullOrEmpty(id) ? "(no ID)" : id;
            element.Q<Label>("row-speaker").text = string.IsNullOrEmpty(speaker) ? "—" : speaker;
            element.Q<Label>("row-text").text = CollapseForList(text);
            element.tooltip = "CSV row " + (rowIndex + 2) + " · ID " + id;
        }

        private void HandleListSelectionChanged(IEnumerable<object> selection)
        {
            if (_syncingSelection) return;
            var selected = selection.FirstOrDefault();
            if (selected != null && _model.SelectRow((int)selected))
                RebuildDetailForm();
        }

        private void HandleCsvSelectionChanged(TextAsset next, TextAsset previous)
        {
            if (ReferenceEquals(next, _csvFile)) return;
            if (!ConfirmDiscardOrSaveDraft())
            {
                _csvField.SetValueWithoutNotify(previous);
                return;
            }
            SetCsvFile(next, true);
        }

        private void SetCsvFile(TextAsset csvFile, bool load)
        {
            _csvFile = csvFile;
            if (_csvField != null)
                _csvField.SetValueWithoutNotify(csvFile);
            if (load && _statusLabel != null)
                ReloadCsv();
        }

        private void ReloadCsv()
        {
            if (_csvFile == null)
            {
                SetLoadedUiEnabled(false);
                _statusLabel.text = "Choose a CSV asset to begin.";
                return;
            }

            if (_model.IsDirty && !EditorUtility.DisplayDialog(
                    "Reload dialogue CSV?", "Reloading discards the unsaved editor draft.", "Reload", "Cancel"))
                return;

            if (!_model.Load(_csvFile.text))
            {
                SetLoadedUiEnabled(false);
                ShowDiagnostics(_model.LoadDiagnostics, "The CSV has no header row.");
                return;
            }

            _searchField.SetValueWithoutNotify(string.Empty);
            RefreshSpeakerChoices();
            SetLoadedUiEnabled(true);
            RefreshAll("Loaded " + _model.RowCount.ToString("N0") + " dialogue rows.");
            ShowDiagnostics(_model.LoadDiagnostics, string.Empty);
        }

        private bool SaveCsv()
        {
            if (_csvFile == null || _model.ColumnCount == 0) return false;
            var path = AssetDatabase.GetAssetPath(_csvFile);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Cannot save CSV", "The selected TextAsset has no project asset path.", "OK");
                return false;
            }

            try
            {
                File.WriteAllText(path, _model.ToCsv(), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                _csvFile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                _csvField.SetValueWithoutNotify(_csvFile);
                _model.MarkSaved();
                SyncDirtyState();
                _statusLabel.text = "Saved " + path;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Cannot save CSV", exception.Message, "OK");
                return false;
            }
        }

        private void ApplyFilters()
        {
            if (_model.ColumnCount == 0) return;
            var speaker = _speakerField.value == AllSpeakers ? string.Empty : _speakerField.value;
            _model.SetFilter(_searchField.value, speaker);
            RefreshListAndSelection();
            UpdateRowCount();
        }

        private void GoToId()
        {
            if (!_model.SelectRowById(_goToIdField.value))
            {
                _statusLabel.text = "ID was not found: " + _goToIdField.value;
                return;
            }

            _searchField.SetValueWithoutNotify(string.Empty);
            _speakerField.SetValueWithoutNotify(AllSpeakers);
            _model.SetFilter(string.Empty, string.Empty);
            RefreshListAndSelection();
            RebuildDetailForm();
            _statusLabel.text = "Selected ID " + _goToIdField.value + ".";
        }

        private void AddRow()
        {
            if (_model.ColumnCount == 0) return;
            _model.AddRow();
            RefreshAfterEdit("Added a dialogue row.");
        }

        private void DuplicateRow()
        {
            if (_model.DuplicateSelectedRow() >= 0)
                RefreshAfterEdit("Duplicated the selected row with a new ID.");
        }

        private void DeleteRow()
        {
            if (_model.SelectedRowIndex < 0) return;
            var id = _model.GetCell(_model.SelectedRowIndex, _model.GetColumnIndex(DialogueSchema.Id));
            if (!EditorUtility.DisplayDialog(
                    "Delete dialogue row?",
                    "Delete row ID " + (string.IsNullOrEmpty(id) ? "(empty)" : id) +
                    " from the editor draft? References are not changed automatically.",
                    "Delete", "Cancel"))
                return;

            if (_model.DeleteSelectedRow())
                RefreshAfterEdit("Deleted the dialogue row. Validate references before saving.");
        }

        private void MoveRow(int delta)
        {
            if (_model.MoveSelectedRow(delta))
                RefreshAfterEdit(delta < 0 ? "Moved the row up." : "Moved the row down.");
        }

        private void Undo()
        {
            if (_model.Undo())
                RefreshAfterEdit("Undid the last editor change.");
        }

        private void Redo()
        {
            if (_model.Redo())
                RefreshAfterEdit("Redid the editor change.");
        }

        private void RebuildDetailForm()
        {
            _detailScroll.Clear();
            var rowIndex = _model.SelectedRowIndex;
            if (rowIndex < 0 || rowIndex >= _model.RowCount)
            {
                _detailTitle.text = "Select a dialogue row";
                return;
            }

            _detailTitle.text = BuildDetailTitle(rowIndex);
            var rendered = new HashSet<int>();
            AddFieldGroup("Dialogue", DialogueFields, rendered, true);
            AddFieldGroup("Flow and logic", FlowFields, rendered, true);
            AddFieldGroup("Progress", ProgressFields, rendered, false);
            AddFieldGroup("Presentation", PresentationFields, rendered, false);

            var extras = Enumerable.Range(0, _model.ColumnCount).Where(index => !rendered.Contains(index)).ToList();
            if (extras.Count > 0)
                AddFieldGroup("Custom columns", extras, false);
        }

        private void AddFieldGroup(string title, IEnumerable<string> headers, ISet<int> rendered, bool expanded)
        {
            var columns = headers.Select(_model.GetColumnIndex).Where(index => index >= 0 && rendered.Add(index)).ToList();
            AddFieldGroup(title, columns, expanded);
        }

        private void AddFieldGroup(string title, IList<int> columns, bool expanded)
        {
            if (columns.Count == 0) return;
            var foldout = new Foldout { text = title, value = expanded };
            foldout.style.marginBottom = 4f;
            foreach (var columnIndex in columns)
                foldout.Add(CreateField(_model.SelectedRowIndex, columnIndex));
            _detailScroll.Add(foldout);
        }

        private VisualElement CreateField(int rowIndex, int columnIndex)
        {
            var header = _model.Headers[columnIndex];
            var multiline = string.Equals(header, DialogueSchema.Text, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header, DialogueSchema.Choices, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header, DialogueSchema.Characters, StringComparison.OrdinalIgnoreCase);
            var field = new TextField(header)
            {
                value = _model.GetCell(rowIndex, columnIndex),
                isDelayed = true,
                multiline = multiline
            };
            field.style.marginBottom = 5f;
            field.labelElement.style.minWidth = 135f;
            field.labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            string description;
            field.tooltip = FieldDescriptions.TryGetValue(header, out description)
                ? description
                : "Custom CSV column. Talk System preserves this value for game-specific use.";

            if (multiline)
            {
                field.style.minHeight = string.Equals(header, DialogueSchema.Text, StringComparison.OrdinalIgnoreCase) ? 96f : 62f;
                field.style.whiteSpace = WhiteSpace.Normal;
            }

            field.RegisterValueChangedCallback(evt =>
            {
                if (!_model.EditCell(rowIndex, columnIndex, evt.newValue)) return;
                RefreshSpeakerChoices();
                RefreshListAndSelection();
                UpdateRowCount();
                UpdateButtons();
                SyncDirtyState();
                _detailTitle.text = BuildDetailTitle(rowIndex);
                _statusLabel.text = "Edited " + header + ".";
            });
            return field;
        }

        private string BuildDetailTitle(int rowIndex)
        {
            var id = _model.GetCell(rowIndex, _model.GetColumnIndex(DialogueSchema.Id));
            return "Row " + (rowIndex + 2) + " · ID " + (string.IsNullOrEmpty(id) ? "(empty)" : id);
        }

        private void ValidateDraft()
        {
            if (_model.ColumnCount == 0) return;
            var report = DialogueValidator.ValidateCsv(_model.ToCsv());
            var errors = report.Messages.Count(message => message.Severity == DialogueValidationSeverity.Error);
            var warnings = report.Messages.Count(message => message.Severity == DialogueValidationSeverity.Warning);
            var info = report.Messages.Count(message => message.Severity == DialogueValidationSeverity.Info);
            ShowDiagnostics(report, "Errors: " + errors + "  Warnings: " + warnings + "  Info: " + info);
            _statusLabel.text = report.HasErrors ? "Draft validation found errors." : "Draft validation completed without errors.";
        }

        private void ShowDiagnostics(DialogueValidationReport report, string summary)
        {
            _diagnostics.Clear();
            if (!string.IsNullOrEmpty(summary))
            {
                var title = new Label(summary);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                _diagnostics.Add(title);
            }
            if (report == null || report.Messages.Count == 0) return;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 125f;
            foreach (var message in report.Messages.Take(30))
                scroll.Add(new HelpBox(message.ToString(), ToHelpBoxType(message.Severity)));
            if (report.Messages.Count > 30)
                scroll.Add(new Label("… " + (report.Messages.Count - 30) + " more messages. Open Validator for the complete report."));
            _diagnostics.Add(scroll);
        }

        private void OpenValidator()
        {
            if (SaveBeforeOpeningTool()) DialogueValidationWindow.Open(_csvFile);
        }

        private void OpenPreview()
        {
            if (SaveBeforeOpeningTool()) DialoguePreviewWindow.Open(_csvFile);
        }

        private bool SaveBeforeOpeningTool()
        {
            if (!_model.IsDirty) return _csvFile != null;
            return EditorUtility.DisplayDialog(
                       "Save dialogue draft?",
                       "Validator and Preview read the CSV asset from disk. Save the current draft first?",
                       "Save and Open", "Cancel") && SaveCsv();
        }

        private bool ConfirmDiscardOrSaveDraft()
        {
            if (!_model.IsDirty) return true;
            var choice = EditorUtility.DisplayDialogComplex(
                "Unsaved dialogue edits", "Save the current CSV draft before switching assets?",
                "Save", "Cancel", "Discard");
            if (choice == 0) return SaveCsv();
            if (choice == 2)
            {
                _model.MarkSaved();
                SyncDirtyState();
                return true;
            }
            return false;
        }

        private void RefreshAfterEdit(string status)
        {
            RefreshSpeakerChoices();
            RefreshListAndSelection();
            UpdateRowCount();
            UpdateButtons();
            SyncDirtyState();
            RebuildDetailForm();
            _statusLabel.text = status;
        }

        private void RefreshAll(string status)
        {
            RefreshListAndSelection();
            UpdateRowCount();
            UpdateButtons();
            SyncDirtyState();
            RebuildDetailForm();
            _statusLabel.text = status;
        }

        private void RefreshSpeakerChoices()
        {
            if (_speakerField == null) return;
            var current = _speakerField.value;
            var choices = new List<string> { AllSpeakers };
            choices.AddRange(_model.GetSpeakers());
            _speakerField.choices = choices;
            _speakerField.SetValueWithoutNotify(choices.Contains(current) ? current : AllSpeakers);
            var activeSpeaker = _speakerField.value == AllSpeakers ? string.Empty : _speakerField.value;
            _model.SetFilter(_searchField != null ? _searchField.value : string.Empty, activeSpeaker);
        }

        private void RefreshListAndSelection()
        {
            _rowList.itemsSource = _model.VisibleRowIndices;
            _rowList.Rebuild();
            _syncingSelection = true;
            var visibleIndex = _model.VisibleRowIndices.IndexOf(_model.SelectedRowIndex);
            if (visibleIndex >= 0)
            {
                _rowList.SetSelectionWithoutNotify(new[] { visibleIndex });
                _rowList.ScrollToItem(visibleIndex);
            }
            else
            {
                _rowList.ClearSelection();
            }
            _syncingSelection = false;
        }

        private void UpdateRowCount()
        {
            _rowCountLabel.text = _model.VisibleRowCount == _model.RowCount
                ? _model.RowCount.ToString("N0") + " rows"
                : _model.VisibleRowCount.ToString("N0") + " / " + _model.RowCount.ToString("N0");
        }

        private void UpdateButtons()
        {
            var selected = _model.SelectedRowIndex;
            _undoButton.SetEnabled(_model.CanUndo);
            _redoButton.SetEnabled(_model.CanRedo);
            _deleteButton.SetEnabled(selected >= 0);
            _moveUpButton.SetEnabled(selected > 0);
            _moveDownButton.SetEnabled(selected >= 0 && selected < _model.RowCount - 1);
            _saveButton.SetEnabled(_csvFile != null && _model.ColumnCount > 0);
        }

        private void SetLoadedUiEnabled(bool enabled)
        {
            _searchField.SetEnabled(enabled);
            _speakerField.SetEnabled(enabled);
            _goToIdField.SetEnabled(enabled);
            _rowList.SetEnabled(enabled);
            _detailScroll.SetEnabled(enabled);
            _saveButton.SetEnabled(enabled);
            _undoButton.SetEnabled(false);
            _redoButton.SetEnabled(false);
            _deleteButton.SetEnabled(false);
            _moveUpButton.SetEnabled(false);
            _moveDownButton.SetEnabled(false);
        }

        private void SyncDirtyState()
        {
            hasUnsavedChanges = _model.IsDirty;
            titleContent = new GUIContent(_model.IsDirty ? "Dialogue CSV Editor *" : "Dialogue CSV Editor");
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey) return;
            if (evt.keyCode == KeyCode.S)
            {
                SaveCsv();
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.F)
            {
                _searchField.Focus();
                evt.StopPropagation();
                return;
            }
            if (rootVisualElement.focusController.focusedElement is TextField) return;
            if (evt.keyCode == KeyCode.Z)
            {
                if (evt.shiftKey) Redo(); else Undo();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Y)
            {
                Redo();
                evt.StopPropagation();
            }
        }

        private static VisualElement CreateHorizontalBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.paddingLeft = 8f;
            bar.style.paddingRight = 8f;
            bar.style.paddingBottom = 6f;
            return bar;
        }

        private static string CollapseForList(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            var collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return collapsed.Length <= 90 ? collapsed : collapsed.Substring(0, 87) + "…";
        }

        private static HelpBoxMessageType ToHelpBoxType(DialogueValidationSeverity severity)
        {
            switch (severity)
            {
                case DialogueValidationSeverity.Error: return HelpBoxMessageType.Error;
                case DialogueValidationSeverity.Warning: return HelpBoxMessageType.Warning;
                default: return HelpBoxMessageType.Info;
            }
        }
    }
}

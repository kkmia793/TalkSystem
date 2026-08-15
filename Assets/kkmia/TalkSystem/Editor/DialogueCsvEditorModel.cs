using System;
using System.Collections.Generic;
using System.Linq;

namespace kkmia.TalkSystem.Editor
{
    internal sealed class DialogueCsvEditorModel
    {
        internal const int MaxHistoryEntries = 200;

        private abstract class EditCommand
        {
            protected EditCommand(int selectionBefore, int selectionAfter)
            {
                SelectionBefore = selectionBefore;
                SelectionAfter = selectionAfter;
            }

            public int SelectionBefore { get; private set; }
            public int SelectionAfter { get; private set; }
            public long StateBefore { get; set; }
            public long StateAfter { get; set; }
            public abstract void Apply(DialogueCsvEditorModel model);
            public abstract void Revert(DialogueCsvEditorModel model);
        }

        private sealed class CellEditCommand : EditCommand
        {
            private readonly int _rowIndex;
            private readonly int _columnIndex;
            private readonly string _before;
            private readonly string _after;

            public CellEditCommand(int rowIndex, int columnIndex, string before, string after, int selection)
                : base(selection, selection)
            {
                _rowIndex = rowIndex;
                _columnIndex = columnIndex;
                _before = before;
                _after = after;
            }

            public override void Apply(DialogueCsvEditorModel model)
            {
                model.SetCellDirect(_rowIndex, _columnIndex, _after);
            }

            public override void Revert(DialogueCsvEditorModel model)
            {
                model.SetCellDirect(_rowIndex, _columnIndex, _before);
            }
        }

        private sealed class InsertRowCommand : EditCommand
        {
            private readonly int _rowIndex;
            private readonly string[] _row;

            public InsertRowCommand(int rowIndex, string[] row, int selectionBefore)
                : base(selectionBefore, rowIndex)
            {
                _rowIndex = rowIndex;
                _row = CloneRow(row);
            }

            public override void Apply(DialogueCsvEditorModel model)
            {
                model._rows.Insert(_rowIndex, CloneRow(_row));
            }

            public override void Revert(DialogueCsvEditorModel model)
            {
                model._rows.RemoveAt(_rowIndex);
            }
        }

        private sealed class DeleteRowCommand : EditCommand
        {
            private readonly int _rowIndex;
            private readonly string[] _row;

            public DeleteRowCommand(int rowIndex, string[] row, int selectionBefore, int selectionAfter)
                : base(selectionBefore, selectionAfter)
            {
                _rowIndex = rowIndex;
                _row = CloneRow(row);
            }

            public override void Apply(DialogueCsvEditorModel model)
            {
                model._rows.RemoveAt(_rowIndex);
            }

            public override void Revert(DialogueCsvEditorModel model)
            {
                model._rows.Insert(_rowIndex, CloneRow(_row));
            }
        }

        private sealed class MoveRowCommand : EditCommand
        {
            private readonly int _from;
            private readonly int _to;

            public MoveRowCommand(int from, int to)
                : base(from, to)
            {
                _from = from;
                _to = to;
            }

            public override void Apply(DialogueCsvEditorModel model)
            {
                model.MoveRowDirect(_from, _to);
            }

            public override void Revert(DialogueCsvEditorModel model)
            {
                model.MoveRowDirect(_to, _from);
            }
        }

        private readonly List<string> _headers = new List<string>();
        private readonly List<string[]> _rows = new List<string[]>();
        private readonly List<int> _visibleRowIndices = new List<int>();
        private readonly List<EditCommand> _history = new List<EditCommand>();
        private readonly DialogueValidationReport _loadDiagnostics = new DialogueValidationReport();

        private int _historyCursor;
        private long _nextStateId;
        private long _currentStateId;
        private long _savedStateId;
        private string _searchText = string.Empty;
        private string _speakerFilter = string.Empty;

        public IReadOnlyList<string> Headers { get { return _headers; } }
        public List<int> VisibleRowIndices { get { return _visibleRowIndices; } }
        public DialogueValidationReport LoadDiagnostics { get { return _loadDiagnostics; } }
        public int RowCount { get { return _rows.Count; } }
        public int ColumnCount { get { return _headers.Count; } }
        public int VisibleRowCount { get { return _visibleRowIndices.Count; } }
        public int SelectedRowIndex { get; private set; } = -1;
        public bool CanUndo { get { return _historyCursor > 0; } }
        public bool CanRedo { get { return _historyCursor < _history.Count; } }
        public bool IsDirty { get { return _currentStateId != _savedStateId; } }
        public string SearchText { get { return _searchText; } }
        public string SpeakerFilter { get { return _speakerFilter; } }

        public bool Load(string csvText)
        {
            var document = DialogueCsvCodec.Parse(csvText);
            _headers.Clear();
            _rows.Clear();
            _visibleRowIndices.Clear();
            _history.Clear();
            _loadDiagnostics.Clear();
            _historyCursor = 0;
            _nextStateId = 0;
            _currentStateId = 0;
            _savedStateId = 0;
            _searchText = string.Empty;
            _speakerFilter = string.Empty;
            SelectedRowIndex = -1;

            _loadDiagnostics.AddRange(document.Diagnostics.Messages);
            if (document.Headers.Count == 0)
                return false;

            _headers.AddRange(document.Headers);
            foreach (var sourceRow in document.Rows)
            {
                if (sourceRow.Values.Count != _headers.Count)
                {
                    _loadDiagnostics.Add(
                        DialogueValidationSeverity.Warning,
                        sourceRow.RowNumber,
                        string.Empty,
                        "Column count does not match the header. Missing values were padded and extra values were discarded in the editor draft.");
                }

                var row = new string[_headers.Count];
                for (var i = 0; i < row.Length; i++)
                    row[i] = i < sourceRow.Values.Count ? sourceRow.Values[i] ?? string.Empty : string.Empty;
                _rows.Add(row);
            }

            RebuildVisibleRows();
            if (_rows.Count > 0)
                SelectedRowIndex = 0;
            return true;
        }

        public string ToCsv()
        {
            return DialogueCsvCodec.Write(
                _headers,
                _rows.Select(row => (IReadOnlyList<string>)row));
        }

        public void MarkSaved()
        {
            _savedStateId = _currentStateId;
        }

        public string GetCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count || columnIndex < 0 || columnIndex >= _headers.Count)
                return string.Empty;
            return _rows[rowIndex][columnIndex] ?? string.Empty;
        }

        public string[] GetRowCopy(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex < _rows.Count
                ? CloneRow(_rows[rowIndex])
                : new string[0];
        }

        public int GetColumnIndex(string header)
        {
            if (string.IsNullOrEmpty(header)) return -1;
            for (var i = 0; i < _headers.Count; i++)
            {
                if (string.Equals(_headers[i], header, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public bool SelectRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count)
                return false;
            SelectedRowIndex = rowIndex;
            return true;
        }

        public bool SelectRowById(string id)
        {
            var idColumn = GetColumnIndex(DialogueSchema.Id);
            if (idColumn < 0 || string.IsNullOrWhiteSpace(id))
                return false;

            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(GetCell(i, idColumn).Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRowIndex = i;
                    return true;
                }
            }

            return false;
        }

        public void SetFilter(string searchText, string speaker)
        {
            _searchText = (searchText ?? string.Empty).Trim();
            _speakerFilter = (speaker ?? string.Empty).Trim();
            RebuildVisibleRows();
        }

        public IReadOnlyList<string> GetSpeakers()
        {
            var speakerColumn = GetColumnIndex(DialogueSchema.Speaker);
            if (speakerColumn < 0)
                return new string[0];

            return _rows
                .Select(row => row[speakerColumn] ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool EditCell(int rowIndex, int columnIndex, string value)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count || columnIndex < 0 || columnIndex >= _headers.Count)
                return false;

            var before = GetCell(rowIndex, columnIndex);
            var after = value ?? string.Empty;
            if (string.Equals(before, after, StringComparison.Ordinal))
                return false;

            Execute(new CellEditCommand(rowIndex, columnIndex, before, after, SelectedRowIndex));
            return true;
        }

        public int AddRow()
        {
            var insertionIndex = SelectedRowIndex >= 0 ? SelectedRowIndex + 1 : _rows.Count;
            var row = CreateBlankRow();
            Execute(new InsertRowCommand(insertionIndex, row, SelectedRowIndex));
            return insertionIndex;
        }

        public int DuplicateSelectedRow()
        {
            if (SelectedRowIndex < 0 || SelectedRowIndex >= _rows.Count)
                return -1;

            var insertionIndex = SelectedRowIndex + 1;
            var row = CloneRow(_rows[SelectedRowIndex]);
            var idColumn = GetColumnIndex(DialogueSchema.Id);
            if (idColumn >= 0)
                row[idColumn] = SuggestNextId();
            Execute(new InsertRowCommand(insertionIndex, row, SelectedRowIndex));
            return insertionIndex;
        }

        public bool DeleteSelectedRow()
        {
            if (SelectedRowIndex < 0 || SelectedRowIndex >= _rows.Count)
                return false;

            var rowIndex = SelectedRowIndex;
            var selectionAfter = _rows.Count <= 1 ? -1 : Math.Min(rowIndex, _rows.Count - 2);
            Execute(new DeleteRowCommand(rowIndex, _rows[rowIndex], SelectedRowIndex, selectionAfter));
            return true;
        }

        public bool MoveSelectedRow(int delta)
        {
            if (SelectedRowIndex < 0 || SelectedRowIndex >= _rows.Count || delta == 0)
                return false;

            var target = Math.Max(0, Math.Min(_rows.Count - 1, SelectedRowIndex + delta));
            if (target == SelectedRowIndex)
                return false;

            Execute(new MoveRowCommand(SelectedRowIndex, target));
            return true;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;

            var command = _history[_historyCursor - 1];
            command.Revert(this);
            _historyCursor--;
            _currentStateId = command.StateBefore;
            SelectedRowIndex = ClampSelection(command.SelectionBefore);
            RebuildVisibleRows();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;

            var command = _history[_historyCursor];
            command.Apply(this);
            _historyCursor++;
            _currentStateId = command.StateAfter;
            SelectedRowIndex = ClampSelection(command.SelectionAfter);
            RebuildVisibleRows();
            return true;
        }

        private void Execute(EditCommand command)
        {
            if (_historyCursor < _history.Count)
                _history.RemoveRange(_historyCursor, _history.Count - _historyCursor);

            command.StateBefore = _currentStateId;
            command.StateAfter = ++_nextStateId;
            command.Apply(this);
            _history.Add(command);
            _historyCursor++;
            _currentStateId = command.StateAfter;
            SelectedRowIndex = ClampSelection(command.SelectionAfter);

            if (_history.Count > MaxHistoryEntries)
            {
                _history.RemoveAt(0);
                _historyCursor--;
            }

            RebuildVisibleRows();
        }

        private string[] CreateBlankRow()
        {
            var row = new string[_headers.Count];
            for (var i = 0; i < row.Length; i++)
                row[i] = string.Empty;

            var idColumn = GetColumnIndex(DialogueSchema.Id);
            if (idColumn >= 0)
                row[idColumn] = SuggestNextId();

            var nextIdColumn = GetColumnIndex(DialogueSchema.NextId);
            if (nextIdColumn >= 0)
                row[nextIdColumn] = "-1";
            return row;
        }

        private string SuggestNextId()
        {
            var idColumn = GetColumnIndex(DialogueSchema.Id);
            var maximum = 0;
            if (idColumn >= 0)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    int id;
                    if (int.TryParse(GetCell(i, idColumn), out id) && id > maximum)
                        maximum = id;
                }
            }

            return (maximum + 1).ToString();
        }

        private void SetCellDirect(int rowIndex, int columnIndex, string value)
        {
            _rows[rowIndex][columnIndex] = value ?? string.Empty;
        }

        private void MoveRowDirect(int from, int to)
        {
            var row = _rows[from];
            _rows.RemoveAt(from);
            _rows.Insert(to, row);
        }

        private int ClampSelection(int rowIndex)
        {
            if (_rows.Count == 0) return -1;
            return Math.Max(0, Math.Min(_rows.Count - 1, rowIndex));
        }

        private void RebuildVisibleRows()
        {
            _visibleRowIndices.Clear();
            var speakerColumn = GetColumnIndex(DialogueSchema.Speaker);

            for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (!string.IsNullOrEmpty(_speakerFilter))
                {
                    var speaker = speakerColumn >= 0 ? row[speakerColumn] : string.Empty;
                    if (!string.Equals(speaker, _speakerFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (!string.IsNullOrEmpty(_searchText) && !RowContains(row, _searchText))
                    continue;

                _visibleRowIndices.Add(rowIndex);
            }
        }

        private static bool RowContains(IEnumerable<string> row, string searchText)
        {
            foreach (var value in row)
            {
                if (!string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string[] CloneRow(string[] row)
        {
            return row != null ? (string[])row.Clone() : new string[0];
        }
    }
}

using System.Diagnostics;
using System.Linq;
using System.Text;
using kkmia.TalkSystem.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace kkmia.TalkSystem.Tests
{
    public sealed class DialogueCsvEditorModelTests
    {
        private const string Headers = "Id,Speaker,Text,NextId,EmotionKey,TriggerKey,ConditionKey,EventKey,Choices,AutoNextSeconds,CustomNote\n";

        [Test]
        public void Window_CreateGuiUsesFixedHeightVirtualizedList()
        {
            var window = ScriptableObject.CreateInstance<DialogueCSVEditorWindow>();
            try
            {
                window.rootVisualElement.Clear();
                window.CreateGUI();

                var list = window.rootVisualElement.Q<ListView>();
                Assert.IsNotNull(list);
                Assert.AreEqual(CollectionVirtualizationMethod.FixedHeight, list.virtualizationMethod);
                Assert.AreEqual(SelectionType.Single, list.selectionType);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void LoadEditAndSave_PreservesMultilineAndCustomColumns()
        {
            var model = new DialogueCsvEditorModel();
            var csv = Headers + "1,Alice,Hello,-1,,,,,,,draft\n";

            Assert.IsTrue(model.Load(csv));
            Assert.IsTrue(model.EditCell(0, model.GetColumnIndex(DialogueSchema.Text), "First line\nSecond, \"quoted\" line"));

            var document = DialogueCsvCodec.Parse(model.ToCsv());

            Assert.AreEqual(11, document.Headers.Count);
            Assert.AreEqual("First line\nSecond, \"quoted\" line", document.Rows[0].Values[2]);
            Assert.AreEqual("draft", document.Rows[0].Values[10]);
        }

        [Test]
        public void Filter_SearchesAllColumnsAndCombinesSpeakerFilter()
        {
            var model = new DialogueCsvEditorModel();
            var csv = Headers +
                      "1,Alice,Open the door,-1,,,,,,,opening\n" +
                      "2,Bob,Wait here,-1,,,,,,,door-note\n" +
                      "3,Alice,Look outside,-1,,,,,,,balcony\n";
            model.Load(csv);

            model.SetFilter("door", string.Empty);
            Assert.AreEqual(new[] { 0, 1 }, model.VisibleRowIndices.ToArray());

            model.SetFilter("door", "Bob");
            Assert.AreEqual(new[] { 1 }, model.VisibleRowIndices.ToArray());

            model.SetFilter("BALCONY", "alice");
            Assert.AreEqual(new[] { 2 }, model.VisibleRowIndices.ToArray());
        }

        [Test]
        public void CellEdit_UndoRedoTracksDirtyStateWithoutDatabaseSnapshots()
        {
            var model = new DialogueCsvEditorModel();
            model.Load(Headers + "1,Alice,Before,-1,,,,,,,\n");
            var textColumn = model.GetColumnIndex(DialogueSchema.Text);

            Assert.IsTrue(model.EditCell(0, textColumn, "After"));
            Assert.IsTrue(model.IsDirty);
            Assert.AreEqual("After", model.GetCell(0, textColumn));

            Assert.IsTrue(model.Undo());
            Assert.IsFalse(model.IsDirty);
            Assert.AreEqual("Before", model.GetCell(0, textColumn));

            Assert.IsTrue(model.Redo());
            Assert.IsTrue(model.IsDirty);
            Assert.AreEqual("After", model.GetCell(0, textColumn));

            model.MarkSaved();
            Assert.IsFalse(model.IsDirty);
        }

        [Test]
        public void RowCommands_AddDuplicateMoveDeleteAndUndo()
        {
            var model = new DialogueCsvEditorModel();
            model.Load(Headers +
                       "10,Alice,First,20,,,,,,,\n" +
                       "20,Bob,Second,-1,,,,,,,\n");

            Assert.AreEqual(1, model.DuplicateSelectedRow());
            Assert.AreEqual("21", model.GetCell(1, model.GetColumnIndex(DialogueSchema.Id)));
            Assert.AreEqual("First", model.GetCell(1, model.GetColumnIndex(DialogueSchema.Text)));

            Assert.IsTrue(model.MoveSelectedRow(1));
            Assert.AreEqual("21", model.GetCell(2, model.GetColumnIndex(DialogueSchema.Id)));

            Assert.IsTrue(model.DeleteSelectedRow());
            Assert.AreEqual(2, model.RowCount);
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(3, model.RowCount);
            Assert.AreEqual("21", model.GetCell(2, model.GetColumnIndex(DialogueSchema.Id)));

            Assert.IsTrue(model.Undo());
            Assert.AreEqual("21", model.GetCell(1, model.GetColumnIndex(DialogueSchema.Id)));
        }

        [Test]
        public void LargeScenario_LoadSearchAndEditMaintainsStableCounts()
        {
            const int rowCount = 10000;
            var csv = new StringBuilder(Headers);
            for (var i = 1; i <= rowCount; i++)
            {
                csv.Append(i).Append(',')
                    .Append(i % 2 == 0 ? "Alice" : "Bob").Append(',')
                    .Append(i == rowCount ? "unique search marker" : "Dialogue line " + i).Append(',')
                    .Append(i == rowCount ? -1 : i + 1)
                    .Append(",,,,,,,note-").Append(i).Append('\n');
            }

            var stopwatch = Stopwatch.StartNew();
            var model = new DialogueCsvEditorModel();
            Assert.IsTrue(model.Load(csv.ToString()));
            model.SetFilter("unique search marker", string.Empty);
            Assert.IsTrue(model.EditCell(rowCount - 1, model.GetColumnIndex(DialogueSchema.Text), "edited unique search marker"));
            stopwatch.Stop();

            Assert.AreEqual(rowCount, model.RowCount);
            Assert.AreEqual(1, model.VisibleRowCount);
            Assert.AreEqual(rowCount - 1, model.VisibleRowIndices[0]);
            Assert.AreEqual("edited unique search marker", model.GetCell(rowCount - 1, model.GetColumnIndex(DialogueSchema.Text)));
            TestContext.WriteLine("10,000-row editor model load/search/edit: " + stopwatch.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void History_IsBoundedToConfiguredEntryCount()
        {
            var model = new DialogueCsvEditorModel();
            model.Load(Headers + "1,Alice,Initial,-1,,,,,,,\n");
            var textColumn = model.GetColumnIndex(DialogueSchema.Text);

            for (var i = 0; i < DialogueCsvEditorModel.MaxHistoryEntries + 25; i++)
                model.EditCell(0, textColumn, "Edit " + i);

            var undoCount = 0;
            while (model.Undo()) undoCount++;

            Assert.AreEqual(DialogueCsvEditorModel.MaxHistoryEntries, undoCount);
        }
    }
}

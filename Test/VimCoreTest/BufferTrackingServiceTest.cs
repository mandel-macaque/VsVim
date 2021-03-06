﻿using System;
using Vim.EditorHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Vim.Extensions;
using Xunit;

namespace Vim.UnitTest
{
    public abstract class BufferTrackingServiceTest : VimTestBase
    {
        private readonly BufferTrackingService _bufferTrackingServiceRaw;
        private readonly IBufferTrackingService _bufferTrackingService;

        public BufferTrackingServiceTest()
        {
            _bufferTrackingServiceRaw = new BufferTrackingService();
            _bufferTrackingService = _bufferTrackingServiceRaw;
        }

        public sealed class EditTrackingTest : BufferTrackingServiceTest
        {
            private ITextBuffer _textBuffer;
            private ITextUndoHistory _undoHistory;

            private void Create(params string[] lines)
            {
                _textBuffer = CreateTextBuffer(lines);
                var undoManagerProvider = TextBufferUndoManagerProvider;
                var undoManager = undoManagerProvider.GetTextBufferUndoManager(_textBuffer);
                _undoHistory = undoManager.TextBufferUndoHistory;
            }

            private ITrackingLineColumn Create(ITextBuffer buffer, int line, int column)
            {
                return _bufferTrackingServiceRaw.CreateLineOffset(buffer, line, column, LineColumnTrackingMode.Default);
            }

            private static void AssertPoint(ITrackingLineColumn tlc, int lineNumber, int column)
            {
                var point = tlc.Point;
                Assert.True(point.IsSome());
                AssertLineColumn(point.Value, lineNumber, column);
            }

            private static void AssertLineColumn(SnapshotPoint point, int lineNumber, int column)
            {
                var line = point.GetContainingLine();
                Assert.Equal(lineNumber, line.LineNumber);
                Assert.Equal(column, point.Position - line.Start.Position);
            }

            [WpfFact]
            public void SimpleEdit1()
            {
                var buffer = CreateTextBuffer("foo bar", "baz");
                var tlc = Create(buffer, 0, 1);
                buffer.Replace(new Span(0, 0), "foo");
                AssertPoint(tlc, 0, 1);
            }

            /// <summary>
            /// Replace the line, shouldn't affect the column tracking
            /// </summary>
            [WpfFact]
            public void SimpleEdit2()
            {
                var buffer = CreateTextBuffer("foo bar", "baz");
                var tlc = Create(buffer, 0, 1);
                buffer.Replace(new Span(0, 5), "barbar");
                AssertPoint(tlc, 0, 1);
            }

            /// <summary>
            /// Edit at the end of the line
            /// </summary>
            [WpfFact]
            public void SimpleEdit3()
            {
                var buffer = CreateTextBuffer("foo bar", "baz");
                var tlc = Create(buffer, 0, 1);
                buffer.Replace(new Span(5, 0), "barbar");
                AssertPoint(tlc, 0, 1);
            }

            /// <summary>
            /// Edit a different line
            /// </summary>
            [WpfFact]
            public void SimpleEdit4()
            {
                var buffer = CreateTextBuffer("foo bar", "baz");
                var tlc = Create(buffer, 0, 1);
                buffer.Replace(buffer.GetLineRange(1, 1).ExtentIncludingLineBreak.Span, "hello world");
                AssertPoint(tlc, 0, 1);
            }

            /// <summary>
            /// Deleting the line containing the tracking item should cause it to be deleted
            /// </summary>
            [WpfFact]
            public void Edit_DeleteLine()
            {
                var buffer = CreateTextBuffer("foo", "bar");
                var tlc = Create(buffer, 0, 0);
                buffer.Delete(buffer.GetLine(0).ExtentIncludingLineBreak.Span);
                Assert.True(tlc.Point.IsNone());
                Assert.True(tlc.VirtualPoint.IsNone());
            }

            /// <summary>
            /// Deleting lines containing the tracking item and then undoing it
            /// should not affect its postion
            /// </summary>
            [WpfFact]
            public void Edit_DeleteLinesAndUndo()
            {
                Create("foo", "bar", "baz", "qux");
                var tlc = Create(_textBuffer, 1, 0);
                AssertPoint(tlc, 1, 0);
                using (var undoTransaction = _undoHistory.CreateTransaction("delete"))
                {
                    _textBuffer.Delete(_textBuffer.GetLineRange(1, 2).ExtentIncludingLineBreak.Span);
                    undoTransaction.Complete();
                }
                Assert.Equal(new[] { "foo", "qux" }, _textBuffer.GetLines());
                Assert.True(tlc.Point.IsNone());
                Assert.True(tlc.VirtualPoint.IsNone());
                _undoHistory.Undo(1);
                Assert.Equal(new[] { "foo", "bar", "baz", "qux" }, _textBuffer.GetLines());
                AssertPoint(tlc, 1, 0);
            }

            /// <summary>
            /// Deleting lines containing a tracking span and then undoing it
            /// should not affect its postion
            /// </summary>
            [WpfFact]
            public void Edit_DeleteVisualLinesAndUndo()
            {
                Create("foo", "bar", "baz", "qux");
                var snapshot = _textBuffer.CurrentSnapshot;
                var visualSpan = VisualSpan.NewLine(new SnapshotLineRange(snapshot, 1, 2));
                var trackingVisualSpan = _bufferTrackingService.CreateVisualSpan(visualSpan);
                using (var undoTransaction = _undoHistory.CreateTransaction("delete"))
                {
                    _textBuffer.Delete(_textBuffer.GetLineRange(1, 2).ExtentIncludingLineBreak.Span);
                    undoTransaction.Complete();
                }
                Assert.Equal(new[] { "foo", "qux" }, _textBuffer.GetLines());
                _undoHistory.Undo(1);
                Assert.Equal(new[] { "foo", "bar", "baz", "qux" }, _textBuffer.GetLines());
                var newSnapshot = _textBuffer.CurrentSnapshot;
                var newVisualSpan = VisualSpan.NewLine(new SnapshotLineRange(newSnapshot, 1, 2));
                Assert.True(trackingVisualSpan.VisualSpan.IsSome());
                Assert.Equal(newVisualSpan, trackingVisualSpan.VisualSpan.Value);
            }

            /// <summary>
            /// Deleting the line below shouldn't affect it
            /// </summary>
            [WpfFact]
            public void Edit_DeleteLineBelow()
            {
                var buffer = CreateTextBuffer("foo", "bar");
                var tlc = Create(buffer, 0, 2);
                buffer.Delete(buffer.GetLine(1).ExtentIncludingLineBreak.Span);
                AssertPoint(tlc, 0, 2);
            }

            /// <summary>
            /// Deleting the line above should just adjust it
            /// </summary>
            [WpfFact]
            public void Edit_DeleteLineAbove()
            {
                var buffer = CreateTextBuffer("foo", "bar", "baz");
                var tlc = Create(buffer, 1, 2);
                buffer.Delete(buffer.GetLine(0).ExtentIncludingLineBreak.Span);
                AssertPoint(tlc, 0, 2);
            }

            [WpfFact]
            public void TruncatingEdit1()
            {
                var buffer = CreateTextBuffer("foo bar baz");
                var tlc = Create(buffer, 0, 5);
                buffer.Replace(buffer.GetLine(0).ExtentIncludingLineBreak, "yes");
                AssertPoint(tlc, 0, 3);
            }

            /// <summary>
            /// Make it 0 width
            /// </summary>
            [WpfFact]
            public void TruncatingEdit2()
            {
                var buffer = CreateTextBuffer("foo bar baz");
                var tlc = Create(buffer, 0, 5);
                buffer.Replace(buffer.GetLine(0).ExtentIncludingLineBreak, "");
                AssertPoint(tlc, 0, 0);
            }

            /// <summary>
            /// Adding a line at the start of a block should shift the block down
            /// </summary>
            [WpfFact]
            public void VisualSelection_Block_AddLineAbove()
            {
                var textBuffer = CreateTextBuffer("cats", "dogs", "fish");
                var visualSelection = VisualSelection.NewBlock(
                    textBuffer.GetBlockSpan(1, 2, 1, 2),
                    BlockCaretLocation.BottomRight);
                var trackingVisualSelection = _bufferTrackingService.CreateVisualSelection(visualSelection);
                textBuffer.Insert(textBuffer.GetLine(1).Start, Environment.NewLine);
                var newVisualSelection = VisualSelection.NewBlock(
                    textBuffer.GetBlockSpan(1, 2, 2, 2),
                    BlockCaretLocation.BottomRight);
                Assert.True(trackingVisualSelection.VisualSelection.IsSome());
                Assert.Equal(newVisualSelection, trackingVisualSelection.VisualSelection.Value);
            }

            /// <summary>
            /// Adding a line at the start of a block should shift the block down
            /// </summary>
            [WpfFact]
            public void VisualSpan_Block_AddLineAbove()
            {
                var textBuffer = CreateTextBuffer("cats", "dogs", "fish");
                var visualSpan = VisualSpan.NewBlock(textBuffer.GetBlockSpan(1, 2, 1, 2));
                var trackingVisualSpan = _bufferTrackingService.CreateVisualSpan(visualSpan);
                textBuffer.Insert(textBuffer.GetLine(1).Start, Environment.NewLine);
                var newVisualSpan = VisualSpan.NewBlock(textBuffer.GetBlockSpan(1, 2, 2, 2));
                Assert.True(trackingVisualSpan.VisualSpan.IsSome());
                Assert.Equal(newVisualSpan, trackingVisualSpan.VisualSpan.Value);
            }

            /// <summary>
            /// When tracking a Visual Character span an edit before the point should not move the
            /// start of the selection to the right
            /// </summary>
            [WpfFact]
            public void VisualSpan_Character_EditBefore()
            {
                var textBuffer = CreateTextBuffer("cat", "dog");
                var visualSpan = VisualSpan.NewCharacter(new CharacterSpan(textBuffer.GetPoint(0), 1, 2));
                var trackingVisualSpan = _bufferTrackingService.CreateVisualSpan(visualSpan);
                textBuffer.Insert(0, "bat ");
                var newVisualSpan = VisualSpan.NewCharacter(new CharacterSpan(textBuffer.GetPoint(0), 1, 2));
                Assert.True(trackingVisualSpan.VisualSpan.IsSome());
                Assert.Equal(newVisualSpan, trackingVisualSpan.VisualSpan.Value);
            }

            /// <summary>
            /// When tracking a Visual Character span which spans multiple lines an edit before 
            /// the point should not move the start of the selection to the right
            /// </summary>
            [WpfFact]
            public void VisualSpan_Character_EditBeforeMultiLine()
            {
                var textBuffer = CreateTextBuffer("cat", "dog");
                var visualSpan = VisualSpan.NewCharacter(new CharacterSpan(textBuffer.GetPoint(0), 2, 1));
                var trackingVisualSpan = _bufferTrackingService.CreateVisualSpan(visualSpan);
                textBuffer.Insert(0, "bat ");
                var newVisualSpan = VisualSpan.NewCharacter(new CharacterSpan(textBuffer.GetPoint(0), 2, 1));
                Assert.True(trackingVisualSpan.VisualSpan.IsSome());
                Assert.Equal(newVisualSpan, trackingVisualSpan.VisualSpan.Value);
            }
        }

        public sealed class CloseTest : BufferTrackingServiceTest
        {
            /// <summary>
            /// Ensure that Close properly disposes of the underlying data 
            /// </summary>
            [WpfFact]
            public void VisualSpanAll()
            {
                var textBuffer = CreateTextBuffer("cat");
                foreach (var visualKind in VisualKind.All)
                {
                    var visualSpan = VisualSpan.CreateForSpan(textBuffer.GetSpan(0, 1), visualKind, tabStop: 4);
                    var trackingVisualSpan = _bufferTrackingService.CreateVisualSpan(visualSpan);
                    Assert.True(_bufferTrackingService.HasTrackingItems(textBuffer));
                    trackingVisualSpan.Close();
                    Assert.False(_bufferTrackingService.HasTrackingItems(textBuffer));
                }
            }

            [WpfFact]
            public void VisualSelectionAll()
            {
                var textBuffer = CreateTextBuffer("cat");
                foreach (var visualKind in VisualKind.All)
                {
                    var visualSpan = VisualSpan.CreateForSpan(textBuffer.GetSpan(0, 1), visualKind, tabStop: 4);
                    var visualSelection = VisualSelection.Create(visualSpan, SearchPath.Forward, textBuffer.GetVirtualPoint(0));
                    var trackingVisualSelection = _bufferTrackingService.CreateVisualSelection(visualSelection);
                    Assert.True(_bufferTrackingService.HasTrackingItems(textBuffer));
                    trackingVisualSelection.Close();
                    Assert.False(_bufferTrackingService.HasTrackingItems(textBuffer));
                }
            }

            [WpfFact]
            public void LineColumn()
            {
                var textBuffer = CreateTextBuffer("cat");
                var trackingLineColumn = _bufferTrackingService.CreateLineOffset(textBuffer, 0, 0, LineColumnTrackingMode.Default);
                Assert.True(_bufferTrackingService.HasTrackingItems(textBuffer));
                trackingLineColumn.Close();
                Assert.False(_bufferTrackingService.HasTrackingItems(textBuffer));
            }
        }
    }
}

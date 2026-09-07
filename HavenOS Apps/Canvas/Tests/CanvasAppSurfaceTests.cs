using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasAppSurfaceTests
{
    [Fact]
    public void CreateManipulateInkPanUndoRedoKeepsCanonicalCanvasDocument()
    {
        var surface = CanvasAppSurface.Create("Journey");

        Assert.Equal("haven://apps/canvas", CanvasAppSurface.Route);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(surface.Document));
        Assert.Equal(NotesLayoutMode.InfiniteCanvas, surface.Document.LayoutMode);

        var note = surface.AddTextNote("Idea");
        var shape = surface.AddShape("Result");
        Assert.True(surface.MoveObject(note.Id, 120, 140, 10));

        var connector = surface.Connect(note.Id, shape.Id, "leads to");
        Assert.NotNull(connector);

        Assert.True(surface.DrawStroke([
            new CanvasPointerSample(40, 45, 0.4, TimestampMilliseconds: 1000),
            new CanvasPointerSample(70, 80, 0.6, TimestampMilliseconds: 1016),
            new CanvasPointerSample(100, 105, 0.8, TimestampMilliseconds: 1032)
        ]));
        Assert.True(surface.Pan(0, 0, 35, -20));
        surface.SetZoom(1.5);

        var snapshot = surface.Snapshot;
        Assert.Equal(3, snapshot.ObjectCount);
        Assert.Equal(1, snapshot.StrokeCount);
        Assert.Equal(1.5, snapshot.Zoom);
        Assert.Equal(35, snapshot.OffsetX);
        Assert.Equal(-20, snapshot.OffsetY);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));

        Assert.True(surface.Undo());
        Assert.Empty(surface.Board.Strokes);
        Assert.Equal(3, surface.Board.Objects.Count);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));

        Assert.True(surface.Redo());
        Assert.Single(surface.Board.Strokes);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));
    }

    [Fact]
    public void AttachRejectsOrdinaryNotesDocument()
    {
        var document = NotesDocument.Create("Notes");

        var error = Assert.Throws<ArgumentException>(() => new CanvasAppSurface(document));

        Assert.Contains("Haven Canvas document", error.Message);
    }
}

using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Canvas;

public sealed partial class CanvasPage
{
    private UserPreferencesService _preferences = null!;
    private int _boardIndex;

    private void InitializeRecovery(UserPreferencesService preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _route.BoardRequested += OnBoardRequested;
        _route.AddBoardRequested += OnAddBoardRequested;
        _route.BoardRenameRequested += OnBoardRenameRequested;
        _route.DeleteBoardRequested += OnDeleteBoardRequested;
        _route.SavePenPresetRequested += OnSavePenPresetRequested;
        _route.LibraryRequested += OnLibraryRequested;
        _route.DocumentOpenRequested += OnDocumentOpenRequested;

        // Register before the legacy OnLoaded hook is attached by the constructor. This handler marks
        // initialization started synchronously, so the old auto-open path becomes a no-op and Canvas
        // opens on the local library landing instead.
        Loaded += OnCanvasLibraryLoaded;
    }

    private void UnwireRecovery()
    {
        Loaded -= OnCanvasLibraryLoaded;
        _route.BoardRequested -= OnBoardRequested;
        _route.AddBoardRequested -= OnAddBoardRequested;
        _route.BoardRenameRequested -= OnBoardRenameRequested;
        _route.DeleteBoardRequested -= OnDeleteBoardRequested;
        _route.SavePenPresetRequested -= OnSavePenPresetRequested;
        _route.LibraryRequested -= OnLibraryRequested;
        _route.DocumentOpenRequested -= OnDocumentOpenRequested;
    }

    private async void OnCanvasLibraryLoaded(object? sender, EventArgs e)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        SetBusy(true);
        try
        {
            await RefreshDocumentsAsync(CancellationToken.None);
            ShowLibrary();
            _autosaveTimer.Start();
            _bus.Fire("Canvas.Opened");
        }
        catch (Exception exception)
        {
            _initialized = false;
            _route.SetStatus("Couldn’t open local canvases: " + exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowLibrary()
    {
        Document = null;
        _controller = null;
        _boardIndex = 0;
        _dirty = false;
        _route.SetLibrary(_documents);
        _bus.Fire("Canvas.Library.Opened");
    }

    private async void OnLibraryRequested(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (Document is not null && _dirty && !await SaveAsync("Autosave before opening Canvas library")) return;
            await RefreshDocumentsAsync(CancellationToken.None);
            ShowLibrary();
        }, "open Canvas library");
    }

    private async void OnDocumentOpenRequested(Guid documentId)
    {
        await RunBusyAsync(async () =>
        {
            await RefreshDocumentsAsync(CancellationToken.None);
            var index = IndexOf(documentId);
            if (index < 0 || index >= _documents.Count || _documents[index].Id != documentId)
            {
                _route.SetStatus("That local Canvas no longer exists.");
                return;
            }
            await OpenDocumentAtAsync(index, CancellationToken.None, true);
        }, "open this canvas");
    }

    private void SetRecoveryDocument(NotesDocument document, int documentIndex)
    {
        Document = document;
        _documentIndex = Math.Clamp(documentIndex, 0, Math.Max(0, _documents.Count - 1));
        _boardIndex = 0;
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(document, _boardIndex), null);
        _dirty = false;
        _route.ShowWorkspace();
        RefreshScene();
    }

    private static CanvasInteractionController CreateBoardController(NotesCanvasData board, CanvasInteractionController? previous)
    {
        var controller = new CanvasInteractionController(board);
        if (previous is null) return controller;
        controller.Tool = previous.Tool;
        controller.PenColour = previous.PenColour;
        controller.PenWidth = previous.PenWidth;
        controller.PenOpacity = previous.PenOpacity;
        controller.PenEffect = previous.PenEffect;
        controller.EraserMode = previous.EraserMode;
        controller.GridSize = previous.GridSize;
        return controller;
    }

    private void OnBoardRequested(int index) => SwitchBoard(index);

    private void SwitchBoard(int index)
    {
        if (Document is null || _controller is null) return;
        var count = CanvasDocumentModel.GetBoardCount(Document);
        if (count == 0) return;
        index = Math.Clamp(index, 0, count - 1);
        if (index == _boardIndex) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        var previous = _controller;
        _boardIndex = index;
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        RefreshScene();
    }

    private void OnAddBoardRequested(object? sender, EventArgs e)
    {
        if (Document is null || _controller is null) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        var previous = _controller;
        _boardIndex = CanvasDocumentModel.AddBoard(Document);
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        MarkDirty();
        RefreshScene();
    }

    private void OnBoardRenameRequested(int index, string title)
    {
        if (Document is null) return;
        var documentId = Document.Id;
        if (!CanvasDocumentModel.RenameBoard(Document, index, title)) return;
        if (Document.Id != documentId) throw new InvalidOperationException("Canvas board rename changed document identity.");
        MarkDirty();
        RefreshScene();
    }

    private void OnDeleteBoardRequested(int index)
    {
        if (Document is null || _controller is null) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        if (!CanvasDocumentModel.RemoveBoard(Document, index)) return;
        var previous = _controller;
        _boardIndex = Math.Clamp(_boardIndex > index ? _boardIndex - 1 : _boardIndex, 0, CanvasDocumentModel.GetBoardCount(Document) - 1);
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        MarkDirty();
        RefreshScene();
    }

    private void OnSavePenPresetRequested(CanvasPenPresetPreference requested)
    {
        var saved = _preferences.SaveCanvasPenPreset(requested);
        if (saved is null)
        {
            _route.SetStatus("Canvas pen presets were created by a newer Haven version, so this version left them unchanged.");
            return;
        }
        _route.SetPenPresets(_preferences.CanvasPenPresets);
        _route.SetStatus($"Saved pen preset ‘{saved.Name}’.");
    }

    private void RefreshRecoveryChrome()
    {
        if (Document is null) return;
        _route.SetBoards(CanvasDocumentModel.GetBoardTitles(Document), _boardIndex);
        _route.SetPenPresets(_preferences.CanvasPenPresets);
    }

    private void ReleaseInteractionForPersistence()
    {
        if (_controller is null) return;
        var committedMutation = _route.ReleaseInteraction();
        SyncBoardReference();
        if (committedMutation) MarkDirty();
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IDataWorkbookRepository _repository;
    private readonly IDataWorkbookFormatService _formats;
    private readonly IDataWorkbookQueryService _queries;
    private readonly GenUiLiveActivityTracker _activities;
    private readonly GenUiInstanceStore _genUiInstances;
    private readonly DataHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private IReadOnlyList<DataWorkbookSummary> _workbooks = [];
    private DataQueryResult? _lastQueryResult;
    private int _workbookIndex;
    private int _sheetIndex;
    private int _queryIndex;
    private int _selectedRow;
    private int _selectedColumn;
    private int _drawingIndex;
    private int _rowOffset;
    private int _columnOffset;
    private int _saveRunning;
    private bool _initialized;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public DataPage(HavenEventBus bus, IDataWorkbookRepository repository, IDataWorkbookFormatService formats, IDataWorkbookQueryService queries, GenUiLiveActivityTracker? activities = null, GenUiInstanceStore? genUiInstances = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _activities = activities ?? new GenUiLiveActivityTracker();
        _genUiInstances = genUiInstances ?? new GenUiInstanceStore();
        InitializeComponent();
        _route = new DataHavenScene(); Scene.Root = _route.Root; _ = VisualQueryGraph; _ = SpreadsheetChrome; InitializeSpreadsheetEditingTools(); InitializeRecoveredDataUi();
        _route.PreviousWorkbookRequested += OnPreviousWorkbookRequested; _route.NextWorkbookRequested += OnNextWorkbookRequested; _route.NewWorkbookRequested += OnNewWorkbookRequested; _route.SaveRequested += OnSaveRequested; _route.ImportRequested += OnImportRequested; _route.ExportRequested += OnExportRequested;
        _route.AddSheetRequested += OnAddSheetRequested; _route.DeleteSheetRequested += OnDeleteSheetRequested; _route.AddQueryRequested += OnAddQueryRequested; _route.DeleteQueryRequested += OnDeleteQueryRequested; _route.BuildSqlRequested += OnBuildSqlRequested; _route.RunQueryRequested += OnRunQueryRequested;
        _route.AddShapeRequested += OnAddShapeRequested; _route.PreviousDrawingRequested += OnPreviousDrawingRequested; _route.NextDrawingRequested += OnNextDrawingRequested; _route.RotateDrawingRequested += OnRotateDrawingRequested; _route.DeleteDrawingRequested += OnDeleteDrawingRequested;
        _route.SheetSelected += OnSheetSelected; _route.QuerySelected += OnQuerySelected; _route.CellSelected += OnCellSelected; _route.GridWindowRequested += OnGridWindowRequested; _route.SpreadsheetUndoRequested += UndoSpreadsheetEdit; _route.SpreadsheetRedoRequested += RedoSpreadsheetEdit;
        _route.WorkbookTitleChanged += OnWorkbookTitleChanged; _route.SheetNameChanged += OnSheetNameChanged; _route.CellValueChanged += OnCellValueChanged; _route.CellFormulaChanged += OnCellFormulaChanged; _route.QueryNameChanged += OnQueryNameChanged; _route.SqlChanged += OnSqlChanged;
        _route.VisualSourceChanged += value => UpdateVisual(visual => visual.Source = value); _route.VisualColumnsChanged += value => UpdateVisual(visual => visual.Columns = value); _route.VisualFilterChanged += value => UpdateVisual(visual => visual.Filter = value); _route.VisualGroupChanged += value => UpdateVisual(visual => visual.GroupBy = value); _route.VisualOrderChanged += value => UpdateVisual(visual => visual.OrderBy = value); _route.VisualLimitChanged += OnVisualLimitChanged;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += OnLoaded; DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public DataWorkbook? Workbook { get; private set; }
    public bool IsDirty => _dirty;
    internal DataHavenScene Route => _route; internal HavenSceneControl SceneHost => Scene; internal Haven.UI.Components.Page SceneRoot => _route.Root;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed) return; _initialized = true; SetBusy(true);
        try
        {
            await RefreshWorkbooksAsync(cancellationToken);
            if (_workbooks.Count == 0)
            {
                Workbook = null;
                _route.SetLandingState(true);
            }
            else
            {
                await OpenWorkbookAtAsync(0, cancellationToken, false);
            }
            _autosaveTimer.Start(); _bus.Fire("Data.Opened");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _initialized = false; throw; }
        catch (Exception ex) { _initialized = false; _route.SetStatus("Couldn’t open local Data workbooks: " + ex.Message); }
        finally { SetBusy(false); }
    }

    public async Task<bool> SaveAsync(string reason = "Manual save", CancellationToken cancellationToken = default)
    {
        if (Workbook is null) return true; if (Interlocked.Exchange(ref _saveRunning, 1) != 0) return false;
        try
        {
            RecalculateWorkbook(); if (_formulaReport.ChangedCells > 0) _dirty = true; if (!_dirty) { RenderFormulaState(); return true; }
            if (string.IsNullOrWhiteSpace(Workbook.Title)) Workbook.Title = "Untitled workbook";
            var result = await _repository.SaveAsync(Workbook, reason, cancellationToken); Workbook.Version = result.Version; _dirty = false;
            await RefreshWorkbooksAsync(cancellationToken); _workbookIndex = IndexOfWorkbook(Workbook.Id); RenderCurrent(); _route.SetStatus($"Saved locally at {result.SavedAt.LocalDateTime:t} · v{result.Version}"); _bus.Fire("Data.Saved"); return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t save this workbook: " + ex.Message); return false; }
        finally { Interlocked.Exchange(ref _saveRunning, 0); }
    }

    internal async Task<bool> ImportFromPathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (Workbook is not null && _dirty && !await SaveAsync("Autosave before import", cancellationToken)) return false;
        try
        {
            var workbook = await _formats.ImportAsync(sourcePath, cancellationToken); _formulaReport = _formulaEngine.Recalculate(workbook); var result = await _repository.SaveAsync(workbook, "Imported XLSX", cancellationToken); workbook.Version = result.Version;
            await RefreshWorkbooksAsync(cancellationToken); Workbook = workbook; _workbookIndex = IndexOfWorkbook(workbook.Id); ResetViewState(); _dirty = false; RenderCurrent(); _route.SetStatus($"Imported {Path.GetFileName(sourcePath)} · {_formulaReport.FormulaCells} formula cell(s), {_formulaReport.Issues.Count} calculation issue(s) · formatting/charts remain explicitly unsupported."); _bus.Fire("Data.Workbook.Imported"); return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t import this workbook: " + ex.Message); return false; }
    }

    internal async Task<bool> ExportToPathAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath); if (Workbook is null) return false;
        if (!await SaveAsync("Save before XLSX export", cancellationToken)) return false;
        try { var path = await _formats.ExportAsync(Workbook, destinationPath, cancellationToken); _route.SetStatus("Exported " + Path.GetFileName(path)); _bus.Fire("Data.Workbook.Exported"); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t export this workbook: " + ex.Message); return false; }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) { await InitializeAsync(); if (!_disposed) _autosaveTimer.Start(); }
    private async void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) { _autosaveTimer.Stop(); if (_dirty && Workbook is not null) await SaveAsync("Autosave on leaving Data"); }
    private async void OnAutosaveTick(object? sender, EventArgs e) { if (!_disposed && _dirty && !_busy && Workbook is not null) await SaveAsync("Autosave"); }
    private async void OnPreviousWorkbookRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveWorkbookAsync(-1), "open the previous workbook");
    private async void OnNextWorkbookRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveWorkbookAsync(1), "open the next workbook");
    private async void OnNewWorkbookRequested(object? sender, EventArgs e) => await RunBusyAsync(() => CreateWorkbookAsync(CancellationToken.None), "create a workbook");
    private async void OnSaveRequested(object? sender, EventArgs e) => await SaveAsync();
    private async void OnImportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickImportAsync, "import an XLSX workbook");
    private async void OnExportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickExportAsync, "export this workbook");
    private void OnAddSheetRequested(object? sender, EventArgs e) => AddSheet();
    private void OnDeleteSheetRequested(object? sender, EventArgs e) => DeleteSheet();
    private void OnAddQueryRequested(object? sender, EventArgs e) => AddQuery();
    private void OnDeleteQueryRequested(object? sender, EventArgs e) => DeleteQuery();
    private void OnBuildSqlRequested(object? sender, EventArgs e) { var query = CurrentQuery; if (query is null) return; query.Sql = query.Visual.BuildSql(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _route.SetStatus("Built SQL from the visual query fields."); }
    private async void OnRunQueryRequested(object? sender, EventArgs e) => await RunBusyAsync(RunCurrentQueryAsync, "run the read-only query preview");

    private DataSheet? CurrentSheet => Workbook is null || Workbook.Sheets.Count == 0 ? null : Workbook.Sheets[Math.Clamp(_sheetIndex, 0, Workbook.Sheets.Count - 1)];
    private DataQuery? CurrentQuery => Workbook is null || Workbook.Queries.Count == 0 ? null : Workbook.Queries[Math.Clamp(_queryIndex, 0, Workbook.Queries.Count - 1)];
    private void OnWorkbookTitleChanged(string value) { if (Workbook is null || Workbook.Title == value) return; Workbook.Title = value; MarkDirty(); }
    private void OnSheetNameChanged(string value)
    {
        var sheet = CurrentSheet; if (Workbook is null || sheet is null || sheet.Name == value) return; var name = string.IsNullOrWhiteSpace(value) ? $"Sheet {_sheetIndex + 1}" : value.Trim();
        if (Workbook.Sheets.Any(other => other.Id != sheet.Id && other.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { _route.SetStatus("Sheet names must be unique for spreadsheet and SQL compatibility."); RenderCurrent(); return; }
        var old = sheet.Name; sheet.Name = name; RewriteSheetReferences(old, name);
        foreach (var query in Workbook.Queries.Where(query => query.Visual.Source.Equals(old, StringComparison.OrdinalIgnoreCase))) { var generated = query.Sql.Equals(query.Visual.BuildSql(), StringComparison.Ordinal); query.Visual.Source = name; if (generated) query.Sql = query.Visual.BuildSql(); }
        RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent();
    }
    private void OnCellValueChanged(string value)
    {
        var sheet = CurrentSheet; if (sheet is null) return; var existing = sheet.GetCell(_selectedRow, _selectedColumn);
        if (!string.IsNullOrWhiteSpace(existing?.Formula)) { RenderCurrent(); return; }
        if (string.Equals(existing?.Value ?? string.Empty, value, StringComparison.Ordinal)) return;
        var validation = ValidateCellValue(sheet, _selectedRow, _selectedColumn, value);
        if (!validation.IsValid) { _route.SetStatus("Validation blocked the edit: " + validation.Message); RenderCurrent(); return; }
        CaptureSpreadsheetUndo();
        sheet.SetCell(_selectedRow, _selectedColumn, value, null, DataCell.InferKind(value)); RecalculateFrom(sheet, _selectedRow, _selectedColumn); _lastQueryResult = null; MarkDirty(); RenderCurrent();
    }
    private void OnCellFormulaChanged(string value)
    {
        var sheet = CurrentSheet; if (sheet is null) return; var existing = sheet.GetCell(_selectedRow, _selectedColumn); var formula = value.Trim(); var retainedValue = existing?.Value ?? string.Empty;
        if (string.Equals(existing?.Formula ?? string.Empty, formula, StringComparison.Ordinal)) return; CaptureSpreadsheetUndo();
        if (string.IsNullOrWhiteSpace(formula)) sheet.SetCell(_selectedRow, _selectedColumn, retainedValue, null, DataCell.InferKind(retainedValue));
        else sheet.SetCell(_selectedRow, _selectedColumn, string.Empty, formula, DataCellKind.Formula);
        var updated = sheet.GetCell(_selectedRow, _selectedColumn); if (updated is not null) { updated.Metadata.Remove("xlsxCachedValue"); updated.Metadata.Remove("formulaCachedFallback"); updated.Metadata.Remove("formulaError"); }
        RecalculateFrom(sheet, _selectedRow, _selectedColumn); _lastQueryResult = null; MarkDirty(); RenderCurrent();
    }
    private void OnQueryNameChanged(string value) { var query = CurrentQuery; if (query is null || query.Name == value) return; query.Name = string.IsNullOrWhiteSpace(value) ? $"Query {_queryIndex + 1}" : value.Trim(); MarkDirty(); }
    private void OnSqlChanged(string value) { var query = CurrentQuery; if (query is null || query.Sql == value) return; query.Sql = value; _lastQueryResult = null; MarkDirty(); }
    private void OnVisualLimitChanged(string value) { UpdateVisual(visual => visual.Limit = int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var limit) && limit > 0 ? limit : null); }
    private void UpdateVisual(Action<DataVisualQuery> update) { var query = CurrentQuery; if (query is null) return; update(query.Visual); _lastQueryResult = null; MarkDirty(); }

    private void OnSheetSelected(int index) { if (Workbook is null) return; _sheetIndex = Math.Clamp(index, 0, Workbook.Sheets.Count - 1); _selectedRow = _selectedColumn = _drawingIndex = _rowOffset = _columnOffset = 0; _lastQueryResult = null; RenderCurrent(); }
    private void OnQuerySelected(int index) { if (Workbook is null) return; _queryIndex = Math.Clamp(index, 0, Workbook.Queries.Count - 1); _lastQueryResult = null; RenderCurrent(); }
    private void OnCellSelected(int row, int column) { _selectedRow = Math.Max(0, row); _selectedColumn = Math.Max(0, column); _route.SetSelectedCell(CurrentSheet?.GetCell(_selectedRow, _selectedColumn), _selectedRow, _selectedColumn); RenderFormulaState(); }
    private void OnGridWindowRequested(int rowDelta, int columnDelta) { _rowOffset = Math.Max(0, _rowOffset + rowDelta); _columnOffset = Math.Max(0, _columnOffset + columnDelta); _selectedRow = Math.Max(_rowOffset, _selectedRow); _selectedColumn = Math.Max(_columnOffset, _selectedColumn); RenderCurrent(); }

    private void AddSheet() { if (Workbook is null) return; var baseName = "Sheet " + (Workbook.Sheets.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); var name = baseName; var suffix = 2; while (Workbook.Sheets.Any(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = baseName + " " + suffix++; Workbook.Sheets.Add(DataSheet.Create(Workbook.Sheets.Count, name)); _sheetIndex = Workbook.Sheets.Count - 1; ResetGridState(); MarkDirty(); RenderCurrent(); _bus.Fire("Data.Sheet.Added"); }
    private void DeleteSheet() { if (Workbook is null) return; var index = Math.Clamp(_sheetIndex, 0, Workbook.Sheets.Count - 1); var removedSheetId = Workbook.Sheets[index].Id; if (Workbook.Sheets.Count == 1) { Workbook.Sheets[0] = DataSheet.Create(0, "Sheet 1"); } else Workbook.Sheets.RemoveAt(index); Workbook.Tables.RemoveAll(value => value.SheetId == removedSheetId); Workbook.Validations.RemoveAll(value => value.SheetId == removedSheetId); Workbook.Charts.RemoveAll(value => value.SheetId == removedSheetId); for (var i = 0; i < Workbook.Sheets.Count; i++) Workbook.Sheets[i].Order = i; _sheetIndex = Math.Clamp(_sheetIndex, 0, Workbook.Sheets.Count - 1); ResetGridState(); RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _bus.Fire("Data.Sheet.Deleted"); }
    private void AddQuery() { if (Workbook is null) return; var query = DataQuery.Create($"Query {Workbook.Queries.Count + 1}"); query.Visual.Source = CurrentSheet?.Name ?? "Sheet 1"; query.Sql = query.Visual.BuildSql(); Workbook.Queries.Add(query); _queryIndex = Workbook.Queries.Count - 1; _lastQueryResult = null; MarkDirty(); RenderCurrent(); _bus.Fire("Data.Query.Added"); }
    private void DeleteQuery() { if (Workbook is null) return; if (Workbook.Queries.Count == 1) Workbook.Queries[0] = DataQuery.Create("Query 1"); else Workbook.Queries.RemoveAt(Math.Clamp(_queryIndex, 0, Workbook.Queries.Count - 1)); for (var i = 0; i < Workbook.Queries.Count; i++) Workbook.Queries[i].Order = i; _queryIndex = Math.Clamp(_queryIndex, 0, Workbook.Queries.Count - 1); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _bus.Fire("Data.Query.Deleted"); }

    private async Task RunCurrentQueryAsync()
    {
        if (Workbook is null || CurrentQuery is null) return; RecalculateWorkbook(); if (_formulaReport.ChangedCells > 0) MarkDirty(); var safety = DataSqlSafety.Analyze(CurrentQuery.Sql); if (!safety.IsReadOnly) { _route.SetStatus(safety.Message); _route.SetQuerySafety(CurrentQuery.Sql); RenderFormulaState(); return; }
        _lastQueryResult = await _queries.ExecuteReadOnlyAsync(Workbook, CurrentQuery.Sql, 200, CancellationToken.None); _route.SetQueryResult(_lastQueryResult); _route.SetStatus($"Query preview returned {_lastQueryResult.Rows.Count} row{(_lastQueryResult.Rows.Count == 1 ? string.Empty : "s")}{(_lastQueryResult.Truncated ? " (truncated)" : string.Empty)}."); _bus.Fire("Data.Query.Previewed");
    }

    private async Task MoveWorkbookAsync(int offset) { if (_workbooks.Count <= 1) return; if (_dirty && !await SaveAsync("Autosave before switching workbook")) return; var next = (_workbookIndex + offset + _workbooks.Count) % _workbooks.Count; await OpenWorkbookAtAsync(next, CancellationToken.None, false); }
    private async Task CreateWorkbookAsync(CancellationToken cancellationToken)
    {
        if (Workbook is not null && _dirty && !await SaveAsync("Autosave before creating workbook", cancellationToken)) return; var workbook = DataWorkbook.Create("Untitled workbook"); var result = await _repository.SaveAsync(workbook, "Workbook created", cancellationToken); workbook.Version = result.Version; await RefreshWorkbooksAsync(cancellationToken); Workbook = workbook; _workbookIndex = IndexOfWorkbook(workbook.Id); ResetViewState(); RecalculateWorkbook(); _dirty = false; RenderCurrent(); _route.SetStatus("Created a new local workbook."); _bus.Fire("Data.Workbook.Created");
    }
    private async Task OpenWorkbookAtAsync(int index, CancellationToken cancellationToken, bool saveBeforeSwitch)
    {
        if (_workbooks.Count == 0) return; if (saveBeforeSwitch && Workbook is not null && _dirty && !await SaveAsync("Autosave before switching workbook", cancellationToken)) return; index = Math.Clamp(index, 0, _workbooks.Count - 1); var loaded = await _repository.LoadAsync(_workbooks[index].Id, cancellationToken); if (loaded is null) { await RefreshWorkbooksAsync(cancellationToken); _route.SetStatus("That local workbook no longer exists."); return; } loaded.Normalize(); Workbook = loaded; _workbookIndex = index; ResetViewState(); RecalculateWorkbook(); _dirty = _formulaReport.ChangedCells > 0; RenderCurrent(); _route.SetStatus(loaded.Recovery.RecoveredFromBackup ? loaded.Recovery.Message : _dirty ? $"Recalculated {_formulaReport.ChangedCells} cached formula value(s) · autosave pending" : "Saved locally · autosave is on"); _bus.Fire("Data.Workbook.Opened");
    }

    private async Task PickImportAsync()
    {
        var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) { _route.SetStatus("Import isn’t available from this platform surface."); return; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import workbook", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }] }); if (files.Count == 0) return; var file = files[0]; var localPath = file.TryGetLocalPath(); if (!string.IsNullOrWhiteSpace(localPath)) { await ImportFromPathAsync(localPath); return; }
        var temporary = Path.Combine(Path.GetTempPath(), $"haven-data-import-{Guid.NewGuid():N}.xlsx"); try { await using var source = await file.OpenReadAsync(); await using (var destination = File.Create(temporary)) await source.CopyToAsync(destination); await ImportFromPathAsync(temporary); } finally { TryDeleteTemporary(temporary); }
    }

    private async Task PickExportAsync()
    {
        if (Workbook is null) return; var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) { _route.SetStatus("Export isn’t available from this platform surface."); return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export workbook", SuggestedFileName = SanitizeFileName(Workbook.Title) + ".xlsx", DefaultExtension = "xlsx", FileTypeChoices = [new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }], ShowOverwritePrompt = true }); if (file is null) return; var localPath = file.TryGetLocalPath(); if (!string.IsNullOrWhiteSpace(localPath)) { await ExportToPathAsync(localPath); return; }
        var temporary = Path.Combine(Path.GetTempPath(), $"haven-data-export-{Guid.NewGuid():N}.xlsx"); try { if (!await ExportToPathAsync(temporary)) return; await using var source = File.OpenRead(temporary); await using var destination = await file.OpenWriteAsync(); destination.SetLength(0); await source.CopyToAsync(destination); await destination.FlushAsync(); _route.SetStatus("Exported " + file.Name); } finally { TryDeleteTemporary(temporary); }
    }

    private void MarkDirty() { if (Workbook is null) return; Workbook.UpdatedAt = DateTimeOffset.UtcNow; _dirty = true; _route.SetStatus("Unsaved changes · autosave is on"); }
    private void RenderCurrent() { if (Workbook is null) return; Workbook.Normalize(); _sheetIndex = Math.Clamp(_sheetIndex, 0, Workbook.Sheets.Count - 1); _queryIndex = Math.Clamp(_queryIndex, 0, Workbook.Queries.Count - 1); var sheet = CurrentSheet; if (sheet is not null) _drawingIndex = Math.Clamp(_drawingIndex, 0, Math.Max(0, sheet.Drawings.Count - 1)); _route.SetWorkbook(Workbook, _workbookIndex, _workbooks.Count, _sheetIndex, _queryIndex, _selectedRow, _selectedColumn, _rowOffset, _columnOffset, _lastQueryResult); if (sheet is not null) _route.SetDrawingState(sheet, _drawingIndex); RenderFormulaState(); SyncSpreadsheetEditingUi(); SyncRecoveredDataUi(); }
    private void ResetViewState() { _sheetIndex = _queryIndex = _selectedRow = _selectedColumn = _drawingIndex = _rowOffset = _columnOffset = 0; _lastQueryResult = null; ResetSpreadsheetHistory(); }
    private void ResetGridState() { _selectedRow = _selectedColumn = _drawingIndex = _rowOffset = _columnOffset = 0; }
    private async Task RefreshWorkbooksAsync(CancellationToken cancellationToken) => _workbooks = await _repository.ListAsync(cancellationToken);
    private int IndexOfWorkbook(Guid id) { for (var index = 0; index < _workbooks.Count; index++) if (_workbooks[index].Id == id) return index; return 0; }
    private async Task RunBusyAsync(Func<Task> action, string description) { if (_busy || _disposed) return; SetBusy(true); try { await action(); } catch (Exception ex) { _route.SetStatus($"Couldn’t {description}: {ex.Message}"); } finally { SetBusy(false); } }
    private void SetBusy(bool busy) { _busy = busy; _route.SetBusy(busy); }
    private static string SanitizeFileName(string title) { var value = string.IsNullOrWhiteSpace(title) ? "Untitled workbook" : title.Trim(); foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_'); return value; }
    private void TryDeleteTemporary(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _route.SetStatus(_route.StatusText.Content + " Temporary-file cleanup failed: " + ex.Message); } }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _autosaveTimer.Stop(); _autosaveTimer.Tick -= OnAutosaveTick; Loaded -= OnLoaded; DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _route.PreviousWorkbookRequested -= OnPreviousWorkbookRequested; _route.NextWorkbookRequested -= OnNextWorkbookRequested; _route.NewWorkbookRequested -= OnNewWorkbookRequested; _route.SaveRequested -= OnSaveRequested; _route.ImportRequested -= OnImportRequested; _route.ExportRequested -= OnExportRequested;
        _route.AddSheetRequested -= OnAddSheetRequested; _route.DeleteSheetRequested -= OnDeleteSheetRequested; _route.AddQueryRequested -= OnAddQueryRequested; _route.DeleteQueryRequested -= OnDeleteQueryRequested; _route.BuildSqlRequested -= OnBuildSqlRequested; _route.RunQueryRequested -= OnRunQueryRequested;
        _route.AddShapeRequested -= OnAddShapeRequested; _route.PreviousDrawingRequested -= OnPreviousDrawingRequested; _route.NextDrawingRequested -= OnNextDrawingRequested; _route.RotateDrawingRequested -= OnRotateDrawingRequested; _route.DeleteDrawingRequested -= OnDeleteDrawingRequested;
        _route.SheetSelected -= OnSheetSelected; _route.QuerySelected -= OnQuerySelected; _route.CellSelected -= OnCellSelected; _route.GridWindowRequested -= OnGridWindowRequested; _route.SpreadsheetUndoRequested -= UndoSpreadsheetEdit; _route.SpreadsheetRedoRequested -= RedoSpreadsheetEdit; _route.WorkbookTitleChanged -= OnWorkbookTitleChanged; _route.SheetNameChanged -= OnSheetNameChanged; _route.CellValueChanged -= OnCellValueChanged; _route.CellFormulaChanged -= OnCellFormulaChanged; _route.QueryNameChanged -= OnQueryNameChanged; _route.SqlChanged -= OnSqlChanged; _route.VisualLimitChanged -= OnVisualLimitChanged; _route.Dispose();
    }
}

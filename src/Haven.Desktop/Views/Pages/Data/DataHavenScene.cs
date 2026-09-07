using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed class DataHavenScene : IDisposable
{
    private const int VisibleRows = 10;
    private const int VisibleColumns = 8;
    private bool _suppressChanges;
    private bool _selectedCellHasFormula;
    private bool _disposed;
    private string _workbookTitle = string.Empty;
    private string _sheetName = string.Empty;
    private string _cellValue = string.Empty;
    private string _cellFormula = string.Empty;
    private string _queryName = string.Empty;
    private string _querySql = string.Empty;
    private string _visualSource = string.Empty;
    private string _visualColumns = string.Empty;
    private string _visualFilter = string.Empty;
    private string _visualGroup = string.Empty;
    private string _visualOrder = string.Empty;
    private string _visualLimit = string.Empty;

    public DataHavenScene()
    {
        Root = new Page { Name = "Data.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 22px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Header = new Container { Name = "Data.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        Header.SetValue(HavenProperties.Row, 0);
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        WorkbookTitleInput = NewInput("Data.Workbook.Title", "Workbook title", "Untitled workbook");
        WorkbookTitleInput.SetValue(HavenProperties.Column, 0);
        WorkbookTitleInput.SetValue(HavenProperties.FontSize, 24d);
        WorkbookTitleInput.SetValue(HavenProperties.FontWeight, 700);
        WorkbookTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(52));
        Header.Add(WorkbookTitleInput);
        PositionText = new HavenText { Name = "Data.Position", Level = TextLevel.Caption };
        PositionText.SetValue(HavenProperties.Column, 1);
        PositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        PositionText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Header.Add(PositionText);
        Root.Add(Header);

        WorkbookToolbar = NewToolbar("Data.Workbook.Toolbar", 1);
        PreviousWorkbookButton = NewButton("Data.Workbook.Previous", "Previous workbook");
        NextWorkbookButton = NewButton("Data.Workbook.Next", "Next workbook");
        NewWorkbookButton = NewButton("Data.Workbook.New", "New workbook");
        SaveButton = NewButton("Data.Workbook.Save", "Save");
        ImportButton = NewButton("Data.Workbook.Import", "Import .xlsx");
        ExportButton = NewButton("Data.Workbook.Export", "Export .xlsx");
        WorkbookToolbar.Add(PreviousWorkbookButton); WorkbookToolbar.Add(NextWorkbookButton); WorkbookToolbar.Add(NewWorkbookButton);
        WorkbookToolbar.Add(SaveButton); WorkbookToolbar.Add(ImportButton); WorkbookToolbar.Add(ExportButton);
        Root.Add(WorkbookToolbar);

        Workspace = new Container { Name = "Data.Workspace", Layout = HavenLayout.Grid, Columns = "230px 1fr", Rows = "1fr" };
        Workspace.SetValue(HavenProperties.Row, 2);
        Workspace.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Workspace.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Explorer = NewCard("Data.Explorer");
        Explorer.SetValue(HavenProperties.Column, 0);
        Explorer.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Workspace.Add(Explorer);

        Editor = NewCard("Data.Editor");
        Editor.SetValue(HavenProperties.Column, 1);
        Editor.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        BuildEditor();
        Workspace.Add(Editor);
        Root.Add(Workspace);

        StatusText = new HavenText("Opening local workbooks…") { Name = "Data.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Row, 3);
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        Landing = NewCard("Data.Landing");
        Landing.SetValue(HavenProperties.Row, 2);
        Landing.SetValue(HavenProperties.Padding, HavenThickness.Parse("30px"));
        Landing.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        var landingTitle = new HavenText("Start with Data") { Name = "Data.Landing.Title", Level = TextLevel.H1 };
        var landingSubtitle = new HavenText("Create a workbook or bring in an existing Excel file. Your workbook opens in the full Data editor when it is ready.") { Name = "Data.Landing.Subtitle", Level = TextLevel.Body };
        landingSubtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        var landingActions = new Container { Name = "Data.Landing.Actions", Layout = HavenLayout.Horizontal };
        landingActions.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        LandingNewWorkbookButton = NewButton("Data.Landing.New", "New workbook");
        LandingNewWorkbookButton.Variant = ButtonVariant.Primary;
        LandingImportButton = NewButton("Data.Landing.Import", "Import .xlsx");
        landingActions.Add(LandingNewWorkbookButton);
        landingActions.Add(LandingImportButton);
        Landing.Add(landingTitle);
        Landing.Add(landingSubtitle);
        Landing.Add(landingActions);
        Root.Add(Landing);

        WorkbookTitleInput.Invalidated += OnWorkbookTitleInvalidated;
        SheetNameInput.Invalidated += OnSheetNameInvalidated;
        CellValueInput.Invalidated += OnCellValueInvalidated;
        CellFormulaInput.Invalidated += OnCellFormulaInvalidated;
        QueryNameInput.Invalidated += OnQueryNameInvalidated;
        SqlInput.Invalidated += OnSqlInvalidated;
        VisualSourceInput.Invalidated += OnVisualSourceInvalidated;
        VisualColumnsInput.Invalidated += OnVisualColumnsInvalidated;
        VisualFilterInput.Invalidated += OnVisualFilterInvalidated;
        VisualGroupInput.Invalidated += OnVisualGroupInvalidated;
        VisualOrderInput.Invalidated += OnVisualOrderInvalidated;
        VisualLimitInput.Invalidated += OnVisualLimitInvalidated;
        SetLandingState(true);

        PreviousWorkbookButton.Invoked += (_, _) => PreviousWorkbookRequested?.Invoke(this, EventArgs.Empty);
        NextWorkbookButton.Invoked += (_, _) => NextWorkbookRequested?.Invoke(this, EventArgs.Empty);
        NewWorkbookButton.Invoked += (_, _) => NewWorkbookRequested?.Invoke(this, EventArgs.Empty);
        SaveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        ImportButton.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        ExportButton.Invoked += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        AddSheetButton.Invoked += (_, _) => AddSheetRequested?.Invoke(this, EventArgs.Empty);
        DeleteSheetButton.Invoked += (_, _) => DeleteSheetRequested?.Invoke(this, EventArgs.Empty);
        PreviousRowsButton.Invoked += (_, _) => GridWindowRequested?.Invoke(-VisibleRows, 0);
        NextRowsButton.Invoked += (_, _) => GridWindowRequested?.Invoke(VisibleRows, 0);
        PreviousColumnsButton.Invoked += (_, _) => GridWindowRequested?.Invoke(0, -VisibleColumns);
        NextColumnsButton.Invoked += (_, _) => GridWindowRequested?.Invoke(0, VisibleColumns);
        AddQueryButton.Invoked += (_, _) => AddQueryRequested?.Invoke(this, EventArgs.Empty);
        DeleteQueryButton.Invoked += (_, _) => DeleteQueryRequested?.Invoke(this, EventArgs.Empty);
        BuildSqlButton.Invoked += (_, _) => BuildSqlRequested?.Invoke(this, EventArgs.Empty);
        RunQueryButton.Invoked += (_, _) => RunQueryRequested?.Invoke(this, EventArgs.Empty);
        AddShapeButton.Invoked += (_, _) => AddShapeRequested?.Invoke(this, EventArgs.Empty);
        PreviousDrawingButton.Invoked += (_, _) => PreviousDrawingRequested?.Invoke(this, EventArgs.Empty);
        NextDrawingButton.Invoked += (_, _) => NextDrawingRequested?.Invoke(this, EventArgs.Empty);
        RotateDrawingButton.Invoked += (_, _) => RotateDrawingRequested?.Invoke(this, EventArgs.Empty);
        DeleteDrawingButton.Invoked += (_, _) => DeleteDrawingRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PreviousWorkbookRequested; public event EventHandler? NextWorkbookRequested; public event EventHandler? NewWorkbookRequested;
    public event EventHandler? SaveRequested; public event EventHandler? ImportRequested; public event EventHandler? ExportRequested;
    public event EventHandler? AddSheetRequested; public event EventHandler? DeleteSheetRequested; public event EventHandler? AddQueryRequested; public event EventHandler? DeleteQueryRequested;
    public event EventHandler? BuildSqlRequested; public event EventHandler? RunQueryRequested;
    public event EventHandler? AddShapeRequested; public event EventHandler? PreviousDrawingRequested; public event EventHandler? NextDrawingRequested; public event EventHandler? RotateDrawingRequested; public event EventHandler? DeleteDrawingRequested;
    public event Action<int>? SheetSelected; public event Action<int>? QuerySelected; public event Action<int, int>? CellSelected; public event Action<int, int>? GridWindowRequested;
    public event Action? SpreadsheetUndoRequested; public event Action? SpreadsheetRedoRequested;
    public event Action<string>? WorkbookTitleChanged; public event Action<string>? SheetNameChanged; public event Action<string>? CellValueChanged; public event Action<string>? CellFormulaChanged;
    public event Action<string>? QueryNameChanged; public event Action<string>? SqlChanged; public event Action<string>? VisualSourceChanged; public event Action<string>? VisualColumnsChanged; public event Action<string>? VisualFilterChanged; public event Action<string>? VisualGroupChanged; public event Action<string>? VisualOrderChanged; public event Action<string>? VisualLimitChanged;

    public Page Root { get; } public Container Header { get; } public Container WorkbookToolbar { get; } public Container Workspace { get; } public Container Explorer { get; } public Container Editor { get; }
    public Container Landing { get; } public HavenButton LandingNewWorkbookButton { get; } public HavenButton LandingImportButton { get; }
    public Input WorkbookTitleInput { get; } public HavenText PositionText { get; } public HavenText StatusText { get; }
    public HavenButton PreviousWorkbookButton { get; } public HavenButton NextWorkbookButton { get; } public HavenButton NewWorkbookButton { get; } public HavenButton SaveButton { get; } public HavenButton ImportButton { get; } public HavenButton ExportButton { get; }
    public Input SheetNameInput { get; private set; } = null!; public Container GridHost { get; private set; } = null!; public HavenText GridWindowText { get; private set; } = null!;
    public HavenText DrawingSummaryText { get; private set; } = null!; public HavenButton AddShapeButton { get; private set; } = null!; public HavenButton PreviousDrawingButton { get; private set; } = null!; public HavenButton NextDrawingButton { get; private set; } = null!; public HavenButton RotateDrawingButton { get; private set; } = null!; public HavenButton DeleteDrawingButton { get; private set; } = null!;
    public HavenButton AddSheetButton { get; private set; } = null!; public HavenButton DeleteSheetButton { get; private set; } = null!; public HavenButton PreviousRowsButton { get; private set; } = null!; public HavenButton NextRowsButton { get; private set; } = null!; public HavenButton PreviousColumnsButton { get; private set; } = null!; public HavenButton NextColumnsButton { get; private set; } = null!;
    public HavenText SelectedCellText { get; private set; } = null!; public HavenText FormulaStatusText { get; private set; } = null!; public Input CellValueInput { get; private set; } = null!; public Input CellFormulaInput { get; private set; } = null!;
    public Container QueryTabs { get; private set; } = null!; public HavenButton AddQueryButton { get; private set; } = null!; public HavenButton DeleteQueryButton { get; private set; } = null!; public Input QueryNameInput { get; private set; } = null!; public Input SqlInput { get; private set; } = null!;
    public Input VisualSourceInput { get; private set; } = null!; public Input VisualColumnsInput { get; private set; } = null!; public Input VisualFilterInput { get; private set; } = null!; public Input VisualGroupInput { get; private set; } = null!; public Input VisualOrderInput { get; private set; } = null!; public Input VisualLimitInput { get; private set; } = null!;
    public HavenButton BuildSqlButton { get; private set; } = null!; public HavenButton RunQueryButton { get; private set; } = null!; public HavenText SqlSafetyText { get; private set; } = null!; public HavenText ResultsText { get; private set; } = null!;

    public void SetWorkbook(DataWorkbook workbook, int workbookIndex, int workbookCount, int sheetIndex, int queryIndex, int selectedRow, int selectedColumn, int rowOffset, int columnOffset, DataQueryResult? result)
    {
        ArgumentNullException.ThrowIfNull(workbook); workbook.Normalize(); SetLandingState(false);
        sheetIndex = Math.Clamp(sheetIndex, 0, workbook.Sheets.Count - 1); queryIndex = Math.Clamp(queryIndex, 0, workbook.Queries.Count - 1);
        var sheet = workbook.Sheets[sheetIndex]; var query = workbook.Queries[queryIndex]; var cell = sheet.GetCell(selectedRow, selectedColumn);
        _suppressChanges = true;
        try
        {
            _workbookTitle = workbook.Title; _sheetName = sheet.Name; _cellValue = cell?.Value ?? string.Empty; _cellFormula = cell?.Formula ?? string.Empty; _selectedCellHasFormula = !string.IsNullOrWhiteSpace(_cellFormula);
            _queryName = query.Name; _querySql = query.Sql; _visualSource = query.Visual.Source; _visualColumns = query.Visual.Columns; _visualFilter = query.Visual.Filter; _visualGroup = query.Visual.GroupBy; _visualOrder = query.Visual.OrderBy; _visualLimit = query.Visual.Limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            WorkbookTitleInput.Text = _workbookTitle; SheetNameInput.Text = _sheetName; CellValueInput.Text = _cellValue; CellFormulaInput.Text = _cellFormula; CellValueInput.SetValue(HavenProperties.Enabled, !_selectedCellHasFormula); QueryNameInput.Text = _queryName; SqlInput.Text = _querySql;
            VisualSourceInput.Text = _visualSource; VisualColumnsInput.Text = _visualColumns; VisualFilterInput.Text = _visualFilter; VisualGroupInput.Text = _visualGroup; VisualOrderInput.Text = _visualOrder; VisualLimitInput.Text = _visualLimit;
            PositionText.Content = $"Workbook {workbookIndex + 1} of {Math.Max(workbookCount, 1)} · {workbook.Sheets.Count} sheet{(workbook.Sheets.Count == 1 ? string.Empty : "s")} · {workbook.Queries.Count} quer{(workbook.Queries.Count == 1 ? "y" : "ies")} · v{workbook.Version}";
            SelectedCellText.Content = $"Selected cell · {ColumnName(selectedColumn)}{selectedRow + 1}";
            RebuildExplorer(workbook, sheetIndex); RebuildGrid(sheet, selectedRow, selectedColumn, rowOffset, columnOffset); RebuildQueryTabs(workbook, queryIndex); SetQuerySafety(query.Sql); SetQueryResult(result);
        }
        finally { _suppressChanges = false; }
    }

    public void SetLandingState(bool showLanding)
    {
        Landing.SetValue(HavenProperties.Visibility, showLanding ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        var editorVisibility = showLanding ? HavenVisibility.Collapsed : HavenVisibility.Visible;
        Header.SetValue(HavenProperties.Visibility, editorVisibility);
        WorkbookToolbar.SetValue(HavenProperties.Visibility, editorVisibility);
        Workspace.SetValue(HavenProperties.Visibility, editorVisibility);
        StatusText.SetValue(HavenProperties.Visibility, editorVisibility);
    }

    public void SetSelectedCell(DataCell? cell, int row, int column)
    {
        _suppressChanges = true;
        try
        {
            _cellValue = cell?.Value ?? string.Empty;
            _cellFormula = cell?.Formula ?? string.Empty;
            _selectedCellHasFormula = !string.IsNullOrWhiteSpace(_cellFormula);
            CellValueInput.Text = _cellValue;
            CellFormulaInput.Text = _cellFormula;
            CellValueInput.SetValue(HavenProperties.Enabled, !_selectedCellHasFormula);
            SelectedCellText.Content = $"Selected cell · {ColumnName(column)}{row + 1}";
        }
        finally { _suppressChanges = false; }
    }

    public void SetDrawingState(DataSheet sheet, int drawingIndex)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Drawings.Count == 0)
        {
            DrawingSummaryText.Content = "No drawing objects on this sheet. Add a custom shape to create native editable vector geometry.";
            PreviousDrawingButton.SetValue(HavenProperties.Enabled, false); NextDrawingButton.SetValue(HavenProperties.Enabled, false); RotateDrawingButton.SetValue(HavenProperties.Enabled, false); DeleteDrawingButton.SetValue(HavenProperties.Enabled, false);
            return;
        }
        drawingIndex = Math.Clamp(drawingIndex, 0, sheet.Drawings.Count - 1);
        var drawing = sheet.Drawings[drawingIndex]; var vector = drawing.VectorShape;
        var paths = vector?.Paths.Count ?? 0; var nodes = vector?.Paths.Sum(path => path.Subpaths.Sum(subpath => subpath.Nodes.Count)) ?? 0;
        DrawingSummaryText.Content = $"Shape {drawingIndex + 1} of {sheet.Drawings.Count} · {drawing.Name} · {paths} path(s) · {nodes} node(s) · {drawing.Width:0}×{drawing.Height:0} · {drawing.Rotation:0}°{(drawing.Locked ? " · locked" : string.Empty)}";
        PreviousDrawingButton.SetValue(HavenProperties.Enabled, sheet.Drawings.Count > 1); NextDrawingButton.SetValue(HavenProperties.Enabled, sheet.Drawings.Count > 1); RotateDrawingButton.SetValue(HavenProperties.Enabled, !drawing.Locked); DeleteDrawingButton.SetValue(HavenProperties.Enabled, !drawing.Locked);
    }

    public void SetFormulaState(DataFormulaRecalculationReport report, DataCell? selectedCell)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (selectedCell is null || string.IsNullOrWhiteSpace(selectedCell.Formula))
        {
            FormulaStatusText.Content = report.FormulaCells == 0 ? "No formulas in this workbook. Formula cells calculate locally as you edit." : $"Workbook calculation · {report.FormulaCells} formula cell(s) · {report.Issues.Count} issue(s).";
            return;
        }
        if (selectedCell.Metadata.TryGetValue("formulaError", out var error) && !string.IsNullOrWhiteSpace(error))
        {
            FormulaStatusText.Content = selectedCell.Metadata.TryGetValue("formulaCachedFallback", out var fallback) && fallback == "xlsx"
                ? $"Imported cached result · {selectedCell.Value} · Haven cannot calculate this Excel formula yet: {error}"
                : $"Formula issue · {selectedCell.Value}: {error}";
        }
        else
            FormulaStatusText.Content = $"Calculated locally · result {selectedCell.Value}";
    }

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;
    public void SetQuerySafety(string sql)
    {
        var safety = DataSqlSafety.Analyze(sql);
        SqlSafetyText.Content = $"SQL safety · {safety.Risk}: {safety.Message}";
        RunQueryButton.SetValue(HavenProperties.Enabled, safety.IsReadOnly);
    }

    public void SetQueryResult(DataQueryResult? result)
    {
        if (result is null) { ResultsText.Content = "Run a read-only SELECT to preview workbook data. Sheets are exposed as SQL tables with spreadsheet columns A, B, C… and a _row column."; return; }
        var lines = new List<string>();
        if (result.Columns.Count > 0) lines.Add(string.Join(" | ", result.Columns));
        foreach (var row in result.Rows.Take(15)) lines.Add(string.Join(" | ", row.Select(value => Truncate(value, 24))));
        if (result.Rows.Count == 0) lines.Add("(no rows)");
        if (result.Truncated) lines.Add("… result truncated to the preview limit");
        ResultsText.Content = string.Join(Environment.NewLine, lines);
    }

    public void SetBusy(bool busy)
    {
        var enabled = !busy;
        foreach (var button in new[] { PreviousWorkbookButton, NextWorkbookButton, NewWorkbookButton, SaveButton, ImportButton, ExportButton, AddSheetButton, DeleteSheetButton, PreviousRowsButton, NextRowsButton, PreviousColumnsButton, NextColumnsButton, AddShapeButton, PreviousDrawingButton, NextDrawingButton, RotateDrawingButton, DeleteDrawingButton, AddQueryButton, DeleteQueryButton, BuildSqlButton }) button.SetValue(HavenProperties.Enabled, enabled);
        foreach (var input in new[] { WorkbookTitleInput, SheetNameInput, CellFormulaInput, QueryNameInput, SqlInput, VisualSourceInput, VisualColumnsInput, VisualFilterInput, VisualGroupInput, VisualOrderInput, VisualLimitInput }) input.SetValue(HavenProperties.Enabled, enabled); CellValueInput.SetValue(HavenProperties.Enabled, enabled && !_selectedCellHasFormula);
        if (busy) RunQueryButton.SetValue(HavenProperties.Enabled, false); else SetQuerySafety(SqlInput.Text);
    }

    private void BuildEditor()
    {
        var sheetHeader = new Container { Name = "Data.Sheet.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto", Rows = "Auto" }; sheetHeader.SetValue(HavenProperties.Gap, HavenLength.Px(8)); sheetHeader.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SheetNameInput = NewInput("Data.Sheet.Name", "Sheet name", "Sheet 1"); SheetNameInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(220)); SheetNameInput.SetValue(HavenProperties.Column, 0);
        AddSheetButton = NewButton("Data.Sheet.Add", "Add sheet"); AddSheetButton.SetValue(HavenProperties.Column, 1); DeleteSheetButton = NewButton("Data.Sheet.Delete", "Delete sheet"); DeleteSheetButton.SetValue(HavenProperties.Column, 2);
        sheetHeader.Add(SheetNameInput); sheetHeader.Add(AddSheetButton); sheetHeader.Add(DeleteSheetButton); Editor.Add(sheetHeader);

        var gridToolbar = NewToolbar("Data.Grid.Toolbar", 0);
        GridWindowText = Caption("Grid window"); PreviousRowsButton = NewButton("Data.Grid.Rows.Previous", "Rows ↑"); NextRowsButton = NewButton("Data.Grid.Rows.Next", "Rows ↓"); PreviousColumnsButton = NewButton("Data.Grid.Columns.Previous", "Columns ←"); NextColumnsButton = NewButton("Data.Grid.Columns.Next", "Columns →");
        gridToolbar.Add(GridWindowText); gridToolbar.Add(PreviousRowsButton); gridToolbar.Add(NextRowsButton); gridToolbar.Add(PreviousColumnsButton); gridToolbar.Add(NextColumnsButton); Editor.Add(gridToolbar);

        GridHost = new Container { Name = "Data.Grid", Layout = HavenLayout.Grid }; GridHost.SetValue(HavenProperties.Gap, HavenLength.Px(4)); GridHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(390)); GridHost.SetValue(HavenProperties.Overflow, HavenOverflow.Clip); Editor.Add(GridHost);

        var drawings = NewCard("Data.Drawings"); drawings.Add(new HavenText("Sheet drawings") { Level = TextLevel.H2 }); DrawingSummaryText = Caption("No drawing objects on this sheet."); DrawingSummaryText.Name = "Data.Drawings.Summary"; drawings.Add(DrawingSummaryText);
        var drawingActions = NewToolbar("Data.Drawings.Actions", 0); AddShapeButton = NewButton("Data.Drawings.AddShape", "Add custom shape"); PreviousDrawingButton = NewButton("Data.Drawings.Previous", "Previous shape"); NextDrawingButton = NewButton("Data.Drawings.Next", "Next shape"); RotateDrawingButton = NewButton("Data.Drawings.Rotate", "Rotate +15°"); DeleteDrawingButton = NewButton("Data.Drawings.Delete", "Delete shape"); DeleteDrawingButton.Variant = ButtonVariant.Danger; foreach (var button in new[] { AddShapeButton, PreviousDrawingButton, NextDrawingButton, RotateDrawingButton, DeleteDrawingButton }) drawingActions.Add(button); drawings.Add(drawingActions); Editor.Add(drawings);

        var cellEditor = NewCard("Data.Cell.Editor"); SelectedCellText = new HavenText { Name = "Data.Cell.Selected", Level = TextLevel.Caption }; cellEditor.Add(SelectedCellText);
        cellEditor.Add(Caption("Cell value")); CellValueInput = NewInput("Data.Cell.Value", "Selected cell value", "Value"); cellEditor.Add(CellValueInput);
        cellEditor.Add(Caption("Formula")); CellFormulaInput = NewInput("Data.Cell.Formula", "Selected cell formula", "=SUM(A1:A5)"); CellFormulaInput.SetValue(HavenProperties.FontFamily, "Code"); cellEditor.Add(CellFormulaInput);
        FormulaStatusText = Caption("No formulas in this workbook. Formula cells calculate locally as you edit."); FormulaStatusText.Name = "Data.Cell.FormulaStatus"; FormulaStatusText.Accessibility.Description = "Live calculation status for the selected spreadsheet formula."; cellEditor.Add(FormulaStatusText); Editor.Add(cellEditor);

        var separator = new Separator { Name = "Data.Query.Separator" }; Editor.Add(separator);
        Editor.Add(new HavenText("Visual SQL") { Name = "Data.Query.Heading", Level = TextLevel.H2 });
        QueryTabs = NewToolbar("Data.Query.Tabs", 0); AddQueryButton = NewButton("Data.Query.Add", "Add query"); DeleteQueryButton = NewButton("Data.Query.Delete", "Delete query"); QueryTabs.Add(AddQueryButton); QueryTabs.Add(DeleteQueryButton); Editor.Add(QueryTabs);
        QueryNameInput = NewInput("Data.Query.Name", "Query name", "Query 1"); Editor.Add(QueryNameInput);

        var visual = NewCard("Data.Query.VisualBuilder"); visual.Add(Caption("Build SELECT visually"));
        VisualSourceInput = NewInput("Data.Query.Visual.Source", "Source table or sheet", "Sheet 1"); VisualColumnsInput = NewInput("Data.Query.Visual.Columns", "Columns", "*"); VisualFilterInput = NewInput("Data.Query.Visual.Filter", "WHERE expression", "A = 'example'"); VisualGroupInput = NewInput("Data.Query.Visual.Group", "GROUP BY expression", "A"); VisualOrderInput = NewInput("Data.Query.Visual.Order", "ORDER BY expression", "_row DESC"); VisualLimitInput = NewInput("Data.Query.Visual.Limit", "Row limit", "100");
        foreach (var input in new[] { VisualSourceInput, VisualColumnsInput, VisualFilterInput, VisualGroupInput, VisualOrderInput, VisualLimitInput }) visual.Add(input);
        BuildSqlButton = NewButton("Data.Query.BuildSql", "Build SQL"); visual.Add(BuildSqlButton); Editor.Add(visual);

        Editor.Add(Caption("SQL editor")); SqlInput = NewInput("Data.Query.Sql", "SQL query", "SELECT * FROM \"Sheet 1\";"); SqlInput.Multiline = true; SqlInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(150)); SqlInput.SetValue(HavenProperties.FontFamily, "Code"); Editor.Add(SqlInput);
        SqlSafetyText = new HavenText { Name = "Data.Query.Safety", Level = TextLevel.Caption }; SqlSafetyText.SetValue(HavenProperties.Foreground, "TextSecondary"); Editor.Add(SqlSafetyText);
        RunQueryButton = NewButton("Data.Query.Run", "Run read-only preview"); RunQueryButton.Variant = ButtonVariant.Primary; Editor.Add(RunQueryButton);
        Editor.Add(Caption("Results")); ResultsText = new HavenText { Name = "Data.Query.Results", Level = TextLevel.Paragraph }; ResultsText.SetValue(HavenProperties.FontFamily, "Code"); ResultsText.SetValue(HavenProperties.MinHeight, HavenLength.Px(120)); Editor.Add(ResultsText);
    }

    private void RebuildExplorer(DataWorkbook workbook, int selectedSheet)
    {
        Explorer.Children.ToList().ForEach(child => child.Parent?.Remove(child)); Explorer.Add(Caption("TABLES / SHEETS"));
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            var captured = index; var sheet = workbook.Sheets[index]; var button = NewButton($"Data.Explorer.Sheet.{sheet.Id:N}", $"{sheet.Name} · {sheet.Cells.Count} cells"); button.Variant = index == selectedSheet ? ButtonVariant.Primary : ButtonVariant.Tertiary; button.Invoked += (_, _) => SheetSelected?.Invoke(captured); Explorer.Add(button);
        }
        Explorer.Add(Caption("FIELDS"));
        var maxColumn = Math.Min(15, workbook.Sheets[selectedSheet].Cells.Count == 0 ? 7 : workbook.Sheets[selectedSheet].Cells.Max(cell => cell.Column));
        Explorer.Add(new HavenText(string.Join("  ·  ", Enumerable.Range(0, maxColumn + 1).Select(ColumnName))) { Name = "Data.Explorer.Fields", Level = TextLevel.Caption });
        if (workbook.Schema.Tables.Count > 0)
        {
            Explorer.Add(Caption("IMPORTED SCHEMA"));
            foreach (var table in workbook.Schema.Tables.Take(20)) Explorer.Add(new HavenText($"{table.Name} · {table.Columns.Count} fields") { Level = TextLevel.Caption });
        }
    }

    private void RebuildGrid(DataSheet sheet, int selectedRow, int selectedColumn, int rowOffset, int columnOffset)
    {
        if (RebuildRetainedSpreadsheet()) return;
        bool RebuildRetainedSpreadsheet()
        {
            var spreadsheet = GridHost.Children.OfType<DataSpreadsheetSurface>().FirstOrDefault();
            if (spreadsheet is null)
            {
                spreadsheet = new DataSpreadsheetSurface();
                spreadsheet.SelectionChanged += (row, column) => CellSelected?.Invoke(row, column);
                spreadsheet.CellCommitted += (row, column, text) =>
                {
                    CellSelected?.Invoke(row, column);
                    if (text.StartsWith("=", StringComparison.Ordinal)) { CellFormulaChanged?.Invoke(text); return; }
                    CellFormulaChanged?.Invoke(string.Empty); CellValueChanged?.Invoke(text);
                };
                spreadsheet.UndoRequested += () => SpreadsheetUndoRequested?.Invoke(); spreadsheet.RedoRequested += () => SpreadsheetRedoRequested?.Invoke();
                spreadsheet.ViewportChanged += () => GridWindowText.Content = spreadsheet.ViewportSummary;
            }
            GridHost.Children.ToList().ForEach(child => child.Parent?.Remove(child)); GridHost.Columns = "1fr"; GridHost.Rows = "1fr"; GridHost.SetValue(HavenProperties.Gap, HavenLength.Px(0)); GridHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(560));
            PreviousRowsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); NextRowsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); PreviousColumnsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); NextColumnsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            spreadsheet.SetSheet(sheet, selectedRow, selectedColumn); GridHost.Add(spreadsheet); GridWindowText.Content = spreadsheet.ViewportSummary + " · scroll continuously · type to edit";
            if (GridWindowText.Parent is Container toolbar && !toolbar.Children.Any(child => child.Name == "Data.Grid.Zoom.Out"))
            {
                var zoomOut = NewButton("Data.Grid.Zoom.Out", "Zoom −"); var zoomReset = NewButton("Data.Grid.Zoom.Reset", "100%"); var zoomIn = NewButton("Data.Grid.Zoom.In", "Zoom +");
                zoomOut.Invoked += (_, _) => spreadsheet.SetZoom(spreadsheet.Zoom - .1); zoomReset.Invoked += (_, _) => spreadsheet.SetZoom(1); zoomIn.Invoked += (_, _) => spreadsheet.SetZoom(spreadsheet.Zoom + .1); toolbar.Add(zoomOut); toolbar.Add(zoomReset); toolbar.Add(zoomIn);
            }
            return true;
        }
        rowOffset = Math.Max(0, rowOffset); columnOffset = Math.Max(0, columnOffset); GridHost.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        GridHost.Columns = "48px 1fr 1fr 1fr 1fr 1fr 1fr 1fr 1fr"; GridHost.Rows = string.Join(' ', Enumerable.Repeat("Auto", VisibleRows + 1));
        GridWindowText.Content = $"{ColumnName(columnOffset)}{rowOffset + 1}:{ColumnName(columnOffset + VisibleColumns - 1)}{rowOffset + VisibleRows}";
        for (var column = 0; column < VisibleColumns; column++)
        {
            var label = new HavenText(ColumnName(columnOffset + column)) { Level = TextLevel.Caption }; label.SetValue(HavenProperties.Row, 0); label.SetValue(HavenProperties.Column, column + 1); GridHost.Add(label);
        }
        for (var row = 0; row < VisibleRows; row++)
        {
            var actualRow = rowOffset + row; var rowLabel = new HavenText((actualRow + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) { Level = TextLevel.Caption }; rowLabel.SetValue(HavenProperties.Row, row + 1); rowLabel.SetValue(HavenProperties.Column, 0); GridHost.Add(rowLabel);
            for (var column = 0; column < VisibleColumns; column++)
            {
                var actualColumn = columnOffset + column; var cell = sheet.GetCell(actualRow, actualColumn); var display = cell?.Value ?? string.Empty; var capturedRow = actualRow; var capturedColumn = actualColumn;
                var button = NewButton($"Data.Cell.{ColumnName(actualColumn)}{actualRow + 1}", Truncate(display, 18)); button.Variant = actualRow == selectedRow && actualColumn == selectedColumn ? ButtonVariant.Primary : ButtonVariant.Tertiary; button.Accessibility.AccessibleName = $"Cell {ColumnName(actualColumn)}{actualRow + 1}, {(string.IsNullOrEmpty(display) ? "empty" : display)}{(!string.IsNullOrWhiteSpace(cell?.Formula) ? $", formula {cell!.Formula}" : string.Empty)}"; button.SetValue(HavenProperties.Row, row + 1); button.SetValue(HavenProperties.Column, column + 1); button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36)); button.Invoked += (_, _) => CellSelected?.Invoke(capturedRow, capturedColumn); GridHost.Add(button);
            }
        }
    }

    private void RebuildQueryTabs(DataWorkbook workbook, int selectedQuery)
    {
        QueryTabs.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        for (var index = 0; index < workbook.Queries.Count; index++) { var captured = index; var query = workbook.Queries[index]; var button = NewButton($"Data.Query.Tab.{query.Id:N}", query.Name); button.Variant = index == selectedQuery ? ButtonVariant.Primary : ButtonVariant.Tertiary; button.Invoked += (_, _) => QuerySelected?.Invoke(captured); QueryTabs.Add(button); }
        QueryTabs.Add(AddQueryButton); QueryTabs.Add(DeleteQueryButton);
    }

    private void OnWorkbookTitleInvalidated(object? sender, EventArgs e) { if (_suppressChanges || WorkbookTitleInput.Text == _workbookTitle) return; _workbookTitle = WorkbookTitleInput.Text; WorkbookTitleChanged?.Invoke(_workbookTitle); }
    private void OnSheetNameInvalidated(object? sender, EventArgs e) { if (_suppressChanges || SheetNameInput.Text == _sheetName) return; _sheetName = SheetNameInput.Text; SheetNameChanged?.Invoke(_sheetName); }
    private void OnCellValueInvalidated(object? sender, EventArgs e) { if (_suppressChanges || CellValueInput.Text == _cellValue) return; _cellValue = CellValueInput.Text; CellValueChanged?.Invoke(_cellValue); }
    private void OnCellFormulaInvalidated(object? sender, EventArgs e) { if (_suppressChanges || CellFormulaInput.Text == _cellFormula) return; _cellFormula = CellFormulaInput.Text; CellFormulaChanged?.Invoke(_cellFormula); }
    private void OnQueryNameInvalidated(object? sender, EventArgs e) { if (_suppressChanges || QueryNameInput.Text == _queryName) return; _queryName = QueryNameInput.Text; QueryNameChanged?.Invoke(_queryName); }
    private void OnSqlInvalidated(object? sender, EventArgs e) { if (_suppressChanges || SqlInput.Text == _querySql) return; _querySql = SqlInput.Text; SetQuerySafety(_querySql); SqlChanged?.Invoke(_querySql); }
    private void OnVisualSourceInvalidated(object? sender, EventArgs e) { Emit(VisualSourceInput, ref _visualSource, VisualSourceChanged); }
    private void OnVisualColumnsInvalidated(object? sender, EventArgs e) { Emit(VisualColumnsInput, ref _visualColumns, VisualColumnsChanged); }
    private void OnVisualFilterInvalidated(object? sender, EventArgs e) { Emit(VisualFilterInput, ref _visualFilter, VisualFilterChanged); }
    private void OnVisualGroupInvalidated(object? sender, EventArgs e) { Emit(VisualGroupInput, ref _visualGroup, VisualGroupChanged); }
    private void OnVisualOrderInvalidated(object? sender, EventArgs e) { Emit(VisualOrderInput, ref _visualOrder, VisualOrderChanged); }
    private void OnVisualLimitInvalidated(object? sender, EventArgs e) { Emit(VisualLimitInput, ref _visualLimit, VisualLimitChanged); }
    private void Emit(Input input, ref string cache, Action<string>? callback) { if (_suppressChanges || input.Text == cache) return; cache = input.Text; callback?.Invoke(cache); }

    private static Container NewCard(string name) { var card = new Container { Name = name, Layout = HavenLayout.Vertical }; card.SetValue(HavenProperties.Background, "SurfaceRaised"); card.SetValue(HavenProperties.BorderColor, "Border"); card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px")); card.SetValue(HavenProperties.Gap, HavenLength.Px(8)); return card; }
    private static Container NewToolbar(string name, int row) { var toolbar = new Container { Name = name, Layout = HavenLayout.Horizontal }; toolbar.SetValue(HavenProperties.Row, row); toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return toolbar; }
    private static HavenButton NewButton(string name, string label) { var button = new HavenButton { Name = name, Content = string.IsNullOrEmpty(label) ? " " : label, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = string.IsNullOrEmpty(label) ? name : label; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); return button; }
    private static Input NewInput(string name, string accessibleName, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessibleName; input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return input; }
    private static HavenText Caption(string text) { var label = new HavenText(text) { Level = TextLevel.Caption }; label.SetValue(HavenProperties.Foreground, "TextSecondary"); return label; }
    private static string Truncate(string? value, int length) { var text = value ?? string.Empty; return text.Length <= length ? text : text[..Math.Max(1, length - 1)] + "…"; }
    private static string ColumnName(int column) { var value = column + 1; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        WorkbookTitleInput.Invalidated -= OnWorkbookTitleInvalidated; SheetNameInput.Invalidated -= OnSheetNameInvalidated; CellValueInput.Invalidated -= OnCellValueInvalidated; CellFormulaInput.Invalidated -= OnCellFormulaInvalidated; QueryNameInput.Invalidated -= OnQueryNameInvalidated; SqlInput.Invalidated -= OnSqlInvalidated;
        VisualSourceInput.Invalidated -= OnVisualSourceInvalidated; VisualColumnsInput.Invalidated -= OnVisualColumnsInvalidated; VisualFilterInput.Invalidated -= OnVisualFilterInvalidated; VisualGroupInput.Invalidated -= OnVisualGroupInvalidated; VisualOrderInput.Invalidated -= OnVisualOrderInvalidated; VisualLimitInput.Invalidated -= OnVisualLimitInvalidated;
    }
}

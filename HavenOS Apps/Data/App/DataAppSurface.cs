using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Data;

public enum DataAppJourney
{
    Spreadsheet = 0,
    Query = 1
}

public static class DataAppRoutes
{
    public const string Root = "/data";
    public const string Spreadsheet = "/data/spreadsheet";
    public const string Query = "/data/query";
    public const string Database = "/data/database";

    public static DataAppJourney Resolve(string? route)
    {
        var normalized = route?.Trim().TrimEnd('/').ToLowerInvariant();
        return normalized switch
        {
            null or "" or Root or Spreadsheet => DataAppJourney.Spreadsheet,
            Query or Database => DataAppJourney.Query,
            _ => throw new ArgumentException($"Unsupported HavenOS Data route '{route}'.", nameof(route))
        };
    }

    public static string For(DataAppJourney journey) => journey switch
    {
        DataAppJourney.Spreadsheet => Spreadsheet,
        DataAppJourney.Query => Query,
        _ => throw new ArgumentOutOfRangeException(nameof(journey), journey, "Unknown HavenOS Data journey.")
    };
}

public sealed record DataSpreadsheetEditResult(
    DataSaveResult Save,
    DataFormulaRecalculationReport Recalculation);

public sealed class DataAppSurface
{
    public const string AppKey = "data";
    public const string DisplayName = "Data";

    private readonly IDataWorkbookRepository _repository;
    private readonly IDataWorkbookQueryService _queryService;
    private readonly DataFormulaEngine _formulaEngine;

    public DataAppSurface(
        IDataWorkbookRepository repository,
        IDataWorkbookQueryService queryService,
        DataFormulaEngine? formulaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queryService);
        _repository = repository;
        _queryService = queryService;
        _formulaEngine = formulaEngine ?? new DataFormulaEngine();
    }

    public Task<IReadOnlyList<DataWorkbookSummary>> ListWorkbooksAsync(CancellationToken cancellationToken = default) =>
        _repository.ListAsync(cancellationToken);

    public async Task<DataAppSession> CreateWorkbookAsync(
        string? title = null,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journey = DataAppRoutes.Resolve(route);
        var workbook = DataWorkbook.Create(title);
        _ = await _repository.SaveAsync(workbook, "Created from HavenOS Data", cancellationToken).ConfigureAwait(false);
        return CreateSession(workbook, journey);
    }

    public async Task<DataAppSession?> OpenWorkbookAsync(
        Guid workbookId,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journey = DataAppRoutes.Resolve(route);
        var workbook = await _repository.LoadAsync(workbookId, cancellationToken).ConfigureAwait(false);
        if (workbook is null) return null;
        workbook.Normalize();
        return CreateSession(workbook, journey);
    }

    private DataAppSession CreateSession(DataWorkbook workbook, DataAppJourney journey) =>
        new(workbook, _repository, _queryService, _formulaEngine, journey);
}

public sealed class DataAppSession
{
    private readonly IDataWorkbookRepository _repository;
    private readonly IDataWorkbookQueryService _queryService;
    private readonly DataFormulaEngine _formulaEngine;

    internal DataAppSession(
        DataWorkbook workbook,
        IDataWorkbookRepository repository,
        IDataWorkbookQueryService queryService,
        DataFormulaEngine formulaEngine,
        DataAppJourney journey)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(formulaEngine);
        Workbook = workbook;
        _repository = repository;
        _queryService = queryService;
        _formulaEngine = formulaEngine;
        ActiveJourney = journey;
    }

    public DataWorkbook Workbook { get; }
    public DataAppJourney ActiveJourney { get; private set; }
    public string Route => DataAppRoutes.For(ActiveJourney);

    public void Navigate(DataAppJourney journey)
    {
        _ = DataAppRoutes.For(journey);
        ActiveJourney = journey;
    }

    public async Task<DataSpreadsheetEditResult> EditCellAsync(
        Guid sheetId,
        int row,
        int column,
        string? value,
        string? formula = null,
        DataCellKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (row < 0) throw new ArgumentOutOfRangeException(nameof(row));
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));

        var sheet = Workbook.Sheets.FirstOrDefault(candidate => candidate.Id == sheetId)
            ?? throw new KeyNotFoundException($"Workbook does not contain sheet '{sheetId}'.");

        ActiveJourney = DataAppJourney.Spreadsheet;
        sheet.SetCell(row, column, value, formula, kind);
        var recalculation = _formulaEngine.Recalculate(
            Workbook,
            [DataFormulaEngine.Address(sheet, row, column)]);
        var save = await _repository.SaveAsync(
            Workbook,
            "Spreadsheet edit from HavenOS Data",
            cancellationToken).ConfigureAwait(false);
        return new DataSpreadsheetEditResult(save, recalculation);
    }

    public async Task<DataQueryResult> ExecuteQueryAsync(
        Guid queryId,
        string? sql = null,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = Workbook.Queries.FirstOrDefault(candidate => candidate.Id == queryId)
            ?? throw new KeyNotFoundException($"Workbook does not contain query '{queryId}'.");

        ActiveJourney = DataAppJourney.Query;
        var statement = string.IsNullOrWhiteSpace(sql) ? query.Sql : sql.Trim();

        _ = _formulaEngine.Recalculate(Workbook);
        var result = await _queryService.ExecuteReadOnlyAsync(
            Workbook,
            statement,
            maxRows,
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(query.Sql, statement, StringComparison.Ordinal))
        {
            query.Sql = statement;
            _ = await _repository.SaveAsync(
                Workbook,
                "Saved read-only query from HavenOS Data",
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}

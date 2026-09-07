using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Data.Tests;

public sealed class DataAppSurfaceTests
{
    [Fact]
    public async Task Spreadsheet_journey_recalculates_formula_and_saves_through_workbook_repository()
    {
        var repository = new RecordingRepository();
        var surface = CreateSurface(repository);
        var session = await surface.CreateWorkbookAsync("Budget", DataAppRoutes.Spreadsheet);
        var sheet = session.Workbook.Sheets[0];

        _ = await session.EditCellAsync(sheet.Id, 0, 0, "10", kind: DataCellKind.Number);
        var edit = await session.EditCellAsync(sheet.Id, 0, 1, string.Empty, formula: "=A1*2");

        Assert.Equal(DataAppJourney.Spreadsheet, session.ActiveJourney);
        Assert.Equal(DataAppRoutes.Spreadsheet, session.Route);
        Assert.Equal("20", sheet.GetCell(0, 1)!.Value);
        Assert.Empty(edit.Recalculation.Issues);
        Assert.Equal(1, edit.Recalculation.FormulaCells);
        Assert.Equal(3, repository.SaveCount);
        Assert.Equal(session.Workbook.Version, edit.Save.Version);
    }

    [Fact]
    public async Task Query_journey_uses_existing_read_only_sql_engine_over_calculated_sheet_values()
    {
        var repository = new RecordingRepository();
        var surface = CreateSurface(repository);
        var session = await surface.CreateWorkbookAsync("Query workbook", DataAppRoutes.Database);
        var sheet = session.Workbook.Sheets[0];
        var query = session.Workbook.Queries[0];

        _ = await session.EditCellAsync(sheet.Id, 0, 0, "2", kind: DataCellKind.Number);
        _ = await session.EditCellAsync(sheet.Id, 1, 0, "3", kind: DataCellKind.Number);
        _ = await session.EditCellAsync(sheet.Id, 2, 0, string.Empty, formula: "=A1+A2");

        const string sql = "SELECT A FROM \"Sheet 1\" WHERE _row = 3;";
        var result = await session.ExecuteQueryAsync(query.Id, sql, maxRows: 10);

        Assert.Equal(DataAppJourney.Query, session.ActiveJourney);
        Assert.Equal(DataAppRoutes.Query, session.Route);
        Assert.Equal(sql, query.Sql);
        Assert.Equal(new[] { "A" }, result.Columns);
        Assert.Single(result.Rows);
        Assert.Equal("5", Assert.Single(result.Rows[0]));
        Assert.False(result.Truncated);
        Assert.Equal("Local workbook preview", result.SourceDescription);
    }

    [Fact]
    public async Task Query_journey_does_not_persist_mutating_sql_when_engine_rejects_it()
    {
        var repository = new RecordingRepository();
        var surface = CreateSurface(repository);
        var session = await surface.CreateWorkbookAsync("Safe query workbook", DataAppRoutes.Query);
        var query = session.Workbook.Queries[0];
        var originalSql = query.Sql;
        var savesBeforeQuery = repository.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ExecuteQueryAsync(query.Id, "DELETE FROM \"Sheet 1\";", maxRows: 10));

        Assert.Equal(DataAppJourney.Query, session.ActiveJourney);
        Assert.Equal(originalSql, query.Sql);
        Assert.Equal(savesBeforeQuery, repository.SaveCount);
    }

    [Fact]
    public async Task Unsupported_route_is_rejected_before_storage_is_changed()
    {
        var repository = new RecordingRepository();
        var surface = CreateSurface(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            surface.CreateWorkbookAsync("Should not save", "/data/not-a-route"));

        Assert.Equal(0, repository.SaveCount);
    }

    private static DataAppSurface CreateSurface(RecordingRepository repository) =>
        new(repository, new DataWorkbookQueryService(), new DataFormulaEngine(new FixedTimeProvider()));

    private sealed class RecordingRepository : IDataWorkbookRepository
    {
        private readonly Dictionary<Guid, DataWorkbook> _workbooks = [];

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DataWorkbookSummary> summaries = _workbooks.Values
                .Select(workbook => new DataWorkbookSummary(
                    workbook.Id,
                    workbook.Title,
                    workbook.UpdatedAt,
                    workbook.Version,
                    workbook.Sheets.Count,
                    workbook.Queries.Count,
                    workbook.Recovery.RecoveredFromBackup))
                .ToArray();
            return Task.FromResult(summaries);
        }

        public Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _workbooks.TryGetValue(workbookId, out var workbook);
            return Task.FromResult(workbook);
        }

        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            cancellationToken.ThrowIfCancellationRequested();
            workbook.Normalize();
            workbook.Version = checked(workbook.Version + 1);
            workbook.UpdatedAt = DateTimeOffset.UtcNow;
            workbook.Metadata["lastSaveReason"] = reason;
            _workbooks[workbook.Id] = workbook;
            SaveCount++;
            return Task.FromResult(new DataSaveResult(
                workbook.Id,
                workbook.Version,
                workbook.UpdatedAt,
                $"memory://{workbook.Id:D}/current.json",
                $"memory://{workbook.Id:D}/previous.json"));
        }

        public Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _workbooks.Remove(workbookId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 30, 18, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

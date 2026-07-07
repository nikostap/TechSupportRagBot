using System.Data;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class KnowledgeFtsService
{
    private readonly ApplicationDbContext _db;

    public KnowledgeFtsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task UpsertChunkAsync(KnowledgeChunk chunk, CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);

        var data = await _db.KnowledgeChunks
            .AsNoTracking()
            .Include(x => x.KnowledgeDocument)
            .ThenInclude(x => x!.Machine)
            .FirstOrDefaultAsync(x => x.Id == chunk.Id, cancellationToken);

        if (data == null)
        {
            return;
        }

        var document = data.KnowledgeDocument;
        var machine = document?.Machine;
        if (machine == null && data.MachineId.HasValue)
        {
            machine = await _db.Machines
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == data.MachineId.Value, cancellationToken);
        }
        var category = string.IsNullOrWhiteSpace(data.Category) ? document?.Category : data.Category;
        var machineModel = data.MachineModel ?? document?.MachineModel ?? machine?.Model;
        var serialNumber = data.SerialNumber ?? document?.SerialNumber ?? machine?.SerialNumber;
        var documentName = document?.OriginalFileName ?? "ResolvedTicket";

        await EnsureOpenAsync(cancellationToken);
        await DeleteChunkAsync(data.Id, cancellationToken);

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        var searchText = BuildSearchText(data);
        if (IsPostgres())
        {
            command.CommandText = """
                INSERT INTO "KnowledgeChunksFts"("ChunkId", "Text", "Category", "MachineModel", "SerialNumber", "DocumentName", "SearchText")
                VALUES (@chunkId, @text, @category, @machineModel, @serialNumber, @documentName, @searchText)
                ON CONFLICT ("ChunkId") DO UPDATE SET
                    "Text" = EXCLUDED."Text",
                    "Category" = EXCLUDED."Category",
                    "MachineModel" = EXCLUDED."MachineModel",
                    "SerialNumber" = EXCLUDED."SerialNumber",
                    "DocumentName" = EXCLUDED."DocumentName",
                    "SearchText" = EXCLUDED."SearchText"
                """;
            AddParameter(command, "@chunkId", data.Id);
            AddParameter(command, "@text", searchText);
            AddParameter(command, "@category", category);
            AddParameter(command, "@machineModel", machineModel);
            AddParameter(command, "@serialNumber", serialNumber);
            AddParameter(command, "@documentName", documentName);
            AddParameter(command, "@searchText", searchText);
        }
        else
        {
            command.CommandText = """
                INSERT INTO KnowledgeChunksFts(ChunkId, Text, Category, MachineModel, SerialNumber, DocumentName)
                VALUES ($chunkId, $text, $category, $machineModel, $serialNumber, $documentName)
                """;
            AddParameter(command, "$chunkId", data.Id);
            AddParameter(command, "$text", searchText);
            AddParameter(command, "$category", category);
            AddParameter(command, "$machineModel", machineModel);
            AddParameter(command, "$serialNumber", serialNumber);
            AddParameter(command, "$documentName", documentName);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);
        await EnsureOpenAsync(cancellationToken);

        var connection = _db.Database.GetDbConnection();
        await using (var clearCommand = connection.CreateCommand())
        {
            clearCommand.CommandText = IsPostgres()
                ? "DELETE FROM \"KnowledgeChunksFts\""
                : "DELETE FROM KnowledgeChunksFts";
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var chunkIds = await _db.KnowledgeChunks
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        // Перестраиваем локальный keyword-индекс из основной таблицы чанков.
        // Это чинит старые документы, которые были загружены до появления FTS,
        // и защищает базу от рассинхронизации после ошибок индексации.
        foreach (var chunkId in chunkIds)
        {
            await UpsertChunkAsync(new KnowledgeChunk { Id = chunkId }, cancellationToken);
        }
    }

    public async Task DeleteChunkAsync(int chunkId, CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);
        await EnsureOpenAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = IsPostgres()
            ? """DELETE FROM "KnowledgeChunksFts" WHERE "ChunkId" = @chunkId"""
            : "DELETE FROM KnowledgeChunksFts WHERE ChunkId = $chunkId";
        AddParameter(command, IsPostgres() ? "@chunkId" : "$chunkId", chunkId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteChunksAsync(IEnumerable<int> chunkIds, CancellationToken cancellationToken = default)
    {
        foreach (var chunkId in chunkIds.Distinct())
        {
            await DeleteChunkAsync(chunkId, cancellationToken);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = IsPostgres()
            ? """
                CREATE TABLE IF NOT EXISTS "KnowledgeChunksFts"(
                    "ChunkId" integer PRIMARY KEY,
                    "Text" text,
                    "Category" text,
                    "MachineModel" text,
                    "SerialNumber" text,
                    "DocumentName" text,
                    "SearchText" text
                );
                CREATE INDEX IF NOT EXISTS "IX_KnowledgeChunksFts_SearchText"
                    ON "KnowledgeChunksFts"
                    USING GIN (to_tsvector('simple', coalesce("SearchText", '')));
                """
            : """
                CREATE VIRTUAL TABLE IF NOT EXISTS KnowledgeChunksFts USING fts5(
                    ChunkId UNINDEXED,
                    Text,
                    Category,
                    MachineModel,
                    SerialNumber,
                    DocumentName,
                    tokenize = 'unicode61'
                )
                """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private bool IsPostgres()
    {
        return _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string BuildSearchText(KnowledgeChunk chunk)
    {
        if (string.Equals(chunk.DocumentType, "QA", StringComparison.OrdinalIgnoreCase))
        {
            // Для QA индексируем только вопрос, альтернативы и метаданные.
            // Сам ответ хранится отдельно в Solution и не должен размывать keyword search.
            return string.Join("\n", new[]
            {
                chunk.FileName,
                chunk.DocumentType,
                chunk.Title,
                chunk.SerialNumber,
                chunk.NodeName,
                chunk.Topic,
                chunk.Tags,
                chunk.NormalizedText,
                chunk.Text
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return string.Join("\n", new[]
        {
            chunk.FileName,
            chunk.DocumentType,
            chunk.Title,
            chunk.SerialNumber,
            chunk.SectionTitle,
            chunk.SubsectionTitle,
            chunk.ErrorCode,
            chunk.ErrorName,
            chunk.Cause,
            chunk.Solution,
            chunk.NodeName,
            chunk.SheetName,
            chunk.RowNumber?.ToString(),
            chunk.ColumnNames,
            chunk.Participants,
            chunk.Topic,
            chunk.SourceChat,
            chunk.Tags,
            chunk.NormalizedText,
            chunk.Text
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}

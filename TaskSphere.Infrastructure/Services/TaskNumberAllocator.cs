using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskSphere.Application.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Services;

public sealed class TaskNumberAllocator : ITaskNumberAllocator
{
    private const string AllocateSql = """
        UPDATE Projects
           SET NextTaskNumber = NextTaskNumber + 1
        OUTPUT DELETED.NextTaskNumber
         WHERE Id = @projectId;
        """;

    private readonly ApplicationDbContext _db;

    public TaskNumberAllocator(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int?> AllocateAsync(int projectId, CancellationToken ct)
    {
        // Raw ADO rather than EF: "UPDATE ... OUTPUT" cannot survive being composed
        // into a subquery, which is what EF may do with FromSql/SqlQuery.
        //
        // NOTE: this autocommits immediately and does NOT flow through
        // IUnitOfWork.SaveChangesAsync. If the caller's insert later fails, the
        // allocated number is lost. Gaps are accepted by design.
        var connection = _db.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = AllocateSql;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@projectId";
        parameter.DbType = DbType.Int32;
        parameter.Value = projectId;
        command.Parameters.Add(parameter);

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var result = await command.ExecuteScalarAsync(ct);

        return result is int number ? number : null;
    }
}

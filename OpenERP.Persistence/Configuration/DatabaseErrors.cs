using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpenERP.Api.Persistence;

internal static class DatabaseErrors
{
    internal static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            or SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE
}

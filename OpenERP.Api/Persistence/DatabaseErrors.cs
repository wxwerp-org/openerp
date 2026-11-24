using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Persistence;

internal static class DatabaseErrors
{
    internal static bool IsUnavailable(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or DbUpdateException or RetryLimitExceededException)
                return true;
        }
        return false;
    }
}

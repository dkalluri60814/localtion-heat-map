using LocationHeatMap.Models;
using SQLite;

namespace LocationHeatMap.Services;

/// <summary>
/// SQLite implementation of <see cref="ILocationRepository"/> built on sqlite-net-pcl.
/// The connection is created lazily and shared for the lifetime of the app.
/// </summary>
public class LocationRepository : ILocationRepository
{
    private const string DatabaseFileName = "locations.db3";

    // Open read/write, create the file if needed, and use shared-cache so the
    // background tracker and the UI can both talk to the same file safely.
    private const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    /// <summary>
    /// Opens the database and creates the table on first use.
    /// The semaphore guarantees that concurrent callers only initialise once.
    /// </summary>
    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
                var connection = new SQLiteAsyncConnection(path, Flags);
                await connection.CreateTableAsync<LocationPoint>();
                _connection = connection;
            }

            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocationPoint>> GetAllAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocationPoint>().OrderBy(p => p.TimestampUtc).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocationPoint>().CountAsync();
    }

    /// <inheritdoc />
    public async Task InsertAsync(LocationPoint point)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(point);
    }

    /// <inheritdoc />
    public async Task InsertAllAsync(IEnumerable<LocationPoint> points)
    {
        var db = await GetConnectionAsync();
        // runInTransaction = true: thousands of rows are written in one go instead of one by one.
        await db.InsertAllAsync(points, runInTransaction: true);
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<LocationPoint>();
    }
}

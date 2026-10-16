using LocationHeatMap.Models;

namespace LocationHeatMap.Services;

/// <summary>
/// Abstraction over the persistence layer so view models do not depend on SQLite directly
/// (keeps them testable and the storage technology replaceable).
/// </summary>
public interface ILocationRepository
{
    /// <summary>Returns every stored location, oldest first.</summary>
    Task<IReadOnlyList<LocationPoint>> GetAllAsync();

    /// <summary>Returns the number of stored locations.</summary>
    Task<int> CountAsync();

    /// <summary>Stores a single location.</summary>
    Task InsertAsync(LocationPoint point);

    /// <summary>Stores many locations in a single transaction (fast bulk insert).</summary>
    Task InsertAllAsync(IEnumerable<LocationPoint> points);

    /// <summary>Deletes every stored location.</summary>
    Task ClearAsync();
}

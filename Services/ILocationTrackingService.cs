using LocationHeatMap.Models;

namespace LocationHeatMap.Services;

/// <summary>
/// Periodically samples the device position and persists it through <see cref="ILocationRepository"/>.
/// </summary>
public interface ILocationTrackingService
{
    /// <summary>Whether the tracker loop is currently running.</summary>
    bool IsTracking { get; }

    /// <summary>Raised every time a new location has been saved.</summary>
    event EventHandler<LocationPoint>? LocationRecorded;

    /// <summary>
    /// Requests the location permission (if necessary) and starts sampling.
    /// Returns <c>false</c> when the user refused the permission.
    /// </summary>
    Task<bool> StartAsync();

    /// <summary>Stops sampling.</summary>
    void Stop();
}

using LocationHeatMap.Models;

namespace LocationHeatMap.Services;

/// <summary>
/// Foreground location tracker. While running, it asks the device for a fix every
/// <see cref="SamplingInterval"/> and stores it when the user has moved far enough
/// from the last stored point (this avoids flooding the database while standing still).
/// </summary>
public sealed class LocationTrackingService : ILocationTrackingService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(5);

    /// <summary>Minimum distance (km) between two stored points - 5 metres.</summary>
    private const double MinimumDistanceKm = 0.005;

    private readonly ILocationRepository _repository;
    private CancellationTokenSource? _cts;
    private Location? _lastStored;

    public LocationTrackingService(ILocationRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public bool IsTracking => _cts is not null;

    /// <inheritdoc />
    public event EventHandler<LocationPoint>? LocationRecorded;

    /// <inheritdoc />
    public async Task<bool> StartAsync()
    {
        if (IsTracking)
        {
            return true;
        }

        // Runtime permissions must be requested from the UI thread.
        var status = await MainThread.InvokeOnMainThreadAsync(
            () => Permissions.RequestAsync<Permissions.LocationWhenInUse>());

        if (status != PermissionStatus.Granted)
        {
            return false;
        }

        _cts = new CancellationTokenSource();
        // Fire-and-forget: the loop owns its own lifetime through the cancellation token.
        _ = RunAsync(_cts.Token);
        return true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _lastStored = null;
    }

    /// <summary>The sampling loop; ends when <paramref name="token"/> is cancelled.</summary>
    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(SamplingInterval);
        try
        {
            do
            {
                await SampleOnceAsync(token);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown - nothing to do.
        }
    }

    /// <summary>Takes a single reading and stores it if it passes the distance filter.</summary>
    private async Task SampleOnceAsync(CancellationToken token)
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request, token);

            if (location is null || !IsFarEnoughFromLast(location))
            {
                return;
            }

            var point = new LocationPoint
            {
                TimestampUtc = DateTime.UtcNow,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                AccuracyMeters = location.Accuracy,
                IsSample = false
            };

            await _repository.InsertAsync(point);
            _lastStored = location;
            LocationRecorded?.Invoke(this, point);
        }
        catch (OperationCanceledException)
        {
            throw; // let RunAsync handle shutdown
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException
                                       or FeatureNotEnabledException
                                       or PermissionException)
        {
            // GPS switched off or permission revoked at runtime: skip this tick and try again later.
            System.Diagnostics.Debug.WriteLine($"Location unavailable: {ex.Message}");
        }
    }

    private bool IsFarEnoughFromLast(Location current) =>
        _lastStored is null
        || Location.CalculateDistance(_lastStored, current, DistanceUnits.Kilometers) >= MinimumDistanceKm;
}

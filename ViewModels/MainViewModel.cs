using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocationHeatMap.Models;
using LocationHeatMap.Services;

namespace LocationHeatMap.ViewModels;

/// <summary>
/// View model of the main (and only) page. It owns the list of stored locations, starts/stops the
/// tracker and exposes the commands bound to the toolbar buttons.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ILocationRepository _repository;
    private readonly ILocationTrackingService _tracker;

    public MainViewModel(ILocationRepository repository, ILocationTrackingService tracker)
    {
        _repository = repository;
        _tracker = tracker;
        _tracker.LocationRecorded += OnLocationRecorded;
    }

    /// <summary>Raised after the whole data set was (re)loaded, so the page can zoom the map to it.</summary>
    public event EventHandler? DataReloaded;

    /// <summary>All stored locations - bound to <c>HeatMap.HeatPoints</c>.</summary>
    [ObservableProperty]
    private IReadOnlyList<LocationPoint> _points = Array.Empty<LocationPoint>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackingButtonText))]
    private bool _isTracking;

    [ObservableProperty]
    private bool _isHeatVisible = true;

    [ObservableProperty]
    private bool _arePointsVisible;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Loading...";

    /// <summary>Caption of the start/stop button.</summary>
    public string TrackingButtonText => IsTracking ? "Stop" : "Track me";

    // ---- lifecycle ---------------------------------------------------------------------------

    /// <summary>
    /// Called once when the page appears: loads stored data and, on the very first launch,
    /// fills the database with the Paris sample data so there is something to look at.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (await _repository.CountAsync() == 0)
        {
            await SampleDataSeeder.SeedAsync(_repository);
        }

        await ReloadAsync();
    }

    // ---- commands ----------------------------------------------------------------------------

    /// <summary>Replaces everything in the database with the generated Paris demo data.</summary>
    [RelayCommand]
    private async Task LoadSampleDataAsync()
    {
        await RunBusyAsync(async () =>
        {
            await SampleDataSeeder.SeedAsync(_repository);
            await ReloadAsync();
        });
    }

    /// <summary>Starts or stops recording the real device location.</summary>
    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsTracking)
        {
            _tracker.Stop();
            IsTracking = false;
            UpdateStatus();
            return;
        }

        IsTracking = await _tracker.StartAsync();
        StatusText = IsTracking
            ? "Tracking your location..."
            : "Location permission denied - enable it in the system settings.";
    }

    /// <summary>Deletes all stored locations.</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _repository.ClearAsync();
            await ReloadAsync();
        });
    }

    [RelayCommand]
    private void ToggleHeat() => IsHeatVisible = !IsHeatVisible;

    [RelayCommand]
    private void TogglePoints() => ArePointsVisible = !ArePointsVisible;

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>Reads the database again and tells the page to zoom to the data.</summary>
    private async Task ReloadAsync()
    {
        Points = await _repository.GetAllAsync();
        UpdateStatus();
        DataReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="work"/> while <see cref="IsBusy"/> is set (disables the buttons).</summary>
    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await work();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The tracker raises its event on a background thread, so hop to the UI thread first.</summary>
    private void OnLocationRecorded(object? sender, LocationPoint point) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // A new list instance is required so the bound HeatMap notices the change.
            Points = [.. Points, point];
            UpdateStatus();
        });

    private void UpdateStatus()
    {
        StatusText = Points.Count == 0
            ? "No locations yet - tap \"Track me\" or load the sample data."
            : IsTracking
                ? $"Tracking - {Points.Count:N0} locations stored"
                : $"{Points.Count:N0} locations stored";
    }
}

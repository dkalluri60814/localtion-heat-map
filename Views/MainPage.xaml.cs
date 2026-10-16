using LocationHeatMap.Services;
using LocationHeatMap.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;

namespace LocationHeatMap.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private bool _isInitialized;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        // Start with a view of central Paris until the stored data is loaded.
        HeatMapView.MoveToRegion(MapSpan.FromCenterAndRadius(
            new Location(48.8566, 2.3522), Distance.FromKilometers(5)));

        AddLandmarkPins();

        // Zoom to the data whenever it was (re)loaded - but not for every single tracked point,
        // otherwise the camera would jump while the user is panning around.
        _viewModel.DataReloaded += (_, _) => HeatMapView.FitToPoints();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // OnAppearing can fire again (e.g. after returning from another app); load only once.
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await _viewModel.InitializeAsync();
    }

    /// <summary>Adds a pin for every landmark of the sample city.</summary>
    private void AddLandmarkPins()
    {
        foreach (var landmark in SampleDataSeeder.Landmarks)
        {
            HeatMapView.Pins.Add(new Pin
            {
                Label = landmark.Name,
                Address = landmark.Description,
                Type = PinType.Place,
                Location = new Location(landmark.Latitude, landmark.Longitude)
            });
        }
    }
}

using LocationHeatMap.Views;

namespace LocationHeatMap;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    // MainPage is resolved by the dependency-injection container and handed in here.
    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_mainPage);
}

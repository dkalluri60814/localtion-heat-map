using LocationHeatMap.Models;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map; // disambiguates from Microsoft.Maui.ApplicationModel.Map

namespace LocationHeatMap.Controls;

/// <summary>
/// Extension of the .NET MAUI <see cref="Map"/> control that visualises a collection of
/// <see cref="LocationPoint"/>s as a heat map.
/// <para>
/// How it works (no platform-specific code needed, so it runs on Android, iOS and Mac Catalyst):
/// <list type="number">
///   <item>The points are binned into a square grid. The cell size follows the zoom level, so the
///         picture stays readable whether you look at one street or the whole city.</item>
///         <item>The number of points per cell (its "density") is normalised to 0..1 on a log scale.</item>
///   <item>Each cell is drawn as a few concentric, translucent <see cref="Circle"/> map elements
///         whose colour comes from a blue -> green -> yellow -> red gradient. Overlapping circles
///         add up, which produces the typical soft "glow" of a heat map.</item>
/// </list>
/// </para>
/// <remarks>The control owns <see cref="Map.MapElements"/>; anything added there manually is replaced.</remarks>
/// </summary>
public class HeatMap : Map
{
    // ---- tuning constants ------------------------------------------------------------------

    private const double MetersPerDegree = 111_320.0;
    private const double TargetCellsAcrossScreen = 34;
    private const double MinCellMeters = 12;
    private const double MaxCellMeters = 600;
    private const int MaxCells = 700;           // upper bound for the number of drawn cells
    private const int MaxRawPoints = 1500;      // upper bound for the number of drawn raw dots
    private const double RerenderZoomRatio = 1.2; // re-bin only if the ideal cell size changed by >20 %

    /// <summary>Concentric layers drawn for every cell: (radius relative to cell, base opacity).</summary>
    private static readonly (double Radius, double Alpha)[] Layers =
    {
        (2.1, 0.10), // wide, faint halo
        (1.4, 0.17), // body
        (0.8, 0.28), // bright core
    };

    // ---- bindable properties -----------------------------------------------------------------

    /// <summary>The points that are turned into the heat map.</summary>
    public static readonly BindableProperty HeatPointsProperty = BindableProperty.Create(
        nameof(HeatPoints), typeof(IReadOnlyList<LocationPoint>), typeof(HeatMap), null,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(50));

    /// <summary>Show / hide the heat layer.</summary>
    public static readonly BindableProperty IsHeatVisibleProperty = BindableProperty.Create(
        nameof(IsHeatVisible), typeof(bool), typeof(HeatMap), true,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(0));

    /// <summary>Show / hide the raw recorded positions as small dots.</summary>
    public static readonly BindableProperty ArePointsVisibleProperty = BindableProperty.Create(
        nameof(ArePointsVisible), typeof(bool), typeof(HeatMap), false,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(0));

    public IReadOnlyList<LocationPoint>? HeatPoints
    {
        get => (IReadOnlyList<LocationPoint>?)GetValue(HeatPointsProperty);
        set => SetValue(HeatPointsProperty, value);
    }

    public bool IsHeatVisible
    {
        get => (bool)GetValue(IsHeatVisibleProperty);
        set => SetValue(IsHeatVisibleProperty, value);
    }

    public bool ArePointsVisible
    {
        get => (bool)GetValue(ArePointsVisibleProperty);
        set => SetValue(ArePointsVisibleProperty, value);
    }

    // ---- state ---------------------------------------------------------------------------------

    private CancellationTokenSource? _renderDelay;
    private double _renderedCellMeters;

    // ---- public API ----------------------------------------------------------------------------

    /// <summary>Moves and zooms the camera so that all points are visible.</summary>
    public void FitToPoints()
    {
        var points = HeatPoints;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var minLat = points.Min(p => p.Latitude);
        var maxLat = points.Max(p => p.Latitude);
        var minLon = points.Min(p => p.Longitude);
        var maxLon = points.Max(p => p.Longitude);

        var center = new Location((minLat + maxLat) / 2, (minLon + maxLon) / 2);
        // 25 % margin around the data and a minimum span so a single point does not zoom in infinitely.
        var latDegrees = Math.Max((maxLat - minLat) * 1.25, 0.004);
        var lonDegrees = Math.Max((maxLon - minLon) * 1.25, 0.004);

        MoveToRegion(new MapSpan(center, latDegrees, lonDegrees));
    }

    // ---- reacting to map movement ----------------------------------------------------------------

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // VisibleRegion is updated by the platform whenever the user pans/zooms.
        if (propertyName == nameof(VisibleRegion) && HasZoomChangedEnough())
        {
            ScheduleRender(350); // debounce: wait until the gesture settles
        }
    }

    private bool HasZoomChangedEnough()
    {
        if (_renderedCellMeters <= 0)
        {
            return true;
        }

        var ratio = CalculateCellMeters() / _renderedCellMeters;
        return ratio > RerenderZoomRatio || ratio < 1 / RerenderZoomRatio;
    }

    // ---- rendering ---------------------------------------------------------------------------------

    /// <summary>Re-renders after <paramref name="delayMs"/>; newer requests cancel older ones (debounce).</summary>
    private void ScheduleRender(int delayMs)
    {
        _renderDelay?.Cancel();
        _renderDelay = new CancellationTokenSource();
        var token = _renderDelay.Token;

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, token);
                }

                if (!token.IsCancellationRequested)
                {
                    Render();
                }
            }
            catch (TaskCanceledException)
            {
                // A newer render request replaced this one.
            }
        });
    }

    /// <summary>Rebuilds all map elements from the current points and zoom level.</summary>
    private void Render()
    {
        MapElements.Clear();

        var points = HeatPoints;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var cellMeters = CalculateCellMeters();
        _renderedCellMeters = cellMeters;

        if (IsHeatVisible)
        {
            DrawHeat(points, cellMeters);
        }

        if (ArePointsVisible)
        {
            DrawRawPoints(points, cellMeters);
        }
    }

    /// <summary>Chooses the grid size so that roughly <see cref="TargetCellsAcrossScreen"/> cells fit on screen.</summary>
    private double CalculateCellMeters()
    {
        var region = VisibleRegion;
        if (region is null)
        {
            return 100; // map has not been laid out yet
        }

        var widthMeters = region.LongitudeDegrees * MetersPerDegree
                          * Math.Cos(region.Center.Latitude * Math.PI / 180);
        return Math.Clamp(widthMeters / TargetCellsAcrossScreen, MinCellMeters, MaxCellMeters);
    }

    /// <summary>Bins the points, then draws one glowing blob per grid cell.</summary>
    private void DrawHeat(IReadOnlyList<LocationPoint> points, double cellMeters)
    {
        // Local equirectangular projection: good enough for city-scale distances.
        var cosLat = Math.Cos(points[0].Latitude * Math.PI / 180);
        var cells = new Dictionary<(int X, int Y), Cell>();

        foreach (var p in points)
        {
            var key = (
                X: (int)Math.Floor(p.Longitude * MetersPerDegree * cosLat / cellMeters),
                Y: (int)Math.Floor(p.Latitude * MetersPerDegree / cellMeters));

            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new Cell();
                cells[key] = cell;
            }

            cell.Add(p);
        }

        // Keep only the densest cells if there are too many (protects rendering performance).
        var ordered = cells.Values
            .OrderByDescending(c => c.Count)
            .Take(MaxCells)
            .OrderBy(c => c.Count)          // hottest cells are drawn last (on top)
            .ToList();

        var maxCount = ordered[^1].Count;
        var logMax = Math.Log(1 + maxCount);

        // Draw layer by layer so the bright cores of hot cells are never hidden by neighbouring halos.
        foreach (var (radiusFactor, baseAlpha) in Layers)
        {
            foreach (var cell in ordered)
            {
                var intensity = Math.Log(1 + cell.Count) / logMax;          // 0..1, log scale
                var alpha = (float)Math.Clamp(baseAlpha * (0.5 + intensity), 0, 0.9);

                MapElements.Add(new Circle
                {
                    Center = cell.Center,
                    Radius = Distance.FromMeters(cellMeters * radiusFactor),
                    FillColor = GradientColor(intensity).WithAlpha(alpha),
                    StrokeColor = Colors.Transparent,
                    StrokeWidth = 0
                });
            }
        }
    }

    /// <summary>Draws the raw positions as small blue dots (thinned out when there are very many).</summary>
    private void DrawRawPoints(IReadOnlyList<LocationPoint> points, double cellMeters)
    {
        var stride = Math.Max(1, points.Count / MaxRawPoints);
        var dotRadius = Math.Max(4, cellMeters * 0.12);

        for (var i = 0; i < points.Count; i += stride)
        {
            MapElements.Add(new Circle
            {
                Center = new Location(points[i].Latitude, points[i].Longitude),
                Radius = Distance.FromMeters(dotRadius),
                FillColor = Color.FromArgb("#E6FFFFFF"),
                StrokeColor = Color.FromArgb("#1D4ED8"),
                StrokeWidth = 2
            });
        }
    }

    /// <summary>
    /// Maps an intensity (0 = cold, 1 = hot) to a colour on a
    /// blue -> cyan -> green -> yellow -> red gradient.
    /// </summary>
    public static Color GradientColor(double intensity)
    {
        (double At, Color Colour)[] stops =
        {
            (0.00, Color.FromArgb("#2563EB")), // blue
            (0.25, Color.FromArgb("#06B6D4")), // cyan
            (0.50, Color.FromArgb("#22C55E")), // green
            (0.75, Color.FromArgb("#FACC15")), // yellow
            (1.00, Color.FromArgb("#EF4444")), // red
        };

        intensity = Math.Clamp(intensity, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            if (intensity <= stops[i].At)
            {
                // Linear interpolation between the two neighbouring stops.
                var t = (float)((intensity - stops[i - 1].At) / (stops[i].At - stops[i - 1].At));
                var a = stops[i - 1].Colour;
                var b = stops[i].Colour;
                return new Color(
                    a.Red + (b.Red - a.Red) * t,
                    a.Green + (b.Green - a.Green) * t,
                    a.Blue + (b.Blue - a.Blue) * t);
            }
        }

        return stops[^1].Colour;
    }

    /// <summary>Accumulates the points that fall into one grid cell and tracks their centroid.</summary>
    private sealed class Cell
    {
        private double _latSum;
        private double _lonSum;

        public int Count { get; private set; }

        public Location Center => new(_latSum / Count, _lonSum / Count);

        public void Add(LocationPoint p)
        {
            _latSum += p.Latitude;
            _lonSum += p.Longitude;
            Count++;
        }
    }
}

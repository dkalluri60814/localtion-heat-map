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
///         <item>The number of points per cell (its "density") is normalised to 0..1 (power curve).</item>
///   <item>Each cell is drawn as a few concentric, translucent <see cref="Circle"/> map elements
///         whose colour comes from a blue -> green -> yellow -> red gradient. Overlapping circles
///         add up, which produces the typical soft "glow" of a heat map.</item>
/// </list>
/// </para>
/// <remarks>The control tracks the circles it adds to <see cref="Map.MapElements"/> and only replaces those.</remarks>
/// </summary>
public class HeatMap : Map
{
    // ---- tuning constants ------------------------------------------------------------------

    private const double MetersPerDegree = 111_320.0;
    private const double TargetCellsAcrossScreen = 46;
    private const double MinCellMeters = 10;
    private const double MaxCellMeters = 600;
    private const int MaxCells = 260;           // upper bound for the number of drawn cells
    private const int MaxRawPoints = 150;       // upper bound for the number of drawn raw dots
    private const double RerenderZoomRatio = 1.2; // re-bin only if the ideal cell size changed by >20 %

    /// <summary>Concentric layers drawn for every cell: (radius relative to cell, base opacity).</summary>
    private static readonly (double Radius, double Alpha)[] Layers =
    {
        (2.2, 0.09), // wide, faint halo
        (1.4, 0.15), // body
        (0.8, 0.24), // bright core
    };

    // ---- bindable properties -----------------------------------------------------------------

    /// <summary>The points that are turned into the heat map.</summary>
    public static readonly BindableProperty HeatPointsProperty = BindableProperty.Create(
        nameof(HeatPoints), typeof(IReadOnlyList<LocationPoint>), typeof(HeatMap), null,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(50, Layer.Heat | Layer.Dots));

    /// <summary>Show / hide the heat layer.</summary>
    public static readonly BindableProperty IsHeatVisibleProperty = BindableProperty.Create(
        nameof(IsHeatVisible), typeof(bool), typeof(HeatMap), true,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(0, Layer.Heat));

    /// <summary>Show / hide the raw recorded positions as small dots.</summary>
    public static readonly BindableProperty ArePointsVisibleProperty = BindableProperty.Create(
        nameof(ArePointsVisible), typeof(bool), typeof(HeatMap), false,
        propertyChanged: (bindable, _, _) => ((HeatMap)bindable).ScheduleRender(0, Layer.Dots));

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

    /// <summary>The independently re-rendered layers. Toggling one never touches the other (big speed-up).</summary>
    [Flags]
    private enum Layer
    {
        Heat = 1,
        Dots = 2
    }

    private readonly List<MapElement> _heatElements = new();
    private readonly List<MapElement> _dotElements = new();
    private CancellationTokenSource? _renderDelay;
    private Layer _pendingLayers;
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
        // Margin around the data (wider horizontally because phones are usually held in portrait) and a
        // minimum span so a single point does not zoom in infinitely.
        var latDegrees = Math.Max((maxLat - minLat) * 1.3, 0.004);
        var lonDegrees = Math.Max((maxLon - minLon) * 1.8, 0.004);

        MoveToRegion(new MapSpan(center, latDegrees, lonDegrees));
    }

    // ---- reacting to map movement ----------------------------------------------------------------

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // VisibleRegion is updated by the platform whenever the user pans/zooms.
        if (propertyName == nameof(VisibleRegion) && HasZoomChangedEnough())
        {
            ScheduleRender(350, Layer.Heat | Layer.Dots); // debounce: wait until the gesture settles
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

    /// <summary>
    /// Re-renders the given layers after <paramref name="delayMs"/>; newer requests cancel older ones
    /// (debounce) and their layers are merged so nothing is lost.
    /// </summary>
    private void ScheduleRender(int delayMs, Layer layers)
    {
        _pendingLayers |= layers;
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
                    var pending = _pendingLayers;
                    _pendingLayers = 0;
                    Render(pending);
                }
            }
            catch (TaskCanceledException)
            {
                // A newer render request replaced this one.
            }
        });
    }

    /// <summary>Rebuilds the requested layers from the current points and zoom level.</summary>
    private void Render(Layer layers)
    {
        var points = HeatPoints;
        var hasPoints = points is { Count: > 0 };
        var cellMeters = CalculateCellMeters();

        if (layers.HasFlag(Layer.Heat))
        {
            Replace(_heatElements, IsHeatVisible && hasPoints ? DrawHeat(points!, cellMeters) : null);
        }

        if (layers.HasFlag(Layer.Dots))
        {
            Replace(_dotElements, ArePointsVisible && hasPoints ? DrawRawPoints(points!, cellMeters) : null);
        }

        if (layers.HasFlag(Layer.Heat))
        {
            _renderedCellMeters = cellMeters;
        }
    }

    /// <summary>Removes the previously drawn elements of one layer and adds the new ones.</summary>
    private void Replace(List<MapElement> current, IEnumerable<MapElement>? replacement)
    {
        foreach (var element in current)
        {
            MapElements.Remove(element);
        }

        current.Clear();

        if (replacement is null)
        {
            return;
        }

        foreach (var element in replacement)
        {
            MapElements.Add(element);
            current.Add(element);
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
    private List<Circle> DrawHeat(IReadOnlyList<LocationPoint> points, double cellMeters)
    {
        var circles = new List<Circle>();
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

        // Smooth the density: every cell also "feels" its 8 neighbours (centre 1.0, edge 0.5, corner 0.25).
        // This spreads hot spots into wider glowing areas instead of single isolated squares.
        foreach (var (key, cell) in cells)
        {
            var weight = cell.Count * 1.0;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if ((dx != 0 || dy != 0) && cells.TryGetValue((key.X + dx, key.Y + dy), out var neighbour))
                    {
                        weight += neighbour.Count * (dx == 0 || dy == 0 ? 0.5 : 0.25);
                    }
                }
            }

            cell.Weight = weight;
        }

        // Keep only the densest cells if there are too many (protects rendering performance).
        var ordered = cells.Values
            .OrderByDescending(c => c.Weight)
            .Take(MaxCells)
            .OrderBy(c => c.Weight)         // hottest cells are drawn last (on top)
            .ToList();

        var maxWeight = ordered[^1].Weight;

        // Draw layer by layer so the bright cores of hot cells are never hidden by neighbouring halos.
        foreach (var (radiusFactor, baseAlpha) in Layers)
        {
            foreach (var cell in ordered)
            {
                // Square-root curve: lightly-visited areas stay cool while real hotspots turn red.
                var intensity = Math.Pow(cell.Weight / maxWeight, 0.5);          // 0..1
                var alpha = (float)Math.Clamp(baseAlpha * (0.6 + intensity), 0, 0.9);

                circles.Add(new Circle
                {
                    Center = cell.Center,
                    Radius = Distance.FromMeters(cellMeters * radiusFactor),
                    FillColor = GradientColor(intensity).WithAlpha(alpha),
                    StrokeColor = Colors.Transparent,
                    StrokeWidth = 0
                });
            }
        }

        return circles;
    }

    /// <summary>Draws the raw positions as small blue dots (thinned out when there are very many).</summary>
    private List<Circle> DrawRawPoints(IReadOnlyList<LocationPoint> points, double cellMeters)
    {
        var circles = new List<Circle>();
        var stride = Math.Max(1, points.Count / MaxRawPoints);
        var dotRadius = Math.Max(6, cellMeters * 0.22);

        for (var i = 0; i < points.Count; i += stride)
        {
            circles.Add(new Circle
            {
                Center = new Location(points[i].Latitude, points[i].Longitude),
                Radius = Distance.FromMeters(dotRadius),
                FillColor = Color.FromArgb("#E6FFFFFF"),
                StrokeColor = Color.FromArgb("#1D4ED8"),
                StrokeWidth = 2
            });
        }

        return circles;
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

        /// <summary>Neighbour-smoothed density used for colouring.</summary>
        public double Weight { get; set; }

        public Location Center => new(_latSum / Count, _lonSum / Count);

        public void Add(LocationPoint p)
        {
            _latSum += p.Latitude;
            _lonSum += p.Longitude;
            Count++;
        }
    }
}

using LocationHeatMap.Models;

namespace LocationHeatMap.Services;

/// <summary>
/// Generates a realistic-looking, fully deterministic set of "visited" locations in Paris so the
/// heat map can be demonstrated without walking around. Several sight-seeing routes are walked
/// with different frequencies (popular routes become the "hot" areas) and the traveller lingers
/// around each landmark (which creates the hottest spots).
/// </summary>
public static class SampleDataSeeder
{
    private const double MetersPerDegreeLatitude = 111_320.0;
    private const double StepMeters = 12;          // distance between two samples while walking
    private const double WalkNoiseMeters = 7;      // GPS jitter while walking
    private const double TripOffsetMeters = 12;    // each trip is shifted a bit sideways
    private const double DwellNoiseMeters = 14;    // spread of points while lingering at a landmark
    private const int TripCount = 18;

    /// <summary>Points of interest used both for the routes and for the map pins.</summary>
    public static IReadOnlyList<Landmark> Landmarks { get; } = new List<Landmark>
    {
        new("Eiffel Tower",       "Iron lattice tower on the Champ de Mars", 48.8584, 2.2945),
        new("Trocadéro",          "Viewpoint across the Seine",              48.8616, 2.2893),
        new("Arc de Triomphe",    "Monument at the Champs-Élysées",          48.8738, 2.2950),
        new("Place de la Concorde","Largest square in Paris",                48.8656, 2.3212),
        new("Louvre Museum",      "World's largest art museum",              48.8606, 2.3376),
        new("Musée d'Orsay",      "Impressionist art in a former station",   48.8600, 2.3266),
        new("Notre-Dame",         "Gothic cathedral on Île de la Cité",      48.8530, 2.3499),
        new("Luxembourg Gardens", "Palace gardens in the Latin Quarter",     48.8462, 2.3372),
        new("Sacré-Cœur",         "Basilica on the hill of Montmartre",      48.8867, 2.3431),
    };

    // Each route is a list of landmark indexes plus a weight (how often it is walked).
    private static readonly (int[] Stops, int Weight)[] Routes =
    {
        (new[] { 0, 1, 2 },       6), // Eiffel Tower -> Trocadéro -> Arc de Triomphe (most popular)
        (new[] { 4, 5, 6, 7 },    5), // Louvre -> Orsay -> Notre-Dame -> Luxembourg
        (new[] { 6, 4, 3, 2 },    4), // Notre-Dame -> Louvre -> Concorde -> Arc de Triomphe
        (new[] { 8, 4 },          2), // Sacré-Cœur -> Louvre
        (new[] { 3, 5, 0 },       3), // Concorde -> Orsay -> Eiffel Tower
    };

    /// <summary>Builds the sample points. The same <paramref name="seed"/> always yields the same data.</summary>
    public static IReadOnlyList<LocationPoint> Generate(int seed = 42)
    {
        var random = new Random(seed);
        var points = new List<LocationPoint>();
        var clock = DateTime.UtcNow.Date.AddDays(-7).AddHours(9); // start one week ago, 09:00 UTC

        for (var trip = 0; trip < TripCount; trip++)
        {
            var route = PickRoute(random);

            // A constant sideways shift per trip makes repeated trips form a band instead of one thin line.
            var offsetNorth = Gaussian(random) * TripOffsetMeters;
            var offsetEast = Gaussian(random) * TripOffsetMeters;

            for (var i = 0; i < route.Length; i++)
            {
                var stop = Landmarks[route[i]];

                // Linger around the landmark.
                AddDwell(points, random, stop, offsetNorth, offsetEast, ref clock);

                // Walk to the next landmark.
                if (i < route.Length - 1)
                {
                    AddWalk(points, random, stop, Landmarks[route[i + 1]], offsetNorth, offsetEast, ref clock);
                }
            }

            clock = clock.AddHours(3); // gap before the next trip
        }

        return points;
    }

    /// <summary>Replaces the database content with freshly generated sample data.</summary>
    public static async Task SeedAsync(ILocationRepository repository)
    {
        await repository.ClearAsync();
        await repository.InsertAllAsync(Generate());
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Weighted random choice of a route.</summary>
    private static int[] PickRoute(Random random)
    {
        var total = Routes.Sum(r => r.Weight);
        var roll = random.Next(total);
        foreach (var (stops, weight) in Routes)
        {
            if (roll < weight)
            {
                return stops;
            }

            roll -= weight;
        }

        return Routes[0].Stops;
    }

    /// <summary>Adds a cluster of points scattered around a landmark.</summary>
    private static void AddDwell(List<LocationPoint> points, Random random, Landmark at,
                                 double offsetNorth, double offsetEast, ref DateTime clock)
    {
        var count = random.Next(25, 60);
        for (var i = 0; i < count; i++)
        {
            points.Add(CreatePoint(at.Latitude, at.Longitude,
                offsetNorth + Gaussian(random) * DwellNoiseMeters,
                offsetEast + Gaussian(random) * DwellNoiseMeters,
                ref clock));
        }
    }

    /// <summary>Adds points along the straight line between two landmarks.</summary>
    private static void AddWalk(List<LocationPoint> points, Random random, Landmark from, Landmark to,
                                double offsetNorth, double offsetEast, ref DateTime clock)
    {
        var cosLat = Math.Cos(from.Latitude * Math.PI / 180);
        var dNorth = (to.Latitude - from.Latitude) * MetersPerDegreeLatitude;
        var dEast = (to.Longitude - from.Longitude) * MetersPerDegreeLatitude * cosLat;
        var distance = Math.Sqrt(dNorth * dNorth + dEast * dEast);
        var steps = Math.Max(1, (int)(distance / StepMeters));

        for (var s = 1; s < steps; s++)
        {
            var t = (double)s / steps;
            points.Add(CreatePoint(
                from.Latitude + (to.Latitude - from.Latitude) * t,
                from.Longitude + (to.Longitude - from.Longitude) * t,
                offsetNorth + Gaussian(random) * WalkNoiseMeters,
                offsetEast + Gaussian(random) * WalkNoiseMeters,
                ref clock));
        }
    }

    /// <summary>Creates a point displaced by the given number of metres from a base coordinate.</summary>
    private static LocationPoint CreatePoint(double lat, double lon, double northMeters, double eastMeters,
                                             ref DateTime clock)
    {
        clock = clock.AddSeconds(5);
        var cosLat = Math.Cos(lat * Math.PI / 180);
        return new LocationPoint
        {
            TimestampUtc = clock,
            Latitude = lat + northMeters / MetersPerDegreeLatitude,
            Longitude = lon + eastMeters / (MetersPerDegreeLatitude * cosLat),
            AccuracyMeters = 5,
            IsSample = true
        };
    }

    /// <summary>Standard normal random number (Box-Muller transform).</summary>
    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble(); // (0,1] so Log never sees 0
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

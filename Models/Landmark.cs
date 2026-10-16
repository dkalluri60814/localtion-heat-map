namespace LocationHeatMap.Models;

/// <summary>A named point of interest that is shown as a pin on the map.</summary>
/// <param name="Name">Display name of the place.</param>
/// <param name="Description">Short text shown in the pin's info window.</param>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
public sealed record Landmark(string Name, string Description, double Latitude, double Longitude);

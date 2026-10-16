using SQLite;

namespace LocationHeatMap.Models;

/// <summary>
/// A single recorded position of the user. Each instance maps to one row of the
/// <c>LocationPoints</c> table in the local SQLite database.
/// </summary>
[Table("LocationPoints")]
public class LocationPoint
{
    /// <summary>Auto-incremented primary key.</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Moment (UTC) at which the position was captured.</summary>
    [Indexed]
    public DateTime TimestampUtc { get; set; }

    /// <summary>Latitude in decimal degrees (WGS-84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude in decimal degrees (WGS-84).</summary>
    public double Longitude { get; set; }

    /// <summary>Horizontal accuracy reported by the device, in metres (if known).</summary>
    public double? AccuracyMeters { get; set; }

    /// <summary><c>true</c> for generated demo data, <c>false</c> for real device fixes.</summary>
    public bool IsSample { get; set; }
}

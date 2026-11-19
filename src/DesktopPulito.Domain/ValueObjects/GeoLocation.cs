namespace DesktopPulito.Domain;

/// <summary>Coordinate geografiche valide.</summary>
public readonly record struct GeoLocation(double Latitude, double Longitude, string Name)
{
    public static GeoLocation Create(double lat, double lon, string name)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180) throw new DomainException("Coordinate non valide.");
        return new(lat, lon, name);
    }
}


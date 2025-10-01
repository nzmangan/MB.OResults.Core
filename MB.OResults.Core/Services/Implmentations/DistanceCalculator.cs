namespace MB.OResults.Core;

public class DistanceCalculator : IDistanceCalculator {
  private const double EarthRadiusMeters = 6378137; // Radius of the earth in meters

  public int? CalculateDistance(double? lat1, double? lng1, double? lat2, double? lng2) {
    if (!lat1.HasValue || !lng1.HasValue || !lat2.HasValue || !lng2.HasValue) {
      return null;
    }

    // Convert degrees to radians
    double dLat = ToRadians(lat2.Value - lat1.Value);
    double dLon = ToRadians(lng2.Value - lng1.Value);

    // Haversine formula
    double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
              Math.Cos(ToRadians(lat1.Value)) * Math.Cos(ToRadians(lat2.Value)) *
              Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

    double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

    // Calculate distance in meters
    double distance = EarthRadiusMeters * c;

    return Convert.ToInt32(distance);
  }

  private static double ToRadians(double degrees) {
    return degrees * (Math.PI / 180);
  }
}
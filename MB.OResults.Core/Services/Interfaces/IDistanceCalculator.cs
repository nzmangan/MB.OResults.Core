namespace MB.OResults.Core;

public interface IDistanceCalculator {
  int? CalculateDistance(double? lat1, double? lng1, double? lat2, double? lng2);
}
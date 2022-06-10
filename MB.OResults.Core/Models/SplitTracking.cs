namespace MB.OResults.Core;

public class SplitTracking {
  public string Id { get; set; }
  public double? TimeInSeconds { get; set; }
  public string Code { get; set; }
  public string PreviousCode { get; set; }
  public double? Total { get; set; }
  public string Name { get; set; }
}
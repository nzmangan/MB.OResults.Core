namespace MB.OResults.Core;

public class LegSplitStatistic {
  public int Leg { get; set; }
  public string Grade { get; set; }
  public string Control { get; set; }
  public string PreviousControl { get; set; }
  public string NextControl { get; set; }
  public string FirstName { get; set; }
  public string LastName { get; set; }
  public string Club { get; set; }
  public double? LegTime { get; set; }
  public double? PerformanceIndex { get; set; }
  public double? PerformanceIndexAdjusted { get; set; }
  public double? PredictedLegTime { get; set; }
  public double? TimeLoss { get; set; }
  public DateTime? ActualTime { get; set; }
  public double? KmRate { get; set; }
}
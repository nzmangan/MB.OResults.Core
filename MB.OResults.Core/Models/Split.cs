using System;
using System.Collections.Generic;

namespace MB.OResults.Core;

public class Split {
  public string Code { get; set; }
  public string PreviousCode { get; set; }
  public string NextCode { get; set; }
  public double? Leg { get; set; }
  public int? LegPosition { get; set; }
  public double? LegTimeBehind { get; set; }
  public double? Total { get; set; }
  public int? TotalPosition { get; set; }
  public double? TotalBehind { get; set; }
  public double? PerformanceIndex { get; set; }
  public double? PerformanceIndexAdjusted { get; set; }
  public double? PredictedLegTime { get; set; }
  public double? TimeLoss { get; set; }
  public DateTime? ActualTime { get; set; }
  public List<RunnerDetails> Pack { get; set; } = new();
}
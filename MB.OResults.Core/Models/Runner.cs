using System;
using System.Collections.Generic;

namespace MB.OResults.Core;

public class Runner {
  public string Name { get; set; }
  public string Club { get; set; }
  public string Position { get; set; }
  public double? TimeInSeconds { get; set; }
  public string Status { get; set; }
  public List<Split> Splits { get; set; }
  public string Id { get; set; }
  public double? PerformanceIndex { get; set; }
  public double? TimeLoss { get; set; }
  public double? KmRate { get; set; }
  public DateTime? StartTime { get; set; }
}
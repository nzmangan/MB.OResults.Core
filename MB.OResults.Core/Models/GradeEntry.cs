using System.Collections.Generic;

namespace MB.OResults.Core;

public class GradeEntry {
  public string Id { get; set; }
  public string Name { get; set; }
  public List<Runner> Runners { get; set; }
  public Course Course { get; set; }
}
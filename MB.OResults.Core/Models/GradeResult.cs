namespace MB.OResults.Core;

public class GradeResult {
  public string Id { get; set; }
  public string Name { get; set; }
  public List<LegData> Legs { get; set; }
  public List<Runner> Runners { get; set; }
  public Course Course { get; set; }
}
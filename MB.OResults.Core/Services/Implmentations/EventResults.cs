namespace MB.OResults.Core;

public class EventResults {
  public List<GradeResult> Grades { get; set; } = [];
  public DateTime Created { get; set; }
  public string EventName { get; set; }
}
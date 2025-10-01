namespace MB.OResults.Core;

public class RebuildResponse {
  public List<GradeResult> Results { get; set; }
  public bool HasSplits { get; set; }
  public bool HasResults { get; set; }
  public DateTime Created { get; set; }
  public string EventName { get; set; }
}
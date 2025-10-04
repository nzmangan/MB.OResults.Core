namespace MB.OResults.Core;

public class ResultBuilderService(ILogger<ResultBuilderService> _Logger, IResultService _ResultService, IStartListService _StartListService, IEntryListService _EntryListService, IClassListService _ClassListService, ICourseDataService _CourseDataService, IAnalyzerService _AnalyzerService) : IResultBuilderService {

  public async Task<RebuildResponse> Build() {
    _Logger.LogInformation("Rebuilding cache...");

    var courseData = await GetCourseDataAsync();

    var gradeResults = await GetResultAsync(courseData);

    gradeResults ??= new();
    gradeResults.Grades ??= [];

    bool hasResults = gradeResults.Grades.Count > 0;
    bool hasSplits = false;

    if (!hasResults) {
      _Logger.LogInformation("No results uploaded!");
    }

    var starts = await GetStartsAsync();

    var currentRunners = gradeResults.Grades.SelectMany(p => p.Runners).Select(p => p.Id).ToList();

    foreach (var start in starts) {
      var grade = gradeResults.Grades.FirstOrDefault(p => p.Name == start.Name);
      if (grade == null) {
        grade = new GradeResult { Legs = [], Course = start.Course, Id = start.Id, Name = start.Name, Runners = [] };
        gradeResults.Grades.Add(grade);
      }
      var noResults = start.Runners.Where(p => !currentRunners.Contains(p.Id)).OrderBy(p => p.StartTime.HasValue ? p.StartTime.Value.ToString() : $"{p.FirstName} {p.LastName}".Trim());
      grade.Runners.AddRange(noResults);

    }

    currentRunners = [.. gradeResults.Grades.SelectMany(p => p.Runners).Select(p => p.Id)];

    var entries = await GetEntriesAsync();

    foreach (var entry in entries) {
      var grade = gradeResults.Grades.FirstOrDefault(p => p.Name == entry.Name);
      if (grade == null) {
        grade = new GradeResult { Legs = [], Course = entry.Course, Id = entry.Id, Name = entry.Name, Runners = [] };
        gradeResults.Grades.Add(grade);
      }
      var noResults = entry.Runners.Where(p => !currentRunners.Contains(p.Id)).OrderBy(p => $"{p.FirstName} {p.LastName}".Trim());
      grade.Runners.AddRange(noResults);
    }

    if (hasResults) {
      hasSplits = gradeResults.Grades.SelectMany(p => p.Runners ?? []).SelectMany(p => p.Splits ?? []).Any();

      if (!hasSplits) {
        _Logger.LogInformation("No split times in any grade.");
      }
    }

    var results = gradeResults.Grades.OrderBy(p => p.Name).ToList();

    return new RebuildResponse {
      HasResults = hasResults,
      HasSplits = hasSplits,
      Results = results,
      Created = gradeResults.Created,
      EventName = gradeResults.EventName,
    };
  }

  private async Task<List<GradeEntry>> GetEntriesAsync() {
    var results = await _EntryListService.Get();

    if (results?.PersonEntry is null) {
      return [];
    }

    var grades = await GetGradesAsync();

    return _AnalyzerService.ConvertToGradeEntry(results.PersonEntry, grades);
  }

  private async Task<List<Class>> GetGradesAsync() {
    var results = await _ClassListService.Get();

    if (results?.Class is null) {
      return [];
    }

    return [.. results.Class];
  }

  private async Task<CourseInfo> GetCourseDataAsync() {
    var data = await _CourseDataService.Get();
    return _AnalyzerService.ConvertToControlData(data);
  }

  private async Task<List<GradeStart>> GetStartsAsync() {
    var results = await _StartListService.Get();

    if (results?.ClassStart is null) {
      return [];
    }

    return [.. results.ClassStart.Select(p => _AnalyzerService.ConvertToGradeStart(p))];
  }

  private async Task<EventResults> GetResultAsync(CourseInfo courseInfo) {
    var results = await _ResultService.Get();

    if (results?.ClassResult is null) {
      return new();
    }

    var instance = results.ClassResult.Select(p => _AnalyzerService.ConvertToGradeResult(p, courseInfo)).ToList();

    if (instance.SelectMany(p => p.Runners).SelectMany(p => p.Splits).Any()) {
      _Logger.LogInformation("No split times in results.");
    }

    return new EventResults {
      Grades = instance,
      Created = results.CreateTimeSpecified ? results.CreateTime : DateTime.Now,
      EventName = results.Event?.Name
    };
  }
}
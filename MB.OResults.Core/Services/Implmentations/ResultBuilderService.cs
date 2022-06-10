using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IOF.XML.V3;
using Microsoft.Extensions.Logging;

namespace MB.OResults.Core;

public class ResultBuilderService : IResultBuilderService {
  private readonly ILogger<ResultBuilderService> _Logger;
  private readonly IResultService _ResultService;
  private readonly IStartListService _StartListService;
  private readonly IEntryListService _EntryListService;
  private readonly IClassListService _ClassListService;
  private readonly IAnalyzerService _AnalyzerService;

  public ResultBuilderService(ILogger<ResultBuilderService> logger, IResultService resultService, IStartListService startListService, IEntryListService entryListService, IClassListService classListService, IAnalyzerService analyzerService) {
    _Logger = logger;
    _ResultService = resultService;
    _StartListService = startListService;
    _EntryListService = entryListService;
    _ClassListService = classListService;
    _AnalyzerService = analyzerService;
  }

  public async Task<RebuildResponse> Build() {
    _Logger.LogInformation("Rebuilding cache...");

    var gradeResults = await GetResultAsync();

    gradeResults ??= new();
    gradeResults.Grades ??= new();

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
        grade = new GradeResult { Codes = new List<string>(), Course = start.Course, Id = start.Id, Name = start.Name, Runners = new List<Runner>() };
        gradeResults.Grades.Add(grade);
      }
      var noResults = start.Runners.Where(p => !currentRunners.Contains(p.Id)).OrderBy(p => p.StartTime.HasValue ? p.StartTime.Value.ToString() : p.Name);
      grade.Runners.AddRange(noResults);

    }

    currentRunners = gradeResults.Grades.SelectMany(p => p.Runners).Select(p => p.Id).ToList();

    var entries = await GetEntriesAsync();

    foreach (var entry in entries) {
      var grade = gradeResults.Grades.FirstOrDefault(p => p.Name == entry.Name);
      if (grade == null) {
        grade = new GradeResult { Codes = new List<string>(), Course = entry.Course, Id = entry.Id, Name = entry.Name, Runners = new List<Runner>() };
        gradeResults.Grades.Add(grade);
      }
      var noResults = entry.Runners.Where(p => !currentRunners.Contains(p.Id)).OrderBy(p => p.Name);
      grade.Runners.AddRange(noResults);
    }

    if (hasResults) {
      hasSplits = gradeResults.Grades.SelectMany(p => p.Runners ?? new List<Runner>()).SelectMany(p => p.Splits ?? new List<Split>()).Any();

      if (!hasSplits) {
        _Logger.LogInformation("No split times in any grade.");
      }
    }

    return new RebuildResponse {
      HasResults = hasResults,
      HasSplits = hasSplits,
      Results = gradeResults.Grades.OrderBy(p => p.Name).ToList(),
      Created = gradeResults.Created
    };
  }

  private async Task<List<GradeEntry>> GetEntriesAsync() {
    var results = await _EntryListService.Get();

    if (results?.PersonEntry is null) {
      return new();
    }

    var grades = await GetGradesAsync();

    return _AnalyzerService.ConvertToGradeEntry(results.PersonEntry, grades);
  }

  private async Task<List<Class>> GetGradesAsync() {
    var results = await _ClassListService.Get();

    if (results?.Class is null) {
      return new();
    }

    return results.Class.ToList();
  }

  private async Task<List<GradeStart>> GetStartsAsync() {
    var results = await _StartListService.Get();

    if (results?.ClassStart is null) {
      return new();
    }

    return results.ClassStart.Select(p => _AnalyzerService.ConvertToGradeStart(p)).ToList();
  }

  private async Task<Results> GetResultAsync() {
    var results = await _ResultService.Get();

    if (results?.ClassResult is null) {
      return new();
    }

    var instance = results.ClassResult.Select(p => _AnalyzerService.ConvertToGradeResult(p)).ToList();

    if (instance.SelectMany(p => p.Runners).SelectMany(p => p.Splits).Count() > 0) {
      _Logger.LogInformation("No split times in results.");
    }

    return new Results {
      Grades = instance,
      Created = results.CreateTimeSpecified ? results.CreateTime : DateTime.Now
    };
  }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IOF.XML.V3;
using Microsoft.Extensions.Logging;

namespace MB.OResults.Core {
  public class AnalyzerService : IAnalyzerService {
    private readonly ILogger<AnalyzerService> _Logger;
    private readonly AnalyzerServiceConfiguration _AnalyzerServiceConfiguration;

    public AnalyzerService(ILogger<AnalyzerService> logger, AnalyzerServiceConfiguration analyzerServiceConfiguration) {
      _Logger = logger;
      _AnalyzerServiceConfiguration = analyzerServiceConfiguration;
    }

    public GradeResult ConvertToGradeResult(ClassResult result) {
      string lastControl = null;
      List<string> codes = new List<string>();
      double? courseLength = null;
      DateTime? firstStart = null;

      if (result.Course != null && result.Course.Length > 0 && result.Course[0].LengthSpecified) {
        courseLength = result.Course[0].Length;
      }

      if (result?.PersonResult?.Length > 0 && result?.PersonResult[0].Result?.Length > 0) {
        var r = result.PersonResult[0].Result[0];
        var splitTimes = r.SplitTime ?? new List<SplitTime>().ToArray();
        lastControl = splitTimes.Where(p => p.Status != SplitTimeStatus.Additional).LastOrDefault()?.ControlCode;
        codes = splitTimes.Where(p => p.Status != SplitTimeStatus.Additional).Select(p => p.ControlCode).ToList();

        var resultsWithStartTime = result.PersonResult.SelectMany(p => p.Result).Where(p => p.StartTimeSpecified).ToList();

        if (resultsWithStartTime != null && resultsWithStartTime.Count > 0) {
          firstStart = resultsWithStartTime.Min(p => p.StartTime);
        }

        _Logger.LogInformation($"First start: {firstStart}.");
      }

      var splitTracking = new List<SplitTracking>();
      var splitReference = new Dictionary<string, Split>();
      var performanceTracking = new Dictionary<string, List<double>>();

      List<Runner> runners = new List<Runner>();

      if (result.PersonResult != null) {
        runners = result.PersonResult.Select(p => {
          var person = new Runner { };

          if (p.Person?.Name is not null) {
            person.Name = (p.Person.Name.Given + " " + p.Person.Name.Family).Trim();

            var id = p.Person.Id?.FirstOrDefault()?.Value;

            person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? GetPersonId(result.Class.Name, p) : id;
          }

          if (p.Organisation != null) {
            person.Club = p.Organisation.Name;
          }

          if (p.Result.Length < 0) {
            return person;
          }

          var cr = p.Result[0];
          var status = cr.Status.ToString();
          person.Position = cr.Position;
          person.TimeInSeconds = cr.TimeSpecified ? cr.Time : (double?)null;

          if (cr.StartTimeSpecified) {
            person.StartTime = cr.StartTime.Date.AddSeconds((cr.StartTime - cr.StartTime.Date).TotalSeconds);
          }

          person.Status = GetStatusCode(status);

          var splitTimes = cr.SplitTime != null ? cr.SplitTime.ToList() : new List<SplitTime>();
          var timeAtLastSplit = !String.IsNullOrWhiteSpace(lastControl) ? cr.SplitTime.Where(st => st.ControlCode == lastControl).FirstOrDefault()?.Time : null;
          var sprint = person.TimeInSeconds.HasValue && timeAtLastSplit.HasValue ? person.TimeInSeconds - timeAtLastSplit : null;
          splitTimes.Add(new IOF.XML.V3.SplitTime {
            ControlCode = Constants.FinishCode,
            TimeSpecified = sprint.HasValue,
            Time = person.TimeInSeconds ?? 0
          });

          person.Splits = new List<Split>();

          double? previousTime = 0;
          string previousCode = Constants.StartCode;
          bool valid = true;

          for (var controlIndex = 0; controlIndex < splitTimes.Count; controlIndex++) {
            var control = splitTimes[controlIndex];

            var splitTime = new Split {
              Code = control.ControlCode,
              PreviousCode = previousCode,
              NextCode = controlIndex == splitTimes.Count - 1 ? Constants.DownloadCode : splitTimes[controlIndex + 1].ControlCode,
              Leg = null,
              LegPosition = null,
              LegTimeBehind = null,
              PerformanceIndex = null,
              PerformanceIndexAdjusted = null,
              PredictedLegTime = null,
              TimeLoss = null,
              Total = null,
              TotalBehind = null,
              TotalPosition = null
            };

            if (control.TimeSpecified && control.Status == SplitTimeStatus.OK) {
              var split = previousTime.HasValue ? control.Time - previousTime.Value : (double?)null;

              splitTime.Total = control.Time;
              previousTime = control.Time;

              if (split.HasValue && split < 0.01) {
                _Logger.LogInformation($"{person.Name} {person.Club} has a negative split time on leg between {previousCode} and {control.ControlCode}.");
                split = null;
                previousTime = null;
              }


              if (split.HasValue) {
                splitTime.Leg = split.Value;

                splitTracking.Add(new SplitTracking {
                  Code = splitTime.Code,
                  Id = person.Id,
                  PreviousCode = splitTime.PreviousCode,
                  TimeInSeconds = splitTime.Leg,
                  Total = valid ? splitTime.Total : null,
                  Name = person.Name
                });

                string performanceTrackingKey = GetGenericKey(splitTime.PreviousCode, splitTime.Code, splitTime.NextCode);

                if (!performanceTracking.ContainsKey(performanceTrackingKey)) {
                  performanceTracking[performanceTrackingKey] = new List<double>();
                }

                performanceTracking[performanceTrackingKey].Add(split.Value);
              }
            } else {
              previousTime = null;
            }


            if ((control.Status == SplitTimeStatus.Missing || !control.TimeSpecified) && status != Constants.StatusOK) {
              valid = false;
            }

            splitTime.LegPosition = null;
            splitTime.TotalPosition = null;

            person.Splits.Add(splitTime);
            previousCode = control.ControlCode;

            splitReference.Add(GetPersonKey(person.Id, splitTime.PreviousCode, splitTime.Code, splitTime.NextCode), splitTime);
          }

          return person;
        }).ToList();
      }

      double totalAverageTime = 0;
      var fastestSplit = new Dictionary<string, double>();
      var fastestTotal = new Dictionary<string, double>();
      var totalAverageTimeMapping = new Dictionary<string, double>();

      for (int i = 0; i <= codes.Count; i++) {
        var previousCode = i == 0 ? Constants.StartCode : codes[i - 1];
        var code = i == codes.Count ? Constants.FinishCode : codes[i];
        var nextCode = i + 1 == codes.Count ? Constants.FinishCode : i == codes.Count ? Constants.DownloadCode : codes[i + 1];

        var leg = splitTracking.Where(p => p.PreviousCode == previousCode && p.Code == code);

        var previousPlace = 0;
        var previousTime = 0d;
        var offset = 0;

        var withTotals = leg.Where(p => p.Total.HasValue).ToList(); //Make sure that only valid are left.

        if (withTotals.Count > 0) {
          fastestTotal.Add(GetGenericKey(previousCode, code, nextCode), withTotals.Min(p => p.Total.Value));
        }

        var withSplits = leg.Where(p => p.TimeInSeconds.HasValue);
        var values = withSplits.Count();

        var averageSplitTime = (double?)null;

        if (values > 0) {
          fastestSplit.Add(GetGenericKey(previousCode, code, nextCode), withSplits.Min(p => p.TimeInSeconds.Value));

          int itemsToCount = Convert.ToInt32(Math.Ceiling((double)values / 4));
          averageSplitTime = withSplits.OrderBy(p => p.TimeInSeconds.Value).Take(itemsToCount).Average(p => p.TimeInSeconds.Value);
          totalAverageTime += averageSplitTime.Value;
          totalAverageTimeMapping.Add(GetGenericKey(previousCode, code, nextCode), averageSplitTime.Value);
        }

        foreach (var splitOrder in leg.Where(p => p.TimeInSeconds.HasValue).OrderBy(p => p.TimeInSeconds.Value)) {
          if (splitOrder.TimeInSeconds.Value != previousTime) {
            previousPlace = previousPlace + offset + 1;
            offset = 0;
          } else {
            offset++;
          }

          previousTime = splitOrder.TimeInSeconds.Value;

          string key = GetPersonKey(splitOrder.Id, previousCode, code, nextCode);

          if (splitReference.ContainsKey(key) && averageSplitTime.HasValue) {
            splitReference[key].LegPosition = previousPlace;
            if (splitOrder.TimeInSeconds.Value != 0) {
              splitReference[key].PerformanceIndex = averageSplitTime / splitOrder.TimeInSeconds.Value;
            } else {
              splitReference[key].PerformanceIndex = null;
            }
          }
        }

        previousPlace = 0;
        previousTime = 0d;
        offset = 0;

        foreach (var splitOrder in leg.Where(p => p.Total.HasValue).OrderBy(p => p.Total.Value)) {
          if (splitOrder.Total.Value != previousTime) {
            previousPlace = previousPlace + offset + 1;
            offset = 0;
          } else {
            offset++;
          }

          previousTime = splitOrder.Total.Value;

          string key = GetPersonKey(splitOrder.Id, previousCode, code, nextCode);

          if (splitReference.ContainsKey(key)) {
            splitReference[key].TotalPosition = previousPlace;
          }
        }
      }

      var controlPassingLookup = new Dictionary<string, List<PassingTime>>();

      foreach (var runner in runners) {
        if (runner.TimeInSeconds.HasValue && runner.Status == Constants.StatusOK) {
          runner.PerformanceIndex = totalAverageTime / runner.TimeInSeconds.Value;
        } else {
          double average = 0;
          double personal = 0;

          foreach (var split in runner.Splits) {
            var key = GetGenericKey(split.PreviousCode, split.Code, split.NextCode);
            if (split.Leg.HasValue && totalAverageTimeMapping.ContainsKey(key)) {
              average += totalAverageTimeMapping[key];
              personal += split.Leg.Value;
            }
          }

          if (average > 0 && personal > 0) {
            runner.PerformanceIndex = average / personal;
          }
        }

        foreach (var split in runner.Splits) {
          var key = GetGenericKey(split.PreviousCode, split.Code, split.NextCode);

          if (split.PerformanceIndex.HasValue) {
            split.PerformanceIndexAdjusted = split.PerformanceIndex.Value / runner.PerformanceIndex;

            if (split.PerformanceIndexAdjusted < _AnalyzerServiceConfiguration.MistakeIndex && totalAverageTimeMapping.ContainsKey(key)) {
              var average = totalAverageTimeMapping[key];
              split.PredictedLegTime = average / runner.PerformanceIndex;
              split.TimeLoss = split.Leg - split.PredictedLegTime;
            }
          }

          if (split.Leg.HasValue && fastestSplit.ContainsKey(key)) {
            split.LegTimeBehind = split.Leg.Value - fastestSplit[key];
          }

          if (split.Total.HasValue && fastestTotal.ContainsKey(key)) {
            split.TotalBehind = split.Total.Value - fastestTotal[key];
          }

          if (runner.StartTime.HasValue && split.Total.HasValue) {
            split.ActualTime = runner.StartTime.Value.Add(TimeSpan.FromSeconds(split.Total.Value));
            if (!controlPassingLookup.ContainsKey(key)) {
              controlPassingLookup.Add(key, new());
            }
            controlPassingLookup[key].Add(new PassingTime {
              Time = split.ActualTime,
              Name = runner.Name,
              Club = runner.Club
            });
          }
        }

        runner.TimeLoss = runner.Splits.Where(p => p.TimeLoss.HasValue).Sum(p => p.TimeLoss.Value);

        if (runner.TimeInSeconds.HasValue && courseLength.HasValue) {
          runner.KmRate = (runner.TimeInSeconds.Value / 60) / (courseLength.Value / 1000);
        }
      }

      foreach (var runner in runners) {
        foreach (var split in runner.Splits) {
          var key = GetGenericKey(split.PreviousCode, split.Code, split.NextCode);
          if (controlPassingLookup.ContainsKey(key) && split.ActualTime.HasValue) {
            split.Pack = controlPassingLookup[key]
             .Select(p => new RunnerDetails { Name = p.Name, Club = p.Club, Delta = (p.Time.Value - split.ActualTime.Value).TotalMinutes })
             .Where(p => Math.Abs(p.Delta) <= _AnalyzerServiceConfiguration.Pack && $"{p.Name} {p.Club}" != $"{runner.Name} {runner.Club}")
             .OrderBy(p => p.Delta)
             .ToList();
          }
        }
      }

      runners = runners.OrderBy(p => GetSortValue(p)).ThenBy(p => p.Name).ToList();

      return new GradeResult {
        Id = result.Class.Id.Value,
        Name = result.Class.Name,
        Codes = codes,
        Runners = runners,
        Course = new Course {
          Length = courseLength
        }
      };
    }

    private int GetSortValue(Runner p) {
      int baseOffset = 10000;

      if (p.Status == "OK") {
        if (Int32.TryParse(p.Position, out int position)) {
          return position;
        } else {
          return baseOffset - 1;
        }
      } else if (p.Status == "MP") {
        return baseOffset;
      } else if (p.Status == "DNF") {
        return baseOffset + 1;
      } else if (p.Status == "DSQ" || p.Status == "OT") {
        return baseOffset + 2;
      } else if (p.Status == "DNS") {
        return baseOffset + 3;
      } else {
        if (p.StartTime.HasValue) {
          return Convert.ToInt32((p.StartTime.Value - p.StartTime.Value.Date).TotalSeconds) + baseOffset + 4;
        }

        return baseOffset + 5 + (24 * 60 * 60);
      }
    }

    private string GetStatusCode(string status) {
      if (string.IsNullOrWhiteSpace(status)) {
        return "";
      }

      if (status == ResultStatus.Active.ToString()) {
        return "";
      }

      if (status == ResultStatus.Inactive.ToString()) {
        return "";
      }

      if (status == ResultStatus.OK.ToString()) {
        return "OK";
      }

      if (status == ResultStatus.MissingPunch.ToString()) {
        return "MP";
      }

      if (status == ResultStatus.DidNotFinish.ToString()) {
        return "DNF";
      }

      if (status == ResultStatus.DidNotStart.ToString()) {
        return "DNS";
      }

      if (status == ResultStatus.Disqualified.ToString()) {
        return "DSQ";
      }

      if (status == ResultStatus.OverTime.ToString()) {
        return "OT";
      }

      return String.Concat(status.Select(x => Char.IsUpper(x) ? " " + x : x.ToString())).TrimStart(' ');

    }

    private string GetPersonKey(string personId, string previousCode, string code, string nextCode) {
      return $"{personId}-{previousCode}-{code}-{nextCode}";
    }

    private string GetGenericKey(string previousCode, string code, string nextCode) {
      return $"{previousCode}-{code}-{nextCode}";
    }

    public GradeStart ConvertToGradeStart(ClassStart starts) {
      double? courseLength = null;

      if (starts.Course != null && starts.Course.Length > 0 && starts.Course[0].LengthSpecified) {
        courseLength = starts.Course[0].Length;
      }

      List<Runner> runners = new List<Runner>();

      if (starts.PersonStart != null) {
        runners = starts.PersonStart.Select(p => {
          var person = new Runner { };

          if (p.Person != null && p.Person.Name != null) {
            person.Name = (p.Person.Name.Given + " " + p.Person.Name.Family).Trim();

            var id = p.Person.Id?.FirstOrDefault()?.Value;

            person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? GetPersonId(starts.Class.Name, p) : id;
          }

          if (p.Organisation != null) {
            person.Club = p.Organisation.Name;
          }

          if (p.Start.Length < 0) {
            return person;
          }

          var cr = p.Start[0];

          if (cr.StartTimeSpecified) {
            person.StartTime = cr.StartTime.Date.AddSeconds((cr.StartTime - cr.StartTime.Date).TotalSeconds);
          }

          return person;
        }).ToList();
      }

      return new GradeStart {
        Id = starts.Class.Id.Value,
        Name = starts.Class.Name,
        Runners = runners,
        Course = new Course {
          Length = courseLength
        }
      };
    }

    private string GetPersonId(string grade, PersonStart p) {
      return GetPersonId(grade, p.Person, p.Organisation);
    }

    private string GetPersonId(string grade, PersonEntry p) {
      return GetPersonId(grade, p.Person, p.Organisation);
    }

    private string GetPersonId(string grade, PersonResult p) {
      return GetPersonId(grade, p.Person, p.Organisation);
    }

    private string GetPersonId(string grade, Person person, Organisation organisation) {
      return CalculateMD5Hash($"{grade} {person?.Name?.Family} {person?.Name?.Given} {organisation?.Name}");
    }

    public string CalculateMD5Hash(string input) {
      return String.Join("", MD5.Create().ComputeHash(Encoding.ASCII.GetBytes(input)).Select(p => p.ToString("X2")));
    }

    public List<GradeEntry> ConvertToGradeEntry(PersonEntry[] entries, List<Class> gradeLookup) {
      List<GradeEntry> returnValue = new List<GradeEntry>();

      var grades = entries.Where(p => p.Class.Length > 0).GroupBy(p => p.Class[0].Name);

      foreach (var grade in grades) {
        var gradeInfo = gradeLookup.FirstOrDefault(p => p.Name == grade.Key || p.ShortName == grade.Key);

        if (gradeInfo == null) {
          gradeInfo = grade.FirstOrDefault()?.Class.FirstOrDefault();
        }

        List<Runner> runners = new List<Runner>();

        runners = grade.Select(p => {
          var person = new Runner { };

          if (p.Person != null && p.Person.Name != null) {
            person.Name = (p.Person.Name.Given + " " + p.Person.Name.Family).Trim();

            var id = p.Person.Id?.FirstOrDefault()?.Value;

            person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? GetPersonId(grade.Key, p) : id;
          }

          if (p.Organisation != null) {
            person.Club = p.Organisation.Name;
          }

          return person;
        }).ToList();

        returnValue.Add(new GradeEntry {
          Id = gradeInfo.Id.Value,
          Name = gradeInfo.Name,
          Runners = runners,
          Course = new Course {
            Length = null
          }
        });
      }

      return returnValue;
    }
  }
}
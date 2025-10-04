using Microsoft.Extensions.Options;

namespace MB.OResults.Core;

public class AnalyzerService(ILogger<AnalyzerService> _Logger, IOptions<AnalyzerServiceConfiguration> _AnalyzerServiceConfiguration, IDistanceCalculator _DistanceCalculator) : IAnalyzerService {
  public GradeResult ConvertToGradeResult(ClassResult result, CourseInfo courseInfo) {
    string lastControl = null;
    List<LegData> codes = [];
    double? courseLength = null;
    DateTime? firstStart = null;
    Dictionary<string, double?> distanceLookUp = [];

    if (result.Course != null && result.Course.Length > 0 && result.Course[0].LengthSpecified) {
      courseLength = result.Course[0].Length;
    }

    if (result?.PersonResult?.Length > 0 && result?.PersonResult[0].Result?.Length > 0) {
      var r = result.PersonResult[0].Result[0];
      var splitTimes = r.SplitTime ?? [];
      lastControl = splitTimes.Where(p => p.Status != SplitTimeStatus.Additional).LastOrDefault()?.ControlCode;

      var splits = splitTimes.Where(p => p.Status != SplitTimeStatus.Additional).ToList();

      for (int i = 0; i < splits.Count; i++) {
        SplitTime split = splits[i];

        var c1 = courseInfo?.Controls?.FirstOrDefault(p => p.Id == (i == 0 ? Constants.StartCode : splits[i - 1].ControlCode));
        var c2 = courseInfo?.Controls?.FirstOrDefault(p => p.Id == split.ControlCode);
        double? distance = _DistanceCalculator.CalculateDistance(c1?.Lat, c1?.Lng, c2?.Lat, c2?.Lng);

        if (distance.HasValue) {
          distanceLookUp.TryAdd($"{c1.Id}-{c2.Id}", distance);
        }

        codes.Add(new() { Id = split.ControlCode, Distance = distance is null ? "" : $"{distance}m" });
      }

      var resultsWithStartTime = result.PersonResult.SelectMany(p => p.Result).Where(p => p.StartTimeSpecified).ToList();

      if (resultsWithStartTime != null && resultsWithStartTime.Count > 0) {
        firstStart = resultsWithStartTime.Min(p => p.StartTime);
      }

      //_Logger.LogInformation($"First start: {firstStart}.");
    }

    List<SplitTracking> splitTracking = [];
    Dictionary<string, Split> splitReference = [];
    Dictionary<string, List<double>> performanceTracking = [];

    List<Runner> runners = [];

    if (result.PersonResult != null) {
      runners = [.. result.PersonResult.Select(p => {
        var person = new Runner { };

        if (p.Person?.Name is not null) {
          person.FirstName = p.Person.Name.Given?.Trim();
          person.LastName = p.Person.Name.Family?.Trim();

          var id = p.Person.Id?.FirstOrDefault()?.Value;

          person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? p.GetPersonId(result.Class.Name) : id;
        }

        if (p.Organisation != null) {
          person.Club = p.Organisation.Name;
        }

        if (p.Result.Length < 0) {
          return person;
        }

        var cr = p.Result[0];
        var status = cr.Status.ToString();
        person.Position = cr.Position.ToInt();
        person.TimeInSeconds = cr.TimeSpecified ? cr.Time : (double?)null;

        if (cr.StartTimeSpecified) {
          person.StartTime = cr.StartTime.Date.AddSeconds((cr.StartTime - cr.StartTime.Date).TotalSeconds);
        }

        person.Status = PersonHelper.GetStatusCode(status);

        var splitTimes = cr.SplitTime != null ? cr.SplitTime.ToList() : [];
        var timeAtLastSplit = !String.IsNullOrWhiteSpace(lastControl) ? cr.SplitTime.Where(st => st.ControlCode == lastControl).FirstOrDefault()?.Time : null;
        var sprint = person.TimeInSeconds.HasValue && timeAtLastSplit.HasValue ? person.TimeInSeconds - timeAtLastSplit : null;
        splitTimes.Add(new IOF.XML.V3.SplitTime {
          ControlCode = Constants.FinishCode,
          TimeSpecified = sprint.HasValue,
          Time = person.TimeInSeconds ?? 0
        });

        person.Splits = [];

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
              _Logger.LogInformation($"{person.FirstName} {person.LastName} {person.Club} has a negative split time on leg between {previousCode} and {control.ControlCode}.");
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
                FirstName = person.FirstName,
                LastName = person.LastName
              });

              string performanceTrackingKey = GetGenericKey(splitTime.PreviousCode, splitTime.Code, splitTime.NextCode);

              if (!performanceTracking.TryGetValue(performanceTrackingKey, out List<double> _)) {
                performanceTracking[performanceTrackingKey] = [];
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
      })];
    }

    double totalAverageTime = 0;
    Dictionary<string, double> fastestSplit = [];
    Dictionary<string, double> fastestTotal = [];
    Dictionary<string, double> totalAverageTimeMapping = [];

    for (int i = 0; i <= codes.Count; i++) {
      var previousCode = i == 0 ? Constants.StartCode : codes[i - 1].Id;
      var code = i == codes.Count ? Constants.FinishCode : codes[i].Id;
      var nextCode = i + 1 == codes.Count ? Constants.FinishCode : i == codes.Count ? Constants.DownloadCode : codes[i + 1].Id;

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

        if (splitReference.TryGetValue(key, out Split split) && averageSplitTime.HasValue) {
          split.LegPosition = previousPlace;
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

        if (splitReference.TryGetValue(key, out Split value)) {
          value.TotalPosition = previousPlace;
        }
      }
    }

    Dictionary<string, List<PassingTime>> controlPassingLookup = [];

    foreach (var runner in runners) {
      if (runner.TimeInSeconds.HasValue && runner.Status == Constants.StatusOK) {
        runner.PerformanceIndex = totalAverageTime / runner.TimeInSeconds.Value;
      } else {
        double average = 0;
        double personal = 0;

        foreach (var split in runner.Splits) {
          var key = GetGenericKey(split.PreviousCode, split.Code, split.NextCode);
          if (split.Leg.HasValue && totalAverageTimeMapping.TryGetValue(key, out double value)) {
            average += value;
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

          if (split.PerformanceIndexAdjusted < _AnalyzerServiceConfiguration.Value.MistakeIndex && totalAverageTimeMapping.TryGetValue(key, out double average)) {
            split.PredictedLegTime = average / runner.PerformanceIndex;
            split.TimeLoss = split.Leg - split.PredictedLegTime;
          }
        }

        if (split.Leg.HasValue && fastestSplit.TryGetValue(key, out double value)) {
          split.LegTimeBehind = split.Leg.Value - value;
        }

        if (split.Total.HasValue && fastestTotal.TryGetValue(key, out double value2)) {
          split.TotalBehind = split.Total.Value - value2;
        }

        if (runner.StartTime.HasValue && split.Total.HasValue) {
          split.ActualTime = runner.StartTime.Value.Add(TimeSpan.FromSeconds(split.Total.Value));
          if (!controlPassingLookup.TryGetValue(key, out List<PassingTime> _)) {
            controlPassingLookup.Add(key, []);
          }

          controlPassingLookup[key].Add(new PassingTime {
            Time = split.ActualTime,
            FirstName = runner.FirstName,
            LastName = runner.LastName,
            Club = runner.Club
          });
        }

        if (distanceLookUp.TryGetValue($"{split.PreviousCode}-{split.Code}", out double? distance)) {
          if (distance.HasValue && split.Leg.HasValue) {
            split.KmRate = (split.Leg.Value / 60) / (distance.Value / 1000);
          }
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
        if (controlPassingLookup.TryGetValue(key, out List<PassingTime> value) && split.ActualTime.HasValue) {
          split.Pack = [.. value.Select(p => new RunnerDetails { FirstName = p.FirstName, LastName = p.LastName, Club = p.Club, Delta = (p.Time.Value - split.ActualTime.Value).TotalSeconds })
           .Where(p => Math.Abs(p.Delta) <= _AnalyzerServiceConfiguration.Value.Pack && $"{p.FirstName} {p.LastName} {p.Club}" != $"{runner.FirstName} {runner.LastName} {runner.Club}")
           .OrderBy(p => p.Delta)];
        }
      }
    }

    return new GradeResult {
      Id = result.Class.Id.Value,
      Name = result.Class.Name,
      Legs = codes,
      Runners = runners,
      Course = new Course {
        Length = courseLength
      }
    };
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

    List<Runner> runners = [];

    if (starts.PersonStart != null) {
      runners = [.. starts.PersonStart.Select(p => {
        var person = new Runner { };

        if (p.Person != null && p.Person.Name != null) {
          person.FirstName = p.Person.Name.Given?.Trim();
          person.LastName = p.Person.Name.Family?.Trim();

          var id = p.Person.Id?.FirstOrDefault()?.Value;

          person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? p.GetPersonId(starts.Class.Name) : id;
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
      })];
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

  public List<GradeEntry> ConvertToGradeEntry(PersonEntry[] entries, List<Class> gradeLookup) {
    List<GradeEntry> returnValue = [];

    var grades = entries.Where(p => p.Class.Length > 0).GroupBy(p => p.Class[0].Name);

    foreach (var grade in grades) {
      var gradeInfo = gradeLookup.FirstOrDefault(p => p.Name == grade.Key || p.ShortName == grade.Key) ?? (grade.FirstOrDefault()?.Class.FirstOrDefault());
      List<Runner> runners = [];

      runners = [.. grade.Select(p => {
        var person = new Runner { };

        if (p.Person != null && p.Person.Name != null) {
          person.FirstName = p.Person.Name.Given?.Trim();
          person.LastName = p.Person.Name.Family?.Trim();

          var id = p.Person.Id?.FirstOrDefault()?.Value;

          person.Id = String.IsNullOrWhiteSpace(id) | id == "0" ? p.GetPersonId(grade.Key) : id;
        }

        if (p.Organisation != null) {
          person.Club = p.Organisation.Name;
        }

        return person;
      })];

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

  public CourseInfo ConvertToControlData(CourseData courseData) {
    var controls = courseData?.RaceCourseData?.SelectMany(p => p.Control);

    if (controls is null) {
      return new();
    }

    return new() { Controls = [.. controls.Select(c => new ControlData { Lat = c.Position?.Lat, Lng = c.Position?.Lng, Id = c.Id.Value })] };
  }

  public EventStatistic AnalysEvent(List<GradeResult> results, CourseInfo courseInfo) {
    List<LegSplitStatistic> legs = [];

    foreach (var grade in results) {
      foreach (var runner in grade.Runners) {
        foreach (var leg in runner.Splits) {
          legs.Add(new LegSplitStatistic {
            Leg = runner.Splits.IndexOf(leg) + 1,
            ActualTime = leg.ActualTime,
            Club = runner.Club,
            Control = leg.Code,
            Grade = grade.Name,
            KmRate = leg.KmRate,
            LegTime = leg.Leg,
            FirstName = runner.FirstName,
            LastName = runner.LastName,
            NextControl = leg.NextCode,
            PerformanceIndex = leg.PerformanceIndex,
            PerformanceIndexAdjusted = leg.PerformanceIndexAdjusted,
            PredictedLegTime = leg.PredictedLegTime,
            PreviousControl = leg.PreviousCode,
            TimeLoss = leg.TimeLoss
          });
        }
      }
    }

    return new EventStatistic { Legs = legs };
  }
}
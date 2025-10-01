namespace MB.OResults.Core;

public interface IAnalyzerService {
  GradeResult ConvertToGradeResult(ClassResult result, CourseInfo courseInfo);
  GradeStart ConvertToGradeStart(ClassStart starts);
  List<GradeEntry> ConvertToGradeEntry(PersonEntry[] entry, List<Class> grades);
  CourseInfo ConvertToControlData(CourseData courseData);
  EventStatistic AnalysEvent(List<GradeResult> results, CourseInfo courseInfo);
}
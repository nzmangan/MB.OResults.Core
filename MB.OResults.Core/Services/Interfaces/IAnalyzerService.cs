using System.Collections.Generic;
using IOF.XML.V3;

namespace MB.OResults.Core {
  public interface IAnalyzerService {
    GradeResult ConvertToGradeResult(ClassResult result);
    GradeStart ConvertToGradeStart(ClassStart starts);
    List<GradeEntry> ConvertToGradeEntry(PersonEntry[] entry, List<Class> grades);
  }
}
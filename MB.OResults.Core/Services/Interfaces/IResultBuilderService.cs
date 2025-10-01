namespace MB.OResults.Core;

public interface IResultBuilderService {
  Task<RebuildResponse> Build();
}
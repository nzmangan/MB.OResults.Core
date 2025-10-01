namespace MB.OResults.Core;

public interface IResultService {
  Task<ResultList> Get();
}
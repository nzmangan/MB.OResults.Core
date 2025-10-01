namespace MB.OResults.Core;

public interface IEntryListService {
  Task<EntryList> Get();
}
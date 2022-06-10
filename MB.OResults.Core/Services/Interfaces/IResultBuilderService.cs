using System.Threading.Tasks;

namespace MB.OResults.Core {
  public interface IResultBuilderService {
    Task<RebuildResponse> Build();
  }
}
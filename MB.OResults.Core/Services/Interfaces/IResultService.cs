using System.Threading.Tasks;
using IOF.XML.V3;

namespace MB.OResults.Core {
  public interface IResultService {
    Task<ResultList> Get();
  }
}
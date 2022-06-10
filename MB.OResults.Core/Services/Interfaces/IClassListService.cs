using System.Threading.Tasks;
using IOF.XML.V3;

namespace MB.OResults.Core {
  public interface IClassListService {
    Task<ClassList> Get();
  }
}
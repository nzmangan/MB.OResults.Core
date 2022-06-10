using System;
using System.IO;
using System.Threading.Tasks;

namespace MB.OResults.Core;

public interface IXmlSerializerService {
  Task<T> Deserialize<T>(string path) where T : class;
  Task<T> Deserialize<T>(Func<Stream> streamGetter) where T : class;
  Task Serialize<T>(string path, T instance);
}
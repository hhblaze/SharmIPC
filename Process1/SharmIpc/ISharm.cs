using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tiesky.com
{
    public interface ISharm
    {
        string UsageReport();
        void AsyncAnswerOnRemoteCall(ulong msgId, Tuple<bool, byte[]> res);
        bool RemoteRequestWithoutResponse(byte[] args);
        Tuple<bool, byte[]> RemoteRequest(byte[] args, Action<Tuple<bool, byte[]>> callBack = null, int timeoutMs = 30000);
        Task<Tuple<bool, byte[]>> RemoteRequestAsync(byte[] args, int timeoutMs = 30000);
        void Dispose();
        int CommunicationProtocolVersion { get; }
    }
}

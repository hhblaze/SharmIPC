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
        void AsyncAnswerOnRemoteCall(ulong msgId, (bool, byte[]) res);
        bool RemoteRequestWithoutResponse(byte[] args);
        (bool, byte[]) RemoteRequest(byte[] args, Action<(bool, byte[])> callBack = null, int timeoutMs = 30000);
        Task<(bool, byte[])> RemoteRequestAsync(byte[] args, int timeoutMs = 30000);
        void Dispose();
        int CommunicationProtocolVersion { get; }
    }
}

using Akka.Actor;
using Akka.Remote.Transport;
using System;
using System.Threading.Tasks;

namespace Akka.Remote.LocalOnlyIPC;

public class IPCConnectionFailedException : Exception
{
    public IPCConnectionFailedException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

public interface ILocalOnlyIPC
{
    public string IPCSchemeIdentifier { get; }
    public string ConnectionName { get; }
    public int ConnectionNumber { get; }

    LocalOnlyIPCConnectionBase CreateOutboundConnection(
        Address remoteAddress);

    void PrepareInboundConnection(
        Task<IAssociationEventListener> associationEventListener);
}

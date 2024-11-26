using Akka.Actor;
using Akka.Remote.Transport;
using Google.Protobuf;
using System;
using System.Threading.Tasks;

namespace Akka.Remote.LocalOnlyIPC;

public class LocalOnlyIPCAssociationHandler :
    AssociationHandle
{
    private readonly LocalOnlyIPCConnectionBase connection;
    private readonly Task<IHandleEventListener> readHandler;

    public LocalOnlyIPCAssociationHandler(
        Address localAddress,
        Address remoteAddress,
        LocalOnlyIPCConnectionBase aConnection): base (localAddress, remoteAddress)
    { 
        connection = aConnection;
        readHandler = ReadHandlerSource.Task;

        _ = connection.ProcessIncomingData(readHandler);
    }
    
    [Obsolete]
    public override void Disassociate()
    {
        connection.CloseConnection();
    }

    public override bool Write(ByteString payload)
    {
        return connection.ProcessOutgoingData(readHandler, payload);
    }
}


public class ConnectionIOException : Exception
{
    public ConnectionIOException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public abstract class LocalOnlyIPCConnectionBase
{
    private readonly byte[] readBuffer;

    public Task ConnectionEstablished { get; }

    public LocalOnlyIPCConnectionBase(int maxTransferSize, Task connectedTask)
    { 
        readBuffer = new byte[maxTransferSize];
        ConnectionEstablished = connectedTask;
    }

    virtual public Task CloseConnection() => Task.CompletedTask;

    public async Task ProcessIncomingData(Task<IHandleEventListener> aReadHandler)
    {
        await ConnectionEstablished.ConfigureAwait(false);
        IHandleEventListener readHandler = await aReadHandler.ConfigureAwait(false);
        try
        {
            Array.Clear(readBuffer, 0, readBuffer.Length);

            int nrOfBytesRead = await ReadFromConnection(readBuffer);
            if (nrOfBytesRead == 0)
            {
                return;
            }

            ByteString payload = ByteString.CopyFrom(readBuffer, 0, nrOfBytesRead);
            readHandler.Notify(new InboundPayload(payload));

            _ = ProcessIncomingData(aReadHandler);
        }
        catch (ConnectionIOException)
        {
            readHandler.Notify(
                    new Disassociated(DisassociateInfo.Unknown));
        }
    }

    public bool ProcessOutgoingData(Task<IHandleEventListener> aReadHandler, ByteString payload)
    {
        if (ConnectionEstablished.Status == TaskStatus.RanToCompletion)
        {
            try
            {
                WriteToConnection(payload);                
                return true;
            }
            catch(ConnectionIOException)
            {
                if (aReadHandler.Status == TaskStatus.RanToCompletion)
                {
                    aReadHandler.Result.Notify(
                        new Disassociated(DisassociateInfo.Unknown));
                }
                return false;
            }
        }
        return false;
    }

    protected abstract Task<int> ReadFromConnection(byte[] buffer);

    protected abstract void WriteToConnection(ByteString payload);
}
using Akka.Actor;
using Akka.Remote.Transport;
using Google.Protobuf;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Akka.Remote.LocalOnlyIPC;

public class UnixDomainSocketIPC : ILocalOnlyIPC
{
    public string IPCSchemeIdentifier => "uds";

    public string ConnectionName { get; }

    public int ConnectionNumber { get; }

    private readonly Socket inboundSocket;
    private readonly int maximumTransferBytes;

    public UnixDomainSocketIPC(
        string ipcConnectionName,
        int ipcConnectionNumber,
        int maxTransferBytes)
    {
#if NETSTANDARD2_1_OR_GREATER
        if (string.IsNullOrEmpty(ipcConnectionName))
        {
            ipcConnectionName = Guid.NewGuid().ToString();
        }
        ConnectionName = ipcConnectionName;

        if (ipcConnectionNumber == 0)
        {
            ipcConnectionNumber = LocalOnlyIPCTransportHelper.GetFreeRandomPortForSchemeId(
                LocalOnlyIPCTransportHelper.HighestWellKnownConnectionNumber,
                LocalOnlyIPCTransportHelper.HighestAllowedConnectionNumber,
                IPCSchemeIdentifier,
                udsFileDoesNotExist);
        }
        ConnectionNumber = ipcConnectionNumber;

        string inboundUDSFilePath = createUDSFilePath(IPCSchemeIdentifier, ConnectionNumber);
        if (File.Exists(inboundUDSFilePath))
        {
            // these "files" get not deleted automagically when the process terminates unexpectedly :(
            File.Delete(inboundUDSFilePath);
        }
        EndPoint udsEndpoint = new UnixDomainSocketEndPoint(inboundUDSFilePath);
        inboundSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
        inboundSocket.ReceiveBufferSize = maximumTransferBytes;
        inboundSocket.Bind(udsEndpoint);
        inboundSocket.Listen(1);

        maximumTransferBytes = maxTransferBytes;
#else
        throw new NotImplementedException("Unix Domain Sockets need .NET Standard 2.1 or greater");
#endif
    }

    public LocalOnlyIPCConnectionBase CreateOutboundConnection(
        Address remoteAddress)
    {
#if NETSTANDARD2_1_OR_GREATER
        if (remoteAddress.Port == null)
        {
            throw new InvalidAssociationException($"{nameof(remoteAddress)}.Port must not be null.");
        }

        string remoteUDSPath = createUDSFilePath(
            IPCSchemeIdentifier,
            remoteAddress.Port.Value);

        TaskCompletionSource<Socket> socketConnectedTCS = 
            new TaskCompletionSource<Socket>();

        EndPoint ep = new UnixDomainSocketEndPoint(remoteUDSPath);
        Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
        socket.ReceiveBufferSize = maximumTransferBytes;

        socket.ConnectAsync(ep)
            .ContinueWith(_ => socketConnectedTCS.SetResult(socket));

        LocalOnlyIPCConnectionBase connection = new UnixDomainSocketIPCConnection(
            socketConnectedTCS.Task,
            maximumTransferBytes);

        return connection;
#else
        throw new NotImplementedException("Unix Domain Sockets need .NET Standard 2.1 or greater");
#endif
    }

    public void PrepareInboundConnection(
        Task<IAssociationEventListener> associationEventListener)
    {
        try
        {
            inboundSocket.AcceptAsync()
                .ContinueWith(incomingConnectionTask =>
                    processIncomingConnection(incomingConnectionTask, associationEventListener));
        }
        catch (SocketException ex)
        {
            throw new IPCConnectionFailedException(
                    "Accepting new connection failed", ex);
        }
    }

    private async Task processIncomingConnection(
        Task<Socket> connectionTask,
        Task<IAssociationEventListener> associationEventListenerTask)
    {
        await connectionTask.ConfigureAwait(false);

        IAssociationEventListener listener =
            await associationEventListenerTask.ConfigureAwait(false);

        LocalOnlyIPCConnectionBase inboundConnection = new UnixDomainSocketIPCConnection(
            connectionTask,
            maximumTransferBytes);

        LocalOnlyIPCAssociationHandler inboundHandler = new LocalOnlyIPCAssociationHandler(
            new Address(IPCSchemeIdentifier, ""),
            new Address(IPCSchemeIdentifier, ""),
            inboundConnection);

        listener.Notify(new InboundAssociation(inboundHandler));

        PrepareInboundConnection(associationEventListenerTask);
    }

    private static bool udsFileDoesNotExist(string schemeIdentifier, int port)
    {
        bool fileExists = Directory
            .GetFiles(Path.GetTempPath())
            .Contains(createUDSFileName(schemeIdentifier, port));

        return !fileExists;
    }

    private static string createUDSFileName(string schemeIdentifier, int port)
        => $"akka.{schemeIdentifier}.{port}.tmp";

    private static string createUDSFilePath(string schemeIdentifier, int port)
        => Path.Combine(
            Path.GetTempPath(),
            createUDSFileName(schemeIdentifier, port));
}




public class UnixDomainSocketIPCConnection 
    : LocalOnlyIPCConnectionBase
{
    private readonly Task<Socket> connectionTask;

    public UnixDomainSocketIPCConnection(
            Task<Socket> socketConnectedTask,
            int maxTransferSize): 
        base (maxTransferSize, socketConnectedTask)
    { 
        connectionTask = socketConnectedTask;
    }


    public override Task CloseConnection()
    {
        if (connectionTask.Status == TaskStatus.RanToCompletion)
        { 
            connectionTask.Result.Close();
        }
        return base.CloseConnection();
    }

    protected override async Task<int> ReadFromConnection(byte[] buffer)
    {
        try
        {
            int bytes = await connectionTask.Result.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                SocketFlags.None);
            
            return bytes;
        }
        catch (SocketException e)
        {
            throw new ConnectionIOException("Reading from socket failed", e);
        }
    }

    protected override void WriteToConnection(ByteString payload)
    {
        try
        {
            connectionTask.Result.Send(payload.ToArray());
        }
        catch (SocketException e)
        {
            throw new ConnectionIOException("Writing to socket failed", e);
        }
    }
}
using Akka.Actor;
using Akka.Remote.Transport;
using Google.Protobuf;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Remote.LocalOnlyIPC;

public class NamedPipeIPC : ILocalOnlyIPC
{
    private readonly string inboundPipeName;
    private readonly int maximumTransferBytes;
    private readonly SemaphoreSlim pipeCountSemaphore;
    private readonly int maxPipeCount = 100;

    public string IPCSchemeIdentifier => "pipe";

    public string ConnectionName { get; }

    public int ConnectionNumber { get; }

    public NamedPipeIPC(
        string ipcConnectionName, 
        int ipcConnectionNumber, 
        int maxTransferBytes)
    {
        if (string.IsNullOrEmpty(ipcConnectionName))
        { 
            ipcConnectionName = Guid.NewGuid().ToString();
        }
        ConnectionName = ipcConnectionName;

        if (ipcConnectionNumber == 0)
        {
            ipcConnectionNumber =
                LocalOnlyIPCTransportHelper.GetFreeRandomPortForSchemeId(
                    LocalOnlyIPCTransportHelper.HighestWellKnownConnectionNumber,
                    LocalOnlyIPCTransportHelper.HighestAllowedConnectionNumber,
                    IPCSchemeIdentifier,
                    pipeNameDoesNotExist);

        }
        ConnectionNumber = ipcConnectionNumber;

        inboundPipeName = createPipeName(
            IPCSchemeIdentifier, 
            ConnectionNumber);

        maximumTransferBytes = maxTransferBytes;

        pipeCountSemaphore = new SemaphoreSlim(maxPipeCount, maxPipeCount);
    }

    public LocalOnlyIPCConnectionBase CreateOutboundConnection(
        Address remoteAddress)
    {
        if (remoteAddress.Port == null)
        {
            throw new InvalidAssociationException($"{nameof(remoteAddress)}.Port must not be null.");
        }
        string outboundPipeName = createPipeName(
                remoteAddress.Protocol,
                remoteAddress.Port.Value);

        TaskCompletionSource<PipeStream> pipeConnectedTCS =
            new TaskCompletionSource<PipeStream>();

        NamedPipeClientStream outboundStream = new NamedPipeClientStream(
            ".",
            outboundPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        outboundStream.ConnectAsync()
            .ContinueWith(_ => pipeConnectedTCS.SetResult(outboundStream));

        LocalOnlyIPCConnectionBase connection = new NamedPipeIPCConnection(
            pipeConnectedTCS.Task,
            maximumTransferBytes);

        return connection;
    }

    public async void PrepareInboundConnection(
        Task<IAssociationEventListener> associationEventListener)
    {
        await pipeCountSemaphore.WaitAsync();

        int tries = 0;
        const int maxConnectionTries = 5;
        do
        {
            try
            {
                NamedPipeServerStream inboundStream = new NamedPipeServerStream(
                    inboundPipeName,
                    PipeDirection.InOut,
                    maxPipeCount,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    maximumTransferBytes,
                    maximumTransferBytes);

                _ = inboundStream.WaitForConnectionAsync()
                    .ContinueWith(_ =>
                        processInboundConnection(
                            Task.FromResult<PipeStream>(inboundStream),
                            associationEventListener));

                return;
            }
            catch (IOException ex)
            {
                if (tries < maxConnectionTries)
                {
                    tries++;
                    await Task.Delay(100);
                }
                else
                {
                    throw new IPCConnectionFailedException(
                        "Creating the specified pipe failed",
                        ex);
                }
            }
        } while (tries < 5);
    }

    private async Task processInboundConnection(
        Task<PipeStream> pipeConnectionTask,
        Task<IAssociationEventListener> associationEventListenerTask)
    {
        await pipeConnectionTask.ConfigureAwait(false);

        IAssociationEventListener listener =
            await associationEventListenerTask.ConfigureAwait(false);


        LocalOnlyIPCConnectionBase inboundConnection = new NamedPipeIPCConnection(
            pipeConnectionTask,
            maximumTransferBytes,
            allowNewConnection);

        LocalOnlyIPCAssociationHandler inboundHandler = new LocalOnlyIPCAssociationHandler(
            new Address(IPCSchemeIdentifier, ""),
            new Address(IPCSchemeIdentifier, ""),
            inboundConnection);

        listener.Notify(new InboundAssociation(inboundHandler));

        PrepareInboundConnection(associationEventListenerTask);
    }


    private static bool pipeNameDoesNotExist(string schemeIdentifier, int port)
    {
        string aPipeName = createPipeName(schemeIdentifier, port);

        bool pipeExists = System.IO.Directory
            .GetFiles("\\\\.\\pipe\\")
            .Contains($"\\\\.\\pipe\\{aPipeName}");

        return !pipeExists;
    }

    private static string createPipeName(string scheme, int port)
        => $"akka.{scheme}:{port}";


    private void allowNewConnection()
    {
        pipeCountSemaphore.Release();
    }
}


public class NamedPipeIPCConnection: LocalOnlyIPCConnectionBase
{
    private readonly Task<PipeStream> connectionTask;
    private readonly Action? onConnectionClosed;

    public NamedPipeIPCConnection(
            Task<PipeStream> pipeConnectedTask,
            int maxTransferSize,
            Action? onConnectionClosed = null) 
        : base(maxTransferSize, pipeConnectedTask)
    {
        connectionTask = pipeConnectedTask;
        this.onConnectionClosed = onConnectionClosed;
    }

    public override Task CloseConnection()
    {
        if (connectionTask.Status == TaskStatus.RanToCompletion)
        {
            connectionTask.Result.Dispose();
            onConnectionClosed?.Invoke();
        }
        return base.CloseConnection();
    }

    protected override async Task<int> ReadFromConnection(byte[] buffer)
    {
        try
        {
            int bytes = await connectionTask.Result.ReadAsync(
                buffer,
                0,
                buffer.Length);

            return bytes;
        }
        catch (IOException e)
        {
            throw new ConnectionIOException("Reading from pipe failed", e);
        }
    }

    protected override void WriteToConnection(ByteString payload)
    {
        try
        {
            payload.WriteTo(connectionTask.Result);
        }
        catch (IOException e)
        {
            throw new ConnectionIOException("Writing to pipe failed", e);
        }
    }
}

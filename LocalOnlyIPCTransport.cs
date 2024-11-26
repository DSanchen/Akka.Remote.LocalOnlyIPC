using Akka.Actor;
using Akka.Remote.Transport;
using System;
using System.Threading.Tasks;

namespace Akka.Remote.LocalOnlyIPC;

public class LocalOnlyIPCTransport: Transport.Transport
{
    private readonly TaskCompletionSource<IAssociationEventListener>
        _associationEventListener;

    private readonly Address localAddress;
    private readonly ILocalOnlyIPC localOnlyIPC;

    public LocalOnlyIPCTransport(
            ActorSystem actorSystem,
            Akka.Configuration.Config config) :
        this(
            actorSystem.Name,
            config.GetString("connection-name"),
            config.GetInt("connection-number"),
            config.GetString("scheme-identifier"),
            config.GetByteSize("maximum-payload-bytes", null) ?? 32000L)
    {
    }

    private LocalOnlyIPCTransport(
        string actorSystemName,
        string connectionName,
        int connectionNumber,
        string schemeIdentifier,
        long maximumPayloadBytes)
    {
        localOnlyIPC = schemeIdentifier.ToLowerInvariant() switch
        {
            "pipe" => new NamedPipeIPC(connectionName, connectionNumber, (int)maximumPayloadBytes),
            "uds" => new UnixDomainSocketIPC(connectionName, connectionNumber, (int)maximumPayloadBytes),
            _ => throw new NotImplementedException($"no Implementation for Local-Only IPC \"{schemeIdentifier}\"")
        };

        SchemeIdentifier = localOnlyIPC.IPCSchemeIdentifier;
        MaximumPayloadBytes = (int)maximumPayloadBytes;

        localAddress = new Address(
            SchemeIdentifier,
            actorSystemName,
            localOnlyIPC.ConnectionName,
            localOnlyIPC.ConnectionNumber);

        _associationEventListener = new TaskCompletionSource<IAssociationEventListener>();
    }

    public override Task<AssociationHandle> Associate(Address remoteAddress)
    {
        LocalOnlyIPCConnectionBase connection =
            localOnlyIPC.CreateOutboundConnection(remoteAddress);

        AssociationHandle handle =
            new LocalOnlyIPCAssociationHandler(
                localAddress,
                remoteAddress,
                connection);

        return Task.FromResult(handle);
    }

    public override bool IsResponsibleFor(Address remote) => true;

    public override Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
    {
        try
        {
            localOnlyIPC.PrepareInboundConnection(
                _associationEventListener.Task);
        }
        catch (IPCConnectionFailedException ex)
        {
            System.Log.Log(
                Akka.Event.LogLevel.ErrorLevel,
                ex,
                "can not offer new inbound connection");

            // what do we do here ??
        }

        return Task.FromResult((localAddress, _associationEventListener));
    }

    public override Task<bool> Shutdown()
    {
        return Task.FromResult(true);
    }

}

using Akka.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Remote.LocalOnlyIPC;

public static class LocalOnlyIPCTransportHelper
{
    public const int HighestWellKnownConnectionNumber = 32768;
    public const int HighestAllowedConnectionNumber = 65536;

    public static AkkaConfigurationBuilder WithLocalOnlyIPCRemoting(
        this AkkaConfigurationBuilder builder,
        LocalOnlyRemotingOptions options)
    {
        options.Build(builder);
        return builder;
    }

    internal static int GetFreeRandomPortForSchemeId(
        int lowerBoundNr,
        int upperBoundNr,
        string schemeIdentifier,
        Func<string, int, bool> portIsUsable)
    {
        Random randomPortGenerator = new Random();

        List<int> availablePorts = Enumerable
            .Range(lowerBoundNr, upperBoundNr - lowerBoundNr)
            .ToList();

        int port;
        do
        {
            if (availablePorts.Count == 0)
            {
                throw new Exception($"No more free ports available for transport {schemeIdentifier}");
            }

            port = randomPortGenerator.Next(lowerBoundNr, upperBoundNr);
            availablePorts.Remove(port);
        }
        while (portIsUsable(schemeIdentifier, port) == false);
        return port;
    }
}
using Akka.Hosting;
using System.Text;

namespace Akka.Remote.LocalOnlyIPC;

public record LocalOnlyRemotingOptions(
    string ConnectionName,
    int ConnectionNumber,
    string SchemeIdentifier,
    int MaximumPayloadBytes = 4*1024*1024)
{
    internal void Build(AkkaConfigurationBuilder builder)
    { 
        StringBuilder sb = new StringBuilder();
        Build(sb);

        if (sb.Length > 0)
        {
            builder.AddHocon(sb.ToString(), HoconAddMode.Append);
        }
    }

    private void Build(StringBuilder sb)
    {
        sb.AppendLine("akka.actor.provider = remote");
        sb.AppendLine("akka.remote {");
        sb.AppendLine("enabled-transports = [ akka.remote.LocalOnlyIPC ]");

        sb.AppendLine("LocalOnlyIPC {");

        sb.AppendLine($"transport-class = " +
            $"\" {typeof(LocalOnlyIPCTransport).FullName}, " +
            $"{typeof(LocalOnlyIPCTransport).Assembly.GetName().Name}\"");

        if (!string.IsNullOrWhiteSpace(ConnectionName))
            sb.AppendLine($"connection-name = {ConnectionName}");

        sb.AppendLine($"connection-number = {ConnectionNumber}");

        if (!string.IsNullOrWhiteSpace(SchemeIdentifier))
            sb.AppendLine($"scheme-identifier = {SchemeIdentifier}");

        sb.AppendLine($"maximum-payload-bytes = {MaximumPayloadBytes}");
        
        sb.AppendLine("}");

        sb.Append("}");

    }
}

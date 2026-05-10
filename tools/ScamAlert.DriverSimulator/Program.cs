using System.Text.Json;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;

const int UnprotectedPortExitCode = 2;
const int PipeProtocolFailureExitCode = 3;

var sourceIp = GetArgument("--ip", "203.0.113.10");
var port = int.Parse(GetArgument("--port", "3389"));

if (!ProtectedServiceMap.TryFromPort(port, out var service))
{
    Console.Error.WriteLine($"Port {port} is not protected by ScamAlert.");
    return UnprotectedPortExitCode;
}

var attempt = new ProtectedConnectionAttempt(
    Guid.NewGuid(),
    DateTimeOffset.UtcNow,
    sourceIp,
    port,
    service);

var client = new BrokerDriverPipeClient();

try
{
    var response = await client.SendAttemptAsync(attempt);
    Console.WriteLine(JsonSerializer.Serialize(response, SignalJson.Options));
    return 0;
}
catch (BrokerPipeProtocolException ex)
{
    Console.Error.WriteLine($"Broker pipe protocol failed: {ex.Message}");
    return PipeProtocolFailureExitCode;
}

string GetArgument(string name, string fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return fallback;
}

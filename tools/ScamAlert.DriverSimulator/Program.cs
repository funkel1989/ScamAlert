using System.IO.Pipes;
using System.Text;
using System.Text.Json;
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

await using var pipe = new NamedPipeClientStream(
    ".",
    "scamalert-driver-events",
    PipeDirection.InOut,
    PipeOptions.Asynchronous);

try
{
    await pipe.ConnectAsync(3000);

    using var protocolTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    using var reader = new StreamReader(
        pipe,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false,
        leaveOpen: true);

    await using var writer = new StreamWriter(
        pipe,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        leaveOpen: true)
    {
        AutoFlush = true
    };

    await writer.WriteLineAsync(JsonSerializer.Serialize(attempt, SignalJson.Options).AsMemory(), protocolTimeout.Token);

    var responseLine = await reader.ReadLineAsync(protocolTimeout.Token);
    if (string.IsNullOrWhiteSpace(responseLine))
    {
        Console.Error.WriteLine("Broker pipe closed without a response.");
        return PipeProtocolFailureExitCode;
    }

    var response = JsonSerializer.Deserialize<DriverDecisionResponse>(responseLine, SignalJson.Options);
    if (response is null)
    {
        Console.Error.WriteLine("Broker pipe returned an invalid response.");
        return PipeProtocolFailureExitCode;
    }

    if (response.ObservedEventId != attempt.EventId)
    {
        Console.Error.WriteLine("Broker pipe returned a response for a different event.");
        return PipeProtocolFailureExitCode;
    }

    Console.WriteLine(JsonSerializer.Serialize(response, SignalJson.Options));
    return 0;
}
catch (TimeoutException)
{
    Console.Error.WriteLine("Broker pipe is unavailable.");
    return PipeProtocolFailureExitCode;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"Broker pipe protocol failed: {ex.Message}");
    return PipeProtocolFailureExitCode;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Broker pipe response timed out.");
    return PipeProtocolFailureExitCode;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Broker pipe returned malformed JSON: {ex.Message}");
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

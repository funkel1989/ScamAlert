using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

var sourceIp = GetArgument("--ip", "203.0.113.10");
var port = int.Parse(GetArgument("--port", "3389"));

if (!ProtectedServiceMap.TryFromPort(port, out var service))
{
    Console.Error.WriteLine($"Port {port} is not protected by ScamAlert.");
    return 2;
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

await pipe.ConnectAsync(3000);

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

await writer.WriteLineAsync(JsonSerializer.Serialize(attempt, SignalJson.Options));

var responseLine = await reader.ReadLineAsync();
var response = responseLine is null
    ? null
    : JsonSerializer.Deserialize<DriverDecisionResponse>(responseLine, SignalJson.Options);

Console.WriteLine(JsonSerializer.Serialize(response, SignalJson.Options));
return 0;

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

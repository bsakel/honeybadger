using System.IO.Pipes;
using System.Text.Json;
using Honeybadger.Core.Models;
using Spectre.Console;

var groupName = args.Length > 1 && args[0] == "--group" ? args[1] : "main";

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AnsiConsole.MarkupLine($"[bold cyan]Honeybadger Chat[/] [dim](group: {groupName})[/]");

NamedPipeClientStream? pipe = null;
StreamReader? reader = null;
StreamWriter? writer = null;

try
{
    // Connect to service with 3 retries, 5 second timeout per attempt
    const int maxRetries = 3;
    const int timeoutSeconds = 5;
    bool connected = false;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            AnsiConsole.MarkupLine($"[dim]Connecting to service... (attempt {attempt}/{maxRetries})[/]");

            pipe?.Dispose();
            pipe = new NamedPipeClientStream(".", "honeybadger-chat", PipeDirection.InOut, PipeOptions.Asynchronous);

            var connectTask = pipe.ConnectAsync(cts.Token);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutSeconds * 1000, cts.Token)) != connectTask)
            {
                if (attempt < maxRetries)
                {
                    AnsiConsole.MarkupLine($"[yellow]Connection timeout. Retrying...[/]");
                    await Task.Delay(1000, cts.Token); // Wait 1 second before retry
                    continue;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Could not connect to Honeybadger service (timeout after {timeoutSeconds}s, {maxRetries} attempts)");
                    AnsiConsole.MarkupLine("[dim]Make sure the service is running: dotnet run --project src/Honeybadger.Console[/]");
                    return 1;
                }
            }

            await connectTask; // Rethrow any exception
            connected = true;
            break;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxRetries)
        {
            AnsiConsole.MarkupLine($"[yellow]Connection failed: {ex.Message}. Retrying...[/]");
            await Task.Delay(1000, cts.Token); // Wait 1 second before retry
        }
    }

    if (!connected || pipe == null)
    {
        AnsiConsole.MarkupLine("[red]Error:[/] Failed to connect to Honeybadger service after multiple attempts");
        AnsiConsole.MarkupLine("[dim]Make sure the service is running: dotnet run --project src/Honeybadger.Console[/]");
        return 1;
    }

    reader = new StreamReader(pipe);
    writer = new StreamWriter(pipe) { AutoFlush = true };

    // Send registration message
    var registerMsg = new PipeMessage
    {
        Type = PipeMessage.Types.Register,
        GroupName = groupName
    };
    await writer.WriteLineAsync(JsonSerializer.Serialize(registerMsg));

    AnsiConsole.MarkupLine("[green]Connected![/] Type your messages below. Press Ctrl+C to exit.\n");

    // Run render and input loops concurrently
    var renderTask = RenderLoopAsync(reader, groupName, cts.Token);
    var inputTask = InputLoopAsync(writer, groupName, cts.Token);

    await Task.WhenAny(renderTask, inputTask);
    cts.Cancel();

    try
    {
        await Task.WhenAll(renderTask, inputTask);
    }
    catch (OperationCanceledException)
    {
        // Expected during shutdown
    }

    return 0;
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
    return 1;
}
finally
{
    writer?.Dispose();
    reader?.Dispose();
    pipe?.Dispose();
}

static async Task RenderLoopAsync(StreamReader reader, string groupName, CancellationToken ct)
{
    bool isThinking = false;

    while (!ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(ct);
        if (string.IsNullOrEmpty(line))
            break; // Service disconnected

        var msg = JsonSerializer.Deserialize<PipeMessage>(line);
        if (msg is null || msg.GroupName != groupName)
            continue;

        switch (msg.Type)
        {
            case PipeMessage.Types.ThinkingShow:
                isThinking = true;
                AnsiConsole.MarkupLine("[dim]Agent is thinking...[/]");
                break;

            case PipeMessage.Types.ThinkingHide:
                if (isThinking)
                {
                    isThinking = false;
                }
                break;

            case PipeMessage.Types.StreamChunk:
                if (!string.IsNullOrEmpty(msg.Chunk))
                    AnsiConsole.Markup($"[green]{msg.Chunk.EscapeMarkup()}[/]");
                break;

            case PipeMessage.Types.StreamDone:
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();
                break;

            case PipeMessage.Types.AgentMessage:
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    var color = msg.IsFromAgent ? "green" : "yellow";
                    AnsiConsole.MarkupLine($"[{color}]{msg.Content.EscapeMarkup()}[/]");
                    AnsiConsole.WriteLine();
                }
                break;
        }
    }
}

static async Task InputLoopAsync(StreamWriter writer, string groupName, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        AnsiConsole.Markup("[bold blue]You:[/] ");
        var input = await Console.In.ReadLineAsync(ct);

        if (string.IsNullOrWhiteSpace(input))
            continue;

        var msg = new PipeMessage
        {
            Type = PipeMessage.Types.UserMessage,
            GroupName = groupName,
            Content = input,
            Sender = "user"
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(msg));
    }
}

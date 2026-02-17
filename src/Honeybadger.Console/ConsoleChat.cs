using System.Threading.Channels;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Honeybadger.Console;

/// <summary>
/// Console-based IChatFrontend using Spectre.Console for rich rendering.
/// Reads user input from stdin on a background thread; renders responses as Panels.
/// </summary>
internal sealed class ConsoleChat : IChatFrontend, IDisposable
{
    private readonly Channel<ChatMessage> _incoming = Channel.CreateUnbounded<ChatMessage>();
    private readonly ILogger<ConsoleChat> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _inputTask;

    // Guards Spectre.Console output so thinking indicator and responses don't interleave.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _thinkingActive;

    public ConsoleChat(ILogger<ConsoleChat> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        // Cancel input loop and complete the channel when the host begins shutting down.
        lifetime.ApplicationStopping.Register(() =>
        {
            _cts.Cancel();
            _incoming.Writer.TryComplete();
        });
        _inputTask = Task.Run(ReadInputLoopAsync);
    }

    public ChannelReader<ChatMessage> IncomingMessages => _incoming.Reader;

    public async Task SendToUserAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending response to user (sender={Sender})", message.Sender);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            AnsiConsole.Write(new Rule());

            if (message.IsFromAgent)
            {
                var panel = new Panel(new Markup(Markup.Escape(message.Content)))
                {
                    Header = new PanelHeader($"[bold green] {message.Sender} [/]"),
                    Border = BoxBorder.Rounded,
                    Expand = true
                };
                AnsiConsole.Write(panel);
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold cyan]{Markup.Escape(message.Sender)}:[/] {Markup.Escape(message.Content)}");
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ShowAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!_thinkingActive)
            {
                AnsiConsole.MarkupLine($"[dim grey]  ⠋ [[{Markup.Escape(groupName)}]] thinking...[/]");
                _thinkingActive = true;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task HideAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _thinkingActive = false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendStreamChunkAsync(string groupName, string chunk, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Stream chunk ({Length} chars)", chunk.Length);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Write chunk directly without newline (streaming mode)
            AnsiConsole.Write(chunk);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendStreamCompleteAsync(string groupName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stream complete for group {Group}", groupName);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // End the streaming line
            AnsiConsole.WriteLine();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadInputLoopAsync()
    {
        try
        {
            AnsiConsole.MarkupLine("[bold]Honeybadger[/] [dim]— type a message and press Enter. Ctrl+C to quit.[/]");
            AnsiConsole.Write(new Rule());

            while (!_cts.Token.IsCancellationRequested)
            {
                AnsiConsole.Markup("[bold cyan]you>[/] ");
                // ReadLineAsync supports cancellation on .NET 7+ console streams.
                var line = await System.Console.In.ReadLineAsync(_cts.Token);

                if (line is null)
                    break;

                line = line.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                _logger.LogDebug("User input received ({Length} chars)", line.Length);

                await _incoming.Writer.WriteAsync(new ChatMessage
                {
                    GroupName = "main",
                    Content = line,
                    Sender = "user"
                }, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConsoleChat input loop error");
        }
        finally
        {
            _incoming.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}

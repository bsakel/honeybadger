using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Ipc;

/// <summary>
/// Named-pipe based chat frontend. Allows multiple chat clients (one per group) to connect
/// to the headless service via "honeybadger-chat" pipe.
/// </summary>
public class NamedPipeChatFrontend : IChatFrontend, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<NamedPipeChatFrontend> _logger;
    private readonly Channel<ChatMessage> _incoming = Channel.CreateUnbounded<ChatMessage>();
    private readonly Dictionary<string, StreamWriter> _groupClients = new();
    private readonly SemaphoreSlim _clientsLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public ChannelReader<ChatMessage> IncomingMessages => _incoming.Reader;

    public NamedPipeChatFrontend(ILogger<NamedPipeChatFrontend> logger)
    {
        _logger = logger;
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Named pipe chat frontend listening on 'honeybadger-chat'");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    "honeybadger-chat",
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                _logger.LogDebug("Client connected to named pipe");

                // Fire-and-forget client handler
                _ = Task.Run(() => HandleClientAsync(pipe, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in named pipe listen loop");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        string? groupName = null;
        StreamWriter? writer = null;

        try
        {
            var reader = new StreamReader(pipe);
            writer = new StreamWriter(pipe) { AutoFlush = true };

            // Read registration message
            var firstLine = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(firstLine))
            {
                _logger.LogWarning("Client disconnected before sending register message");
                return;
            }

            var registerMsg = JsonSerializer.Deserialize<PipeMessage>(firstLine, JsonOpts);
            if (registerMsg?.Type != PipeMessage.Types.Register || string.IsNullOrEmpty(registerMsg.GroupName))
            {
                _logger.LogWarning("Invalid register message from client");
                return;
            }

            groupName = registerMsg.GroupName;
            _logger.LogInformation("Client registered for group '{Group}'", groupName);

            // Register client writer (last-writer-wins for same group)
            await _clientsLock.WaitAsync(ct);
            try
            {
                _groupClients[groupName] = writer;
            }
            finally
            {
                _clientsLock.Release();
            }

            // Read user messages from client
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line))
                    break; // Client disconnected

                var msg = JsonSerializer.Deserialize<PipeMessage>(line, JsonOpts);
                if (msg?.Type == PipeMessage.Types.UserMessage && !string.IsNullOrEmpty(msg.Content))
                {
                    await _incoming.Writer.WriteAsync(new ChatMessage
                    {
                        GroupName = groupName,
                        Content = msg.Content,
                        Sender = msg.Sender ?? "user",
                        IsFromAgent = false
                    }, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling named pipe client for group '{Group}'", groupName ?? "(unknown)");
        }
        finally
        {
            // Cleanup: remove client from registry
            if (groupName is not null)
            {
                await _clientsLock.WaitAsync(CancellationToken.None);
                try
                {
                    if (_groupClients.TryGetValue(groupName, out var existingWriter) && existingWriter == writer)
                        _groupClients.Remove(groupName);
                }
                finally
                {
                    _clientsLock.Release();
                }

                _logger.LogInformation("Client disconnected from group '{Group}'", groupName);
            }

            writer?.Dispose();
            pipe.Dispose();
        }
    }

    private async Task SendToGroupAsync(string groupName, PipeMessage message, CancellationToken ct)
    {
        await _clientsLock.WaitAsync(ct);
        StreamWriter? writer;
        try
        {
            if (!_groupClients.TryGetValue(groupName, out writer))
                return; // No client connected for this group
        }
        finally
        {
            _clientsLock.Release();
        }

        try
        {
            var json = JsonSerializer.Serialize(message, JsonOpts);
            await writer.WriteLineAsync(json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error sending to group '{Group}' (client may have disconnected)", groupName);
        }
    }

    public Task SendToUserAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        return SendToGroupAsync(message.GroupName, new PipeMessage
        {
            Type = PipeMessage.Types.AgentMessage,
            GroupName = message.GroupName,
            Content = message.Content,
            Sender = message.Sender,
            IsFromAgent = message.IsFromAgent
        }, cancellationToken);
    }

    public Task ShowAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return SendToGroupAsync(groupName, new PipeMessage
        {
            Type = PipeMessage.Types.ThinkingShow,
            GroupName = groupName
        }, cancellationToken);
    }

    public Task HideAgentThinkingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return SendToGroupAsync(groupName, new PipeMessage
        {
            Type = PipeMessage.Types.ThinkingHide,
            GroupName = groupName
        }, cancellationToken);
    }

    public Task SendStreamChunkAsync(string groupName, string chunk, CancellationToken cancellationToken = default)
    {
        return SendToGroupAsync(groupName, new PipeMessage
        {
            Type = PipeMessage.Types.StreamChunk,
            GroupName = groupName,
            Chunk = chunk
        }, cancellationToken);
    }

    public Task SendStreamCompleteAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return SendToGroupAsync(groupName, new PipeMessage
        {
            Type = PipeMessage.Types.StreamDone,
            GroupName = groupName
        }, cancellationToken);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _clientsLock.Dispose();
        _incoming.Writer.Complete();
    }
}

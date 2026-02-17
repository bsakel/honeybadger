using System.Text.Json;
using FluentAssertions;
using Honeybadger.Core.Models;
using Honeybadger.Host.Ipc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honeybadger.Integration.Tests.Ipc;

public class FileBasedIpcTransportTests : IDisposable
{
    private readonly string _tempDir;

    public FileBasedIpcTransportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hb-ipc-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Transport_PicksUpJsonFile_AndDeserializes()
    {
        var transport = new FileBasedIpcTransport(_tempDir, NullLogger<FileBasedIpcTransport>.Instance);
        var received = new List<IpcMessage>();

        await transport.StartAsync(msg =>
        {
            received.Add(msg);
            return Task.CompletedTask;
        });

        // Simulate agent writing an IPC file
        var message = new IpcMessage
        {
            Type = IpcMessageType.SendMessage,
            GroupName = "main",
            Payload = JsonSerializer.Serialize(new SendMessagePayload { Content = "Hello from agent" })
        };
        var filePath = Path.Combine(_tempDir, $"{message.Id}.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(message));

        // Poll picks it up within ~1 second
        await Task.Delay(800);

        await transport.StopAsync();

        received.Should().HaveCount(1);
        received[0].Type.Should().Be(IpcMessageType.SendMessage);
        received[0].GroupName.Should().Be("main");
    }

    [Fact]
    public async Task Transport_DeletesFileAfterProcessing()
    {
        var transport = new FileBasedIpcTransport(_tempDir, NullLogger<FileBasedIpcTransport>.Instance);
        await transport.StartAsync(_ => Task.CompletedTask);

        var message = new IpcMessage { Type = IpcMessageType.ListTasks, GroupName = "main" };
        var filePath = Path.Combine(_tempDir, $"{message.Id}.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(message));

        await Task.Delay(800);
        await transport.StopAsync();

        File.Exists(filePath).Should().BeFalse("file should be deleted after processing");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}

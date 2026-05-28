using MessagePack;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

public class IpcMessageSchemaEvolutionTests
{
    [Test]
    public void ProcessByteStats_NewKeyAddedAtKey5_DeserializesOnOlderSchema_DefaultsField()
    {
        var current = new ProcessByteStats
        {
            ProcessId = 42,
            ProcessName = "wirebound",
            TotalBytesSent = 123,
            TotalBytesReceived = 456,
            ActiveConnectionCount = 7,
            ExecutablePath = "x.exe"
        };

        var bytes = IpcTransport.SerializePayload(current);
        var older = IpcTransport.DeserializePayload<ProcessByteStatsV1>(bytes);

        older.ProcessId.Should().Be(42);
        older.ProcessName.Should().Be("wirebound");
        older.TotalBytesSent.Should().Be(123);
        older.TotalBytesReceived.Should().Be(456);
        older.ActiveConnectionCount.Should().Be(7);
    }

    [Test]
    public void ProcessByteStats_OldSchemaDeserializedOnNewSchema_ExecutablePathIsEmpty()
    {
        var older = new ProcessByteStatsV1
        {
            ProcessId = 43,
            ProcessName = "legacy",
            TotalBytesSent = 1000,
            TotalBytesReceived = 2000,
            ActiveConnectionCount = 3
        };

        var bytes = IpcTransport.SerializePayload(older);
        var current = IpcTransport.DeserializePayload<ProcessByteStats>(bytes);

        current.ProcessId.Should().Be(43);
        current.ProcessName.Should().Be("legacy");
        current.TotalBytesSent.Should().Be(1000);
        current.TotalBytesReceived.Should().Be(2000);
        current.ActiveConnectionCount.Should().Be(3);
        current.ExecutablePath.Should().BeEmpty();
    }

    [Test]
    public void ProcessConnectionStats_NewKeyAddedAtKey5_DeserializesOnOlderSchema_DefaultsField()
    {
        var current = new ProcessConnectionStats
        {
            ProcessId = 44,
            ProcessName = "browser",
            BytesSent = 3000,
            BytesReceived = 4000,
            Connections =
            [
                new ConnectionByteStats
                {
                    LocalAddress = "127.0.0.1",
                    LocalPort = 5000,
                    RemoteAddress = "127.0.0.1",
                    RemotePort = 5001,
                    Protocol = 6,
                    BytesSent = 30,
                    BytesReceived = 40
                }
            ],
            ExecutablePath = "browser.exe"
        };

        var bytes = IpcTransport.SerializePayload(current);
        var older = IpcTransport.DeserializePayload<ProcessConnectionStatsV1>(bytes);

        older.ProcessId.Should().Be(44);
        older.ProcessName.Should().Be("browser");
        older.BytesSent.Should().Be(3000);
        older.BytesReceived.Should().Be(4000);
        older.Connections.Should().HaveCount(1);
        older.Connections[0].RemotePort.Should().Be(5001);
    }

    [Test]
    public void ProcessConnectionStats_OldSchemaDeserializedOnNewSchema_ExecutablePathIsEmpty()
    {
        var older = new ProcessConnectionStatsV1
        {
            ProcessId = 45,
            ProcessName = "legacy-browser",
            BytesSent = 5000,
            BytesReceived = 6000,
            Connections =
            [
                new ConnectionByteStats
                {
                    LocalAddress = "10.0.0.1",
                    LocalPort = 1234,
                    RemoteAddress = "10.0.0.2",
                    RemotePort = 443,
                    Protocol = 6,
                    BytesSent = 50,
                    BytesReceived = 60
                }
            ]
        };

        var bytes = IpcTransport.SerializePayload(older);
        var current = IpcTransport.DeserializePayload<ProcessConnectionStats>(bytes);

        current.ProcessId.Should().Be(45);
        current.ProcessName.Should().Be("legacy-browser");
        current.BytesSent.Should().Be(5000);
        current.BytesReceived.Should().Be(6000);
        current.Connections.Should().HaveCount(1);
        current.Connections[0].RemotePort.Should().Be(443);
        current.ExecutablePath.Should().BeEmpty();
    }

    [MessagePackObject]
    public sealed class ProcessByteStatsV1
    {
        [Key(0)]
        public int ProcessId { get; set; }

        [Key(1)]
        public string ProcessName { get; set; } = string.Empty;

        [Key(2)]
        public long TotalBytesSent { get; set; }

        [Key(3)]
        public long TotalBytesReceived { get; set; }

        [Key(4)]
        public int ActiveConnectionCount { get; set; }
    }

    [MessagePackObject]
    public sealed class ProcessConnectionStatsV1
    {
        [Key(0)]
        public int ProcessId { get; set; }

        [Key(1)]
        public string ProcessName { get; set; } = string.Empty;

        [Key(2)]
        public long BytesSent { get; set; }

        [Key(3)]
        public long BytesReceived { get; set; }

        [Key(4)]
        public List<ConnectionByteStats> Connections { get; set; } = [];
    }
}

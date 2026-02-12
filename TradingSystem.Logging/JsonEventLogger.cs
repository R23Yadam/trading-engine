using System.Text.Json;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Logging;

public class JsonEventLogger : IEventLogger
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _jsonOptions;
    private int _eventCount;

    public string FilePath { get; }
    public int EventCount => _eventCount;

    public JsonEventLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var fileName = $"session_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.jsonl";
        FilePath = Path.Combine(logDirectory, fileName);
        _writer = new StreamWriter(FilePath, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = false
        };
        _jsonOptions = EventSerializerOptions.Default;
    }

    public void Log(IEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, evt.GetType(), _jsonOptions);
        _writer.WriteLine(json);
        _eventCount++;

        // Flush every 100 events for safety
        // TODO: make flush interval configurable, or switch to async writes for high-throughput
        if (_eventCount % 100 == 0)
            _writer.Flush();
    }

    public void Flush()
    {
        _writer.Flush();
    }

    public void Close()
    {
        _writer.Flush();
        _writer.Close();
    }
}

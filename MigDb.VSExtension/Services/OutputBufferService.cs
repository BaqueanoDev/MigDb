using System.Text;

namespace MigDb.VSExtension.Services;

internal sealed class OutputBufferService : IDisposable
{
    private const int FlushDelayMilliseconds = 100;

    private readonly StringBuilder _buffer = new("Ready :)");
    private readonly Timer _flushTimer;

    private bool _flushPending;
    private bool _disposed;

    public event Action<string>? TextChanged;

    public string Text
    {
        get
        {
            lock (_buffer)
                return _buffer.ToString();
        }
    }

    public OutputBufferService()
    {
        _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_buffer)
        {
            _disposed = true;
            _flushPending = false;
        }

        _flushTimer.Dispose();
    }

    public void Restart(string header)
    {
        lock (_buffer)
        {
            _buffer.Clear();
            _buffer.AppendLine(header).AppendLine();
        }

        Flush();
    }

    public void AppendLine(string line)
    {
        lock (_buffer)
        {
            _buffer.AppendLine(line);

            if (_flushPending || _disposed)
                return;

            _flushPending = true;
            _flushTimer.Change(FlushDelayMilliseconds, Timeout.Infinite);
        }
    }

    public void Set(string text)
    {
        lock (_buffer)
        {
            _buffer.Clear();
            _buffer.Append(text);
        }

        Flush();
    }

    private void Flush()
    {
        string text;

        lock (_buffer)
        {
            if (_flushPending)
            {
                _flushPending = false;
                _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            text = _buffer.ToString();
        }

        TextChanged?.Invoke(text);
    }
}

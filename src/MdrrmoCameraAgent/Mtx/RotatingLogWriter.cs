namespace MdrrmoCameraAgent.Mtx;

/// <summary>
/// Append-only writer that rotates the underlying file when it exceeds
/// <paramref name="maxBytes"/>. Keeps up to <paramref name="maxFiles"/>
/// historical copies named .1, .2, …, dropping the oldest. Thread-safe
/// via a single lock — fine for one writer per process which is all we
/// need (one mediamtx child).
/// </summary>
public sealed class RotatingLogWriter : IDisposable
{
    private readonly string _path;
    private readonly long   _maxBytes;
    private readonly int    _maxFiles;
    private readonly object _lock = new();
    private FileStream?     _stream;

    public RotatingLogWriter(string path, long maxBytes = 10 * 1024 * 1024, int maxFiles = 5)
    {
        _path     = path;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _stream   = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public void WriteLine(string line)
    {
        lock (_lock)
        {
            if (_stream is null) return;
            var bytes = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
            if (_stream.Length + bytes.Length > _maxBytes) Rotate();
            _stream!.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
    }

    private void Rotate()
    {
        _stream!.Dispose();
        for (var i = _maxFiles; i >= 1; i--)
        {
            var older = $"{_path}.{i}";
            var newer = i == 1 ? _path : $"{_path}.{i - 1}";
            if (File.Exists(older)) File.Delete(older);
            if (File.Exists(newer)) File.Move(newer, older);
        }
        _stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}

using MdrrmoCameraAgent.Backend;
using MdrrmoCameraAgent.Mtx;

namespace MdrrmoCameraAgent.Lifecycle;

public sealed class PublishLoopHost
{
    private readonly Dictionary<string, CameraEntry>   _byCameraId;
    private readonly Dictionary<string, PublishStatus> _statusByStreamPath = new();
    private readonly object _lock = new();

    public PublishLoopHost(IEnumerable<CameraEntry> cameras)
    {
        _byCameraId = cameras.ToDictionary(c => c.Id);
    }

    public IReadOnlyList<CameraEntry> Cameras
    {
        get { lock (_lock) return _byCameraId.Values.ToList(); }
    }

    public bool ReplaceCameras(IEnumerable<CameraEntry> next)
    {
        lock (_lock)
        {
            var nextById = next.ToDictionary(c => c.Id);
            var changed  = nextById.Count != _byCameraId.Count
                         || nextById.Any(kv => !_byCameraId.TryGetValue(kv.Key, out var old) || old != kv.Value);
            if (!changed) return false;

            var validPaths = new HashSet<string>(nextById.Values.Select(c => c.StreamPath));
            foreach (var stale in _statusByStreamPath.Keys.Where(k => !validPaths.Contains(k)).ToList())
                _statusByStreamPath.Remove(stale);

            _byCameraId.Clear();
            foreach (var (k, v) in nextById) _byCameraId[k] = v;
            return true;
        }
    }

    public void RecordProbeResult(IReadOnlyDictionary<string, PublishStatus> resultsByStreamPath)
    {
        lock (_lock)
        {
            var validPaths = new HashSet<string>(_byCameraId.Values.Select(c => c.StreamPath));
            foreach (var (k, v) in resultsByStreamPath)
                if (validPaths.Contains(k))
                    _statusByStreamPath[k] = v;
        }
    }

    public List<HeartbeatCamera> SnapshotHeartbeatPayload()
    {
        lock (_lock)
        {
            return _byCameraId.Values.Select(c =>
            {
                var s = _statusByStreamPath.TryGetValue(c.StreamPath, out var v) ? v : PublishStatus.Unknown;
                // "online" = the agent process is running; publish_status carries the per-camera stream health.
                // A camera that disappears from the list is simply absent from the payload, never "offline".
                return new HeartbeatCamera(c.Id, "online", null, s.ToWireString());
            }).ToList();
        }
    }
}

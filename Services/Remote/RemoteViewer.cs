using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// Viewer side of the video: decodes every access unit in order on its own thread and keeps only the newest picture for the
/// screen, so a slow UI frame never makes the stream fall behind.
/// </summary>
public sealed class RemoteViewer : IDisposable
{
    public sealed record Picture(byte[] Bgra, int Width, int Height);

    private readonly BlockingCollection<VideoFrame> _queue = new(boundedCapacity: 240);
    private readonly Action _pictureReady;
    private Picture? _latest;

    /// <param name="pictureReady">Called on the decoding thread when a picture is waiting and the previous one was taken: call <see cref="TakeLatest"/>.</param>
    public RemoteViewer(Action pictureReady)
    {
        _pictureReady = pictureReady;
        new Thread(Run) { IsBackground = true, Name = "ClientSSH video" }.Start();
    }

    public void Push(VideoFrame frame)
    {
        try { _queue.TryAdd(frame); }
        catch (InvalidOperationException) { /* closed while a packet was on its way */ }
    }

    private void Run()
    {
        H264Decoder? decoder = null;
        var waitForKey = false;
        try
        {
            foreach (var frame in _queue.GetConsumingEnumerable())
            {
                if (waitForKey && !frame.IsKey) continue;
                waitForKey = false;
                try
                {
                    decoder ??= new H264Decoder();
                    var picture = decoder.Decode(frame.Data, frame.Width, frame.Height);
                    if (picture is null) continue;
                    // Behind: skip showing this one, the next decoded picture is newer anyway.
                    if (_queue.Count > 2) continue;
                    if (Interlocked.Exchange(ref _latest, new Picture(picture, frame.Width, frame.Height)) is null) _pictureReady();
                }
                catch (COMException)
                {
                    // A broken stream: start a fresh decoder at the next key frame.
                    decoder?.Dispose();
                    decoder = null;
                    waitForKey = true;
                }
            }
        }
        finally
        {
            decoder?.Dispose();
        }
    }

    /// <summary>The newest picture not shown yet, or null.</summary>
    public Picture? TakeLatest() => Interlocked.Exchange(ref _latest, null);

    public void Dispose() => _queue.CompleteAdding();
}

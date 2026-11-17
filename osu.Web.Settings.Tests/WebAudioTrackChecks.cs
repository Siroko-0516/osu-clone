// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.JSInterop;
using osu.Web;

static class WebAudioTrackChecks
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        using var module = new MediaModule();
        var track = new WebAudioTrack(module, new MemoryStream(new byte[] { 1 }), "clock fixture");
        int completions = 0, failures = 0;
        track.Completed += () => completions++;
        track.Failed += () => failures++;

        Check(!track.IsLoaded && !track.IsRunning && track.CurrentTime == 0 && !track.HasCompleted,
            "A newly created track did not lazily read its initial media state.");
        Check(track.StateReadCount == 1, "Initial state getters repeated the media state interop read.");

        module.Media.Loaded = true;
        module.Media.Running = true;
        for (int frame = 1; frame <= 100; frame++)
        {
            module.Media.Position = frame * 16.0;
            track.Update();
            for (int read = 0; read < 10; read++)
            {
                Check(track.IsLoaded && track.IsRunning && track.CurrentTime == frame * 16.0
                      && !track.HasCompleted && !track.RequiresGesture && track.LoadError.Length == 0,
                    "The gameplay clock did not see the current audio frame snapshot.");
            }
        }
        Check(track.StateReadCount == 101 && track.Length == module.Media.Duration,
            "Audio updates must refresh media state exactly once per frame.");

        Check(track.Seek(2500) && track.CurrentTime == 2500,
            "A seek did not invalidate the same-frame cached position.");
        long readsBeforeFailedSeek = track.StateReadCount;
        Check(!track.Seek(-1) && track.CurrentTime == 2500 && track.StateReadCount == readsBeforeFailedSeek,
            "A rejected seek changed position or discarded a valid cached state.");
        track.Stop();
        Check(!track.IsRunning && track.CurrentTime == 2500, "Pause left the cached track running.");
        track.Start();
        Check(track.IsRunning, "Resume left the cached track paused.");

        module.Media.Running = false;
        module.Media.Ended = true;
        track.Update();
        track.Update();
        Check(track.HasCompleted && !track.IsRunning && completions == 1,
            "An ended browser track did not complete exactly once after the next audio update.");
        Check(track.Seek(0) && !track.HasCompleted, "Seeking a completed track did not clear its cached end state.");
        module.Media.Error = "decode fixture failure";
        track.Update();
        track.Update();
        Check(track.LoadError == module.Media.Error && failures == 1,
            "Asynchronous media failure did not propagate exactly once through the cached state.");

        track.Dispose();
        track.Update(); // AudioComponent disposal is queued until the next audio update.
        long readsAfterDisposal = track.StateReadCount;
        Check(!track.IsLoaded && !track.IsRunning && !track.HasCompleted && track.CurrentTime == 0
              && track.LoadError.Length == 0 && !track.RequiresGesture && track.StateReadCount == readsAfterDisposal
              && module.DisposedTracks == 1,
            "Disposed track getters accessed browser media resources.");
        Console.WriteLine("PASS browser audio frame snapshots, bounded state reads, seek/pause/resume, completion, errors and disposal");
    }

    private sealed class MediaModule : IJSInProcessObjectReference
    {
        public readonly WebAudioTrack.State Media = new() { Duration = 10000 };
        public int DisposedTracks;

        public TValue Invoke<TValue>(string identifier, params object?[]? args)
        {
            object? result = null;
            switch (identifier)
            {
                case "createTrack": result = 1; break;
                case "trackState":
                    result = new WebAudioTrack.State
                    {
                        Duration = Media.Duration, Position = Media.Position, Loaded = Media.Loaded,
                        Running = Media.Running, Ended = Media.Ended, Blocked = Media.Blocked, Error = Media.Error,
                    };
                    break;
                case "seekTrack":
                    double position = (double)args![1]!;
                    bool accepted = position >= 0 && position <= Media.Duration;
                    if (accepted) { Media.Position = position; Media.Ended = false; }
                    result = accepted;
                    break;
                case "playTrack": Media.Running = true; Media.Ended = false; break;
                case "stopTrack": Media.Running = false; break;
                case "configureTrack": break;
                case "disposeTrack": DisposedTracks++; break;
                default: throw new InvalidOperationException("Unexpected browser audio call: " + identifier);
            }
            return result is null ? default! : (TValue)result;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => new(Invoke<TValue>(identifier, args));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new(Invoke<TValue>(identifier, args));

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

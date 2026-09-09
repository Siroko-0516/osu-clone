// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.JSInterop;
using osu.Framework.Audio.Track;

namespace osu.Web;

public sealed class WebAudioTrack : Track
{
    private readonly IJSInProcessObjectReference module;
    private readonly int id;
    private bool completionReported;
    private bool failureReported;

    public WebAudioTrack(IJSInProcessObjectReference module, Stream stream, string name) : base(name)
    {
        this.module = module;
        using (stream)
        using (var bytes = new MemoryStream())
        {
            stream.CopyTo(bytes);
            id = module.Invoke<int>("createTrack", bytes.ToArray());
        }
    }

    private State ReadState() => module.Invoke<State>("trackState", id);
    public override bool IsDummyDevice => false;
    public string LoadError => IsDisposed ? string.Empty : ReadState().Error;
    public override bool IsLoaded => !IsDisposed && ReadState().Loaded;
    public override bool IsRunning => !IsDisposed && ReadState().Running;
    public override double CurrentTime => IsDisposed ? 0 : ReadState().Position;
    public override bool HasCompleted => !IsDisposed && ReadState().Ended;
    public override bool Seek(double seek) { completionReported = false; return module.Invoke<bool>("seekTrack", id, seek); }
    public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));
    public override void Start() { completionReported = false; module.InvokeVoid("playTrack", id); }
    public override Task StartAsync() { Start(); return Task.CompletedTask; }
    public override void Stop() => module.InvokeVoid("stopTrack", id);
    public override Task StopAsync() { Stop(); return Task.CompletedTask; }

    protected override void UpdateState()
    {
        var state = ReadState();
        Length = state.Duration;
        module.InvokeVoid("configureTrack", id, AggregateVolume.Value, AggregateBalance.Value, AggregateFrequency.Value, AggregateTempo.Value, false);
        if (state.Error.Length > 0 && !failureReported) { failureReported = true; RaiseFailed(); }
        base.UpdateState();
        if (state.Ended && !Looping && !completionReported) { completionReported = true; RaiseCompleted(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed) module.InvokeVoid("disposeTrack", id);
        base.Dispose(disposing);
    }

    public sealed class State
    {
        public double Duration { get; set; }
        public double Position { get; set; }
        public bool Loaded { get; set; }
        public bool Running { get; set; }
        public bool Ended { get; set; }
        public string Error { get; set; } = "";
    }
}

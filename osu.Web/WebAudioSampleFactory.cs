// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.JSInterop;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Sample;

namespace osu.Web;

public sealed class WebAudioSampleFactory : BrowserSampleFactory
{
    private readonly IJSInProcessObjectReference module;
    private readonly AudioMixer mixer;
    private readonly int id;

    public WebAudioSampleFactory(IJSInProcessObjectReference module, byte[] bytes, string name, AudioMixer mixer) : base(name)
    {
        this.module = module;
        this.mixer = mixer;
        id = module.Invoke<int>("createSample", bytes);
    }

    public override bool IsLoaded => !IsDisposed && module.Invoke<SampleState>("sampleState", id).Loaded;
    public override double Length => IsDisposed ? 0 : module.Invoke<SampleState>("sampleState", id).Duration;

    protected override void UpdateState()
    {
        var state = module.Invoke<SampleState>("sampleState", id);
        if (state.Error.Length > 0) throw new InvalidDataException($"Could not decode sample '{Name}': {state.Error}");
        module.InvokeVoid("configureSample", id, PlaybackConcurrency.Value);
        base.UpdateState();
    }

    protected override Sample CreateSampleCore() => new WebSample(this);

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed) module.InvokeVoid("disposeSample", id);
        base.Dispose(disposing);
    }

    public sealed class SampleState
    {
        public bool Loaded { get; set; }
        public double Duration { get; set; }
        public string Error { get; set; } = "";
    }

    private sealed class WebSample : Sample
    {
        private readonly WebAudioSampleFactory factory;
        public WebSample(WebAudioSampleFactory factory) : base(factory.Name)
        {
            this.factory = factory;
            PlaybackConcurrency.BindTo(factory.PlaybackConcurrency);
        }
        public override bool IsLoaded => factory.IsLoaded;
        public override double Length => factory.Length;
        protected override SampleChannel CreateChannel()
        {
            ObjectDisposedException.ThrowIf(factory.IsDisposed, factory);
            var channel = new WebChannel(factory.module, factory.id, Name);
            factory.mixer.Add(channel);
            return channel;
        }
    }

    private sealed class WebChannel : SampleChannel
    {
        private readonly IJSInProcessObjectReference module;
        private readonly int id;
        private bool startPending;
        private int generation;
        public WebChannel(IJSInProcessObjectReference module, int sampleId, string name) : base(name)
        {
            this.module = module;
            id = module.Invoke<int>("createChannel", sampleId);
        }
        public override bool Playing => !IsDisposed && (startPending || module.Invoke<bool>("channelPlaying", id));
        public override void Play()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            startPending = true;
            int requestedGeneration = ++generation;
            base.Play();
            EnqueueAction(() =>
            {
                if (generation != requestedGeneration) return;
                try { configure(); module.InvokeVoid("playChannel", id); }
                finally { startPending = false; }
            });
        }
        public override void Stop()
        {
            generation++;
            startPending = false;
            module.InvokeVoid("stopChannel", id);
        }
        private void configure() => module.InvokeVoid("configureChannel", id, AggregateVolume.Value, AggregateBalance.Value, AggregateFrequency.Value, AggregateTempo.Value, Looping);
        protected override void UpdateState() { configure(); base.UpdateState(); }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            module.InvokeVoid("disposeChannel", id);
        }
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Audio.Track;
using osu.Framework.Audio.Sample;
using osu.Framework.IO.Stores;
using osu.Framework.Input.Bindings;
using osu.Game.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Configuration;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.UI;
using osu.Game.Resources;
using osu.Game.Database.Persistence;
using osuTK.Graphics;

namespace osu.Web
{
    /// <summary>
    /// Realm-free diagnostic runtime used only while the real OsuGame services are ported.
    /// This must never be presented as the original osu! user interface.
    /// </summary>
    public class BrowserBootstrapGame : osu.Framework.Game
    {
        private readonly IKeyBindingSource bindingSource;
        private readonly IGamePersistence persistence;
        private readonly BrowserBuiltInSkinSource skinSource = new();
        private BrowserRulesetConfigCache? rulesetConfigCache;
        public BrowserGameplaySession? GameplaySession { get; private set; }
        public DrawableRuleset? CurrentDrawableRuleset => GameplaySession?.DrawableRuleset;
        public GameplayClockContainer? GameplayClock => GameplaySession?.Clock;
        public string GameplayError { get; private set; } = string.Empty;
        public long ActionPressCount { get; private set; }
        public long ActionReleaseCount { get; private set; }
        public string LastAction { get; private set; } = "none";

        public BrowserBootstrapGame(IKeyBindingSource bindingSource, IGamePersistence persistence)
        {
            this.bindingSource = bindingSource;
            this.persistence = persistence;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs(bindingSource);
            dependencies.CacheAs(persistence);
            dependencies.CacheAs<ISkinSource>(skinSource);
            dependencies.CacheAs<IRulesetConfigCache>(rulesetConfigCache = new BrowserRulesetConfigCache(persistence.RulesetSettings));
            dependencies.Cache(new OsuConfigManager(Host.Storage));
            return dependencies;
        }

        private sealed class ActionProbe<T>(BrowserBootstrapGame game, string mode) : Drawable, IKeyBindingHandler<T>
            where T : struct
        {
            public bool OnPressed(KeyBindingPressEvent<T> e)
            {
                game.ActionPressCount++;
                game.LastAction = $"{mode}: {e.Action}";
                return true;
            }
            public void OnReleased(KeyBindingReleaseEvent<T> e) => game.ActionReleaseCount++;
        }
        public long ProcessedInputEvents { get; private set; }
        private readonly Box cursor = new Box { Size = new osuTK.Vector2(16), Colour = Color4.Cyan, Depth = -1 };
        public Track? AudioTestTrack { get; private set; }
        public Sample? AudioTestSample { get; private set; }
        public int SamplePlayCount { get; private set; }
        private readonly Container inputLayer = new() { RelativeSizeAxes = Axes.Both };
        private readonly Container idleLayer = new() { RelativeSizeAxes = Axes.Both };

        public void SetRuleset(string mode, int variant = 0) => Schedule(() =>
        {
            inputLayer.Child = mode switch
            {
                "mania" => createInputContainer<ManiaAction>(new ManiaRuleset(), variant, mode),
                "taiko" => createInputContainer<TaikoAction>(new TaikoRuleset(), 0, mode),
                "catch" => createInputContainer<CatchAction>(new CatchRuleset(), 0, mode),
                _ => createInputContainer<OsuAction>(new OsuRuleset(), 0, "osu")
            };
        });

        public void LoadBeatmap(IBeatmap beatmap, Ruleset ruleset, Track track)
        {
            ArgumentNullException.ThrowIfNull(beatmap);
            ArgumentNullException.ThrowIfNull(ruleset);
            ArgumentNullException.ThrowIfNull(track);

            Schedule(() =>
            {
                try
                {
                    GameplayError = string.Empty;
                    GameplaySession?.Dispose();
                    GameplaySession = new BrowserGameplaySession(beatmap, ruleset, track);
                    idleLayer.Hide();
                    inputLayer.Child = GameplaySession.Clock;
                    GameplaySession.Start();
                }
                catch (Exception exception)
                {
                    GameplaySession?.Dispose();
                    GameplaySession = null;
                    GameplayError = formatException(exception);
                    inputLayer.Clear();
                    idleLayer.Show();
                    idleLayer.Show();
                }
            });
        }

        public void PauseGameplay() => Schedule(() => GameplaySession?.Pause());
        public void ResumeGameplay() => Schedule(() => GameplaySession?.Resume());
        public void RestartGameplay() => Schedule(() => GameplaySession?.Restart());
        public void SeekGameplay(double time) => Schedule(() => GameplaySession?.Seek(time));
        public void StopGameplay() => Schedule(() =>
        {
            GameplaySession?.Dispose();
            GameplaySession = null;
            inputLayer.Clear();
            idleLayer.Show();
        });

        private static string formatException(Exception exception)
        {
            var messages = new List<string>();
            for (Exception? current = exception; current is not null; current = current.InnerException)
                messages.Add($"{current.GetType().Name}: {current.Message}");
            return string.Join(" → ", messages);
        }

        private Drawable createInputContainer<T>(osu.Game.Rulesets.Ruleset ruleset, int variant, string mode)
            where T : struct
            => new RulesetInputManager<T>.RulesetKeyBindingContainer(ruleset.RulesetInfo, variant, SimultaneousBindingMode.Unique)
            {
                RelativeSizeAxes = Axes.Both,
                Child = new ActionProbe<T>(this, mode) { RelativeSizeAxes = Axes.Both },
            };

        public void StartSampleTest()
        {
            if (AudioTestSample is null)
            {
                writeTestTone("browser-sample-test.wav", 0.2);
                AudioTestSample = Audio.GetSampleStore(new StorageBackedResourceStore(Host.Storage)).Get("browser-sample-test.wav");
            }
            AudioTestSample.Play();
            SamplePlayCount++;
        }

        public void StartAudioTest()
        {
            if (AudioTestTrack is null)
            {
                writeTestTone("browser-audio-test.wav", 5);
                AudioTestTrack = Audio.GetTrackStore(new StorageBackedResourceStore(Host.Storage)).Get("browser-audio-test.wav");
            }
            AudioTestTrack.Seek(0);
            AudioTestTrack.Start();
        }

        private void writeTestTone(string filename, double seconds)
        {
            const int sampleRate = 44100;
            int samples = (int)(sampleRate * seconds);
            using (var stream = Host.Storage.GetStream(filename, FileAccess.Write, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                {
                    double envelope = Math.Min(1, Math.Min(i, samples - i - 1) / 441.0);
                    writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 2000 * envelope));
                }
            }
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            cursor.Position = e.MousePosition;
            ProcessedInputEvents++;
            return base.OnMouseMove(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            cursor.Colour = Color4.White;
            ProcessedInputEvents++;
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            cursor.Colour = Color4.Cyan;
            ProcessedInputEvents++;
            base.OnMouseUp(e);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            ProcessedInputEvents++;
            return true;
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            ProcessedInputEvents++;
            base.OnKeyUp(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                GameplaySession?.Dispose();
                rulesetConfigCache?.Dispose();
                skinSource.Dispose();
            }

            base.Dispose(isDisposing);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Exercise the real osu! resource and texture pipeline here. This is the
            // original embedded logo asset, not a web-side copy or replacement image.
            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));
            skinSource.AttachResources(Resources, Host, Audio);

            idleLayer.Child = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new osuTK.Vector2(320),
                Texture = skinSource.GetTexture(@"Menu/logo", default, default)
                          ?? throw new InvalidOperationException("The built-in skin did not provide the original menu logo texture."),
            };

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(18, 12, 24, 255),
                },
                idleLayer,
                cursor,
                inputLayer,
            };
            SetRuleset("osu");
        }
    }
}

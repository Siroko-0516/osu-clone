// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osuTK.Graphics;

namespace osu.Web
{
    /// <summary>
    /// Realm-free diagnostic runtime used only while the real OsuGame services are ported.
    /// This must never be presented as the original osu! user interface.
    /// </summary>
    public sealed class BrowserBootstrapGame : osu.Framework.Game
    {
        public long ProcessedInputEvents { get; private set; }
        private readonly Box cursor = new Box { Size = new osuTK.Vector2(16), Colour = Color4.Cyan, Depth = -1 };
        public Track? AudioTestTrack { get; private set; }

        public void StartAudioTest()
        {
            if (AudioTestTrack is null)
            {
                const int sampleRate = 44100;
                const int samples = sampleRate * 5;
                using (var stream = Host.Storage.GetStream("browser-audio-test.wav", FileAccess.Write, FileMode.Create))
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
                AudioTestTrack = Audio.GetTrackStore(new StorageBackedResourceStore(Host.Storage)).Get("browser-audio-test.wav");
            }
            AudioTestTrack.Seek(0);
            AudioTestTrack.Start();
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

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(18, 12, 24, 255),
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new osuTK.Vector2(360, 120),
                    Colour = new Color4(236, 52, 123, 255),
                },
                cursor,
            };
        }
    }
}

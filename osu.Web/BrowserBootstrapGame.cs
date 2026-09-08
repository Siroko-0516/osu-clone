// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
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

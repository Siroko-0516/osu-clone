// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace osu.Web
{
    /// <summary>
    /// Realm-free diagnostic runtime used only while the real OsuGame services are ported.
    /// This must never be presented as the original osu! user interface.
    /// </summary>
    public sealed class BrowserBootstrapGame : osu.Framework.Game
    {
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
            };
        }
    }
}

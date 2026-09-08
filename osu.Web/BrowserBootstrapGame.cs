// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Resources;
using osuTK;
using osuTK.Graphics;

namespace osu.Web
{
    /// <summary>
    /// Browser-safe original UI scene used while persistent osu! services are migrated to IndexedDB.
    /// It deliberately loads UI assets without constructing Realm-backed gameplay services.
    /// </summary>
    public sealed class BrowserBootstrapGame : osu.Framework.Game
    {
        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(18, 12, 24, 255),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(12),
                    Children = new Drawable[]
                    {
                        new Sprite
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Texture = textures.Get(@"Menu/logo"),
                            Scale = new Vector2(0.72f),
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "click to start",
                            Font = OsuFont.GetFont(size: 22, weight: FontWeight.Regular),
                            Colour = Color4.White,
                        },
                    },
                },
            };
        }
    }
}

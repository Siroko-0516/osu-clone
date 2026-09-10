// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Web;

/// <summary>
/// Exposes the original built-in Argon skin to the Realm-free browser runtime.
/// File-backed custom skin resources remain disabled until their storage adapter is available.
/// </summary>
public sealed class BrowserBuiltInSkinSource : ISkinSource, IDisposable
{
    private readonly ArgonSkin skin = new(null!);
    private ResourceStoreBackedSkin? resources;

    public event Action? SourceChanged;

    public IEnumerable<ISkin> AllSources
    {
        get
        {
            yield return skin;

            if (resources is not null)
                yield return resources;
        }
    }

    public void AttachResources(IResourceStore<byte[]> resourceStore, GameHost host, AudioManager audio)
    {
        resources?.Dispose();
        resources = new ResourceStoreBackedSkin(resourceStore, host, audio);
        SourceChanged?.Invoke();
    }

    public ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (lookupFunction(skin))
            return skin;

        return resources is not null && lookupFunction(resources) ? resources : null;
    }

    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => skin.GetDrawableComponent(lookup);

    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        skin.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? resources?.GetTexture(componentName, wrapModeS, wrapModeT);

    public ISample? GetSample(ISampleInfo sampleInfo) => resources?.GetSample(sampleInfo);

    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => skin.GetConfig<TLookup, TValue>(lookup);

    public void Dispose()
    {
        resources?.Dispose();
        skin.Dispose();
    }
}

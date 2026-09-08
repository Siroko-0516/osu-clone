// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework;

namespace osu.Web
{
    /// <summary>
    /// Realm-free browser runtime used while persistent osu! services are migrated to IndexedDB.
    /// Keeping this host separate ensures unsupported native database code can never run in WebAssembly.
    /// </summary>
    public sealed class BrowserBootstrapGame : Game
    {
    }
}

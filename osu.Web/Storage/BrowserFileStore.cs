// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.JSInterop;
using osu.Framework.Platform;
using FrameworkStorage = osu.Framework.Platform.Storage;

namespace osu.Web.Storage;

public sealed class BrowserFileStore(IJSRuntime js) : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/browserFiles.mjs").AsTask());
    private readonly SemaphoreSlim syncLock = new(1, 1);
    public sealed class Entry { public string Path { get; set; } = ""; public byte[] Data { get; set; } = Array.Empty<byte>(); }

    public async Task<int> HydrateAsync(FrameworkStorage storage)
    {
        var entries = await (await module.Value).InvokeAsync<Entry[]>("list");
        foreach (var entry in entries)
        {
            validate(entry.Path);
            using var output = storage.GetStream(entry.Path, FileAccess.Write, FileMode.Create);
            await output.WriteAsync(entry.Data);
        }
        return entries.Length;
    }

    public async Task PutAsync(IEnumerable<Entry> entries)
    {
        Entry[] snapshot = entries.ToArray();
        foreach (Entry entry in snapshot)
            validate(entry.Path);

        await syncLock.WaitAsync();
        try
        {
            await (await module.Value).InvokeAsync<int>("put", (object)snapshot);
        }
        finally { syncLock.Release(); }
    }

    public async Task FlushAsync(FrameworkStorage storage)
    {
        await syncLock.WaitAsync();
        try
        {
            var entries = new List<Entry>();
            collect(storage, "", entries);
            await (await module.Value).InvokeAsync<int>("replaceAll", (object)entries);
        }
        finally { syncLock.Release(); }
    }

    private static void collect(FrameworkStorage storage, string prefix, List<Entry> entries)
    {
        foreach (string file in storage.GetFiles(""))
        {
            string path = string.IsNullOrEmpty(prefix) ? file : $"{prefix}/{file}";
            validate(path);
            using var input = storage.GetStream(file, FileAccess.Read, FileMode.Open);
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            entries.Add(new Entry { Path = path, Data = memory.ToArray() });
        }
        foreach (string directory in storage.GetDirectories(""))
        {
            string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            collect(storage.GetStorageForDirectory(name), string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}", entries);
        }
    }

    private static void validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Split('/', '\\').Contains(".."))
            throw new InvalidDataException("Browser files require a safe relative path.");
    }

    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated && module.Value.IsCompletedSuccessfully)
        {
            var loaded = await module.Value;
            await loaded.InvokeVoidAsync("close");
            await loaded.DisposeAsync();
        }
        syncLock.Dispose();
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace osu.Game.Database.Persistence
{
    /// <summary>Reads a browser-provided .osz without relying on Realm or a physical filesystem.</summary>
    public static class OszArchiveReader
    {
        public const long MAX_UNCOMPRESSED_SIZE = 512 * 1024 * 1024;
        public const int MAX_FILES = 4096;

        public static OszImportPackage Read(Stream source)
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var files = new List<OszFile>(archive.Entries.Count);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSize = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                if (files.Count >= MAX_FILES)
                    throw new InvalidDataException($"Beatmap archive contains more than {MAX_FILES} files.");

                string path = normalisePath(entry.FullName);
                if (!paths.Add(path))
                    throw new InvalidDataException($"Beatmap archive contains duplicate resource path '{path}'.");
                totalSize = checked(totalSize + entry.Length);
                if (totalSize > MAX_UNCOMPRESSED_SIZE)
                    throw new InvalidDataException("Beatmap archive is too large after decompression.");

                using Stream input = entry.Open();
                using var output = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
                input.CopyTo(output);
                files.Add(new OszFile(path, output.ToArray()));
            }

            OszFile[] mapFiles = files.Where(file => file.Path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (mapFiles.Length == 0)
                throw new InvalidDataException("Beatmap archive does not contain an .osu difficulty.");

            ParsedBeatmap[] parsed = mapFiles.Select(parseBeatmap).ToArray();
            ParsedBeatmap first = parsed[0];
            using var archiveHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (OszFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                archiveHasher.AppendData(Encoding.UTF8.GetBytes(file.Path));
                archiveHasher.AppendData(file.Data);
            }
            string archiveHash = Convert.ToHexString(archiveHasher.GetHashAndReset()).ToLowerInvariant();
            string setId = first.SetId > 0 ? first.SetId.ToString(CultureInfo.InvariantCulture) : archiveHash;
            string root = $"beatmaps/{setId}";
            var set = new BeatmapSetSnapshot(setId, first.Artist, first.Title, first.Creator, root);
            var difficulties = parsed.Select(map => new BeatmapSnapshot(
                map.BeatmapId > 0 ? map.BeatmapId.ToString(CultureInfo.InvariantCulture) : hashText(setId + "\0" + map.Path),
                setId, map.Version, map.Mode, 0, $"{root}/{map.Path}", $"{root}/{audioPath(map)}"))
                                     .ToArray();

            return new OszImportPackage(set, difficulties, files.Select(file => new OszFile($"{root}/{file.Path}", file.Data)).ToArray());

            string audioPath(ParsedBeatmap map)
            {
                string requested = resolveRelativePath(map.Path, map.AudioFilename);
                return files.SingleOrDefault(file => file.Path.Equals(requested, StringComparison.OrdinalIgnoreCase))?.Path
                       ?? throw new InvalidDataException($"Difficulty '{map.Version}' references missing audio '{requested}'.");
            }
        }

        /// <summary>
        /// Reads metadata first, then extracts each archive entry individually.
        /// This avoids retaining the complete decompressed archive in WebAssembly memory.
        /// </summary>
        public static async Task<OszImportPackage> ReadStreamingAsync(Stream source, Func<OszFile, ValueTask> storeFile)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(storeFile);
            if (!source.CanSeek)
                throw new ArgumentException("Streaming import requires a seekable archive stream.", nameof(source));

            source.Position = 0;
            string archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(source).ConfigureAwait(false)).ToLowerInvariant();
            source.Position = 0;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parsed = new List<ParsedBeatmap>();
            long totalSize = 0;

            using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    if (paths.Count >= MAX_FILES)
                        throw new InvalidDataException($"Beatmap archive contains more than {MAX_FILES} files.");

                    string path = normalisePath(entry.FullName);
                    if (!paths.Add(path))
                        throw new InvalidDataException($"Beatmap archive contains duplicate resource path '{path}'.");
                    totalSize = checked(totalSize + entry.Length);
                    if (totalSize > MAX_UNCOMPRESSED_SIZE)
                        throw new InvalidDataException("Beatmap archive is too large after decompression.");

                    if (!path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using Stream input = entry.Open();
                    using var output = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
                    await input.CopyToAsync(output).ConfigureAwait(false);
                    parsed.Add(parseBeatmap(new OszFile(path, output.ToArray())));
                }
            }

            if (parsed.Count == 0)
                throw new InvalidDataException("Beatmap archive does not contain an .osu difficulty.");

            ParsedBeatmap first = parsed[0];
            string setId = first.SetId > 0 ? first.SetId.ToString(CultureInfo.InvariantCulture) : archiveHash;
            string root = $"beatmaps/{setId}";
            var set = new BeatmapSetSnapshot(setId, first.Artist, first.Title, first.Creator, root);
            var difficulties = parsed.Select(map => new BeatmapSnapshot(
                map.BeatmapId > 0 ? map.BeatmapId.ToString(CultureInfo.InvariantCulture) : hashText(setId + "\0" + map.Path),
                setId, map.Version, map.Mode, 0, $"{f.3,5,3,5c}(s=>s)}"{root}/{map.Path}", $"{root}/{resolveAudioPath(map)}"))
                                     .ToArray();

            source.Position = 0;
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string path = normalisePath(entry.FullName);
                    using Stream input = entry.Open();
                    using var output = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
                    await input.CopyToAsync(output).ConfigureAwait(false);
                    await storeFile(new OszFile($"{root}/{path}", output.ToArray())).ConfigureAwait(false);
                }
            }

            return new OszImportPackage(set, difficulties, Array.Empty<OszFile>());

            string resolveAudioPath(ParsedBeatmap map)
            {
                string requested = resolveRelativePath(map.Path, map.AudioFilename);
                return paths.SingleOrDefault(path => path.Equals(requested, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidDataException($"Difficulty '{map.Version}' references missing audio '{requested}'.");
            }
        }

        private static ParsedBeatmap parseBeatmap(OszFile file)
        {
            string text = Encoding.UTF8.GetString(file.Data).TrimStart('\uFEFF');
            string section = string.Empty;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line;
                    continue;
                }

                if (section is not ("[General]" or "[Metadata]"))
                    continue;
                int separator = line.IndexOf(':');
                if (separator > 0)
                    values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            string required(string key)
            {
                if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                    throw new InvalidDataException($"Difficulty '{file.Path}' is missing {key}.");
                return value;
            }

            return new ParsedBeatmap(file.Path, required("Artist"), required("Title"), required("Creator"), required("Version"),
                required("AudioFilename"), parseInt("BeatmapSetID"), parseInt("BeatmapID"), parseInt("Mode"));

            int parseInt(string key) => values.TryGetValue(key, out string? value)
                                        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
        }

        private static string normalisePath(string path)
        {
            string result = path.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(result) || result.Split('/').Any(segment => segment is "" or "." or "..") || result.Contains(':'))
                throw new InvalidDataException($"Unsafe beatmap archive path: {path}");
            return result;
        }

        private static string resolveRelativePath(string mapPath, string referencedPath)
        {
            string directory = mapPath.Contains('/') ? mapPath[..mapPath.LastIndexOf('/')] : string.Empty;
            return normalisePath(string.IsNullOrEmpty(directory) ? referencedPath : $"{directory}/{referencedPath}");
        }

        private static string hashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private sealed record ParsedBeatmap(string Path, string Artist, string Title, string Creator, string Version, string AudioFilename,
                                            int SetId, int BeatmapId, int Mode);
    }

    public sealed record OszFile(string Path, byte[] Data);
    public sealed record OszImportPackage(BeatmapSetSnapshot Set, IReadOnlyList<BeatmapSnapshot> Beatmaps, IReadOnlyList<OszFile> Files);
}


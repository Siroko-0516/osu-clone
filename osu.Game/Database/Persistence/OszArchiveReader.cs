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
            long totalSize = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                if (files.Count >= MAX_FILES)
                    throw new InvalidDataException($"Beatmap archive contains more than {MAX_FILES} files.");

                string path = normalisePath(entry.FullName);
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
                setId, map.Version, map.Mode, 0, $"{root}/{map.Path}", $"{root}/{resolveRelativePath(map.Path, map.AudioFilename)}"))
                                     .ToArray();

            foreach (BeatmapSnapshot difficulty in difficulties)
            {
                string relativeAudioPath = difficulty.AudioPath[(root.Length + 1)..];
                if (!files.Any(file => file.Path.Equals(relativeAudioPath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Difficulty '{difficulty.DifficultyName}' references missing audio '{relativeAudioPath}'.");
            }

            return new OszImportPackage(set, difficulties, files.Select(file => new OszFile($"{root}/{file.Path}", file.Data)).ToArray());
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


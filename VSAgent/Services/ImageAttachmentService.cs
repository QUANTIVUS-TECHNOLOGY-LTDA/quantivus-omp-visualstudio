using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace VSAgent.Services
{
    /// <summary>
    /// Persists images that the user pastes or drops into the prompt composer
    /// to a stable directory and returns references that the agent can later
    /// resolve via its <c>read</c> or <c>inspect_image</c> tools.
    ///
    /// OMP itself only accepts text on the ACP session/prompt channel, so the
    /// reference is delivered as markdown that the agent interprets. The path
    /// is stored in a deterministic location under %LOCALAPPDATA% so the user
    /// can find the attachment, and stale files are pruned on every save.
    /// </summary>
    internal sealed class ImageAttachmentService
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"
        };

        private readonly object sync = new();
        private readonly string rootDirectory;
        private readonly TimeSpan retention = TimeSpan.FromDays(7);

        public ImageAttachmentService()
        {
            rootDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuantivusOMP", "attachments");
            Directory.CreateDirectory(rootDirectory);
        }

        public string RootDirectory => rootDirectory;

        public bool IsImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension)) return false;
            return ImageExtensions.Contains(extension);
        }

        /// <summary>
        /// Saves a pasted bitmap to the attachment directory and returns the
        /// full path. PNG is preferred for lossless clipboard content.
        /// </summary>
        public string SaveClipboardImage(Image image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            lock (sync)
            {
                PruneStaleAttachments();
                var id = Guid.NewGuid().ToString("N");
                var fileName = $"clipboard-{id}.png";
                var fullPath = Path.Combine(rootDirectory, fileName);
                using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    image.Save(stream, ImageFormat.Png);
                }
                return fullPath;
            }
        }

        /// <summary>
        /// Copies a dropped image file into the attachment directory and returns
        /// the new path. Non-image inputs return null so callers can keep their
        /// existing text-file attachment behaviour.
        /// </summary>
        public string? IngestDroppedFile(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return null;
            if (!File.Exists(sourcePath)) return null;
            if (!IsImagePath(sourcePath)) return null;
            lock (sync)
            {
                PruneStaleAttachments();
                var id = Guid.NewGuid().ToString("N");
                var extension = Path.GetExtension(sourcePath);
                var fileName = $"dropped-{id}{extension}";
                var destination = Path.Combine(rootDirectory, fileName);
                File.Copy(sourcePath, destination, overwrite: true);
                return destination;
            }
        }

        public string FormatAttachmentReference(string fullPath, string? caption = null)
        {
            var escaped = (caption ?? "attached image").Replace("]", "\\]");
            return $"\r\n\r\n![{escaped}]({fullPath})\r\n";
        }

        /// <summary>
        /// Resolves <see cref="BitmapSource"/> from WPF clipboard payloads.
        /// Returns null when no bitmap is present.
        /// </summary>
        public static BitmapSource? TryReadBitmapSource(object? data)
        {
            if (data is BitmapSource source) return source;
            if (data is Image image) return ConvertToBitmapSource(image);
            return null;
        }

        private static BitmapSource ConvertToBitmapSource(Image image)
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void PruneStaleAttachments()
        {
            try
            {
                if (!Directory.Exists(rootDirectory)) return;
                var cutoff = DateTime.UtcNow - retention;
                foreach (var file in Directory.EnumerateFiles(rootDirectory))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                    }
                    catch { /* ignore individual file errors */ }
                }
            }
            catch { /* best-effort cleanup */ }
        }
    }
}

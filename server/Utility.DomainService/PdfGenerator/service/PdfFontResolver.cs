using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PdfSharp.Fonts;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Cross-platform font resolver for PdfSharp 6.
    ///
    /// PdfSharpCore shipped a bundled font resolver, but PdfSharp 6 requires one to be registered
    /// explicitly before any <c>XFont</c> is created on non-Windows hosts. This resolver maps the
    /// common family names used by the PDF engines (Arial, Calibri, Times, Courier, ...) onto the
    /// TrueType files that ship with a standard Linux image (Liberation, then DejaVu, then FreeFont)
    /// so that merge, stamp and page-number rendering work in Linux CI and containers.
    /// </summary>
    internal sealed class PdfFontResolver : IFontResolver
    {
        private static readonly string[] FontRoots =
        {
            "/app/fonts",
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/Library/Fonts",
            "/System/Library/Fonts",
            @"C:\Windows\Fonts"
        };

        private static readonly string[] SerifFamilies = { "times", "serif", "georgia", "garamond" };
        private static readonly string[] MonoFamilies = { "courier", "consolas", "mono", "menlo" };

        private readonly ConcurrentDictionary<string, byte[]> _fontCache = new();

        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var category = Categorize(familyName);
            var style = (bold ? "b" : string.Empty) + (italic ? "i" : string.Empty);
            var faceName = string.IsNullOrEmpty(style) ? category : $"{category}-{style}";
            return new FontResolverInfo(faceName);
        }

        public byte[] GetFont(string faceName)
        {
            return _fontCache.GetOrAdd(faceName, LoadFont);
        }

        private static string Categorize(string familyName)
        {
            var name = (familyName ?? string.Empty).ToLowerInvariant();
            if (Array.Exists(SerifFamilies, f => name.Contains(f))) return "serif";
            if (Array.Exists(MonoFamilies, f => name.Contains(f))) return "mono";
            return "sans";
        }

        private static byte[] LoadFont(string faceName)
        {
            var parts = faceName.Split('-');
            var category = parts[0];
            var style = parts.Length > 1 ? parts[1] : string.Empty;
            var bold = style.Contains('b');
            var italic = style.Contains('i');

            foreach (var fileName in CandidateFileNames(category, bold, italic))
            {
                var path = FindFile(fileName);
                if (path != null)
                {
                    return File.ReadAllBytes(path);
                }
            }

            // Last resort: any TrueType font on the system so rendering never hard-fails.
            var anyFont = FindAnyTrueTypeFont();
            if (anyFont != null)
            {
                return File.ReadAllBytes(anyFont);
            }

            throw new FileNotFoundException($"No system font could be resolved for face '{faceName}'.");
        }

        private static IEnumerable<string> CandidateFileNames(string category, bool bold, bool italic)
        {
            switch (category)
            {
                case "serif":
                    yield return LiberationName("LiberationSerif", bold, italic);
                    yield return DejaVuName("DejaVuSerif", bold);
                    yield return FreeFontName("FreeSerif", bold, italic);
                    break;
                case "mono":
                    yield return LiberationName("LiberationMono", bold, italic);
                    yield return DejaVuName("DejaVuSansMono", bold);
                    yield return FreeFontName("FreeMono", bold, italic);
                    break;
                default:
                    yield return LiberationName("LiberationSans", bold, italic);
                    yield return DejaVuName("DejaVuSans", bold);
                    yield return FreeFontName("FreeSans", bold, italic);
                    break;
            }
        }

        private static string LiberationName(string family, bool bold, bool italic)
        {
            var style = (bold, italic) switch
            {
                (true, true) => "BoldItalic",
                (true, false) => "Bold",
                (false, true) => "Italic",
                _ => "Regular"
            };
            return $"{family}-{style}.ttf";
        }

        private static string DejaVuName(string family, bool bold)
        {
            return bold ? $"{family}-Bold.ttf" : $"{family}.ttf";
        }

        private static string FreeFontName(string family, bool bold, bool italic)
        {
            var style = (bold, italic) switch
            {
                (true, true) => "BoldOblique",
                (true, false) => "Bold",
                (false, true) => "Oblique",
                _ => string.Empty
            };
            return $"{family}{style}.ttf";
        }

        private static string? FindFile(string fileName)
        {
            foreach (var root in FontRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var match = Directory
                        .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (match != null) return match;
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip unreadable directories.
                }
            }
            return null;
        }

        private static string? FindAnyTrueTypeFont()
        {
            foreach (var root in FontRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var match = Directory
                        .EnumerateFiles(root, "*.ttf", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (match != null) return match;
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip unreadable directories.
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Registers <see cref="PdfFontResolver"/> as the global PdfSharp font resolver exactly once
    /// per process. Runs on assembly load so any <c>XFont</c> created by the PDF engines (or their
    /// tests) has a resolver available before the first font is used.
    /// </summary>
    internal static class PdfFontConfig
    {
        private static readonly object Gate = new();
        private static bool _configured;

        [ModuleInitializer]
        internal static void Initialize()
        {
            EnsureConfigured();
        }

        internal static void EnsureConfigured()
        {
            if (_configured) return;
            lock (Gate)
            {
                if (_configured) return;
                if (GlobalFontSettings.FontResolver is null)
                {
                    GlobalFontSettings.FontResolver = new PdfFontResolver();
                }
                _configured = true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VSAgent.Services.Omp
{
    internal static class OmpExecutableLocator
    {
        public static string Find(string extensionDirectory)
        {
            var candidates = new List<string>
            {
                Path.Combine(extensionDirectory ?? string.Empty, "Runtime", "omp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "oh-my-pi", "omp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "omp.exe")
            };

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            candidates.AddRange(path.Split(Path.PathSeparator)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => Path.Combine(part.Trim(), "omp.exe")));

            return candidates.FirstOrDefault(File.Exists);
        }
    }
}

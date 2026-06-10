using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    public class EnvironmentVariableInstantProvider : IInstantResultProvider
    {
        public string Name => TranslationService.Get("Env_Name");

        // Matches %VARIABLE_NAME% pattern
        private static readonly Regex EnvVarRegex = new Regex(@"%[a-zA-Z0-9_]+%", RegexOptions.Compiled);

        public IEnumerable<InstantResultItem> GetInstantResults(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                yield break;

            string trimmed = query.Trim();

            // Only process if it contains at least one environment variable pattern
            if (!EnvVarRegex.IsMatch(trimmed))
                yield break;

            string expanded;
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(trimmed);
            }
            catch
            {
                yield break;
            }

            // If the expanded text is identical to input or still contains unexpanded % characters, it's invalid
            if (string.Equals(trimmed, expanded, StringComparison.OrdinalIgnoreCase) || expanded.Contains("%"))
                yield break;

            // Handle multi-path variables (like %PATH%, %PATHEXT%, %PSMODULEPATH%) which contain semicolons
            if (expanded.Contains(";"))
            {
                string[] paths = expanded.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (string path in paths)
                {
                    string cleanedPath = path.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(cleanedPath))
                        continue;

                    bool partIsDir = Directory.Exists(cleanedPath);
                    bool partIsFile = File.Exists(cleanedPath);
                    bool partExists = partIsDir || partIsFile;
                    string partTypeDesc = partIsDir
                        ? TranslationService.Get("Column_TypeFolder")
                        : (partIsFile ? TranslationService.Get("Column_TypeFile") : TranslationService.Get("Env_PathNotExist"));

                    yield return new InstantResultItem
                    {
                        Title = cleanedPath,
                        Description = partExists
                            ? TranslationService.Format("Env_SegmentOpenHint", partTypeDesc)
                            : TranslationService.Format("Env_SegmentCopyHint", partTypeDesc),
                        IconData = partExists
                            ? "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z" // Folder icon
                            : "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z", // Copy icon
                        IconColor = partExists ? "AccentBlue" : "TextSecondary",
                        ActionType = partExists ? "Execute" : "Copy",
                        ActionArgument = cleanedPath,
                        TabCompletion = cleanedPath
                    };
                }
            }
            else
            {
                // Single path variable (like %APPDATA%, %TEMP%)
                bool isDir = Directory.Exists(expanded);
                bool isFile = File.Exists(expanded);
                bool exists = isDir || isFile;
                string typeDesc = isDir
                    ? TranslationService.Get("Column_TypeFolder")
                    : (isFile ? TranslationService.Get("Column_TypeFile") : TranslationService.Get("Env_PathNotExist"));

                yield return new InstantResultItem
                {
                    Title = expanded,
                    Description = exists
                        ? TranslationService.Format("Env_ExpandOpenHint", typeDesc)
                        : TranslationService.Format("Env_ExpandCopyHint", typeDesc),
                    IconData = exists
                        ? "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z" // Folder icon
                        : "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z", // Copy icon
                    IconColor = exists ? "AccentBlue" : "TextSecondary",
                    ActionType = exists ? "Execute" : "Copy",
                    ActionArgument = expanded,
                    TabCompletion = expanded
                };
            }
        }
    }
}

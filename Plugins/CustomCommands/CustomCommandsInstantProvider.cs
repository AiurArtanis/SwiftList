using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CustomCommands;

public class CustomCommandsInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("CustomCommands_ProviderName");

    public class CommandItem
    {
        public bool Enabled { get; set; } = true;
        public string Keyword { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public bool RunSilently { get; set; } = false;
        public bool RunAsAdmin { get; set; } = false;
    }

    private List<CommandItem> LoadCommands()
    {
        try
        {
            var cmds = PluginSettingsService.GetSetting<List<CommandItem>>("SwiftList.Plugins.CustomCommands", "Commands", null!);
            if (cmds != null)
            {
                return cmds;
            }
        }
        catch
        {
            // Fallback
        }

        return new List<CommandItem>();
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            yield break;
        }

        var cmds = LoadCommands();
        if (cmds == null || cmds.Count == 0)
            yield break;

        var parts = query.Split(new[] { ' ' }, 2);
        var keyword = parts[0];
        var argSuffix = parts.Length > 1 ? parts[1] : string.Empty;

        var matchedCmds = cmds.Where(c => c.Enabled && string.Equals(c.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

        foreach (var cmd in matchedCmds)
        {
            // Compile final target executable path, arguments, working directory, and window style.
            // If WorkingDir is set, or RunSilently is true, we serialize options into a JSON payload starting with 'cc_exec:'
            // to let the executor launch the process directly without wrapping in cmd.exe /c start.
            var finalArg = "";
            var resolvedParam = cmd.Parameter ?? "";

            // Parse arguments supporting quotes (e.g., "a b" or 'a b')
            var parsedArgs = new List<string>();
            if (!string.IsNullOrEmpty(argSuffix))
            {
                var inQuotes = false;
                var quoteChar = '\0';
                var currentArg = new System.Text.StringBuilder();

                for (var i = 0; i < argSuffix.Length; i++)
                {
                    var c = argSuffix[i];
                    if ((c == '"' || c == '\'') && (i == 0 || argSuffix[i - 1] != '\\'))
                    {
                        if (inQuotes && c == quoteChar)
                        {
                            inQuotes = false;
                        }
                        else if (!inQuotes)
                        {
                            inQuotes = true;
                            quoteChar = c;
                        }
                        else
                        {
                            currentArg.Append(c);
                        }
                    }
                    else if (c == ' ' && !inQuotes)
                    {
                        if (currentArg.Length > 0)
                        {
                            parsedArgs.Add(currentArg.ToString());
                            currentArg.Clear();
                        }
                    }
                    else
                    {
                        currentArg.Append(c);
                    }
                }
                if (currentArg.Length > 0)
                {
                    parsedArgs.Add(currentArg.ToString());
                }
            }

            // Positional placeholders: %s1/{1} .. %sn/{n} -> the n-th argument (1-based).
            // Single regex pass so %s1 can't match inside %s10, and so a leftover positional
            // token can't be clobbered by the "all arguments" replacement below.
            // Out-of-range indices resolve to an empty string. We quote each value ourselves
            // so it stays a single argument — users must NOT quote placeholders themselves.
            resolvedParam = System.Text.RegularExpressions.Regex.Replace(resolvedParam, @"%s(\d+)|\{(\d+)\}", m =>
            {
                var digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                var value = int.TryParse(digits, out var n) && n >= 1 && n <= parsedArgs.Count
                    ? parsedArgs[n - 1]
                    : string.Empty;
                // A missing/out-of-range argument vanishes rather than becoming an empty "".
                return value.Length == 0 ? string.Empty : ArgQuoting.Quote(value);
            });

            // "All arguments as one" placeholders: %s or {} -> the whole input as a single
            // quoted argument (empty input -> nothing).
            var allArgs = string.IsNullOrEmpty(argSuffix) ? string.Empty : ArgQuoting.Quote(argSuffix);
            if (resolvedParam.Contains("%s"))
            {
                resolvedParam = resolvedParam.Replace("%s", allArgs);
            }
            if (resolvedParam.Contains("{}"))
            {
                resolvedParam = resolvedParam.Replace("{}", allArgs);
            }

            // Input is used only through placeholders. A template with no placeholder runs with
            // exactly its configured parameters — trailing input is not appended.

            if (!string.IsNullOrWhiteSpace(cmd.WorkingDir) || cmd.RunSilently)
            {
                var payload = new
                {
                    Path = cmd.Path,
                    Arguments = resolvedParam,
                    WorkingDir = cmd.WorkingDir,
                    RunSilently = cmd.RunSilently,
                    RunAsAdmin = cmd.RunAsAdmin
                };
                finalArg = "cc_exec:" + System.Text.Json.JsonSerializer.Serialize(payload);
            }
            else
            {
                // Simple run (backward compatible)
                if (cmd.RunAsAdmin)
                {
                    finalArg += "runas:";
                }

                if (cmd.Path.Contains(" ") && !cmd.Path.StartsWith("\""))
                {
                    finalArg += $"\"{cmd.Path}\"";
                }
                else
                {
                    finalArg += cmd.Path;
                }

                if (!string.IsNullOrWhiteSpace(resolvedParam))
                {
                    finalArg += $" {resolvedParam}";
                }
            }

            var adminSuffix = cmd.RunAsAdmin ? " " + TranslationService.Get("CustomCommands_ResultDescAdmin") : "";
            var title = !string.IsNullOrEmpty(cmd.Title) ? cmd.Title : cmd.Keyword;

            // Default premium Command Terminal icon: M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-8 12H8v-2h4v2zm6-4h-6V8h6v4z
            var iconData = !string.IsNullOrWhiteSpace(cmd.Icon) ? cmd.Icon.Trim() : "M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-8 12H8v-2h4v2zm6-4h-6V8h6v4z";

            yield return new InstantResultItem
            {
                Title = TranslationService.Format("CustomCommands_ResultTitle", title),
                Description = string.Format(TranslationService.Get("CustomCommands_ResultDesc"), cmd.Path, adminSuffix),
                IconData = iconData,
                ActionType = "Execute",
                ActionArgument = finalArg
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var parts = query.Split(new[] { ' ' }, 2);
        var keyword = parts[0];
        if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            var mask = new bool[text.Length];
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                for (var i = idx; i < idx + keyword.Length && i < mask.Length; i++)
                {
                    mask[i] = true;
                }
            }
            return mask;
        }
        return null;
    }
}

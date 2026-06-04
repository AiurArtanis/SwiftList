using System;
using System.Collections.Generic;
using System.Linq;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    public class CommandInstantProvider : IInstantResultProvider
    {
        public string Name => TranslationService.Get("Command_Name");

        public IEnumerable<InstantResultItem> GetInstantResults(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                yield break;

            string trimmed = query.Trim();
            bool isAdmin = trimmed.StartsWith("#");
            bool isNormal = trimmed.StartsWith("$");

            if (!isAdmin && !isNormal)
                yield break;

            string target = trimmed.Substring(1).Trim();
            if (string.IsNullOrEmpty(target))
                yield break;

            string actionArg;
            string title;
            string desc;

            if (isAdmin)
            {
                actionArg = $"runas:cmd.exe /k {target}";
                title = $"以管理员权限运行命令: {target}";
                desc = "打开提升权限的命令提示符窗口并执行该命令";
            }
            else
            {
                actionArg = $"cmd.exe /k {target}";
                title = $"运行命令: {target}";
                desc = "打开命令提示符窗口并执行该命令";
            }

            yield return new InstantResultItem
            {
                Title = title,
                Description = desc,
                IconData = "M20 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 12H4V8h16v10zM12 12c0-.55-.45-1-1-1H7c-.55 0-1 .45-1 1s.45 1 1 1h4c.55 0 1-.45 1-1zm6 2h-4c-.55 0-1 .45-1 1s.45 1 1 1h4c.55 0 1-.45 1-1s-.45-1-1-1z",
                IconColor = "DefaultPluginIconColor",
                ActionType = "Execute",
                ActionArgument = actionArg,
                TabCompletion = query
            };
        }
    }
}

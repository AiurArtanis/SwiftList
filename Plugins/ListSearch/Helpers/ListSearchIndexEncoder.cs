using System.Text;

namespace SwiftList.Plugins.ListSearch.Helpers;

public static class ListSearchIndexEncoder
{
    public static bool IsListBoxClass(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("ListBox", StringComparison.OrdinalIgnoreCase) ||
               className.Contains(".ListBox.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsListViewClass(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
               className.Contains(".SysListView32.", StringComparison.OrdinalIgnoreCase);
    }

    public static string EncodeIndex(string item, int index)
    {
        var sbSuffix = new StringBuilder();
        sbSuffix.Append('\u200D'); // Start marker
        var binary = Convert.ToString(index, 2);
        foreach (var bit in binary)
        {
            sbSuffix.Append(bit == '1' ? '\u200C' : '\u200B');
        }
        return item + sbSuffix.ToString();
    }

    public static int DecodeIndex(string path)
    {
        if (string.IsNullOrEmpty(path)) return -1;
        var markerIndex = path.LastIndexOf('\u200D');
        if (markerIndex == -1 || markerIndex == path.Length - 1) return -1;

        var sb = new StringBuilder();
        for (var i = markerIndex + 1; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '\u200C') sb.Append('1');
            else if (c == '\u200B') sb.Append('0');
            else break;
        }
        try
        {
            return Convert.ToInt32(sb.ToString(), 2);
        }
        catch
        {
            return -1;
        }
    }
}

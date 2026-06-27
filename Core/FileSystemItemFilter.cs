namespace SwiftList.Core;

internal static class FileSystemItemFilter
{
    public static bool IsHiddenOrSystem(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public static bool IsHiddenOrSystem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return IsHiddenOrSystem(File.GetAttributes(path));
        }
        catch
        {
            return false;
        }
    }
}

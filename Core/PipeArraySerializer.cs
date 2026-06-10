namespace SwiftList.Core;

public static class PipeArraySerializer
{
    public static void WriteStringArray(BinaryWriter writer, string[]? values)
    {
        writer.Write(values?.Length ?? 0);
        if (values == null) return;
        foreach (var value in values)
            writer.Write(value ?? string.Empty);
    }

    public static string[] ReadStringArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new string[count];
        for (var i = 0; i < count; i++)
            values[i] = reader.ReadString();
        return values;
    }

    public static void WriteIntArray(BinaryWriter writer, int[]? values)
    {
        writer.Write(values?.Length ?? 0);
        if (values == null) return;
        foreach (var value in values)
            writer.Write(value);
    }

    public static int[] ReadIntArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new int[count];
        for (var i = 0; i < count; i++)
            values[i] = reader.ReadInt32();
        return values;
    }
}

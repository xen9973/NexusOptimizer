namespace NexusOptimizer.Services;

public static class ByteSize
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
            return "0 Б";

        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} Б" : $"{value:0.#} {Units[unit]}";
    }
}

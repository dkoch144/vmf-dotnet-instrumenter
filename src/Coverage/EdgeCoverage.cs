namespace Vmf.ManagedCoverage;

public static class EdgeCoverage
{
    private const int MapSize = 1 << 16;
    private static readonly byte[] Map = new byte[MapSize];

    [ThreadStatic]
    private static int previous;

    public static void Visit(int location)
    {
        int edge = (previous ^ location) & (MapSize - 1);
        previous = location >> 1;
        lock (Map)
        {
            if (Map[edge] < byte.MaxValue)
                Map[edge]++;
        }
    }

    public static void WriteTo(string path)
    {
        byte[] snapshot;
        lock (Map)
            snapshot = (byte[])Map.Clone();

        File.WriteAllBytes(path, snapshot);
    }
}

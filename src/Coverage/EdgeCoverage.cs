namespace Vmf.ManagedCoverage;

public static class EdgeCoverage
{
    private const int MapSize = 1 << 16;
    private static readonly byte[] Map = new byte[MapSize];
    private static long generation;

    [ThreadStatic]
    private static int previous;

    [ThreadStatic]
    private static long threadGeneration;

    public static void Visit(int location)
    {
        lock (Map)
        {
            if (threadGeneration != generation)
            {
                previous = 0;
                threadGeneration = generation;
            }
            int edge = (previous ^ location) & (MapSize - 1);
            previous = location >> 1;
            if (Map[edge] < byte.MaxValue)
                Map[edge]++;
        }
    }

    public static void Reset()
    {
        lock (Map)
        {
            Array.Clear(Map);
            generation++;
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

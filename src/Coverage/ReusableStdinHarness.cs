using System.Buffers.Binary;

namespace Vmf.ManagedCoverage;

public static class ReusableStdinHarness
{
    public const string Argument = "--vmf-reuse-stdin";

    // The callback handles expected input rejection. The reset action must quiesce
    // background work before returning so it cannot affect the next testcase.
    public static int Run(string coveragePath, Action<Stream> runTestcase, Action resetTestcase)
    {
        using var input = Console.OpenStandardInput();
        using var protocol = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);
        protocol.WriteByte((byte)'R');
        protocol.Flush();

        Span<byte> header = stackalloc byte[sizeof(int)];
        while (true)
        {
            int count = input.ReadAtLeast(header, header.Length, false);
            if (count == 0)
                return 0;
            if (count != header.Length)
                return 1;
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 0)
                return 1;
            var bytes = new byte[length];
            input.ReadExactly(bytes);

            using var testcase = new MemoryStream(bytes, writable: false);
            EdgeCoverage.Reset();
            runTestcase(testcase);
            EdgeCoverage.WriteTo(coveragePath);
            try
            {
                resetTestcase();
            }
            catch (Exception error)
            {
                protocol.WriteByte((byte)'X');
                protocol.Flush();
                Console.Error.WriteLine(error);
                return 1;
            }
            protocol.WriteByte((byte)'C');
            protocol.Flush();
        }
    }
}

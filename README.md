# VMF .NET assembly instrumenter

This tool rewrites selected managed assemblies offline using Mono.Cecil.
Like SharpFuzz, it adds IL probes at control-flow boundaries and records edges
between consecutive probes. VMF is the intended consumer of the coverage.

## Build and use

Install the .NET 10 SDK and build the instrumenter:

    dotnet build src/Instrumenter --output artifacts/tool

Reference the Coverage project from your managed fuzz harness so that
`Vmf.ManagedCoverage.dll` is available through the harness's .NET dependencies.
Build the framework-dependent harness and copy its output into a staging
directory. Pass only the assemblies you want to instrument:

    dotnet artifacts/tool/Instrumenter.dll staged/Target.dll

Run the harness from the staged output. At the end of the testcase, the harness
calls `Vmf.ManagedCoverage.EdgeCoverage.WriteTo(path)` to write a raw 65,536-byte
edge-count bitmap for VMF. Run each testcase in a fresh process. Probe IDs are
derived from the original assembly, method and instruction location.
Thread-local predecessor state keeps transitions on the same thread.

The rewriter supports unsigned single-module IL assemblies. It skips methods
with explicit tail calls and rewrites IL without debugging symbols. Keep the
original build output for debugging and stage a fresh copy for each rewrite.

## Verify

Run the end-to-end fixture with CMake and .NET 10 on your PATH:

    cmake -DDOTNET=dotnet -DOUTPUT=artifacts/verify -P tests/Verify.cmake

The fixture verifies that instrumented code still behaves correctly, that edge
coverage distinguishes branches and repeats across processes, and that the
original assembly stays intact.

Licensed under GPL-2.0-only.

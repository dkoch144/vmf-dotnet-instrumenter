using System.Buffers.Binary;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Vmf.ManagedCoverage;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Instrumenter <staged-assembly.dll> [...]");
    return 2;
}

foreach (string path in args)
{
    try
    {
        Rewrite(Path.GetFullPath(path));
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"{path}: {error.Message}");
        return 1;
    }
}
return 0;

static void Rewrite(string path)
{
    using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
    if (module.Assembly is null || module.Assembly.Modules.Count != 1 ||
        (module.Attributes & ModuleAttributes.ILOnly) == 0 || module.Assembly.Name.HasPublicKey)
        throw new InvalidOperationException("Select an unsigned, single-module IL assembly.");

    if (module.Assembly.Name.Name == typeof(EdgeCoverage).Assembly.GetName().Name)
        throw new InvalidOperationException("The coverage runtime cannot instrument itself.");

    var methods = module.GetTypes().SelectMany(type => type.Methods)
        .Where(method => method.HasBody && method.Body.Instructions.Count > 0).ToArray();
    var visit = module.ImportReference(typeof(EdgeCoverage).GetMethod(nameof(EdgeCoverage.Visit))!);
    if (methods.SelectMany(method => method.Body.Instructions).Any(instruction =>
        instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference called &&
        called.FullName == visit.FullName &&
        called.DeclaringType.Scope.Name == visit.DeclaringType.Scope.Name))
        throw new InvalidOperationException("Assembly already contains VMF probes. Stage a fresh copy.");

    foreach (var method in methods)
        RewriteMethod(method, visit, module.Mvid);

    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        module.Write(temporary);
        File.Move(temporary, path, overwrite: true);
    }
    finally
    {
        File.Delete(temporary);
    }
}

static void RewriteMethod(MethodDefinition method, MethodReference visit, Guid moduleId)
{
    var body = method.Body;
    var instructions = body.Instructions.ToArray();

    // Preserve the required tail-call instruction sequence.
    if (instructions.Any(instruction => instruction.OpCode == OpCodes.Tail))
        return;

    var starts = new HashSet<Instruction> { instructions[0] };
    foreach (var instruction in instructions)
    {
        if (instruction.Operand is Instruction target)
            starts.Add(target);
        else if (instruction.Operand is Instruction[] targets)
            starts.UnionWith(targets);

        if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch && instruction.Next is not null)
            starts.Add(instruction.Next);
    }
    foreach (var handler in body.ExceptionHandlers)
    {
        starts.Add(handler.HandlerStart);
        if (handler.FilterStart is not null)
            starts.Add(handler.FilterStart);
    }

    body.SimplifyMacros();
    var processor = body.GetILProcessor();
    var probes = new Dictionary<Instruction, Instruction>();
    foreach (var start in instructions.Where(starts.Contains))
    {
        var load = processor.Create(OpCodes.Ldc_I4, Location(moduleId, method.MetadataToken.ToInt32(), start.Offset));
        processor.InsertBefore(start, load);
        processor.InsertAfter(load, processor.Create(OpCodes.Call, visit));
        probes.Add(start, load);
    }

    Instruction Redirect(Instruction target) => probes.GetValueOrDefault(target, target);
    foreach (var instruction in instructions)
    {
        if (instruction.Operand is Instruction target)
            instruction.Operand = Redirect(target);
        else if (instruction.Operand is Instruction[] targets)
            instruction.Operand = targets.Select(Redirect).ToArray();
    }
    foreach (var handler in body.ExceptionHandlers)
    {
        handler.TryStart = Redirect(handler.TryStart);
        if (handler.TryEnd is not null)
            handler.TryEnd = Redirect(handler.TryEnd);
        handler.HandlerStart = Redirect(handler.HandlerStart);
        if (handler.HandlerEnd is not null)
            handler.HandlerEnd = Redirect(handler.HandlerEnd);
        if (handler.FilterStart is not null)
            handler.FilterStart = Redirect(handler.FilterStart);
    }
}

static int Location(Guid moduleId, int methodToken, int offset)
{
    Span<byte> identity = stackalloc byte[24];
    moduleId.TryWriteBytes(identity);
    BinaryPrimitives.WriteInt32LittleEndian(identity[16..], methodToken);
    BinaryPrimitives.WriteInt32LittleEndian(identity[20..], offset);
    Span<byte> hash = stackalloc byte[32];
    SHA256.HashData(identity, hash);
    // Use the same 16-bit location space as the coverage bitmap.
    return BinaryPrimitives.ReadUInt16LittleEndian(hash);
}

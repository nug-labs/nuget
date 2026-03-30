using System.Text;
using System.Text.Json;
using NugLabs.Models;
using Wasmtime;

namespace NugLabs.Wasm;

/// <summary>
/// Thin Wasmtime bridge over the shared <c>nuglabs_core.wasm</c> C ABI.
/// </summary>
internal sealed class WasmBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Engine _engine;
    private readonly Store _store;
    private readonly Linker _linker;
    private readonly Module _module;
    private readonly Instance _instance;
    private readonly Memory _memory;
    private readonly int _handle;

    private WasmBridge(Engine engine, Store store, Linker linker, Module module, Instance instance, Memory memory, int handle)
    {
        _engine = engine;
        _store = store;
        _linker = linker;
        _module = module;
        _instance = instance;
        _memory = memory;
        _handle = handle;
    }

    public static WasmBridge Create(string? explicitWasmPath = null)
    {
        var wasmPath = ResolveWasmPath(explicitWasmPath)
            ?? throw new FileNotFoundException("Could not locate nuglabs_core.wasm. Set NugLabsClientOptions.WasmPath or NUGLABS_WASM_PATH.");
        var engine = new Engine();
        var store = new Store(engine);
        var linker = new Linker(engine);
        var module = Module.FromFile(engine, wasmPath);
        var instance = linker.Instantiate(store, module);
        var memory = instance.GetMemory("memory") ?? throw new InvalidOperationException("WASM export `memory` not found.");
        var create = instance.GetFunction("nuglabs_engine_create") ?? throw new InvalidOperationException("WASM export `nuglabs_engine_create` not found.");
        var rawHandle = create.Invoke();
        var handle = Convert.ToInt32(rawHandle);
        if (handle == 0)
        {
            throw new InvalidOperationException("nuglabs_engine_create returned 0.");
        }
        return new WasmBridge(engine, store, linker, module, instance, memory, handle);
    }

    public void LoadDataset(string datasetJson)
    {
        WriteStringCall("nuglabs_engine_load_dataset", datasetJson);
    }

    public void LoadRules(string rulesJson)
    {
        WriteStringCall("nuglabs_engine_load_rules", rulesJson);
    }

    public Strain? GetStrain(string name)
    {
        var raw = CallJsonOut("nuglabs_engine_get_strain", name);
        if (raw == "null")
        {
            return null;
        }
        return JsonSerializer.Deserialize<Strain>(raw, JsonOptions);
    }

    public IReadOnlyList<Strain> SearchStrains(string query)
    {
        var raw = CallJsonOut("nuglabs_engine_search", query);
        return JsonSerializer.Deserialize<List<Strain>>(raw, JsonOptions) ?? [];
    }

    public IReadOnlyList<Strain> GetAllStrains()
    {
        var alloc = RequireFunction("nuglabs_alloc");
        var dealloc = RequireFunction("nuglabs_dealloc");
        var fn = RequireFunction("nuglabs_engine_get_all_strains");

        var outPtrSlot = Convert.ToInt32(alloc.Invoke(4)!);
        var outLenSlot = Convert.ToInt32(alloc.Invoke(4)!);
        try
        {
            var status = Convert.ToInt32(fn.Invoke(_handle, outPtrSlot, outLenSlot)!);
            if (status != 0)
            {
                throw new InvalidOperationException($"nuglabs_engine_get_all_strains failed with status {status}.");
            }
            var resultPtr = _memory.ReadInt32(outPtrSlot);
            var resultLen = _memory.ReadInt32(outLenSlot);
            var raw = _memory.ReadString(resultPtr, resultLen, Encoding.UTF8);
            dealloc.Invoke(resultPtr, resultLen);
            return JsonSerializer.Deserialize<List<Strain>>(raw, JsonOptions) ?? [];
        }
        finally
        {
            dealloc.Invoke(outPtrSlot, 4);
            dealloc.Invoke(outLenSlot, 4);
        }
    }

    public void Dispose()
    {
        try
        {
            var destroy = _instance.GetFunction("nuglabs_engine_destroy");
            destroy?.Invoke(_handle);
        }
        catch
        {
            // Best effort disposal.
        }
        _store.Dispose();
        _module.Dispose();
        _linker.Dispose();
        _engine.Dispose();
    }

    private void WriteStringCall(string functionName, string payload)
    {
        var alloc = RequireFunction("nuglabs_alloc");
        var dealloc = RequireFunction("nuglabs_dealloc");
        var fn = RequireFunction(functionName);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var ptr = bytes.Length == 0 ? 0 : Convert.ToInt32(alloc.Invoke(bytes.Length)!);
        try
        {
            if (bytes.Length > 0)
            {
                bytes.CopyTo(_memory.GetSpan(ptr, bytes.Length));
            }
            var status = Convert.ToInt32(fn.Invoke(_handle, ptr, bytes.Length)!);
            if (status != 0)
            {
                throw new InvalidOperationException($"{functionName} failed with status {status}.");
            }
        }
        finally
        {
            if (bytes.Length > 0 && ptr != 0)
            {
                dealloc.Invoke(ptr, bytes.Length);
            }
        }
    }

    private string CallJsonOut(string functionName, string input)
    {
        var alloc = RequireFunction("nuglabs_alloc");
        var dealloc = RequireFunction("nuglabs_dealloc");
        var fn = RequireFunction(functionName);

        var inputBytes = Encoding.UTF8.GetBytes(input);
        var inputPtr = inputBytes.Length == 0 ? 0 : Convert.ToInt32(alloc.Invoke(inputBytes.Length)!);
        var outPtrSlot = Convert.ToInt32(alloc.Invoke(4)!);
        var outLenSlot = Convert.ToInt32(alloc.Invoke(4)!);
        try
        {
            if (inputBytes.Length > 0)
            {
                inputBytes.CopyTo(_memory.GetSpan(inputPtr, inputBytes.Length));
            }
            var status = Convert.ToInt32(fn.Invoke(_handle, inputPtr, inputBytes.Length, outPtrSlot, outLenSlot)!);
            if (status != 0)
            {
                throw new InvalidOperationException($"{functionName} failed with status {status}.");
            }
            var resultPtr = _memory.ReadInt32(outPtrSlot);
            var resultLen = _memory.ReadInt32(outLenSlot);
            var raw = _memory.ReadString(resultPtr, resultLen, Encoding.UTF8);
            dealloc.Invoke(resultPtr, resultLen);
            return raw;
        }
        finally
        {
            if (inputBytes.Length > 0 && inputPtr != 0)
            {
                dealloc.Invoke(inputPtr, inputBytes.Length);
            }
            dealloc.Invoke(outPtrSlot, 4);
            dealloc.Invoke(outLenSlot, 4);
        }
    }

    private Function RequireFunction(string name)
    {
        return _instance.GetFunction(name) ?? throw new InvalidOperationException($"WASM export `{name}` not found.");
    }

    private static string? ResolveWasmPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var env = Environment.GetEnvironmentVariable("NUGLABS_WASM_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "NugLabs", "wasm", "nuglabs_core.wasm"),
            Path.Combine(AppContext.BaseDirectory, "wasm", "nuglabs_core.wasm"),
            Path.Combine(AppContext.BaseDirectory, "nuglabs_core.wasm")
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            candidates.Add(Path.Combine(current.FullName, "npm", "wasm", "nuglabs_core.wasm"));
            candidates.Add(Path.Combine(current.FullName, "core", "target", "wasm32-unknown-unknown", "release", "nuglabs_core.wasm"));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}

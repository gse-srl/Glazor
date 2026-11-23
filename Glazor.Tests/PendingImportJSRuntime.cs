using Microsoft.JSInterop;

namespace Glazor.Tests;

/// <summary>
/// A JS runtime whose module imports complete only when the test says so, each with a module that counts its disposals.
/// </summary>
internal sealed class PendingImportJSRuntime : IJSRuntime
{
    public readonly List<(string Path, TaskCompletionSource<IJSObjectReference> Tcs)> Imports = new();
    public readonly List<CountingModule> Modules = new();
    public readonly CountingModule Module = new();
    public TaskCompletionSource<IJSObjectReference> Import => Imports[0].Tcs;

    public sealed class CountingModule : IJSObjectReference
    {
        public int Disposed;
        public readonly List<string> Calls = new();
        public string? FailOn;   // the function that throws, as one called on an element already gone does
        public ValueTask DisposeAsync() { Disposed++; return ValueTask.CompletedTask; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return identifier == FailOn ? throw new JSException($"{identifier} failed") : ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier != "import")
            return default!;
        TaskCompletionSource<IJSObjectReference> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Imports.Add(((string)args![0]!, tcs));
        return (TValue)(object)await tcs.Task;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);

    public int ImportsOf(string module) => Imports.Count(i => i.Path.EndsWith(module));

    /// <summary>Completes every pending import with a fresh module of its own.</summary>
    public void CompleteAll()
    {
        foreach (var (_, tcs) in Imports.Where(i => !i.Tcs.Task.IsCompleted).ToList())
        {
            CountingModule module = new();
            Modules.Add(module);
            tcs.SetResult(module);
        }
    }
}

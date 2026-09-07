# HavenOS llama.cpp runtime seam

## Decision

HavenOS keeps the existing Ollama provider as the default local-model path for this slice. The current source proves that Haven's Ollama implementation is a replaceable HTTP/provider wrapper, but it also owns Ollama-specific model installation/removal, streaming/thinking parsing, tool-call parsing, and usage capture. No repository-local benchmark or compatibility test currently proves that replacing those semantics with direct llama.cpp would preserve functionality or improve performance.

This slice therefore adds an opt-in llama.cpp process seam without changing existing provider routing.

## Scope

The shared OS-root runtime owns only a llama.cpp child process started explicitly by Haven. It does not install llama.cpp, alter system services, change boot/login, modify drivers, change power plans, adjust process priority, or terminate unrelated processes.

The launch plan is fail-closed:

- disabled by default;
- explicit absolute executable and GGUF model paths are required;
- the server binds to `127.0.0.1` only;
- the web UI is disabled;
- the port is restricted to the unprivileged range `1024-65535`;
- context and parallel-request counts are bounded;
- process arguments use `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`;
- the endpoint exposed by the seam is the llama.cpp OpenAI-compatible `/v1/` root.

Both `llama-server` and the newer unified `llama serve` form are supported through `UseUnifiedCli`.

## Configuration

Configuration is process-local and can be supplied directly through `LlamaCppRuntimeOptions` or through these environment variables:

- `HAVEN_LLAMA_CPP_ENABLED`
- `HAVEN_LLAMA_CPP_EXECUTABLE`
- `HAVEN_LLAMA_CPP_MODEL`
- `HAVEN_LLAMA_CPP_UNIFIED_CLI`
- `HAVEN_LLAMA_CPP_PORT`
- `HAVEN_LLAMA_CPP_CONTEXT_SIZE`
- `HAVEN_LLAMA_CPP_PARALLEL`
- `HAVEN_LLAMA_CPP_ALWAYS_LOADED`

Malformed numeric environment values remain invalid and do not silently fall back to runnable defaults.

## Always-loaded mode

`AlwaysLoaded=true` does not add a boot service. A non-critical Haven composition root may explicitly register the seam with `AddHavenLlamaCppRuntime` and call `StartIfAlwaysLoadedAsync` after normal application startup. If the executable, model, or configuration is unavailable, the call returns a stopped status rather than making HavenOS startup fail.

The model remains loaded only for the lifetime of the Haven-owned llama.cpp process. `StopAsync`/disposal targets only that owned process handle.

## Deliberately not wired yet

This slice does not adapt llama.cpp's OpenAI-compatible API to `IOllamaClient`/`ILocalOllamaClient`, and it does not replace `OllamaClient` or `LocalOllamaClientAdapter`. That integration requires focused compatibility coverage for Haven's existing streaming, thinking, tools, model-management, and usage-accounting behavior, plus reproducible performance evidence on target HavenOS hardware.

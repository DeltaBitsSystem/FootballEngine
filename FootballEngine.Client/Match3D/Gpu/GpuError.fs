namespace FootballEngine.Client.Match3D

/// Every GPU failure the viewer can report. Creation code returns these as values; only the renderer facade
/// turns one into an exception, so callers receive a single message format.
type GpuError =
    | AdapterUnavailable
    | DeviceLost of message: string
    | CreationFailed of label: string
    | ShaderCompile of file: string * message: string
    | SurfaceLost

module GpuError =
    let describe (error: GpuError) : string =
        match error with
        | AdapterUnavailable -> "WebGPU found no surface-compatible adapter."
        | DeviceLost message -> $"WebGPU lost the device: {message}"
        | CreationFailed label -> $"WebGPU failed to create {label}."
        | ShaderCompile(file, message) -> $"WebGPU shader {file} failed: {message}"
        | SurfaceLost -> "WebGPU lost the presentation surface."

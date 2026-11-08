namespace FootballEngine.Client.Match3D

open System
open System.IO

/// WGSL is assembled from ordered segments because later files use constants and functions from earlier ones.
/// Segment order is the only dependency mechanism available here, so it is kept explicit at the call site.
module ShaderLibrary =
    let compose (segments: string list) : string = String.Concat segments

    let shaderDirectory () =
        Path.Combine(AppContext.BaseDirectory, "Match3D", "Shaders")

    let readFile (name: string) : Result<string, GpuError> =
        let path = Path.Combine(shaderDirectory (), name)

        if File.Exists path then
            Ok(File.ReadAllText path)
        else
            Error(ShaderCompile(name, $"the shader file is missing at {path}"))

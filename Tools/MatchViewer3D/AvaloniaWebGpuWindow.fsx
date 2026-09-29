#r "nuget: Avalonia, 12.1.1"
#r "nuget: Avalonia.Fonts.Inter, 12.1.1"
#r "nuget: ProGPU.Avalonia.Rendering, 12.1.1-preview.63"
#r "nuget: ProGPU.Avalonia.SilkNet, 12.1.1-preview.63"

open System
open System.IO
open System.Numerics
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.ProGpu
open Avalonia.Rendering.SceneGraph
open ProGPU.Scene.Extensions

// FSI's base directory belongs to the SDK, so NuGet's native assets aren't copied
// there. Set an isolated script-only base directory; production projects use
// NuGet's normal RID asset copying instead.
let nativePackageRoot =
    match Environment.GetEnvironmentVariable("NUGET_PACKAGES") with
    | null
    | "" -> Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
    | path -> path

let nativeLibrary =
    Path.Combine(nativePackageRoot, "silk.net.webgpu.native.wgpu", "2.23.0", "runtimes", "linux-x64", "native", "libwgpu_native.so")

if not (File.Exists nativeLibrary) then
    failwithf "Linux x64 native WebGPU library missing: %s" nativeLibrary

let scriptRuntime = Path.Combine(Path.GetTempPath(), "opencode", "fe-webgpu-fsi")
let nativeDir = Path.Combine(scriptRuntime, "runtimes", "linux-x64", "native")
Directory.CreateDirectory(nativeDir) |> ignore
let link = Path.Combine(nativeDir, "libwgpu_native.so")

if not (File.Exists link) then
    File.CreateSymbolicLink(link, nativeLibrary) |> ignore

AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", scriptRuntime + string Path.DirectorySeparatorChar)

type ProbeOperation(bounds: Rect) =
    let shaderSource =
        """// One ray/plane and one ray/sphere intersection per pixel; O(1) memory and work per pixel.
fn mainImage(fragCoord: vec2<f32>) -> vec4<f32> {
    let uv = (fragCoord / inputs.iResolution.xy) * 2.0 - vec2<f32>(1.0, 1.0);
    let origin = vec3<f32>(0.0, 20.0, 34.0);
    let direction = normalize(vec3<f32>(uv.x * 0.9, -0.65 + uv.y * 0.45, -1.0));
    let groundDistance = -origin.y / direction.y;
    let ground = origin + direction * groundDistance;
    let sphere = vec3<f32>(0.0, 1.5, -12.0);
    let relative = origin - sphere;
    let b = dot(relative, direction);
    let discriminant = b * b - dot(relative, relative) + 2.25;
    var color = vec3<f32>(0.025, 0.06, 0.085);

    if (groundDistance > 0.0 && abs(ground.x) <= 52.5 && abs(ground.z) <= 34.0) {
        let stripe = i32(floor((ground.z + 34.0) / 8.5)) % 2;
        let grass = select(vec3<f32>(0.10, 0.32, 0.11), vec3<f32>(0.14, 0.38, 0.12), stripe == 0);
        color = grass * (0.8 + 0.2 * clamp(dot(vec3<f32>(0.0, 1.0, 0.0), normalize(vec3<f32>(0.3, 1.0, 0.25))), 0.0, 1.0));
        if (abs(ground.x) > 52.3 || abs(ground.z) > 33.8 || abs(ground.z) < 0.12) {
            color = vec3<f32>(0.88, 0.88, 0.80);
        }
    }

    if (discriminant > 0.0) {
        let sphereDistance = -b - sqrt(discriminant);
        if (sphereDistance > 0.0 && sphereDistance < groundDistance) {
            let normal = normalize(origin + direction * sphereDistance - sphere);
            color = vec3<f32>(0.89, 0.88, 0.78) * (0.28 + 0.72 * max(dot(normal, normalize(vec3<f32>(0.4, 0.9, 0.5))), 0.0));
        }
    }

    return vec4<f32>(color, 1.0);
}
"""

    interface ICustomDrawOperation with
        member _.Bounds = bounds
        member _.HitTest _ = false
        member _.Equals _ = false
        member _.Dispose() = ()

        member _.Render(context: ImmediateDrawingContext) =
            let feature = context.TryGetFeature<IProGpuApiLeaseFeature>()

            if isNull feature then
                failwith "WebGPU API lease unavailable"

            use lease = feature.Lease()
            let width = float32 bounds.Width
            let height = float32 bounds.Height
            let shader = ShaderToyParams()
            shader.Rect <- ProGPU.Scene.Rect(0f, 0f, width, height)
            shader.Resolution <- Vector3(width, height, 1f)
            shader.ShaderKey <- "football-engine-ray-scene-v1"
            shader.ShaderSource <- shaderSource

            lease.DrawingContext.DrawExtension(
                ProGPU.Scene.CompositorBuiltInExtensions.ShaderToy,
                dataParam = shader,
                transform = lease.CurrentTransform
            )

type ProbeSurface() =
    inherit Control()

    override this.Render(context: DrawingContext) =
        base.Render(context)
        context.Custom(new ProbeOperation(Rect(this.Bounds.Size)))

type ProbeApp() =
    inherit Application()

    override _.Initialize() = ()

    override _.OnFrameworkInitializationCompleted() =
        let window = Window(Title = "FE WebGPU + Avalonia HUD probe", Width = 900.0, Height = 580.0)
        let grid = Grid()
        grid.Children.Add(ProbeSurface()) |> ignore

        let overlay =
            Border(
                Background = SolidColorBrush(Avalonia.Media.Color.Parse("#dd152434")),
                CornerRadius = CornerRadius(8.0),
                Padding = Thickness(12.0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = Thickness(20.0)
            )

        overlay.Child <- TextBlock(Text = "AVALONIA HUD OVER WEBGPU", Foreground = Brushes.White)
        grid.Children.Add(overlay) |> ignore
        window.Content <- grid
        window.Show()
        base.OnFrameworkInitializationCompleted()

AppBuilder.Configure<ProbeApp>()
    .UseSilkNet()
    .UseProGpu()
    .UseProGpuTextShaping()
    .WithInterFont()
    .StartWithClassicDesktopLifetime([||])

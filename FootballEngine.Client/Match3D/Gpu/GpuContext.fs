#nowarn "9"

namespace FootballEngine.Client.Match3D

open System
open System.IO
open System.Runtime.InteropServices
open FsToolkit.ErrorHandling
open Microsoft.FSharp.NativeInterop
open Silk.NET.Core.Contexts
open Silk.NET.WebGPU

module GpuPointer =
    let isMissing (pointer: nativeptr<'T>) = NativePtr.toNativeInt pointer = 0n

/// The native library cannot be unloaded and the API object is cheap to share, so one copy serves every context.
module WebGpuRuntime =
    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type DevicePoll = delegate of nativeint * byte * nativeint -> byte

    type Shared = { Api: WebGPU; DevicePoll: DevicePoll }

    let private libraryPath () : Result<string, GpuError> =
        let fileName =
            if OperatingSystem.IsWindows() then "wgpu_native.dll"
            elif OperatingSystem.IsMacOS() then "libwgpu_native.dylib"
            else "libwgpu_native.so"

        let path =
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName)

        if File.Exists path then
            Ok path
        else
            Error(CreationFailed $"the native WebGPU runtime at {path}")

    let private shared =
        lazy
            (libraryPath ()
             |> Result.map (fun path ->
                 let nativeContext = new DefaultNativeContext(path)

                 { Api = new WebGPU(nativeContext)
                   DevicePoll =
                     nativeContext.Library.LoadFunction("wgpuDevicePoll")
                     |> Marshal.GetDelegateForFunctionPointer<DevicePoll> }))

    let load () : Result<Shared, GpuError> = shared.Force()

/// Instance, surface, adapter, device and queue for one presentation target. Creation reports every failure as a
/// GpuError value; the renderer facade is the only place that turns one into an exception.
[<Sealed>]
type GpuContext private (runtime: WebGpuRuntime.Shared) =
    let api = runtime.Api
    let nullVoid = NativePtr.toVoidPtr (NativePtr.nullPtr<byte>)
    let mutable instance = NativePtr.nullPtr<Instance>
    let mutable surface = NativePtr.nullPtr<Surface>
    let mutable adapter = NativePtr.nullPtr<Adapter>
    let mutable device = NativePtr.nullPtr<Device>
    let mutable queue = NativePtr.nullPtr<Queue>
    let mutable surfaceConfigured = false
    let mutable format = TextureFormat.Undefined
    let mutable surfaceWidth = 0u
    let mutable surfaceHeight = 0u
    let mutable disposed = false

    // The device keeps a pointer to this delegate, so both must outlive the device; they are released last.
    let errorCallback =
        ErrorCallback(fun errorType message _ ->
            let text = Marshal.PtrToStringUTF8(NativePtr.toNativeInt message)
            Diagnostics.Trace.WriteLine($"WebGPU uncaptured error ({errorType}): {text}"))

    let errorCallbackPointer = PfnErrorCallback.From(errorCallback)

    let requestAdapter () =
        let mutable requested = NativePtr.nullPtr<Adapter>

        let callback =
            RequestAdapterCallback(fun status value _ _ ->
                if status = RequestAdapterStatus.Success then
                    requested <- value)

        use callbackPointer = PfnRequestAdapterCallback.From(callback)
        let mutable options = RequestAdapterOptions()
        options.CompatibleSurface <- surface
        options.PowerPreference <- PowerPreference.HighPerformance
        api.InstanceRequestAdapter(instance, &options, callbackPointer, nullVoid)
        requested

    let requestDevice () =
        let mutable requested = NativePtr.nullPtr<Device>

        let callback =
            RequestDeviceCallback(fun status value _ _ ->
                if status = RequestDeviceStatus.Success then
                    requested <- value)

        use callbackPointer = PfnRequestDeviceCallback.From(callback)
        api.AdapterRequestDevice(adapter, NativePtr.nullPtr<DeviceDescriptor>, callbackPointer, nullVoid)
        requested

    let createXlibSurface (display: nativeint) (window: unativeint) =
        let mutable xlibSource = SurfaceDescriptorFromXlibWindow()
        xlibSource.Chain.SType <- SType.SurfaceDescriptorFromXlibWindow
        xlibSource.Display <- NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> display)
        xlibSource.Window <- uint64 window
        let xlibSourcePointer = NativePtr.stackalloc<SurfaceDescriptorFromXlibWindow> 1
        NativePtr.write xlibSourcePointer xlibSource
        let mutable surfaceDescriptor = SurfaceDescriptor()
        surfaceDescriptor.NextInChain <- NativePtr.ofNativeInt<ChainedStruct> (NativePtr.toNativeInt xlibSourcePointer)
        api.InstanceCreateSurface(instance, &surfaceDescriptor)

    let require (condition: bool) (error: GpuError) : Result<unit, GpuError> = if condition then Ok() else Error error

    let initialize (display: nativeint) (window: unativeint) : Result<unit, GpuError> =
        instance <- api.CreateInstance(NativePtr.nullPtr<InstanceDescriptor>)

        require (not (GpuPointer.isMissing instance)) (CreationFailed "a WebGPU instance")
        |> Result.bind (fun () ->
            surface <- createXlibSurface display window
            require (not (GpuPointer.isMissing surface)) (CreationFailed "the stadium Xlib surface"))
        |> Result.bind (fun () ->
            adapter <- requestAdapter ()
            require (not (GpuPointer.isMissing adapter)) AdapterUnavailable)
        |> Result.bind (fun () ->
            device <- requestDevice ()
            require (not (GpuPointer.isMissing device)) (CreationFailed "a device"))
        |> Result.bind (fun () ->
            queue <- api.DeviceGetQueue(device)
            require (not (GpuPointer.isMissing queue)) (CreationFailed "the WebGPU queue"))
        |> Result.map (fun () -> api.DeviceSetUncapturedErrorCallback(device, errorCallbackPointer, nullVoid))

    // Format and alpha are picked once per surface; a later reconfigure must keep the same format because the
    // pipelines and render targets were built for it.
    let configure (requestedWidth: uint32) (requestedHeight: uint32) : Result<unit, GpuError> =
        let mutable capabilities = SurfaceCapabilities()
        api.SurfaceGetCapabilities(surface, adapter, &capabilities)

        let formats =
            Array.init (int capabilities.FormatCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.Formats index))

        let alphaModes =
            Array.init (int capabilities.AlphaModeCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.AlphaModes index))

        let presentModes =
            Array.init (int capabilities.PresentModeCount) (fun index ->
                NativePtr.read (NativePtr.add capabilities.PresentModes index))

        api.SurfaceCapabilitiesFreeMembers(capabilities)

        let selection =
            result {
                // Hardware sRGB encoding keeps both scene output and premultiplied HUD blending in linear light.
                let! outputFormat =
                    [ TextureFormat.Bgra8UnormSrgb; TextureFormat.Rgba8UnormSrgb ]
                    |> List.tryFind (fun candidate -> Array.contains candidate formats)
                    |> Option.map Ok
                    |> Option.defaultValue (Error(CreationFailed "an sRGB presentation surface for the HDR renderer"))

                let! alphaMode =
                    if Array.contains CompositeAlphaMode.Opaque alphaModes then
                        Ok CompositeAlphaMode.Opaque
                    else
                        alphaModes
                        |> Array.tryHead
                        |> Option.map Ok
                        |> Option.defaultValue (Error(CreationFailed "a supported presentation alpha mode"))

                let! presentMode =
                    if Array.contains PresentMode.Mailbox presentModes then
                        Ok PresentMode.Mailbox
                    // FIFO remains a supported presentation mode, not a legacy renderer or quality fallback.
                    elif Array.contains PresentMode.Fifo presentModes then
                        Ok PresentMode.Fifo
                    else
                        Error(CreationFailed "a tear-free mailbox or FIFO presentation mode")

                if format <> TextureFormat.Undefined && format <> outputFormat then
                    return!
                        Error(
                            CreationFailed
                                "a presentation surface in its original format; recreate the HDR renderer instead of reusing incompatible pipelines"
                        )

                return outputFormat, alphaMode, presentMode
            }

        match selection with
        | Error error -> Error error
        | Ok(outputFormat, alphaMode, presentMode) ->
            format <- outputFormat
            let mutable configuration = SurfaceConfiguration()
            configuration.Device <- device
            configuration.Format <- outputFormat
            configuration.Usage <- TextureUsage.RenderAttachment
            configuration.AlphaMode <- alphaMode
            configuration.Width <- requestedWidth
            configuration.Height <- requestedHeight
            configuration.PresentMode <- presentMode
            Diagnostics.Trace.WriteLine($"WebGPU presentation: {presentMode}, {requestedWidth}x{requestedHeight}.")
            api.SurfaceConfigure(surface, &configuration)
            surfaceConfigured <- true
            surfaceWidth <- requestedWidth
            surfaceHeight <- requestedHeight
            Ok()

    static member Create(display: nativeint, window: unativeint) : Result<GpuContext, GpuError> =
        WebGpuRuntime.load ()
        |> Result.bind (fun runtime ->
            let context = new GpuContext(runtime)

            match context.Initialize(display, window) with
            | Ok() -> Ok context
            | Error error ->
                (context :> IDisposable).Dispose()
                Error error)

    member internal _.Initialize(display: nativeint, window: unativeint) = initialize display window

    member _.Api = api
    member _.Device = device
    member _.Queue = queue
    member _.Surface = surface
    member _.Format = format
    member _.Width = surfaceWidth
    member _.Height = surfaceHeight

    member _.Configure(width: uint32, height: uint32) : Result<unit, GpuError> = configure width height

    /// Waiting blocks until submitted work completes, which diagnostics need; the frame path only pumps callbacks.
    member _.Poll(wait: bool) =
        if not disposed then
            runtime.DevicePoll.Invoke(NativePtr.toNativeInt device, (if wait then 1uy else 0uy), 0n)
            |> ignore

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if not (GpuPointer.isMissing queue) then
                    api.QueueRelease(queue)

                if not (GpuPointer.isMissing surface) then
                    if surfaceConfigured then
                        api.SurfaceUnconfigure(surface)

                    api.SurfaceRelease(surface)

                if not (GpuPointer.isMissing device) then
                    api.DeviceRelease(device)

                if not (GpuPointer.isMissing adapter) then
                    api.AdapterRelease(adapter)

                if not (GpuPointer.isMissing instance) then
                    api.InstanceRelease(instance)

                (errorCallbackPointer :> IDisposable).Dispose()

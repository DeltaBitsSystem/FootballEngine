#nowarn "9"

namespace FootballEngine.Client.Match3D

open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Buffers keep their own length so a write past the end is caught here instead of corrupting GPU memory.
[<Sealed>]
type GpuBuffer<'T when 'T: unmanaged> private (context: GpuContext, handle: nativeptr<Buffer>, length: int) =
    let api = context.Api
    let mutable disposed = false

    static member Create
        (context: GpuContext, label: string, usage: BufferUsage, data: 'T array)
        : Result<GpuBuffer<'T>, GpuError> =
        let mutable descriptor = BufferDescriptor()
        descriptor.Usage <- usage
        descriptor.Size <- uint64 (max (sizeof<'T>) (data.Length * sizeof<'T>))
        let handle = context.Api.DeviceCreateBuffer(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed label)
        else
            let buffer = new GpuBuffer<'T>(context, handle, data.Length)
            buffer.Write data
            Ok buffer

    member _.Handle = handle
    member _.Length = length

    member this.Write(data: 'T array) = this.Write(data, 0)

    member _.Write(data: 'T array, offsetElements: int) =
        if disposed then
            raise (System.ObjectDisposedException(nameof GpuBuffer))

        if offsetElements < 0 || offsetElements + data.Length > length then
            invalidArg
                (nameof offsetElements)
                $"Writing {data.Length} elements at {offsetElements} exceeds a buffer of {length}."

        // An empty array has no address to pin, and nothing needs uploading.
        if data.Length > 0 then
            use pointer = fixed data

            api.QueueWriteBuffer(
                context.Queue,
                handle,
                uint64 (offsetElements * sizeof<'T>),
                NativePtr.toVoidPtr pointer,
                unativeint (data.Length * sizeof<'T>)
            )

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                api.BufferRelease(handle)

type TextureSpec =
    { Label: string
      Format: TextureFormat
      Width: uint32
      Height: uint32
      Layers: uint32
      MipLevels: uint32
      Samples: uint32
      Usage: TextureUsage }

/// One 2D texture, optionally an array and a mip chain, with its default view. Uploads go through the queue, so
/// they are ordered with the frame that reads them.
[<Sealed>]
type GpuTexture
    private (context: GpuContext, handle: nativeptr<Texture>, view: nativeptr<TextureView>, spec: TextureSpec) =
    let api = context.Api
    let mutable disposed = false

    let write
        (mipLevel: uint32)
        (origin: Origin3D)
        (extent: Extent3D)
        (bytesPerRow: uint32)
        (rowsPerImage: uint32)
        (data: voidptr)
        (byteCount: int)
        =
        let mutable destination = ImageCopyTexture()
        destination.Texture <- handle
        destination.MipLevel <- mipLevel
        destination.Origin <- origin
        destination.Aspect <- TextureAspect.All
        let mutable layout = TextureDataLayout()
        layout.BytesPerRow <- bytesPerRow
        layout.RowsPerImage <- rowsPerImage
        let mutable size = extent
        api.QueueWriteTexture(context.Queue, &destination, data, unativeint byteCount, &layout, &size)

    static member Create(context: GpuContext, spec: TextureSpec) : Result<GpuTexture, GpuError> =
        let mutable descriptor = TextureDescriptor()
        descriptor.Usage <- spec.Usage
        descriptor.Dimension <- TextureDimension.Dimension2D
        descriptor.Size <- Extent3D(spec.Width, spec.Height, spec.Layers)
        descriptor.Format <- spec.Format
        descriptor.MipLevelCount <- spec.MipLevels
        descriptor.SampleCount <- spec.Samples
        let handle = context.Api.DeviceCreateTexture(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed spec.Label)
        else
            let view =
                context.Api.TextureCreateView(handle, NativePtr.nullPtr<TextureViewDescriptor>)

            if GpuPointer.isMissing view then
                context.Api.TextureDestroy(handle)
                context.Api.TextureRelease(handle)
                Error(CreationFailed $"{spec.Label} view")
            else
                Ok(new GpuTexture(context, handle, view, spec))

    member _.Handle = handle
    member _.View = view
    member _.Format = spec.Format
    member _.Width = spec.Width
    member _.Height = spec.Height
    member _.Layers = spec.Layers
    member _.MipLevels = spec.MipLevels
    member _.Samples = spec.Samples

    /// Whole mip levels from tightly packed rows, which is how the authored environment and surface assets are stored.
    member _.UploadLevel
        (
            mipLevel: uint32,
            levelWidth: uint32,
            levelHeight: uint32,
            levelLayers: uint32,
            bytesPerRow: uint32,
            data: byte array,
            offset: int,
            byteCount: int
        ) =
        if disposed then
            raise (System.ObjectDisposedException(nameof GpuTexture))

        use pointer = fixed data

        write
            mipLevel
            (Origin3D(0u, 0u, 0u))
            (Extent3D(levelWidth, levelHeight, levelLayers))
            bytesPerRow
            levelHeight
            (NativePtr.toVoidPtr (NativePtr.add pointer offset))
            byteCount

    /// A rectangle of mip 0 from a larger CPU surface, such as the HUD's dirty regions.
    member _.UploadRegion
        (x: uint32, y: uint32, width: uint32, height: uint32, bytesPerRow: uint32, data: System.IntPtr, byteCount: int)
        =
        if disposed then
            raise (System.ObjectDisposedException(nameof GpuTexture))

        write 0u (Origin3D(x, y, 0u)) (Extent3D(width, height, 1u)) bytesPerRow height (data.ToPointer()) byteCount

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                api.TextureViewRelease(view)
                api.TextureDestroy(handle)
                api.TextureRelease(handle)

type SamplerSpec =
    { AddressU: AddressMode
      AddressV: AddressMode
      AddressW: AddressMode
      Magnification: FilterMode
      Minification: FilterMode
      Mipmap: MipmapFilterMode
      MaxAnisotropy: uint16
      LodMaxClamp: float32
      Compare: CompareFunction option }

[<Sealed>]
type GpuSampler private (context: GpuContext, handle: nativeptr<Sampler>) =
    let mutable disposed = false

    static member Create(context: GpuContext, label: string, spec: SamplerSpec) : Result<GpuSampler, GpuError> =
        let mutable descriptor = SamplerDescriptor()
        descriptor.AddressModeU <- spec.AddressU
        descriptor.AddressModeV <- spec.AddressV
        descriptor.AddressModeW <- spec.AddressW
        descriptor.MagFilter <- spec.Magnification
        descriptor.MinFilter <- spec.Minification
        descriptor.MipmapFilter <- spec.Mipmap
        descriptor.MaxAnisotropy <- spec.MaxAnisotropy
        descriptor.LodMaxClamp <- spec.LodMaxClamp
        spec.Compare |> Option.iter (fun compare -> descriptor.Compare <- compare)
        let handle = context.Api.DeviceCreateSampler(context.Device, &descriptor)

        if GpuPointer.isMissing handle then
            Error(CreationFailed label)
        else
            Ok(new GpuSampler(context, handle))

    member _.Handle = handle

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                context.Api.SamplerRelease(handle)

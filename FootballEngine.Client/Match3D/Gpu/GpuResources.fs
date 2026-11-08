#nowarn "9"

namespace FootballEngine.Client.Match3D

open Microsoft.FSharp.NativeInterop
open Silk.NET.WebGPU

/// Typed buffer uploads on the render thread. Callers own the returned native buffer and release it through
/// the same WebGPU instance that created it.
module GpuBuffer =
    let write<'T when 'T: unmanaged>
        (api: WebGPU)
        (queue: nativeptr<Queue>)
        (buffer: nativeptr<Buffer>)
        (data: 'T array)
        =
        use dataPointer = fixed data

        api.QueueWriteBuffer(queue, buffer, 0UL, NativePtr.toVoidPtr dataPointer, unativeint (data.Length * sizeof<'T>))

    let create<'T when 'T: unmanaged>
        (api: WebGPU)
        (device: nativeptr<Device>)
        (queue: nativeptr<Queue>)
        (label: string)
        usage
        (data: 'T array)
        =
        let size = uint64 (max (sizeof<'T>) (data.Length * sizeof<'T>))
        let mutable descriptor = BufferDescriptor()
        descriptor.Usage <- usage
        descriptor.Size <- size
        let buffer = api.DeviceCreateBuffer(device, &descriptor)

        if NativePtr.toNativeInt buffer = 0n then
            invalidOp $"WebGPU failed to create {label}."

        try
            if data.Length > 0 then
                write api queue buffer data

            buffer
        with _ ->
            api.BufferRelease(buffer)
            reraise ()

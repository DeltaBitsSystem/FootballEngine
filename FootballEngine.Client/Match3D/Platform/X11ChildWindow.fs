#nowarn "9"

namespace FootballEngine.Client.Match3D

open System.Runtime.InteropServices
open Avalonia
open Microsoft.FSharp.NativeInterop

/// Native X11 child window that hosts the WebGPU surface inside Avalonia. The X11 calls and the cutout
/// geometry live here; the renderer decides when the window moves or changes shape, never how.
module X11ChildWindow =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type Rectangle =
        { X: int16
          Y: int16
          Width: uint16
          Height: uint16 }

    module private Native =
        [<DllImport("libX11.so.6", EntryPoint = "XOpenDisplay", CallingConvention = CallingConvention.Cdecl)>]
        extern nativeint openDisplay(nativeint name)

        [<DllImport("libX11.so.6", EntryPoint = "XCloseDisplay", CallingConvention = CallingConvention.Cdecl)>]
        extern int closeDisplay(nativeint display)

        [<DllImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow", CallingConvention = CallingConvention.Cdecl)>]
        extern unativeint createSimpleWindow(
            nativeint display,
            unativeint parent,
            int x,
            int y,
            uint32 width,
            uint32 height,
            uint32 borderWidth,
            unativeint border,
            unativeint background
        )

        [<DllImport("libX11.so.6", EntryPoint = "XMapRaised", CallingConvention = CallingConvention.Cdecl)>]
        extern int mapRaised(nativeint display, unativeint window)

        [<DllImport("libX11.so.6", EntryPoint = "XMoveResizeWindow", CallingConvention = CallingConvention.Cdecl)>]
        extern int moveResizeWindow(nativeint display, unativeint window, int x, int y, uint32 width, uint32 height)

        [<DllImport("libX11.so.6", EntryPoint = "XDestroyWindow", CallingConvention = CallingConvention.Cdecl)>]
        extern int destroyWindow(nativeint display, unativeint window)

        [<DllImport("libX11.so.6", EntryPoint = "XFlush", CallingConvention = CallingConvention.Cdecl)>]
        extern int flush(nativeint display)

        [<DllImport("libXext.so.6", EntryPoint = "XShapeCombineRectangles", CallingConvention = CallingConvention.Cdecl)>]
        extern void shapeCombineRectangles(
            nativeint display,
            unativeint window,
            int destinationKind,
            int xOffset,
            int yOffset,
            nativeint rectangles,
            int rectangleCount,
            int operation,
            int ordering
        )

    let openDisplay () = Native.openDisplay 0n

    let closeDisplay (display: nativeint) = Native.closeDisplay display |> ignore

    /// Width and height are clamped to one pixel because X rejects zero-sized windows at creation.
    let createChild (display: nativeint) (parent: unativeint) (width: int) (height: int) : unativeint =
        Native.createSimpleWindow (display, parent, 0, 0, uint32 (max 1 width), uint32 (max 1 height), 0u, 0un, 0un)

    let mapRaised (display: nativeint) (window: unativeint) =
        Native.mapRaised (display, window) |> ignore

    let flush (display: nativeint) = Native.flush display |> ignore

    let moveResize (display: nativeint) (window: unativeint) (bounds: PixelRect) =
        Native.moveResizeWindow (display, window, bounds.X, bounds.Y, uint32 bounds.Width, uint32 bounds.Height)
        |> ignore

    let destroy (display: nativeint) (window: unativeint) =
        Native.destroyWindow (display, window) |> ignore

    let setShape (display: nativeint) (window: unativeint) (rectangles: Rectangle array) =
        use rectanglePointer = fixed rectangles

        Native.shapeCombineRectangles (
            display,
            window,
            0,
            0,
            0,
            NativePtr.toNativeInt rectanglePointer,
            rectangles.Length,
            0,
            0
        )

    /// Rectangles of the window that stay visible once the cutouts are removed. The window is split on every
    /// cutout edge so the result is a disjoint cover. An empty rectangle list would clear the shape entirely,
    /// so a window with no visible cells falls back to its full bounds.
    let cutoutRectangles (bounds: PixelRect) (cutouts: PixelRect array) : Rectangle array =
        let clamp low high value = max low (min high value)

        let clipped =
            cutouts
            |> Array.choose (fun rect ->
                let x = clamp 0 bounds.Width rect.X
                let y = clamp 0 bounds.Height rect.Y
                let right = clamp x bounds.Width (rect.X + rect.Width)
                let bottom = clamp y bounds.Height (rect.Y + rect.Height)

                if right <= x || bottom <= y then
                    None
                else
                    Some(x, y, right, bottom))

        let boundaries (start: int) (finish: int) selector =
            seq {
                yield start
                yield finish

                for rect in clipped do
                    let first, last = selector rect
                    yield first
                    yield last
            }
            |> Seq.distinct
            |> Seq.sort
            |> Seq.toArray

        let horizontal = boundaries 0 bounds.Width (fun (x, _, right, _) -> x, right)
        let vertical = boundaries 0 bounds.Height (fun (_, y, _, bottom) -> y, bottom)
        let visible = ResizeArray<Rectangle>()

        for yIndex = 0 to vertical.Length - 2 do
            let y = vertical[yIndex]
            let bottom = vertical[yIndex + 1]

            for xIndex = 0 to horizontal.Length - 2 do
                let x = horizontal[xIndex]
                let right = horizontal[xIndex + 1]
                let centerX = x + (right - x) / 2
                let centerY = y + (bottom - y) / 2

                let covered =
                    clipped
                    |> Array.exists (fun (left, top, edge, lower) ->
                        centerX >= left && centerX < edge && centerY >= top && centerY < lower)

                if right > x && bottom > y && not covered then
                    visible.Add
                        { X = int16 x
                          Y = int16 y
                          Width = uint16 (right - x)
                          Height = uint16 (bottom - y) }

        if visible.Count = 0 then
            visible.Add
                { X = 0s
                  Y = 0s
                  Width = uint16 bounds.Width
                  Height = uint16 bounds.Height }

        visible.ToArray()

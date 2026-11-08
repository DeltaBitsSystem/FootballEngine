module FootballEngine.Client.Tests.Match3DFoundationTests

open Avalonia
open Expecto
open FootballEngine.Client.Match3D

let private area (rectangles: X11ChildWindow.Rectangle array) =
    rectangles |> Array.sumBy (fun rect -> int rect.Width * int rect.Height)

let private intersects (rect: X11ChildWindow.Rectangle) (cutout: PixelRect) =
    let left = int rect.X
    let top = int rect.Y
    let right = left + int rect.Width
    let bottom = top + int rect.Height

    left < cutout.X + cutout.Width
    && cutout.X < right
    && top < cutout.Y + cutout.Height
    && cutout.Y < bottom

let tests =
    testList
        "Match3D foundation"
        [ testCase "shader segments are concatenated in the order they are given" (fun _ ->
              Expect.equal
                  (ShaderLibrary.compose [ "constants;"; "lighting;"; "surface;" ])
                  "constants;lighting;surface;"
                  "order"

              Expect.equal (ShaderLibrary.compose [ "b"; "a" ]) "ba" "reversed input stays reversed"
              Expect.equal (ShaderLibrary.compose []) "" "no segments compose to empty source")
          testCase "a missing shader file is reported with its name rather than a generic failure" (fun _ ->
              let name = "NoSuchShaderForTests.wgsl"

              match ShaderLibrary.readFile name with
              | Error(ShaderCompile(file, message)) ->
                  Expect.equal file name "file name is carried in the error"
                  Expect.stringContains message "missing" "message says the file is missing"
              | other -> failtestf "expected ShaderCompile for a missing file, got %A" other)
          testCase "without cutouts the whole window stays visible as one rectangle" (fun _ ->
              let bounds = PixelRect(0, 0, 100, 80)
              let rectangles = X11ChildWindow.cutoutRectangles bounds [||]

              Expect.equal rectangles.Length 1 "one rectangle"
              Expect.equal (area rectangles) 8000 "full area")
          testCase "a cutout removes exactly its area and no visible rectangle overlaps it" (fun _ ->
              let bounds = PixelRect(0, 0, 100, 80)
              let cutout = PixelRect(10, 10, 20, 20)
              let rectangles = X11ChildWindow.cutoutRectangles bounds [| cutout |]

              Expect.equal (area rectangles) (8000 - 400) "visible area excludes the cutout"

              Expect.isFalse
                  (rectangles |> Array.exists (fun rect -> intersects rect cutout))
                  "no overlap with the cutout")
          testCase "a fully covered window falls back to its full bounds instead of an empty shape" (fun _ ->
              let bounds = PixelRect(0, 0, 40, 30)

              let rectangles =
                  X11ChildWindow.cutoutRectangles bounds [| PixelRect(-5, -5, 60, 60) |]

              Expect.equal rectangles.Length 1 "fallback rectangle"
              Expect.equal (area rectangles) 1200 "fallback covers the full window") ]

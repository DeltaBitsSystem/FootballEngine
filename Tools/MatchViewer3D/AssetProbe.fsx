#r "nuget: SharpGLTF.Core, 1.0.7"

open System
open System.IO
open SharpGLTF.Schema2

let path =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| path |] -> Path.GetFullPath path
    | _ -> failwith "Usage: dotnet fsi --exec Tools/MatchViewer3D/AssetProbe.fsx player.glb"

let model = ModelRoot.Load path
let meshes = model.LogicalMeshes.Count
let nodes = model.LogicalNodes.Count
let skins = model.LogicalSkins.Count
let animations = model.LogicalAnimations.Count
let materials = model.LogicalMaterials.Count

printfn "GLB %s: meshes=%d nodes=%d skins=%d animations=%d materials=%d" path meshes nodes skins animations materials

if meshes = 0 then
    failwith "No mesh geometry in exported Blender asset"

if skins = 0 || animations = 0 then
    printfn "Static asset only: this GLB does not satisfy the animated-player gate."

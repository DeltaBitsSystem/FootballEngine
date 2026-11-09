namespace FootballEngine.Client.Match3D

/// How a draw feeds the sun shadow. Static casters are rendered once into the cached stadium depth, so only
/// moving geometry pays for shadow rendering every frame.
type ShadowCasting =
    | StaticCaster
    | DynamicCaster
    | NoShadow

/// Where a draw lands in the main pass. Scene geometry precedes the procedural sky and grass; overlay annotations
/// (the carrier marker) follow them so grass blades never cover a presentation marker.
type DrawLayer =
    | SceneLayer
    /// Glass, polycarbonate and nets: composited over the finished opaque scene without writing depth.
    | BlendedLayer
    | OverlayLayer
    /// Coarse geometry that exists only for the cached shadow map and is never drawn by the main pass.
    | ShadowOnlyLayer

/// Generic over the mesh so ordering and grouping stay pure and testable without a device.
type Draw<'Mesh> =
    { Mesh: 'Mesh
      FirstIndex: uint32
      IndexCount: uint32
      /// Offsets `instance_index`, which the kit shader uses to address its instance transforms.
      FirstInstance: uint32
      InstanceCount: uint32
      Shadow: ShadowCasting
      Layer: DrawLayer }

/// Grouping keeps submission order: with a single opaque pipeline and depth `Less`, reordering equal-depth
/// fragments would change pixels, so captures stay identical only if systems' order is preserved.
module DrawList =
    let shadowCasters (casting: ShadowCasting) (draws: Draw<'Mesh> list) =
        draws |> List.filter (fun draw -> draw.Shadow = casting)

    let inLayer (layer: DrawLayer) (draws: Draw<'Mesh> list) =
        draws |> List.filter (fun draw -> draw.Layer = layer)

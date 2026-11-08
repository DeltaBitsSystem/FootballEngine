namespace FootballEngine.Client.Match3D

open System
open FootballEngine.Settings

/// Every visual-quality number of the 3D viewer. Shaders and passes read these values; none of them is hard-coded
/// in a pass, so tiers are derived from Ultra by lowering values instead of by branching on a tier name.
type ViewerQuality =
    {
        Name: string
        /// Samples of the HDR scene target and its depth. WebGPU only guarantees 1 and 4.
        MsaaSamples: int
        ShadowCascades: int
        /// Resolution of every dynamic cascade (players and ball).
        ShadowMapSize: int
        /// Resolution of the cached stadium depth, rasterized once because the stadium never moves.
        StaticShadowMapSize: int
        /// Blend between uniform (0) and logarithmic (1) cascade splits.
        ShadowSplitLambda: float32
        ShadowMaxDistance: float32
        /// Fraction of a cascade, at its far end, over which it fades into the next one.
        ShadowBlendFraction: float32
        /// Rotated Poisson taps over the hardware 2x2 comparison filter; 1 means a single hardware tap.
        ShadowPcfTaps: int
        ShadowPcfRadiusTexels: float32
        /// Receiver offset along the normal, in texels of the selected cascade.
        ShadowNormalBiasTexels: float32
        /// Extra depth bias that grows with the receiver's slope relative to the sun, in texels.
        ShadowSlopeBiasTexels: float32
        ShadowConstantBias: float32
        AoEnabled: bool
        /// Fraction of the output resolution at which ambient occlusion is evaluated.
        AoResolutionScale: float32
        AoRadiusMetres: float32
        AoIntensity: float32
        AoSlices: int
        AoStepsPerSide: int
        BloomEnabled: bool
        BloomIntensity: float32
        /// Exposed scene radiance below which a pixel contributes no glow.
        BloomThreshold: float32
        BloomKnee: float32
        BloomMips: int
        /// Share of each larger mip replaced by the upsampled smaller one; higher spreads the glow wider.
        BloomSpread: float32
        GradingLutEnabled: bool
        GradingLutSize: int
        Anisotropy: int
        /// Side of the square grid of procedural grass tufts.
        GrassGrid: int
        SpecularAa: bool
        /// Multiplier on the screen-space variance that widens highlights.
        SpecularAaScale: float32
        /// Largest roughness-squared increase specular AA may add, so surfaces never turn into mirrors of fog.
        SpecularAaClamp: float32
    }

module ViewerQuality =

    let ultra =
        { Name = "ultra"
          MsaaSamples = 4
          ShadowCascades = 3
          ShadowMapSize = 2048
          StaticShadowMapSize = 4096
          ShadowSplitLambda = 0.4f
          ShadowMaxDistance = 150.0f
          ShadowBlendFraction = 0.12f
          ShadowPcfTaps = 16
          ShadowPcfRadiusTexels = 1.6f
          ShadowNormalBiasTexels = 1.5f
          ShadowSlopeBiasTexels = 1.0f
          ShadowConstantBias = 0.00008f
          AoEnabled = true
          AoResolutionScale = 1.0f
          AoRadiusMetres = 1.2f
          AoIntensity = 0.9f
          AoSlices = 4
          AoStepsPerSide = 10
          BloomEnabled = true
          BloomIntensity = 0.045f
          BloomThreshold = 1.2f
          BloomKnee = 0.6f
          BloomMips = 6
          BloomSpread = 0.6f
          GradingLutEnabled = true
          GradingLutSize = 32
          Anisotropy = 16
          GrassGrid = 160
          SpecularAa = true
          SpecularAaScale = 0.5f
          SpecularAaClamp = 0.18f }

    let high =
        { ultra with
            Name = "high"
            ShadowMapSize = 2048
            StaticShadowMapSize = 4096
            ShadowPcfTaps = 9
            ShadowPcfRadiusTexels = 1.3f
            AoResolutionScale = 0.5f
            AoSlices = 3
            AoStepsPerSide = 8
            BloomMips = 5
            Anisotropy = 8
            GrassGrid = 128 }

    let medium =
        { high with
            Name = "medium"
            ShadowCascades = 2
            ShadowMapSize = 1024
            StaticShadowMapSize = 2048
            ShadowMaxDistance = 110.0f
            ShadowPcfTaps = 4
            ShadowPcfRadiusTexels = 1.0f
            AoResolutionScale = 0.5f
            AoSlices = 2
            AoStepsPerSide = 6
            BloomMips = 4
            GradingLutEnabled = false
            Anisotropy = 4
            GrassGrid = 96 }

    let low =
        { medium with
            Name = "low"
            MsaaSamples = 1
            ShadowCascades = 1
            ShadowMapSize = 1024
            StaticShadowMapSize = 2048
            ShadowMaxDistance = 90.0f
            ShadowPcfTaps = 1
            ShadowPcfRadiusTexels = 1.0f
            AoEnabled = false
            AoResolutionScale = 0.25f
            AoSlices = 1
            AoStepsPerSide = 4
            BloomEnabled = false
            BloomMips = 1
            Anisotropy = 1
            GrassGrid = 64
            SpecularAa = false }

    let presets = [ low; medium; high; ultra ]

    let ofTier (tier: ViewerQualityTier) =
        match tier with
        | ViewerQualityTier.Low -> low
        | ViewerQualityTier.Medium -> medium
        | ViewerQualityTier.High -> high
        | ViewerQualityTier.Ultra -> ultra

    let tryParse (name: string) =
        presets
        |> List.tryFind (fun preset -> String.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))

    /// Knobs whose larger value costs more GPU time; presets must never lower one while raising the tier.
    let costKnobs (quality: ViewerQuality) : (string * float) list =
        let flag value = if value then 1.0 else 0.0

        [ "MsaaSamples", float quality.MsaaSamples
          "ShadowCascades", float quality.ShadowCascades
          "ShadowMapSize", float quality.ShadowMapSize
          "StaticShadowMapSize", float quality.StaticShadowMapSize
          "ShadowPcfTaps", float quality.ShadowPcfTaps
          "AoEnabled", flag quality.AoEnabled
          "AoResolutionScale", float quality.AoResolutionScale
          "AoSlices", float quality.AoSlices
          "AoStepsPerSide", float quality.AoStepsPerSide
          "BloomEnabled", flag quality.BloomEnabled
          "BloomMips", float quality.BloomMips
          "GradingLutEnabled", flag quality.GradingLutEnabled
          "Anisotropy", float quality.Anisotropy
          "GrassGrid", float quality.GrassGrid
          "SpecularAa", flag quality.SpecularAa ]

    let validate (quality: ViewerQuality) : string list =
        let isPowerOfTwo value =
            value > 0 && (value &&& (value - 1)) = 0

        let finite (value: float32) = Single.IsFinite value

        [ if quality.MsaaSamples <> 1 && quality.MsaaSamples <> 4 then
              yield "MsaaSamples must be 1 or 4, the sample counts WebGPU guarantees."
          if quality.ShadowCascades < 1 || quality.ShadowCascades > 4 then
              yield "ShadowCascades must be between 1 and 4."
          if
              not (isPowerOfTwo quality.ShadowMapSize)
              || quality.ShadowMapSize < 256
              || quality.ShadowMapSize > 4096
          then
              yield "ShadowMapSize must be a power of two between 256 and 4096."
          if
              not (isPowerOfTwo quality.StaticShadowMapSize)
              || quality.StaticShadowMapSize < 512
              || quality.StaticShadowMapSize > 8192
          then
              yield "StaticShadowMapSize must be a power of two between 512 and 8192."
          if
              not (finite quality.ShadowSplitLambda)
              || quality.ShadowSplitLambda < 0.0f
              || quality.ShadowSplitLambda > 1.0f
          then
              yield "ShadowSplitLambda must be between 0 and 1."
          if
              not (finite quality.ShadowMaxDistance)
              || quality.ShadowMaxDistance < 10.0f
              || quality.ShadowMaxDistance > 500.0f
          then
              yield "ShadowMaxDistance must be between 10 and 500 metres."
          if
              not (finite quality.ShadowBlendFraction)
              || quality.ShadowBlendFraction < 0.0f
              || quality.ShadowBlendFraction > 0.5f
          then
              yield "ShadowBlendFraction must be between 0 and 0.5."
          if not (List.contains quality.ShadowPcfTaps [ 1; 4; 9; 16 ]) then
              yield "ShadowPcfTaps must be 1, 4, 9 or 16."
          if
              not (finite quality.ShadowPcfRadiusTexels)
              || quality.ShadowPcfRadiusTexels < 0.0f
              || quality.ShadowPcfRadiusTexels > 8.0f
          then
              yield "ShadowPcfRadiusTexels must be between 0 and 8."
          if
              not (finite quality.ShadowNormalBiasTexels)
              || quality.ShadowNormalBiasTexels < 0.0f
              || quality.ShadowNormalBiasTexels > 16.0f
          then
              yield "ShadowNormalBiasTexels must be between 0 and 16."
          if
              not (finite quality.ShadowSlopeBiasTexels)
              || quality.ShadowSlopeBiasTexels < 0.0f
              || quality.ShadowSlopeBiasTexels > 16.0f
          then
              yield "ShadowSlopeBiasTexels must be between 0 and 16."
          if
              not (finite quality.ShadowConstantBias)
              || quality.ShadowConstantBias < 0.0f
              || quality.ShadowConstantBias > 0.01f
          then
              yield "ShadowConstantBias must be between 0 and 0.01."
          if
              not (finite quality.AoResolutionScale)
              || quality.AoResolutionScale < 0.25f
              || quality.AoResolutionScale > 1.0f
          then
              yield "AoResolutionScale must be between 0.25 and 1."
          if
              not (finite quality.AoRadiusMetres)
              || quality.AoRadiusMetres <= 0.0f
              || quality.AoRadiusMetres > 10.0f
          then
              yield "AoRadiusMetres must be positive and at most 10."
          if
              not (finite quality.AoIntensity)
              || quality.AoIntensity < 0.0f
              || quality.AoIntensity > 1.0f
          then
              yield "AoIntensity must be between 0 and 1."
          if quality.AoSlices < 1 || quality.AoSlices > 8 then
              yield "AoSlices must be between 1 and 8."
          if quality.AoStepsPerSide < 1 || quality.AoStepsPerSide > 24 then
              yield "AoStepsPerSide must be between 1 and 24."
          if
              not (finite quality.BloomIntensity)
              || quality.BloomIntensity < 0.0f
              || quality.BloomIntensity > 1.0f
          then
              yield "BloomIntensity must be between 0 and 1."
          if not (finite quality.BloomThreshold) || quality.BloomThreshold < 0.0f then
              yield "BloomThreshold must be finite and nonnegative."
          if
              not (finite quality.BloomKnee)
              || quality.BloomKnee < 0.0f
              || quality.BloomKnee > 4.0f
          then
              yield "BloomKnee must be between 0 and 4."
          if quality.BloomMips < 1 || quality.BloomMips > 8 then
              yield "BloomMips must be between 1 and 8."
          if
              not (finite quality.BloomSpread)
              || quality.BloomSpread < 0.0f
              || quality.BloomSpread > 1.0f
          then
              yield "BloomSpread must be between 0 and 1."
          if quality.GradingLutSize < 2 || quality.GradingLutSize > 64 then
              yield "GradingLutSize must be between 2 and 64."
          if
              quality.Anisotropy < 1
              || quality.Anisotropy > 16
              || not (isPowerOfTwo quality.Anisotropy)
          then
              yield "Anisotropy must be 1, 2, 4, 8 or 16."
          if quality.GrassGrid < 1 || quality.GrassGrid > 512 then
              yield "GrassGrid must be between 1 and 512."
          if
              not (finite quality.SpecularAaScale)
              || quality.SpecularAaScale < 0.0f
              || quality.SpecularAaScale > 4.0f
          then
              yield "SpecularAaScale must be between 0 and 4."
          if
              not (finite quality.SpecularAaClamp)
              || quality.SpecularAaClamp < 0.0f
              || quality.SpecularAaClamp > 1.0f
          then
              yield "SpecularAaClamp must be between 0 and 1." ]

    /// Presets are authored data; a failure here is a programming error surfaced by tests, not a runtime fallback.
    let validated (quality: ViewerQuality) =
        match validate quality with
        | [] -> Ok quality
        | errors -> Error(String.concat "; " errors)

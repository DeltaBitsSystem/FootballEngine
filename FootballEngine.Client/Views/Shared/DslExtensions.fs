namespace FootballEngine

open Avalonia.Controls
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.Types

/// Attributes Avalonia has and the FuncUI DSL does not expose yet.
[<AutoOpen>]
module DslExtensions =

    type TextBox with

        /// Avalonia 12 replaced Watermark with PlaceholderText; the DSL still only binds the deprecated property.
        static member placeholderText<'t when 't :> TextBox>(value: string) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<string>(TextBox.PlaceholderTextProperty, value, ValueNone)

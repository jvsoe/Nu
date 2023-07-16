namespace Nu
open System
open System.Collections.Generic
open System.Numerics
open ImGuiNET
open ImGuizmoNET
open Prime
open Nu

// TODO: document this!

[<AutoOpen>]
module ImGuiIOPtr =

    // HACK: allows manual tracking of mouse and keyboard event swallowing since Dead ImGui doesn't seem to yet have
    // it worked out - https://github.com/ocornut/imgui/issues/3370
    let mutable internal wantCaptureMousePlus = false
    let mutable internal wantCaptureKeyboardPlus = false

    let internal BeginFrame () =
        wantCaptureMousePlus <- false
        wantCaptureKeyboardPlus <- false

    type ImGuiIOPtr with

        member this.WantCaptureMousePlus = wantCaptureMousePlus || this.WantCaptureMouse
        member this.WantCaptureKeyboardPlus = wantCaptureKeyboardPlus || this.WantCaptureKeyboard
        member this.SwallowMouse () = wantCaptureMousePlus <- true
        member this.SwallowKeyboard () = wantCaptureKeyboardPlus <- true

/// Wraps ImGui context, state, and calls. Also extends the ImGui interface with static methods.
/// NOTE: API is primarily object-oriented / mutation-based because it's ported from a port of a port.
type ImGui (windowWidth : int, windowHeight : int) =

    let charsPressed =
        List<char> ()

    let keyboardKeys =
        Enum.GetValues typeof<KeyboardKey> |>
        enumerable |>
        Seq.map cast<KeyboardKey> |>
        Array.ofSeq

    let context =
        ImGui.CreateContext ()

    do
        // make context current
        ImGui.SetCurrentContext context

        // set guizmo context
        ImGuizmo.SetImGuiContext context

        // enable guizmo
        ImGuizmo.Enable true

        // retrieve configuration targets
        let io = ImGui.GetIO ()
        let keyMap = io.KeyMap
        let fonts = io.Fonts

        // configure the imgui backend to presume the use of vertex offsets (necessary since we're using 16 bit indices)
        io.BackendFlags <- io.BackendFlags ||| ImGuiBackendFlags.RendererHasVtxOffset

        // configure initial display size
        io.DisplaySize <- v2 (single windowWidth) (single windowHeight)

        // configure docking enabled
        io.ConfigFlags <- io.ConfigFlags ||| ImGuiConfigFlags.DockingEnable

        // configure imgui advance time to a constant speed regardless of frame-rate
        io.DeltaTime <- 1.0f / 60.0f

        // configure key mappings
        keyMap.[int ImGuiKey.Space] <- int KeyboardKey.Space
        keyMap.[int ImGuiKey.Tab] <- int KeyboardKey.Tab
        keyMap.[int ImGuiKey.LeftArrow] <- int KeyboardKey.Left
        keyMap.[int ImGuiKey.RightArrow] <- int KeyboardKey.Right
        keyMap.[int ImGuiKey.UpArrow] <- int KeyboardKey.Up
        keyMap.[int ImGuiKey.DownArrow] <- int KeyboardKey.Down
        keyMap.[int ImGuiKey.PageUp] <- int KeyboardKey.Pageup
        keyMap.[int ImGuiKey.PageDown] <- int KeyboardKey.Pagedown
        keyMap.[int ImGuiKey.Home] <- int KeyboardKey.Home
        keyMap.[int ImGuiKey.End] <- int KeyboardKey.End
        keyMap.[int ImGuiKey.Delete] <- int KeyboardKey.Delete
        keyMap.[int ImGuiKey.Backspace] <- int KeyboardKey.Backspace
        keyMap.[int ImGuiKey.Enter] <- int KeyboardKey.Return
        keyMap.[int ImGuiKey.Escape] <- int KeyboardKey.Escape
        keyMap.[int ImGuiKey.LeftCtrl] <- int KeyboardKey.Lctrl
        keyMap.[int ImGuiKey.RightCtrl] <- int KeyboardKey.Rctrl
        keyMap.[int ImGuiKey.LeftAlt] <- int KeyboardKey.Lalt
        keyMap.[int ImGuiKey.RightAlt] <- int KeyboardKey.Ralt
        keyMap.[int ImGuiKey.LeftShift] <- int KeyboardKey.Lshift
        keyMap.[int ImGuiKey.RightShift] <- int KeyboardKey.Rshift
        for i in 0 .. dec 10 do keyMap.[int ImGuiKey._1 + i] <- int KeyboardKey.Num1 + i
        for i in 0 .. dec 26 do keyMap.[int ImGuiKey.A + i] <- int KeyboardKey.A + i
        for i in 0 .. dec 12 do keyMap.[int ImGuiKey.F1 + i] <- int KeyboardKey.F1 + i

        // add default font
        fonts.AddFontDefault () |> ignore<ImFontPtr>

        // configure styling theme to nu
        ImGui.StyleColorsNu ()

    member this.Fonts =
        let io = ImGui.GetIO ()
        io.Fonts

    member this.HandleMouseWheelChange change =
        let io = ImGui.GetIO ()
        io.MouseWheel <- io.MouseWheel + change

    member this.HandleKeyChar (keyChar : char) =
        charsPressed.Add keyChar

    member this.BeginFrame () =
        ImGui.NewFrame ()
        ImGuiIOPtr.BeginFrame ()
        ImGuizmo.BeginFrame ()

    member this.EndFrame () =
        () // nothing to do

    member this.InputFrame () =

        // update mouse states
        let io = ImGui.GetIO ()
        let mouseDown = io.MouseDown
        mouseDown.[0] <- MouseState.isButtonDown MouseLeft
        mouseDown.[1] <- MouseState.isButtonDown MouseRight
        mouseDown.[2] <- MouseState.isButtonDown MouseMiddle
        io.MousePos <- MouseState.getPosition ()

        // update keyboard states.
        // NOTE: using modifier detection from sdl since it works better given how things have been configued.
        io.KeyCtrl <- KeyboardState.isCtrlDown ()
        io.KeyAlt <- KeyboardState.isAltDown ()
        io.KeyShift <- KeyboardState.isShiftDown ()
        let keysDown = io.KeysDown
        for keyboardKey in keyboardKeys do
            keysDown.[int keyboardKey] <- KeyboardState.isKeyDown keyboardKey

        // register key char input
        for c in charsPressed do
            io.AddInputCharacter (uint32 c)
        charsPressed.Clear ()

    member this.RenderFrame () =
        ImGui.Render ()
        ImGui.GetDrawData ()

    member this.CleanUp () =
        ImGui.DestroyContext context

    static member StyleColorsNu () =
        ImGui.StyleColorsDark ()
        let style = ImGui.GetStyle ()
        let colors = style.Colors
        colors.[int ImGuiCol.MenuBarBg] <- v4 0.0f 0.0f 0.0f 0.5f
        colors.[int ImGuiCol.TitleBg] <- v4 0.0f 0.0f 0.0f 0.5f
        colors.[int ImGuiCol.WindowBg] <- v4 0.0f 0.0f 0.0f 0.333f

    static member IsCtrlDown () =
        ImGui.IsKeyDown ImGuiKey.LeftCtrl ||
        ImGui.IsKeyDown ImGuiKey.RightCtrl

    static member IsAltDown () =
        ImGui.IsKeyDown ImGuiKey.LeftAlt ||
        ImGui.IsKeyDown ImGuiKey.RightAlt

    static member IsShiftDown () =
        ImGui.IsKeyDown ImGuiKey.LeftShift ||
        ImGui.IsKeyDown ImGuiKey.RightShift

    static member IsCtrlPlusKeyPressed (key : ImGuiKey) =
        ImGui.IsCtrlDown () && ImGui.IsKeyPressed key

    static member PositionToWindow (modelViewProjection : Matrix4x4, position : Vector3) =
        // NOTE: code mostly lifted from - https://github.com/CedricGuillemet/ImGuizmo/blob/822be7b44c37dbe98d328739ebe0d5a1ea87ecfc/ImGuizmo.cpp#L798-L810
        let windowPosition = ImGui.GetWindowPos ()
        let windowSize = ImGui.GetWindowSize ()
        let mutable position = Vector4.Transform (Vector4 (position, 1.0f), modelViewProjection)
        position <- position * (0.5f / position.W)
        position <- position + v4 0.5f 0.5f 0.0f 0.0f
        position.Y <- 1.0f - position.Y
        position.X <- position.X * windowSize.X
        position.Y <- position.Y * windowSize.Y
        position.X <- position.X + windowPosition.X
        position.Y <- position.Y + windowPosition.Y
        v2 position.X position.Y
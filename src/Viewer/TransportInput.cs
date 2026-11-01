using System;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Carries the player's transport gesture from Godot input events to <see cref="TransportControl"/>: the transport chord
/// and, while the mode is pending, a right-click (taken, so the orders node gives no order) and the cursor over the
/// unit under the mouse. The node comes after the orders node in the tree, so it sees an event first. The selection
/// hands it the left-click and its changes (<see cref="UnitSelection.ClickTaker"/>).
/// </summary>
public partial class TransportInput : Node
{
    // stand-in: the cursor sheets hold 32 x 32 frames (Sprites/cursor.spr); an animated sheet shows its first frame.
    const int Frame = 32;
    const int NoTransportFrame = 5; // c_notransport: the sixth frame of curs_noanim

    readonly TransportControl _control;
    readonly UnitSelection _selection;
    readonly Texture2D? _transport;
    readonly Texture2D? _noTransport;
    TransportCursor _shown = TransportCursor.Default;

    /// <summary>
    /// Wires the control to the selection; reads the two cursor images of the install once. An image that cannot be read
    /// leaves the default cursor, is logged as a WARN, and goes to <c>gap</c> once (INV-0002).
    /// </summary>
    public TransportInput(TransportControl control, UnitSelection selection, GameFileSystem files, Action<string> gap)
    {
        _control = control;
        _selection = selection;
        _transport = Cursor(files, "Curs_Trans", 0, gap);
        _noTransport = Cursor(files, "Curs_NoAnim", NoTransportFrame, gap);
        selection.ClickTaker = control.Click;
        selection.SelectionChanged += control.SelectionChanged;
    }

    static Texture2D? Cursor(GameFileSystem files, string texture, int frame, Action<string> gap)
    {
        try
        {
            var sheet = GodotDecoders.Tga(new TextureKind().LoadBase(files, texture + ".tga").Data, texture);

            return ImageTexture.CreateFromImage(sheet.GetRegion(new Rect2I(frame * Frame, 0, Frame, Frame)));
        }
        catch (Exception e) // a cursor image that did not load is a gap, whatever the reason (INV-0002)
        {
            var text = $"{texture}.tga cannot be read, so the transport mode keeps the default cursor: {e.Message}";
            Log.Warn(Area.Combat, text);
            gap(text);

            return null;
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } key)
        {
            // stand-in: Godot's key names match the Input.map names for letters (see AlertKeys)
            var chord = new KeyChord
            {
                Key = OS.GetKeycodeString(key.Keycode),
                Shift = key.ShiftPressed,
                Control = key.CtrlPressed,
                Alt = key.AltPressed,
            };
            if (_control.Pressed(chord, key.Echo))
                GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && _control.RightClick())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var hovered = _selection.UnitAt(GetViewport().GetMousePosition())?.Object;
        var choice = _control.Cursor(hovered);
        if (choice == _shown)
            return;
        _shown = choice;
        var image = choice switch
        {
            TransportCursor.Transport => _transport,
            TransportCursor.NoTransport => _noTransport,
            _ => null,
        };
        Input.SetCustomMouseCursor(image, Input.CursorShape.Arrow, new Vector2(Frame / 2, Frame / 2));
    }
}

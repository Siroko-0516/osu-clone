// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Platform;
using osuTK;
using osuTK.Input;

namespace osu.Web;

public sealed class BrowserInputEvent
{
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public int Button { get; set; }
    public bool Pressed { get; set; }
}

/// <summary>Translates ordered browser events into the original framework input pipeline.</summary>
public sealed class BrowserInputHandler : InputHandler
{
    public override bool IsActive => Enabled.Value;
    public override string Description => "Browser pointer and keyboard";
    private readonly HashSet<Key> heldKeys = new();
    private readonly HashSet<MouseButton> heldButtons = new();

    public void Receive(BrowserInputEvent input)
    {
        if (!Enabled.Value || IsDisposed) return;

        switch (input.Kind)
        {
            case "move":
                PendingInputs.Enqueue(new MousePositionAbsoluteInput { Position = new Vector2(input.X, input.Y) });
                break;
            case "button":
                MouseButton button = input.Button switch { 0 => MouseButton.Left, 1 => MouseButton.Middle, 2 => MouseButton.Right, _ => MouseButton.LastButton };
                if (button == MouseButton.LastButton) return;
                PendingInputs.Enqueue(new MousePositionAbsoluteInput { Position = new Vector2(input.X, input.Y) });
                if (input.Pressed ? heldButtons.Add(button) : heldButtons.Remove(button))
                    PendingInputs.Enqueue(new MouseButtonInput(button, input.Pressed));
                break;
            case "key":
                string name = input.Code switch
                {
                    "ArrowLeft" => "Left", "ArrowRight" => "Right", "ArrowUp" => "Up", "ArrowDown" => "Down",
                    "ShiftLeft" => "LShift", "ShiftRight" => "RShift", "ControlLeft" => "LControl", "ControlRight" => "RControl",
                    "AltLeft" => "LAlt", "AltRight" => "RAlt", "Backspace" => "BackSpace",
                    _ when input.Code.StartsWith("Key", StringComparison.Ordinal) => input.Code[3..],
                    _ when input.Code.StartsWith("Digit", StringComparison.Ordinal) => "Number" + input.Code[5..],
                    _ => input.Code
                };
                if (Enum.TryParse<Key>(name, out var key) && key != Key.Unknown && Enum.IsDefined(key))
                {
                    if (input.Pressed ? heldKeys.Add(key) : heldKeys.Remove(key))
                        PendingInputs.Enqueue(new KeyboardKeyInput(key, input.Pressed));
                }
                break;
            case "wheel":
                PendingInputs.Enqueue(new MouseScrollRelativeInput { Delta = new Vector2(input.X, input.Y), IsPrecise = true });
                break;
            case "reset":
                foreach (var held in heldKeys) PendingInputs.Enqueue(new KeyboardKeyInput(held, false));
                foreach (var held in heldButtons) PendingInputs.Enqueue(new MouseButtonInput(held, false));
                heldKeys.Clear();
                heldButtons.Clear();
                break;
        }
    }
}

public sealed class WebGameHost : BrowserGameHost
{
    public BrowserInputHandler BrowserInput { get; } = new();
    public WebGameHost(string name) : base(name) { }
    public void Resize(int width, int height) => Config.SetValue(osu.Framework.Configuration.FrameworkSetting.WindowedSize, new System.Drawing.Size(Math.Max(1, width), Math.Max(1, height)));
    protected override IEnumerable<InputHandler> CreateAvailableInputHandlers() => new[] { BrowserInput };
}

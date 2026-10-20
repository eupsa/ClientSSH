using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// Plays the viewer's mouse and keyboard on this PC with SendInput. It only ever receives events that already passed
/// <see cref="InputEvent.TryParse"/> and only while the person allowed control. shortcut: virtual-key codes, so a
/// different keyboard layout on the two PCs can type other characters; Windows cannot inject into admin windows or the
/// secure desktop either.
/// </summary>
public sealed class SendInputSink : IInputSink
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseAbsolute = 0x8000, MouseWheel = 0x0800;
    private const uint KeyExtended = 0x0001, KeyUp = 0x0002;

    // (down, up) flags per button: left, right, middle
    private static readonly (uint Down, uint Up)[] Buttons = [(0x0002, 0x0004), (0x0008, 0x0010), (0x0020, 0x0040)];

    // Keys Windows treats as "extended": arrows, navigation block, right-hand modifiers, numpad divide, Windows keys.
    private static readonly HashSet<int> Extended =
        [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90, 0xA3, 0xA5];

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public nint Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    public void Apply(InputEvent input)
    {
        var data = new InputUnion();
        uint type;

        switch (input.Kind)
        {
            case InputKind.MouseMove:
                type = InputMouse;
                data.Mouse = new MouseInput { Dx = input.A, Dy = input.B, Flags = MouseMove | MouseAbsolute };
                break;
            case InputKind.MouseDown or InputKind.MouseUp:
                type = InputMouse;
                var (down, up) = Buttons[input.A];
                data.Mouse = new MouseInput { Flags = input.Kind == InputKind.MouseDown ? down : up };
                break;
            case InputKind.Wheel:
                type = InputMouse;
                data.Mouse = new MouseInput { Flags = MouseWheel, MouseData = (uint)input.A };
                break;
            default:
                type = InputKeyboard;
                data.Keyboard = new KeyboardInput
                {
                    Vk = (ushort)input.A,
                    Scan = (ushort)MapVirtualKey((uint)input.A, 0),
                    Flags = (Extended.Contains(input.A) ? KeyExtended : 0) | (input.Kind == InputKind.KeyUp ? KeyUp : 0),
                };
                break;
        }

        SendInput(1, [new Input { Type = type, Data = data }], Marshal.SizeOf<Input>());
    }
}

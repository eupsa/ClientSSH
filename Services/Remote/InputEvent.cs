using System.Buffers.Binary;

namespace ClientSSH.Services.Remote;

public enum InputKind : byte { MouseMove = 1, MouseDown = 2, MouseUp = 3, Wheel = 4, KeyDown = 5, KeyUp = 6 }

/// <summary>None: the host person has not decided yet.</summary>
public enum RemoteAccess : byte { None = 0, View = 1, Control = 2, Denied = 3 }

/// <summary>
/// One mouse or keyboard action sent by the viewing PC. Mouse positions are 0..65535 across the shared screen, so
/// the two screens need not have the same size. Anything out of range is rejected before it can reach SendInput.
/// </summary>
public readonly record struct InputEvent(InputKind Kind, int A, int B = 0)
{
    public const int Length = 9;
    public const int ButtonLeft = 0, ButtonRight = 1, ButtonMiddle = 2;

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        bytes[0] = (byte)Kind;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), A);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(5), B);
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out InputEvent input)
    {
        input = default;
        if (bytes.Length != Length || !Enum.IsDefined((InputKind)bytes[0])) return false;

        var kind = (InputKind)bytes[0];
        var a = BinaryPrimitives.ReadInt32BigEndian(bytes[1..]);
        var b = BinaryPrimitives.ReadInt32BigEndian(bytes[5..]);

        var valid = kind switch
        {
            InputKind.MouseMove => a is >= 0 and <= 65535 && b is >= 0 and <= 65535,
            InputKind.MouseDown or InputKind.MouseUp => a is >= 0 and <= 2,
            InputKind.Wheel => a is >= -12000 and <= 12000,
            _ => a is >= 1 and <= 254, // virtual-key codes
        };
        if (!valid) return false;

        input = new InputEvent(kind, a, b);
        return true;
    }
}

/// <summary>One encoded picture of the shared screen: an H.264 access unit (Annex B).</summary>
public sealed record VideoFrame(byte[] Data, bool IsKey, int Width, int Height);

/// <summary>The screen of the PC being shared. Null when there is nothing to send this time.</summary>
public interface IScreenSource
{
    VideoFrame? Next(bool keyFrame);
}

/// <summary>Where input from the viewing PC goes on the shared PC.</summary>
public interface IInputSink
{
    void Apply(InputEvent input);
}

using System.Numerics;
using System.Runtime.InteropServices;

namespace Videotoy.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = SizeInBytes)]
public struct ShadertoyUniformsBuffer
{
    public const int SizeInBytes = 128;

    public Vector3 Resolution;
    public float Time;

    public float TimeDelta;
    public int Frame;
    public float SampleRate;
    public float Padding0;

    public Vector4 Mouse;

    public Vector4 Date;

    public Vector4 ChannelResolution0;
    public Vector4 ChannelResolution1;
    public Vector4 ChannelResolution2;
    public Vector4 ChannelResolution3;
}

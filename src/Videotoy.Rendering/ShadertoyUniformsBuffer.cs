using System.Numerics;
using System.Runtime.InteropServices;

namespace Videotoy.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = SizeInBytes)]
public struct ShadertoyUniformsBuffer
{
    public const int SizeInBytes = 192;

    public Vector3 Resolution;
    public float Time;

    public float TimeDelta;
    public int Frame;
    public float SampleRate;
    public float FrameRate;

    public Vector4 Mouse;

    public Vector4 Date;

    public Vector4 ChannelResolution0;
    public Vector4 ChannelResolution1;
    public Vector4 ChannelResolution2;
    public Vector4 ChannelResolution3;

    /// <summary>
    /// `iChannelTime[4]` — un `float` par canal en GLSL, mais chaque élément
    /// d'un tableau HLSL est aligné sur 16 octets quel que soit son type
    /// (règle de layout de cbuffer D3D), d'où ce padding en `Vector4` dont
    /// seul `.X` est réellement lu côté HLSL (`iChannelTime[n].x`) — même
    /// convention que `ChannelResolution0-3` ci-dessus.
    /// </summary>
    public Vector4 ChannelTime0;
    public Vector4 ChannelTime1;
    public Vector4 ChannelTime2;
    public Vector4 ChannelTime3;
}

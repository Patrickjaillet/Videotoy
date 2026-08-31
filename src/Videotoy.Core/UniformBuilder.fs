module Videotoy.Core.UniformBuilder

open Videotoy.Core.Domain

let private defaultChannelResolutions : Resolution[] =
    Array.create 4 { Width = 0; Height = 0 }

let empty (resolution: Resolution) : UniformValues =
    { Resolution = resolution
      Time = 0.0
      TimeDelta = 0.0
      FrameIndex = 0
      MousePosition = (0.0, 0.0)
      SampleRate = 44100.0
      Date = (0.0, 0.0, 0.0, 0.0)
      ChannelResolutions = defaultChannelResolutions }

let forFrame (resolution: Resolution) (frame: RenderFrame) (previous: UniformValues) : UniformValues =
    { previous with
        Resolution = resolution
        Time = frame.TimeSeconds
        TimeDelta = frame.DeltaSeconds
        FrameIndex = frame.Index }

let withMousePosition (mousePosition: float * float) (uniforms: UniformValues) : UniformValues =
    { uniforms with MousePosition = mousePosition }

let withSampleRate (sampleRate: float) (uniforms: UniformValues) : UniformValues =
    { uniforms with SampleRate = sampleRate }

let withDate (year: float) (month: float) (day: float) (secondsSinceMidnight: float) (uniforms: UniformValues) : UniformValues =
    { uniforms with Date = (year, month, day, secondsSinceMidnight) }

let withChannelResolution (channelIndex: int) (resolution: Resolution) (uniforms: UniformValues) : UniformValues =
    let updated = Array.copy uniforms.ChannelResolutions
    if channelIndex >= 0 && channelIndex < updated.Length then
        updated.[channelIndex] <- resolution
    { uniforms with ChannelResolutions = updated }

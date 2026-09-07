# HavenOS Wave

This is the bounded first standalone Wave app surface.

## Functional journey

1. Launch Wave with a local `.wav` path.
2. Wave validates the RIFF/WAVE structure and accepts only 16-bit PCM in this first slice.
3. It reads real audio frames and produces 512 normalized waveform peaks using the same bounded sampling shape as the existing Imagine waveform reference.
4. The standalone surface prints duration, sample rate, channel count, and a compact waveform preview.
5. Missing, unsupported, or corrupt input fails closed with no fabricated waveform.

The slice is intentionally app-local under `HavenOS Apps/Wave`; it does not change shared HUI, shell routing, platform services, or the legacy Imagine journey.

## Focused validation

```text
dotnet build "HavenOS Apps/Wave/HavenOS.Wave.csproj"
dotnet run --project "HavenOS Apps/Wave/HavenOS.Wave.csproj" -- --self-test
```

The self-test writes a temporary one-second PCM tone, validates real bounded peaks and metadata, then confirms corrupt input is rejected.

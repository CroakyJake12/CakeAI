# Wave donor / reference provenance

Authoritative HavenOS base: `CroakyJake12/HavenAI` branch `havenos-main`, base commit `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.

## Located reference

The first Wave slice is based on the repository's existing real-audio waveform journey rather than an invented external donor:

- Introduction commit: `f7a3c051bdc0997860cd89f232689cf5ff4f93a3` (`int(PROJECT-CREATIVE): INT-008 add real audio waveforms`).
- Current reference source: `src/Haven.Desktop/Views/Pages/Imagine/ImagineAudioWaveformCache.cs` on `havenos-main`.
- Reference validation introduced with it: `tests/Haven.Desktop.Tests/ImagineAudioWaveformTests.cs`.
- Behavioral anchors retained here: real local audio only, 512 bounded peak buckets, at most 1024 probed frames per bucket, and fail-closed decode behavior instead of fabricated waveform data.

This standalone slice uses the reference for behavior and migration provenance. It does not alter or delete the Imagine implementation.

## Licence status

The donor/reference is first-party code in this same `CroakyJake12/HavenAI` repository. At the authoritative base commit, GitHub repository metadata reports `license: null`, and a root `LICENSE` file is not present. Therefore this slice does **not** assert an open-source licence that the repository has not declared.

The existing Imagine donor uses NAudio for decoding. This first Wave slice intentionally introduces no new package dependency and implements only a bounded 16-bit PCM WAV reader locally, so it does not copy or vendor NAudio code.

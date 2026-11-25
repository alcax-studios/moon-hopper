# 0001: Unity 6 with URP for a 3D game

- **Date:** 2026-11-25
- **Status:** accepted

## Context

The project was created from Unity's Universal 3D template.

## Decision

Use Unity 6000.6.10f1 with the Universal Render Pipeline (URP), the Input System, and the Pipeline package so AI agents can control the Editor.

## Alternatives considered

- **Built-in Render Pipeline:** deprecated from Unity 6.5.
- **HDRP:** higher visual ceiling, but heavier and desktop-only.

## Consequences

- Scales from mobile to PC through the two URP assets.
- Imported materials and shaders must be URP-compatible.

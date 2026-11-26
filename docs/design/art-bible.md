# Art Bible: the Station

Every color comes from `Assets/_Project/Scripts/Runtime/Core/Palette.cs` (the `St*` entries). Don't use literal colors elsewhere.

1. **Style:** Realistic-leaning brutalist-futurist. Bright, dry, matte architectural concrete (Ando-style board-tie panels with form-tie holes), light tech paneling, and exposed loft-style services. A few cyan light accents and orange underglow. Plants are used sparingly, set in real soil.
2. **Palette:** pale concrete `#E2E0DC` · floor concrete `#C9C5BD` · light tech panel `#B7BCC2` · panel light (neutral white) `#F2F6FA` · tech cyan `#6FE3FF` · accent orange `#FF6A2B` · desert sand `#C98A4F` · sky haze `#E7B88E` · planet bands `#D8A47C` / `#9C6A55`.
3. **Mood:** White office light inside. Through the giant window there's a permanent warm desert sunset under a huge ringed gas giant. Warmth stays outside, and clean daylight-white stays inside.

## Rules

- Interiors are matte and dry: concrete smoothness about 0.3, nothing wet-looking.
- Light shafts and dust stay subtle (they must never obscure the view).
- Glass reflects the interior at about 40% (`GlassReflectionProbe`).
- The legacy forest and zombie palette entries were removed with the prototype (ADR 0003).

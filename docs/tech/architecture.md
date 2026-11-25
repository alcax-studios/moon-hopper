# Technical Architecture

## Engine and packages

| Item | Version / setting |
|---|---|
| Unity | 6000.6.10f1 |
| Render pipeline | URP 17.6.0, with `PC_RPAsset` and `Mobile_RPAsset` in `Assets/Settings/` |
| Input | Input System 1.20.1 (`Assets/InputSystem_Actions.inputactions`) |
| UI | uGUI 2.6.0 (UI Toolkit is also available) |
| Editor automation | `com.unity.pipeline` 1.0.1, driven by the Unity CLI |
| AI | `com.unity.ai.assistant`, `com.unity.ai.inference` |

Keep this table in sync with `Packages/manifest.json`.

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/Scenes/SampleScene.unity` | Template placeholder |

## Systems

_Add a section per system as it's built: responsibility, key scripts, dependencies, data._

<!--
### Player controller
- **Responsibility:**
- **Scripts:**
- **Depends on:**
- **Data:**
-->

## Data and saves

_Not designed yet._

## Build targets

_Not chosen yet._

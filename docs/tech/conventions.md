# Conventions

## Folder layout

Project-owned content goes under `Assets/_Project/` so it stays separate from imported packages and store assets.

```
Assets/
  _Project/
    Art/
      Models/        # FBX / GLB, one subfolder per asset
      Materials/
      Textures/
      Animations/
    Audio/
      Music/
      SFX/
    Prefabs/
    Scenes/
    Scripts/
      Runtime/       # gameplay code
      Editor/        # editor-only tools
    Settings/        # ScriptableObject configs
    UI/
  Settings/          # URP assets (template default, leave in place)
docs/                # documentation (outside Assets)
```

The folders get created as they're needed. The template's `Assets/Scenes` and `Assets/TutorialInfo` can be removed once real content exists.

## Naming

| Thing | Style | Example |
|---|---|---|
| Scripts and classes | PascalCase | `PlayerController.cs` |
| Prefabs | PascalCase | `Goblin.prefab` |
| Materials | `M_` prefix | `M_GoblinSkin` |
| Textures | `T_` prefix plus a suffix | `T_Goblin_Albedo`, `T_Goblin_Normal` |
| Animation clips | `A_` prefix | `A_Goblin_Run` |
| Scenes | PascalCase | `MainMenu`, `Level01` |

## Code style

- C# namespaces: `Alcax.<Area>` (for example `Alcax.Player`, `Alcax.Combat`).
- One `MonoBehaviour` or `ScriptableObject` per file, with the file named after the class.
- Use `[SerializeField] private` fields over public fields.
- Tunable values go in ScriptableObjects or serialized fields, not hard-coded.
- Read input through the Input System actions asset, not `UnityEngine.Input`.
- Assembly definitions: `Alcax.Runtime` and `Alcax.Editor`, added once there's enough code to matter.

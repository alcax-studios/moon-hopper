using UnityEditor;
using UnityEditor.SceneManagement;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Quick map switching in the Editor: Alcax > Maps > Open (edit) or Play (open and enter Play mode).
    /// In game, the pause menu has a button for each other map.
    /// </summary>
    public static class MapMenu
    {
        const string Atrium = "Assets/_Project/Scenes/Atrium.unity";
        const string Playground = "Assets/_Project/Scenes/Playground.unity";

        [MenuItem("Alcax/Maps/Open Atrium", priority = 0)] static void OpenAtrium() => Open(Atrium, false);
        [MenuItem("Alcax/Maps/Open AI Playground", priority = 1)] static void OpenPlayground() => Open(Playground, false);
        [MenuItem("Alcax/Maps/Play Atrium", priority = 20)] static void PlayAtrium() => Open(Atrium, true);
        [MenuItem("Alcax/Maps/Play AI Playground", priority = 21)] static void PlayPlayground() => Open(Playground, true);

        static void Open(string path, bool play)
        {
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path);
            if (play) EditorApplication.isPlaying = true;
        }
    }
}

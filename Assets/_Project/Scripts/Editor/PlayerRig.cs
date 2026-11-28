using Alcax.Combat;
using Alcax.Core;
using Alcax.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>The first-person player rig (controller, weapons, interactor, flashlight, camera) and the title camera, shared by all level builders.</summary>
    public static class PlayerRig
    {
        public static (PlayerController, Transform) BuildPlayer(Vector3 position, float yaw, out Camera cam)
        {
            var player = new GameObject("Player");
            player.transform.position = position;
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            player.layer = 2; // Ignore Raycast: keeps the player out of the NavMesh bake and default raycasts.

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.05f;

            player.AddComponent<Health>();
            var obstacle = player.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
            obstacle.center = new Vector3(0f, 0.9f, 0f);
            obstacle.radius = 0.4f;
            obstacle.height = 1.8f;
            obstacle.carving = false;
            var pc = player.AddComponent<PlayerController>();
            var head = Node("Head", player.transform, new Vector3(0, 1.62f, 0));
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            SetRef(pc, "actions", actions);
            SetRef(pc, "head", head);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = head.position;
            cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 170f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            var viewRoot = Node("ViewModel", camGo.transform, Vector3.zero);
            var spot = Node("FlashlightSpot", camGo.transform, new Vector3(0.18f, -0.12f, 0.1f)).gameObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 28f;
            spot.spotAngle = 55f;
            spot.innerSpotAngle = 28f;
            spot.intensity = 5f;
            spot.color = Palette.Flashlight;
            spot.shadows = LightShadows.None;

            var wh = player.AddComponent<WeaponHolder>();
            WeaponData LoadW(string n) => AssetDatabase.LoadAssetAtPath<WeaponData>($"{Root}/Data/Weapons/{n}.asset");
            SetRef(wh, "fists", LoadW("W_Fists"));
            SetRefArray(wh, "allWeapons", new Object[] { LoadW("W_Bat"), LoadW("W_Machete"), LoadW("W_Pistol"), LoadW("W_Shotgun"), LoadW("W_Rifle"), LoadW("W_PlasmaCarbine") });
            SetRef(wh, "viewRoot", viewRoot);
            SetRef(wh, "aimCamera", cam);
            player.AddComponent<PlayerInteractor>();
            var fl = player.AddComponent<Flashlight>();
            SetRef(fl, "spot", spot);

            return (pc, head);
        }

        public static GameObject BuildCameras(Camera cam, Transform head, Vector3 rigPosition, Vector3 camLocal, Vector3 lookAtLocal, float titleFov)
        {
            var pcamGo = new GameObject("PlayerCam");
            pcamGo.transform.position = head.position;
            var vc = pcamGo.AddComponent<CinemachineCamera>();
            vc.Target.TrackingTarget = head;
            vc.Lens = LensSettings.Default;
            vc.Lens.FieldOfView = 72f;
            vc.Lens.NearClipPlane = 0.03f;
            vc.Lens.FarClipPlane = 170f;
            vc.Priority = new PrioritySettings { Enabled = true, Value = 10 };
            pcamGo.AddComponent<CinemachineHardLockToTarget>();
            pcamGo.AddComponent<CinemachineRotateWithFollowTarget>();
            pcamGo.AddComponent<CinemachineImpulseListener>();

            var rig = new GameObject("TitleRig");
            rig.transform.position = rigPosition;
            rig.AddComponent<TitleOrbit>();
            var tcamGo = new GameObject("TitleCam");
            tcamGo.transform.SetParent(rig.transform, false);
            tcamGo.transform.localPosition = camLocal;
            tcamGo.transform.localRotation = Quaternion.LookRotation(lookAtLocal - camLocal);
            var tc = tcamGo.AddComponent<CinemachineCamera>();
            tc.Lens = LensSettings.Default;
            tc.Lens.FieldOfView = titleFov;
            tc.Lens.NearClipPlane = 0.1f;
            tc.Lens.FarClipPlane = 170f;
            tc.Priority = new PrioritySettings { Enabled = true, Value = 20 };
            return tcamGo;
        }
    }
}

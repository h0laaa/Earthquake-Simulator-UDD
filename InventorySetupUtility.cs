#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Utilidad del editor para configurar automáticamente el sistema de inventario y colocar
/// los assets de Botiquín y Extintor en la escena con sus materiales, colisionadores y scripts de interacción.
/// </summary>
public static class InventorySetupUtility
{
    private const string BotiquinFbxPath = "Assets/Modelos3d/Botiquin.fbx";
    private const string ExtintorFbxPath = "Assets/Modelos3d/Extintor.fbx";

    private const string BotiquinMatPath = "Assets/Materials/Materials/Botiquin_Albedo.mat";
    private const string ExtintorMatPath = "Assets/Materials/Materials/Extintor_Albedo.mat";

    private const string BotiquinIconPath = "Assets/Materials/Icon_Botiquin.png";
    private const string ExtintorIconPath = "Assets/Materials/Icon_Extintor.png";

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            SetupProjectAssets(false);
        };
    }

    [MenuItem("Tools/Inventario/Colocar Botiquín y Extintor en la Escena", false, 30)]
    public static void PlaceItemsInSceneMenu()
    {
        SetupProjectAssets(true);
    }

    [MenuItem("Tools/Inventario/Configurar Componentes en Cámara", false, 31)]
    public static void SetupCameraComponentsMenu()
    {
        EnsureInventoryAndInteractionComponents(true);
    }

    public static void SetupProjectAssets(bool showDialog)
    {
        // 1. Remapear materiales en los FBX de Botiquín y Extintor
        RemapFbx(BotiquinFbxPath, BotiquinMatPath);
        RemapFbx(ExtintorFbxPath, ExtintorMatPath);

        // 2. Asegurar componentes en la cámara
        EnsureInventoryAndInteractionComponents(false);

        // 3. Colocar los objetos en la escena activa si no están presentes
        bool placedAny = EnsureItemsInScene();

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Inventario Configurado",
                "Se han colocado y configurado con éxito el Botiquín y el Extintor en la escena.\n\n" +
                "• Interacción: Acércate y presiona [E], pulsa el botón táctil en pantalla o usa el mando VR.\n" +
                "• Inventario: 2 casillas en la esquina superior izquierda.\n" +
                "• Soltar: Presiona las teclas [1] o [2], o haz clic en la casilla correspondiente.", "Aceptar");
        }
    }

    private static void EnsureInventoryAndInteractionComponents(bool showDialog)
    {
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();

        if (cam != null)
        {
            if (cam.CompareTag("Untagged"))
            {
                cam.tag = "MainCamera";
                EditorUtility.SetDirty(cam.gameObject);
            }

            if (cam.GetComponent<PlayerInteraction>() == null)
            {
                Undo.AddComponent<PlayerInteraction>(cam.gameObject);
            }
            if (cam.GetComponent<InventorySystem>() == null && Object.FindAnyObjectByType<InventorySystem>() == null)
            {
                Undo.AddComponent<InventorySystem>(cam.gameObject);
            }
            if (cam.GetComponent<ExtinguisherSprayController>() == null)
            {
                Undo.AddComponent<ExtinguisherSprayController>(cam.gameObject);
            }
        }
        else
        {
            if (Object.FindAnyObjectByType<InventorySystem>() == null)
            {
                GameObject invGo = new GameObject("InventorySystem");
                Undo.RegisterCreatedObjectUndo(invGo, "Crear InventorySystem");
                invGo.AddComponent<InventorySystem>();
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Componentes Configurados", "Se verificaron y añadieron los componentes de Inventario e Interacción a la cámara.", "Aceptar");
        }
    }

    private static bool EnsureItemsInScene()
    {
        bool changed = false;

        GameObject botiquinAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BotiquinFbxPath);
        GameObject extintorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ExtintorFbxPath);

        Material botiquinMat = AssetDatabase.LoadAssetAtPath<Material>(BotiquinMatPath);
        Material extintorMat = AssetDatabase.LoadAssetAtPath<Material>(ExtintorMatPath);

        Texture2D botiquinIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(BotiquinIconPath);
        Texture2D extintorIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(ExtintorIconPath);

        // Buscar cámara para posicionar los objetos cómodamente frente a ella
        Transform camTrans = Camera.main != null ? Camera.main.transform : null;
        if (camTrans == null)
        {
            Camera c = Object.FindAnyObjectByType<Camera>();
            if (c != null) camTrans = c.transform;
        }

        Vector3 camPos = camTrans != null ? camTrans.position : new Vector3(3.43f, 3.6f, 260f);
        Vector3 camFwd = camTrans != null ? camTrans.forward : new Vector3(0f, 0f, -1f);
        camFwd.y = 0f;
        if (camFwd.sqrMagnitude < 0.001f) camFwd = Vector3.back;
        camFwd.Normalize();

        Vector3 camRight = Vector3.Cross(Vector3.up, camFwd).normalized;

        // 1. Comprobar Botiquín en la escena
        InteractableItem existingBotiquin = null;
        foreach (InteractableItem item in Object.FindObjectsByType<InteractableItem>(FindObjectsSortMode.None))
        {
            if (item.name.ToLower().Contains("botiquin")) existingBotiquin = item;
        }

        if (existingBotiquin == null && botiquinAsset != null)
        {
            Vector3 pos = camPos + camFwd * 3.2f - camRight * 1.2f;
            pos.y = Mathf.Max(pos.y - 1.8f, 0.4f);

            GameObject botiquinInstance = PrefabUtility.InstantiatePrefab(botiquinAsset) as GameObject;
            if (botiquinInstance != null)
            {
                botiquinInstance.name = "Botiquin";
                botiquinInstance.transform.position = pos;
                botiquinInstance.transform.rotation = Quaternion.Euler(0f, camTrans != null ? camTrans.eulerAngles.y : 0f, 0f);

                ConfigureInteractable(botiquinInstance, "Botiquín", botiquinMat, botiquinIcon, InteractableItem.ItemType.Botiquin);

                Undo.RegisterCreatedObjectUndo(botiquinInstance, "Crear Botiquin");
                changed = true;
            }
        }

        // 2. Comprobar Extintor en la escena
        InteractableItem existingExtintor = null;
        foreach (InteractableItem item in Object.FindObjectsByType<InteractableItem>(FindObjectsSortMode.None))
        {
            if (item.name.ToLower().Contains("extintor")) existingExtintor = item;
        }

        if (existingExtintor == null && extintorAsset != null)
        {
            Vector3 pos = camPos + camFwd * 3.2f + camRight * 1.2f;
            pos.y = Mathf.Max(pos.y - 1.8f, 0.4f);

            GameObject extintorInstance = PrefabUtility.InstantiatePrefab(extintorAsset) as GameObject;
            if (extintorInstance != null)
            {
                extintorInstance.name = "Extintor";
                extintorInstance.transform.position = pos;
                extintorInstance.transform.rotation = Quaternion.Euler(0f, camTrans != null ? camTrans.eulerAngles.y : 0f, 0f);

                ConfigureInteractable(extintorInstance, "Extintor", extintorMat, extintorIcon, InteractableItem.ItemType.Extintor);

                Undo.RegisterCreatedObjectUndo(extintorInstance, "Crear Extintor");
                changed = true;
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded && changed)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        return changed;
    }

    private static void ConfigureInteractable(GameObject go, string displayName, Material mat, Texture2D icon, InteractableItem.ItemType itemType)
    {
        // 1. Asignar material si es necesario
        if (mat != null)
        {
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterial = mat;
                EditorUtility.SetDirty(mr);
            }
        }

        // 2. Colisionador
        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                box.center = go.transform.InverseTransformPoint(r.bounds.center);
                box.size = r.bounds.size;
            }
        }

        // 3. InteractableItem
        InteractableItem interactable = go.GetComponent<InteractableItem>();
        if (interactable == null)
        {
            interactable = go.AddComponent<InteractableItem>();
        }

        SerializedObject so = new SerializedObject(interactable);
        so.FindProperty("nombreItem").stringValue = displayName;
        so.FindProperty("tipoItem").enumValueIndex = (int)itemType;
        if (icon != null)
        {
            so.FindProperty("iconoInventario").objectReferenceValue = icon;
        }
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(go);
    }

    private static void RemapFbx(string fbxPath, string matPath)
    {
        if (!File.Exists(fbxPath) || !File.Exists(matPath)) return;

        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        Material targetMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (importer == null || targetMat == null) return;

        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        bool changed = false;

        foreach (Object sub in subAssets)
        {
            if (sub is Material subMat)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(subMat), targetMat);
                changed = true;
            }
        }

        if (changed)
        {
            AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
#endif

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Configura automáticamente el asset vending_machine con colisionadores, materiales
/// y el script VendingMachineHazard para que explote y quede en llamas durante el temblor.
/// </summary>
public static class VendingMachineSetupUtility
{
    private const string VendingFbxPath = "Assets/Modelos3d/vending_machine.fbx";
    private const string VendingMatPath = "Assets/Materials/M_VendingMachine.mat";

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            SetupVendingMachine(false);
        };
    }

    [MenuItem("Tools/Máquina Expendedora/Configurar y Colocar en la Escena", false, 40)]
    public static void PlaceVendingMachineMenu()
    {
        SetupVendingMachine(true);
    }

    [MenuItem("Tools/Sistema de Salud/Configurar Barra de Vida en Jugador", false, 50)]
    public static void SetupPlayerHealthMenu()
    {
        Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            if (cam.GetComponent<PlayerHealth>() == null)
            {
                cam.gameObject.AddComponent<PlayerHealth>();
                EditorUtility.SetDirty(cam.gameObject);
                EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            }
            EditorUtility.DisplayDialog("Sistema de Salud", "Barra de vida configurada en la cámara del jugador (esquina inferior izquierda).\n\n• Daño por fuego: Disminuye al tocar las llamas.\n• Daño por choque en temblor: Disminuye levemente al chocar con objetos durante el sismo.\n• Curación: Usa el Botiquín desde el inventario.", "Aceptar");
        }
    }

    [MenuItem("Tools/Sistema de Salud/Probar Daño 15 HP (Play Mode)", false, 51)]
    public static void TestDamageMenu()
    {
        if (PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.TakeDamage(15f, PlayerHealth.DamageType.Generic, "Prueba");
        }
        else
        {
            EditorUtility.DisplayDialog("Aviso", "Entra en Play Mode para probar el daño.", "Aceptar");
        }
    }

    [MenuItem("Tools/Sistema de Salud/Probar Curación con Botiquín (Play Mode)", false, 52)]
    public static void TestHealMenu()
    {
        if (PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.Heal(PlayerHealth.Instance.BotiquinHealAmount);
        }
        else
        {
            EditorUtility.DisplayDialog("Aviso", "Entra en Play Mode para probar la curación.", "Aceptar");
        }
    }

    [MenuItem("Tools/Máquina Expendedora/Probar Explosión (Play Mode)", false, 41)]
    public static void TestExplosionMenu()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor entra en Play Mode para probar la explosión.", "Aceptar");
            return;
        }

        VendingMachineHazard hazard = Object.FindAnyObjectByType<VendingMachineHazard>();
        if (hazard != null)
        {
            hazard.ExecuteExplosion();
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "No se encontró ningún objeto con el componente VendingMachineHazard en la escena.", "Aceptar");
        }
    }

    [MenuItem("Tools/Máquina Expendedora/Probar Golpear y Soltar Lata (Play Mode)", false, 42)]
    public static void TestHitVendingMachineMenu()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor entra en Play Mode para probar golpear la máquina.", "Aceptar");
            return;
        }

        Compra compra = Object.FindAnyObjectByType<Compra>();
        if (compra != null)
        {
            compra.IntentarGolpear();
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "No se encontró ningún objeto con el componente Compra en la escena.", "Aceptar");
        }
    }

    [MenuItem("Tools/Máquina Expendedora/Configurar Textura y Materiales de la Lata", false, 43)]
    public static void SetupCanMaterialsMenu()
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/T_Lata_Refresco.png");
        Material matCuerpo = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Lata_Refresco.mat");
        Material matAlu = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Lata_Aluminio.mat");

        int count = 0;
        foreach (Compra compra in Object.FindObjectsByType<Compra>(FindObjectsSortMode.None))
        {
            compra.TexturaLata = tex;
            compra.MaterialCuerpoLata = matCuerpo;
            compra.MaterialAluminioLata = matAlu;
            compra.InicializarMaterialesYTexturas();
            EditorUtility.SetDirty(compra);
            EditorSceneManager.MarkSceneDirty(compra.gameObject.scene);
            count++;
        }

        EditorUtility.DisplayDialog("Textura de la Lata Configurada",
            $"Se han configurado y vinculado las texturas y materiales de refresco en {count} componente(s) Compra de la escena activa.\n\n" +
            "• Textura: T_Lata_Refresco.png (Etiqueta de refresco Cola)\n" +
            "• Material Cuerpo: M_Lata_Refresco.mat (Lit URP con textura y reflejos)\n" +
            "• Material Aluminio: M_Lata_Aluminio.mat (Aluminio pulido para tapa, base y anilla)", "Aceptar");
    }

    public static void SetupVendingMachine(bool showDialog)
    {
        // 1. Remapear material en el FBX
        RemapFbx(VendingFbxPath, VendingMatPath);

        // 2. Comprobar si ya existe en la escena activa
        bool placed = EnsureVendingMachineInScene();

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Máquina Expendedora Configurada",
                "La máquina expendedora (vending_machine) ha sido configurada con éxito.\n\n" +
                "• Golpe y Compra (Compra.cs): Si la máquina NO está en llamas, puedes golpearla (Clic Izq / tecla F) y te soltará el objeto 'lata_Lowpoly'.\n" +
                "• Bloqueo por Fuego: Si la máquina está ardiendo tras el temblor, no permite comprar ni golpearla hasta apagarla con el extintor.\n" +
                "• Temblor e Incendio: Cuando comience el temblor (CameraShake), esperará un breve instante y explotará quedando en llamas.", "Aceptar");
        }
    }

    private static bool EnsureVendingMachineInScene()
    {
        GameObject vendingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VendingFbxPath);
        Material vendingMat = AssetDatabase.LoadAssetAtPath<Material>(VendingMatPath);

        // Buscar si ya existe en la escena
        VendingMachineHazard existingHazard = Object.FindAnyObjectByType<VendingMachineHazard>();
        if (existingHazard != null)
        {
            ConfigureHazardComponents(existingHazard.gameObject, vendingMat);
            return false;
        }

        // Si no tiene el componente pero el objeto ya está en la escena
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name.ToLower().Contains("vending"))
            {
                ConfigureHazardComponents(go, vendingMat);
                return false;
            }
        }

        if (vendingAsset == null) return false;

        // Posicionar frente a la cámara o cerca de la entrada
        Camera c = Camera.main;
        if (c == null) c = Object.FindAnyObjectByType<Camera>();
        if (c != null)
        {
            if (c.CompareTag("Untagged"))
            {
                c.tag = "MainCamera";
                EditorUtility.SetDirty(c.gameObject);
            }
            if (c.GetComponent<PlayerHealth>() == null)
            {
                c.gameObject.AddComponent<PlayerHealth>();
                EditorUtility.SetDirty(c.gameObject);
            }
        }
        Transform camTrans = c != null ? c.transform : null;

        Vector3 camPos = camTrans != null ? camTrans.position : new Vector3(3.43f, 3.6f, 260f);
        Vector3 camFwd = camTrans != null ? camTrans.forward : new Vector3(0f, 0f, -1f);
        camFwd.y = 0f;
        if (camFwd.sqrMagnitude < 0.001f) camFwd = Vector3.back;
        camFwd.Normalize();

        Vector3 camRight = Vector3.Cross(Vector3.up, camFwd).normalized;

        // Ubicación visible: a 5 metros frente a la cámara, ligeramente a la derecha
        Vector3 spawnPos = camPos + camFwd * 5.0f + camRight * 2.2f;
        spawnPos.y = Mathf.Max(spawnPos.y - 2.5f, 0f);

        GameObject instance = PrefabUtility.InstantiatePrefab(vendingAsset) as GameObject;
        if (instance != null)
        {
            instance.name = "vending_machine";
            instance.transform.position = spawnPos;
            // Mirando hacia el jugador
            instance.transform.rotation = Quaternion.Euler(0f, (camTrans != null ? camTrans.eulerAngles.y : 0f) + 180f, 0f);

            ConfigureHazardComponents(instance, vendingMat);

            Undo.RegisterCreatedObjectUndo(instance, "Colocar vending_machine");

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }

            return true;
        }

        return false;
    }

    private static void ConfigureHazardComponents(GameObject go, Material mat)
    {
        if (mat != null)
        {
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterial = mat;
                EditorUtility.SetDirty(mr);
            }
        }

        // Colisionador
        if (go.GetComponent<Collider>() == null && go.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                box.center = go.transform.InverseTransformPoint(r.bounds.center);
                box.size = r.bounds.size;
            }
            else
            {
                box.center = new Vector3(0f, 1f, 0f);
                box.size = new Vector3(1f, 2f, 1f);
            }
        }

        // Componente de peligro de explosión e incendio
        if (go.GetComponent<VendingMachineHazard>() == null)
        {
            go.AddComponent<VendingMachineHazard>();
        }

        // Componente Compra para golpear y soltar lata_Lowpoly si no está en llamas
        Compra compra = go.GetComponent<Compra>();
        if (compra == null)
        {
            compra = go.AddComponent<Compra>();
        }
        compra.InicializarMaterialesYTexturas();

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

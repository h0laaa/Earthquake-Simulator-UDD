#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Aplica materiales y texturas PBR inspirados en el edificio de referencia (Universidad del Desarrollo):
/// 1. Hormigón Visto (M_Edificio_Hormigon): Fachadas, muros y vigas estructurales con juntas y conos de encofrado.
/// 2. Madera / Cielo Falso (M_Edificio_MaderaTecho): Paneles modulares cálidos en el voladizo/alero suspendido.
/// 3. Pavimento (M_Edificio_Suelo): Adoquines y baldosas de hormigón exterior para explanada y suelo.
/// </summary>
public static class EdificioTextureUtility
{
    private const string MaterialsFolder = "Assets/Materials";

    public const string HormigonMatPath = "Assets/Materials/M_Edificio_Hormigon.mat";
    public const string MaderaTechoMatPath = "Assets/Materials/M_Edificio_MaderaTecho.mat";
    public const string SueloMatPath = "Assets/Materials/M_Edificio_Suelo.mat";

    public const string HormigonTexPath = "Assets/Materials/T_Hormigon_Base.png";
    public const string MaderaTechoTexPath = "Assets/Materials/T_Techo_Madera.png";
    public const string SueloTexPath = "Assets/Materials/T_Suelo_Pavimento.png";

    private static readonly string[] BuildingFbxPaths = new string[]
    {
        "Assets/Modelos3d/Edificio.fbx"
    };

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            ApplyReferenceMaterials(false);
        };
    }

    [MenuItem("Tools/Edificio/Aplicar Materiales de Referencia al Edificio (Automático)", false, 20)]
    public static void ApplyReferenceMaterialsMenu()
    {
        ApplyReferenceMaterials(true);
    }

    [MenuItem("Tools/Edificio/Asignar Hormigón Visto a Selección", false, 21)]
    public static void ApplyHormigonToSelection()
    {
        ApplyMaterialToSelectedObjects(HormigonMatPath, "Hormigón Visto");
    }

    [MenuItem("Tools/Edificio/Asignar Madera de Techo a Selección", false, 22)]
    public static void ApplyMaderaTechoToSelection()
    {
        ApplyMaterialToSelectedObjects(MaderaTechoMatPath, "Madera de Techo");
    }

    [MenuItem("Tools/Edificio/Asignar Pavimento a Selección", false, 23)]
    public static void ApplySueloToSelection()
    {
        ApplyMaterialToSelectedObjects(SueloMatPath, "Pavimento Exterior");
    }

    public static void ApplyReferenceMaterials(bool showDialog)
    {
        // 1. Asegurar carpeta de materiales
        if (!Directory.Exists(MaterialsFolder))
        {
            Directory.CreateDirectory(MaterialsFolder);
            AssetDatabase.Refresh();
        }

        // 2. Configurar texturas
        ConfigureTextureImporter(HormigonTexPath);
        ConfigureTextureImporter(MaderaTechoTexPath);
        ConfigureTextureImporter(SueloTexPath);

        // 3. Cargar / Configurar los 3 Materiales URP Lit
        Material matHormigon = EnsureMaterial(HormigonMatPath, HormigonTexPath,
            new Color(0.88f, 0.86f, 0.82f, 1f), smoothness: 0.22f, metallic: 0.02f, tiling: new Vector2(3f, 3f));

        Material matMadera = EnsureMaterial(MaderaTechoMatPath, MaderaTechoTexPath,
            Color.white, smoothness: 0.35f, metallic: 0f, tiling: new Vector2(4f, 4f));

        Material matSuelo = EnsureMaterial(SueloMatPath, SueloTexPath,
            new Color(0.9f, 0.9f, 0.9f, 1f), smoothness: 0.18f, metallic: 0f, tiling: new Vector2(8f, 8f));

        AssetDatabase.SaveAssets();

        // 4. Remapear materiales en los FBX de Edificio
        foreach (string fbxPath in BuildingFbxPaths)
        {
            RemapFbxMaterials(fbxPath, matHormigon);
        }

        // 5. Aplicar a las mallas en la escena activa (excluyendo pilares)
        int updatedCount = 0;
        int buildingMeshesCount = 0;

        MeshRenderer[] allRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (MeshRenderer mr in allRenderers)
        {
            string name = mr.gameObject.name.ToLower();

            // Omitir pilares por completo
            if (name.Contains("pilar")) continue;

            Transform cur = mr.transform;
            bool isBuilding = false;
            bool isPillar = false;

            // Revisar ancestros
            while (cur != null)
            {
                string cName = cur.name.ToLower();
                if (cName.Contains("pilar"))
                {
                    isPillar = true;
                    break;
                }
                if (cName.Contains("edificio")) isBuilding = true;
                cur = cur.parent;
            }

            if (isPillar) continue;

            if (isBuilding)
            {
                Undo.RecordObject(mr, "Aplicar Textura Edificio");

                // Distinguir partes si tienen nombre específico de techo o piso
                if (name.Contains("techo") || name.Contains("cielo") || name.Contains("ceiling") || name.Contains("madera"))
                {
                    mr.sharedMaterial = matMadera;
                }
                else if (name.Contains("suelo") || name.Contains("piso") || name.Contains("floor") || name.Contains("pavimento"))
                {
                    mr.sharedMaterial = matSuelo;
                }
                else
                {
                    mr.sharedMaterial = matHormigon;
                }

                EditorUtility.SetDirty(mr);
                buildingMeshesCount++;
                updatedCount++;
            }
            else if (name.Contains("suelo") || name.Contains("floor") || name.Contains("piso") || name.Contains("pavimento"))
            {
                Undo.RecordObject(mr, "Aplicar Pavimento a Suelo");
                mr.sharedMaterial = matSuelo;
                EditorUtility.SetDirty(mr);
                updatedCount++;
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded && updatedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log($"[EdificioTextureUtility] Materiales aplicados: {buildingMeshesCount} partes del edificio (Total: {updatedCount}).");

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Materiales de Edificio Aplicados",
                $"Se han aplicado con éxito los materiales estilo referencia:\n\n" +
                $"• Mallas del Edificio: {buildingMeshesCount}\n" +
                $"• Total de mallas actualizadas: {updatedCount}\n\n" +
                $"Materiales utilizados:\n" +
                $"• {HormigonMatPath} (Hormigón Visto)\n" +
                $"• {MaderaTechoMatPath} (Cielo Falso de Madera)\n" +
                $"• {SueloMatPath} (Pavimento Exterior)", "Aceptar");
        }
    }

    private static Material EnsureMaterial(string matPath, string texPath, Color color, float smoothness, float metallic, Vector2 tiling)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
        if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void ConfigureTextureImporter(string path)
    {
        if (!File.Exists(path)) return;

        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            bool modified = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; modified = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; modified = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; modified = true; }
            if (ti.filterMode != FilterMode.Bilinear) { ti.filterMode = FilterMode.Bilinear; modified = true; }

            if (modified)
            {
                ti.SaveAndReimport();
            }
        }
    }

    private static void RemapFbxMaterials(string fbxPath, Material targetMaterial)
    {
        if (!File.Exists(fbxPath)) return;

        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        bool dirty = false;

        foreach (Object sub in subAssets)
        {
            if (sub is Material subMat)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(subMat), targetMaterial);
                dirty = true;
            }
        }

        if (dirty)
        {
            AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }
    }

    private static void ApplyMaterialToSelectedObjects(string matPath, string matDisplayName)
    {
        if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor selecciona uno o más objetos en la jerarquía (Hierarchy) primero.", "Aceptar");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            ApplyReferenceMaterials(false);
            material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer mr in renderers)
            {
                Undo.RecordObject(mr, $"Asignar {matDisplayName}");
                mr.sharedMaterial = material;
                EditorUtility.SetDirty(mr);
                count++;
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded && count > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log($"[EdificioTextureUtility] Se asignó '{matDisplayName}' a {count} mallas seleccionadas.");
        EditorUtility.DisplayDialog("Material Asignado",
            $"Se aplicó el material '{matDisplayName}' a {count} mallas de los objetos seleccionados.", "Aceptar");
    }
}
#endif

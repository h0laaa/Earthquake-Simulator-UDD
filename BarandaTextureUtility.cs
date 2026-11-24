#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Genera la textura y material Negro Mate y los aplica automáticamente a los assets y modelos de baranda.
/// </summary>
public static class BarandaTextureUtility
{
    private const string MaterialsFolder = "Assets/Materials";
    private const string TexturePath = "Assets/Materials/T_Baranda_NegroMate.png";
    private const string MaterialPath = "Assets/Materials/M_Baranda_NegroMate.mat";

    private static readonly string[] BarandaFbxPaths = new string[]
    {
        "Assets/Modelos3d/Asset_2_Baranda.fbx",
        "Assets/Modelos3d/Asset_2.2_Baranda.fbx"
    };

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            ApplyMatteBlackToBarandas(false);
        };
    }

    [MenuItem("Tools/Aplicar Textura Negro Mate a Barandas", false, 10)]
    public static void ApplyMatteBlackMenu()
    {
        ApplyMatteBlackToBarandas(true);
    }

    [MenuItem("Tools/Aplicar Material Negro Mate a Selección", false, 11)]
    public static void ApplyMatteBlackToSelection()
    {
        if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor selecciona uno o más objetos en la jerarquía (Hierarchy) primero.", "Aceptar");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            ApplyMatteBlackToBarandas(false);
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer mr in renderers)
            {
                Undo.RecordObject(mr, "Aplicar Material Negro Mate a Selección");
                mr.sharedMaterial = material;
                EditorUtility.SetDirty(mr);
                count++;
            }
        }

        Debug.Log($"[BarandaTextureUtility] Se aplicó el material Negro Mate a {count} elementos seleccionados.");
        EditorUtility.DisplayDialog("Material Aplicado", $"Se aplicó el material Negro Mate a {count} mallas de los objetos seleccionados.", "Aceptar");
    }

    public static void ApplyMatteBlackToBarandas(bool showDialog)
    {
        // 1. Asegurar que exista la carpeta de materiales
        if (!Directory.Exists(MaterialsFolder))
        {
            Directory.CreateDirectory(MaterialsFolder);
            AssetDatabase.Refresh();
        }

        // 2. Generar textura de pintura electrostática negro mate (micro-rugosidad)
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null)
        {
            texture = GenerateMatteBlackTexture();
            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(TexturePath, pngBytes);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            TextureImporter ti = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        // 3. Crear y configurar el Material URP Lit
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            material = new Material(litShader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        // Ajustes de Negro Mate en PBR:
        // Color carbón oscuro / negro mate realista
        Color matteBlackColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", matteBlackColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", matteBlackColor);

        // Asignar textura con grano de pintura mate
        if (texture != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        }

        // Smoothness = 0.20 (acabado mate, sin brillos especulares reflectantes)
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);

        // Metallic = 0.15 (metal con pintura en polvo)
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.15f);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        // 4. Remapear el material en los importadores de los modelos FBX de Baranda
        foreach (string fbxPath in BarandaFbxPaths)
        {
            if (File.Exists(fbxPath))
            {
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                    foreach (Object sub in subAssets)
                    {
                        if (sub is Material subMat)
                        {
                            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(subMat), material);
                        }
                    }
                    AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
                    AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        // 5. Aplicar inmediatamente a todas las barandas en la escena activa
        int updatedRenderers = 0;
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (MeshRenderer mr in renderers)
        {
            string objName = mr.gameObject.name.ToLower();
            Transform parent = mr.transform.parent;
            string parentName = parent != null ? parent.name.ToLower() : "";

            if (objName.Contains("baranda") || parentName.Contains("baranda"))
            {
                Undo.RecordObject(mr, "Aplicar Material Negro Mate");
                mr.sharedMaterial = material;
                EditorUtility.SetDirty(mr);
                updatedRenderers++;
            }
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded && updatedRenderers > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        Debug.Log($"[BarandaTextureUtility] Se aplicó el material Negro Mate a {updatedRenderers} barandas en la escena y en los FBX.");

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Textura Negro Mate Aplicada",
                $"Se ha generado la textura y aplicado el material Negro Mate a {updatedRenderers} barandas de la escena.\n\nMaterial guardado en: {MaterialPath}", "Aceptar");
        }
    }

    private static Texture2D GenerateMatteBlackTexture()
    {
        int width = 512;
        int height = 512;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
        tex.name = "T_Baranda_NegroMate";

        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;
                float ny = (float)y / height;

                // Ruido orgánico de pintura mate
                float noise1 = Mathf.PerlinNoise(nx * 35f, ny * 35f) * 0.035f;
                float noise2 = Mathf.PerlinNoise(nx * 90f, ny * 90f) * 0.02f;
                float fineGrain = (Random.value - 0.5f) * 0.015f;

                float lum = 0.11f + noise1 + noise2 + fineGrain;
                lum = Mathf.Clamp(lum, 0.07f, 0.18f);

                pixels[y * width + x] = new Color(lum, lum, lum, 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
#endif

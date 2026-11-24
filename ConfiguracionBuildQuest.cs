#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Evita el error "La aplicación no se ha instalado debido a un conflicto de paquete" en Meta Quest.
///
/// CAUSA: el proyecto usaba el nombre de paquete por defecto de la plantilla de Unity
/// (com.UnityTechnologies.com.unity.template.urpblank) y se firmaba con la clave de depuración,
/// que es DISTINTA en cada PC. Si en el visor ya hay una app con ese nombre firmada en otro PC
/// (una build anterior u otro proyecto hecho con la misma plantilla), Android rechaza la instalación.
///
/// QUÉ HACE (automáticamente antes de cada build de Android):
///  1. Si el nombre de paquete es el de la plantilla, lo cambia por uno propio (ver 'PaquetePropio').
///  2. Sube el "Bundle Version Code" en 1, para que cada APK sea más nuevo que el anterior.
///  3. Avisa en la consola si se está firmando con la clave de depuración (cambia según el PC).
///
/// INSTALACIÓN: copiar este archivo en una carpeta llamada "Editor" dentro de Assets
/// (por ejemplo Assets/Editor/ConfiguracionBuildQuest.cs). No se incluye en el juego.
/// También se puede ejecutar a mano: menú Tools > Earthquake Simulator > Preparar build para Meta Quest.
/// </summary>
public class ConfiguracionBuildQuest : IPreprocessBuildWithReport
{
    /// <summary>Nombre de paquete único del juego. Cámbialo si quieres, pero mantenlo fijo entre builds.</summary>
    public const string PaquetePropio = "com.earthquakesim.vr";

    private const string EmpresaPropia = "EarthquakeSim";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;

        AsegurarNombreDePaquete();

        PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode + 1);

        Debug.Log($"[ConfiguracionBuildQuest] Paquete: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}  ·  " +
                  $"Version Code: {PlayerSettings.Android.bundleVersionCode}  ·  Versión: {PlayerSettings.bundleVersion}");

        if (!PlayerSettings.Android.useCustomKeystore)
        {
            Debug.LogWarning("[ConfiguracionBuildQuest] El APK se firma con la clave de DEPURACIÓN de este PC. " +
                             "Si compilas en otro ordenador, el Quest volverá a dar 'conflicto de paquete' al actualizar. " +
                             "Solución: Player Settings > Android > Publishing Settings > Keystore Manager, crea un keystore propio " +
                             "y úsalo en todos los PCs (guárdalo junto al proyecto y no pierdas la contraseña).");
        }
    }

    [MenuItem("Tools/Earthquake Simulator/Preparar build para Meta Quest")]
    public static void PrepararManualmente()
    {
        AsegurarNombreDePaquete();
        AssetDatabase.SaveAssets();

        string firma = PlayerSettings.Android.useCustomKeystore
            ? "Keystore propio (correcto)"
            : "Clave de depuración de este PC (cambia en cada ordenador)";

        EditorUtility.DisplayDialog("Build para Meta Quest",
            "Nombre de paquete: " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + "\n" +
            "Bundle Version Code: " + PlayerSettings.Android.bundleVersionCode + " (sube solo en cada build)\n" +
            "Firma: " + firma,
            "Aceptar");
    }

    /// <summary>Sustituye el nombre de paquete de la plantilla por uno propio y activa "Override Default Package Name".</summary>
    private static void AsegurarNombreDePaquete()
    {
        string actual = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (!EsNombrePorDefecto(actual)) return;

        ActivarOverrideDePaquete();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PaquetePropio);

        // Si la casilla "Override Default Package Name" no se pudo activar, Unity genera el nombre como
        // com.<Company Name>.<Product Name>: al menos que no use la empresa de la plantilla.
        if (EsNombrePorDefecto(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)) &&
            (PlayerSettings.companyName == "UnityTechnologies" || PlayerSettings.companyName == "DefaultCompany"))
        {
            PlayerSettings.companyName = EmpresaPropia;
        }

        string nuevo = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (EsNombrePorDefecto(nuevo))
        {
            Debug.LogError("[ConfiguracionBuildQuest] No se pudo cambiar el nombre de paquete automáticamente. Hazlo a mano en " +
                           "Player Settings > Android > Other Settings > Identification: marca 'Override Default Package Name' " +
                           "y escribe '" + PaquetePropio + "'.");
        }
        else
        {
            Debug.Log($"[ConfiguracionBuildQuest] Nombre de paquete cambiado de '{actual}' a '{nuevo}'. " +
                      "En el Quest se instalará como una app nueva (la antigua puedes desinstalarla desde 'Fuentes desconocidas').");
        }
    }

    private static bool EsNombrePorDefecto(string identificador)
    {
        if (string.IsNullOrEmpty(identificador)) return true;
        string id = identificador.ToLowerInvariant();
        return id.Contains("unity.template") ||
               id.StartsWith("com.unitytechnologies.") ||
               id.StartsWith("com.defaultcompany.") ||
               id == "com.company.productname";
    }

    /// <summary>La casilla no tiene API pública: se activa en ProjectSettings.asset si existe la propiedad.</summary>
    private static void ActivarOverrideDePaquete()
    {
        Object[] ajustes = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (ajustes == null || ajustes.Length == 0 || ajustes[0] == null) return;

        SerializedObject so = new SerializedObject(ajustes[0]);
        SerializedProperty overrideProp = so.FindProperty("overrideDefaultApplicationIdentifier");
        if (overrideProp != null && overrideProp.propertyType == SerializedPropertyType.Boolean && !overrideProp.boolValue)
        {
            overrideProp.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

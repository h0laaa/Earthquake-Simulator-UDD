using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Administrador y configurador de iluminación realista de día (Daylight Lighting).
/// Transforma el ambiente del juego en un día soleado, limpio y luminoso:
/// - Sol direccional cálido y potente con ángulo cenital de media mañana/tarde.
/// - Sombras suaves y naturales (no negras).
/// - Luz ambiental tricolor rica (cielo azul brillante, horizonte claro, rebote de suelo cálido).
/// - Post-procesamiento URP activo en la cámara (Exposición de día, Tonemapping cinematográfico, Contraste, Saturación viva).
/// - Niebla atmosférica sutil de horizonte para profundidad.
/// Funciona automáticamente en tiempo de ejecución (Play Mode) y en el Editor.
/// </summary>
[ExecuteAlways]
public class DaylightManager : MonoBehaviour
{
    public static DaylightManager Instance { get; private set; }

    [Header("--- CONFIGURACIÓN DEL SOL (DIRECTIONAL LIGHT) ---")]
    [Tooltip("Intensidad de la luz solar directa")]
    [Range(0.8f, 3.5f)]
    [SerializeField] private float sunIntensity = 1.6f;

    [Tooltip("Color de la luz solar (blanco cálido natural)")]
    [SerializeField] private Color sunColor = new Color(1.0f, 0.98f, 0.94f, 1f);

    [Tooltip("Temperatura de color en Kelvin (6500K = luz de día neutra)")]
    [Range(4000f, 8500f)]
    [SerializeField] private float sunColorTemperature = 6500f;

    [Tooltip("Ángulo de inclinación y rotación del sol (X: elevación, Y: orientación este-oeste)")]
    [SerializeField] private Vector3 sunRotationEuler = new Vector3(52f, -32f, 0f);

    [Tooltip("Opacidad de las sombras proyectadas (0 = transparentes, 1 = opacas)")]
    [Range(0.4f, 1.0f)]
    [SerializeField] private float shadowStrength = 0.80f;

    [Header("--- CICLO DIURNO (AMANECER <-> ATARDECER, NUNCA DE NOCHE) ---")]
    [Tooltip("Mueve el sol de forma continua entre el amanecer y el atardecer (ida y vuelta con curva suave: sin saltos de iluminación)")]
    [SerializeField] private bool activarCicloDiurno = true;

    [Tooltip("Segundos que tarda el sol en ir del amanecer al atardecer")]
    [Range(30f, 3600f)]
    [SerializeField] private float duracionAmanecerAtardecer = 240f;

    [Tooltip("Momento del día al empezar (0 = amanecer, 0.5 = mediodía, 1 = atardecer)")]
    [Range(0f, 1f)]
    [SerializeField] private float progresoInicial = 0.3f;

    [Tooltip("Elevación del sol (grados) al amanecer y al atardecer")]
    [Range(2f, 30f)]
    [SerializeField] private float elevacionAmanecer = 8f;

    [Tooltip("Elevación máxima del sol (grados) a mediodía")]
    [Range(30f, 89f)]
    [SerializeField] private float elevacionMediodia = 62f;

    [Tooltip("Orientación (Y) del sol al amanecer")]
    [SerializeField] private float azimutAmanecer = -110f;

    [Tooltip("Orientación (Y) del sol al atardecer")]
    [SerializeField] private float azimutAtardecer = 46f;

    [Tooltip("Temperatura de color del sol cerca del horizonte (luz más cálida al amanecer/atardecer)")]
    [Range(2500f, 6500f)]
    [SerializeField] private float temperaturaHorizonte = 4300f;

    [Tooltip("Intensidad relativa del sol en el horizonte respecto a mediodía")]
    [Range(0.2f, 1f)]
    [SerializeField] private float intensidadRelativaHorizonte = 0.55f;

    [Header("--- LUZ AMBIENTAL (ILUMINACIÓN INDIRECTA DE DÍA) ---")]
    [Tooltip("Color ambiental recibido desde el cielo")]
    [SerializeField] private Color ambientSkyColor = new Color(0.72f, 0.84f, 1.0f, 1f);

    [Tooltip("Color ambiental en la línea del horizonte")]
    [SerializeField] private Color ambientEquatorColor = new Color(0.78f, 0.82f, 0.88f, 1f);

    [Tooltip("Color de rebote de luz desde el suelo")]
    [SerializeField] private Color ambientGroundColor = new Color(0.52f, 0.50f, 0.46f, 1f);

    [Tooltip("Multiplicador de intensidad de la luz ambiental")]
    [Range(0.8f, 2.5f)]
    [SerializeField] private float ambientIntensity = 1.25f;

    [Header("--- NIEBLA ATMOSFÉRICA DE DÍA ---")]
    [Tooltip("Habilita una neblina azulada lejana muy sutil que da profundidad natural")]
    [SerializeField] private bool enableAtmosphericFog = true;

    [Tooltip("Color de la bruma atmosférica diurna")]
    [SerializeField] private Color fogColor = new Color(0.76f, 0.84f, 0.94f, 1f);

    [Tooltip("Densidad de la niebla")]
    [Range(0.0001f, 0.008f)]
    [SerializeField] private float fogDensity = 0.0012f;

    [Header("--- POST-PROCESAMIENTO URP (GLOBAL VOLUME) ---")]
    [Tooltip("Compensación de exposición fotográfica para iluminar la escena")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float postExposure = 0.65f;

    [Tooltip("Contraste visual de la escena diurna")]
    [Range(0f, 25f)]
    [SerializeField] private float contrast = 8f;

    [Tooltip("Saturación de color (resalta el verde de árboles y el cielo)")]
    [Range(0f, 30f)]
    [SerializeField] private float saturation = 10f;

    [Tooltip("Intensidad del resplandor solar (Bloom)")]
    [Range(0f, 1f)]
    [SerializeField] private float bloomIntensity = 0.25f;

    // Referencias cacheadas
    private Light directionalSunLight;
    private Volume globalDaylightVolume;

    private static bool sceneHookInstalled = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void AutoInitialize()
    {
        EnsureDaylightSetup();

        // AfterSceneLoad solo se ejecuta una vez: aplicar también al recargar la escena
        if (!sceneHookInstalled)
        {
            sceneHookInstalled = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (escena, modo) => EnsureDaylightSetup();
        }
    }

    public static DaylightManager EnsureDaylightSetup()
    {
        DaylightManager mgr = Object.FindAnyObjectByType<DaylightManager>();
        if (mgr == null)
        {
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            GameObject host = cam != null ? cam.gameObject : new GameObject("DaylightManager");
            mgr = host.AddComponent<DaylightManager>();
        }

        mgr.ApplyDaylightLighting();
        return mgr;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this && Application.isPlaying)
        {
            Destroy(this);
            return;
        }

        ApplyDaylightLighting();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Unity no permite crear objetos dentro de OnValidate (avisos y objetos duplicados): se aplaza al siguiente tick
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) ApplyDaylightLighting();
        };
    }
#endif

    /// <summary>
    /// Configura y aplica toda la iluminación de día en el sol, ambiente, cámara y volumen URP.
    /// </summary>
    [ContextMenu("Aplicar Iluminación de Día")]
    public void ApplyDaylightLighting()
    {
        SetupSun();
        SetupAmbientAndSky();
        SetupCameraPostProcessing();
        SetupGlobalVolume();
    }

    // =========================================================================
    // CICLO DIURNO EN BUCLE (solo en Play Mode)
    // =========================================================================

    private float tiempoCiclo = 0f;

    private void Update()
    {
        if (!Application.isPlaying || !activarCicloDiurno || directionalSunLight == null) return;

        tiempoCiclo += Time.deltaTime;
        AplicarPosicionSolar(CalcularProgresoDiurno());
    }

    /// <summary>
    /// Progreso 0..1..0 del día con una curva coseno: va del amanecer al atardecer y vuelve,
    /// con velocidad cero en los extremos. No hay saltos bruscos (como reiniciar de golpe al amanecer)
    /// ni asignaciones de memoria por frame.
    /// </summary>
    private float CalcularProgresoDiurno()
    {
        float duracion = Mathf.Max(1f, duracionAmanecerAtardecer);
        float faseInicial = Mathf.Acos(Mathf.Clamp(1f - 2f * progresoInicial, -1f, 1f)); // 0..PI
        float fase = faseInicial + (tiempoCiclo / duracion) * Mathf.PI;
        return 0.5f - 0.5f * Mathf.Cos(fase);
    }

    private void AplicarPosicionSolar(float progreso)
    {
        if (directionalSunLight == null) return;

        float altura = Mathf.Sin(progreso * Mathf.PI); // 0 en el horizonte, 1 a mediodía
        float elevacion = Mathf.Lerp(elevacionAmanecer, elevacionMediodia, altura);
        float azimut = Mathf.Lerp(azimutAmanecer, azimutAtardecer, progreso);

        directionalSunLight.transform.rotation = Quaternion.Euler(elevacion, azimut, 0f);
        directionalSunLight.intensity = sunIntensity * Mathf.Lerp(intensidadRelativaHorizonte, 1f, altura);
        directionalSunLight.colorTemperature = Mathf.Lerp(temperaturaHorizonte, sunColorTemperature, altura);
    }

    private void SetupSun()
    {
        // 1. Buscar o asignar el Sol
        if (directionalSunLight == null)
        {
            directionalSunLight = RenderSettings.sun;
        }

        if (directionalSunLight == null)
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l != null && l.type == LightType.Directional)
                {
                    directionalSunLight = l;
                    break;
                }
            }
        }

        if (directionalSunLight == null)
        {
            GameObject sunGo = new GameObject("Directional Light (Sun)");
            directionalSunLight = sunGo.AddComponent<Light>();
        }

        // 2. Configuración de sol de día
        directionalSunLight.type = LightType.Directional;
        directionalSunLight.color = sunColor;
        directionalSunLight.useColorTemperature = true;
        directionalSunLight.colorTemperature = sunColorTemperature;
        directionalSunLight.intensity = sunIntensity;
        directionalSunLight.shadows = LightShadows.Soft;
        directionalSunLight.shadowStrength = shadowStrength;
        if (Application.isPlaying && activarCicloDiurno)
        {
            AplicarPosicionSolar(CalcularProgresoDiurno());
        }
        else
        {
            directionalSunLight.transform.rotation = Quaternion.Euler(sunRotationEuler);
        }

        // URP Additional Light Data
        UniversalAdditionalLightData lightData = directionalSunLight.GetComponent<UniversalAdditionalLightData>();
        if (lightData == null)
        {
            lightData = directionalSunLight.gameObject.AddComponent<UniversalAdditionalLightData>();
        }
        if (lightData != null)
        {
            lightData.softShadowQuality = SoftShadowQuality.Medium;
        }

        RenderSettings.sun = directionalSunLight;
    }

    private void SetupAmbientAndSky()
    {
        // Modo Trilight (Gradiente de tres colores: cielo, horizonte y suelo)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSkyColor;
        RenderSettings.ambientEquatorColor = ambientEquatorColor;
        RenderSettings.ambientGroundColor = ambientGroundColor;
        RenderSettings.ambientIntensity = ambientIntensity;

        // Niebla atmosférica
        RenderSettings.fog = enableAtmosphericFog;
        if (enableAtmosphericFog)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }
    }

    private void SetupCameraPostProcessing()
    {
        List<Camera> cameras = new List<Camera>();
        if (Camera.main != null) cameras.Add(Camera.main);

        foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (c != null && !cameras.Contains(c))
            {
                cameras.Add(c);
            }
        }

        foreach (Camera cam in cameras)
        {
            if (cam == null) continue;

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
                camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                camData.antialiasingQuality = AntialiasingQuality.Medium;
            }
        }
    }

    private void SetupGlobalVolume()
    {
        if (globalDaylightVolume == null)
        {
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v != null && v.isGlobal)
                {
                    globalDaylightVolume = v;
                    break;
                }
            }
        }

        if (globalDaylightVolume == null)
        {
            GameObject volGo = new GameObject("Global Daylight Volume");
            globalDaylightVolume = volGo.AddComponent<Volume>();
            globalDaylightVolume.isGlobal = true;
            globalDaylightVolume.priority = 100f;
        }

        // Solo el perfil compartido: leer '.profile' crea una copia nueva en memoria cada vez (fuga en el Editor)
        VolumeProfile profile = globalDaylightVolume.sharedProfile;

        if (profile == null)
        {
#if UNITY_EDITOR
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
#endif
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "DaylightVolumeProfile";
            }
            globalDaylightVolume.sharedProfile = profile;
        }

        // 1. Tonemapping (Neutral)
        if (!profile.TryGet(out Tonemapping tonemapping))
        {
            tonemapping = profile.Add<Tonemapping>(true);
        }
        tonemapping.active = true;
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.Neutral;

        // 2. Color Adjustments (Exposición, Contraste, Saturación de día)
        if (!profile.TryGet(out ColorAdjustments colorAdj))
        {
            colorAdj = profile.Add<ColorAdjustments>(true);
        }
        colorAdj.active = true;
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value = postExposure;
        colorAdj.contrast.overrideState = true;
        colorAdj.contrast.value = contrast;
        colorAdj.saturation.overrideState = true;
        colorAdj.saturation.value = saturation;

        // 3. White Balance (Calidez solar)
        if (!profile.TryGet(out WhiteBalance whiteBalance))
        {
            whiteBalance = profile.Add<WhiteBalance>(true);
        }
        whiteBalance.active = true;
        whiteBalance.temperature.overrideState = true;
        whiteBalance.temperature.value = 5f;

        // 4. Bloom (Resplandor solar sutil)
        if (!profile.TryGet(out Bloom bloom))
        {
            bloom = profile.Add<Bloom>(true);
        }
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 1.0f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = bloomIntensity;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Iluminación/Configurar Iluminación de Día (Daylight)", false, 30)]
    public static void MenuSetupDaylight()
    {
        DaylightManager mgr = EnsureDaylightSetup();
        if (mgr != null)
        {
            mgr.ApplyDaylightLighting();
            EditorUtility.SetDirty(mgr.gameObject);
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            EditorUtility.DisplayDialog("Iluminación de Día", 
                "¡Iluminación de día configurada con éxito!\n\n" +
                "• Sol direccional calibrado a 6500K con sombras suaves.\n" +
                "• Luz ambiental diurna Trilight aplicada.\n" +
                "• Post-procesamiento URP habilitado en la cámara con compensación de exposición diurna.\n" +
                "• Niebla atmosférica sutil para profundidad visual.", "Aceptar");
        }
    }
#endif
}

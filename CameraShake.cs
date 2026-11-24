using System.Collections;
using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Configuración del Ciclo")]
    [Tooltip("Activa o desactiva la vibración periódica automática")]
    [SerializeField] private bool autoLoop = true;

    [Tooltip("Espera obligatoriamente a que se haya seleccionado un sistema de movimiento antes de iniciar el temblor")]
    [SerializeField] private bool esperarSeleccionDeMovimiento = true;

    [Tooltip("Tiempo de espera en segundos entre cada vibración (ej. 5 segundos)")]
    [SerializeField] private float intervalBetweenShakes = 5f;

    [Tooltip("Duración en segundos de la vibración (ej. 10 segundos)")]
    [SerializeField] private float shakeDuration = 10f;

    [Header("Intensidad y Movimiento")]
    [Tooltip("Amplitud o fuerza máxima del movimiento de la cámara (se puede ajustar en Play Mode)")]
    [Range(0.01f, 2f)]
    [SerializeField] private float intensity = 0.3f;

    [Tooltip("Rapidez o frecuencia con la que vibra la cámara")]
    [SerializeField] private float frequency = 25f;

    [Tooltip("Si está activo, utiliza Perlin Noise para una vibración más fluida y realista. Si está inactivo, usa vibración aleatoria.")]
    [SerializeField] private bool usePerlinNoise = true;

    [Tooltip("Incluir movimiento en el eje Z (profundidad)")]
    [SerializeField] private bool shakeZ = false;

    [Tooltip("Rotación máxima (grados) de la sacudida. CameraMovement la suma a la mirada de forma aditiva")]
    [Range(0f, 5f)]
    [SerializeField] private float rotationIntensity = 0.8f;

    [Tooltip("Segundos de rampa de entrada y salida: el temblor empieza y termina suave y la cámara vuelve exactamente a su sitio")]
    [Range(0.05f, 3f)]
    [SerializeField] private float rampaSegundos = 0.6f;

    [Tooltip("Si NO hay CameraMovement en la escena, aplicar la sacudida directamente al transform de este objeto")]
    [SerializeField] private bool aplicarAlTransformSiNoHayCameraMovement = true;

    [Header("Efecto de Partículas de Polvo")]
    [Tooltip("Activa la emisión de partículas de polvo mientras la cámara tiembla")]
    [SerializeField] private bool enableDust = true;

    [Tooltip("Sistema de partículas asignado. Si se deja vacío, se generará uno optimizado automáticamente como hijo de la cámara")]
    [SerializeField] private ParticleSystem dustParticles;

    [Tooltip("Cantidad de partículas emitidas por segundo durante el temblor")]
    [Range(10f, 200f)]
    [SerializeField] private float dustEmissionRate = 60f;

    [Tooltip("Color y transparencia de las motas de polvo")]
    [SerializeField] private Color dustColor = new Color(0.88f, 0.85f, 0.78f, 0.5f);

    [Tooltip("Tamaño de las partículas de polvo")]
    [Range(0.02f, 0.4f)]
    [SerializeField] private float dustSize = 0.08f;

    [Header("Audio del Terremoto")]
    [Tooltip("Clip de sonido que se reproduce mientras tiembla la tierra (ej. Assets/audios/Sonido de terremoto.mp3)")]
    [SerializeField] private AudioClip earthquakeAudioClip;

    [Tooltip("Volumen del sonido del terremoto")]
    [Range(0f, 1f)]
    [SerializeField] private float earthquakeVolume = 1.0f;

    [Tooltip("Reproduce el audio en bucle continuo mientras dura la vibración")]
    [SerializeField] private bool loopAudioDuringShake = true;

    [Tooltip("Transición suave de volumen (fade in / fade out) al iniciar y terminar el temblor")]
    [SerializeField] private bool fadeAudioTransitions = true;

    // Estado del sismo. IMPORTANTE: este script ya NO mueve el transform de la cámara.
    // Solo calcula el desplazamiento/rotación y CameraMovement los suma en LateUpdate, después de resolver
    // el movimiento. Así ninguno sobrescribe al otro y la cámara regresa exactamente a su sitio.
    private Vector3 currentShakeOffset = Vector3.zero;
    private Vector3 currentShakeRotation = Vector3.zero;
    private bool isShaking = false;
    private bool isPaused = false;
    private float shakeElapsed = 0f;
    private float shakeTotalDuration = 0f;
    private float loopTimer = 0f;
    private float seedX, seedY, seedZ, seedRotX, seedRotY, seedRotZ;
    private Vector3 fallbackAppliedOffset = Vector3.zero;
    private AudioSource earthquakeAudioSource;
    private Coroutine audioFadeCoroutine;

    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Max(0f, value);
    }

    /// <summary>Desplazamiento actual de la sacudida (espacio local horizontal de la cámara).</summary>
    public Vector3 CurrentShakeOffset => currentShakeOffset;

    /// <summary>Rotación actual de la sacudida en grados (pitch, yaw, roll).</summary>
    public Vector3 CurrentShakeRotation => currentShakeRotation;

    public bool IsShaking => isShaking;

    public static event System.Action OnShakeStarted;
    public static event System.Action OnShakeEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        SetupDustParticles();
        SetupAudio();
        loopTimer = 0f;
    }

    /// <summary>
    /// Configura o genera automáticamente el sistema de partículas de polvo si no existe.
    /// </summary>
    private void SetupDustParticles()
    {
        if (!enableDust) return;

        if (dustParticles == null)
        {
            // Verificar si ya existe el hijo creado previamente
            Transform existingChild = transform.Find("DustParticlesEffect");
            if (existingChild != null)
            {
                dustParticles = existingChild.GetComponent<ParticleSystem>();
            }

            if (dustParticles == null)
            {
                // Crear GameObject hijo dedicado para las partículas de polvo
                GameObject dustObj = new GameObject("DustParticlesEffect");
                dustObj.transform.SetParent(transform, false);
                dustObj.transform.localPosition = new Vector3(0f, 0f, 1f);

                dustParticles = dustObj.AddComponent<ParticleSystem>();
                ConfigureDustParticleSystem(dustParticles);
            }
        }

        // Asegurar que inicie detenido
        var emission = dustParticles.emission;
        emission.enabled = false;
        dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ConfigureDustParticleSystem(ParticleSystem ps)
    {
        // 1. Módulo Principal
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // Flotan en el mundo real, no pegadas a la lente
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(dustSize * 0.6f, dustSize * 1.4f);
        main.startColor = dustColor;
        main.gravityModifier = 0.035f; // Suave caída por gravedad
        main.maxParticles = 600;

        // 2. Módulo de Emisión
        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = dustEmissionRate;

        // 3. Módulo de Forma (Caja volumétrica envolvente alrededor de la cámara)
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(8f, 5f, 8f);
        shape.position = new Vector3(0f, 1f, 0f);

        // 4. Color y Desvanecimiento sobre el tiempo de vida
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(dustColor, 0f), new GradientColorKey(dustColor, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(dustColor.a, 0.2f),
                new GradientAlphaKey(dustColor.a, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = grad;

        // 5. Tamaño sobre el tiempo de vida (Aparición gradual)
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.2f);
        sizeCurve.AddKey(0.3f, 1f);
        sizeCurve.AddKey(0.8f, 0.9f);
        sizeCurve.AddKey(1f, 0.1f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // 6. Turbulencia / Ruido orgánico
        var noise = ps.noise;
        noise.enabled = true;
        noise.frequency = 0.35f;
        noise.strength = 0.35f;
        noise.scrollSpeed = 0.2f;
        noise.damping = true;

        // 7. Material procedural con textura suave circular
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader == null) particleShader = Shader.Find("Sprites/Default");

        Material dustMat = new Material(particleShader);
        dustMat.name = "Procedural_Dust_Material";
        dustMat.mainTexture = GenerateSoftDustTexture();

        if (dustMat.HasProperty("_Surface")) dustMat.SetFloat("_Surface", 1f); // Transparent
        if (dustMat.HasProperty("_Blend")) dustMat.SetFloat("_Blend", 0f); // Alpha blend
        if (dustMat.HasProperty("_Color")) dustMat.SetColor("_Color", Color.white);
        if (dustMat.HasProperty("_BaseColor")) dustMat.SetColor("_BaseColor", Color.white);

        renderer.material = dustMat;
    }

    private Texture2D GenerateSoftDustTexture()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Procedural_Dust_Mote";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxRadius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float normalized = Mathf.Clamp01(dist / maxRadius);
                float alpha = Mathf.SmoothStep(1f, 0f, normalized);
                alpha = alpha * alpha; // Bordes suaves difuminados

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        return tex;
    }

    private void StartDustEmission()
    {
        if (!enableDust || dustParticles == null) return;

        var emission = dustParticles.emission;
        emission.rateOverTime = dustEmissionRate;
        emission.enabled = true;

        if (!dustParticles.isPlaying)
        {
            dustParticles.Play();
        }
    }

    private void StopDustEmission()
    {
        if (dustParticles == null) return;

        var emission = dustParticles.emission;
        emission.enabled = false;
        // Se deja que las partículas ya emitidas floten y se disipen suavemente
    }

    private void SetupAudio()
    {
        // AudioSource PROPIO: compartirlo con PlayerHealth/teletransporte hacía que el Stop() del sismo cortara sus sonidos
        if (earthquakeAudioSource == null)
        {
            earthquakeAudioSource = gameObject.AddComponent<AudioSource>();
        }
        earthquakeAudioSource.playOnAwake = false;
        earthquakeAudioSource.spatialBlend = 0f; // 2D estéreo inmersivo
        earthquakeAudioSource.loop = loopAudioDuringShake;
        earthquakeAudioSource.volume = earthquakeVolume;

        LoadEarthquakeAudioClip();
    }

    private void LoadEarthquakeAudioClip()
    {
        if (earthquakeAudioClip != null) return;

#if UNITY_EDITOR
        earthquakeAudioClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/audios/Sonido de terremoto.mp3");
#endif
    }

    private void StartEarthquakeAudio()
    {
        if (earthquakeAudioSource == null) SetupAudio();
        if (earthquakeAudioClip == null) LoadEarthquakeAudioClip();
        if (earthquakeAudioSource == null || earthquakeAudioClip == null) return;

        if (audioFadeCoroutine != null)
        {
            StopCoroutine(audioFadeCoroutine);
            audioFadeCoroutine = null;
        }

        earthquakeAudioSource.clip = earthquakeAudioClip;
        earthquakeAudioSource.loop = loopAudioDuringShake;

        if (fadeAudioTransitions)
        {
            audioFadeCoroutine = StartCoroutine(FadeAudioRoutine(0f, earthquakeVolume, 0.45f, true));
        }
        else
        {
            earthquakeAudioSource.volume = earthquakeVolume;
            if (!earthquakeAudioSource.isPlaying) earthquakeAudioSource.Play();
        }
    }

    private void StopEarthquakeAudio(bool immediate = false)
    {
        if (earthquakeAudioSource == null || !earthquakeAudioSource.isPlaying) return;

        if (audioFadeCoroutine != null)
        {
            StopCoroutine(audioFadeCoroutine);
            audioFadeCoroutine = null;
        }

        if (immediate || !fadeAudioTransitions)
        {
            earthquakeAudioSource.Stop();
        }
        else
        {
            audioFadeCoroutine = StartCoroutine(FadeAudioRoutine(earthquakeAudioSource.volume, 0f, 0.65f, false));
        }
    }

    private IEnumerator FadeAudioRoutine(float fromVol, float toVol, float duration, bool playAtStart)
    {
        if (playAtStart && !earthquakeAudioSource.isPlaying)
        {
            earthquakeAudioSource.volume = fromVol;
            earthquakeAudioSource.Play();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            earthquakeAudioSource.volume = Mathf.Lerp(fromVol, toVol, t);
            yield return null;
        }

        earthquakeAudioSource.volume = toVol;

        if (!playAtStart && toVol <= 0.001f)
        {
            earthquakeAudioSource.Stop();
        }

        audioFadeCoroutine = null;
    }

    // =========================================================================
    // CICLO DEL SISMO: máquina de estados en Update (sin corrutinas anidadas que
    // se solapaban y dejaban isShaking a medias).
    // =========================================================================

    /// <summary>El sismo se congela con el menú abierto, en Game Over o hasta elegir sistema de movimiento.</summary>
    private bool DebeEsperar()
    {
        if (MovementSystemSelector.IsGameplayBlocked) return true;
        if (esperarSeleccionDeMovimiento && MovementSystemSelector.Instance != null && !MovementSystemSelector.HasSelectedMovementSystem) return true;
        return false;
    }

    private void Update()
    {
        bool bloqueado = DebeEsperar();
        SetPaused(bloqueado);

        if (bloqueado)
        {
            ResetOffsets();
            return;
        }

        if (isShaking)
        {
            shakeElapsed += Time.deltaTime;
            if (shakeElapsed >= shakeTotalDuration)
            {
                EndShake(false, true);
            }
            else
            {
                ComputeShake();
            }
        }
        else if (autoLoop)
        {
            // Espera entre sismos (ej. 5 segundos)
            loopTimer += Time.deltaTime;
            // El modo de juego ajusta la frecuencia y la duración (Extremo: más seguidos y largos)
            if (loopTimer >= intervalBetweenShakes * ModoDeJuego.MultiplicadorIntervaloSismo)
            {
                loopTimer = 0f;
                BeginShake(shakeDuration * ModoDeJuego.MultiplicadorDuracionSismo);
            }
        }
    }

    private void LateUpdate()
    {
        // Solo como respaldo: si no existe CameraMovement que consuma el offset, aplicarlo aquí (aditivo y reversible)
        bool hayConsumidor = CameraMovement.Instance != null && CameraMovement.Instance.isActiveAndEnabled;
        Vector3 objetivo = (!hayConsumidor && aplicarAlTransformSiNoHayCameraMovement) ? currentShakeOffset : Vector3.zero;
        if (objetivo != fallbackAppliedOffset)
        {
            transform.localPosition += objetivo - fallbackAppliedOffset;
            fallbackAppliedOffset = objetivo;
        }
    }

    private void ComputeShake()
    {
        // Envolvente de entrada/salida: sin saltos al empezar y vuelta exacta a cero al terminar
        float rampa = Mathf.Max(0.01f, rampaSegundos);
        float env = Mathf.Clamp01(shakeElapsed / rampa) * Mathf.Clamp01((shakeTotalDuration - shakeElapsed) / rampa);
        env = env * env * (3f - 2f * env);

        float x, y, z, rx, ry, rz;
        if (usePerlinNoise)
        {
            // Ruido Perlin: movimiento orgánico continuo
            float t = Time.time * frequency;
            x = Mathf.PerlinNoise(t + seedX, 0f) * 2f - 1f;
            y = Mathf.PerlinNoise(0f, t + seedY) * 2f - 1f;
            z = shakeZ ? Mathf.PerlinNoise(t + seedZ, t + seedZ) * 2f - 1f : 0f;

            float tr = Time.time * frequency * 0.5f;
            rx = Mathf.PerlinNoise(tr + seedRotX, 1.3f) * 2f - 1f;
            ry = Mathf.PerlinNoise(2.7f, tr + seedRotY) * 2f - 1f;
            rz = Mathf.PerlinNoise(tr + seedRotZ, tr + 4.1f) * 2f - 1f;
        }
        else
        {
            // Ruido aleatorio estándar
            x = Random.Range(-1f, 1f);
            y = Random.Range(-1f, 1f);
            z = shakeZ ? Random.Range(-1f, 1f) : 0f;
            rx = Random.Range(-1f, 1f);
            ry = Random.Range(-1f, 1f);
            rz = Random.Range(-1f, 1f);
        }

        float fuerzaModo = ModoDeJuego.MultiplicadorIntensidadSismo;
        currentShakeOffset = new Vector3(x, y, z) * (intensity * fuerzaModo * env);
        currentShakeRotation = new Vector3(rx, ry * 0.5f, rz) * (rotationIntensity * fuerzaModo * env);
    }

    private void BeginShake(float duration)
    {
        isShaking = true;
        shakeElapsed = 0f;
        shakeTotalDuration = Mathf.Max(0.05f, duration);

        // Semillas para que cada eje tenga una trayectoria independiente
        seedX = Random.value * 100f;
        seedY = Random.value * 100f;
        seedZ = Random.value * 100f;
        seedRotX = Random.value * 100f;
        seedRotY = Random.value * 100f;
        seedRotZ = Random.value * 100f;

        StartDustEmission();
        StartEarthquakeAudio();
        OnShakeStarted?.Invoke();
    }

    private void EndShake(bool audioInmediato, bool notificar)
    {
        bool estabaTemblando = isShaking;
        isShaking = false;
        shakeElapsed = 0f;
        ResetOffsets();
        StopDustEmission();
        StopEarthquakeAudio(audioInmediato);

        if (estabaTemblando && notificar)
        {
            OnShakeEnded?.Invoke();
        }
    }

    private void ResetOffsets()
    {
        currentShakeOffset = Vector3.zero;
        currentShakeRotation = Vector3.zero;
    }

    private void SetPaused(bool pausar)
    {
        if (pausar == isPaused) return;
        isPaused = pausar;
        if (!isShaking) return;

        if (pausar)
        {
            if (earthquakeAudioSource != null && earthquakeAudioSource.isPlaying) earthquakeAudioSource.Pause();
            if (dustParticles != null)
            {
                var em = dustParticles.emission;
                em.enabled = false;
            }
        }
        else
        {
            if (earthquakeAudioSource != null) earthquakeAudioSource.UnPause();
            if (enableDust && dustParticles != null)
            {
                var em = dustParticles.emission;
                em.enabled = true;
            }
        }
    }

    /// <summary>Inicia el bucle periódico de vibración.</summary>
    public void StartLoop()
    {
        autoLoop = true;
        loopTimer = 0f;
    }

    /// <summary>Detiene el bucle periódico y deja la cámara en su posición exacta.</summary>
    public void StopLoop()
    {
        autoLoop = false;
        loopTimer = 0f;
        EndShake(true, true);
    }

    /// <summary>Ejecuta una vibración puntual. Si ya está temblando, la alarga en lugar de solaparse.</summary>
    public void TriggerShake(float duration)
    {
        if (DebeEsperar()) return;

        if (isShaking)
        {
            float restante = shakeTotalDuration - shakeElapsed;
            if (duration > restante) shakeTotalDuration = shakeElapsed + duration;
            return;
        }

        loopTimer = 0f;
        BeginShake(duration);
    }

    private void OnDisable()
    {
        // Sin notificar: durante la descarga de escena otros objetos ya pueden estar destruidos
        EndShake(true, false);
        if (fallbackAppliedOffset != Vector3.zero)
        {
            transform.localPosition -= fallbackAppliedOffset;
            fallbackAppliedOffset = Vector3.zero;
        }
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// Controla el efecto de explosión e incendio en la máquina expendedora (vending_machine)
/// durante el temblor de tierra (CameraShake).
/// El fuego abraza el cuerpo de la máquina de manera cercana y con gran escala/volumen de llamas.
/// Incluye:
/// - Detección automática del sismo.
/// - Pequeña explosión (destello, chispas eléctricas, humo y sacudida física).
/// - Sistema de llamas de gran tamaño, humo negro denso, brasas ardientes y luz parpadeante.
/// - Compatibilidad para extinguir el fuego con el Extintor del inventario.
/// </summary>
public class VendingMachineHazard : MonoBehaviour
{
    [Header("Activación Durante el Temblor")]
    [Tooltip("Segundos de espera tras empezar el temblor antes de empezar a evaluar la probabilidad de explosión")]
    [Range(0.2f, 5f)]
    [SerializeField] private float delayBeforeExplosion = 1.5f;

    [Tooltip("La máquina solo explota una única vez por sesión de juego")]
    [SerializeField] private bool onlyExplodeOnce = true;

    [Tooltip("Probabilidad (0-1) de explotar en cada evaluación mientras dura el temblor")]
    [Range(0f, 1f)]
    [SerializeField] private float probabilidadExplosion = 0.25f;

    [Tooltip("Segundos entre cada evaluación de la probabilidad durante el temblor")]
    [Range(0.2f, 5f)]
    [SerializeField] private float intervaloEvaluacion = 1f;

    [Header("Escala e Intensidad del Fuego (Más grande y cercano)")]
    [Tooltip("Multiplicador del tamaño de las llamas (aumenta la escala general del fuego)")]
    [Range(0.8f, 5f)]
    [SerializeField] private float fireSizeMultiplier = 2.4f;

    [Tooltip("Densidad y cantidad de llamas emitidas por segundo")]
    [Range(0.8f, 5f)]
    [SerializeField] private float fireDensityMultiplier = 2.5f;

    [Tooltip("Ajuste fino de posición del fuego respecto a la máquina (X: lateral, Y: vertical, Z: frontal)")]
    [SerializeField] private Vector3 fireOffsetAdjustment = new Vector3(0f, 0f, 0.05f);

    [Header("Configuración de la Explosión")]
    [Tooltip("Fuerza del destello de luz durante la explosión")]
    [SerializeField] private float explosionLightIntensity = 7f;

    [Tooltip("Sacudir físicamente la máquina expendedora al explotar")]
    [SerializeField] private bool shakeMachineOnExplosion = true;

    [Tooltip("Reproducir sonido de explosión y chispazo")]
    [SerializeField] private bool playAudio = true;

    [Header("Daño de Explosión al Jugador (Editable)")]
    [Tooltip("Daño máximo que recibe el jugador en el epicentro de la explosión")]
    [Range(0f, 60f)]
    [SerializeField] private float explosionMaxDamage = 16f;

    [Tooltip("Daño mínimo que recibe el jugador en el borde de la onda de la explosión")]
    [Range(0f, 30f)]
    [SerializeField] private float explosionMinDamage = 4f;

    [Tooltip("Radio en metros del alcance de la explosión")]
    [Range(1f, 10f)]
    [SerializeField] private float explosionDamageRadius = 4.2f;

    public float ExplosionMaxDamage { get => explosionMaxDamage; set => explosionMaxDamage = Mathf.Max(0f, value); }
    public float ExplosionMinDamage { get => explosionMinDamage; set => explosionMinDamage = Mathf.Max(0f, value); }
    public float ExplosionDamageRadius { get => explosionDamageRadius; set => explosionDamageRadius = Mathf.Max(0.1f, value); }

    [Header("Configuración de Iluminación del Fuego")]
    [Tooltip("Intensidad de la luz parpadeante del fuego")]
    [SerializeField] private float fireLightIntensity = 4.2f;

    [Tooltip("Rango de alcance de la luz del fuego en metros")]
    [SerializeField] private float fireLightRange = 11f;

    [Tooltip("Color de la luz del fuego")]
    [SerializeField] private Color fireLightColor = new Color(1f, 0.45f, 0.1f, 1f);

    [Tooltip("Oscurecer el material de la máquina con efecto de hollín tras la explosión")]
    [SerializeField] private bool applyCharredMaterial = true;

    [Header("Interacción con Extintor")]
    [Tooltip("Permite apagar el fuego si el jugador tiene el Extintor en el inventario")]
    [SerializeField] private bool allowExtinguishWithExtintor = true;

    [Tooltip("Distancia máxima para apagar el fuego (medida a la superficie de la máquina)")]
    [SerializeField] private float extinguishDistance = 6.5f;

    [Header("Mecánica de Barra de Progreso de Apagado")]
    [Tooltip("Progreso actual de extinción del fuego (0 = 100% en llamas, 1 = completamente apagado)")]
    [Range(0f, 1f)]
    [SerializeField] private float extinguishProgress = 0f;

    [Tooltip("Segundos de rociado continuo necesarios para apagar el fuego por completo")]
    [SerializeField] private float timeRequiredToExtinguish = 2.8f;

    [Tooltip("El fuego se reaviva lentamente si el jugador deja de rociar")]
    [SerializeField] private bool allowFireRekindle = true;

    [Tooltip("Tiempo de espera sin rociar antes de que el fuego empiece a reavivarse")]
    [SerializeField] private float rekindleDelay = 1.4f;

    [Tooltip("Velocidad a la que se reaviva el fuego por segundo")]
    [SerializeField] private float rekindleSpeed = 0.15f;

    private float lastSprayTimestamp = -10f;

    public float ExtinguishProgress => extinguishProgress;

    public static VendingMachineHazard Instance { get; private set; }

    // Estados internos
    private bool hasExploded = false;
    private bool isOnFire = false;
    private bool isExtinguished = false;
    private Coroutine evaluacionCoroutine;
    private Collider colliderMaquina;
    private float factorLuzFuego = 1f;
    private bool enDestello = false;
    private bool jugadorEnRangoCache = false;
    private bool jugadorApuntandoCache = false;
    private readonly RaycastHit[] bufferRayos = new RaycastHit[16];

    /// <summary>Se dispara cuando la máquina explota y queda en llamas.</summary>
    public event System.Action OnExploded;

    /// <summary>Se dispara cuando el fuego queda completamente apagado.</summary>
    public event System.Action OnFireExtinguished;

    // Sistemas de partículas y componentes
    private ParticleSystem explosionParticles;
    private ParticleSystem mainFlamesParticles;
    private ParticleSystem coreFlamesParticles;
    private ParticleSystem embersParticles;
    private ParticleSystem smokeParticles;
    private ParticleSystem extinguishParticles;
    private Light firePointLight;
    private AudioSource audioSource;

    private Quaternion originalRotation;
    private Vector3 originalPosition;
    private Renderer[] machineRenderers;
    private Camera cachedPlayerCamera;

    // Dispositivos VR
    private readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> vrRightDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    private readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> vrLeftDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    private bool vrActionLastFrame = false;

    public bool HasExploded => hasExploded;
    public bool IsOnFire => isOnFire;
    public bool IsExtinguished => isExtinguished;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        machineRenderers = GetComponentsInChildren<Renderer>();

        // Asegurar AudioSource
        // AudioSource PROPIO para el fuego (antes lo compartía con Compra y el bucle de fuego pisaba sus sonidos)
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // Audio 3D
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 25f;

        // Asegurar Colisionador para interacción
        if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            GetMachineBounds(out Vector3 localCenter, out Vector3 localSize);
            box.center = localCenter;
            box.size = localSize;
        }
        colliderMaquina = GetComponentInChildren<Collider>();

        // Asegurar componente Compra para golpear y soltar lata_Lowpoly cuando no esté en llamas
        if (GetComponent<Compra>() == null)
        {
            gameObject.AddComponent<Compra>();
        }

        BuildVisualEffects();
        GetPlayerCamera();
        ExtinguisherSprayController.EnsureInstance();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        CameraShake.OnShakeStarted += HandleShakeStarted;
        CameraShake.OnShakeEnded += HandleShakeEnded;
    }

    private void OnDisable()
    {
        CameraShake.OnShakeStarted -= HandleShakeStarted;
        CameraShake.OnShakeEnded -= HandleShakeEnded;
        DetenerEvaluacion();
    }

    private void Update()
    {
        // Respaldo: si el temblor ya estaba en marcha cuando se activó este objeto, empezar a evaluar
        if (evaluacionCoroutine == null && PuedeExplotar() && CameraShake.Instance != null && CameraShake.Instance.IsShaking)
        {
            evaluacionCoroutine = StartCoroutine(EvaluarExplosionDuranteSismo());
        }

        // Parpadeo de la luz del fuego (respeta la atenuación por el extintor y el destello de la explosión)
        if (isOnFire && firePointLight != null && !enDestello)
        {
            float noise = Mathf.PerlinNoise(Time.time * 16f, 0.2f);
            firePointLight.intensity = fireLightIntensity * factorLuzFuego * Mathf.Lerp(0.65f, 1.35f, noise);
        }

        // Si el fuego está ardiendo y no se está rociando, se reaviva lentamente
        if (isOnFire && !isExtinguished && allowFireRekindle && extinguishProgress > 0f)
        {
            if (Time.time - lastSprayTimestamp > rekindleDelay)
            {
                extinguishProgress = Mathf.Max(0f, extinguishProgress - rekindleSpeed * Time.deltaTime);
                UpdateFireVisualIntensity(1f - extinguishProgress);
            }
        }

        // Rango y puntería se calculan UNA vez por frame (OnGUI los repetía varias veces por frame)
        bool bloqueado = MovementSystemSelector.IsGameplayBlocked;
        jugadorEnRangoCache = !bloqueado && isOnFire && IsPlayerInRange();
        jugadorApuntandoCache = jugadorEnRangoCache && IsPlayerAimingAtMachine();

        if (!bloqueado && isOnFire && allowExtinguishWithExtintor)
        {
            CheckExtinguisherInteraction();
        }
    }

    private bool PuedeExplotar()
    {
        if (isOnFire) return false;
        if (hasExploded && onlyExplodeOnce) return false;
        return true;
    }

    private void HandleShakeStarted()
    {
        if (!isActiveAndEnabled || !PuedeExplotar()) return;
        DetenerEvaluacion();
        evaluacionCoroutine = StartCoroutine(EvaluarExplosionDuranteSismo());
    }

    private void HandleShakeEnded()
    {
        DetenerEvaluacion();
    }

    private void DetenerEvaluacion()
    {
        if (evaluacionCoroutine != null)
        {
            StopCoroutine(evaluacionCoroutine);
            evaluacionCoroutine = null;
        }
    }

    /// <summary>
    /// Mientras dura el temblor se "tira el dado" cada 'intervaloEvaluacion' segundos.
    /// Si sale, la máquina explota y queda en llamas. Si el temblor termina antes, no pasa nada.
    /// </summary>
    private IEnumerator EvaluarExplosionDuranteSismo()
    {
        if (delayBeforeExplosion > 0.05f)
        {
            yield return new WaitForSeconds(delayBeforeExplosion);
        }

        while (PuedeExplotar() && CameraShake.Instance != null && CameraShake.Instance.IsShaking)
        {
            // Con el menú abierto o en Game Over el sismo está congelado: no se evalúa
            float probabilidad = Mathf.Clamp01(probabilidadExplosion * ModoDeJuego.MultiplicadorProbabilidadExplosion);
            if (!MovementSystemSelector.IsGameplayBlocked && Random.value < probabilidad)
            {
                evaluacionCoroutine = null;
                ExecuteExplosion();
                yield break;
            }
            yield return new WaitForSeconds(Mathf.Max(0.2f, intervaloEvaluacion));
        }

        evaluacionCoroutine = null;
    }

    [ContextMenu("Probar Explosión e Incendio")]
    public void ExecuteExplosion()
    {
        hasExploded = true;
        isOnFire = true;
        isExtinguished = false;
        extinguishProgress = 0f;

        // 1. Efecto de partículas de explosión
        if (explosionParticles != null)
        {
            explosionParticles.Play();
        }

        // 2. Destello de luz intenso y breve
        StartCoroutine(ExplosionLightFlashRoutine());

        // 3. Sonido de explosión y chispazo
        if (playAudio && audioSource != null)
        {
            audioSource.PlayOneShot(GenerateExplosionAudio());
        }

        // 4. Sacudida física de la máquina
        if (shakeMachineOnExplosion)
        {
            StartCoroutine(ShakeMachineRoutine());
        }

        // 5. Aplicar efecto de hollín / quemado en la textura
        if (applyCharredMaterial)
        {
            ApplyCharredAppearance();
        }

        // 6. Iniciar llamas y humo continuo
        StartFire();

        // 7. Daño por onda expansiva al jugador si está cerca en el momento de la explosión
        Camera playerCam = GetPlayerCamera();
        if (playerCam != null && PlayerHealth.Instance != null && explosionDamageRadius > 0.01f)
        {
            float distToExplosion = Vector3.Distance(playerCam.transform.position, GetMachineWorldCenter());
            if (distToExplosion < explosionDamageRadius)
            {
                float blastDamage = Mathf.Lerp(explosionMaxDamage, explosionMinDamage, distToExplosion / explosionDamageRadius);
                PlayerHealth.Instance.TakeDamage(blastDamage, PlayerHealth.DamageType.Generic, "Explosión de máquina");
            }
        }

        OnExploded?.Invoke();

        Debug.Log("[VendingMachineHazard] ¡La máquina expendedora ha sufrido un cortocircuito explosivo y está en llamas!");
    }

    private void StartFire()
    {
        isOnFire = true;
        UpdateFireVisualIntensity(1f);

        if (mainFlamesParticles != null) mainFlamesParticles.Play();
        if (coreFlamesParticles != null) coreFlamesParticles.Play();
        if (embersParticles != null) embersParticles.Play();
        if (smokeParticles != null) smokeParticles.Play();

        if (firePointLight != null)
        {
            firePointLight.enabled = true;
            firePointLight.color = fireLightColor;
        }

        // Iniciar sonido de crepitar de fuego en bucle
        if (playAudio && audioSource != null)
        {
            audioSource.clip = GenerateFireCrackleAudio();
            audioSource.loop = true;
            audioSource.volume = 0.7f;
            audioSource.Play();
        }
    }

    public Camera GetPlayerCamera()
    {
        if (cachedPlayerCamera != null && cachedPlayerCamera.isActiveAndEnabled)
            return cachedPlayerCamera;

        if (Camera.main != null)
        {
            cachedPlayerCamera = Camera.main;
        }
        else
        {
            CameraMovement mov = Object.FindAnyObjectByType<CameraMovement>();
            if (mov != null) cachedPlayerCamera = mov.GetComponentInChildren<Camera>();

            if (cachedPlayerCamera == null)
            {
                PlayerInteraction pi = Object.FindAnyObjectByType<PlayerInteraction>();
                if (pi != null) cachedPlayerCamera = pi.GetComponentInChildren<Camera>();
            }

            if (cachedPlayerCamera == null)
            {
                cachedPlayerCamera = Object.FindAnyObjectByType<Camera>();
            }
        }

        if (cachedPlayerCamera != null && cachedPlayerCamera.CompareTag("Untagged"))
        {
            try { cachedPlayerCamera.tag = "MainCamera"; } catch { }
        }

        return cachedPlayerCamera;
    }

    public Vector3 GetMachineWorldCenter()
    {
        Collider col = colliderMaquina != null ? colliderMaquina : GetComponentInChildren<Collider>();
        if (col != null)
        {
            return col.bounds.center;
        }

        if (machineRenderers != null && machineRenderers.Length > 0)
        {
            Bounds b = machineRenderers[0].bounds;
            for (int i = 1; i < machineRenderers.Length; i++)
            {
                if (machineRenderers[i] != null && !machineRenderers[i].name.Contains("Effect"))
                {
                    b.Encapsulate(machineRenderers[i].bounds);
                }
            }
            return b.center;
        }

        return transform.position + Vector3.up * 2f;
    }

    public bool IsPlayerInRange()
    {
        Camera cam = GetPlayerCamera();
        if (cam == null) return false;

        Vector3 p = cam.transform.position;
        Collider col = colliderMaquina != null ? colliderMaquina : GetComponentInChildren<Collider>();
        float dist = col != null
            ? Vector3.Distance(p, CameraMovement.PuntoMasCercano(col, p))
            : Vector3.Distance(p, GetMachineWorldCenter());

        return dist <= extinguishDistance;
    }

    public bool IsPlayerAimingAtMachine()
    {
        Camera cam = GetPlayerCamera();
        if (cam == null) return false;

        Vector3 center = GetMachineWorldCenter();
        Vector3 dirToCenter = (center - cam.transform.position).normalized;
        float dot = Vector3.Dot(cam.transform.forward, dirToCenter);

        // 1. Raycast frontal ignorando los colliders del propio jugador
        Transform raizJugador = CameraMovement.Instance != null ? CameraMovement.Instance.transform : cam.transform;
        int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, bufferRayos, extinguishDistance + 5f, ~0, QueryTriggerInteraction.Ignore);
        float mejor = float.MaxValue;
        Transform golpeado = null;
        for (int i = 0; i < n; i++)
        {
            Collider c = bufferRayos[i].collider;
            if (c == null) continue;
            if (c.transform == raizJugador || c.transform.IsChildOf(raizJugador)) continue;
            if (bufferRayos[i].distance < mejor)
            {
                mejor = bufferRayos[i].distance;
                golpeado = c.transform;
            }
        }
        if (golpeado != null && (golpeado == transform || golpeado.IsChildOf(transform)))
        {
            return true;
        }

        // 2. Campo de visión amplio mirando hacia la máquina (dot > 0.35f, ~69 grados)
        return dot > 0.35f;
    }

    /// <summary>True solo si el extintor está en el INVENTARIO (antes contaba uno tirado en el suelo cerca).</summary>
    public bool PlayerHasExtinguisher()
    {
        return InventorySystem.Instance != null && InventorySystem.Instance.HasExtinguisher();
    }

    /// <summary>
    /// Llamado continuamente por ExtinguisherSprayController cada frame mientras el chorro de polvo impacta el fuego.
    /// Incrementa la barra de progreso y reduce la intensidad de las llamas en tiempo real.
    /// </summary>
    public void ApplyExtinguishSpray(float deltaTime)
    {
        if (!isOnFire || isExtinguished) return;

        lastSprayTimestamp = Time.time;
        extinguishProgress += deltaTime / Mathf.Max(0.5f, timeRequiredToExtinguish);
        extinguishProgress = Mathf.Clamp01(extinguishProgress);

        UpdateFireVisualIntensity(1f - extinguishProgress);

        if (extinguishProgress >= 1f)
        {
            ExtinguishFire();
        }
    }

    public void UpdateFireVisualIntensity(float factor)
    {
        factor = Mathf.Clamp01(factor);

        if (mainFlamesParticles != null)
        {
            var emission = mainFlamesParticles.emission;
            emission.rateOverTime = (65f * fireDensityMultiplier) * Mathf.Lerp(0.12f, 1f, factor);
        }

        if (coreFlamesParticles != null)
        {
            var emission = coreFlamesParticles.emission;
            emission.rateOverTime = (35f * fireDensityMultiplier) * Mathf.Lerp(0.05f, 1f, factor);
        }

        if (smokeParticles != null)
        {
            var emission = smokeParticles.emission;
            emission.rateOverTime = (25f * fireDensityMultiplier) * Mathf.Lerp(0.2f, 1f, factor);
        }

        factorLuzFuego = Mathf.Lerp(0.2f, 1f, factor);
        if (firePointLight != null && !enDestello)
        {
            firePointLight.intensity = fireLightIntensity * factorLuzFuego;
        }
    }

    [ContextMenu("Apagar Fuego")]
    public void ExtinguishFire()
    {
        if (!isOnFire) return;

        isOnFire = false;
        isExtinguished = true;
        extinguishProgress = 1f;

        // Detener todas las llamas, brasas y el humo negro
        if (mainFlamesParticles != null) mainFlamesParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (coreFlamesParticles != null) coreFlamesParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (embersParticles != null) embersParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (smokeParticles != null) smokeParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (firePointLight != null) firePointLight.enabled = false;

        // Reproducir partículas de humo blanco / polvo extintor
        if (extinguishParticles != null)
        {
            extinguishParticles.Play();
        }

        // Detener sonido de fuego y reproducir sonido de descarga del extintor
        if (audioSource != null)
        {
            if (audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }

            if (playAudio)
            {
                audioSource.PlayOneShot(GenerateExtinguisherSprayAudio(), 1.0f);
            }
        }

        // Notificación en pantalla si existe InventorySystem
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.ShowNotification("🧯 ¡Fuego extinguido con el extintor!", new Color(0.2f, 1f, 0.8f, 1f), 3.5f);
        }

        OnFireExtinguished?.Invoke();

        Debug.Log("[VendingMachineHazard] El incendio de la máquina expendedora ha sido extinguido con éxito.");
    }

    private bool CheckVRActionPressed()
    {
        vrRightDevices.Clear();
        vrLeftDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrRightDevices);
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrLeftDevices);

        bool pressed = false;
        if (vrRightDevices.Count > 0 && vrRightDevices[0].isValid)
        {
            if (vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btnA) && btnA) pressed = true;
            if (vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trig) && trig) pressed = true;
            if (vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip) pressed = true;
        }
        if (vrLeftDevices.Count > 0 && vrLeftDevices[0].isValid)
        {
            if (vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btnX) && btnX) pressed = true;
            if (vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigL) && trigL) pressed = true;
            if (vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripL) && gripL) pressed = true;
        }

        bool triggered = pressed && !vrActionLastFrame;
        vrActionLastFrame = pressed;
        return triggered;
    }

    private void CheckExtinguisherInteraction()
    {
        if (!isOnFire || !allowExtinguishWithExtintor) return;
        if (!jugadorEnRangoCache || !jugadorApuntandoCache) return;
        if (!PlayerHasExtinguisher()) return;

        bool actionPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            if (UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.E].wasPressedThisFrame ||
                UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.Enter].wasPressedThisFrame)
            {
                actionPressed = true;
            }
        }
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            actionPressed = true;
        }
        if (UnityEngine.InputSystem.Gamepad.current != null &&
            (UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame ||
             UnityEngine.InputSystem.Gamepad.current.buttonWest.wasPressedThisFrame))
        {
            actionPressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))
        {
            actionPressed = true;
        }
#endif

        if (!actionPressed && CheckVRActionPressed())
        {
            actionPressed = true;
        }

        if (actionPressed)
        {
            if (ExtinguisherSprayController.Instance == null)
            {
                ApplyExtinguishSpray(Time.deltaTime);
            }
        }
    }

    private GUIStyle warningPromptStyle;

    private void EnsureHazardStyles()
    {
        if (warningPromptStyle == null)
        {
            warningPromptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            warningPromptStyle.normal.textColor = new Color(1f, 0.65f, 0.2f, 1f);
        }
    }

    private void LateUpdate()
    {
        // En VR OnGUI no se ve: el aviso de "¡FUEGO!" va al HUD VR
        if (!HUDVR.Activo || !isOnFire || !allowExtinguishWithExtintor || MovementSystemSelector.IsGameplayBlocked) return;
        if (!jugadorEnRangoCache || !jugadorApuntandoCache) return;
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected) return;

        HUDVR.Mensaje(CanalHUD.Fuego, PlayerHasExtinguisher()
            ? "¡FUEGO! Equipa el extintor con [Y] para apagarlo"
            : "¡FUEGO! Recoge el extintor para apagarlo", new Color(1f, 0.6f, 0.25f, 1f));
    }

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (LateUpdate)
        if (!isOnFire || !allowExtinguishWithExtintor) return;
        if (MovementSystemSelector.IsGameplayBlocked) return;
        if (!jugadorEnRangoCache || !jugadorApuntandoCache) return;

        // Con el extintor en la mano ya se muestra la barra de progreso de ExtinguisherSprayController
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected) return;

        EnsureHazardStyles();

        string texto = PlayerHasExtinguisher()
            ? "🔥 ¡FUEGO! Equipa el extintor con [1] o [2] para apagarlo"
            : "🔥 ¡FUEGO! Recoge el Extintor para apagarlo";

        Rect warnRect = new Rect((Screen.width - 420f) * 0.5f, Screen.height * 0.5f + 50f, 420f, 32f);
        Color prevColor = GUI.color;
        GUI.color = new Color(0.28f, 0.08f, 0.08f, 0.88f);
        GUI.DrawTexture(warnRect, Texture2D.whiteTexture);
        GUI.color = prevColor;
        GUI.Label(warnRect, texto, warningPromptStyle);
    }

    private IEnumerator ExplosionLightFlashRoutine()
    {
        if (firePointLight == null) yield break;
        enDestello = true;

        firePointLight.enabled = true;
        firePointLight.color = new Color(1f, 0.9f, 0.45f, 1f);
        firePointLight.intensity = explosionLightIntensity;

        float elapsed = 0f;
        float duration = 0.4f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            firePointLight.intensity = Mathf.Lerp(explosionLightIntensity, fireLightIntensity, elapsed / duration);
            yield return null;
        }

        enDestello = false;
        firePointLight.color = fireLightColor;
    }

    private IEnumerator ShakeMachineRoutine()
    {
        float duration = 0.45f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float damper = 1f - (elapsed / duration);
            float offsetX = (Random.value * 2f - 1f) * 0.07f * damper;
            float offsetZ = (Random.value * 2f - 1f) * 0.07f * damper;
            float tilt = (Random.value * 2f - 1f) * 3.5f * damper;

            transform.position = originalPosition + new Vector3(offsetX, 0f, offsetZ);
            transform.rotation = originalRotation * Quaternion.Euler(0f, 0f, tilt);

            yield return null;
        }

        transform.position = originalPosition;
        transform.rotation = originalRotation;
    }

    private void ApplyCharredAppearance()
    {
        if (machineRenderers == null) return;

        foreach (Renderer r in machineRenderers)
        {
            if (r == null) continue;
            foreach (Material m in r.materials)
            {
                if (m.HasProperty("_BaseColor"))
                {
                    Color baseCol = m.GetColor("_BaseColor");
                    m.SetColor("_BaseColor", baseCol * 0.28f);
                }
                else if (m.HasProperty("_Color"))
                {
                    Color col = m.GetColor("_Color");
                    m.SetColor("_Color", col * 0.28f);
                }

                if (m.HasProperty("_Smoothness"))
                {
                    m.SetFloat("_Smoothness", 0.05f);
                }
            }
        }
    }

    /// <summary>
    /// Calcula el centro y dimensiones exactas de la máquina expendedora en espacio local
    /// para que el fuego se adhiera directamente a su cuerpo.
    /// </summary>
    private void GetMachineBounds(out Vector3 localCenter, out Vector3 localSize)
    {
        localCenter = new Vector3(0f, 1f, 0f);
        localSize = new Vector3(1f, 2f, 0.9f);

        if (machineRenderers != null && machineRenderers.Length > 0)
        {
            Bounds b = machineRenderers[0].bounds;
            for (int i = 1; i < machineRenderers.Length; i++)
            {
                if (machineRenderers[i] != null && !machineRenderers[i].name.Contains("Effect"))
                {
                    b.Encapsulate(machineRenderers[i].bounds);
                }
            }

            localCenter = transform.InverseTransformPoint(b.center);
            Vector3 worldExtents = b.size;
            Vector3 lossy = transform.lossyScale;
            localSize = new Vector3(
                Mathf.Abs(lossy.x > 0.001f ? worldExtents.x / lossy.x : worldExtents.x),
                Mathf.Abs(lossy.y > 0.001f ? worldExtents.y / lossy.y : worldExtents.y),
                Mathf.Abs(lossy.z > 0.001f ? worldExtents.z / lossy.z : worldExtents.z)
            );
        }
    }

    private void BuildVisualEffects()
    {
        GetMachineBounds(out Vector3 localCenter, out Vector3 localSize);

        // 1. Luz puntual para fuego y explosión (centrada en la máquina)
        GameObject lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(transform);
        lightGo.transform.localPosition = localCenter + new Vector3(0f, localSize.y * 0.1f, 0f) + fireOffsetAdjustment;

        firePointLight = lightGo.AddComponent<Light>();
        firePointLight.type = LightType.Point;
        firePointLight.range = fireLightRange;
        firePointLight.intensity = fireLightIntensity;
        firePointLight.color = fireLightColor;
        firePointLight.enabled = false;

        // 2. Partículas de Explosión (Burst amplio)
        GameObject expGo = new GameObject("ExplosionEffect");
        expGo.transform.SetParent(transform);
        expGo.transform.localPosition = localCenter + new Vector3(0f, 0f, 0.05f) + fireOffsetAdjustment;
        explosionParticles = CreateExplosionParticleSystem(expGo, localSize);

        // 3. Partículas de Llamas Principales (Grandes, rodeando y abrazando la máquina)
        GameObject fireGo = new GameObject("FlamesEffect");
        fireGo.transform.SetParent(transform);
        // Nace desde la base/mitad inferior del chasis para subir envolviendo la máquina completa
        fireGo.transform.localPosition = localCenter + new Vector3(0f, -localSize.y * 0.22f, 0f) + fireOffsetAdjustment;
        mainFlamesParticles = CreateMainFlamesParticleSystem(fireGo, localSize);

        // 4. Núcleo Interno de Fuego (Llamas ardientes de alta intensidad en el interior/cristal)
        GameObject coreGo = new GameObject("CoreFlamesEffect");
        coreGo.transform.SetParent(transform);
        coreGo.transform.localPosition = localCenter + new Vector3(0f, -localSize.y * 0.15f, localSize.z * 0.05f) + fireOffsetAdjustment;
        coreFlamesParticles = CreateCoreFlamesParticleSystem(coreGo, localSize);

        // 5. Chispas y Brasas Ascendentes
        GameObject embersGo = new GameObject("EmbersEffect");
        embersGo.transform.SetParent(transform);
        embersGo.transform.localPosition = localCenter + new Vector3(0f, 0f, 0f) + fireOffsetAdjustment;
        embersParticles = CreateEmbersParticleSystem(embersGo, localSize);

        // 6. Columna de Humo Negro Ascendente
        GameObject smokeGo = new GameObject("SmokeEffect");
        smokeGo.transform.SetParent(transform);
        smokeGo.transform.localPosition = localCenter + new Vector3(0f, localSize.y * 0.35f, 0f) + fireOffsetAdjustment;
        smokeParticles = CreateSmokeParticleSystem(smokeGo, localSize);

        // 7. Partículas de Extintor (Nube blanca de sofocación)
        GameObject extGo = new GameObject("ExtinguishEffect");
        extGo.transform.SetParent(transform);
        extGo.transform.localPosition = localCenter + new Vector3(0f, 0f, localSize.z * 0.2f) + fireOffsetAdjustment;
        extinguishParticles = CreateExtinguishParticleSystem(extGo, localSize);
    }

    private ParticleSystem CreateExplosionParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1.0f;
        main.startLifetime = 0.9f;
        main.startSpeed = 5.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startColor = new Color(1f, 0.65f, 0.1f, 1f);
        main.gravityModifier = 0.25f;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 55) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(localSize.x * 0.9f, localSize.y * 0.5f, localSize.z * 0.8f);

        ConfigureParticleRenderer(parent, new Color(1f, 0.75f, 0.2f, 1f), CreateFlameTexture());
        return ps;
    }

    private ParticleSystem CreateMainFlamesParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        // Vida y velocidad aumentadas para que las llamas asciendan y superen el techo de la máquina
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        // TAMAÑO MUCHO MÁS GRANDE (1.5m a 2.4m por partícula multiplicado por fireSizeMultiplier)
        main.startSize = new ParticleSystem.MinMaxCurve(1.5f * fireSizeMultiplier, 2.4f * fireSizeMultiplier);
        main.startColor = new Color(1f, 0.42f, 0.05f, 0.95f);
        main.gravityModifier = -0.55f; // Fuerza térmica ascendente pronunciada

        var emission = ps.emission;
        emission.rateOverTime = 65f * fireDensityMultiplier;

        // Caja de emisión estrechamente ajustada a las dimensiones de la vending machine
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(localSize.x * 0.95f, localSize.y * 0.45f, localSize.z * 0.9f);

        // Curva de tamaño durante la vida: la llama nace, se expande con furia y se afila en la punta
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.45f);
        sizeCurve.AddKey(0.3f, 1.15f);
        sizeCurve.AddKey(0.7f, 0.9f);
        sizeCurve.AddKey(1f, 0.15f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Gradiente de color: núcleo ardiente a naranja y rojo en las puntas
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.85f, 0.35f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.05f), 0.45f),
                new GradientColorKey(new Color(0.9f, 0.12f, 0.01f), 0.85f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.2f, 0f),
                new GradientAlphaKey(0.95f, 0.15f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = grad;

        ConfigureParticleRenderer(parent, Color.white, CreateFlameTexture());
        return ps;
    }

    private ParticleSystem CreateCoreFlamesParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.1f * fireSizeMultiplier, 1.8f * fireSizeMultiplier);
        main.startColor = new Color(1f, 0.9f, 0.45f, 0.98f); // Núcleo amarillo incandescente
        main.gravityModifier = -0.4f;

        var emission = ps.emission;
        emission.rateOverTime = 35f * fireDensityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(localSize.x * 0.75f, localSize.y * 0.35f, localSize.z * 0.75f);

        ConfigureParticleRenderer(parent, new Color(1f, 0.95f, 0.5f, 1f), CreateFlameTexture());
        return ps;
    }

    private ParticleSystem CreateEmbersParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new Color(1f, 0.75f, 0.2f, 1f);
        main.gravityModifier = -0.3f;

        var emission = ps.emission;
        emission.rateOverTime = 40f * fireDensityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(localSize.x * 0.9f, localSize.y * 0.5f, localSize.z * 0.85f);

        ConfigureParticleRenderer(parent, new Color(1f, 0.8f, 0.3f, 1f), CreateSoftCircleTexture());
        return ps;
    }

    private ParticleSystem CreateSmokeParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 4.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        // Humo denso y de gran volumen (2m a 3.8m de diámetro)
        main.startSize = new ParticleSystem.MinMaxCurve(2.0f * fireSizeMultiplier, 3.6f * fireSizeMultiplier);
        main.startColor = new Color(0.12f, 0.12f, 0.12f, 0.65f);
        main.gravityModifier = -0.2f;

        var emission = ps.emission;
        emission.rateOverTime = 25f * fireDensityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = localSize.x * 0.45f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve smokeSizeCurve = new AnimationCurve();
        smokeSizeCurve.AddKey(0f, 0.5f);
        smokeSizeCurve.AddKey(0.5f, 1.4f);
        smokeSizeCurve.AddKey(1f, 2.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, smokeSizeCurve);

        ConfigureParticleRenderer(parent, new Color(0.15f, 0.15f, 0.15f, 0.6f), CreateSoftCircleTexture());
        return ps;
    }

    private ParticleSystem CreateExtinguishParticleSystem(GameObject parent, Vector3 localSize)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1.2f;
        main.startLifetime = 1.5f;
        main.startSpeed = 4.2f;
        main.startSize = 2.2f;
        main.startColor = new Color(0.95f, 0.98f, 1f, 0.85f);
        main.gravityModifier = 0.05f;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 65) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 28f;
        shape.radius = localSize.x * 0.5f;

        ConfigureParticleRenderer(parent, new Color(0.96f, 0.96f, 1f, 0.85f), CreateSoftCircleTexture());
        return ps;
    }

    private void ConfigureParticleRenderer(GameObject go, Color tint, Texture2D texture)
    {
        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.mainTexture = texture;
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // Alpha blend

        renderer.material = mat;
    }

    private Texture2D CreateFlameTexture()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size * 0.5f, size * 0.42f);
        float rX = size * 0.42f;
        float rY = size * 0.52f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - center.x) / rX;
                float dy = (y + 0.5f - center.y) / rY;
                float dist = dx * dx + dy * dy;

                float alpha = Mathf.Clamp01(1f - dist);
                alpha = Mathf.Pow(alpha, 1.8f);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private Texture2D CreateSoftCircleTexture()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float r = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float a = Mathf.SmoothStep(1f, 0f, d / r);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    private AudioClip GenerateExplosionAudio()
    {
        int sampleRate = 44100;
        float duration = 1.0f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 5f);
            float noise = (Random.value * 2f - 1f);
            float boom = Mathf.Sin(2f * Mathf.PI * 60f * t * (1f - t * 0.5f));
            float zap = Mathf.Sin(2f * Mathf.PI * (1200f - t * 800f) * t) * (t < 0.15f ? 1f : 0f);

            data[i] = (noise * 0.4f + boom * 0.5f + zap * 0.25f) * env;
        }

        AudioClip clip = AudioClip.Create("ExplosionAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip GenerateFireCrackleAudio()
    {
        int sampleRate = 44100;
        float duration = 2.5f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float noise = (Random.value * 2f - 1f) * 0.12f;
            if (Random.value < 0.003f)
            {
                noise += (Random.value * 2f - 1f) * 0.55f;
            }
            data[i] = noise;
        }

        AudioClip clip = AudioClip.Create("FireCrackleAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip GenerateExtinguisherSprayAudio()
    {
        int sampleRate = 44100;
        float duration = 1.6f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = t < 0.12f 
                ? (t / 0.12f) 
                : Mathf.Exp(-(t - 0.12f) * 2.2f);

            float whiteNoise = (Random.value * 2f - 1f);
            float hiss = Mathf.Sin(2f * Mathf.PI * 3200f * t) * (Random.value * 2f - 1f) * 0.45f;
            data[i] = (whiteNoise * 0.65f + hiss) * env * 0.85f;
        }

        AudioClip clip = AudioClip.Create("ExtinguisherSprayAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

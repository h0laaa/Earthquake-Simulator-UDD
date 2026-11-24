using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controlador avanzado del chorro y nube del extintor.
/// Características:
/// - Nube blanca de gran escala y volumen que nace bajo la cámara del jugador y se proyecta hacia adelante.
/// - Se puede usar y rociar en todo momento (siempre que se tenga el extintor o en modo prueba).
/// - Sistema de apagado continuo: rociar el fuego va llenando una barra de progreso hasta extinguirlo por completo.
/// - Las llamas y humo disminuyen visualmente en tiempo real a medida que la barra se llena.
/// - Compatible con Teclado, Ratón (clic sostenido), Barra Espaciadora, Pantalla Táctil, Mandos VR y Gamepad.
/// </summary>
public class ExtinguisherSprayController : MonoBehaviour
{
    public static ExtinguisherSprayController Instance { get; private set; }

    [Header("Configuración del Chorro y Nube")]
    [Tooltip("Distancia máxima de alcance del chorro del extintor en metros")]
    [SerializeField] private float maxSprayDistance = 8.5f;

    [Tooltip("Radio del cono de rociado para detectar impacto sobre el fuego")]
    [SerializeField] private float sprayConeRadius = 2.4f;

    [Tooltip("Multiplicador de escala del tamaño de la nube blanca")]
    [Range(1f, 4f)]
    [SerializeField] private float cloudScaleMultiplier = 2.5f;

    [Tooltip("Posición relativa de la boquilla respecto a la cámara (bajo y ligeramente a la derecha)")]
    [SerializeField] private Vector3 nozzleOffset = new Vector3(0.22f, -0.28f, 0.45f);

    [Header("Requisitos de Uso")]
    [Tooltip("Requiere tener el Extintor en el inventario para poder rociar")]
    [SerializeField] private bool requireExtinguisherInInventory = true;

    [Tooltip("Permite rociar en todo momento incluso sin extintor (ideal para pruebas y modo libre)")]
    [SerializeField] private bool allowTestSprayAlways = false;

    [Header("Interfaz y HUD")]
    [Tooltip("Muestra la barra de progreso de apagado en pantalla")]
    [SerializeField] private bool showProgressBar = true;

    [Tooltip("Muestra el botón táctil de rociado sostenido")]
    [SerializeField] private bool showTouchButton = true;

    // Componentes del jugador
    private Camera playerCamera;
    private GameObject nozzleObject;
    private ParticleSystem cloudParticleSystem;
    private ParticleSystem jetCoreParticleSystem;
    private AudioSource sprayAudioSource;

    // Estados
    private bool isSpraying = false;
    private bool isAimingAtFire = false;
    private float touchButtonHeldTimer = 0f;
    private float extinguishSuccessTimer = 0f;

    // Dispositivos VR
    private readonly List<UnityEngine.XR.InputDevice> vrRightDevices = new List<UnityEngine.XR.InputDevice>();
    private readonly List<UnityEngine.XR.InputDevice> vrLeftDevices = new List<UnityEngine.XR.InputDevice>();

    // Estilos de interfaz
    private GUIStyle barBackgroundStyle;
    private GUIStyle barFillStyle;
    private GUIStyle barTextStyle;
    private GUIStyle barSubtextStyle;
    private GUIStyle touchButtonStyle;
    private GUIStyle equippedNoticeStyle;

    private Texture2D whitePixelTex;
    private Texture2D borderTex;

    public bool IsSpraying => isSpraying;
    public bool IsAimingAtFire => isAimingAtFire;

    public bool HasExtinguisher => !requireExtinguisherInInventory || allowTestSprayAlways ||
        (InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected);

    public void TriggerSprayPulse(float duration = 0.8f)
    {
        touchButtonHeldTimer = Mathf.Max(touchButtonHeldTimer, duration);
    }

    private static bool sceneHookInstalled = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();

        // AfterSceneLoad solo se ejecuta una vez: también hay que crearlo al recargar la escena (Reintentar)
        if (!sceneHookInstalled)
        {
            sceneHookInstalled = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (escena, modo) => EnsureInstance();
        }
    }

    public static ExtinguisherSprayController EnsureInstance()
    {
        if (Instance != null) return Instance;

        ExtinguisherSprayController existente = Object.FindAnyObjectByType<ExtinguisherSprayController>();
        if (existente != null) return existente;

        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            ExtinguisherSprayController ctrl = cam.GetComponent<ExtinguisherSprayController>();
            if (ctrl == null)
            {
                ctrl = cam.gameObject.AddComponent<ExtinguisherSprayController>();
            }
            return ctrl;
        }
        return null;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        playerCamera = GetComponent<Camera>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamera == null) playerCamera = Object.FindAnyObjectByType<Camera>();

        SetupNozzleAndParticles();
        SetupAudio();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void SetupNozzleAndParticles()
    {
        if (playerCamera == null) return;

        // 1. Objeto boquilla anclado bajo la cámara
        if (nozzleObject == null)
        {
            nozzleObject = new GameObject("ExtinguisherNozzle");
            nozzleObject.transform.SetParent(playerCamera.transform, false);
            nozzleObject.transform.localPosition = nozzleOffset;
            // La boquilla sale abajo a la derecha pero apunta hacia la mira (converge a 4 m): la nube sigue a la cámara
            nozzleObject.transform.localRotation = RotacionBoquillaCamara();
        }

        Texture2D powderTex = CreatePowderTexture();

        // 2. Sistema de Nube Blanca Gigante y Volumétrica
        GameObject cloudGO = new GameObject("CloudParticles");
        cloudGO.transform.SetParent(nozzleObject.transform, false);
        cloudGO.transform.localPosition = Vector3.zero;
        cloudGO.transform.localRotation = Quaternion.identity;

        cloudParticleSystem = cloudGO.AddComponent<ParticleSystem>();
        var main = cloudParticleSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // Las partículas flotan en el mundo 3D
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(16f, 24f); // Chorro potente hacia adelante
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f * cloudScaleMultiplier, 1.4f * cloudScaleMultiplier);
        main.startColor = new Color(0.97f, 0.98f, 1f, 0.9f);
        main.gravityModifier = -0.04f; // Ligera flotabilidad como gas frío expandido

        var emission = cloudParticleSystem.emission;
        emission.rateOverTime = 130f;
        emission.enabled = false; // Solo emite mientras se sostiene el rociado

        var shape = cloudParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 13f; // Cono estrecho de salida que luego se ensancha enormemente
        shape.radius = 0.06f;

        // Curva de expansión masiva: de 0.4m en la boquilla a más de 4.5m a medida que avanza
        var sizeOverLifetime = cloudParticleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.35f);
        sizeCurve.AddKey(0.18f, 1.2f);
        sizeCurve.AddKey(0.55f, 2.8f);
        sizeCurve.AddKey(1f, 4.5f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(cloudScaleMultiplier, sizeCurve);

        // Desaceleración por resistencia del aire (de chorro supersónico a densa nube rodante)
        var limitVelocity = cloudParticleSystem.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.limit = 2.2f;
        limitVelocity.dampen = 0.38f;

        // Curva de color y desvanecimiento suave
        var colorOverLifetime = cloudParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(0.95f, 0.98f, 1f), 0.6f),
                new GradientColorKey(new Color(0.9f, 0.95f, 1f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.2f, 0f),
                new GradientAlphaKey(0.92f, 0.12f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = grad;

        ConfigureRenderer(cloudGO, new Color(0.98f, 0.98f, 1f, 0.9f), powderTex);

        // 3. Núcleo central del chorro de alta presión
        GameObject coreGO = new GameObject("JetCoreParticles");
        coreGO.transform.SetParent(nozzleObject.transform, false);
        coreGO.transform.localPosition = Vector3.zero;
        coreGO.transform.localRotation = Quaternion.identity;

        jetCoreParticleSystem = coreGO.AddComponent<ParticleSystem>();
        var coreMain = jetCoreParticleSystem.main;
        coreMain.loop = true;
        coreMain.playOnAwake = false;
        coreMain.simulationSpace = ParticleSystemSimulationSpace.World;
        coreMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        coreMain.startSpeed = new ParticleSystem.MinMaxCurve(22f, 30f);
        coreMain.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        coreMain.startColor = new Color(1f, 1f, 1f, 0.95f);

        var coreEmission = jetCoreParticleSystem.emission;
        coreEmission.rateOverTime = 80f;
        coreEmission.enabled = false;

        var coreShape = jetCoreParticleSystem.shape;
        coreShape.shapeType = ParticleSystemShapeType.Cone;
        coreShape.angle = 4f;
        coreShape.radius = 0.03f;

        ConfigureRenderer(coreGO, Color.white, powderTex);
    }

    private void SetupAudio()
    {
        sprayAudioSource = gameObject.AddComponent<AudioSource>();
        sprayAudioSource.playOnAwake = false;
        sprayAudioSource.loop = true;
        sprayAudioSource.spatialBlend = 0f; // Audio 2D nítido para el jugador que sostiene el extintor
        sprayAudioSource.volume = 0.85f;
        sprayAudioSource.clip = CreateSprayLoopAudioClip();
    }

    private Quaternion RotacionBoquillaCamara()
    {
        Vector3 haciaMira = new Vector3(0f, 0f, 4f) - nozzleOffset;
        return haciaMira.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(haciaMira) : Quaternion.identity;
    }

    /// <summary>En VR el chorro sale del mando derecho; en el resto de modos, de la cámara.</summary>
    private Transform ObtenerTransformApuntado()
    {
        CameraMovement cm = CameraMovement.Instance;
        if (cm != null && cm.IsVRPresent && cm.VRRightControllerAnchor != null) return cm.VRRightControllerAnchor;
        return playerCamera.transform;
    }

    private void SeguirOrientacionDeApuntado()
    {
        if (nozzleObject == null) return;

        Transform objetivo = ObtenerTransformApuntado();
        if (nozzleObject.transform.parent == objetivo) return;

        nozzleObject.transform.SetParent(objetivo, false);
        bool esCamara = objetivo == playerCamera.transform;
        nozzleObject.transform.localPosition = esCamara ? nozzleOffset : new Vector3(0f, -0.02f, 0.12f);
        nozzleObject.transform.localRotation = esCamara ? RotacionBoquillaCamara() : Quaternion.identity;
    }

    private static bool EsModoTactil()
    {
        return CameraMovement.Instance != null ? CameraMovement.Instance.IsTouchActive : (Application.isMobilePlatform && !UIVR.Activo);
    }

    private void Update()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) playerCamera = Object.FindAnyObjectByType<Camera>();
            if (playerCamera == null) return;
            if (nozzleObject == null) SetupNozzleAndParticles();
        }

        // Menú abierto, frame de cierre del menú o jugador muerto: nada de rociar
        if (MovementSystemSelector.IsGameplayBlocked)
        {
            touchButtonHeldTimer = 0f;
            if (isSpraying) StopSpraying();
            isAimingAtFire = false;
            return;
        }

        if (extinguishSuccessTimer > 0f)
        {
            extinguishSuccessTimer -= Time.deltaTime;
        }

        SeguirOrientacionDeApuntado();

        bool wantsToSpray = CheckSprayInput();

        if (wantsToSpray && HasExtinguisher)
        {
            if (!isSpraying)
            {
                StartSpraying();
            }
            UpdateSpraying();
        }
        else
        {
            if (isSpraying)
            {
                StopSpraying();
            }
            isAimingAtFire = false;
        }
    }

    private bool CheckSprayInput()
    {
        bool pressed = false;

        // [E] solo rocía si no se está apuntando a un objeto recogible (en ese caso E es "recoger")
        bool teclaELibre = PlayerInteraction.Instance == null || !PlayerInteraction.Instance.HasTarget;

        // 1. Clic izquierdo sostenido / E (PC). Ya NO se usa Espacio: es la tecla de saltar.
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) pressed = true;
        if (teclaELibre && Keyboard.current != null && Keyboard.current[Key.E].isPressed) pressed = true;
        if (Gamepad.current != null)
        {
            // Sin buttonSouth (es saltar)
            if (Gamepad.current.rightTrigger.isPressed || Gamepad.current.buttonWest.isPressed)
                pressed = true;
        }
#else
        if (Input.GetMouseButton(0) || (teclaELibre && Input.GetKey(KeyCode.E))) pressed = true;
#endif

        // 2. Botón táctil en pantalla sostenido / pulso desde el inventario
        if (touchButtonHeldTimer > 0f)
        {
            touchButtonHeldTimer -= Time.deltaTime;
            pressed = true;
        }

        // 3. Mandos de Realidad Virtual (gatillo o grip)
        if (CheckVRTrigger())
        {
            pressed = true;
        }

        return pressed;
    }

    private bool CheckVRTrigger()
    {
        vrRightDevices.Clear();
        vrLeftDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrRightDevices);
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrLeftDevices);

        if (vrRightDevices.Count > 0 && vrRightDevices[0].isValid)
        {
            if (vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trig) && trig) return true;
            if (vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip) return true;
            // (Sin botón A: en VR es saltar)
        }

        if (vrLeftDevices.Count > 0 && vrLeftDevices[0].isValid)
        {
            if (vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigL) && trigL) return true;
            if (vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripL) && gripL) return true;
        }

        return false;
    }

    private void StartSpraying()
    {
        isSpraying = true;

        if (cloudParticleSystem != null)
        {
            var em = cloudParticleSystem.emission;
            em.enabled = true;
            cloudParticleSystem.Play();
        }

        if (jetCoreParticleSystem != null)
        {
            var emCore = jetCoreParticleSystem.emission;
            emCore.enabled = true;
            jetCoreParticleSystem.Play();
        }

        if (sprayAudioSource != null && !sprayAudioSource.isPlaying)
        {
            sprayAudioSource.Play();
        }
    }

    private void UpdateSpraying()
    {
        Transform apuntado = ObtenerTransformApuntado();
        Vector3 origin = apuntado.position;
        Vector3 forward = apuntado.forward;

        isAimingAtFire = false;

        VendingMachineHazard hazard = VendingMachineHazard.Instance;
        if (hazard == null || !hazard.IsOnFire) return;

        Transform raizJugador = CameraMovement.Instance != null ? CameraMovement.Instance.transform : playerCamera.transform;
        bool hitHazard = false;

        // 1. Chorro directo: el PRIMER obstáculo sólido del cono es la máquina (ignorando al propio jugador)
        if (PrimerImpactoSolido(origin, sprayConeRadius * 0.45f, forward, maxSprayDistance, raizJugador, out RaycastHit hit))
        {
            if (EsParteDe(hit.collider.transform, hazard.transform)) hitHazard = true;
        }

        // 2. Dispersión de la nube: dentro del cono (~66°) y CON línea de visión (antes apagaba a través de paredes)
        if (!hitHazard)
        {
            Vector3 hazardCenter = hazard.GetMachineWorldCenter();
            Vector3 haciaMaquina = hazardCenter - origin;
            float dist = haciaMaquina.magnitude;
            if (dist > 0.001f && dist <= maxSprayDistance)
            {
                Vector3 dirToHazard = haciaMaquina / dist;
                if (Vector3.Dot(forward, dirToHazard) > 0.40f)
                {
                    bool bloqueado = PrimerImpactoSolido(origin, 0.05f, dirToHazard, dist, raizJugador, out RaycastHit bloqueo)
                                     && !EsParteDe(bloqueo.collider.transform, hazard.transform);
                    if (!bloqueado) hitHazard = true;
                }
            }
        }

        if (hitHazard)
        {
            isAimingAtFire = true;
            hazard.ApplyExtinguishSpray(Time.deltaTime);

            if (hazard.IsExtinguished)
            {
                extinguishSuccessTimer = 3.5f;
            }
        }
    }

    private readonly RaycastHit[] bufferHits = new RaycastHit[24];

    /// <summary>
    /// SphereCast que devuelve el primer impacto sólido ignorando al jugador y a los objetos recogibles sueltos.
    /// La nube es solo visual (partículas sin colisión): nunca empuja al jugador.
    /// </summary>
    private bool PrimerImpactoSolido(Vector3 origen, float radio, Vector3 direccion, float distancia, Transform ignorar, out RaycastHit mejor)
    {
        mejor = default;
        bool encontrado = false;
        float mejorDist = float.MaxValue;

        int n = Physics.SphereCastNonAlloc(origen, radio, direccion, bufferHits, distancia, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = bufferHits[i];
            if (h.collider == null) continue;
            if (ignorar != null && (h.collider.transform == ignorar || h.collider.transform.IsChildOf(ignorar))) continue;
            if (h.collider.GetComponentInParent<InteractableItem>() != null) continue;

            if (h.distance < mejorDist)
            {
                mejorDist = h.distance;
                mejor = h;
                encontrado = true;
            }
        }
        return encontrado;
    }

    private static bool EsParteDe(Transform t, Transform raiz)
    {
        return t == raiz || t.IsChildOf(raiz);
    }

    private void StopSpraying()
    {
        isSpraying = false;
        isAimingAtFire = false;

        if (cloudParticleSystem != null)
        {
            var em = cloudParticleSystem.emission;
            em.enabled = false;
        }

        if (jetCoreParticleSystem != null)
        {
            var emCore = jetCoreParticleSystem.emission;
            emCore.enabled = false;
        }

        if (sprayAudioSource != null && sprayAudioSource.isPlaying)
        {
            sprayAudioSource.Stop();
        }
    }

    // VR: texto de la barra (se rehace solo cuando cambia el porcentaje o el estado)
    private int porcentajeHUDVR = -1;
    private int estadoHUDVR = -1;
    private string textoProgresoHUDVR = "";

    private void LateUpdate()
    {
        // En VR OnGUI no se ve: la barra de apagado y el aviso de "extintor listo" van al HUD VR
        if (!HUDVR.Activo || MovementSystemSelector.IsGameplayBlocked || playerCamera == null) return;

        VendingMachineHazard hazard = VendingMachineHazard.Instance;
        bool hazardOnFire = hazard != null && hazard.IsOnFire;

        if (showProgressBar && (hazardOnFire || extinguishSuccessTimer > 0f))
        {
            float progress = hazard != null ? hazard.ExtinguishProgress : 0f;
            bool isCompleted = (hazard != null && hazard.IsExtinguished) || progress >= 1f;
            bool mostrar = isSpraying || progress > 0f || isCompleted ||
                (hazard != null && Vector3.Distance(playerCamera.transform.position, hazard.GetMachineWorldCenter()) <= maxSprayDistance + 1f);

            if (mostrar)
            {
                int porcentaje = Mathf.RoundToInt(progress * 100f);
                int estado = isCompleted ? 2 : (isAimingAtFire ? 1 : 0);
                if (porcentaje != porcentajeHUDVR || estado != estadoHUDVR)
                {
                    porcentajeHUDVR = porcentaje;
                    estadoHUDVR = estado;
                    textoProgresoHUDVR = isCompleted ? "¡INCENDIO EXTINTO POR COMPLETO! 100%"
                        : (isAimingAtFire ? $"APAGANDO INCENDIO: {porcentaje}%" : $"APAGAR FUEGO: {porcentaje}%");
                }

                Color color = isCompleted
                    ? new Color(0.18f, 0.95f, 0.45f, 0.92f)
                    : Color.Lerp(new Color(1f, 0.55f, 0.1f, 0.92f), new Color(0f, 0.85f, 1f, 0.95f), progress);
                HUDVR.Progreso(textoProgresoHUDVR, isCompleted ? 1f : progress, color);
            }
        }

        if (HasExtinguisher && !isSpraying && extinguishSuccessTimer <= 0f)
        {
            HUDVR.Mensaje(CanalHUD.Extintor, "Extintor listo: mantén el gatillo para rociar", new Color(0.4f, 1f, 0.9f, 1f));
        }
    }

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (LateUpdate)
        if (MovementSystemSelector.IsGameplayBlocked || playerCamera == null) return;

        EnsureStyles();

        bool hasExtintor = HasExtinguisher;
        VendingMachineHazard hazard = VendingMachineHazard.Instance;
        bool hazardOnFire = hazard != null && hazard.IsOnFire;

        // 1. BARRA DE PROGRESO DE APAGADO (Aparece al rociar o al mirar el fuego)
        if (showProgressBar && (hazardOnFire || extinguishSuccessTimer > 0f))
        {
            float progress = hazard != null ? hazard.ExtinguishProgress : 0f;
            bool isCompleted = (hazard != null && hazard.IsExtinguished) || progress >= 1f;

            // Mostrar si está rociando, si hay progreso acumulado, o si se está cerca del fuego
            bool shouldDrawBar = isSpraying || progress > 0f || isCompleted ||
                (hazard != null && Vector3.Distance(playerCamera.transform.position, hazard.GetMachineWorldCenter()) <= maxSprayDistance + 1f);

            if (shouldDrawBar)
            {
                DrawExtinguishProgressBar(progress, isCompleted);
            }
        }

        // 2. BOTÓN TÁCTIL EN PANTALLA (Para móvil, tablet o clics directos)
        if (showTouchButton && hasExtintor && EsModoTactil())
        {
            float btnWidth = 230f;
            float btnHeight = 70f;
            Rect touchRect = new Rect(Screen.width - btnWidth - 25f, Screen.height * 0.5f - 35f, btnWidth, btnHeight);

            string btnText = isSpraying
                ? "🌊 ROCIANDO...\n[Mantén presionado]"
                : "🧯 ROCIAR EXTINTOR\n[Mantén Presionado]";

            // GUI.RepeatButton detecta si el botón se mantiene presionado continuamente cada frame
            if (GUI.RepeatButton(touchRect, btnText, touchButtonStyle))
            {
                touchButtonHeldTimer = 0.12f; // Mantener activo el rociado durante el frame
            }
        }

        // 3. INDICADOR DE EXTINTOR LISTO EN PANTALLA
        if (hasExtintor && !isSpraying && extinguishSuccessTimer <= 0f)
        {
            Rect hintRect = new Rect((Screen.width - 340f) * 0.5f, Screen.height - 40f, 340f, 24f);
            GUI.Label(hintRect, "🧯 Extintor Listo: Mantén [Clic Izq / E / VR Trigger] para Rociar", equippedNoticeStyle);
        }
    }

    private void DrawExtinguishProgressBar(float progress, bool isCompleted)
    {
        float barWidth = 380f;
        float barHeight = 46f;
        float barX = (Screen.width - barWidth) * 0.5f;
        float barY = Screen.height * 0.72f;

        Rect totalRect = new Rect(barX, barY, barWidth, barHeight);

        // Fondo del panel
        Color prevC = GUI.color;
        GUI.color = new Color(0.06f, 0.1f, 0.16f, 0.94f);
        GUI.DrawTexture(totalRect, whitePixelTex);

        // Borde elegante
        GUI.color = isCompleted ? new Color(0.2f, 1f, 0.5f, 0.95f) : (isAimingAtFire ? new Color(0f, 0.9f, 1f, 0.95f) : new Color(0.4f, 0.6f, 0.75f, 0.7f));
        DrawBorder(totalRect, 2);

        // Relleno de la barra interior
        float pad = 4f;
        float fillWidth = (barWidth - pad * 2f) * Mathf.Clamp01(progress);
        Rect fillRect = new Rect(barX + pad, barY + pad, fillWidth, barHeight - pad * 2f);

        Color fillColor = isCompleted
            ? new Color(0.18f, 0.95f, 0.45f, 0.92f)
            : (Color.Lerp(new Color(1f, 0.55f, 0.1f, 0.92f), new Color(0f, 0.85f, 1f, 0.95f), progress));

        GUI.color = fillColor;
        GUI.DrawTexture(fillRect, whitePixelTex);
        GUI.color = prevC;

        // Texto central del estado y porcentaje
        string mainText = isCompleted
            ? "✨ ¡INCENDIO EXTINTO POR COMPLETO! 100% ✨"
            : (isAimingAtFire
                ? $"🧯 APAGANDO INCENDIO: {Mathf.RoundToInt(progress * 100f)}%"
                : $"🔥 APAGAR FUEGO: {Mathf.RoundToInt(progress * 100f)}%");

        GUI.Label(totalRect, mainText, barTextStyle);

        // Subtexto indicativo
        if (!isCompleted)
        {
            Rect subRect = new Rect(barX, barY + barHeight + 2f, barWidth, 20f);
            string subText = isAimingAtFire
                ? "[Mantén rociando el fuego directamente]"
                : (isSpraying ? "[Apunta el chorro hacia las llamas]" : "[Mantén Clic Izq / E / Espacio / Gatillo VR]");
            GUI.Label(subRect, subText, barSubtextStyle);
        }
    }

    private void DrawBorder(Rect r, int thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), borderTex);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), borderTex);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), borderTex);
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), borderTex);
    }

    private void EnsureStyles()
    {
        if (whitePixelTex == null)
        {
            whitePixelTex = new Texture2D(1, 1);
            whitePixelTex.SetPixel(0, 0, Color.white);
            whitePixelTex.Apply();

            borderTex = new Texture2D(1, 1);
            borderTex.SetPixel(0, 0, Color.white);
            borderTex.Apply();
        }

        if (barTextStyle == null)
        {
            barTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            barTextStyle.normal.textColor = Color.white;
        }

        if (barSubtextStyle == null)
        {
            barSubtextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            barSubtextStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.9f);
        }

        if (touchButtonStyle == null)
        {
            touchButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            touchButtonStyle.normal.textColor = Color.white;
        }

        if (equippedNoticeStyle == null)
        {
            equippedNoticeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            equippedNoticeStyle.normal.textColor = new Color(0.3f, 1f, 0.9f, 0.85f);
        }
    }

    private void ConfigureRenderer(GameObject go, Color tint, Texture2D texture)
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
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);

        renderer.material = mat;
    }

    private Texture2D CreatePowderTexture()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float normDist = Mathf.Clamp01(dist / radius);

                float alpha = Mathf.SmoothStep(1f, 0f, normDist);
                float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                alpha = Mathf.Pow(alpha, 1.4f) * Mathf.Lerp(0.85f, 1f, noise);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private AudioClip CreateSprayLoopAudioClip()
    {
        int sampleRate = 44100;
        float duration = 2.0f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float whiteNoise = (Random.value * 2f - 1f);
            float highHiss = Mathf.Sin(2f * Mathf.PI * 3600f * t) * (Random.value * 2f - 1f) * 0.35f;
            float midTurbulence = Mathf.Sin(2f * Mathf.PI * 850f * t) * (Random.value * 2f - 1f) * 0.25f;

            data[i] = (whiteNoise * 0.55f + highHiss + midTurbulence) * 0.75f;
        }

        AudioClip clip = AudioClip.Create("ContinuousExtinguisherSprayLoop", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Control de movimiento de cámara en Play Mode con soporte para:
/// 1. Teclado y Mouse (PC tradicional).
/// 2. Control Táctil (Pantalla táctil / Móvil con Joystick Virtual y rotación por arrastre).
/// 3. Realidad Virtual (VR con Visor y Mandos XR / Thumbsticks / Snap Turn).
/// Incluye casilla selectora de modo y casillas de activación individual, colisiones esféricas,
/// anclaje de altura y compatibilidad completa con el sistema de sismo/temblor.
/// </summary>
public class CameraMovement : MonoBehaviour
{
    public enum ControlSchemeMode
    {
        [Tooltip("Detecta y permite todos los controles disponibles simultáneamente (Teclado, Táctil y VR)")]
        Automatico_Todos = 0,

        [Tooltip("Solo permite control mediante Teclado y Mouse (PC)")]
        TecladoYMouse = 1,

        [Tooltip("Solo permite control Táctil (Pantalla táctil / Móvil / Joystick virtual)")]
        Tactil = 2,

        [Tooltip("Solo permite control de Realidad Virtual (Visores y mandos VR)")]
        VR = 3
    }

    public enum LocomotionMode
    {
        [Tooltip("Movimiento suave continuo tradicional")]
        Continuo = 0,

        [Tooltip("Teletransporte por arco parábola instantáneo (ideal para confort VR)")]
        Teletransporte = 1,

        [Tooltip("Permite ambos: caminar continuo y teletransportarse a voluntad")]
        Hibrido = 2
    }

    [Header("--- SELECCIÓN DE TIPO DE CONTROL ---")]
    [Tooltip("Casilla para seleccionar qué sistema de control usar: Teclado/Mouse, Táctil, VR o Automático")]
    [SerializeField] private ControlSchemeMode modoControl = ControlSchemeMode.Automatico_Todos;

    [Header("--- TIPO DE LOCOMOCIÓN (CONTINUO / TELETRANSPORTE) ---")]
    [Tooltip("Estilo de locomoción: Continuo (caminar suave), Teletransporte (arco y salto instantáneo) o Híbrido (ambos)")]
    [SerializeField] private LocomotionMode tipoLocomocion = LocomotionMode.Continuo;

    [Tooltip("Distancia máxima del arco de teletransporte (en metros)")]
    [SerializeField] private float teletransporteMaxDistancia = 16f;

    [Tooltip("Velocidad de proyección parabólica del teletransporte")]
    [SerializeField] private float teletransporteVelocidadArco = 13.5f;

    [Tooltip("Número de segmentos para el arco visual de teletransporte")]
    [SerializeField] private int teletransporteSegmentos = 28;

    [Tooltip("Permite usar teletransporte en PC con la tecla T o clic")]
    [SerializeField] private bool teletransporteEnPC = true;

    [Tooltip("Permite usar teletransporte en mandos VR (stick hacia adelante o gatillo)")]
    [SerializeField] private bool teletransporteEnVR = true;

    [Header("Casillas de Activación Individual")]
    [Tooltip("Casilla para activar o desactivar la entrada por Teclado y Mouse")]
    [SerializeField] private bool usarTecladoYMouse = true;

    [Tooltip("Casilla para activar o desactivar la entrada Táctil (móvil, tablet, gestos en pantalla)")]
    [SerializeField] private bool usarControlTactil = true;

    [Tooltip("Casilla para activar o desactivar la entrada por mandos y visores de Realidad Virtual (VR)")]
    [SerializeField] private bool usarControlVR = true;

    public LocomotionMode TipoLocomocion { get => tipoLocomocion; set => tipoLocomocion = value; }
    public ControlSchemeMode ModoControl { get => modoControl; set => modoControl = value; }
    public bool UsarTecladoYMouse { get => usarTecladoYMouse; set => usarTecladoYMouse = value; }
    public bool UsarControlTactil { get => usarControlTactil; set => usarControlTactil = value; }
    public bool UsarControlVR { get => usarControlVR; set => usarControlVR = value; }
    public bool VRSnapTurn { get => vrSnapTurn; set => vrSnapTurn = value; }
    public float VRSnapAngulo { get => vrSnapAngulo; set => vrSnapAngulo = value; }
    public bool RequireRightClickToLook { get => requireRightClickToLook; set => requireRightClickToLook = value; }
    public bool IsMovementPausedByMenu { get => isMovementPausedByMenu; set => isMovementPausedByMenu = value; }

    /// <summary>Giro horizontal de la mirada (grados). El menú de inicio lo usa para la vista panorámica de fondo.</summary>
    public float Yaw { get => yaw; set => yaw = value; }

    public bool IsKeyboardActive => (modoControl == ControlSchemeMode.Automatico_Todos || modoControl == ControlSchemeMode.TecladoYMouse) && usarTecladoYMouse;
    // Meta Quest es Android (isMobilePlatform = true) pero no tiene pantalla táctil: con visor no se activa lo táctil
    public bool IsTouchActive =>
        (Application.isMobilePlatform && !UIVR.Activo) ||
        (modoControl == ControlSchemeMode.Tactil) ||
        ((modoControl == ControlSchemeMode.Automatico_Todos) && (usarControlTactil || (Application.isEditor && simularTactilConMouseEnEditor)));
    public bool IsVRActive => (modoControl == ControlSchemeMode.Automatico_Todos || modoControl == ControlSchemeMode.VR) && usarControlVR;

    [Header("Velocidad de Movimiento")]
    [Tooltip("Velocidad base de movimiento de la cámara")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("Multiplicador de velocidad al presionar la tecla o botón de sprint")]
    [SerializeField] private float sprintMultiplier = 2f;

    [Tooltip("Multiplicador de velocidad al estar agachado")]
    [SerializeField] private float crouchSpeedMultiplier = 0.6f;

    [Header("Dificultad Durante el Shake (Temblor)")]
    [Tooltip("Reduce la velocidad de movimiento cuando la pantalla está temblando (ej. 0.45 para 45% de velocidad)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float shakeSpeedMultiplier = 0.45f;

    [Tooltip("Desactiva o penaliza el sprint mientras la cámara tiembla")]
    [SerializeField] private bool disableSprintDuringShake = true;

    [Tooltip("Añade tambaleo e inestabilidad al caminar durante el temblor")]
    [SerializeField] private bool enableStumble = true;

    [Tooltip("Intensidad del tambaleo / empuje involuntario que desvía los pasos (valores más altos generan sacudidas más fuertes)")]
    [Range(0.1f, 3f)]
    [SerializeField] private float stumbleIntensity = 1f;

    [Tooltip("Inclinación lateral de la cámara (Roll) al tambalearse por el sismo")]
    [Range(0f, 10f)]
    [SerializeField] private float stumbleCameraTilt = 4f;

    [Tooltip("Reduce la fuerza del salto mientras la cámara tiembla")]
    [Range(0.1f, 1f)]
    [SerializeField] private float shakeJumpMultiplier = 0.5f;

    [Tooltip("Añade resistencia e inercia al cambiar de dirección durante el temblor (pérdida de tracción)")]
    [SerializeField] private bool enableSluggishInertia = true;

#if ENABLE_INPUT_SYSTEM
    [Header("Teclas de Movimiento Horizontal (PC)")]
    [Tooltip("Tecla para desplazarse hacia adelante")]
    [SerializeField] private Key forwardKey = Key.W;

    [Tooltip("Tecla para desplazarse hacia atrás")]
    [SerializeField] private Key backwardKey = Key.S;

    [Tooltip("Tecla para desplazarse hacia la izquierda")]
    [SerializeField] private Key leftKey = Key.A;

    [Tooltip("Tecla para desplazarse hacia la derecha")]
    [SerializeField] private Key rightKey = Key.D;

    [Header("Acciones Verticales (Salto y Agachado)")]
    [Tooltip("Tecla para saltar (Up Key)")]
    [SerializeField] private Key upKey = Key.Space;

    [Tooltip("Tecla para agacharse (Down Key - solo baja una sola vez)")]
    [SerializeField] private Key downKey = Key.LeftCtrl;

    [Header("Aceleración")]
    [Tooltip("Tecla para acelerar / sprint")]
    [SerializeField] private Key sprintKey = Key.LeftShift;
#else
    [Header("Teclas de Movimiento Horizontal (PC)")]
    [Tooltip("Tecla para desplazarse hacia adelante")]
    [SerializeField] private KeyCode forwardKey = KeyCode.W;

    [Tooltip("Tecla para desplazarse hacia atrás")]
    [SerializeField] private KeyCode backwardKey = KeyCode.S;

    [Tooltip("Tecla para desplazarse hacia la izquierda")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;

    [Tooltip("Tecla para desplazarse hacia la derecha")]
    [SerializeField] private KeyCode rightKey = KeyCode.D;

    [Header("Acciones Verticales (Salto y Agachado)")]
    [Tooltip("Tecla para saltar (Up Key)")]
    [SerializeField] private KeyCode upKey = KeyCode.Space;

    [Tooltip("Tecla para agacharse (Down Key - solo baja una sola vez)")]
    [SerializeField] private KeyCode downKey = KeyCode.LeftControl; // (KeyCode.LeftCtrl no existe: error de compilación con el Input Manager clásico)

    [Header("Aceleración")]
    [Tooltip("Tecla para acelerar / sprint")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
#endif

    [Header("--- CONFIGURACIÓN DE CONTROL TÁCTIL ---")]
    [Tooltip("Muestra la interfaz en pantalla para control táctil (Joystick virtual y botones de salto/agacho)")]
    [SerializeField] private bool mostrarUITactil = true;

    [Tooltip("Sensibilidad de giro al arrastrar el dedo en la mitad derecha de la pantalla")]
    [SerializeField] private float sensibilidadTactilGiro = 0.16f;

    [Tooltip("Radio máximo en píxeles del joystick virtual táctil")]
    [SerializeField] private float radioJoystickTactil = 75f;

    [Tooltip("Permite simular toques con el cursor del mouse dentro del editor para pruebas")]
    [SerializeField] private bool simularTactilConMouseEnEditor = true;

    [Tooltip("Invertir el eje vertical al girar con el dedo en pantalla")]
    [SerializeField] private bool invertirEjeYTactil = false;

    [Header("--- CONFIGURACIÓN DE CONTROL VR ---")]
    [Tooltip("Velocidad de giro continuo horizontal con el stick derecho en VR (grados por segundo)")]
    [SerializeField] private float vrVelocidadGiro = 65f;

    [Tooltip("Activa Snap Turn (giro instantáneo por pasos fijos para evitar mareos en VR)")]
    [SerializeField] private bool vrSnapTurn = false;

    [Tooltip("Ángulo de giro en grados para cada paso de Snap Turn (ej. 45 grados)")]
    [SerializeField] private float vrSnapAngulo = 45f;

    [Tooltip("Zona muerta para los sticks analógicos de los mandos VR")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float vrZonaMuertaStick = 0.15f;

    [Tooltip("Multiplicador de velocidad de movimiento en VR")]
    [SerializeField] private float vrMultiplicadorVelocidad = 1f;

    public enum VROrientationMode
    {
        [Tooltip("La dirección de avance sigue la mirada del visor (CenterEyeAnchor). Estándar en la mayoría de juegos VR.")]
        Mirada_Head = 0,

        [Tooltip("La dirección de avance sigue la dirección donde apunta el mando de control izquierdo (Controller Tracking).")]
        Mando_Controller = 1
    }

    [Header("--- INTEGRACIÓN META XR BUILDING BLOCKS ---")]
    [Tooltip("Modo de orientación al caminar en VR: hacia donde mira la cabeza (Head) o hacia donde apunta el mando izquierdo (Controller)")]
    [SerializeField] private VROrientationMode modoOrientacionVR = VROrientationMode.Mirada_Head;

    [Tooltip("Transform de la cabeza/visor (CenterEyeAnchor). Si se deja vacío, se detecta automáticamente")]
    [SerializeField] private Transform vrCenterEyeAnchor;

    [Tooltip("Transform del mando izquierdo (Building Block Controller Tracking Left). Si se deja vacío, se detecta automáticamente")]
    [SerializeField] private Transform vrLeftControllerAnchor;

    [Tooltip("Transform del mando derecho (Building Block Controller Tracking Right). Si se deja vacío, se detecta automáticamente")]
    [SerializeField] private Transform vrRightControllerAnchor;

    [Tooltip("En VR con seguimiento Floor Level (a nivel de piso), ajusta automáticamente la base del rig a 0m sobre el suelo real")]
    [SerializeField] private bool autoAjustarAlturaSueloVR = true;

    public VROrientationMode ModoOrientacionVR { get => modoOrientacionVR; set => modoOrientacionVR = value; }
    public Transform VRCenterEyeAnchor { get => vrCenterEyeAnchor; set => vrCenterEyeAnchor = value; }
    public Transform VRLeftControllerAnchor { get => vrLeftControllerAnchor; set => vrLeftControllerAnchor = value; }
    public Transform VRRightControllerAnchor { get => vrRightControllerAnchor; set => vrRightControllerAnchor = value; }
    public bool AutoAjustarAlturaSueloVR { get => autoAjustarAlturaSueloVR; set => autoAjustarAlturaSueloVR = value; }

    [Header("Colisionador Físico de la Cámara")]
    [Tooltip("Garantiza automáticamente que la cámara tenga un SphereCollider en su GameObject")]
    [SerializeField] private bool asegurarColisionador = true;

    [Tooltip("Define si el colisionador de la cámara actúa como Trigger (recomendado para detectar objetos sin interferir con las físicas del movimiento)")]
    [SerializeField] private bool colisionadorEsTrigger = true;

    [Header("Control de Distancia y Altura sobre el Piso")]
    [Tooltip("Distancia que la cámara debe mantener sobre el piso (en metros). Permite desde alturas de ojos normales (1.65m) hasta vistas elevadas (3m, 5m), dron o aéreas (10m - 100m).")]
    [Range(0.2f, 100f)]
    [SerializeField] private float distanciaAlPiso = 2.5f;

    [Tooltip("Permite ajustar la distancia al piso en tiempo real durante Play Mode con las teclas PageUp / PageDown")]
    [SerializeField] private bool permitirAjusteDinamico = true;

    [Tooltip("Velocidad de cambio de altura al pulsar teclas de ajuste (metros/segundo)")]
    [SerializeField] private float velocidadCambioAltura = 4f;

    [Tooltip("Distancia mínima de seguridad absoluta respecto al piso para que la cámara NUNCA lo penetre")]
    [Range(0.1f, 1.2f)]
    [SerializeField] private float distanciaMinimaSeguridad = 0.45f;

    [Tooltip("Velocidad de ajuste suave de la altura al caminar sobre desniveles, rampas o lomas")]
    [Range(1f, 30f)]
    [SerializeField] private float suavizadoAltura = 14f;

    [Tooltip("Radio de la esfera de detección del suelo hacia abajo (SphereCast para mayor estabilidad en bordes y desniveles)")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float radioDeteccionSuelo = 0.25f;

    public float DistanciaAlPiso
    {
        get => distanciaAlPiso;
        set => distanciaAlPiso = Mathf.Clamp(value, distanciaMinimaSeguridad, 150f);
    }

    [Header("Detección de Colisiones (No Atravesar Paredes u Objetos)")]
    [Tooltip("Activa la detección de colisiones para no atravesar muros, columnas u objetos")]
    [SerializeField] private bool enableCollision = true;

    [Tooltip("Radio de la cámara para colisiones (evita que la pantalla se meta dentro de los objetos)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float collisionRadius = 0.35f;

    [Tooltip("Capas de la física con las que la cámara colisiona (por defecto Everything)")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Tooltip("Margen de seguridad para evitar atascarse en bordes")]
    [SerializeField] private float skinWidth = 0.03f;

    [Tooltip("Dibuja una esfera en la vista de escena (Scene) para visualizar el volumen de colisión de la cámara")]
    [SerializeField] private bool showCollisionGizmo = true;

    [Tooltip("Permite que la cámara se deslice a lo largo de los muros al chocar. Desactivado para frenar en seco sin deslizamiento.")]
    [SerializeField] private bool slideAlongWalls = false;

    [Header("Configuración de Salto y Gravedad")]
    [Tooltip("Fuerza o impulso vertical del salto")]
    [SerializeField] private float jumpForce = 5.5f;

    [Tooltip("Activa la gravedad física continua (la cámara cae al estar en el aire, tras saltar o al caminar sobre precipicios)")]
    [SerializeField] private bool activarGravedad = true;

    [Tooltip("Fuerza de aceleración de la gravedad que atrae la cámara hacia el suelo (m/s²)")]
    [SerializeField] private float gravity = 18f;

    [Tooltip("Velocidad máxima terminal de caída bajo gravedad")]
    [SerializeField] private float velocidadMaximaCaida = 35f;

    [Tooltip("Altura máxima de escalón o desnivel para descender pendientes suaves manteniéndose pegado al piso")]
    [SerializeField] private float alturaMaximaEscalon = 0.5f;

    [Tooltip("Altura base del mundo/escenario (Y = 0 por defecto). La cámara jamás caerá por debajo de este nivel incluso si falta el colisionador en el suelo.")]
    [SerializeField] private float nivelPisoBaseMundo = 0f;

    [Header("Configuración de Agachado (Down Key / Botón Agacho)")]
    [Tooltip("Distancia en metros que desciende la cámara al agacharse (baja una sola vez y se detiene)")]
    [SerializeField] private float crouchDistance = 0.8f;

    [Tooltip("Velocidad de transición suave al agacharse o levantarse")]
    [SerializeField] private float crouchTransitionSpeed = 8f;

    [Tooltip("Si es true, presionar la tecla o botón alterna entre agacharse y levantarse (Toggle). Si es false, requiere mantener pulsado.")]
    [SerializeField] private bool toggleCrouch = false;

    [Header("Rotación con Mouse (PC)")]
    [Tooltip("Permite girar la cámara usando el mouse")]
    [SerializeField] private bool enableMouseLook = true;

    [Tooltip("Sensibilidad de giro")]
    [SerializeField] private float mouseSensitivity = 2f;

    [Tooltip("Si está activo, requiere mantener presionado el clic derecho para girar la cámara en PC")]
    [SerializeField] private bool requireRightClickToLook = true;

    // Estados internos de rotación y movimiento
    private float pitch = 0f;
    private float yaw = 0f;

    private float baseGroundY = 0f;
    private float currentVerticalY = 0f;

    private bool isCrouching = false;
    private float currentCrouchOffset = 0f;

    private bool isGrounded = true;
    private float verticalVelocity = 0f;
    private float ultimoSueloValidoY = 0f;

    private float shakeEffectWeight = 0f;
    private Vector3 currentHorizontalVelocity = Vector3.zero;
    private float lastStumbleValue = 0f;

    // Estado interno para control táctil
    private bool touchMovementActive = false;
    private int touchMovementFingerId = -1;
    private Vector2 touchMovementOrigin = Vector2.zero;
    private Vector2 currentTouchMovementVector = Vector2.zero;

    private bool touchLookActive = false;
    private int touchLookFingerId = -1;
    private Vector2 currentTouchLookDelta = Vector2.zero;
    private Vector2 touchLookPreviousPos = Vector2.zero;

    private bool touchJumpTriggered = false;
    private bool touchCrouchTriggered = false;
    private bool touchSprintActive = false;

    // Simulación táctil en editor
    private bool mouseSimulatingMove = false;
    private bool mouseSimulatingLook = false;

    // Estado interno para VR
    private readonly List<UnityEngine.XR.InputDevice> leftControllers = new List<UnityEngine.XR.InputDevice>();
    private readonly List<UnityEngine.XR.InputDevice> rightControllers = new List<UnityEngine.XR.InputDevice>();
    private bool vrJumpLastFrame = false;
    private bool vrCrouchLastFrame = false;
    private bool vrSnapTurnReset = true;
    private bool teleportUsingRightHand = true;

    // UI Styles
    private GUIStyle touchButtonStyle;
    private GUIStyle touchButtonActiveStyle;
    private GUIStyle touchHintStyle;
    private Texture2D circleTexture;

    private SphereCollider cameraSphereCollider;

    // Estado y objetos para el selector y teletransporte
    public static CameraMovement Instance { get; private set; }
    private bool isMovementPausedByMenu = false;
    private bool isAimingTeleport = false;
    private bool teleportHitValid = false;
    private Vector3 teleportHitPoint = Vector3.zero;
    private Vector3 teleportHitNormal = Vector3.up;
    private LineRenderer teleportLineRenderer;
    private GameObject teleportReticleObj;
    private AudioSource teleportAudioSource;
    private AudioClip teleportSoundClip;
    private Material teleportLineMaterial;
    private Material teleportReticleMaterial;

    // --- Simulación de paso fijo (FixedUpdate) + interpolación visual (LateUpdate) ---
    private bool simInicializada = false;
    private Vector3 simPosition;
    private Vector3 prevSimPosition;
    private Vector3 ultimaPosicionEscrita;
    private bool hayPosicionEscrita = false;
    private float prevStumbleValue = 0f;

    // --- Entradas leídas en Update y consumidas en FixedUpdate ---
    private Vector2 inputMovimiento = Vector2.zero;
    private bool inputSprint = false;
    private bool inputAgacharMantenido = false;
    private bool bufferSalto = false;
    private bool bufferAlternarAgachado = false;
    private bool agachadoTactilActivo = false;
    private float inputAjusteAltura = 0f;
    private float multiplicadorVelocidadVR = 1f;
    private int framesIgnorarRaton = 0;
    private bool estabaBloqueado = true;

    // Buffers reutilizables: las consultas físicas ya no asignan memoria cada frame (evita picos de GC = micro-tirones)
    private readonly RaycastHit[] bufferHits = new RaycastHit[32];
    private readonly Collider[] bufferOverlaps = new Collider[32];
    private static readonly Dictionary<Collider, bool> cacheRecogible = new Dictionary<Collider, bool>();
    private static readonly Dictionary<Collider, bool> cachePiso = new Dictionary<Collider, bool>();

    /// <summary>Posición física simulada (sin sacudida del sismo).</summary>
    public Vector3 SimulatedPosition => simPosition;

    /// <summary>True si el menú está abierto, el movimiento está pausado o el jugador ha muerto.</summary>
    public bool IsInputBlocked => isMovementPausedByMenu || MovementSystemSelector.IsGameplayBlocked;

    /// <summary>True solo si hay un visor VR real. Antes bastaba con que el modo VR estuviera permitido,
    /// y en PC (modo Universal) la cámara perdía el giro vertical.</summary>
    public bool IsVRPresent =>
        IsVRActive && (UnityEngine.XR.XRSettings.isDeviceActive ||
                       (vrCenterEyeAnchor != null && vrCenterEyeAnchor != transform && vrCenterEyeAnchor.IsChildOf(transform)));

    private void Awake()
    {
        // ANTES: en Meta Quest (Android) se forzaba el modo táctil y se desactivaban los controles VR
        if (UIVR.Activo)
        {
            modoControl = ControlSchemeMode.VR;
            usarControlVR = true;
            usarControlTactil = false;
            mostrarUITactil = false;
        }
        else if (Application.isMobilePlatform)
        {
            modoControl = ControlSchemeMode.Tactil;
            usarControlTactil = true;
            mostrarUITactil = true;
        }
        else if (Application.isEditor && simularTactilConMouseEnEditor)
        {
            usarControlTactil = true;
            mostrarUITactil = true;
        }

        if (Instance == null) Instance = this;
        AutoDetectVRBuildingBlocks();
        AsegurarColisionadorCamara();
        EnsureTeleportVisuals();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AsegurarColisionadorCamara()
    {
        if (!asegurarColisionador) return;

        cameraSphereCollider = GetComponent<SphereCollider>();
        if (cameraSphereCollider == null)
        {
            cameraSphereCollider = gameObject.AddComponent<SphereCollider>();
        }

        cameraSphereCollider.radius = collisionRadius;
        cameraSphereCollider.isTrigger = colisionadorEsTrigger;
        cameraSphereCollider.center = Vector3.zero;

        // Rigidbody SIEMPRE cinemático: este controlador mueve el transform directamente y un Rigidbody
        // dinámico pelearía con él (empujones, temblores y caídas inesperadas).
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;
    }

    private void Start()
    {
        Vector3 currentEuler = transform.eulerAngles;
        pitch = currentEuler.x;
        if (pitch > 180f) pitch -= 360f;
        yaw = currentEuler.y;

        AutoDetectVRBuildingBlocks();
        AsegurarColisionadorCamara();

        simPosition = transform.position;
        currentVerticalY = transform.position.y;
        baseGroundY = transform.position.y;
        ultimoSueloValidoY = nivelPisoBaseMundo;

        // Comprobar y alinear con el suelo debajo
        if (DetectarPisoDebajo(out float sueloInicial))
        {
            ultimoSueloValidoY = sueloInicial;
            float targetY = sueloInicial + GetDistanciaAlPisoEfectiva();
            // Si la cámara fue colocada en el editor a una altura razonable o cerca del piso, posicionarla de inmediato
            if (transform.position.y < targetY + 2.5f)
            {
                currentVerticalY = targetY;
                baseGroundY = targetY;
                transform.position = new Vector3(transform.position.x, targetY, transform.position.z);
                isGrounded = true;
                verticalVelocity = 0f;
            }
            else
            {
                isGrounded = false;
            }
        }
        else
        {
            isGrounded = false;
        }

        InicializarSimulacion();

        // Asegurar interacción y menú SIN duplicarlos si ya existen en otro objeto de la escena
        if (PlayerInteraction.Instance == null && FindAnyObjectByType<PlayerInteraction>() == null)
        {
            gameObject.AddComponent<PlayerInteraction>();
        }
        if (MovementSystemSelector.Instance == null && FindAnyObjectByType<MovementSystemSelector>() == null)
        {
            gameObject.AddComponent<MovementSystemSelector>();
        }
    }

    private void InicializarSimulacion()
    {
        simPosition = transform.position;
        prevSimPosition = simPosition;
        ultimaPosicionEscrita = transform.position;
        hayPosicionEscrita = true;
        simInicializada = true;
    }

    // =========================================================================
    // CICLO DE UNITY
    //  - Update      -> lectura de entradas (teclado, ratón, táctil, VR) y rotación de la mirada.
    //  - FixedUpdate -> simulación del desplazamiento (colisiones, gravedad, salto, agachado) a paso fijo.
    //  - LateUpdate  -> pose final de la cámara: posición interpolada + sacudida del sismo (aditiva).
    // =========================================================================

    private void Update()
    {
        if (IsInputBlocked)
        {
            if (!estabaBloqueado) LimpiarEntradas();
            estabaBloqueado = true;
            return;
        }

        if (estabaBloqueado)
        {
            // Al cerrar el menú el cursor se re-centra y el primer delta del ratón es enorme: se ignora.
            framesIgnorarRaton = 2;
            estabaBloqueado = false;
        }

        UpdateVRDevices();
        ProcessTouchInput();
        HandleTeleportation();
        LeerEntradasMovimiento();
        LeerEntradaAjusteAltura();
        HandleRotation();
    }

    private void LimpiarEntradas()
    {
        inputMovimiento = Vector2.zero;
        inputSprint = false;
        inputAgacharMantenido = false;
        bufferSalto = false;
        bufferAlternarAgachado = false;
        inputAjusteAltura = 0f;
        touchJumpTriggered = false;
        touchCrouchTriggered = false;
        touchMovementActive = false;
        touchMovementFingerId = -1;
        touchLookActive = false;
        touchLookFingerId = -1;
        mouseSimulatingMove = false;
        mouseSimulatingLook = false;
        currentTouchMovementVector = Vector2.zero;
        currentTouchLookDelta = Vector2.zero;
        if (isAimingTeleport) CancelTeleportAiming();
    }

    private void FixedUpdate()
    {
        if (!simInicializada) InicializarSimulacion();

        SincronizarMovimientoExterno();
        prevSimPosition = simPosition;
        prevStumbleValue = lastStumbleValue;

        float dt = Time.fixedDeltaTime;

        if (IsInputBlocked)
        {
            // Sin entradas mientras hay menú o Game Over (la gravedad y la inercia siguen resolviéndose)
            inputMovimiento = Vector2.zero;
            inputSprint = false;
            bufferSalto = false;
            bufferAlternarAgachado = false;
            inputAjusteAltura = 0f;
        }

        ActualizarAgachado(dt);
        AplicarSalto();
        HandleHorizontalMovement(dt, out Vector3 horizontalMove);

        if (enableCollision)
        {
            // 1. Colisión horizontal: choca contra muros y pilares sin atravesarlos
            horizontalMove = ResolveHorizontalCollision(horizontalMove);
            simPosition += horizontalMove;

            // 2. Resolver penetración horizontal sin elevar jamás la cámara
            ResolveHorizontalPenetration();
        }
        else
        {
            simPosition += horizontalMove;
        }

        // 3. Control de altura, gravedad y salto
        HandleDynamicHeightAdjustment(dt);
        UpdateVerticalPosition(dt);
    }

    /// <summary>
    /// Si otro script movió el transform (respawn, plataforma, recentrado VR...), se traslada la simulación
    /// la misma cantidad en lugar de "devolver" la cámara a la posición anterior.
    /// </summary>
    private void SincronizarMovimientoExterno()
    {
        if (!hayPosicionEscrita) return;

        Vector3 delta = transform.position - ultimaPosicionEscrita;
        if (delta.sqrMagnitude > 0.000001f)
        {
            simPosition += delta;
            prevSimPosition += delta;
            currentVerticalY += delta.y;
            baseGroundY = currentVerticalY;
            ultimaPosicionEscrita = transform.position;
        }
    }

    private void LateUpdate()
    {
        if (!simInicializada) InicializarSimulacion();
        SincronizarMovimientoExterno();

        // Interpolación entre los dos últimos pasos físicos: movimiento fluido a cualquier tasa de FPS
        float alpha = Time.fixedDeltaTime > 0f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;
        Vector3 renderPos = Vector3.Lerp(prevSimPosition, simPosition, alpha);
        float stumble = Mathf.Lerp(prevStumbleValue, lastStumbleValue, alpha);

        Quaternion baseRot = CalcularRotacionBase(stumble);

        // Sacudida ADITIVA: CameraShake solo calcula el desplazamiento; aquí se suma sobre la pose ya resuelta.
        // Al terminar el sismo el offset vale exactamente cero, así que la cámara vuelve a su sitio sin derivas.
        Vector3 shakeOffset = Vector3.zero;
        Quaternion shakeRot = Quaternion.identity;
        CameraShake shake = CameraShake.Instance;
        if (shake != null)
        {
            shakeOffset = Quaternion.Euler(0f, yaw, 0f) * shake.CurrentShakeOffset;
            if (!IsVRPresent) shakeRot = Quaternion.Euler(shake.CurrentShakeRotation);
        }

        transform.SetPositionAndRotation(renderPos + shakeOffset, baseRot * shakeRot);
        ultimaPosicionEscrita = transform.position;
        hayPosicionEscrita = true;
    }

    private Quaternion CalcularRotacionBase(float stumble)
    {
        if (IsVRPresent)
        {
            // En VR el visor maneja físicamente el cabeceo (pitch) y la inclinación (roll):
            // al Rig solo se le aplica Yaw para evitar desorientación y mareos.
            return Quaternion.Euler(0f, yaw, 0f);
        }

        float roll = (enableStumble && stumbleCameraTilt > 0f) ? (-stumble * stumbleCameraTilt) : 0f;
        return Quaternion.Euler(pitch, yaw, roll);
    }

    /// <summary>
    /// Lee todas las entradas de movimiento en Update. Los "pulsados este frame" (salto, agacharse)
    /// se guardan en un buffer para que FixedUpdate no los pierda ni los aplique dos veces.
    /// </summary>
    private void LeerEntradasMovimiento()
    {
        Vector2 mov = Vector2.zero;
        bool sprint = false;
        bool agacharMantenido = false;
        multiplicadorVelocidadVR = 1f;

        // --- ENTRADA 1: TECLADO (PC) ---
        if (IsKeyboardActive)
        {
            if (IsKeyPressed(forwardKey)) mov.y += 1f;
            if (IsKeyPressed(backwardKey)) mov.y -= 1f;
            if (IsKeyPressed(leftKey)) mov.x -= 1f;
            if (IsKeyPressed(rightKey)) mov.x += 1f;
            if (IsKeyPressed(sprintKey)) sprint = true;

            if (toggleCrouch)
            {
                if (IsKeyDown(downKey)) bufferAlternarAgachado = true;
            }
            else if (IsKeyPressed(downKey))
            {
                agacharMantenido = true;
            }

            if (IsKeyDown(upKey)) bufferSalto = true;
        }

        // --- ENTRADA 2: CONTROL TÁCTIL (JOYSTICK VIRTUAL) ---
        if (IsTouchActive)
        {
            if (currentTouchMovementVector.sqrMagnitude > 0.001f)
            {
                mov += currentTouchMovementVector;
                if (touchSprintActive) sprint = true;
            }

            if (touchJumpTriggered)
            {
                bufferSalto = true;
                touchJumpTriggered = false;
            }

            if (touchCrouchTriggered)
            {
                touchCrouchTriggered = false;
                // Un botón táctil no se puede "mantener": en modo mantener funciona como interruptor propio
                if (toggleCrouch) bufferAlternarAgachado = true;
                else agachadoTactilActivo = !agachadoTactilActivo;
            }
        }

        // --- ENTRADA 3: VR (STICK IZQUIERDO Y BOTONES) ---
        if (IsVRActive)
        {
            Vector2 vrMove = GetVRMovementVector(out bool vrSprint);
            if (vrMove.sqrMagnitude > 0.001f)
            {
                mov += vrMove;
                multiplicadorVelocidadVR = vrMultiplicadorVelocidad;
            }
            if (vrSprint) sprint = true;

            if (GetVRJumpTriggered()) bufferSalto = true;

            bool vrAgacharPulsado = GetVRCrouchTriggered();
            if (toggleCrouch)
            {
                if (vrAgacharPulsado) bufferAlternarAgachado = true;
            }
            else if (GetVRCrouchHeld())
            {
                agacharMantenido = true;
            }
        }

        // Normaliza la diagonal (W + D ya no es 1.41x más rápida) sin perder la inclinación parcial de los joysticks
        inputMovimiento = Vector2.ClampMagnitude(mov, 1f);
        inputSprint = sprint;
        inputAgacharMantenido = agacharMantenido || agachadoTactilActivo;
    }

    private void LeerEntradaAjusteAltura()
    {
        inputAjusteAltura = 0f;
        if (!permitirAjusteDinamico) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.pageUpKey.isPressed) inputAjusteAltura = 1f;
            else if (Keyboard.current.pageDownKey.isPressed) inputAjusteAltura = -1f;
        }
#else
        if (Input.GetKey(KeyCode.PageUp)) inputAjusteAltura = 1f;
        else if (Input.GetKey(KeyCode.PageDown)) inputAjusteAltura = -1f;
#endif
    }

    private void HandleDynamicHeightAdjustment(float dt)
    {
        if (!permitirAjusteDinamico || Mathf.Approximately(inputAjusteAltura, 0f)) return;
        distanciaAlPiso = Mathf.Clamp(distanciaAlPiso + inputAjusteAltura * velocidadCambioAltura * dt, distanciaMinimaSeguridad, 100f);
    }

    private void UpdateVRDevices()
    {
        if (!IsVRActive) return;

        leftControllers.Clear();
        rightControllers.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, leftControllers);
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightControllers);
    }

    private void HandleHorizontalMovement(float dt, out Vector3 horizontalMove)
    {
        // Peso del temblor: transición suave entre caminar normal y caminar durante el sismo
        bool isShaking = CameraShake.Instance != null && CameraShake.Instance.IsShaking;
        shakeEffectWeight = Mathf.MoveTowards(shakeEffectWeight, isShaking ? 1f : 0f, dt * 3.5f);

        // Teletransporte puro o apuntando el arco: sin caminar continuo
        if (tipoLocomocion == LocomotionMode.Teletransporte || isAimingTeleport)
        {
            horizontalMove = Vector3.zero;
            currentHorizontalVelocity = Vector3.zero;
            lastStumbleValue = Mathf.MoveTowards(lastStumbleValue, 0f, dt * 5f);
            return;
        }

        // Dirección: en PC sale del yaw de la mirada; en VR, de la cabeza o del mando izquierdo
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;

        if (IsVRPresent)
        {
            Transform orientationSource = null;
            if (modoOrientacionVR == VROrientationMode.Mando_Controller && vrLeftControllerAnchor != null) orientationSource = vrLeftControllerAnchor;
            else if (vrCenterEyeAnchor != null) orientationSource = vrCenterEyeAnchor;

            if (orientationSource != null)
            {
                Vector3 f = orientationSource.forward; f.y = 0f;
                Vector3 r = orientationSource.right; r.y = 0f;
                if (f.sqrMagnitude > 0.001f) forward = f.normalized;
                if (r.sqrMagnitude > 0.001f) right = r.normalized;
            }
        }

        // 1. Tambaleo lateral irregular durante el temblor
        Vector3 stumbleForce = Vector3.zero;
        if (enableStumble && shakeEffectWeight > 0.01f)
        {
            float noisePrimary = (Mathf.PerlinNoise(Time.time * 6f, 0.2f) * 2f - 1f);
            float noiseSecondary = (Mathf.PerlinNoise(Time.time * 12f, 5.7f) * 2f - 1f) * 0.5f;
            lastStumbleValue = Mathf.Clamp(noisePrimary + noiseSecondary, -1.5f, 1.5f) * (stumbleIntensity * shakeEffectWeight);
            stumbleForce = right * (lastStumbleValue * (moveSpeed * 0.85f));
        }
        else
        {
            lastStumbleValue = Mathf.MoveTowards(lastStumbleValue, 0f, dt * 5f);
        }

        Vector3 inputDir = new Vector3(inputMovimiento.x, 0f, inputMovimiento.y);
        float magnitudEntrada = Mathf.Clamp01(inputDir.magnitude);

        if (magnitudEntrada > 0.01f)
        {
            Vector3 dir = inputDir / inputDir.magnitude;

            // Desvío angular de la dirección de avance durante el sismo
            if (enableStumble && shakeEffectWeight > 0.01f)
            {
                dir += Vector3.right * (lastStumbleValue * 1.35f);
                dir.Normalize();
            }

            float currentSpeed = moveSpeed * magnitudEntrada * multiplicadorVelocidadVR;
            if (shakeEffectWeight > 0.001f)
            {
                currentSpeed *= Mathf.Lerp(1f, shakeSpeedMultiplier, shakeEffectWeight);
            }

            if (isCrouching)
            {
                currentSpeed *= crouchSpeedMultiplier;
            }
            else if (inputSprint)
            {
                float sprint = disableSprintDuringShake
                    ? Mathf.Lerp(sprintMultiplier, 1f, shakeEffectWeight)
                    : sprintMultiplier;
                currentSpeed *= sprint;
            }

            Vector3 targetVelocity = (forward * dir.z + right * dir.x) * currentSpeed + stumbleForce;

            // Inercia y pérdida de tracción durante el temblor
            if (enableSluggishInertia)
            {
                float accelRate = Mathf.Lerp(25f, 4f, shakeEffectWeight);
                currentHorizontalVelocity = Vector3.MoveTowards(currentHorizontalVelocity, targetVelocity, accelRate * dt);
            }
            else
            {
                currentHorizontalVelocity = targetVelocity;
            }
        }
        else
        {
            Vector3 targetVelocity = stumbleForce * 0.45f;

            if (enableSluggishInertia)
            {
                float decelRate = Mathf.Lerp(30f, 6f, shakeEffectWeight);
                currentHorizontalVelocity = Vector3.MoveTowards(currentHorizontalVelocity, targetVelocity, decelRate * dt);
            }
            else
            {
                currentHorizontalVelocity = targetVelocity;
            }
        }

        horizontalMove = currentHorizontalVelocity * dt;
        horizontalMove.y = 0f;
    }

    private void ActualizarAgachado(float dt)
    {
        if (toggleCrouch)
        {
            if (bufferAlternarAgachado) isCrouching = !isCrouching;
        }
        else
        {
            isCrouching = inputAgacharMantenido;
        }
        bufferAlternarAgachado = false;

        float targetCrouch = isCrouching ? -crouchDistance : 0f;
        currentCrouchOffset = Mathf.MoveTowards(currentCrouchOffset, targetCrouch, crouchTransitionSpeed * dt);
    }

    private void AplicarSalto()
    {
        if (!bufferSalto) return;
        bufferSalto = false;

        if (!isGrounded) return;

        float currentJumpForce = jumpForce;
        if (shakeEffectWeight > 0.001f)
        {
            currentJumpForce *= Mathf.Lerp(1f, shakeJumpMultiplier, shakeEffectWeight);
        }

        verticalVelocity = currentJumpForce;
        isGrounded = false;
        isCrouching = false;
        agachadoTactilActivo = false;
    }

    /// <summary>
    /// Actualiza yaw/pitch en Update (cada frame de render). La rotación se aplica al transform en LateUpdate.
    /// </summary>
    private void HandleRotation()
    {
        // 1. Giro con Mouse (PC): el delta ya es "por frame", NO se multiplica por deltaTime
        if (IsKeyboardActive && enableMouseLook && !mouseSimulatingLook && !mouseSimulatingMove)
        {
            ReadMouseLook(out float mouseYaw, out float mousePitch);
            if (framesIgnorarRaton <= 0)
            {
                yaw += mouseYaw;
                pitch += mousePitch;
            }
        }
        if (framesIgnorarRaton > 0) framesIgnorarRaton--;

        // 2. Giro Táctil (arrastrar el dedo en la mitad derecha de la pantalla)
        if (IsTouchActive)
        {
            ReadTouchLook(out float touchYaw, out float touchPitch);
            yaw += touchYaw;
            pitch += touchPitch;
        }

        // 3. Giro VR (stick derecho)
        if (IsVRActive)
        {
            ReadVRLook(out float vrYawDelta);
            yaw += vrYawDelta;
        }

        pitch = Mathf.Clamp(pitch, -89f, 89f);
        if (yaw > 720f || yaw < -720f) yaw = Mathf.Repeat(yaw, 360f);
    }

    private void ReadMouseLook(out float mouseYaw, out float mousePitch)
    {
        mouseYaw = 0f;
        mousePitch = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return;
        if (requireRightClickToLook && !Mouse.current.rightButton.isPressed) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * (mouseSensitivity * 0.1f);
        mouseYaw = mouseDelta.x;
        mousePitch = -mouseDelta.y;
#else
        if (requireRightClickToLook && !Input.GetMouseButton(1)) return;

        mouseYaw = Input.GetAxis("Mouse X") * mouseSensitivity;
        mousePitch = -Input.GetAxis("Mouse Y") * mouseSensitivity;
#endif
    }

    private void ReadTouchLook(out float touchYaw, out float touchPitch)
    {
        touchYaw = 0f;
        touchPitch = 0f;

        if (currentTouchLookDelta.sqrMagnitude > 0.0001f)
        {
            touchYaw = currentTouchLookDelta.x * sensibilidadTactilGiro;
            touchPitch = (invertirEjeYTactil ? currentTouchLookDelta.y : -currentTouchLookDelta.y) * sensibilidadTactilGiro;
            currentTouchLookDelta = Vector2.zero;
        }
    }

    private void ReadVRLook(out float vrYawDelta)
    {
        vrYawDelta = 0f;
        Vector2 lookAxis = Vector2.zero;

        // 1. Meta XR OVRInput (Stick secundario en mando derecho)
        try
        {
            Vector2 ovrLook = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);
            if (ovrLook.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
            {
                lookAxis = ovrLook;
            }
        }
        catch { }

        // 2. Fallback a UnityEngine.XR.InputDevices
        if (lookAxis.sqrMagnitude < 0.001f && rightControllers.Count > 0 && rightControllers[0].isValid)
        {
            if (rightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
            {
                if (axis.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
                {
                    lookAxis = axis;
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
        if (lookAxis.sqrMagnitude < 0.001f && Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
            {
                lookAxis = stick;
            }
        }
#endif

        if (vrSnapTurn)
        {
            if (lookAxis.x > 0.65f)
            {
                if (vrSnapTurnReset)
                {
                    vrYawDelta = vrSnapAngulo;
                    vrSnapTurnReset = false;
                }
            }
            else if (lookAxis.x < -0.65f)
            {
                if (vrSnapTurnReset)
                {
                    vrYawDelta = -vrSnapAngulo;
                    vrSnapTurnReset = false;
                }
            }
            else if (Mathf.Abs(lookAxis.x) < 0.35f)
            {
                vrSnapTurnReset = true;
            }
        }
        else
        {
            vrYawDelta = lookAxis.x * vrVelocidadGiro * Time.deltaTime;
        }
    }

    private Vector2 GetVRMovementVector(out bool vrSprint)
    {
        vrSprint = false;
        Vector2 move = Vector2.zero;

        // 1. Meta XR OVRInput (Stick primario en mando izquierdo)
        try
        {
            Vector2 ovrStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (ovrStick.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
            {
                move = ovrStick;
            }
            if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch))
            {
                vrSprint = true;
            }
        }
        catch { }

        // 2. Fallback a UnityEngine.XR.InputDevices
        if (move.sqrMagnitude < 0.001f && leftControllers.Count > 0 && leftControllers[0].isValid)
        {
            if (leftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
            {
                if (axis.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
                {
                    move = axis;
                }
            }
            if (leftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool click))
            {
                vrSprint = click;
            }
        }

#if ENABLE_INPUT_SYSTEM
        if (move.sqrMagnitude < 0.001f && Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > vrZonaMuertaStick * vrZonaMuertaStick)
            {
                move = stick;
            }
            if (Gamepad.current.leftStickButton.isPressed)
            {
                vrSprint = true;
            }
        }
#endif

        return move;
    }

    private bool GetVRJumpTriggered()
    {
        bool pressed = false;

        // 1. Meta XR OVRInput (Botón A en mando derecho)
        try
        {
            if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch)) pressed = true;
        }
        catch { }

        // 2. Fallback a UnityEngine.XR.InputDevices
        if (!pressed && rightControllers.Count > 0 && rightControllers[0].isValid)
        {
            if (rightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btnA) && btnA) pressed = true;
        }

#if ENABLE_INPUT_SYSTEM
        if (!pressed && Gamepad.current != null && Gamepad.current.buttonSouth.isPressed)
        {
            pressed = true;
        }
#endif

        bool triggered = pressed && !vrJumpLastFrame;
        vrJumpLastFrame = pressed;
        return triggered;
    }

    private bool GetVRCrouchTriggered()
    {
        bool pressed = GetVRCrouchHeld();
        bool triggered = pressed && !vrCrouchLastFrame;
        vrCrouchLastFrame = pressed;
        return triggered;
    }

    private bool GetVRCrouchHeld()
    {
        // 1. Meta XR OVRInput (Botón B en derecho o Botón X en izquierdo)
        try
        {
            if (OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch) || 
                OVRInput.Get(OVRInput.Button.Three, OVRInput.Controller.LTouch) ||
                OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch))
            {
                return true;
            }
        }
        catch { }

        // 2. Fallback a UnityEngine.XR.InputDevices
        if (rightControllers.Count > 0 && rightControllers[0].isValid)
        {
            if (rightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool btnB) && btnB) return true;
        }
        if (leftControllers.Count > 0 && leftControllers[0].isValid)
        {
            if (leftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btnX) && btnX) return true;
        }

#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null && (Gamepad.current.buttonEast.isPressed || Gamepad.current.buttonNorth.isPressed))
        {
            return true;
        }
#endif

        return false;
    }

    private struct MobileTouchInfo
    {
        public int fingerId;
        public Vector2 screenPos;
        public Vector2 guiPos;
        public bool isDown;
        public bool isHeld;
        public bool isUp;
    }

    private readonly List<MobileTouchInfo> currentFrameTouches = new List<MobileTouchInfo>(10);

    // --- PROCESAMIENTO TÁCTIL ---
    private void ProcessTouchInput()
    {
        if (!IsTouchActive)
        {
            touchMovementActive = false;
            touchMovementFingerId = -1;
            touchLookActive = false;
            touchLookFingerId = -1;
            currentTouchMovementVector = Vector2.zero;
            currentTouchLookDelta = Vector2.zero;
            return;
        }

        currentFrameTouches.Clear();

#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                var phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None) continue;

                Vector2 screenPos = touch.position.ReadValue();
                Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
                int fingerId = touch.touchId.ReadValue();

                bool isDown = (phase == UnityEngine.InputSystem.TouchPhase.Began);
                bool isHeld = (phase == UnityEngine.InputSystem.TouchPhase.Began ||
                               phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                               phase == UnityEngine.InputSystem.TouchPhase.Stationary);
                bool isUp = (phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                             phase == UnityEngine.InputSystem.TouchPhase.Canceled);

                currentFrameTouches.Add(new MobileTouchInfo
                {
                    fingerId = fingerId,
                    screenPos = screenPos,
                    guiPos = guiPos,
                    isDown = isDown,
                    isHeld = isHeld,
                    isUp = isUp
                });
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
        if (currentFrameTouches.Count == 0)
        {
            try
            {
                int count = Input.touchCount;
                for (int i = 0; i < count; i++)
                {
                    Touch t = Input.GetTouch(i);
                    Vector2 screenPos = t.position;
                    Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

                    bool isDown = (t.phase == UnityEngine.TouchPhase.Began);
                    bool isHeld = (t.phase == UnityEngine.TouchPhase.Began ||
                                   t.phase == UnityEngine.TouchPhase.Moved ||
                                   t.phase == UnityEngine.TouchPhase.Stationary);
                    bool isUp = (t.phase == UnityEngine.TouchPhase.Ended ||
                                 t.phase == UnityEngine.TouchPhase.Canceled);

                    currentFrameTouches.Add(new MobileTouchInfo
                    {
                        fingerId = t.fingerId,
                        screenPos = screenPos,
                        guiPos = guiPos,
                        isDown = isDown,
                        isHeld = isHeld,
                        isUp = isUp
                    });
                }
            }
            catch { }
        }
#endif

        bool hadPhysicalTouches = (currentFrameTouches.Count > 0);

        if (hadPhysicalTouches)
        {
            // 1. Si el joystick de movimiento estaba activo, buscar su dedo correspondiente
            if (touchMovementActive)
            {
                bool found = false;
                for (int i = 0; i < currentFrameTouches.Count; i++)
                {
                    var t = currentFrameTouches[i];
                    if (t.fingerId == touchMovementFingerId)
                    {
                        found = true;
                        if (t.isUp)
                        {
                            touchMovementActive = false;
                            touchMovementFingerId = -1;
                            currentTouchMovementVector = Vector2.zero;
                        }
                        else if (t.isHeld)
                        {
                            Vector2 offset = t.guiPos - touchMovementOrigin;
                            float dist = offset.magnitude;
                            if (dist > radioJoystickTactil) offset = offset.normalized * radioJoystickTactil;
                            currentTouchMovementVector = new Vector2(offset.x / radioJoystickTactil, -offset.y / radioJoystickTactil);
                        }
                        break;
                    }
                }
                if (!found)
                {
                    touchMovementActive = false;
                    touchMovementFingerId = -1;
                    currentTouchMovementVector = Vector2.zero;
                }
            }

            // 2. Si la rotación de cámara estaba activa, buscar su dedo correspondiente
            if (touchLookActive)
            {
                bool found = false;
                for (int i = 0; i < currentFrameTouches.Count; i++)
                {
                    var t = currentFrameTouches[i];
                    if (t.fingerId == touchLookFingerId)
                    {
                        found = true;
                        if (t.isUp)
                        {
                            Vector2 posDelta = t.screenPos - touchLookPreviousPos;
                            currentTouchLookDelta += posDelta;
                            touchLookActive = false;
                            touchLookFingerId = -1;
                        }
                        else if (t.isHeld)
                        {
                            Vector2 posDelta = t.screenPos - touchLookPreviousPos;
                            currentTouchLookDelta += posDelta;
                            touchLookPreviousPos = t.screenPos;
                        }
                        break;
                    }
                }
                if (!found)
                {
                    touchLookActive = false;
                    touchLookFingerId = -1;
                }
            }

            // 3. Procesar nuevos toques iniciados en este fotograma (isDown)
            for (int i = 0; i < currentFrameTouches.Count; i++)
            {
                var t = currentFrameTouches[i];
                if (!t.isDown) continue;
                if (t.fingerId == touchMovementFingerId || t.fingerId == touchLookFingerId) continue;

                // Comprobar si se tocan los botones de interfaz de Salto o Agacho
                if (mostrarUITactil)
                {
                    float jumpBtnSize = Mathf.Clamp(Screen.height * 0.12f, 75f, 120f);
                    Rect jumpRect = new Rect(Screen.width - jumpBtnSize - 30f, Screen.height - jumpBtnSize - 40f, jumpBtnSize, jumpBtnSize);
                    float crouchBtnSize = Mathf.Clamp(Screen.height * 0.11f, 70f, 110f);
                    Rect crouchRect = new Rect(Screen.width - jumpBtnSize - crouchBtnSize - 45f, Screen.height - crouchBtnSize - 40f, crouchBtnSize, crouchBtnSize);

                    if (jumpRect.Contains(t.guiPos))
                    {
                        touchJumpTriggered = true;
                        continue;
                    }
                    if (crouchRect.Contains(t.guiPos))
                    {
                        touchCrouchTriggered = true;
                        continue;
                    }
                }

                // Mitad izquierda de la pantalla: Activar Joystick virtual de movimiento dinámico
                if (t.screenPos.x < Screen.width * 0.5f && !touchMovementActive)
                {
                    touchMovementActive = true;
                    touchMovementFingerId = t.fingerId;
                    touchMovementOrigin = t.guiPos;
                    currentTouchMovementVector = Vector2.zero;
                }
                // Mitad derecha de la pantalla: Activar rotación de cámara por arrastre
                else if (t.screenPos.x >= Screen.width * 0.5f && !touchLookActive)
                {
                    touchLookActive = true;
                    touchLookFingerId = t.fingerId;
                    touchLookPreviousPos = t.screenPos;
                    currentTouchLookDelta = Vector2.zero;
                }
            }
        }
        else
        {
            // No hay ningún dedo sobre la pantalla física
            if (touchMovementActive)
            {
                touchMovementActive = false;
                touchMovementFingerId = -1;
                currentTouchMovementVector = Vector2.zero;
            }
            if (touchLookActive)
            {
                touchLookActive = false;
                touchLookFingerId = -1;
                currentTouchLookDelta = Vector2.zero;
            }

#if UNITY_EDITOR
            if (simularTactilConMouseEnEditor)
            {
                SimulateTouchWithMouseInEditor();
            }
#endif
        }
    }

#if UNITY_EDITOR
    private void SimulateTouchWithMouseInEditor()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return;
        bool isLeft = Mouse.current.leftButton.isPressed;
        bool wasDown = Mouse.current.leftButton.wasPressedThisFrame;
        bool wasUp = Mouse.current.leftButton.wasReleasedThisFrame;
        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
#else
        bool isLeft = Input.GetMouseButton(0);
        bool wasDown = Input.GetMouseButtonDown(0);
        bool wasUp = Input.GetMouseButtonUp(0);
        Vector2 mouseScreenPos = Input.mousePosition;
#endif
        Vector2 guiPos = new Vector2(mouseScreenPos.x, Screen.height - mouseScreenPos.y);

        if (wasDown)
        {
            if (mostrarUITactil)
            {
                float jumpBtnSize = 75f;
                Rect jumpRect = new Rect(Screen.width - jumpBtnSize - 30f, Screen.height - jumpBtnSize - 40f, jumpBtnSize, jumpBtnSize);
                float crouchBtnSize = 70f;
                Rect crouchRect = new Rect(Screen.width - jumpBtnSize - crouchBtnSize - 45f, Screen.height - crouchBtnSize - 40f, crouchBtnSize, crouchBtnSize);

                if (jumpRect.Contains(guiPos)) { touchJumpTriggered = true; return; }
                if (crouchRect.Contains(guiPos)) { touchCrouchTriggered = true; return; }
            }

            if (mouseScreenPos.x < Screen.width * 0.5f)
            {
                mouseSimulatingMove = true;
                touchMovementActive = true;
                touchMovementOrigin = guiPos;
                currentTouchMovementVector = Vector2.zero;
            }
            else
            {
                mouseSimulatingLook = true;
                touchLookActive = true;
                touchLookPreviousPos = mouseScreenPos;
                currentTouchLookDelta = Vector2.zero;
            }
        }
        else if (isLeft)
        {
            if (mouseSimulatingMove)
            {
                Vector2 offset = guiPos - touchMovementOrigin;
                float dist = offset.magnitude;
                if (dist > radioJoystickTactil) offset = offset.normalized * radioJoystickTactil;
                currentTouchMovementVector = new Vector2(offset.x / radioJoystickTactil, -offset.y / radioJoystickTactil);
            }
            else if (mouseSimulatingLook)
            {
                Vector2 posDelta = mouseScreenPos - touchLookPreviousPos;
                currentTouchLookDelta += posDelta;
                touchLookPreviousPos = mouseScreenPos;
            }
        }
        else if (wasUp)
        {
            if (mouseSimulatingMove)
            {
                mouseSimulatingMove = false;
                touchMovementActive = false;
                currentTouchMovementVector = Vector2.zero;
            }
            if (mouseSimulatingLook)
            {
                mouseSimulatingLook = false;
                touchLookActive = false;
                currentTouchLookDelta = Vector2.zero;
            }
        }
    }
#endif

    private void OnGUI()
    {
        if (MovementSystemSelector.IsGameplayBlocked) return;
        if (!IsTouchActive || !mostrarUITactil) return;

        EnsureTouchStyles();

        // 1. Botón SALTO (Esquina inferior derecha)
        float jumpBtnSize = Mathf.Clamp(Screen.height * 0.12f, 75f, 120f);
        Rect jumpRect = new Rect(Screen.width - jumpBtnSize - 30f, Screen.height - jumpBtnSize - 40f, jumpBtnSize, jumpBtnSize);
        if (GUI.Button(jumpRect, "SALTAR", touchButtonStyle))
        {
            touchJumpTriggered = true;
        }

        // 2. Botón AGACHO (Al lado del botón de salto)
        float crouchBtnSize = Mathf.Clamp(Screen.height * 0.11f, 70f, 110f);
        Rect crouchRect = new Rect(Screen.width - jumpBtnSize - crouchBtnSize - 45f, Screen.height - crouchBtnSize - 40f, crouchBtnSize, crouchBtnSize);
        string crouchText = isCrouching ? "PARARSE" : "AGACHO";
        if (GUI.Button(crouchRect, crouchText, isCrouching ? touchButtonActiveStyle : touchButtonStyle))
        {
            touchCrouchTriggered = true;
        }

        // 3. Joystick Virtual Dinámico
        if (touchMovementActive)
        {
            DrawCircle(touchMovementOrigin, radioJoystickTactil, new Color(1f, 1f, 1f, 0.22f));
            Vector2 knobPos = touchMovementOrigin + new Vector2(currentTouchMovementVector.x, -currentTouchMovementVector.y) * radioJoystickTactil;
            DrawCircle(knobPos, 26f, new Color(0f, 0.8f, 1f, 0.75f));
        }
        else
        {
            Vector2 hintCenter = new Vector2(100f, Screen.height - 110f);
            DrawCircle(hintCenter, 45f, new Color(1f, 1f, 1f, 0.08f));
            GUI.Label(new Rect(30f, Screen.height - 130f, 140f, 40f), "Toca aquí para\nmoverte", touchHintStyle);
        }
    }

    private void EnsureTouchStyles()
    {
        if (touchButtonStyle == null)
        {
            touchButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            touchButtonStyle.normal.textColor = Color.white;
        }

        if (touchButtonActiveStyle == null)
        {
            touchButtonActiveStyle = new GUIStyle(touchButtonStyle);
            touchButtonActiveStyle.normal.textColor = new Color(0.2f, 1f, 0.8f, 1f);
        }

        if (touchHintStyle == null)
        {
            touchHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            touchHintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        }
    }

    private void DrawCircle(Vector2 center, float radius, Color color)
    {
        if (circleTexture == null)
        {
            int size = 64;
            circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            Color[] cols = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float alpha = Mathf.Clamp01((r - dist) / 1.5f);
                    cols[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            circleTexture.SetPixels(cols);
            circleTexture.Apply();
        }

        Color prevColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), circleTexture);
        GUI.color = prevColor;
    }

    /// <summary>
    /// Descarta colliders que nunca deben frenar ni sostener al jugador: los suyos propios, triggers
    /// y objetos recogibles (latas, botiquín, extintor) para que no bloqueen ni "eleven" la cámara.
    /// </summary>
    private bool EsColliderIgnorable(Collider c)
    {
        if (c == null || c.isTrigger) return true;
        Transform t = c.transform;
        if (t == transform || t.IsChildOf(transform)) return true;

        if (!cacheRecogible.TryGetValue(c, out bool esRecogible))
        {
            if (cacheRecogible.Count > 1024) cacheRecogible.Clear();
            esRecogible = c.GetComponentInParent<InteractableItem>() != null;
            cacheRecogible[c] = esRecogible;
        }
        return esRecogible;
    }

    /// <summary>SphereCast que devuelve el impacto sólido más cercano, ignorando al propio jugador y los objetos recogibles.</summary>
    private bool SphereCastSolido(Vector3 origen, float radio, Vector3 direccion, float distancia, out RaycastHit mejor)
    {
        mejor = default;
        bool encontrado = false;
        float mejorDist = float.MaxValue;

        int n = Physics.SphereCastNonAlloc(origen, radio, direccion, bufferHits, distancia, collisionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = bufferHits[i];
            if (EsColliderIgnorable(h.collider)) continue;
            // Solapes iniciales (distancia 0): los resuelve ResolveHorizontalPenetration
            if (h.distance <= 0f && h.point == Vector3.zero) continue;
            if (h.distance < mejorDist)
            {
                mejorDist = h.distance;
                mejor = h;
                encontrado = true;
            }
        }
        return encontrado;
    }

    /// <summary>
    /// Punto más cercano de un collider SIN errores: Collider.ClosestPoint no admite MeshCollider cóncavo
    /// ni TerrainCollider (llenaba la consola de errores cada frame).
    /// </summary>
    public static Vector3 PuntoMasCercano(Collider col, Vector3 punto)
    {
        if (col == null) return punto;
        MeshCollider mc = col as MeshCollider;
        bool soportado = col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (mc != null && mc.convex);
        return soportado ? col.ClosestPoint(punto) : col.bounds.ClosestPoint(punto);
    }

    private Vector3 ResolveHorizontalCollision(Vector3 move)
    {
        move.y = 0f;
        float distance = move.magnitude;
        if (distance < 0.0001f) return Vector3.zero;

        Vector3 dir = move / distance;

        if (SphereCastSolido(simPosition, collisionRadius, dir, distance + skinWidth, out RaycastHit hit))
        {
            // El suelo o una rampa transitable jamás son un muro que frene ni dañe
            if (hit.normal.y > 0.55f || EsPisoOSuelo(hit.collider))
            {
                return move;
            }

            // Sismo activo + choque contra pared vertical real: daño leve (con cooldown en PlayerHealth)
            if (CameraShake.Instance != null && CameraShake.Instance.IsShaking && PlayerHealth.Instance != null)
            {
                PlayerHealth.Instance.RegisterShakeCollision(hit.point, hit.normal, hit.collider);
            }

            float allowedDist = Mathf.Max(0f, hit.distance - skinWidth);
            Vector3 allowedMove = dir * allowedDist;
            allowedMove.y = 0f;

            if (slideAlongWalls)
            {
                float remainingDist = distance - allowedDist;
                if (remainingDist > 0.0001f)
                {
                    Vector3 slideDir = Vector3.ProjectOnPlane(dir * remainingDist, hit.normal);
                    slideDir.y = 0f;

                    float slideDist = slideDir.magnitude;
                    if (slideDist > 0.0001f)
                    {
                        Vector3 slideNorm = slideDir / slideDist;
                        if (SphereCastSolido(simPosition + allowedMove, collisionRadius, slideNorm, slideDist + skinWidth, out RaycastHit hit2))
                        {
                            allowedMove += slideNorm * Mathf.Max(0f, hit2.distance - skinWidth);
                        }
                        else
                        {
                            allowedMove += slideDir;
                        }
                    }
                }
            }

            allowedMove.y = 0f;
            return allowedMove;
        }

        return move;
    }

    private void ResolveHorizontalPenetration()
    {
        int n = Physics.OverlapSphereNonAlloc(simPosition, collisionRadius, bufferOverlaps, collisionLayers, QueryTriggerInteraction.Ignore);
        bool temblando = CameraShake.Instance != null && CameraShake.Instance.IsShaking;

        for (int i = 0; i < n; i++)
        {
            Collider col = bufferOverlaps[i];
            if (EsColliderIgnorable(col)) continue;

            // El piso lo gestiona el control vertical: nunca se considera penetración de pared
            if (EsPisoOSuelo(col)) continue;

            Vector3 dirEmpuje;
            float distEmpuje;

            if (cameraSphereCollider != null && cameraSphereCollider.enabled)
            {
                // ComputePenetration funciona también con MeshCollider cóncavos (ClosestPoint no)
                if (!Physics.ComputePenetration(cameraSphereCollider, simPosition, transform.rotation,
                        col, col.transform.position, col.transform.rotation, out dirEmpuje, out distEmpuje))
                {
                    continue;
                }
            }
            else
            {
                Vector3 closest = PuntoMasCercano(col, simPosition);
                Vector3 diff = simPosition - closest;
                float d = diff.magnitude;
                if (d >= collisionRadius) continue;
                dirEmpuje = d > 0.0001f ? diff / d : -(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
                distEmpuje = collisionRadius - d;
            }

            // Obstáculos por debajo (escalones, bordillos): los gestiona el control de altura
            if (dirEmpuje.y > 0.55f) continue;

            Vector3 horizontal = new Vector3(dirEmpuje.x, 0f, dirEmpuje.z);
            if (horizontal.sqrMagnitude < 0.0001f) continue;
            horizontal.Normalize();

            if (temblando && PlayerHealth.Instance != null)
            {
                PlayerHealth.Instance.RegisterShakeCollision(simPosition - horizontal * collisionRadius, horizontal, col);
            }

            simPosition += horizontal * (distEmpuje + skinWidth);
        }
    }

    /// <summary>
    /// Detecta la superficie de piso o terreno transitable directamente debajo de la cámara.
    /// Usa consultas sin asignación de memoria e ignora al propio jugador y a los objetos recogibles.
    /// </summary>
    private bool DetectarPisoDebajo(out float alturaSuelo)
    {
        bool encontrado = false;
        float mejorY = float.MinValue;

        // Lanzar desde un margen seguro por encima de la cámara (sin llegar a techos altos)
        float margenArriba = Mathf.Clamp(distanciaAlPiso * 0.35f, 0.6f, 1.8f);
        Vector3 rayOrigin = new Vector3(simPosition.x, currentVerticalY + margenArriba, simPosition.z);
        float distanciaMaxima = Mathf.Max(120f, distanciaAlPiso * 3.5f + margenArriba);

        // 1. Rayo central hacia abajo
        int n = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, bufferHits, distanciaMaxima, collisionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = bufferHits[i];
            if (EsColliderIgnorable(h.collider)) continue;

            if ((h.normal.y > 0.25f || EsPisoOSuelo(h.collider)) && h.point.y <= rayOrigin.y - 0.02f && h.point.y > mejorY)
            {
                mejorY = h.point.y;
                encontrado = true;
            }
        }

        // 2. Si el rayo central cae en un hueco o borde de malla, usar una esfera
        if (!encontrado)
        {
            n = Physics.SphereCastNonAlloc(rayOrigin, radioDeteccionSuelo, Vector3.down, bufferHits, distanciaMaxima, collisionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = bufferHits[i];
                if (EsColliderIgnorable(h.collider)) continue;
                if (h.distance <= 0f && h.point == Vector3.zero) continue;

                if ((h.normal.y > 0.25f || EsPisoOSuelo(h.collider)) && h.point.y <= rayOrigin.y - 0.02f && h.point.y > mejorY)
                {
                    mejorY = h.point.y;
                    encontrado = true;
                }
            }
        }

        if (encontrado)
        {
            alturaSuelo = mejorY;
            ultimoSueloValidoY = mejorY;
            return true;
        }

        // 3. Salvaguarda contra el vacío: último piso conocido o nivel base del escenario
        alturaSuelo = Mathf.Max(ultimoSueloValidoY, nivelPisoBaseMundo);
        return false;
    }

    private void UpdateVerticalPosition(float dt)
    {
        // 1. Detección del piso debajo
        DetectarPisoDebajo(out float sueloY);
        float distanciaEfectiva = GetDistanciaAlPisoEfectiva();
        float targetGroundY = sueloY + distanciaEfectiva;

        // 2. Techo: frenar el salto solo si hay algo ENCIMA (antes cualquier pared rozada cortaba el salto)
        if (verticalVelocity > 0f)
        {
            int nTecho = Physics.SphereCastNonAlloc(simPosition, collisionRadius, Vector3.up, bufferHits, 0.2f + skinWidth, collisionLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < nTecho; i++)
            {
                RaycastHit h = bufferHits[i];
                if (h.distance <= 0f || h.normal.y > -0.3f) continue;
                if (EsColliderIgnorable(h.collider)) continue;
                verticalVelocity = 0f;
                break;
            }
        }

        // 3. Gravedad y seguimiento del suelo
        if (verticalVelocity > 0f)
        {
            // Fase de impulso de salto (subiendo)
            isGrounded = false;
            if (activarGravedad)
            {
                verticalVelocity -= gravity * dt;
            }
            currentVerticalY += verticalVelocity * dt;
        }
        else
        {
            if (currentVerticalY > targetGroundY + 0.005f)
            {
                float desnivelBajo = currentVerticalY - targetGroundY;
                // En el suelo y con desnivel suave hacia abajo (escalón o rampa): descender pegado al suelo
                if (isGrounded && desnivelBajo <= alturaMaximaEscalon)
                {
                    currentVerticalY = Mathf.MoveTowards(currentVerticalY, targetGroundY, suavizadoAltura * dt);
                }
                else
                {
                    // Caída libre por gravedad (salto, precipicio o caída desde el aire)
                    isGrounded = false;
                    if (activarGravedad)
                    {
                        verticalVelocity -= gravity * dt;
                        verticalVelocity = Mathf.Max(-velocidadMaximaCaida, verticalVelocity);
                        currentVerticalY += verticalVelocity * dt;
                    }
                    else
                    {
                        currentVerticalY = Mathf.MoveTowards(currentVerticalY, targetGroundY, suavizadoAltura * dt);
                    }
                }

                // Al alcanzar o atravesar la altura del suelo: aterrizar
                if (currentVerticalY <= targetGroundY)
                {
                    currentVerticalY = targetGroundY;
                    verticalVelocity = 0f;
                    isGrounded = true;
                }
            }
            else
            {
                // En el suelo o el terreno sube (rampas, escalones): seguirlo con suavidad
                isGrounded = true;
                verticalVelocity = 0f;
                currentVerticalY = Mathf.MoveTowards(currentVerticalY, targetGroundY, suavizadoAltura * 2f * dt);
            }
        }

        // 4. Garantía: la cámara nunca baja de la distancia mínima de seguridad.
        // (Antes se forzaba la altura completa de golpe y cada escalón producía un tirón vertical;
        //  además en VR a nivel de suelo elevaba el rig 0.45 m.)
        float alturaMinimaAbsoluta = sueloY + Mathf.Min(distanciaMinimaSeguridad, distanciaEfectiva);
        if (currentVerticalY < alturaMinimaAbsoluta)
        {
            currentVerticalY = alturaMinimaAbsoluta;
            if (verticalVelocity < 0f) verticalVelocity = 0f;
            isGrounded = true;
        }

        baseGroundY = currentVerticalY;

        // 5. Posición simulada final con agachado (la sacudida del sismo se añade solo en LateUpdate)
        float offsetAgachado = currentCrouchOffset;
        if (currentVerticalY + offsetAgachado < alturaMinimaAbsoluta)
        {
            offsetAgachado = alturaMinimaAbsoluta - currentVerticalY;
        }

        simPosition = new Vector3(simPosition.x, currentVerticalY + offsetAgachado, simPosition.z);
    }

    /// <summary>
    /// Comprueba si un colisionador corresponde al piso, terreno o suelo del escenario.
    /// El resultado se cachea: antes se hacían ToLower() y concatenaciones de texto en cada consulta física.
    /// </summary>
    public static bool EsPisoOSuelo(Collider col)
    {
        if (col == null) return false;
        if (cachePiso.TryGetValue(col, out bool esPiso)) return esPiso;

        if (cachePiso.Count > 1024) cachePiso.Clear();

        esPiso = false;
        if (col.GetComponentInParent<PisoEditable>() != null)
        {
            esPiso = true;
        }
        else
        {
            string nombre = col.name.ToLower();
            if (col.transform.parent != null)
            {
                nombre += " " + col.transform.parent.name.ToLower();
            }

            esPiso = nombre.Contains("piso") || nombre.Contains("suelo") || nombre.Contains("floor") ||
                     nombre.Contains("ground") || nombre.Contains("terrain") || nombre.Contains("pavimento") ||
                     nombre.Contains("explanada") || nombre.Contains("asfalto") || nombre.Contains("acera");
        }

        cachePiso[col] = esPiso;
        return esPiso;
    }

#if ENABLE_INPUT_SYSTEM
    private bool IsKeyPressed(Key key)
    {
        if (Keyboard.current == null || key == Key.None) return false;
        return Keyboard.current[key].isPressed;
    }

    private bool IsKeyDown(Key key)
    {
        if (Keyboard.current == null || key == Key.None) return false;
        return Keyboard.current[key].wasPressedThisFrame;
    }
#else
    private bool IsKeyPressed(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        return Input.GetKey(key);
    }

    private bool IsKeyDown(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        return Input.GetKeyDown(key);
    }
#endif

    // =========================================================================
    // SISTEMA DE TELETRANSPORTE (TELEPORTATION SYSTEM)
    // =========================================================================

    private void EnsureTeleportVisuals()
    {
        if (teleportLineRenderer == null)
        {
            Transform existingLine = transform.Find("_TeleportArcLine");
            GameObject lineObj = existingLine != null ? existingLine.gameObject : new GameObject("_TeleportArcLine");
            lineObj.transform.SetParent(transform, false);
            teleportLineRenderer = lineObj.GetComponent<LineRenderer>();
            if (teleportLineRenderer == null) teleportLineRenderer = lineObj.AddComponent<LineRenderer>();

            teleportLineRenderer.useWorldSpace = true;
            teleportLineRenderer.startWidth = 0.05f;
            teleportLineRenderer.endWidth = 0.02f;
            teleportLineRenderer.positionCount = 0;

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            if (unlitShader != null)
            {
                teleportLineMaterial = new Material(unlitShader);
                teleportLineMaterial.color = new Color(0f, 0.95f, 0.85f, 0.85f);
                teleportLineRenderer.material = teleportLineMaterial;
            }
            lineObj.SetActive(false);
        }

        if (teleportReticleObj == null)
        {
            teleportReticleObj = new GameObject("_TeleportReticle");
            teleportReticleObj.transform.SetParent(null, false);
            LineRenderer circleRenderer = teleportReticleObj.AddComponent<LineRenderer>();
            circleRenderer.useWorldSpace = false;
            circleRenderer.loop = true;
            circleRenderer.startWidth = 0.035f;
            circleRenderer.endWidth = 0.035f;

            int segments = 24;
            circleRenderer.positionCount = segments;
            float radius = 0.45f;
            Vector3[] points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Cos(angle) * radius, 0.02f, Mathf.Sin(angle) * radius);
            }
            circleRenderer.SetPositions(points);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            if (unlitShader != null)
            {
                teleportReticleMaterial = new Material(unlitShader);
                teleportReticleMaterial.color = new Color(0f, 1f, 0.8f, 0.9f);
                circleRenderer.material = teleportReticleMaterial;
            }
            teleportReticleObj.SetActive(false);
        }

        if (teleportAudioSource == null)
        {
            // AudioSource PROPIO: compartido con CameraShake/PlayerHealth se pisaban clip, volumen y Stop()
            teleportAudioSource = gameObject.AddComponent<AudioSource>();
            teleportAudioSource.playOnAwake = false;
            teleportAudioSource.spatialBlend = 0f;
            teleportAudioSource.volume = 0.45f;
            teleportSoundClip = CreateTeleportChimeClip();
        }
    }

    private AudioClip CreateTeleportChimeClip()
    {
        int sampleRate = 44100;
        float duration = 0.18f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float progress = i / (float)sampleCount;
            float freq = Mathf.Lerp(420f, 960f, progress);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.45f;
        }

        AudioClip clip = AudioClip.Create("TeleportChime", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void HandleTeleportation()
    {
        if (tipoLocomocion == LocomotionMode.Continuo)
        {
            if (isAimingTeleport) CancelTeleportAiming();
            return;
        }

        EnsureTeleportVisuals();

        bool wantsToAim = false;

        // 1. Detección en VR con Meta XR Building Blocks (OVRInput nativo y UnityEngine.XR)
        if (IsVRActive && teletransporteEnVR)
        {
            // Gatillo índice derecho o thumbstick hacia adelante en el mando derecho
            if (OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger) || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.35f)
            {
                wantsToAim = true;
                teleportUsingRightHand = true;
            }
            else if (OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y > 0.55f)
            {
                wantsToAim = true;
                teleportUsingRightHand = true;
            }
            // Gatillo índice izquierdo o thumbstick hacia adelante en el mando izquierdo
            else if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger) || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.35f)
            {
                wantsToAim = true;
                teleportUsingRightHand = false;
            }
            else if (OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y > 0.55f)
            {
                wantsToAim = true;
                teleportUsingRightHand = false;
            }

            // Fallback con InputDevices de UnityEngine.XR
            if (!wantsToAim)
            {
                if (rightControllers.Count > 0 && rightControllers[0].isValid)
                {
                    if (rightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float rTrig) && rTrig > 0.35f)
                    {
                        wantsToAim = true;
                        teleportUsingRightHand = true;
                    }
                    else if (rightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rStick) && rStick.y > 0.55f)
                    {
                        wantsToAim = true;
                        teleportUsingRightHand = true;
                    }
                }

                if (!wantsToAim && leftControllers.Count > 0 && leftControllers[0].isValid)
                {
                    if (leftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float lTrig) && lTrig > 0.35f)
                    {
                        wantsToAim = true;
                        teleportUsingRightHand = false;
                    }
                    else if (leftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 lStick) && lStick.y > 0.55f)
                    {
                        wantsToAim = true;
                        teleportUsingRightHand = false;
                    }
                }
            }
        }

        // 2. Detección en PC (Teclado y Ratón)
        if (IsKeyboardActive && teletransporteEnPC)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tKey.isPressed)
            {
                wantsToAim = true;
            }
            if (tipoLocomocion == LocomotionMode.Teletransporte && Mouse.current != null)
            {
                if (Mouse.current.rightButton.isPressed || (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed))
                {
                    wantsToAim = true;
                }
            }
#else
            if (Input.GetKey(KeyCode.T))
            {
                wantsToAim = true;
            }
            if (tipoLocomocion == LocomotionMode.Teletransporte && (Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftShift)))
            {
                wantsToAim = true;
            }
#endif
        }

        if (wantsToAim)
        {
            isAimingTeleport = true;
            SimulateTeleportTrajectory();
        }
        else
        {
            if (isAimingTeleport)
            {
                if (teleportHitValid)
                {
                    TeleportTo(teleportHitPoint);
                }
                CancelTeleportAiming();
            }
        }
    }

    private void SimulateTeleportTrajectory()
    {
        Vector3 origin;
        Vector3 forwardDir;

        if (IsVRActive)
        {
            // El arco parabólico se proyecta desde el mando con el que se está apuntando
            Transform handTransform = teleportUsingRightHand ? vrRightControllerAnchor : vrLeftControllerAnchor;
            if (handTransform == null) handTransform = vrRightControllerAnchor != null ? vrRightControllerAnchor : (vrLeftControllerAnchor != null ? vrLeftControllerAnchor : vrCenterEyeAnchor);

            if (handTransform != null)
            {
                origin = handTransform.position + handTransform.forward * 0.08f;
                forwardDir = handTransform.forward;
            }
            else
            {
                origin = transform.position + Vector3.down * 0.15f + transform.forward * 0.25f;
                forwardDir = transform.forward;
            }
        }
        else
        {
            origin = transform.position + Vector3.down * 0.15f + transform.forward * 0.25f;
            forwardDir = transform.forward;
        }

        Vector3 launchDir = (forwardDir + Vector3.up * 0.12f).normalized;
        Vector3 velocity = launchDir * teletransporteVelocidadArco;
        Vector3 currentPos = origin;
        Vector3 gravityVec = new Vector3(0, -9.81f, 0);

        List<Vector3> points = new List<Vector3>();
        points.Add(currentPos);

        teleportHitValid = false;
        float timeStep = 0.05f;
        int maxSteps = Mathf.Clamp(teletransporteSegmentos, 15, 45);

        for (int i = 0; i < maxSteps; i++)
        {
            Vector3 nextPos = currentPos + velocity * timeStep + gravityVec * (0.5f * timeStep * timeStep);
            velocity += gravityVec * timeStep;

            if (Vector3.Distance(origin, nextPos) > teletransporteMaxDistancia)
            {
                break;
            }

            Vector3 stepDir = nextPos - currentPos;
            float stepDist = stepDir.magnitude;

            if (Physics.Raycast(currentPos, stepDir.normalized, out RaycastHit hit, stepDist, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
                {
                    points.Add(hit.point);

                    if (Vector3.Distance(origin, hit.point) <= teletransporteMaxDistancia && (hit.normal.y >= 0.55f || EsPisoOSuelo(hit.collider)))
                    {
                        teleportHitValid = true;
                        teleportHitPoint = hit.point;
                        teleportHitNormal = hit.normal;
                    }
                    else
                    {
                        teleportHitValid = false;
                    }
                    break;
                }
            }

            points.Add(nextPos);
            currentPos = nextPos;
        }

        if (teleportLineRenderer != null)
        {
            teleportLineRenderer.gameObject.SetActive(true);
            teleportLineRenderer.positionCount = points.Count;
            teleportLineRenderer.SetPositions(points.ToArray());

            Color arcColor = teleportHitValid ? new Color(0f, 0.95f, 0.85f, 0.9f) : new Color(1f, 0.35f, 0.25f, 0.9f);
            if (teleportLineMaterial != null) teleportLineMaterial.color = arcColor;
            teleportLineRenderer.startColor = arcColor;
            teleportLineRenderer.endColor = new Color(arcColor.r, arcColor.g, arcColor.b, 0.25f);
        }

        if (teleportReticleObj != null)
        {
            if (teleportHitValid)
            {
                teleportReticleObj.SetActive(true);
                teleportReticleObj.transform.position = teleportHitPoint + Vector3.up * 0.02f;
                teleportReticleObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, teleportHitNormal);

                float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.06f;
                teleportReticleObj.transform.localScale = new Vector3(pulse, 1f, pulse);

                if (teleportReticleMaterial != null) teleportReticleMaterial.color = new Color(0f, 1f, 0.85f, 0.95f);
            }
            else
            {
                teleportReticleObj.SetActive(false);
            }
        }
    }

    private void CancelTeleportAiming()
    {
        isAimingTeleport = false;
        teleportHitValid = false;
        if (teleportLineRenderer != null) teleportLineRenderer.gameObject.SetActive(false);
        if (teleportReticleObj != null) teleportReticleObj.SetActive(false);
    }

    /// <summary>
    /// Teletransporta instantáneamente al jugador/cámara a una posición de suelo determinada.
    /// Garantiza la altura correcta sobre el piso, anula inercias y emite efecto sonoro.
    /// En VR con Room-Scale, compensa el desplazamiento físico horizontal de la cabeza para que el punto de vista aterrice con total precisión.
    /// </summary>
    public void TeleportTo(Vector3 targetGroundPosition)
    {
        Vector3 headOffset = Vector3.zero;
        if (IsVRPresent && vrCenterEyeAnchor != null)
        {
            headOffset = vrCenterEyeAnchor.position - transform.position;
            headOffset.y = 0f;
        }

        targetGroundPosition.y = Mathf.Max(targetGroundPosition.y, nivelPisoBaseMundo);
        float targetCameraY = targetGroundPosition.y + GetDistanciaAlPisoEfectiva();

        Vector3 finalPos = new Vector3(targetGroundPosition.x - headOffset.x, targetCameraY, targetGroundPosition.z - headOffset.z);

        // Mover simulación y transform a la vez (el salto de posición no se interpola)
        simPosition = finalPos;
        prevSimPosition = finalPos;
        transform.position = finalPos;
        ultimaPosicionEscrita = finalPos;
        hayPosicionEscrita = true;

        currentVerticalY = targetCameraY;
        baseGroundY = targetCameraY;
        currentCrouchOffset = 0f;
        isCrouching = false;
        ultimoSueloValidoY = targetGroundPosition.y;
        verticalVelocity = 0f;
        isGrounded = true;
        currentHorizontalVelocity = Vector3.zero;

        if (teleportAudioSource != null && teleportSoundClip != null)
        {
            teleportAudioSource.PlayOneShot(teleportSoundClip, 0.55f);
        }
    }

    /// <summary>
    /// Comprueba si el sistema de seguimiento VR está operando a nivel de suelo físico (Floor Level).
    /// En FloorLevel, el origen Y del rig coincide con el piso real (Y=0) y el visor mide la estatura del jugador.
    /// </summary>
    public bool EsSeguimientoNivelSueloVR()
    {
        if (OVRManager.instance != null)
        {
            return OVRManager.instance.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel;
        }
        return true;
    }

    /// <summary>
    /// Obtiene la distancia efectiva sobre el suelo considerando el modo de control.
    /// En VR con FloorLevel activo y autoajuste, el rig debe posarse a 0m sobre el suelo para que la altura de los ojos sea natural.
    /// En PC / móvil o EyeLevel, utiliza 'distanciaAlPiso'.
    /// </summary>
    public float GetDistanciaAlPisoEfectiva()
    {
        if (IsVRActive && autoAjustarAlturaSueloVR && EsSeguimientoNivelSueloVR())
        {
            return 0f;
        }
        return distanciaAlPiso;
    }

    /// <summary>
    /// Detecta y enlaza automáticamente los transforms de los Building Blocks de Meta XR
    /// (CenterEyeAnchor, Controller Tracking Left y Controller Tracking Right).
    /// </summary>
    public void AutoDetectVRBuildingBlocks()
    {
        if (vrCenterEyeAnchor == null)
        {
            Transform centerEye = transform.Find("TrackingSpace/CenterEyeAnchor");
            if (centerEye == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Equals("CenterEyeAnchor", System.StringComparison.OrdinalIgnoreCase))
                    {
                        centerEye = child;
                        break;
                    }
                }
            }
            if (centerEye == null && Camera.main != null)
            {
                centerEye = Camera.main.transform;
            }
            vrCenterEyeAnchor = centerEye;
        }

        if (vrLeftControllerAnchor == null)
        {
            Transform leftAnchor = transform.Find("TrackingSpace/LeftHandAnchor/LeftControllerAnchor/[BuildingBlock] Controller Tracking Left");
            if (leftAnchor == null) leftAnchor = transform.Find("TrackingSpace/LeftHandAnchor/LeftControllerAnchor");
            if (leftAnchor == null) leftAnchor = transform.Find("TrackingSpace/LeftHandAnchor");
            if (leftAnchor == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Contains("Controller Tracking Left") || child.name.Equals("LeftControllerAnchor", System.StringComparison.OrdinalIgnoreCase))
                    {
                        leftAnchor = child;
                        break;
                    }
                }
            }
            vrLeftControllerAnchor = leftAnchor;
        }

        if (vrRightControllerAnchor == null)
        {
            Transform rightAnchor = transform.Find("TrackingSpace/RightHandAnchor/RightControllerAnchor/[BuildingBlock] Controller Tracking Right");
            if (rightAnchor == null) rightAnchor = transform.Find("TrackingSpace/RightHandAnchor/RightControllerAnchor");
            if (rightAnchor == null) rightAnchor = transform.Find("TrackingSpace/RightHandAnchor");
            if (rightAnchor == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Contains("Controller Tracking Right") || child.name.Equals("RightControllerAnchor", System.StringComparison.OrdinalIgnoreCase))
                    {
                        rightAnchor = child;
                        break;
                    }
                }
            }
            vrRightControllerAnchor = rightAnchor;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableCollision || !showCollisionGizmo) return;

        Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
        Gizmos.DrawSphere(transform.position, collisionRadius);
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, collisionRadius);
    }
}

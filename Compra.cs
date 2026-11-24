using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Script Compra:
/// Permite al jugador interactuar y golpear la máquina expendedora (vending_machine)
/// para que suelte el objeto 'lata_Lowpoly'.
/// 
/// REQUISITOS Y REGLAS:
/// 1. La máquina expendedora NO debe estar en llamas. Si está ardiendo (VendingMachineHazard.IsOnFire == true),
///    no permite golpearla ni comprar, mostrando una advertencia de peligro e infligiendo una leve quemadura
///    si el jugador intenta tocarla.
/// 2. Cuando la máquina NO esté en llamas (antes de la explosión del temblor, o después de haber extinguido
///    el fuego con el extintor), el jugador puede golpearla mediante:
///    - Clic izquierdo del ratón (PC)
///    - Tecla F o E (Teclado PC)
///    - Gatillo / Grip / Botón A en mandos de Realidad Virtual (VR)
///    - Botón táctil interactivo en pantalla
///    - Golpe físico / Colisión directa
/// 3. Al recibir el golpe:
///    - La máquina expendedora se sacude físicamente con retroceso realista.
///    - Emite un sonido de impacto metálico contundente.
///    - Expulsa / suelta el objeto 'lata_Lowpoly' desde la bandeja dispensadora con física realista (Rigidbody).
///    - La lata queda en el suelo como objeto interactuable (InteractableItem) para recogerla al inventario.
/// </summary>
public class Compra : MonoBehaviour
{
    public static Compra Instance { get; private set; }

    [Header("--- OBJETO A SOLTAR (LATA) ---")]
    [Tooltip("Modelo o Prefab de la lata a soltar. Si se deja vacío, cargará automáticamente 'Assets/Modelos3d/lata_lowpoly.obj'")]
    [SerializeField] private GameObject lataPrefab;

    [Tooltip("Punto de salida por donde es expulsada la lata. Si es nulo, se calcula automáticamente en la bandeja frontal")]
    [SerializeField] private Transform puntoDeSalida;

    [Tooltip("Fuerza y dirección con la que la lata sale despedida hacia adelante")]
    [Range(0.4f, 4f)]
    [SerializeField] private float fuerzaExpulsion = 1.6f;

    [Tooltip("Cantidad de latas que puede soltar (-1 = infinitas)")]
    [SerializeField] private int latasDisponibles = -1;

    [Tooltip("Distancia máxima (metros, medida desde la cámara) a la que el jugador puede recoger la lata expulsada. " +
             "La cámara va a unos 2.5 m del suelo, así que conviene un valor mayor de 3 m para no tener que estar encima de la lata")]
    [Range(1f, 10f)]
    [SerializeField] private float distanciaRecogerLata = 4.5f;

    public float DistanciaRecogerLata
    {
        get => distanciaRecogerLata;
        set => distanciaRecogerLata = Mathf.Clamp(value, 1f, 10f);
    }

    [Header("--- TAMAÑO Y ESCALA DE LA LATA (EDITABLE) ---")]
    [Tooltip("Multiplicador del tamaño general de la lata (1 = escala normal, 1.5 = 50% más grande, 2 = doble de tamaño, 0.5 = mitad)")]
    [Range(0.1f, 5f)]
    [SerializeField] private float escalaLata = 1.0f;

    [Tooltip("Escala tridimensional personalizada por eje (X: ancho/grosor, Y: altura, Z: profundidad)")]
    [SerializeField] private Vector3 escalaPersonalizada = Vector3.one;

    public float EscalaLata
    {
        get => escalaLata;
        set => escalaLata = Mathf.Max(0.05f, value);
    }

    public Vector3 EscalaPersonalizada
    {
        get => escalaPersonalizada;
        set => escalaPersonalizada = value;
    }

    [Header("--- TEXTURA Y MATERIALES DE LA LATA ---")]
    [Tooltip("Textura principal que se aplicará a la lata (etiqueta de refresco/cola). Si se deja vacía, se cargará automáticamente 'Assets/Materials/T_Lata_Refresco.png' o se generará una textura procedural realista.")]
    [SerializeField] private Texture2D texturaLata;

    [Tooltip("Material para el cuerpo cilíndrico de la lata (donde se proyecta la textura de la etiqueta).")]
    [SerializeField] private Material materialCuerpoLata;

    [Tooltip("Material metálico para la tapa superior, base y anilla de aluminio de la lata.")]
    [SerializeField] private Material materialAluminioLata;

    [Tooltip("Color de tinte o base para la lata (Blanco = muestra los colores reales de la textura).")]
    [SerializeField] private Color colorBaseLata = Color.white;

    [Tooltip("Nivel metálico del cuerpo de la lata (0 = plástico mate, 1 = aluminio pulido reflectante).")]
    [Range(0f, 1f)]
    [SerializeField] private float metalicoCuerpo = 0.85f;

    [Tooltip("Nivel de brillo/suavidad del cuerpo de la lata.")]
    [Range(0f, 1f)]
    [SerializeField] private float suavidadCuerpo = 0.80f;

    [Tooltip("Nivel metálico de la tapa y anilla de aluminio.")]
    [Range(0f, 1f)]
    [SerializeField] private float metalicoAluminio = 0.95f;

    [Tooltip("Nivel de brillo/suavidad de la tapa y anilla de aluminio.")]
    [Range(0f, 1f)]
    [SerializeField] private float suavidadAluminio = 0.85f;

    [Tooltip("Asegurar que la lata siempre tenga aplicada la textura y materiales al ser expulsada")]
    [SerializeField] private bool aplicarTexturaAutomatica = true;

    public Texture2D TexturaLata
    {
        get => texturaLata;
        set
        {
            texturaLata = value;
            ActualizarMaterialesLata();
        }
    }

    public Material MaterialCuerpoLata
    {
        get => materialCuerpoLata;
        set => materialCuerpoLata = value;
    }

    public Material MaterialAluminioLata
    {
        get => materialAluminioLata;
        set => materialAluminioLata = value;
    }

    public Color ColorBaseLata
    {
        get => colorBaseLata;
        set
        {
            colorBaseLata = value;
            ActualizarMaterialesLata();
        }
    }

    [Header("--- MECÁNICA DE GOLPE Y COMPRA ---")]
    [Tooltip("Golpes necesarios para que caiga una lata")]
    [Range(1, 5)]
    [SerializeField] private int golpesNecesarios = 1;

    [Tooltip("Distancia máxima en metros a la que el jugador puede golpear la máquina")]
    [Range(1.2f, 5f)]
    [SerializeField] private float distanciaGolpe = 2.8f;

    [Tooltip("Tiempo de enfriamiento en segundos entre cada golpe")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float cooldownGolpe = 0.45f;

    [Tooltip("Intensidad de la sacudida física de la máquina al ser golpeada")]
    [Range(0.01f, 0.15f)]
    [SerializeField] private float intensidadSacudida = 0.05f;

    [Header("--- SEGURIDAD CONTRA FUEGO ---")]
    [Tooltip("Referencia al componente de peligro de incendio de la máquina. Si es null, se detecta automáticamente")]
    [SerializeField] private VendingMachineHazard hazardComponent;

    [Tooltip("Si el jugador golpea la máquina en llamas, recibe una pequeña quemadura en la mano")]
    [SerializeField] private bool quemarSiEstaEnLlamas = true;

    [Tooltip("Daño por quemadura al intentar golpear una máquina ardiendo")]
    [Range(1f, 30f)]
    [SerializeField] private float danioQuemadura = 7f;

    [Tooltip("Si está activo, la máquina deja de dar latas para siempre en cuanto se incendia (aunque luego se apague el fuego)")]
    [SerializeField] private bool bloquearCompraTrasIncendio = true;

    [Header("--- EFECTOS DE AUDIO ---")]
    [Tooltip("Clip de sonido de golpe metálico a la máquina (si es null, se genera proceduralmente)")]
    [SerializeField] private AudioClip sonidoGolpeMetalico;

    [Tooltip("Clip de sonido cuando cae la lata (si es null, se genera proceduralmente)")]
    [SerializeField] private AudioClip sonidoCaidaLata;

    [Tooltip("Volumen de los efectos sonoros")]
    [Range(0f, 1f)]
    [SerializeField] private float volumenAudio = 0.9f;

    [Header("--- INTERFAZ EN PANTALLA ---")]
    [Tooltip("Muestra el mensaje interactivo en pantalla cuando el jugador mira la máquina")]
    [SerializeField] private bool mostrarHUD = true;

    // Estado interno
    private int contadorGolpesActuales = 0;
    private float ultimoGolpeTimestamp = -10f;
    private bool estaSacudiendose = false;
    private Vector3 posicionOriginal;
    private Quaternion rotacionOriginal;
    private AudioSource audioSource;
    private Camera cachedPlayerCamera;
    private Texture2D iconoLataGenerado;
    private bool jugadorEnRangoCache = false;
    private bool jugadorApuntandoCache = false;
    private Collider[] collidersMaquina = new Collider[0];
    private readonly RaycastHit[] bufferRayos = new RaycastHit[16];

    // Dispositivos VR
    private readonly List<UnityEngine.XR.InputDevice> vrRightDevices = new List<UnityEngine.XR.InputDevice>();
    private readonly List<UnityEngine.XR.InputDevice> vrLeftDevices = new List<UnityEngine.XR.InputDevice>();
    private bool vrHitLastFrame = false;

    // Estilos GUI
    private GUIStyle promptBoxStyle;
    private GUIStyle promptWarningStyle;
    private GUIStyle touchButtonStyle;
    private Texture2D promptBgTex;
    private Texture2D promptWarningBgTex;
    private Texture2D buttonBorderTex;

    public int LatasDisponibles => latasDisponibles;
    public int ContadorGolpes => contadorGolpesActuales;

    private void Awake()
    {
        if (Instance == null) Instance = this;

        posicionOriginal = transform.position;
        rotacionOriginal = transform.rotation;

        // 1. Detectar componente de peligro de fuego
        // GetComponentInParent incluye el propio objeto (el patrón "GetComponent() ?? ..." falla en el Editor)
        if (hazardComponent == null) hazardComponent = GetComponentInParent<VendingMachineHazard>();
        if (hazardComponent == null) hazardComponent = GetComponentInChildren<VendingMachineHazard>();

        // 2. AudioSource 3D PROPIO (antes compartía el de VendingMachineHazard: el fuego le cambiaba volumen y Stop())
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // Audio espacial 3D
        audioSource.minDistance = 1.5f;
        audioSource.maxDistance = 20f;

        // 3. Asegurar colisionador para golpes y raycasts
        if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(1f, 2f, 1f);
        }

        // 4. Cargar prefab de lata_lowpoly si no está asignado
        AutoCargarPrefabLata();

        // 5. Configurar punto de salida de la lata si no existe
        CalcularPuntoDeSalida();

        // 6. Cargar e inicializar textura y materiales de la lata
        InicializarMaterialesYTexturas();
    }

    private void Start()
    {
        if (hazardComponent == null) ObtenerHazard();
        collidersMaquina = GetComponentsInChildren<Collider>();
        GetPlayerCamera();
        InicializarMaterialesYTexturas();
        CrearIconoLata();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void AutoCargarPrefabLata()
    {
        if (lataPrefab != null) return;

#if UNITY_EDITOR
        lataPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modelos3d/lata_lowpoly.obj");
        if (lataPrefab != null)
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        if (lataPrefab == null)
        {
            // Intentar buscar en Resources o en la escena
            GameObject found = GameObject.Find("lata_lowpoly") ?? GameObject.Find("lata_Lowpoly");
            if (found != null && found.scene.name == null)
            {
                lataPrefab = found;
            }
        }
    }

    private void CalcularPuntoDeSalida()
    {
        if (puntoDeSalida != null) return;

        // Buscar si ya existe un hijo llamado DispensePoint o SalidaLata
        Transform existingPoint = transform.Find("DispensePoint") ?? transform.Find("SalidaLata");
        if (existingPoint != null)
        {
            puntoDeSalida = existingPoint;
            return;
        }

        // Crear punto de salida en la bandeja inferior frontal de la máquina expendedora
        GameObject salidaGO = new GameObject("DispensePoint_Lata");
        salidaGO.transform.SetParent(transform);

        // Bandeja: frente inferior y FUERA del collider de la máquina (antes la lata nacía dentro y salía disparada)
        float frenteLocal = 0.48f;
        float alturaLocal = 0.42f;
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            frenteLocal = box.center.z + box.size.z * 0.5f;
            alturaLocal = box.center.y - box.size.y * 0.5f + box.size.y * 0.2f;
        }
        salidaGO.transform.position = transform.TransformPoint(new Vector3(0f, alturaLocal, frenteLocal)) + transform.forward * 0.12f;
        salidaGO.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, 90f); // Recostada de lado
        puntoDeSalida = salidaGO.transform;
    }

    private void Update()
    {
        if (MovementSystemSelector.IsGameplayBlocked)
        {
            jugadorEnRangoCache = false;
            jugadorApuntandoCache = false;
            return;
        }

        // Se calcula UNA vez por frame (OnGUI se ejecuta varias veces por frame y repetía los raycasts)
        jugadorEnRangoCache = IsPlayerInRange();
        jugadorApuntandoCache = jugadorEnRangoCache && IsPlayerAimingAtMachine();

        if (jugadorApuntandoCache)
        {
            HandleHitInput();
        }
    }

    /// <summary>
    /// Verifica si la máquina expendedora está en llamas.
    /// </summary>
    public bool EstaEnLlamas()
    {
        VendingMachineHazard h = ObtenerHazard();
        return h != null && h.IsOnFire;
    }

    /// <summary>
    /// Compra deshabilitada si la máquina está ardiendo o (con 'bloquearCompraTrasIncendio') si ya se incendió.
    /// El estado se lee directamente de VendingMachineHazard, así nunca se desincroniza.
    /// </summary>
    public bool CompraBloqueada()
    {
        VendingMachineHazard h = ObtenerHazard();
        if (h == null) return false;
        if (h.IsOnFire) return true;
        return bloquearCompraTrasIncendio && h.HasExploded;
    }

    private VendingMachineHazard ObtenerHazard()
    {
        if (hazardComponent != null) return hazardComponent;

        VendingMachineHazard global = VendingMachineHazard.Instance;
        if (global != null &&
            (global.gameObject == gameObject || global.transform.IsChildOf(transform) || transform.IsChildOf(global.transform)))
        {
            hazardComponent = global;
        }
        return hazardComponent;
    }

    /// <summary>
    /// Entrada de controles para propinar un golpe a la máquina.
    /// </summary>
    private void HandleHitInput()
    {
        // Con el extintor en la mano, clic y gatillos son para ROCIAR: antes también golpeaban la máquina
        // en llamas y quemaban al jugador cada vez que intentaba apagarla.
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected) return;

        // [E] solo golpea si no se apunta a un objeto recogible ni se sostiene nada (si no, E es recoger/usar)
        bool teclaELibre = (PlayerInteraction.Instance == null || !PlayerInteraction.Instance.HasTarget) &&
                           (InventorySystem.Instance == null || !InventorySystem.Instance.HasSelectedItem);

        bool hitPressed = false;

        // 1. Ratón / Teclado en PC (ya NO se usa Espacio: es saltar)
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            hitPressed = true;
        }
        if (Keyboard.current != null)
        {
            if (Keyboard.current[Key.F].wasPressedThisFrame) hitPressed = true;
            if (teclaELibre && Keyboard.current[Key.E].wasPressedThisFrame) hitPressed = true;
        }
        if (Gamepad.current != null &&
            (Gamepad.current.buttonWest.wasPressedThisFrame ||
             Gamepad.current.rightTrigger.wasPressedThisFrame))
        {
            hitPressed = true;
        }
#else
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.F) || (teclaELibre && Input.GetKeyDown(KeyCode.E)))
        {
            hitPressed = true;
        }
#endif

        // 2. Mandos de Realidad Virtual (VR)
        if (CheckVRHitInput())
        {
            hitPressed = true;
        }

        if (hitPressed)
        {
            IntentarGolpear();
        }
    }

    /// <summary>Gatillos índice de los mandos VR (el botón A es saltar y el grip es recoger).</summary>
    private bool CheckVRHitInput()
    {
        bool pressed = false;

        try
        {
            if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger) || OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger))
            {
                pressed = true;
            }
        }
        catch { }

        if (!pressed)
        {
            vrRightDevices.Clear();
            vrLeftDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrRightDevices);
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrLeftDevices);

            if (vrRightDevices.Count > 0 && vrRightDevices[0].isValid &&
                vrRightDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trig) && trig)
            {
                pressed = true;
            }
            if (vrLeftDevices.Count > 0 && vrLeftDevices[0].isValid &&
                vrLeftDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigL) && trigL)
            {
                pressed = true;
            }
        }

        bool triggered = pressed && !vrHitLastFrame;
        vrHitLastFrame = pressed;
        return triggered;
    }

    /// <summary>
    /// Intenta golpear la máquina expendedora para que suelte la lata.
    /// Valida si está en llamas o si no tiene más latas disponibles.
    /// </summary>
    /// <returns>True si el golpe fue procesado con éxito, False si falló.</returns>
    [ContextMenu("👊 Probar Golpear Máquina")]
    public bool IntentarGolpear()
    {
        // Enfriamiento entre golpes
        if (Time.time - ultimoGolpeTimestamp < cooldownGolpe)
        {
            return false;
        }

        ultimoGolpeTimestamp = Time.time;

        // REGLA CRÍTICA (sincronizada con VendingMachineHazard): sin compra si arde o si ya se incendió
        if (EstaEnLlamas())
        {
            RechazarGolpePorFuego();
            return false;
        }

        if (CompraBloqueada())
        {
            if (audioSource != null)
            {
                audioSource.PlayOneShot(GenerarAudioImpactoMetalico(), volumenAudio * 0.6f);
            }
            if (InventorySystem.Instance != null)
            {
                InventorySystem.Instance.ShowNotification("⚠️ La máquina quedó averiada por el incendio: ya no da latas.", new Color(1f, 0.6f, 0.3f, 1f), 3f);
            }
            StartCoroutine(SacudirMaquinaRoutine());
            return false;
        }

        // Comprobar si se agotaron las latas
        if (latasDisponibles == 0)
        {
            if (audioSource != null)
            {
                audioSource.PlayOneShot(GenerarAudioImpactoMetalico(), volumenAudio * 0.7f);
            }
            if (InventorySystem.Instance != null)
            {
                InventorySystem.Instance.ShowNotification("❌ ¡Máquina agotada! No quedan más latas disponibles.", new Color(1f, 0.45f, 0.45f, 1f), 2.5f);
            }
            StartCoroutine(SacudirMaquinaRoutine());
            return false;
        }

        // Ejecutar golpe exitoso
        contadorGolpesActuales++;

        // Sonido de golpe metálico contundente
        if (audioSource != null)
        {
            AudioClip clip = sonidoGolpeMetalico != null ? sonidoGolpeMetalico : GenerarAudioImpactoMetalico();
            audioSource.PlayOneShot(clip, volumenAudio);
        }

        // Sacudida física de la máquina por el impacto
        StartCoroutine(SacudirMaquinaRoutine());

        // Comprobar si se alcanzó la cantidad de golpes necesarios para soltar la lata
        if (contadorGolpesActuales >= golpesNecesarios)
        {
            contadorGolpesActuales = 0;
            SoltarLata();
        }
        else
        {
            if (InventorySystem.Instance != null)
            {
                InventorySystem.Instance.ShowNotification($"👊 ¡Golpeaste la máquina! ({contadorGolpesActuales}/{golpesNecesarios}) ¡Golpea otra vez!", new Color(1f, 0.9f, 0.3f, 1f), 2f);
            }
        }

        return true;
    }

    /// <summary>
    /// Acción cuando el jugador intenta golpear la máquina mientras está ardiendo en llamas.
    /// </summary>
    private void RechazarGolpePorFuego()
    {
        // Sonido de quemadura / advertencia
        if (audioSource != null)
        {
            audioSource.PlayOneShot(GenerarAudioQuemadura(), volumenAudio);
        }

        // Notificación de peligro al jugador
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.ShowNotification("🔥 ¡LA MÁQUINA ESTÁ EN LLAMAS! Apaga el fuego con el extintor antes de golpearla.", new Color(1f, 0.35f, 0.15f, 1f), 3.5f);
        }

        // Daño de quemadura en la mano del jugador si está habilitado
        if (quemarSiEstaEnLlamas && PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.TakeDamage(danioQuemadura, PlayerHealth.DamageType.Fire, "Quemadura por golpear máquina en llamas");
        }

        Debug.LogWarning("[Compra] ¡No puedes golpear la máquina expendedora mientras esté en llamas!");
    }

    /// <summary>
    /// Expulsa y suelta el objeto 'lata_Lowpoly' desde la bandeja dispensadora.
    /// </summary>
    [ContextMenu("🥫 Soltar lata_Lowpoly Directamente")]
    public GameObject SoltarLata()
    {
        if (latasDisponibles > 0)
        {
            latasDisponibles--;
        }

        // 1. Determinar posición y rotación de salida (con compensación frontal si la lata es de gran tamaño)
        float offsetFrente = Mathf.Max(0f, (escalaLata - 1f) * 0.05f);
        Vector3 baseSpawnPos = puntoDeSalida != null ? puntoDeSalida.position : (transform.position + transform.forward * 0.48f + Vector3.up * 0.42f);
        Vector3 spawnPos = baseSpawnPos + transform.forward * offsetFrente;
        Quaternion spawnRot = puntoDeSalida != null ? puntoDeSalida.rotation : Quaternion.Euler(Random.Range(-15f, 15f), transform.eulerAngles.y + 90f, 90f);

        // 2. Calcular escala final personalizada y multiplicador general
        Vector3 escalaFinal = Vector3.Scale(escalaPersonalizada, Vector3.one * escalaLata);

        // 3. Instanciar objeto de la lata
        GameObject lataObj = null;
        if (lataPrefab != null)
        {
            lataObj = Instantiate(lataPrefab, spawnPos, spawnRot);
            lataObj.transform.localScale = Vector3.Scale(lataPrefab.transform.localScale, escalaFinal);
        }
        else
        {
            // Respaldo procedural si no se encontró el prefab en disco
            lataObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lataObj.transform.position = spawnPos;
            lataObj.transform.rotation = spawnRot;
            lataObj.transform.localScale = Vector3.Scale(new Vector3(0.065f, 0.06f, 0.065f), escalaFinal);
        }

        // Asignar el nombre exacto requerido: lata_Lowpoly
        lataObj.name = "lata_Lowpoly";

        // Aplicar textura y materiales de refresco a la lata instanciada
        if (aplicarTexturaAutomatica)
        {
            AplicarTexturasALata(lataObj);
        }

        // 4. Collider: si no trae uno, se crea ajustado a la malla. Siempre CONVEXO (obligatorio con Rigidbody dinámico)
        if (lataObj.GetComponentInChildren<Collider>() == null)
        {
            AgregarColliderALata(lataObj);
        }
        foreach (MeshCollider mc in lataObj.GetComponentsInChildren<MeshCollider>())
        {
            mc.convex = true;
        }

        // 5. Rigidbody para expulsión física y rodado realista
        Rigidbody rb = lataObj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = lataObj.AddComponent<Rigidbody>();
        }
        rb.isKinematic = false;
        rb.mass = Mathf.Clamp(0.35f * escalaLata, 0.05f, 10f); // Masa proporcional al tamaño de la lata
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // La lata ignora a la máquina durante su salida (evita el "disparo" por depenetración).
        // Con el jugador no puede chocar: su collider es un trigger y el controlador ignora los objetos recogibles.
        IgnorarColisionConMaquina(lataObj, 0.6f);

        // Impulso hacia adelante para que ruede saliendo de la bandeja
        Vector3 ejectDir = (transform.forward * 0.85f + Vector3.down * 0.15f + transform.right * Random.Range(-0.15f, 0.15f)).normalized;
        rb.linearVelocity = ejectDir * fuerzaExpulsion;
        rb.angularVelocity = new Vector3(Random.Range(-5f, 5f), Random.Range(-2f, 2f), Random.Range(10f, 18f));

        // 6. InteractableItem con tipo estandarizado 'Lata' (curativo). Sin reflexión.
        InteractableItem itemComponent = lataObj.GetComponent<InteractableItem>();
        if (itemComponent == null)
        {
            itemComponent = lataObj.AddComponent<InteractableItem>();
        }
        itemComponent.Configurar(InteractableItem.ItemType.Lata, "Lata de refresco", "Recoger", distanciaRecogerLata, iconoLataGenerado);

        // 6. Reproducir sonido de caída y claqueo de lata
        if (audioSource != null)
        {
            AudioClip dropClip = sonidoCaidaLata != null ? sonidoCaidaLata : GenerarAudioCaidaLata();
            audioSource.PlayOneShot(dropClip, volumenAudio);
        }

        // 7. Notificación visual al jugador
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.ShowNotification("🥫 ¡Has golpeado la máquina y soltó una lata_Lowpoly!", new Color(0.2f, 1f, 0.55f, 1f), 3.5f);
        }

        Debug.Log("[Compra] ¡Golpe exitoso! La máquina soltó el objeto 'lata_Lowpoly'.");

        return lataObj;
    }

    /// <summary>Añade un CapsuleCollider ajustado a la malla de la lata (en el objeto que tiene la malla).</summary>
    private static Collider AgregarColliderALata(GameObject lataObj)
    {
        MeshFilter mf = lataObj.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            CapsuleCollider capDef = lataObj.AddComponent<CapsuleCollider>();
            capDef.radius = 0.033f;
            capDef.height = 0.122f;
            capDef.direction = 1; // Eje Y
            capDef.center = new Vector3(0f, 0.061f, 0f);
            return capDef;
        }

        Bounds lb = mf.sharedMesh.bounds; // espacio local de la malla
        Vector3 s = lb.size;
        int eje = (s.y >= s.x && s.y >= s.z) ? 1 : (s.x >= s.z ? 0 : 2);
        float diametro = eje == 0 ? Mathf.Max(s.y, s.z) : (eje == 1 ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.y));

        CapsuleCollider cap = mf.gameObject.AddComponent<CapsuleCollider>();
        cap.direction = eje;
        cap.center = lb.center;
        cap.radius = diametro * 0.5f;
        cap.height = Mathf.Max(s[eje], diametro);
        return cap;
    }

    private void IgnorarColisionConMaquina(GameObject lataObj, float segundos)
    {
        if (collidersMaquina == null || collidersMaquina.Length == 0)
        {
            collidersMaquina = GetComponentsInChildren<Collider>();
        }

        Collider[] collidersLata = lataObj.GetComponentsInChildren<Collider>();
        AplicarIgnorarColision(collidersLata, true);
        StartCoroutine(RestaurarColisionRoutine(collidersLata, segundos));
    }

    private IEnumerator RestaurarColisionRoutine(Collider[] collidersLata, float segundos)
    {
        yield return new WaitForSeconds(segundos);
        AplicarIgnorarColision(collidersLata, false);
    }

    private void AplicarIgnorarColision(Collider[] collidersLata, bool ignorar)
    {
        foreach (Collider a in collidersLata)
        {
            if (a == null) continue;
            foreach (Collider m in collidersMaquina)
            {
                if (m == null || m.isTrigger) continue;
                Physics.IgnoreCollision(a, m, ignorar);
            }
        }
    }

    /// <summary>
    /// Aplica la textura y los materiales correspondientes a la lata expulsada.
    /// Soporta tanto el modelo 'lata_lowpoly.obj' (Slot 0: Cuerpo/Etiqueta, Slots 1-2: Aluminio)
    /// como cualquier prefab o cilindro.
    /// </summary>
    public void AplicarTexturasALata(GameObject lataObj)
    {
        if (lataObj == null) return;

        InicializarMaterialesYTexturas();

        MeshRenderer[] renderers = lataObj.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            Renderer r = lataObj.GetComponentInChildren<Renderer>();
            if (r != null && materialCuerpoLata != null)
            {
                r.sharedMaterial = materialCuerpoLata;
            }
            return;
        }

        foreach (MeshRenderer mr in renderers)
        {
            int cantMat = mr.sharedMaterials != null ? mr.sharedMaterials.Length : 1;
            Material[] mats = new Material[cantMat];

            for (int i = 0; i < cantMat; i++)
            {
                if (i == 0)
                {
                    // Slot 0: Cuerpo cilíndrico de la lata (donde se mapea la textura con la etiqueta)
                    mats[i] = materialCuerpoLata;
                }
                else
                {
                    // Slots 1 y 2: Aluminio metálico (tapa superior, base y anilla)
                    mats[i] = materialAluminioLata != null ? materialAluminioLata : materialCuerpoLata;
                }
            }

            mr.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// Inicializa la textura y los materiales del cuerpo y aluminio de la lata.
    /// Si no han sido asignados en el Inspector, los carga de Assets o genera una textura procedural de respaldo.
    /// </summary>
    public void InicializarMaterialesYTexturas()
    {
        // 1. Cargar o generar textura de la lata
        if (texturaLata == null)
        {
#if UNITY_EDITOR
            texturaLata = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/T_Lata_Refresco.png");
            if (texturaLata != null)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            if (texturaLata == null)
            {
                texturaLata = GenerarTexturaRefrescoProcedural();
            }
        }

        // 2. Cargar o crear material del cuerpo de la lata
        if (materialCuerpoLata == null)
        {
#if UNITY_EDITOR
            materialCuerpoLata = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Lata_Refresco.mat");
#endif
            if (materialCuerpoLata == null)
            {
                materialCuerpoLata = CrearMaterialLit("M_Lata_Refresco_Auto", texturaLata, colorBaseLata, metalicoCuerpo, suavidadCuerpo);
            }
            else if (texturaLata != null)
            {
                AsignarPropiedadesAMaterial(materialCuerpoLata, texturaLata, colorBaseLata, metalicoCuerpo, suavidadCuerpo);
            }
        }
        else if (texturaLata != null)
        {
            AsignarPropiedadesAMaterial(materialCuerpoLata, texturaLata, colorBaseLata, metalicoCuerpo, suavidadCuerpo);
        }

        // 3. Cargar o crear material metálico de aluminio (tapa, base y anilla)
        if (materialAluminioLata == null)
        {
#if UNITY_EDITOR
            materialAluminioLata = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Lata_Aluminio.mat");
#endif
            if (materialAluminioLata == null)
            {
                materialAluminioLata = CrearMaterialLit("M_Lata_Aluminio_Auto", null, new Color(0.85f, 0.87f, 0.90f, 1f), metalicoAluminio, suavidadAluminio);
            }
        }
    }

    /// <summary>
    /// Actualiza dinámicamente las propiedades visuales en los materiales de la lata.
    /// </summary>
    public void ActualizarMaterialesLata()
    {
        if (materialCuerpoLata != null)
        {
            AsignarPropiedadesAMaterial(materialCuerpoLata, texturaLata, colorBaseLata, metalicoCuerpo, suavidadCuerpo);
        }
        if (materialAluminioLata != null)
        {
            AsignarPropiedadesAMaterial(materialAluminioLata, null, new Color(0.85f, 0.87f, 0.90f, 1f), metalicoAluminio, suavidadAluminio);
        }
    }

    private Material CrearMaterialLit(string nombre, Texture2D textura, Color color, float metalico, float suavidad)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");
        if (shader == null) shader = Shader.Find("Diffuse");

        Material mat = new Material(shader) { name = nombre };
        AsignarPropiedadesAMaterial(mat, textura, color, metalico, suavidad);
        return mat;
    }

    private void AsignarPropiedadesAMaterial(Material mat, Texture2D tex, Color color, float metalico, float suavidad)
    {
        if (mat == null) return;

        // URP (Universal Render Pipeline)
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

        // Standard / Built-in Pipeline
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        // Parámetros PBR
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metalico);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", suavidad);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", suavidad);
    }

    /// <summary>
    /// Genera una textura de refresco procedural completa en caso de que no haya archivo externo.
    /// Garantiza que la lata NUNCA quede sin textura ni en blanco, incluso en builds de Quest / Android.
    /// </summary>
    private Texture2D GenerarTexturaRefrescoProcedural()
    {
        int w = 512;
        int h = 256;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "TexturaRefresco_Procedural",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / h;

            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w;
                int idx = y * w + x;

                // 1. Bordes superior e inferior de aluminio plateado
                if (v >= 0.90f || v <= 0.10f)
                {
                    float aluGrad = v >= 0.90f ? (v - 0.90f) / 0.10f : (0.10f - v) / 0.10f;
                    pixels[idx] = Color.Lerp(new Color(0.85f, 0.87f, 0.90f, 1f), new Color(0.65f, 0.67f, 0.70f, 1f), aluGrad);
                    continue;
                }

                // 2. Líneas doradas de acento
                if (Mathf.Abs(v - 0.89f) < 0.012f || Mathf.Abs(v - 0.11f) < 0.012f)
                {
                    pixels[idx] = new Color(0.96f, 0.82f, 0.25f, 1f);
                    continue;
                }

                // 3. Fondo rojo metálico con reflejo cilíndrico
                float highlight = Mathf.Exp(-Mathf.Pow((u - 0.28f) * 10f, 2f)) * 0.25f;
                float highlight2 = Mathf.Exp(-Mathf.Pow((u - 0.72f) * 12f, 2f)) * 0.18f;
                float redGrad = Mathf.Lerp(0.88f, 0.55f, v);
                Color baseRed = new Color(redGrad + highlight + highlight2, 0.08f + highlight * 0.5f, 0.12f + highlight * 0.5f, 1f);

                // 4. Onda blanca dinámica (swoosh de soda)
                float wave = 0.42f + Mathf.Sin(u * Mathf.PI * 2f) * 0.10f;
                float distWave = Mathf.Abs(v - wave);

                if (distWave < 0.035f)
                {
                    pixels[idx] = Color.white;
                }
                else if (distWave < 0.05f)
                {
                    pixels[idx] = new Color(0.96f, 0.82f, 0.25f, 1f); // Borde dorado de la ola
                }
                else if (u >= 0.55f && u <= 0.75f && v >= 0.25f && v <= 0.75f)
                {
                    // Panel blanco de información nutricional en la parte trasera
                    if (u <= 0.56f || u >= 0.74f || v <= 0.26f || v >= 0.74f || (y % 16 < 2))
                    {
                        pixels[idx] = new Color(0.15f, 0.15f, 0.15f, 1f); // Líneas de la tabla
                    }
                    else
                    {
                        pixels[idx] = new Color(0.95f, 0.95f, 0.95f, 1f);
                    }
                }
                else if (u >= 0.80f && u <= 0.94f && v >= 0.35f && v <= 0.65f)
                {
                    // Código de barras
                    bool isBar = (x % 5 < 3) && (x % 7 != 0);
                    pixels[idx] = isBar ? Color.black : Color.white;
                }
                else
                {
                    pixels[idx] = baseRed;
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    [ContextMenu("🎨 Cargar/Generar Texturas de la Lata")]
    public void RecargarTexturasDesdeMenu()
    {
        texturaLata = null;
        materialCuerpoLata = null;
        materialAluminioLata = null;
        InicializarMaterialesYTexturas();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log("[Compra] Texturas y materiales de la lata recargados correctamente.");
    }

    [ContextMenu("🥫 Aplicar Textura a Latas en Escena")]
    public void AplicarTexturasALatasEnEscena()
    {
        InicializarMaterialesYTexturas();
        foreach (GameObject go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name.ToLower().Contains("lata"))
            {
                AplicarTexturasALata(go);
            }
        }
    }

    /// <summary>
    /// Animación de sacudida y retroceso físico de la máquina expendedora al recibir un golpe.
    /// </summary>
    private IEnumerator SacudirMaquinaRoutine()
    {
        if (estaSacudiendose) yield break;
        estaSacudiendose = true;

        float duracion = 0.28f;
        float elapsed = 0f;

        while (elapsed < duracion)
        {
            elapsed += Time.deltaTime;
            float damper = 1f - (elapsed / duracion);

            // Sacudida en el eje horizontal y leve inclinación hacia atrás
            float joltX = (Random.value * 2f - 1f) * intensidadSacudida * damper;
            float joltZ = -Mathf.Sin(elapsed / duracion * Mathf.PI) * (intensidadSacudida * 0.8f);
            float tilt = (Random.value * 2f - 1f) * 1.8f * damper;

            transform.position = posicionOriginal + transform.TransformDirection(new Vector3(joltX, 0f, joltZ));
            transform.rotation = rotacionOriginal * Quaternion.Euler(tilt * 0.5f, 0f, tilt);

            yield return null;
        }

        transform.position = posicionOriginal;
        transform.rotation = rotacionOriginal;
        estaSacudiendose = false;
    }

    /// <summary>
    /// Soporte para golpe físico mediante colisión directa de puño, pie o jugador.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // Si choca a cierta velocidad o es el jugador
        if (collision.relativeVelocity.magnitude > 1.8f)
        {
            if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<Camera>() != null)
            {
                IntentarGolpear();
            }
        }
    }

    public bool IsPlayerInRange()
    {
        Camera cam = GetPlayerCamera();
        if (cam == null) return false;

        Vector3 p = cam.transform.position;
        Collider col = GetComponentInChildren<Collider>();
        float dist = col != null
            ? Vector3.Distance(p, CameraMovement.PuntoMasCercano(col, p))
            : Vector3.Distance(p, transform.position);

        return dist <= distanciaGolpe;
    }

    public bool IsPlayerAimingAtMachine()
    {
        Camera cam = GetPlayerCamera();
        if (cam == null) return false;

        // VR: rayo desde los mandos
        CameraMovement cm = CameraMovement.Instance;
        if (cm != null && cm.IsVRPresent)
        {
            if (RayoGolpeaMaquina(cm.VRRightControllerAnchor, distanciaGolpe + 2.5f)) return true;
            if (RayoGolpeaMaquina(cm.VRLeftControllerAnchor, distanciaGolpe + 2.5f)) return true;
        }

        // Rayo desde la mirada (ignorando los colliders del propio jugador)
        if (RayoGolpeaMaquina(cam.transform, distanciaGolpe + 2f)) return true;

        // Tolerancia: mirando claramente a la máquina (~30°). Antes bastaba con ~63° y se golpeaba sin querer.
        Collider col = GetComponentInChildren<Collider>();
        Vector3 centro = col != null ? col.bounds.center : transform.position + Vector3.up;
        Vector3 dir = (centro - cam.transform.position).normalized;
        return Vector3.Dot(cam.transform.forward, dir) > 0.85f;
    }

    private bool RayoGolpeaMaquina(Transform origen, float distancia)
    {
        if (origen == null) return false;

        Transform raizJugador = CameraMovement.Instance != null ? CameraMovement.Instance.transform : origen;
        int n = Physics.RaycastNonAlloc(origen.position, origen.forward, bufferRayos, distancia, ~0, QueryTriggerInteraction.Ignore);

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

        return golpeado != null && (golpeado == transform || golpeado.IsChildOf(transform));
    }

    private static bool EsModoTactil()
    {
        return CameraMovement.Instance != null ? CameraMovement.Instance.IsTouchActive : (Application.isMobilePlatform && !UIVR.Activo);
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
            cachedPlayerCamera = FindAnyObjectByType<Camera>();
        }

        return cachedPlayerCamera;
    }

    private void LateUpdate()
    {
        // En VR OnGUI no se ve: el aviso de la máquina va al HUD VR
        if (!HUDVR.Activo || !mostrarHUD || MovementSystemSelector.IsGameplayBlocked) return;
        if (!jugadorEnRangoCache || !jugadorApuntandoCache) return;

        if (CompraBloqueada())
        {
            HUDVR.Mensaje(CanalHUD.Maquina, EstaEnLlamas()
                ? "¡MÁQUINA EN LLAMAS! No puedes comprar ni golpearla"
                : "Máquina averiada por el incendio: ya no da latas", new Color(1f, 0.55f, 0.3f, 1f));
        }
        else
        {
            bool extintorEnMano = InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected;
            HUDVR.Mensaje(CanalHUD.Maquina, extintorEnMano
                ? "Guarda el extintor con [Y] para golpear la máquina"
                : "[Gatillo] Golpear la máquina para sacar una lata", new Color(0.4f, 0.95f, 1f, 1f));
        }
    }

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (LateUpdate)
        if (!mostrarHUD || MovementSystemSelector.IsGameplayBlocked) return;
        if (!jugadorEnRangoCache || !jugadorApuntandoCache) return;

        EnsureGUIStyles();

        bool enLlamas = CompraBloqueada();

        if (enLlamas)
        {
            // Prompt de ADVERTENCIA cuando la máquina está en llamas
            Rect warnRect = new Rect((Screen.width - 380f) * 0.5f, Screen.height * 0.5f + 85f, 380f, 34f);

            Color prevC = GUI.color;
            GUI.color = new Color(0.32f, 0.08f, 0.08f, 0.92f);
            GUI.DrawTexture(warnRect, promptWarningBgTex);
            GUI.color = new Color(1f, 0.4f, 0.2f, 0.85f);
            DrawBorder(warnRect, 2);
            GUI.color = prevC;

            GUI.Label(warnRect, EstaEnLlamas()
                ? "🔥 ¡MÁQUINA EN LLAMAS! No puedes comprar ni golpearla"
                : "⚠️ Máquina averiada por el incendio: ya no da latas", promptWarningStyle);
        }
        else
        {
            // Prompt para GOLPEAR la máquina cuando NO está en llamas
            bool extintorEnMano = InventorySystem.Instance != null && InventorySystem.Instance.IsExtinguisherSelected;
            string hitKeyText = extintorEnMano
                ? "Guarda el extintor [Q] para golpear la máquina"
                : "[Clic / F] Golpear máquina para sacar lata";
            Rect promptRect = new Rect((Screen.width - 320f) * 0.5f, Screen.height * 0.5f + 85f, 320f, 32f);

            Color prevC = GUI.color;
            GUI.color = new Color(0.08f, 0.16f, 0.25f, 0.9f);
            GUI.DrawTexture(promptRect, promptBgTex);
            GUI.color = new Color(0.2f, 0.95f, 1f, 0.85f);
            DrawBorder(promptRect, 2);
            GUI.color = prevC;

            GUI.Label(promptRect, $"👊 {hitKeyText}", promptBoxStyle);

            // Botón táctil para golpear (solo en modo táctil: con el cursor bloqueado no se puede pulsar)
            if (EsModoTactil() && !extintorEnMano)
            {
                float btnWidth = 150f;
                float btnHeight = 65f;
                Rect touchBtnRect = new Rect(Screen.width - btnWidth - 30f, Screen.height * 0.5f + 45f, btnWidth, btnHeight);

                Color btnColor = GUI.color;
                GUI.color = new Color(0.15f, 0.85f, 1f, 0.95f);
                if (GUI.Button(touchBtnRect, "👊 GOLPEAR\nMÁQUINA", touchButtonStyle))
                {
                    IntentarGolpear();
                }
                GUI.color = btnColor;
            }
        }
    }

    private void DrawBorder(Rect r, int thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), buttonBorderTex);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), buttonBorderTex);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), buttonBorderTex);
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), buttonBorderTex);
    }

    private void EnsureGUIStyles()
    {
        if (promptBgTex == null)
        {
            promptBgTex = CreateColorTexture(Color.white);
            promptWarningBgTex = CreateColorTexture(Color.white);
            buttonBorderTex = CreateColorTexture(Color.white);
        }

        if (promptBoxStyle == null)
        {
            promptBoxStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            promptBoxStyle.normal.textColor = Color.white;
        }

        if (promptWarningStyle == null)
        {
            promptWarningStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            promptWarningStyle.normal.textColor = new Color(1f, 0.65f, 0.25f, 1f);
        }

        if (touchButtonStyle == null)
        {
            touchButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            touchButtonStyle.normal.textColor = Color.white;
        }
    }

    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private void CrearIconoLata()
    {
        if (iconoLataGenerado != null) return;

        int size = 64;
        iconoLataGenerado = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (float)x / size;
                float ny = (float)y / size;

                // Forma cilíndrica de la lata
                if (nx >= 0.28f && nx <= 0.72f && ny >= 0.18f && ny <= 0.82f)
                {
                    // Tapa y base de aluminio plateado
                    if (ny >= 0.76f || ny <= 0.24f)
                    {
                        iconoLataGenerado.SetPixel(x, y, new Color(0.85f, 0.87f, 0.90f, 1f));
                    }
                    else if (ny >= 0.73f || ny <= 0.27f)
                    {
                        // Ribete dorado de acento
                        iconoLataGenerado.SetPixel(x, y, new Color(0.96f, 0.82f, 0.25f, 1f));
                    }
                    else
                    {
                        // Cuerpo rojo con sombreado cilíndrico y ola de cola
                        float shade = Mathf.Sin((nx - 0.28f) / (0.72f - 0.28f) * Mathf.PI);
                        float waveY = 0.46f + Mathf.Sin(nx * Mathf.PI * 2.5f) * 0.07f;
                        bool isWave = Mathf.Abs(ny - waveY) < 0.045f;

                        if (isWave)
                        {
                            iconoLataGenerado.SetPixel(x, y, Color.white);
                        }
                        else
                        {
                            iconoLataGenerado.SetPixel(x, y, new Color(0.88f * shade, 0.10f * shade, 0.14f * shade, 1f));
                        }
                    }
                }
                else
                {
                    iconoLataGenerado.SetPixel(x, y, Color.clear);
                }
            }
        }
        iconoLataGenerado.Apply();
    }

    // =========================================================================
    // GENERACIÓN PROCEDURAL DE EFECTOS DE AUDIO (GARANTÍA 100% FUNCIONAL)
    // =========================================================================

    private AudioClip GenerarAudioImpactoMetalico()
    {
        int sampleRate = 44100;
        float duration = 0.35f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 14f);

            // Resonancia metálica con golpe sordo (frecuencias 180Hz, 440Hz, 880Hz y ruido)
            float thud = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.5f;
            float metallicRing = Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 1140f * t) * 0.2f;
            float noise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 35f) * 0.45f;

            samples[i] = (thud + metallicRing + noise) * envelope;
        }

        AudioClip clip = AudioClip.Create("HitVendingMetal", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip GenerarAudioCaidaLata()
    {
        int sampleRate = 44100;
        float duration = 0.45f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 9f);

            // Claqueo de lata ligera de aluminio al caer y rodar (frecuencia aguda y resonancia hueca)
            float clatter1 = Mathf.Sin(2f * Mathf.PI * 980f * t) * 0.4f;
            float clatter2 = Mathf.Sin(2f * Mathf.PI * 1620f * t) * 0.3f;
            float rattle = (Random.value * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.25f;

            samples[i] = (clatter1 + clatter2 + rattle) * envelope;
        }

        AudioClip clip = AudioClip.Create("CanDropAudio", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip GenerarAudioQuemadura()
    {
        int sampleRate = 44100;
        float duration = 0.4f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 8f);

            // Sonido de chisporroteo / quemadura al tocar fuego
            float hiss = (Random.value * 2f - 1f) * envelope * 0.7f;
            samples[i] = hiss;
        }

        AudioClip clip = AudioClip.Create("BurnSizzleAudio", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

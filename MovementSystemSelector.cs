using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// MENÚ PRINCIPAL ESTILO MINECRAFT — "EARTHQUAKE SIMULATOR"
///
/// Pantallas (se generan solas en tiempo de ejecución, no hace falta montar ningún Canvas):
///  1. Título: logo EARTHQUAKE SIMULATOR (tiembla cada pocos segundos), frase amarilla que late,
///     la escena girando de fondo (panorama) y los botones Jugar / Controles / Salir del juego.
///  2. Seleccionar modo de juego: lista de modos (Simulacro, Supervivencia, Extremo) como la lista de
///     mundos de Minecraft, botón para cambiar el sistema de control y Comenzar partida / Cancelar.
///  3. Controles: lista de controles del sistema elegido y botón Hecho.
///
/// Durante la partida, M / F1 (o Start en el mando VR) abre el mismo menú como PAUSA:
/// Volver al juego / Controles / Menú principal / Salir del juego. El juego se congela (Time.timeScale = 0).
///
/// Mientras el menú está abierto el jugador no puede moverse ni girar la cámara y el cursor queda libre.
///
/// EN META QUEST (VR): un Canvas "Screen Space - Overlay" no se dibuja dentro del visor, así que con visor
/// activo el menú se convierte en un panel flotante delante de los ojos y se maneja con un láser que sale
/// del mando (apuntar + gatillo). Ver la clase UIVR al final de este archivo.
/// </summary>
public class MovementSystemSelector : MonoBehaviour
{
    public static MovementSystemSelector Instance { get; private set; }

    public enum MovementPreset
    {
        TecladoYMouse = 0,
        VR_Continuo = 1,
        VR_Teletransporte = 2,
        Tactil_Mobile = 3,
        Universal_Todos = 4
    }

    private enum Pantalla { Titulo, Modos, Controles }

    [Header("--- CONFIGURACIÓN DEL SELECTOR ---")]
    [Tooltip("Muestra el menú principal al arrancar la partida")]
    [SerializeField] private bool mostrarAlIniciar = true;

    [Tooltip("Permite abrir el menú de pausa durante la partida con M / F1 (o el botón Start del mando VR)")]
    [SerializeField] private bool permitirReabrirConTecla = true;

    [Tooltip("Muestra un botón flotante 'Menú [M]' cuando el cursor está libre (modo táctil / universal)")]
    [SerializeField] private bool mostrarBotonFlotanteHUD = true;

    [Tooltip("Sistema de control por defecto si no hay preferencia guardada")]
    [SerializeField] private MovementPreset preajusteInicial = MovementPreset.TecladoYMouse;

    [Header("--- MENÚ PRINCIPAL (ESTILO MINECRAFT) ---")]
    [Tooltip("Título grande del menú principal")]
    [SerializeField] private string tituloMenu = "EARTHQUAKE SIMULATOR";

    [Tooltip("Frases amarillas que aparecen al azar junto al título (como las 'splash' de Minecraft)")]
    [SerializeField] private string[] frasesAmarillas =
    {
        "¡Agáchate, cúbrete y sujétate!",
        "¡Ten el extintor a mano!",
        "¡Aléjate de las ventanas!",
        "¡Magnitud 7.8!",
        "¡Mantén la calma!",
        "¡Ahora con latas!",
        "¡No uses el ascensor!",
        "¡100% sísmico!",
        "¡Ubica la salida de emergencia!",
        "¡Cuidado con la máquina expendedora!"
    };

    [Tooltip("Texto de versión (esquina inferior izquierda)")]
    [SerializeField] private string textoVersion = "Earthquake Simulator v1.0";

    [Tooltip("Texto de la esquina inferior derecha")]
    [SerializeField] private string textoPie = "Simulacro educativo de sismos";

    [Tooltip("(Opcional) Fuente para todo el menú. Puedes arrastrar aquí una fuente pixelada tipo Minecraft (.ttf)")]
    [SerializeField] private Font fuenteMenu;

    [Tooltip("(Opcional) Sonido al pulsar botones. Si se deja vacío se genera un 'clic' procedural")]
    [SerializeField] private AudioClip sonidoClic;

    [Tooltip("Velocidad (grados/segundo) a la que gira la escena detrás del menú principal. 0 = sin giro")]
    [Range(0f, 20f)]
    [SerializeField] private float velocidadPanorama = 4f;

    [Tooltip("El título tiembla cada pocos segundos como si hubiera un sismo")]
    [SerializeField] private bool animarTituloConSismo = true;

    [Header("--- MENÚ EN REALIDAD VIRTUAL (META QUEST) ---")]
    [Tooltip("Distancia (metros) a la que flota el menú delante de los ojos cuando hay un visor VR")]
    [Range(0.8f, 4f)]
    [SerializeField] private float distanciaMenuVR = 1.6f;

    [Tooltip("Ancho (metros) del menú flotante en VR")]
    [Range(0.8f, 4f)]
    [SerializeField] private float anchoMenuVR = 1.9f;

    [Tooltip("En VR el menú se dibuja por encima de paredes y objetos para que nunca quede tapado")]
    [SerializeField] private bool menuVRSiempreVisible = true;

    [Header("--- MODOS DE JUEGO ---")]
    [Tooltip("Modo seleccionado por defecto la primera vez")]
    [SerializeField] private TipoModoJuego modoDeJuegoInicial = TipoModoJuego.Supervivencia;

    [Tooltip("Ajustes de cada modo, en orden: Simulacro, Supervivencia, Extremo")]
    [SerializeField] private AjustesModoJuego[] ajustesModos = AjustesModoJuego.CrearPorDefecto();

    // Claves de PlayerPrefs
    private const string PREF_SAVED_PRESET = "Movement_SavedPreset";
    private const string PREF_VR_SNAP_TURN = "Movement_VRSnapTurn";
    private const string PREF_RIGHT_CLICK_LOOK = "Movement_RightClickLook";
    private const string PREF_GAME_MODE = "EQ_GameMode";

    // Estado del menú
    private bool isOpen = false;
    private int frameCierre = -10;
    private Pantalla pantallaActual = Pantalla.Titulo;
    private MovementPreset currentPreset = MovementPreset.TecladoYMouse;
    private TipoModoJuego modoSeleccionado = TipoModoJuego.Supervivencia;

    // Opciones derivadas del sistema de control
    private CameraMovement.LocomotionMode currentLocomotion = CameraMovement.LocomotionMode.Continuo;
    private bool currentVRSnapTurn = false;
    private bool currentRightClickLook = false;

    // Referencias de la UI generada
    private Canvas canvasMenu;
    private Text textoAyudaModos;
    private GameObject raizMenu;
    private GameObject panelTitulo;
    private GameObject panelModos;
    private GameObject panelControles;
    private Image fondoTitulo;
    private RectTransform rtTitulo;
    private Vector2 posTituloBase;
    private RectTransform rtFrase;
    private Text textoFrase;
    private Text textoInfoPausa;
    private Button botonJugar;
    private Button botonControles;
    private Button botonMenuPrincipal;
    private Button botonSalir;
    private readonly Button[] botonesModo = new Button[3];
    private readonly GameObject[] marcosModo = new GameObject[3];
    private readonly Text[] nombresModo = new Text[3];
    private Button botonCicloControles;
    private Text textoPreset;
    private Text textoListaControles;

    // Audio y animaciones
    private AudioSource audioMenu;
    private AudioClip clipClicGenerado;
    private bool panoramaActivo = false;
    private bool yawGuardado = false;
    private float yawInicialPanorama = 0f;
    private float proximoTemblorTitulo = 0f;
    private float finTemblorTitulo = 0f;
    private const float DURACION_TEMBLOR_TITULO = 0.7f;
    private int ultimoClicModoIndice = -1;
    private float ultimoClicModoTiempo = -10f;
    private int ultimoClicModoFrame = -1;
    private float siguienteNavegacionVR = 0f;

    // Notificación breve (OnGUI)
    private string toastMessage = "";
    private float toastTimer = 0f;
    private const float TOAST_DURATION = 3.5f;
    private GUIStyle toastStyle;
    private GUIStyle hudButtonStyle;
    private GUIStyle etiquetaModoStyle;
    private Texture2D panelBgTex;

    private static bool hasSelectedAtLeastOnce = false;
    public static bool HasSelectedMovementSystem => hasSelectedAtLeastOnce;

    /// <summary>
    /// Si es true, la siguiente carga de escena empieza a jugar directamente sin mostrar el menú
    /// (lo usa el botón "Volver a intentarlo" del Game Over).
    /// </summary>
    public static bool SaltarMenuEnProximaCarga = false;

    public bool IsOpen => isOpen;
    public MovementPreset CurrentPreset => currentPreset;
    public TipoModoJuego ModoSeleccionado => modoSeleccionado;

    /// <summary>
    /// Punto ÚNICO para saber si el juego debe ignorar las entradas: menú abierto, el frame en que se cerró
    /// (el clic de "Comenzar" no debe golpear/rociar nada) o jugador muerto.
    /// </summary>
    public static bool IsGameplayBlocked
    {
        get
        {
            MovementSystemSelector s = Instance;
            if (s != null && (s.isOpen || Time.frameCount <= s.frameCierre + 1)) return true;

            PlayerHealth ph = PlayerHealth.Instance;
            if (ph != null && !ph.IsAlive) return true;

            return false;
        }
    }

    // =========================================================================
    // CICLO DE VIDA
    // =========================================================================

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

        hasSelectedAtLeastOnce = false;
        AsegurarAjustesModos();
        AplicarAjustesVR();

        // Bloquear desde el primer frame para que nada se mueva antes de mostrar el menú
        isOpen = mostrarAlIniciar && !SaltarMenuEnProximaCarga;
    }

    private void OnValidate()
    {
        AsegurarAjustesModos();
        AplicarAjustesVR();
    }

    private void AplicarAjustesVR()
    {
        UIVR.Distancia = distanciaMenuVR;
        UIVR.Ancho = anchoMenuVR;
        UIVR.SiempreVisible = menuVRSiempreVisible;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        UIVR.Soltar(canvasMenu);

        // Nunca dejar el juego congelado si el menú se destruye (cambio de escena, etc.)
        Time.timeScale = 1f;
        AudioListener.pause = false;
        MenuUIBuilder.AlPulsarBoton = null;
    }

    private void Start()
    {
        // Sistema de control guardado o detectado
        int savedPresetInt = PlayerPrefs.GetInt(PREF_SAVED_PRESET, -1);
        currentPreset = (savedPresetInt >= 0 && savedPresetInt <= 4)
            ? (MovementPreset)savedPresetInt
            : DetectSuggestedPreset();

        // ANTES: "si es móvil y no hay preferencia -> Táctil". Meta Quest es Android (isMobilePlatform = true),
        // así que en la primera partida se forzaba el control táctil y los controles VR quedaban desactivados.
        // DetectSuggestedPreset ya devuelve Táctil en móviles SIN visor; con visor solo se permiten sistemas VR.
        currentPreset = AjustarPresetAlDispositivo(currentPreset);
        SyncDetailedOptionsFromPreset(currentPreset);

        // Modo de juego guardado
        int modoGuardado = PlayerPrefs.GetInt(PREF_GAME_MODE, (int)modoDeJuegoInicial);
        modoSeleccionado = (modoGuardado >= 0 && modoGuardado <= 2) ? (TipoModoJuego)modoGuardado : modoDeJuegoInicial;

        // Construir la interfaz
        MenuUIBuilder.FuentePersonalizada = fuenteMenu;
        MenuUIBuilder.AlPulsarBoton = ReproducirClic;
        ConstruirMenu();
        MenuUIBuilder.AsegurarEventSystem();

        if (SaltarMenuEnProximaCarga || !mostrarAlIniciar)
        {
            // "Volver a intentarlo" o menú desactivado: empezar a jugar directamente con lo último elegido
            SaltarMenuEnProximaCarga = false;
            AplicarModoDeJuego(modoSeleccionado);
            ApplyPreset(currentPreset, false);
            CloseMenu(false);
            ShowToast($"Modo {ModoDeJuego.Nombre}  ·  {GetPresetDisplayName(currentPreset)}");
        }
        else
        {
            OpenMenu();
        }
    }

    private static bool EsPresetVR(MovementPreset preset)
    {
        return preset == MovementPreset.VR_Continuo || preset == MovementPreset.VR_Teletransporte;
    }

    /// <summary>
    /// Con el visor puesto solo tienen sentido los sistemas VR: con "Teclado y ratón" o "Táctil"
    /// CameraMovement desactiva los controles VR y el jugador no puede moverse.
    /// </summary>
    private static MovementPreset AjustarPresetAlDispositivo(MovementPreset preset)
    {
        if (UIVR.Activo && !EsPresetVR(preset)) return MovementPreset.VR_Continuo;
        return preset;
    }

    private MovementPreset DetectSuggestedPreset()
    {
        try
        {
            if (UIVR.Activo) return MovementPreset.VR_Continuo;

            var devs = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devs);
            if (devs.Count > 0) return MovementPreset.VR_Continuo;
        }
        catch { }

        if (Application.isMobilePlatform) return MovementPreset.Tactil_Mobile;

        try
        {
            if (Input.touchSupported && !Application.isEditor) return MovementPreset.Tactil_Mobile;
        }
        catch { } // Input clásico desactivado (solo Input System): se ignora

        return preajusteInicial;
    }

    private void Update()
    {
        if (toastTimer > 0f) toastTimer -= Time.unscaledDeltaTime;

        // Si el visor se activa después de arrancar (o había guardado un sistema no VR), pasar a controles VR
        if (!EsPresetVR(currentPreset) && UIVR.Activo)
        {
            SelectPreset(MovementPreset.VR_Continuo);
            RefrescarTextos();
            if (hasSelectedAtLeastOnce && !isOpen) ApplySettingsToCamera();
        }

        bool vivo = PlayerHealth.Instance == null || PlayerHealth.Instance.IsAlive;

        // Abrir / cerrar el menú de pausa durante la partida
        if (permitirReabrirConTecla && vivo && hasSelectedAtLeastOnce && TeclaMenuPulsada())
        {
            if (isOpen) Continuar();
            else OpenMenu();
            return;
        }

        if (!isOpen) return;

        AnimarMenu();
        ProcesarNavegacionMenu();
    }

    private void LateUpdate()
    {
        // VR: coloca el menú delante del visor y mueve el láser del mando.
        // En LateUpdate porque el OVRCameraRig actualiza la cabeza y los mandos en su Update.
        if (isOpen) UIVR.Actualizar(canvasMenu);

        // VR: HUD flotante (vida, avisos, inventario) en lugar de OnGUI, que no se ve dentro del visor
        if (HUDVR.Activo)
        {
            bool vivo = PlayerHealth.Instance == null || PlayerHealth.Instance.IsAlive;
            string etiqueta = hasSelectedAtLeastOnce && ModoDeJuego.SinDanio ? "SIMULACRO · SIN DAÑO" : null;
            HUDVR.Actualizar(!isOpen && vivo && hasSelectedAtLeastOnce, !isOpen, etiqueta);
        }
    }

    private bool TeclaMenuPulsada()
    {
        bool pulsada = false;

        // Solo el botón Start/Menú del mando VR (el B es "agacharse")
        try { if (OVRInput.GetDown(OVRInput.Button.Start)) pulsada = true; } catch { }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.mKey.wasPressedThisFrame || Keyboard.current.f1Key.wasPressedThisFrame)) pulsada = true;
#else
        if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.F1)) pulsada = true;
#endif
        return pulsada;
    }

    // =========================================================================
    // ANIMACIONES DEL MENÚ (tiempo sin escalar: funcionan con el juego en pausa)
    // =========================================================================

    private void AnimarMenu()
    {
        float t = Time.unscaledTime;

        // 1. Frase amarilla que "late" como en Minecraft
        if (rtFrase != null && rtFrase.gameObject.activeInHierarchy)
        {
            float escala = 1f + Mathf.Abs(Mathf.Sin(t * 5.5f)) * 0.09f;
            rtFrase.localScale = new Vector3(escala, escala, 1f);
        }

        // 2. El título tiembla cada pocos segundos (¡es un simulador de sismos!)
        if (rtTitulo != null && pantallaActual == Pantalla.Titulo)
        {
            if (animarTituloConSismo && t >= proximoTemblorTitulo)
            {
                finTemblorTitulo = t + DURACION_TEMBLOR_TITULO;
                proximoTemblorTitulo = t + Random.Range(4f, 7f);
            }

            if (animarTituloConSismo && t < finTemblorTitulo)
            {
                float k = (finTemblorTitulo - t) / DURACION_TEMBLOR_TITULO;
                rtTitulo.anchoredPosition = posTituloBase + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (10f * k);
                rtTitulo.localEulerAngles = new Vector3(0f, 0f, Random.Range(-1f, 1f) * 1.5f * k);
            }
            else
            {
                rtTitulo.anchoredPosition = posTituloBase;
                rtTitulo.localEulerAngles = Vector3.zero;
            }
        }

        // 3. Panorama: la escena gira lentamente detrás del menú principal (no en VR: marea)
        if (panoramaActivo && velocidadPanorama > 0f && CameraMovement.Instance != null && !UIVR.Activo)
        {
            if (!yawGuardado)
            {
                yawInicialPanorama = CameraMovement.Instance.Yaw;
                yawGuardado = true;
            }
            CameraMovement.Instance.Yaw += velocidadPanorama * Time.unscaledDeltaTime;
        }
    }

    /// <summary>Atajos de teclado y de mando VR (además del láser VR y del ratón).</summary>
    private void ProcesarNavegacionMenu()
    {
        bool confirmar = false, atras = false, arriba = false, abajo = false, izquierda = false, derecha = false;

#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) confirmar = true;
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) atras = true;
            if (kb.upArrowKey.wasPressedThisFrame) arriba = true;
            if (kb.downArrowKey.wasPressedThisFrame) abajo = true;
            if (kb.leftArrowKey.wasPressedThisFrame) izquierda = true;
            if (kb.rightArrowKey.wasPressedThisFrame) derecha = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) confirmar = true;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) atras = true;
        if (Input.GetKeyDown(KeyCode.UpArrow)) arriba = true;
        if (Input.GetKeyDown(KeyCode.DownArrow)) abajo = true;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) izquierda = true;
        if (Input.GetKeyDown(KeyCode.RightArrow)) derecha = true;
#endif

        try
        {
            if (UIVR.PunteroEnUso)
            {
                // Con el láser VR, el gatillo (y A / X) pulsan el botón apuntado: lo gestiona UIVR.
                // A solo hace de "confirmar" si el láser no apunta a ningún botón (si no, se pulsarían dos cosas).
                if (OVRInput.GetDown(OVRInput.Button.One) && !UIVR.ApuntandoBoton) confirmar = true;
            }
            else if (OVRInput.GetDown(OVRInput.Button.One) ||
                     OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                     OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
            {
                confirmar = true;
            }
            if (OVRInput.GetDown(OVRInput.Button.Two)) atras = true;

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
            if (stick.sqrMagnitude > 0.36f && Time.unscaledTime > siguienteNavegacionVR)
            {
                if (Mathf.Abs(stick.y) > Mathf.Abs(stick.x)) { if (stick.y > 0f) arriba = true; else abajo = true; }
                else { if (stick.x < 0f) izquierda = true; else derecha = true; }
                siguienteNavegacionVR = Time.unscaledTime + 0.3f;
            }
        }
        catch { }

        if (!(confirmar || atras || arriba || abajo || izquierda || derecha)) return;

        switch (pantallaActual)
        {
            case Pantalla.Titulo:
                if (atras && hasSelectedAtLeastOnce) Continuar();
                else if (confirmar) AccionBotonPrincipal();
                break;

            case Pantalla.Modos:
                if (atras) { ReproducirClic(); MostrarPantalla(Pantalla.Titulo); }
                else if (arriba) SeleccionarModo(((int)modoSeleccionado + 2) % 3);
                else if (abajo) SeleccionarModo(((int)modoSeleccionado + 1) % 3);
                else if (izquierda) CambiarPreset(-1);
                else if (derecha) CambiarPreset(1);
                else if (confirmar) ComenzarPartida();
                break;

            case Pantalla.Controles:
                if (atras || confirmar) { ReproducirClic(); MostrarPantalla(Pantalla.Titulo); }
                else if (izquierda) CambiarPreset(-1);
                else if (derecha) CambiarPreset(1);
                break;
        }
    }

    // =========================================================================
    // ACCIONES DE LOS BOTONES
    // =========================================================================

    /// <summary>Botón superior: "Jugar" en el menú principal o "Volver al juego" en la pausa.</summary>
    private void AccionBotonPrincipal()
    {
        if (hasSelectedAtLeastOnce) Continuar();
        else MostrarPantalla(Pantalla.Modos);
    }

    /// <summary>Empieza la partida con el modo y el sistema de control seleccionados.</summary>
    public void ComenzarPartida()
    {
        if (!isOpen) return;

        AplicarModoDeJuego(modoSeleccionado);

        // Devolver la cámara a su orientación de inicio tras el giro panorámico del menú
        if (yawGuardado && CameraMovement.Instance != null)
        {
            CameraMovement.Instance.Yaw = yawInicialPanorama;
        }
        panoramaActivo = false;
        yawGuardado = false;

        ConfirmAndStart();
    }

    /// <summary>Cierra el menú de pausa y vuelve al juego.</summary>
    public void Continuar()
    {
        if (!isOpen) return;
        GuardarPreferencias();
        CloseMenu(true);
    }

    /// <summary>Compatibilidad: inicia la partida (primera vez) o continúa (en pausa).</summary>
    public void Iniciar()
    {
        if (!isOpen) return;
        if (hasSelectedAtLeastOnce) Continuar();
        else ComenzarPartida();
    }

    /// <summary>Botón MENÚ PRINCIPAL (pausa): reinicia la escena y vuelve a la pantalla de título.</summary>
    public void VolverAlMenuPrincipal()
    {
        GuardarPreferencias();
        SaltarMenuEnProximaCarga = false;
        RecargarEscenaActual();
    }

    /// <summary>Botón SALIR DEL JUEGO: cierra el juego (o detiene el Play Mode en el Editor).</summary>
    public void Salir()
    {
        GuardarPreferencias();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public static void RecargarEscenaActual()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        Scene actual = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // En el Editor también funciona aunque la escena no esté en Build Settings
        if (actual.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(actual.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        if (actual.buildIndex >= 0) SceneManager.LoadScene(actual.buildIndex);
        else SceneManager.LoadScene(actual.name);
    }

    private void ClicEnModo(int indice)
    {
        // Dos clics en el MISMO frame (p. ej. láser VR + otro módulo de entrada) no cuentan como doble clic
        if (Time.frameCount == ultimoClicModoFrame) return;
        ultimoClicModoFrame = Time.frameCount;

        bool dobleClic = ultimoClicModoIndice == indice && Time.unscaledTime - ultimoClicModoTiempo < 0.4f;
        ultimoClicModoIndice = indice;
        ultimoClicModoTiempo = Time.unscaledTime;

        SeleccionarModo(indice);
        if (dobleClic) ComenzarPartida();
    }

    private void SeleccionarModo(int indice)
    {
        modoSeleccionado = (TipoModoJuego)Mathf.Clamp(indice, 0, 2);
        RefrescarModos();
    }

    private void CambiarPreset(int delta)
    {
        // Con visor solo se alterna entre los dos sistemas VR
        if (UIVR.Activo)
        {
            SelectPreset(currentPreset == MovementPreset.VR_Continuo ? MovementPreset.VR_Teletransporte : MovementPreset.VR_Continuo);
            RefrescarTextos();
            return;
        }

        int total = System.Enum.GetValues(typeof(MovementPreset)).Length;
        SelectPreset((MovementPreset)(((int)currentPreset + delta + total) % total));
        RefrescarTextos();
    }

    private void AplicarModoDeJuego(TipoModoJuego modo)
    {
        AsegurarAjustesModos();
        ModoDeJuego.Aplicar(modo, ajustesModos[(int)modo]);
        PlayerPrefs.SetInt(PREF_GAME_MODE, (int)modo);
    }

    // =========================================================================
    // ABRIR / CERRAR
    // =========================================================================

    public void OpenMenu()
    {
        isOpen = true;

        // Juego en pausa (como Minecraft en un jugador). La UI usa tiempo sin escalar.
        Time.timeScale = 0f;
        AudioListener.pause = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (CameraMovement.Instance != null)
        {
            CameraMovement.Instance.IsMovementPausedByMenu = true;
        }

        SyncDetailedOptionsFromPreset(currentPreset);
        SetActivo(raizMenu, true);
        proximoTemblorTitulo = Time.unscaledTime + 1.2f;
        MostrarPantalla(Pantalla.Titulo);
    }

    public void CloseMenu(bool applyChanges = true)
    {
        if (applyChanges)
        {
            ApplySettingsToCamera();
        }

        isOpen = false;
        frameCierre = Time.frameCount;
        panoramaActivo = false;

        SetActivo(raizMenu, false);
        MenuUIBuilder.LimpiarSeleccion();
        UIVR.Soltar(canvasMenu); // oculta el láser VR

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (CameraMovement.Instance != null)
        {
            CameraMovement.Instance.IsMovementPausedByMenu = false;
        }

        AplicarCursorDeJuego();
    }

    /// <summary>
    /// En juego el cursor se bloquea y oculta. Solo se deja libre en el modo táctil y en el universal
    /// (que usa el ratón para simular toques y requiere clic derecho para mirar).
    /// </summary>
    private void AplicarCursorDeJuego()
    {
        bool cursorLibre = currentPreset == MovementPreset.Tactil_Mobile || currentPreset == MovementPreset.Universal_Todos;
        Cursor.lockState = cursorLibre ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = cursorLibre;
    }

    public void SelectPreset(MovementPreset preset)
    {
        currentPreset = preset;
        SyncDetailedOptionsFromPreset(preset);
    }

    private void SyncDetailedOptionsFromPreset(MovementPreset preset)
    {
        switch (preset)
        {
            case MovementPreset.VR_Teletransporte:
                currentLocomotion = CameraMovement.LocomotionMode.Teletransporte;
                currentVRSnapTurn = true; // Snap turn recomendado para confort total
                currentRightClickLook = false;
                break;

            case MovementPreset.Universal_Todos:
                currentLocomotion = CameraMovement.LocomotionMode.Hibrido;
                currentVRSnapTurn = false;
                currentRightClickLook = true;
                break;

            default: // Teclado y ratón, VR continuo, táctil
                currentLocomotion = CameraMovement.LocomotionMode.Continuo;
                currentVRSnapTurn = false;
                currentRightClickLook = false;
                break;
        }
    }

    private void GuardarPreferencias()
    {
        PlayerPrefs.SetInt(PREF_SAVED_PRESET, (int)currentPreset);
        PlayerPrefs.SetInt(PREF_VR_SNAP_TURN, currentVRSnapTurn ? 1 : 0);
        PlayerPrefs.SetInt(PREF_RIGHT_CLICK_LOOK, currentRightClickLook ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ConfirmAndStart()
    {
        hasSelectedAtLeastOnce = true;
        GuardarPreferencias();
        CloseMenu(true);
        ShowToast($"Modo {ModoDeJuego.Nombre}  ·  {GetPresetDisplayName(currentPreset)}");
    }

    public void ApplyPreset(MovementPreset preset, bool save = true)
    {
        hasSelectedAtLeastOnce = true;
        currentPreset = preset;
        SyncDetailedOptionsFromPreset(preset);
        ApplySettingsToCamera();

        if (save)
        {
            PlayerPrefs.SetInt(PREF_SAVED_PRESET, (int)preset);
            PlayerPrefs.Save();
        }
    }

    private void ApplySettingsToCamera()
    {
        CameraMovement camMov = CameraMovement.Instance != null ? CameraMovement.Instance : FindAnyObjectByType<CameraMovement>();
        if (camMov == null) return;

        switch (currentPreset)
        {
            case MovementPreset.TecladoYMouse:
                camMov.ModoControl = CameraMovement.ControlSchemeMode.TecladoYMouse;
                camMov.UsarTecladoYMouse = true;
                camMov.UsarControlTactil = false;
                camMov.UsarControlVR = false;
                break;

            case MovementPreset.VR_Continuo:
            case MovementPreset.VR_Teletransporte:
                camMov.ModoControl = CameraMovement.ControlSchemeMode.VR;
                camMov.UsarTecladoYMouse = false;
                camMov.UsarControlTactil = false;
                camMov.UsarControlVR = true;
                break;

            case MovementPreset.Tactil_Mobile:
                camMov.ModoControl = CameraMovement.ControlSchemeMode.Tactil;
                camMov.UsarTecladoYMouse = false;
                camMov.UsarControlTactil = true;
                camMov.UsarControlVR = false;
                break;

            case MovementPreset.Universal_Todos:
                camMov.ModoControl = CameraMovement.ControlSchemeMode.Automatico_Todos;
                camMov.UsarTecladoYMouse = true;
                camMov.UsarControlTactil = true;
                camMov.UsarControlVR = true;
                break;
        }

        camMov.TipoLocomocion = currentLocomotion;
        camMov.VRSnapTurn = currentVRSnapTurn;
        camMov.RequireRightClickToLook = currentRightClickLook;
    }

    public void ShowToast(string message, float duration = TOAST_DURATION)
    {
        toastMessage = message;
        toastTimer = duration;
        HUDVR.Mensaje(CanalHUD.Aviso, message, new Color(0.85f, 0.95f, 1f, 1f), duration);
    }

    public static string GetPresetDisplayName(MovementPreset preset)
    {
        switch (preset)
        {
            case MovementPreset.TecladoYMouse: return "Teclado y Ratón (PC)";
            case MovementPreset.VR_Continuo: return "VR: Movimiento Continuo";
            case MovementPreset.VR_Teletransporte: return "VR: Teletransporte";
            case MovementPreset.Tactil_Mobile: return "Control Táctil (Móvil)";
            case MovementPreset.Universal_Todos: return "Modo Universal (Todos)";
            default: return preset.ToString();
        }
    }

    private void AsegurarAjustesModos()
    {
        if (ajustesModos == null || ajustesModos.Length != 3)
        {
            ajustesModos = AjustesModoJuego.CrearPorDefecto();
            return;
        }

        AjustesModoJuego[] porDefecto = null;
        for (int i = 0; i < 3; i++)
        {
            if (ajustesModos[i] != null) continue;
            if (porDefecto == null) porDefecto = AjustesModoJuego.CrearPorDefecto();
            ajustesModos[i] = porDefecto[i];
        }
    }

    private void ReproducirClic()
    {
        if (audioMenu == null) return;
        AudioClip clip = sonidoClic != null ? sonidoClic : clipClicGenerado;
        if (clip != null) audioMenu.PlayOneShot(clip, 0.8f);
    }

    // =========================================================================
    // PANTALLAS
    // =========================================================================

    private void MostrarPantalla(Pantalla pantalla)
    {
        pantallaActual = pantalla;
        SetActivo(panelTitulo, pantalla == Pantalla.Titulo);
        SetActivo(panelModos, pantalla == Pantalla.Modos);
        SetActivo(panelControles, pantalla == Pantalla.Controles);

        if (pantalla == Pantalla.Titulo) ConfigurarPantallaTitulo();
        if (pantalla == Pantalla.Modos) RefrescarModos();

        if (textoAyudaModos != null)
        {
            textoAyudaModos.text = UIVR.Activo
                ? "Apunta con el mando y pulsa el gatillo   ·   Stick izquierdo: elegir modo   ·   B: volver"
                : "Doble clic en un modo para empezar   ·   Flechas: elegir   ·   Enter: comenzar   ·   Esc: volver";
        }

        RefrescarTextos();
        MenuUIBuilder.LimpiarSeleccion();
    }

    /// <summary>La misma pantalla sirve de menú principal (al arrancar) y de menú de pausa (durante la partida).</summary>
    private void ConfigurarPantallaTitulo()
    {
        bool pausa = hasSelectedAtLeastOnce;

        MenuUIBuilder.SetTexto(botonJugar != null ? botonJugar.gameObject : null, pausa ? "Volver al juego" : "Jugar");
        if (botonMenuPrincipal != null) SetActivo(botonMenuPrincipal.gameObject, pausa);

        ColocarBoton(botonJugar, 30f);
        ColocarBoton(botonControles, -70f);
        ColocarBoton(botonMenuPrincipal, -170f);
        ColocarBoton(botonSalir, pausa ? -270f : -170f);

        if (rtFrase != null)
        {
            SetActivo(rtFrase.gameObject, !pausa);
            if (!pausa && textoFrase != null && frasesAmarillas != null && frasesAmarillas.Length > 0)
            {
                textoFrase.text = frasesAmarillas[Random.Range(0, frasesAmarillas.Length)];
            }
        }

        if (textoInfoPausa != null)
        {
            SetActivo(textoInfoPausa.gameObject, pausa);
            textoInfoPausa.text = $"Juego en pausa   ·   Modo {ModoDeJuego.Nombre}";
        }

        if (fondoTitulo != null)
        {
            // En VR el menú es un panel flotante: más opaco para que el texto se lea bien sobre la escena
            float opacidad = UIVR.Activo ? (pausa ? 0.85f : 0.7f) : (pausa ? 0.6f : 0.28f);
            fondoTitulo.color = new Color(0f, 0f, 0f, opacidad);
        }

        // La escena gira de fondo solo en el menú principal, nunca en la pausa
        panoramaActivo = !pausa;
    }

    private static void ColocarBoton(Button boton, float y)
    {
        if (boton == null) return;
        ((RectTransform)boton.transform).anchoredPosition = new Vector2(0f, y);
    }

    private void RefrescarModos()
    {
        for (int i = 0; i < 3; i++)
        {
            bool seleccionado = (int)modoSeleccionado == i;
            if (marcosModo[i] != null) SetActivo(marcosModo[i], seleccionado);
            if (nombresModo[i] != null) nombresModo[i].color = seleccionado ? new Color(1f, 1f, 0.6f, 1f) : Color.white;
        }
    }

    private void RefrescarTextos()
    {
        string nombrePreset = GetPresetDisplayName(currentPreset);
        if (textoPreset != null) textoPreset.text = nombrePreset;
        if (textoListaControles != null) textoListaControles.text = ConstruirTextoControles(currentPreset);
        if (botonCicloControles != null) MenuUIBuilder.SetTexto(botonCicloControles.gameObject, "Controles: " + nombrePreset);
    }

    private static string ConstruirTextoControles(MovementPreset preset)
    {
        string movimiento;
        switch (preset)
        {
            case MovementPreset.TecladoYMouse:
                movimiento =
                    "MOVIMIENTO\n" +
                    "  W A S D: caminar        Ratón: mirar        Shift: correr\n" +
                    "  Espacio: saltar         Ctrl: agacharse     T (mantener): teletransporte";
                break;
            case MovementPreset.VR_Continuo:
                movimiento =
                    "MOVIMIENTO (VR)\n" +
                    "  Stick izquierdo: caminar      Stick derecho: girar\n" +
                    "  A: saltar      B / X: agacharse      Clic stick izquierdo: correr";
                break;
            case MovementPreset.VR_Teletransporte:
                movimiento =
                    "MOVIMIENTO (VR)\n" +
                    "  Gatillo o stick hacia delante: apuntar el arco y soltar para teletransportarte\n" +
                    "  Stick derecho: giro por pasos de 45°      A: saltar";
                break;
            case MovementPreset.Tactil_Mobile:
                movimiento =
                    "MOVIMIENTO (TÁCTIL)\n" +
                    "  Mitad izquierda: joystick virtual      Mitad derecha: arrastrar para mirar\n" +
                    "  Botones SALTAR y AGACHO en pantalla";
                break;
            default:
                movimiento =
                    "MOVIMIENTO (UNIVERSAL)\n" +
                    "  Teclado, ratón (clic derecho para mirar), táctil y VR activos a la vez";
                break;
        }

        if (EsPresetVR(preset))
        {
            return movimiento +
                   "\n\nOBJETOS (VR)\n" +
                   "  Grip (botón lateral): recoger el objeto al que apuntas, o usar el que tienes en la mano\n" +
                   "  Y (mando izquierdo): cambiar de objeto / guardarlo (manos libres)\n" +
                   "  Gatillo: rociar con el extintor en la mano / golpear la máquina expendedora\n\n" +
                   "MENÚS\n" +
                   "  Botón Menú (mando izquierdo): pausa\n" +
                   "  Apunta con el láser y pulsa el gatillo para elegir      B: volver";
        }

        return movimiento +
               "\n\nOBJETOS\n" +
               "  E: recoger / usar el objeto en mano      1 / 2: equipar casilla\n" +
               "  Q: guardar en el inventario      G: soltar      H: usar botiquín\n" +
               "  Extintor: mantener Clic izquierdo (o E) apuntando a las llamas\n" +
               "  Máquina expendedora: Clic izquierdo o F para golpearla\n\n" +
               "OTROS\n" +
               "  M: pausa / menú      RePág / AvPág: altura de la cámara";
    }

    // =========================================================================
    // CONSTRUCCIÓN DE LA INTERFAZ (uGUI generada por código)
    // =========================================================================

    private void ConstruirMenu()
    {
        Canvas canvas = MenuUIBuilder.CrearCanvas("Menu_EarthquakeSimulator (auto)", 100);
        canvasMenu = canvas;
        raizMenu = canvas.gameObject;

        audioMenu = raizMenu.AddComponent<AudioSource>();
        audioMenu.playOnAwake = false;
        audioMenu.spatialBlend = 0f;
        audioMenu.ignoreListenerPause = true; // suena aunque el juego esté en pausa
        clipClicGenerado = MenuUIBuilder.CrearSonidoClic();

        Texture2D texturaTierra = MenuUIBuilder.CrearTexturaTierra();

        ConstruirPantallaTitulo(canvas.transform);
        ConstruirPantallaModos(canvas.transform, texturaTierra);
        ConstruirPantallaControles(canvas.transform, texturaTierra);

        raizMenu.SetActive(false);
    }

    private void ConstruirPantallaTitulo(Transform padre)
    {
        panelTitulo = MenuUIBuilder.CrearPanel(padre, "PantallaTitulo", Color.clear);

        // Velo oscuro sobre la escena (la escena gira detrás: "panorama")
        fondoTitulo = MenuUIBuilder.CrearPanel(panelTitulo.transform, "Velo", new Color(0f, 0f, 0f, 0.28f)).GetComponent<Image>();

        // Logo
        Text titulo = MenuUIBuilder.CrearTexto(panelTitulo.transform, "Titulo", tituloMenu, 124, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 290f), new Vector2(1760f, 190f), new Color(0.93f, 0.93f, 0.93f, 1f));
        MenuUIBuilder.EstiloLogo(titulo);
        rtTitulo = titulo.rectTransform;
        posTituloBase = rtTitulo.anchoredPosition;

        // Frase amarilla inclinada junto al título
        textoFrase = MenuUIBuilder.CrearTexto(panelTitulo.transform, "FraseAmarilla", "", 40, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(560f, 185f), new Vector2(680f, 70f), new Color(1f, 1f, 0.25f, 1f));
        rtFrase = textoFrase.rectTransform;
        rtFrase.localEulerAngles = new Vector3(0f, 0f, 16f);

        // Texto de pausa (sustituye a la frase durante la partida)
        textoInfoPausa = MenuUIBuilder.CrearTexto(panelTitulo.transform, "InfoPausa", "", 34, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 165f), new Vector2(1300f, 60f), new Color(1f, 1f, 1f, 0.95f));

        // Botones
        botonJugar = MenuUIBuilder.CrearBoton(panelTitulo.transform, "BotonJugar", "Jugar", new Vector2(0f, 30f), new Vector2(680f, 82f));
        botonControles = MenuUIBuilder.CrearBoton(panelTitulo.transform, "BotonControles", "Controles", new Vector2(0f, -70f), new Vector2(680f, 82f));
        botonMenuPrincipal = MenuUIBuilder.CrearBoton(panelTitulo.transform, "BotonMenuPrincipal", "Menú principal", new Vector2(0f, -170f), new Vector2(680f, 82f));
        botonSalir = MenuUIBuilder.CrearBoton(panelTitulo.transform, "BotonSalir", "Salir del juego", new Vector2(0f, -170f), new Vector2(680f, 82f));

        botonJugar.onClick.AddListener(AccionBotonPrincipal);
        botonControles.onClick.AddListener(() => MostrarPantalla(Pantalla.Controles));
        botonMenuPrincipal.onClick.AddListener(VolverAlMenuPrincipal);
        botonSalir.onClick.AddListener(Salir);

        // Textos de las esquinas inferiores
        Text version = MenuUIBuilder.CrearTexto(panelTitulo.transform, "Version", textoVersion, 26, FontStyle.Normal, TextAnchor.LowerLeft,
            Vector2.zero, new Vector2(900f, 44f), Color.white);
        MenuUIBuilder.Anclar(version.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 14f));

        Text pie = MenuUIBuilder.CrearTexto(panelTitulo.transform, "Pie", textoPie, 26, FontStyle.Normal, TextAnchor.LowerRight,
            Vector2.zero, new Vector2(900f, 44f), Color.white);
        MenuUIBuilder.Anclar(pie.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 14f));
    }

    private void ConstruirPantallaModos(Transform padre, Texture2D texturaTierra)
    {
        panelModos = MenuUIBuilder.CrearPanel(padre, "PantallaModos", Color.clear);
        MenuUIBuilder.CrearFondoTierra(panelModos.transform, texturaTierra);

        MenuUIBuilder.CrearTexto(panelModos.transform, "Encabezado", "Seleccionar modo de juego", 48, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 440f), new Vector2(1400f, 74f), Color.white);

        // Lista de modos (como la lista de mundos de Minecraft)
        GameObject lista = MenuUIBuilder.CrearCaja(padre: panelModos.transform, nombre: "Lista", posicion: new Vector2(0f, 95f),
            tamano: new Vector2(1200f, 480f), color: new Color(0f, 0f, 0f, 0.55f));

        float[] alturas = { 155f, 0f, -155f };
        Vector2 tamanoEntrada = new Vector2(1140f, 140f);

        for (int i = 0; i < 3; i++)
        {
            AjustesModoJuego a = ajustesModos[i];
            Button entrada = MenuUIBuilder.CrearEntradaLista(lista.transform, "Modo_" + a.nombre, new Vector2(0f, alturas[i]), tamanoEntrada);

            // Icono con la "magnitud" del modo
            GameObject icono = MenuUIBuilder.CrearCaja(entrada.transform, "Icono", new Vector2(-490f, 0f), new Vector2(112f, 112f), a.colorIcono);
            icono.GetComponent<Image>().raycastTarget = false;
            Outline bordeIcono = icono.AddComponent<Outline>();
            bordeIcono.effectColor = new Color(0f, 0f, 0f, 0.9f);
            bordeIcono.effectDistance = new Vector2(3f, -3f);
            MenuUIBuilder.CrearTexto(icono.transform, "Magnitud", a.etiquetaIcono, 44, FontStyle.Bold, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(112f, 112f), Color.white);

            nombresModo[i] = MenuUIBuilder.CrearTexto(entrada.transform, "Nombre", a.nombre, 40, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(90f, 36f), new Vector2(900f, 54f), Color.white);
            MenuUIBuilder.CrearTexto(entrada.transform, "Descripcion", a.descripcion, 27, FontStyle.Normal, TextAnchor.UpperLeft,
                new Vector2(90f, -24f), new Vector2(900f, 80f), new Color(0.72f, 0.72f, 0.72f, 1f));

            // Marco blanco de selección
            marcosModo[i] = MenuUIBuilder.CrearMarco(entrada.transform, "MarcoSeleccion", tamanoEntrada, 4f, Color.white);

            int indice = i;
            entrada.onClick.AddListener(() => ClicEnModo(indice));
            botonesModo[i] = entrada;
        }

        // Botón que cambia el sistema de control con cada clic (como los botones de opciones de Minecraft)
        botonCicloControles = MenuUIBuilder.CrearBoton(panelModos.transform, "BotonCicloControles", "Controles", new Vector2(0f, -240f), new Vector2(860f, 82f));
        botonCicloControles.onClick.AddListener(() => CambiarPreset(1));

        Button comenzar = MenuUIBuilder.CrearBoton(panelModos.transform, "BotonComenzar", "Comenzar partida", new Vector2(-222f, -345f), new Vector2(420f, 82f));
        Button cancelar = MenuUIBuilder.CrearBoton(panelModos.transform, "BotonCancelar", "Cancelar", new Vector2(222f, -345f), new Vector2(420f, 82f));
        comenzar.onClick.AddListener(ComenzarPartida);
        cancelar.onClick.AddListener(() => MostrarPantalla(Pantalla.Titulo));

        textoAyudaModos = MenuUIBuilder.CrearTexto(panelModos.transform, "Ayuda", "Doble clic en un modo para empezar   ·   Flechas: elegir   ·   Enter: comenzar   ·   Esc: volver",
            24, FontStyle.Italic, TextAnchor.MiddleCenter, new Vector2(0f, -440f), new Vector2(1600f, 44f), new Color(0.75f, 0.75f, 0.75f, 1f));
    }

    private void ConstruirPantallaControles(Transform padre, Texture2D texturaTierra)
    {
        panelControles = MenuUIBuilder.CrearPanel(padre, "PantallaControles", Color.clear);
        MenuUIBuilder.CrearFondoTierra(panelControles.transform, texturaTierra);

        MenuUIBuilder.CrearTexto(panelControles.transform, "Encabezado", "Controles", 48, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 440f), new Vector2(1000f, 74f), Color.white);

        Button anterior = MenuUIBuilder.CrearBoton(panelControles.transform, "BotonPresetAnterior", "<", new Vector2(-430f, 345f), new Vector2(84f, 72f));
        Button siguiente = MenuUIBuilder.CrearBoton(panelControles.transform, "BotonPresetSiguiente", ">", new Vector2(430f, 345f), new Vector2(84f, 72f));
        anterior.onClick.AddListener(() => CambiarPreset(-1));
        siguiente.onClick.AddListener(() => CambiarPreset(1));

        textoPreset = MenuUIBuilder.CrearTexto(panelControles.transform, "TextoPreset", "", 34, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 345f), new Vector2(720f, 72f), new Color(1f, 1f, 0.4f, 1f));

        GameObject caja = MenuUIBuilder.CrearCaja(panelControles.transform, "CajaControles", new Vector2(0f, -15f), new Vector2(1320f, 590f), new Color(0f, 0f, 0f, 0.55f));
        textoListaControles = MenuUIBuilder.CrearTexto(caja.transform, "TextoControles", "", 30, FontStyle.Normal, TextAnchor.UpperLeft,
            Vector2.zero, new Vector2(1260f, 550f), Color.white);

        Button hecho = MenuUIBuilder.CrearBoton(panelControles.transform, "BotonHecho", "Hecho", new Vector2(0f, -400f), new Vector2(680f, 82f));
        hecho.onClick.AddListener(() => MostrarPantalla(Pantalla.Titulo));
    }

    private static void SetActivo(GameObject go, bool activo)
    {
        if (go != null && go.activeSelf != activo) go.SetActive(activo);
    }

    // =========================================================================
    // OnGUI: notificación breve, etiqueta de modo y botón flotante (el menú es uGUI)
    // =========================================================================

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR OnGUI no se ve: los avisos van al HUD VR (y así no gasta CPU en Quest)
        if (isOpen) return;
        if (PlayerHealth.Instance != null && !PlayerHealth.Instance.IsAlive) return;

        EnsureStyles();

        if (toastTimer > 0f && !string.IsNullOrEmpty(toastMessage))
        {
            float alpha = Mathf.Clamp01(toastTimer);
            Rect toastRect = new Rect((Screen.width - 520f) * 0.5f, 24f, 520f, 40f);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.1f, 0.16f, 0.9f * alpha);
            GUI.DrawTexture(toastRect, panelBgTex);
            toastStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.color = prev;
            GUI.Label(toastRect, toastMessage, toastStyle);
        }

        // Recordatorio permanente en el modo sin daño
        if (hasSelectedAtLeastOnce && ModoDeJuego.SinDanio)
        {
            GUI.Label(new Rect((Screen.width - 300f) * 0.5f, 70f, 300f, 22f), "SIMULACRO · SIN DAÑO", etiquetaModoStyle);
        }

        // Con el cursor bloqueado no se puede pulsar, así que solo se muestra con el cursor libre
        if (mostrarBotonFlotanteHUD && hasSelectedAtLeastOnce && Cursor.lockState != CursorLockMode.Locked)
        {
            Rect btnRect = new Rect(Screen.width - 161f, 16f, 145f, 32f);
            if (GUI.Button(btnRect, "Menú [M]", hudButtonStyle))
            {
                OpenMenu();
            }
        }
    }

    private void EnsureStyles()
    {
        if (panelBgTex == null)
        {
            panelBgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelBgTex.SetPixel(0, 0, Color.white);
            panelBgTex.Apply();
        }

        if (toastStyle == null)
        {
            toastStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (etiquetaModoStyle == null)
        {
            etiquetaModoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            etiquetaModoStyle.normal.textColor = new Color(0.45f, 1f, 0.5f, 0.85f);
        }

        if (hudButtonStyle == null)
        {
            hudButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            hudButtonStyle.normal.textColor = new Color(0.7f, 0.95f, 1f, 0.95f);
        }
    }
}

// =============================================================================
// MODOS DE JUEGO
// =============================================================================

/// <summary>Modos disponibles. El orden coincide con el array 'Ajustes Modos' del Inspector.</summary>
public enum TipoModoJuego
{
    Simulacro = 0,
    Supervivencia = 1,
    Extremo = 2
}

/// <summary>Ajustes editables de cada modo de juego (Inspector de MovementSystemSelector).</summary>
[System.Serializable]
public class AjustesModoJuego
{
    public string nombre = "Supervivencia";

    [Tooltip("Texto corto del icono (magnitud del sismo)")]
    public string etiquetaIcono = "M6";

    [TextArea(2, 3)]
    public string descripcion = "";

    public Color colorIcono = new Color(0.93f, 0.6f, 0.18f, 1f);

    [Tooltip("Multiplica todo el daño recibido. 0 = invulnerable")]
    [Range(0f, 3f)] public float multiplicadorDanio = 1f;

    [Tooltip("Multiplica la fuerza de la sacudida de cámara")]
    [Range(0.2f, 3f)] public float multiplicadorIntensidadSismo = 1f;

    [Tooltip("Multiplica la espera entre sismos (menor = sismos más frecuentes)")]
    [Range(0.2f, 3f)] public float multiplicadorIntervaloSismo = 1f;

    [Tooltip("Multiplica la duración de cada sismo")]
    [Range(0.2f, 3f)] public float multiplicadorDuracionSismo = 1f;

    [Tooltip("Multiplica la probabilidad de que la máquina expendedora explote")]
    [Range(0f, 4f)] public float multiplicadorProbabilidadExplosion = 1f;

    public static AjustesModoJuego[] CrearPorDefecto()
    {
        return new[]
        {
            new AjustesModoJuego
            {
                nombre = "Simulacro",
                etiquetaIcono = "M3",
                descripcion = "Modo práctica: no recibes daño y los sismos son más suaves. Ideal para aprender qué hacer.",
                colorIcono = new Color(0.30f, 0.72f, 0.36f, 1f),
                multiplicadorDanio = 0f,
                multiplicadorIntensidadSismo = 0.6f,
                multiplicadorIntervaloSismo = 1.3f,
                multiplicadorDuracionSismo = 0.8f,
                multiplicadorProbabilidadExplosion = 0.5f
            },
            new AjustesModoJuego
            {
                nombre = "Supervivencia",
                etiquetaIcono = "M6",
                descripcion = "La experiencia normal: el sismo, el fuego y los golpes te hacen daño. ¡Sobrevive!",
                colorIcono = new Color(0.93f, 0.60f, 0.18f, 1f),
                multiplicadorDanio = 1f,
                multiplicadorIntensidadSismo = 1f,
                multiplicadorIntervaloSismo = 1f,
                multiplicadorDuracionSismo = 1f,
                multiplicadorProbabilidadExplosion = 1f
            },
            new AjustesModoJuego
            {
                nombre = "Extremo",
                etiquetaIcono = "M9",
                descripcion = "Sismos más fuertes y frecuentes, más daño y la máquina explota con mucha facilidad.",
                colorIcono = new Color(0.85f, 0.20f, 0.18f, 1f),
                multiplicadorDanio = 1.6f,
                multiplicadorIntensidadSismo = 1.4f,
                multiplicadorIntervaloSismo = 0.6f,
                multiplicadorDuracionSismo = 1.3f,
                multiplicadorProbabilidadExplosion = 2f
            }
        };
    }
}

/// <summary>
/// Modo de juego activo, accesible desde cualquier script (PlayerHealth, CameraShake, VendingMachineHazard).
/// Es estático: se conserva al reiniciar la escena con "Volver a intentarlo".
/// </summary>
public static class ModoDeJuego
{
    public static TipoModoJuego Actual { get; private set; } = TipoModoJuego.Supervivencia;
    public static string Nombre { get; private set; } = "Supervivencia";
    public static float MultiplicadorDanio { get; private set; } = 1f;
    public static float MultiplicadorIntensidadSismo { get; private set; } = 1f;
    public static float MultiplicadorIntervaloSismo { get; private set; } = 1f;
    public static float MultiplicadorDuracionSismo { get; private set; } = 1f;
    public static float MultiplicadorProbabilidadExplosion { get; private set; } = 1f;

    public static bool SinDanio => MultiplicadorDanio <= 0.0001f;

    public static void Aplicar(TipoModoJuego modo, AjustesModoJuego ajustes)
    {
        if (ajustes == null) ajustes = new AjustesModoJuego();

        Actual = modo;
        Nombre = string.IsNullOrEmpty(ajustes.nombre) ? modo.ToString() : ajustes.nombre;
        MultiplicadorDanio = Mathf.Max(0f, ajustes.multiplicadorDanio);
        MultiplicadorIntensidadSismo = Mathf.Max(0f, ajustes.multiplicadorIntensidadSismo);
        MultiplicadorIntervaloSismo = Mathf.Max(0.1f, ajustes.multiplicadorIntervaloSismo);
        MultiplicadorDuracionSismo = Mathf.Max(0.1f, ajustes.multiplicadorDuracionSismo);
        MultiplicadorProbabilidadExplosion = Mathf.Max(0f, ajustes.multiplicadorProbabilidadExplosion);
    }
}

// =============================================================================
// CONSTRUCTOR DE UI
// =============================================================================

/// <summary>
/// Utilidades para construir UI (uGUI) estilo Minecraft en tiempo de ejecución.
/// Las usan el menú principal (MovementSystemSelector) y el Game Over (PlayerHealth).
/// </summary>
public static class MenuUIBuilder
{
    private static Font fuenteCache;

    /// <summary>Fuente opcional asignada desde el Inspector del menú (por ejemplo, una fuente pixelada).</summary>
    public static Font FuentePersonalizada;

    /// <summary>Se invoca al pulsar cualquier botón creado aquí (el menú lo usa para el sonido de clic).</summary>
    public static System.Action AlPulsarBoton;

    private static readonly Color ColorTextoBoton = new Color(0.88f, 0.88f, 0.88f, 1f);
    private static readonly Color ColorTextoBotonHover = new Color(1f, 1f, 0.63f, 1f);

    public static Font Fuente()
    {
        if (FuentePersonalizada != null) return FuentePersonalizada;
        if (fuenteCache != null) return fuenteCache;
        try { fuenteCache = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (fuenteCache == null)
        {
            try { fuenteCache = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        return fuenteCache;
    }

    public static Canvas CrearCanvas(string nombre, int ordenDibujo)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = ordenDibujo;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>Panel que ocupa todo su padre. Con color transparente no añade Image (no bloquea clics).</summary>
    public static GameObject CrearPanel(Transform padre, string nombre, Color color)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        Estirar((RectTransform)go.transform);

        if (color.a > 0.001f)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
        }
        return go;
    }

    public static GameObject CrearCaja(Transform padre, string nombre, Vector2 posicion, Vector2 tamano, Color color)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    public static Text CrearTexto(Transform padre, string nombre, string texto, int tamanoFuente, FontStyle estilo,
        TextAnchor alineacion, Vector2 posicion, Vector2 tamanoCaja, Color color)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamanoCaja;

        Text t = go.AddComponent<Text>();
        t.font = Fuente();
        t.text = texto;
        t.fontSize = tamanoFuente;
        t.fontStyle = estilo;
        t.alignment = alineacion;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = tamanoFuente;
        t.raycastTarget = false;

        // Sombra dura desplazada, como el texto de Minecraft
        Shadow sombra = go.AddComponent<Shadow>();
        sombra.effectColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        sombra.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    /// <summary>Aspecto de logo: contorno negro grueso y sombra profunda.</summary>
    public static void EstiloLogo(Text titulo)
    {
        Outline contorno = titulo.gameObject.AddComponent<Outline>();
        contorno.effectColor = new Color(0f, 0f, 0f, 1f);
        contorno.effectDistance = new Vector2(5f, -5f);

        Shadow profundidad = titulo.gameObject.AddComponent<Shadow>();
        profundidad.effectColor = new Color(0.25f, 0.18f, 0.12f, 0.9f);
        profundidad.effectDistance = new Vector2(9f, -11f);
    }

    /// <summary>Botón gris con borde negro, texto claro que se vuelve amarillo al pasar el ratón (estilo Minecraft).</summary>
    public static Button CrearBoton(Transform padre, string nombre, string texto, Vector2 posicion, Vector2 tamano)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;

        Image img = go.AddComponent<Image>();
        img.color = Color.white;

        Outline borde = go.AddComponent<Outline>();
        borde.effectColor = new Color(0f, 0f, 0f, 1f);
        borde.effectDistance = new Vector2(3f, -3f);

        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock cb = b.colors;
        cb.normalColor = new Color(0.44f, 0.44f, 0.44f, 1f);
        cb.highlightedColor = new Color(0.49f, 0.53f, 0.78f, 1f); // azul-violeta al pasar el ratón
        cb.pressedColor = new Color(0.30f, 0.32f, 0.48f, 1f);
        cb.selectedColor = cb.normalColor; // que no quede "resaltado" tras pulsarlo
        cb.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;

        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;

        Text etiqueta = CrearTexto(go.transform, "Texto", texto, 34, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, tamano - new Vector2(16f, 8f), ColorTextoBoton);

        // Texto amarillo al pasar el ratón
        EventTrigger disparador = go.AddComponent<EventTrigger>();
        AgregarEvento(disparador, EventTriggerType.PointerEnter, () => etiqueta.color = ColorTextoBotonHover);
        AgregarEvento(disparador, EventTriggerType.PointerExit, () => etiqueta.color = ColorTextoBoton);

        b.onClick.AddListener(() =>
        {
            etiqueta.color = ColorTextoBoton;
            AlPulsarBoton?.Invoke();
            LimpiarSeleccion();
        });
        return b;
    }

    /// <summary>Fila seleccionable de una lista (como un mundo en la lista de Minecraft). Transparente hasta pasar el ratón.</summary>
    public static Button CrearEntradaLista(Transform padre, string nombre, Vector2 posicion, Vector2 tamano)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamano;

        Image img = go.AddComponent<Image>();
        img.color = Color.white;

        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock cb = b.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
        cb.pressedColor = new Color(1f, 1f, 1f, 0.18f);
        cb.selectedColor = cb.normalColor;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;

        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;

        b.onClick.AddListener(() =>
        {
            AlPulsarBoton?.Invoke();
            LimpiarSeleccion();
        });
        return b;
    }

    /// <summary>Marco hueco de 4 barras (borde de selección). Empieza oculto.</summary>
    public static GameObject CrearMarco(Transform padre, string nombre, Vector2 tamano, float grosor, Color color)
    {
        GameObject marco = new GameObject(nombre, typeof(RectTransform));
        marco.transform.SetParent(padre, false);
        ((RectTransform)marco.transform).sizeDelta = tamano;

        float w = tamano.x;
        float h = tamano.y;
        CrearBarra(marco.transform, new Vector2(0f, h * 0.5f - grosor * 0.5f), new Vector2(w, grosor), color);
        CrearBarra(marco.transform, new Vector2(0f, -h * 0.5f + grosor * 0.5f), new Vector2(w, grosor), color);
        CrearBarra(marco.transform, new Vector2(-w * 0.5f + grosor * 0.5f, 0f), new Vector2(grosor, h), color);
        CrearBarra(marco.transform, new Vector2(w * 0.5f - grosor * 0.5f, 0f), new Vector2(grosor, h), color);

        marco.SetActive(false);
        return marco;
    }

    private static void CrearBarra(Transform padre, Vector2 posicion, Vector2 tamano, Color color)
    {
        GameObject barra = CrearCaja(padre, "Barra", posicion, tamano, color);
        barra.GetComponent<Image>().raycastTarget = false;
    }

    /// <summary>Fondo de bloques de tierra en mosaico (como las pantallas de opciones de Minecraft).</summary>
    public static RawImage CrearFondoTierra(Transform padre, Texture2D textura)
    {
        GameObject go = new GameObject("FondoTierra", typeof(RectTransform));
        go.transform.SetParent(padre, false);
        Estirar((RectTransform)go.transform);

        RawImage img = go.AddComponent<RawImage>();
        img.texture = textura;
        img.color = new Color(0.32f, 0.32f, 0.32f, 1f);   // oscurecido para que el texto destaque
        img.uvRect = new Rect(0f, 0f, 1920f / 80f, 1080f / 80f); // bloques de ~80 px
        img.raycastTarget = true; // bloquea los clics hacia lo que haya detrás
        return img;
    }

    public static RawImage CrearImagen(Transform padre, string nombre, Texture textura, Vector2 posicion, Vector2 tamanoMaximo)
    {
        GameObject go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = posicion;

        Vector2 tamano = tamanoMaximo;
        if (textura != null && textura.height > 0)
        {
            float aspecto = (float)textura.width / textura.height;
            tamano = new Vector2(tamanoMaximo.y * aspecto, tamanoMaximo.y);
            if (tamano.x > tamanoMaximo.x) tamano = new Vector2(tamanoMaximo.x, tamanoMaximo.x / aspecto);
        }
        rt.sizeDelta = tamano;

        RawImage img = go.AddComponent<RawImage>();
        img.texture = textura;
        img.raycastTarget = false;
        return img;
    }

    public static void Anclar(RectTransform rt, Vector2 ancla, Vector2 pivote, Vector2 posicion)
    {
        rt.anchorMin = ancla;
        rt.anchorMax = ancla;
        rt.pivot = pivote;
        rt.anchoredPosition = posicion;
    }

    private static void Estirar(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void AgregarEvento(EventTrigger disparador, EventTriggerType tipo, System.Action accion)
    {
        EventTrigger.Entry entrada = new EventTrigger.Entry { eventID = tipo };
        entrada.callback.AddListener(_ => accion());
        disparador.triggers.Add(entrada);
    }

    /// <summary>Quita la selección de la UI para que Enter/Espacio no vuelvan a "pulsar" el último botón.</summary>
    public static void LimpiarSeleccion()
    {
        EventSystem es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
    }

    /// <summary>Textura 16x16 de tierra con píxeles nítidos (sin archivos externos).</summary>
    public static Texture2D CrearTexturaTierra()
    {
        const int n = 16;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            name = "Menu_Tierra",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };

        System.Random rnd = new System.Random(1906); // siempre el mismo patrón
        Color baseTierra = new Color(0.47f, 0.33f, 0.22f, 1f);
        Color[] pixeles = new Color[n * n];
        for (int i = 0; i < pixeles.Length; i++)
        {
            float v = (float)rnd.NextDouble();
            Color c;
            if (v > 0.93f) c = new Color(0.56f, 0.54f, 0.50f, 1f);       // piedrecitas
            else if (v < 0.08f) c = baseTierra * 0.55f;                     // huecos oscuros
            else c = baseTierra * Mathf.Lerp(0.75f, 1.12f, v);
            c.a = 1f;
            pixeles[i] = c;
        }
        tex.SetPixels(pixeles);
        tex.Apply();
        return tex;
    }

    /// <summary>"Clic" corto procedural para los botones.</summary>
    public static AudioClip CrearSonidoClic()
    {
        const int frecuenciaMuestreo = 44100;
        int muestras = (int)(frecuenciaMuestreo * 0.07f);
        float[] datos = new float[muestras];
        System.Random rnd = new System.Random(7);

        for (int i = 0; i < muestras; i++)
        {
            float t = (float)i / frecuenciaMuestreo;
            float envolvente = Mathf.Exp(-t * 70f);
            float tono = Mathf.Sin(2f * Mathf.PI * 1250f * t) * 0.6f;
            float ruido = ((float)rnd.NextDouble() * 2f - 1f) * 0.25f;
            datos[i] = (tono + ruido) * envolvente * 0.7f;
        }

        AudioClip clip = AudioClip.Create("Menu_Clic", muestras, 1, frecuenciaMuestreo, false);
        clip.SetData(datos, 0);
        return clip;
    }

    /// <summary>Crea un EventSystem si no existe, con el módulo de entrada correcto para el Input System activo.</summary>
    public static void AsegurarEventSystem()
    {
        EventSystem es = Object.FindAnyObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject go = new GameObject("EventSystem (auto)");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            var modulo = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            modulo.AssignDefaultActions(); // sin acciones asignadas los botones no responden al ratón
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            return;
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // Con SOLO el Input System nuevo, StandaloneInputModule lanza excepciones cada frame y los botones no responden
        StandaloneInputModule antiguo = es.GetComponent<StandaloneInputModule>();
        if (antiguo != null)
        {
            Object.Destroy(antiguo);
            if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                var modulo = es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                modulo.AssignDefaultActions();
            }
        }
#endif
    }

    /// <summary>Escribe un texto en un Text o TextMeshPro que esté en el objeto o en sus hijos.</summary>
    public static void SetTexto(GameObject go, string texto)
    {
        if (go == null) return;

        TMP_Text tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            tmp.text = texto;
            return;
        }

        Text t = go.GetComponentInChildren<Text>(true);
        if (t != null) t.text = texto;
    }
}

// =============================================================================
// MENÚS EN REALIDAD VIRTUAL (META QUEST)
// =============================================================================

/// <summary>
/// Hace visibles y usables dentro del visor los menús uGUI generados por código
/// (menú principal, pausa, controles y Game Over).
///
/// POR QUÉ HACE FALTA: un Canvas "Screen Space - Overlay" (igual que OnGUI) se dibuja sobre la pantalla
/// del PC, pero NO dentro de las lentes de un visor VR. En Meta Quest el menú existía y congelaba el juego,
/// pero era invisible.
///
/// Con visor activo:
///  - El Canvas pasa a "World Space" y flota delante de la cabeza ('Distancia' metros, 'Ancho' metros de ancho).
///    Si giras la cabeza y el panel queda fuera de la vista, se recoloca suavemente delante de ti.
///  - Sale un láser del mando derecho (el mando con el que pulses el gatillo pasa a ser el activo).
///    Apunta a un botón y pulsa el gatillo (o A / X) para pulsarlo. Sin mandos se apunta con la mirada.
///  - Opcionalmente el panel se dibuja por encima de paredes y objetos para que nunca quede tapado.
/// Sin visor (PC, móvil, Editor sin Link) no cambia nada.
/// </summary>
public static class UIVR
{
    /// <summary>Distancia (m) entre los ojos y el panel.</summary>
    public static float Distancia = 1.6f;

    /// <summary>Ancho (m) del panel en el mundo.</summary>
    public static float Ancho = 1.9f;

    /// <summary>El panel se dibuja por encima de paredes y objetos.</summary>
    public static bool SiempreVisible = true;

    private const float LONGITUD_LASER = 4f;
    private const float TIEMPO_COLOCACION_INICIAL = 0.75f; // el seguimiento de la cabeza tarda unos frames en arrancar

    private static readonly Color ColorLaser = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color ColorLaserSobreBoton = new Color(1f, 1f, 0.45f, 1f);

    private static Canvas canvasActual;
    private static float tiempoMostrado;
    private static bool siguiendoCabeza;
    private static bool manoIzquierdaActiva;
    private static bool izquierdoAntes;
    private static bool derechoAntes;
    private static GameObject objetoHover;
    private static GameObject objetoPresionado;
    private static PointerEventData datosPuntero;
    private static EventSystem eventSystemDatos;
    private static LineRenderer laser;
    private static RectTransform puntoCursor;
    private static Material materialSiempreVisible;
    private static Material materialLaser;
    private static readonly List<Selectable> seleccionables = new List<Selectable>();
    private static readonly List<Graphic> graficos = new List<Graphic>();
    private static readonly List<UnityEngine.XR.InputDevice> dispositivosXR = new List<UnityEngine.XR.InputDevice>();

    private static int visorStandalone = -1; // -1 = sin comprobar, 0 = no, 1 = sí

    /// <summary>
    /// True si el juego se está viendo con un visor VR. En Quest también se reconoce el dispositivo
    /// por su modelo, por si el subsistema XR aún no ha terminado de arrancar en los primeros frames.
    /// </summary>
    public static bool Activo
    {
        get
        {
            try { if (XRSettings.isDeviceActive) return true; } catch { }

            if (visorStandalone < 0)
            {
                string modelo = (SystemInfo.deviceModel ?? "").ToUpperInvariant();
                visorStandalone = Application.platform == RuntimePlatform.Android &&
                                  (modelo.Contains("QUEST") || modelo.Contains("OCULUS")) ? 1 : 0;
            }
            return visorStandalone == 1;
        }
    }

    /// <summary>True mientras el láser VR está manejando algún menú.</summary>
    public static bool PunteroEnUso => canvasActual != null && Activo;

    /// <summary>True si el láser apunta a un botón (el gatillo / A pulsarán ese botón).</summary>
    public static bool ApuntandoBoton => objetoHover != null && canvasActual != null;

    /// <summary>
    /// Llamar cada frame (LateUpdate) mientras el menú esté visible. Sin visor no hace nada.
    /// </summary>
    public static void Actualizar(Canvas canvas)
    {
        if (canvas == null || !canvas.gameObject.activeInHierarchy || !Activo)
        {
            Soltar(canvas);
            return;
        }

        Transform cabeza = ObtenerCabeza();
        if (cabeza == null) return;

        if (canvas.renderMode != RenderMode.WorldSpace) ConvertirAMundo(canvas, cabeza);

        if (canvas != canvasActual)
        {
            SalirDelBoton();
            canvasActual = canvas;
            tiempoMostrado = Time.unscaledTime;
            siguiendoCabeza = true;

            // Si el gatillo ya estaba apretado al abrirse (p. ej. al morir rociando el extintor), no cuenta como clic
            LeerBotones(out izquierdoAntes, out derechoAntes);
        }

        SeguirCabeza(canvas, cabeza, Time.unscaledTime - tiempoMostrado < TIEMPO_COLOCACION_INICIAL);
        ActualizarPuntero(canvas, cabeza);
    }

    /// <summary>Oculta el láser y suelta el botón resaltado. Llamar al cerrar el menú.</summary>
    public static void Soltar(Canvas canvas)
    {
        // Si otro menú (que sigue existiendo) está usando el puntero, no se le quita
        if (canvasActual != null && canvas != canvasActual) return;

        SalirDelBoton();
        canvasActual = null;
        if (laser != null) laser.enabled = false;
        if (puntoCursor != null) puntoCursor.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Panel en el mundo
    // -------------------------------------------------------------------------

    /// <summary>Transform de los ojos del jugador (CenterEyeAnchor o la cámara principal).</summary>
    public static Transform ObtenerCabeza()
    {
        CameraMovement cm = CameraMovement.Instance;
        if (cm != null && cm.VRCenterEyeAnchor != null) return cm.VRCenterEyeAnchor;

        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    private static void ConvertirAMundo(Canvas canvas, Transform cabeza)
    {
        RectTransform rt = (RectTransform)canvas.transform;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();

        // Tamaño de diseño del menú (1920x1080): así se conservan todas las posiciones de los botones
        Vector2 tamano = new Vector2(1920f, 1080f);
        if (scaler != null && scaler.referenceResolution.x > 1f && scaler.referenceResolution.y > 1f)
        {
            tamano = scaler.referenceResolution;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        Camera cam = cabeza.GetComponent<Camera>();
        canvas.worldCamera = cam != null ? cam : Camera.main;

        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = tamano;
        rt.localScale = Vector3.one * (Mathf.Max(0.2f, Ancho) / tamano.x);

        // Texto más nítido visto de cerca
        if (scaler != null) scaler.dynamicPixelsPerUnit = 3f;

        if (SiempreVisible) AplicarMaterialSiempreVisible(canvas);
    }

    /// <summary>
    /// Copia del material de UI por defecto con ZTest Always: el panel se ve aunque haya una pared
    /// entre los ojos y él (por ejemplo si el jugador empieza pegado a un muro).
    /// </summary>
    internal static Material MaterialSiempreVisible()
    {
        if (materialSiempreVisible == null)
        {
            Material baseUI = Canvas.GetDefaultCanvasMaterial();
            if (baseUI == null) return null;
            materialSiempreVisible = new Material(baseUI) { name = "UI_VR_SiempreVisible (auto)" };
            materialSiempreVisible.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        return materialSiempreVisible;
    }

    internal static void AplicarMaterialSiempreVisible(Canvas canvas)
    {
        Material mat = MaterialSiempreVisible();
        if (mat == null) return;

        canvas.GetComponentsInChildren(true, graficos);
        for (int i = 0; i < graficos.Count; i++)
        {
            Graphic g = graficos[i];
            if (g == null || g is TMP_Text) continue;      // TextMeshPro usa su propio material
            if (g.material != g.defaultMaterial) continue; // se respetan los materiales personalizados
            g.material = mat;
        }
        graficos.Clear();
    }

    /// <summary>
    /// Mantiene el panel delante de la cara. Solo se recoloca cuando se sale bastante de la vista,
    /// para que no "persiga" cada pequeño movimiento de cabeza (marea).
    /// </summary>
    private static void SeguirCabeza(Canvas canvas, Transform cabeza, bool inmediato)
    {
        Vector3 adelante = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up);
        if (adelante.sqrMagnitude < 0.0001f) adelante = Vector3.ProjectOnPlane(cabeza.up, Vector3.up); // mirando al techo o al suelo
        if (adelante.sqrMagnitude < 0.0001f) adelante = Vector3.forward;
        adelante.Normalize();

        float distancia = Mathf.Max(0.5f, Distancia);
        Vector3 objetivo = cabeza.position + adelante * distancia + Vector3.down * 0.05f;
        Quaternion rotacionObjetivo = Quaternion.LookRotation(adelante, Vector3.up);

        Transform t = canvas.transform;
        if (inmediato)
        {
            t.SetPositionAndRotation(objetivo, rotacionObjetivo);
            siguiendoCabeza = false;
            return;
        }

        Vector3 haciaPanel = Vector3.ProjectOnPlane(t.position - cabeza.position, Vector3.up);
        float angulo = Vector3.Angle(haciaPanel, adelante);
        float errorDistancia = Mathf.Abs(haciaPanel.magnitude - distancia);
        float errorAltura = Mathf.Abs(t.position.y - objetivo.y);
        if (angulo > 35f || errorDistancia > 0.6f || errorAltura > 0.45f) siguiendoCabeza = true;

        if (!siguiendoCabeza) return;

        float k = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
        t.SetPositionAndRotation(Vector3.Lerp(t.position, objetivo, k), Quaternion.Slerp(t.rotation, rotacionObjetivo, k));
        if ((t.position - objetivo).sqrMagnitude < 0.0004f) siguiendoCabeza = false;
    }

    // -------------------------------------------------------------------------
    // Láser del mando
    // -------------------------------------------------------------------------

    private static void ActualizarPuntero(Canvas canvas, Transform cabeza)
    {
        // 1. Gatillos: el mando con el que se pulsa pasa a ser el puntero
        LeerBotones(out bool izquierdo, out bool derecho);
        bool bajaIzq = izquierdo && !izquierdoAntes, subeIzq = !izquierdo && izquierdoAntes;
        bool bajaDer = derecho && !derechoAntes, subeDer = !derecho && derechoAntes;
        izquierdoAntes = izquierdo;
        derechoAntes = derecho;

        if (bajaIzq && !bajaDer && !manoIzquierdaActiva) { manoIzquierdaActiva = true; SoltarPresion(); }
        else if (bajaDer && !bajaIzq && manoIzquierdaActiva) { manoIzquierdaActiva = false; SoltarPresion(); }

        bool baja = manoIzquierdaActiva ? bajaIzq : bajaDer;
        bool sube = manoIzquierdaActiva ? subeIzq : subeDer;

        // 2. Rayo desde el mando (o desde la mirada si no hay mandos)
        Transform mano = ObtenerMano(manoIzquierdaActiva);
        Ray rayo = mano != null ? new Ray(mano.position, mano.forward) : new Ray(cabeza.position, cabeza.forward);

        // 3. ¿Dónde toca el rayo el panel?
        RectTransform rtCanvas = (RectTransform)canvas.transform;
        Plane plano = new Plane(rtCanvas.forward, rtCanvas.position);
        float largo = LONGITUD_LASER;
        bool sobrePanel = false;
        Vector3 punto = Vector3.zero;
        Vector2 puntoLocal = Vector2.zero;

        if (plano.Raycast(rayo, out float d) && d > 0f && d < 20f)
        {
            punto = rayo.GetPoint(d);
            puntoLocal = rtCanvas.InverseTransformPoint(punto);
            if (rtCanvas.rect.Contains(puntoLocal))
            {
                sobrePanel = true;
                largo = d;
            }
        }

        // 4. Botón bajo el puntero (el último de la jerarquía es el que se dibuja encima)
        GameObject nuevoHover = null;
        if (sobrePanel)
        {
            canvas.GetComponentsInChildren(false, seleccionables);
            for (int i = seleccionables.Count - 1; i >= 0; i--)
            {
                Selectable s = seleccionables[i];
                if (s == null || !s.isActiveAndEnabled || !s.IsInteractable()) continue;

                RectTransform rt = (RectTransform)s.transform;
                if (rt.rect.Contains((Vector2)rt.InverseTransformPoint(punto)))
                {
                    nuevoHover = s.gameObject;
                    break;
                }
            }
            seleccionables.Clear();
        }

        // 5. Eventos de UI normales: el botón se resalta y se pulsa igual que con el ratón
        GameObject hoverAnterior = objetoHover;
        if (nuevoHover != objetoHover)
        {
            if (objetoHover != null) ExecuteEvents.Execute(objetoHover, Datos(), ExecuteEvents.pointerExitHandler);
            objetoHover = nuevoHover;
            if (objetoHover != null) ExecuteEvents.Execute(objetoHover, Datos(), ExecuteEvents.pointerEnterHandler);
        }

        // Solo se presiona un botón que ya estaba resaltado: si la pantalla acaba de cambiar (p. ej. con A),
        // el botón nuevo que quede bajo el láser no se pulsa "solo"
        if (baja && objetoHover != null && objetoHover == hoverAnterior)
        {
            objetoPresionado = objetoHover;
            ExecuteEvents.Execute(objetoPresionado, Datos(), ExecuteEvents.pointerDownHandler);
        }

        if (sube && objetoPresionado != null)
        {
            // Se pulsa al SOLTAR el gatillo (como un clic): así el gatillo no sigue apretado al volver al juego
            GameObject presionado = objetoPresionado;
            objetoPresionado = null;
            ExecuteEvents.Execute(presionado, Datos(), ExecuteEvents.pointerUpHandler);
            if (presionado == objetoHover) ExecuteEvents.Execute(presionado, Datos(), ExecuteEvents.pointerClickHandler);
        }

        // 6. Dibujo del láser y del punto
        if (canvasActual == null) return; // el clic ha cerrado el menú (Soltar ya lo ocultó todo)

        AsegurarLaser();
        if (laser != null)
        {
            laser.enabled = mano != null; // con la mirada no se dibuja la línea, solo el punto
            laser.SetPosition(0, rayo.origin);
            laser.SetPosition(1, rayo.GetPoint(largo));
            Color c = objetoHover != null ? ColorLaserSobreBoton : ColorLaser;
            laser.startColor = c;
            laser.endColor = new Color(c.r, c.g, c.b, sobrePanel ? c.a : 0f);
        }

        AsegurarCursor(canvas);
        if (puntoCursor != null)
        {
            if (puntoCursor.gameObject.activeSelf != sobrePanel) puntoCursor.gameObject.SetActive(sobrePanel);
            if (sobrePanel)
            {
                puntoCursor.anchoredPosition = puntoLocal;
                if (puntoCursor.GetSiblingIndex() != canvas.transform.childCount - 1) puntoCursor.SetAsLastSibling();
            }
        }
    }

    private static Transform ObtenerMano(bool izquierda)
    {
        CameraMovement cm = CameraMovement.Instance;
        if (cm == null) return null;

        Transform preferida = izquierda ? cm.VRLeftControllerAnchor : cm.VRRightControllerAnchor;
        Transform otra = izquierda ? cm.VRRightControllerAnchor : cm.VRLeftControllerAnchor;

        if (preferida != null && MandoConectado(izquierda)) return preferida;
        if (otra != null && MandoConectado(!izquierda)) return otra;
        return null; // sin mandos: se apunta con la mirada
    }

    private static bool MandoConectado(bool izquierdo)
    {
        try { return OVRInput.IsControllerConnected(izquierdo ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch); }
        catch { return true; } // sin OVRManager no se puede saber: se usa el anchor igualmente
    }

    /// <summary>Gatillo o botón A/X de cada mando (OVRInput y, como respaldo, XR genérico de Unity).</summary>
    private static void LeerBotones(out bool izquierdo, out bool derecho)
    {
        izquierdo = false;
        derecho = false;

        try
        {
            // Con un solo mando indicado, "Primary" es el gatillo de ese mando y "One" es X (izq.) / A (der.)
            izquierdo = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) ||
                        OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch);
            derecho = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                      OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        }
        catch { }

        if (!izquierdo) izquierdo = LeerBotonXR(InputDeviceCharacteristics.Left);
        if (!derecho) derecho = LeerBotonXR(InputDeviceCharacteristics.Right);
    }

    private static bool LeerBotonXR(InputDeviceCharacteristics lado)
    {
        try
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | lado, dispositivosXR);
            for (int i = 0; i < dispositivosXR.Count; i++)
            {
                if (dispositivosXR[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool gatillo) && gatillo) return true;
                if (dispositivosXR[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool boton) && boton) return true;
            }
        }
        catch { }
        return false;
    }

    private static PointerEventData Datos()
    {
        EventSystem es = EventSystem.current;
        if (datosPuntero == null || eventSystemDatos != es)
        {
            datosPuntero = new PointerEventData(es);
            eventSystemDatos = es;
        }
        datosPuntero.button = PointerEventData.InputButton.Left;
        return datosPuntero;
    }

    private static void SalirDelBoton()
    {
        SoltarPresion();
        if (objetoHover != null) ExecuteEvents.Execute(objetoHover, Datos(), ExecuteEvents.pointerExitHandler);
        objetoHover = null;
    }

    private static void SoltarPresion()
    {
        if (objetoPresionado != null) ExecuteEvents.Execute(objetoPresionado, Datos(), ExecuteEvents.pointerUpHandler);
        objetoPresionado = null;
    }

    private static void AsegurarLaser()
    {
        if (laser != null) return;

        GameObject go = new GameObject("LaserVR (auto)");
        laser = go.AddComponent<LineRenderer>();
        laser.positionCount = 2;
        laser.useWorldSpace = true;
        laser.startWidth = 0.006f;
        laser.endWidth = 0.003f;
        laser.numCapVertices = 2;
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laser.receiveShadows = false;

        if (materialLaser == null)
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null) materialLaser = new Material(s) { name = "LaserVR (auto)" };
            else if (Canvas.GetDefaultCanvasMaterial() != null) materialLaser = new Material(Canvas.GetDefaultCanvasMaterial());
        }
        if (materialLaser != null) laser.material = materialLaser;
    }

    private static void AsegurarCursor(Canvas canvas)
    {
        if (puntoCursor == null)
        {
            GameObject go = new GameObject("CursorVR", typeof(RectTransform));
            puntoCursor = (RectTransform)go.transform;
            puntoCursor.sizeDelta = new Vector2(18f, 18f);

            Image img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            if (SiempreVisible && MaterialSiempreVisible() != null) img.material = MaterialSiempreVisible();

            Outline borde = go.AddComponent<Outline>();
            borde.effectColor = new Color(0f, 0f, 0f, 0.9f);
            borde.effectDistance = new Vector2(2f, -2f);
        }

        if (puntoCursor.parent != canvas.transform)
        {
            puntoCursor.SetParent(canvas.transform, false);
            puntoCursor.anchorMin = puntoCursor.anchorMax = new Vector2(0.5f, 0.5f);
            puntoCursor.localScale = Vector3.one;
            puntoCursor.localRotation = Quaternion.identity;
        }
    }
}

// =============================================================================
// HUD EN REALIDAD VIRTUAL (META QUEST)
// =============================================================================

/// <summary>Canales de mensajes del HUD VR. El orden es la prioridad (de arriba a abajo en el panel).</summary>
public enum CanalHUD
{
    Combate = 0,     // PlayerHealth: golpes del sismo, fuego, curación
    Fuego = 1,       // VendingMachineHazard: "¡FUEGO! ..."
    Maquina = 2,     // Compra: golpear la máquina / máquina en llamas
    Interaccion = 3, // PlayerInteraction: recoger el objeto apuntado
    Extintor = 4,    // ExtinguisherSprayController: extintor listo
    Inventario = 5,  // InventorySystem: notificaciones del inventario
    Aviso = 6        // MovementSystemSelector: avisos generales (modo elegido, etc.)
}

/// <summary>
/// HUD flotante para VR. Todo lo que se dibuja con OnGUI (barra de vida, avisos, inventario, barra del extintor)
/// solo aparece en el monitor, nunca dentro del visor. Con visor activo cada script publica aquí lo que antes
/// dibujaba con OnGUI, y este HUD lo muestra en un panel que sigue suavemente a la cabeza, algo por debajo de la mirada:
///  - Arriba: etiqueta del modo Simulacro y barra de progreso (apagar el fuego).
///  - Centro: hasta 4 mensajes (golpes, "¡FUEGO!", "[Grip] Recoger ...", avisos).
///  - Abajo: salud (izquierda) e inventario (derecha).
/// Además, un "velo" pegado a los ojos tiñe la vista: destello rojo al recibir daño, verde al curarse,
/// latido rojo con poca vida y fundido a negro al morir.
/// Sin visor no se crea nada y todo sigue usando OnGUI.
/// </summary>
public static class HUDVR
{
    /// <summary>True si hay visor: los scripts publican aquí y dejan de usar OnGUI.</summary>
    public static bool Activo => UIVR.Activo;

    /// <summary>Distancia (m) del HUD a los ojos.</summary>
    public static float Distancia = 1.1f;

    /// <summary>Ancho (m) del HUD.</summary>
    public static float Ancho = 0.85f;

    /// <summary>Cuánto baja el HUD respecto al centro de la mirada (m), para no tapar lo que tienes delante.</summary>
    public static float Descenso = 0.18f;

    private const int NUM_CANALES = 7;
    private const int LINEAS = 4;
    private const float ANCHO_BARRA_VIDA = 400f;
    private const float ANCHO_BARRA_PROGRESO = 692f;
    private static readonly Vector2 TAMANO = new Vector2(1000f, 600f);

    private struct Entrada
    {
        public string original; // texto recibido (para no adaptar el mismo texto cada frame)
        public string texto;    // texto adaptado a VR (botones del mando, sin emojis)
        public Color color;
        public float caduca;    // Time.unscaledTime en que deja de mostrarse (mensajes con duración)
        public int frame;       // último frame en que se publicó (mensajes "mientras se llame cada frame")
        public bool porFrame;
    }

    private static readonly Entrada[] canales = new Entrada[NUM_CANALES];

    // Datos publicados por los scripts
    private static int frameVida = -10;
    private static float vidaActual, vidaMaxima = 100f, vidaRetraso;
    private static bool vidaEnFuego;
    private static int frameProgreso = -10;
    private static string progresoTexto = "";
    private static float progresoValor;
    private static Color progresoColor = Color.white;
    private static int frameInventario = -10;
    private static string inventarioTexto = "";
    private static int frameVelo = -10;
    private static Color veloColor;
    private static Texture veloTextura;

    // UI generada
    private static Canvas canvas;
    private static GameObject cajaVida;
    private static Text textoVida;
    private static RectTransform rellenoVida;
    private static RectTransform retrasoVida;
    private static Image imagenRellenoVida;
    private static Outline bordeVida;
    private static GameObject cajaProgreso;
    private static Text textoProgreso;
    private static RectTransform rellenoProgreso;
    private static Image imagenRellenoProgreso;
    private static GameObject cajaInventario;
    private static Text textoInventario;
    private static Text textoModo;
    private static readonly GameObject[] cajasLinea = new GameObject[LINEAS];
    private static readonly Text[] textosLinea = new Text[LINEAS];
    private static Canvas canvasVelo;
    private static RawImage imagenVelo;
    private static int ultimaVidaMostrada = -1;
    private static int ultimaVidaMaxima = -1;
    private static bool colocado;

    // Atajos de teclado que aparecen en los mensajes de los scripts -> botones del mando
    private static readonly string[,] Reemplazos =
    {
        { "Mantén [Clic Izq] o [E]", "Mantén el gatillo" },
        { "[Mantén Clic Izq / E / Espacio / Gatillo VR]", "[Mantén el gatillo]" },
        { "[Clic Izq / E / VR Trigger]", "[Gatillo]" },
        { "[Clic / F]", "[Gatillo]" },
        { "(Presiona [E] para usar)", "([Grip] para usar)" },
        { "con [1] o [2]", "con [Y]" },
        { "[E]", "[Grip]" },
        { "[Q]", "[Y]" },
    };

    // -------------------------------------------------------------------------
    // API para los scripts
    // -------------------------------------------------------------------------

    /// <summary>
    /// Muestra un mensaje en el canal indicado (solo uno por canal: el último sustituye al anterior).
    /// duracion &gt; 0: se mantiene ese tiempo. duracion = 0: solo mientras se siga llamando cada frame.
    /// </summary>
    public static void Mensaje(CanalHUD canal, string texto, Color color, float duracion = 0f)
    {
        if (!Activo) return;
        int i = (int)canal;
        if (i < 0 || i >= NUM_CANALES) return;

        ref Entrada e = ref canales[i];
        if (string.IsNullOrEmpty(texto))
        {
            e.porFrame = false;
            e.caduca = 0f;
            return;
        }

        if (!ReferenceEquals(e.original, texto))
        {
            e.original = texto;
            e.texto = AdaptarTexto(texto);
        }
        e.color = color;

        if (duracion > 0f)
        {
            e.porFrame = false;
            e.caduca = Time.unscaledTime + duracion;
        }
        else
        {
            e.porFrame = true;
            e.frame = Time.frameCount;
        }
    }

    /// <summary>Barra de vida (llamar cada frame).</summary>
    public static void Vida(float actual, float maxima, float retraso, bool enFuego)
    {
        if (!Activo) return;
        vidaActual = actual;
        vidaMaxima = Mathf.Max(1f, maxima);
        vidaRetraso = retraso;
        vidaEnFuego = enFuego;
        frameVida = Time.frameCount;
    }

    /// <summary>Barra de progreso superior, por ejemplo apagar el incendio (llamar cada frame).</summary>
    public static void Progreso(string texto, float valor, Color color)
    {
        if (!Activo) return;
        progresoTexto = texto ?? "";
        progresoValor = Mathf.Clamp01(valor);
        progresoColor = color;
        frameProgreso = Time.frameCount;
    }

    /// <summary>Recuadro del inventario (llamar cada frame).</summary>
    public static void Inventario(string texto)
    {
        if (!Activo) return;
        inventarioTexto = texto ?? "";
        frameInventario = Time.frameCount;
    }

    /// <summary>Tiñe toda la vista (llamar cada frame mientras dure). Con textura, por ejemplo una viñeta.</summary>
    public static void Velo(Color color, Texture textura = null)
    {
        if (!Activo) return;
        veloColor = color;
        veloTextura = textura;
        frameVelo = Time.frameCount;
    }

    /// <summary>
    /// Coloca y refresca el HUD. Lo llama MovementSystemSelector una vez por frame (LateUpdate).
    /// </summary>
    public static void Actualizar(bool mostrarHUD, bool permitirVelo, string etiquetaModo)
    {
        if (!Activo)
        {
            OcultarTodo();
            return;
        }

        Transform cabeza = UIVR.ObtenerCabeza();
        if (cabeza == null) return;

        ActualizarVelo(cabeza, permitirVelo);

        if (!mostrarHUD)
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            colocado = false;
            return;
        }

        AsegurarHUD();
        if (canvas == null) return;
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);

        SeguirCabeza(cabeza);
        Refrescar(etiquetaModo);
    }

    // -------------------------------------------------------------------------
    // Funcionamiento interno
    // -------------------------------------------------------------------------

    private static bool Reciente(int frame) => Time.frameCount - frame <= 1; // tolera el orden de ejecución entre scripts

    private static void OcultarTodo()
    {
        if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
        if (canvasVelo != null && canvasVelo.gameObject.activeSelf) canvasVelo.gameObject.SetActive(false);
        colocado = false;
    }

    /// <summary>
    /// Sigue a la cabeza con suavidad. Va colgado del jugador (CameraMovement), así que al caminar se mueve
    /// con él sin quedarse atrás; el suavizado solo actúa al girar la cabeza.
    /// </summary>
    private static void SeguirCabeza(Transform cabeza)
    {
        Transform t = canvas.transform;

        CameraMovement cm = CameraMovement.Instance;
        Transform padre = cm != null ? cm.transform : null;
        if (t.parent != padre)
        {
            t.SetParent(padre, true);
            colocado = false;
        }

        // Escala en el mundo constante aunque el jugador tenga escala
        float escalaPadre = padre != null ? Mathf.Max(0.0001f, padre.lossyScale.x) : 1f;
        t.localScale = Vector3.one * (Mathf.Max(0.2f, Ancho) / TAMANO.x / escalaPadre);

        Vector3 direccion = cabeza.forward;
        Vector3 objetivo = cabeza.position + direccion * Mathf.Max(0.4f, Distancia) - cabeza.up * Descenso;
        Vector3 arriba = Mathf.Abs(Vector3.Dot(direccion, Vector3.up)) > 0.95f ? cabeza.up : Vector3.up;
        Quaternion rotacion = Quaternion.LookRotation(objetivo - cabeza.position, arriba);

        if (!colocado)
        {
            t.SetPositionAndRotation(objetivo, rotacion);
            colocado = true;
            return;
        }

        float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        t.SetPositionAndRotation(Vector3.Lerp(t.position, objetivo, k), Quaternion.Slerp(t.rotation, rotacion, k));
    }

    private static void Refrescar(string etiquetaModo)
    {
        float ahora = Time.unscaledTime;

        // Etiqueta del modo
        bool hayEtiqueta = !string.IsNullOrEmpty(etiquetaModo);
        if (textoModo.gameObject.activeSelf != hayEtiqueta) textoModo.gameObject.SetActive(hayEtiqueta);
        if (hayEtiqueta) textoModo.text = etiquetaModo;

        // Barra de progreso
        bool hayProgreso = Reciente(frameProgreso);
        if (cajaProgreso.activeSelf != hayProgreso) cajaProgreso.SetActive(hayProgreso);
        if (hayProgreso)
        {
            textoProgreso.text = progresoTexto;
            rellenoProgreso.sizeDelta = new Vector2(ANCHO_BARRA_PROGRESO * progresoValor, rellenoProgreso.sizeDelta.y);
            imagenRellenoProgreso.color = progresoColor;
        }

        // Mensajes, por orden de prioridad
        int linea = 0;
        for (int i = 0; i < NUM_CANALES && linea < LINEAS; i++)
        {
            Entrada e = canales[i];
            bool vigente = e.porFrame ? Reciente(e.frame) : ahora < e.caduca;
            if (!vigente || string.IsNullOrEmpty(e.texto)) continue;

            Color c = e.color;
            if (!e.porFrame) c.a *= Mathf.Clamp01((e.caduca - ahora) * 2f); // se desvanece al final
            textosLinea[linea].text = e.texto;
            textosLinea[linea].color = c;
            if (!cajasLinea[linea].activeSelf) cajasLinea[linea].SetActive(true);
            linea++;
        }
        for (; linea < LINEAS; linea++)
        {
            if (cajasLinea[linea].activeSelf) cajasLinea[linea].SetActive(false);
        }

        // Salud
        bool hayVida = Reciente(frameVida);
        if (cajaVida.activeSelf != hayVida) cajaVida.SetActive(hayVida);
        if (hayVida)
        {
            int valor = Mathf.CeilToInt(vidaActual);
            int maximo = Mathf.RoundToInt(vidaMaxima);
            if (valor != ultimaVidaMostrada || maximo != ultimaVidaMaxima)
            {
                ultimaVidaMostrada = valor;
                ultimaVidaMaxima = maximo;
                textoVida.text = "SALUD   " + valor + " / " + maximo;
            }

            float ratio = Mathf.Clamp01(vidaActual / vidaMaxima);
            float ratioRetraso = Mathf.Clamp01(vidaRetraso / vidaMaxima);
            rellenoVida.sizeDelta = new Vector2(ANCHO_BARRA_VIDA * ratio, rellenoVida.sizeDelta.y);
            retrasoVida.sizeDelta = new Vector2(ANCHO_BARRA_VIDA * Mathf.Max(ratio, ratioRetraso), retrasoVida.sizeDelta.y);

            Color colorVida = ratio > 0.5f ? new Color(0.2f, 0.88f, 0.4f, 1f)
                : (ratio > 0.25f ? new Color(0.98f, 0.65f, 0.15f, 1f) : new Color(0.95f, 0.22f, 0.22f, 1f));
            imagenRellenoVida.color = colorVida;
            textoVida.color = colorVida;

            Color borde;
            if (vidaEnFuego) borde = new Color(1f, 0.45f, 0.1f, 0.95f);
            else if (ratio <= 0.28f) borde = Color.Lerp(new Color(0.9f, 0.2f, 0.2f, 0.95f), new Color(1f, 0.5f, 0.5f, 0.5f), (Mathf.Sin(ahora * 8f) + 1f) * 0.5f);
            else borde = new Color(0f, 0.85f, 1f, 0.5f);
            bordeVida.effectColor = borde;
        }

        // Inventario
        bool hayInventario = Reciente(frameInventario) && !string.IsNullOrEmpty(inventarioTexto);
        if (cajaInventario.activeSelf != hayInventario) cajaInventario.SetActive(hayInventario);
        if (hayInventario) textoInventario.text = inventarioTexto;
    }

    /// <summary>Velo pegado a los ojos (hijo de la cámara) que tiñe toda la vista.</summary>
    private static void ActualizarVelo(Transform cabeza, bool permitido)
    {
        bool mostrar = permitido && Reciente(frameVelo) && veloColor.a > 0.003f;
        if (!mostrar)
        {
            if (canvasVelo != null && canvasVelo.gameObject.activeSelf) canvasVelo.gameObject.SetActive(false);
            return;
        }

        if (canvasVelo == null)
        {
            GameObject go = new GameObject("VeloVR (auto)", typeof(RectTransform));
            canvasVelo = go.AddComponent<Canvas>();
            canvasVelo.renderMode = RenderMode.WorldSpace;
            canvasVelo.sortingOrder = 1000; // por encima de cualquier otro panel
            ((RectTransform)go.transform).sizeDelta = new Vector2(100f, 100f);

            GameObject img = new GameObject("Velo", typeof(RectTransform));
            img.transform.SetParent(go.transform, false);
            RectTransform rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            imagenVelo = img.AddComponent<RawImage>();
            imagenVelo.raycastTarget = false;
            if (UIVR.MaterialSiempreVisible() != null) imagenVelo.material = UIVR.MaterialSiempreVisible();
        }

        Transform tv = canvasVelo.transform;
        if (tv.parent != cabeza)
        {
            // Justo detrás del plano de recorte cercano de la cámara, tapando todo el campo de visión
            Camera cam = cabeza.GetComponent<Camera>();
            float d = cam != null ? Mathf.Max(0.3f, cam.nearClipPlane * 1.5f + 0.05f) : 0.3f;
            tv.SetParent(cabeza, false);
            tv.localPosition = new Vector3(0f, 0f, d);
            tv.localRotation = Quaternion.identity;
            tv.localScale = Vector3.one * (d * 3.2f / 100f);
        }

        if (!canvasVelo.gameObject.activeSelf) canvasVelo.gameObject.SetActive(true);
        imagenVelo.texture = veloTextura;
        imagenVelo.color = veloColor;
    }

    private static void AsegurarHUD()
    {
        if (canvas != null) return;

        GameObject go = new GameObject("HUD_VR (auto)", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3f; // texto nítido
        RectTransform rt = (RectTransform)go.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = TAMANO;
        // Sin GraphicRaycaster: el HUD no se pulsa

        Transform raiz = go.transform;
        Color fondo = new Color(0.05f, 0.07f, 0.11f, 0.78f);

        // Etiqueta del modo (arriba)
        textoModo = MenuUIBuilder.CrearTexto(raiz, "Modo", "", 26, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 272f), new Vector2(600f, 40f), new Color(0.45f, 1f, 0.5f, 0.95f));

        // Barra de progreso
        cajaProgreso = MenuUIBuilder.CrearCaja(raiz, "Progreso", new Vector2(0f, 205f), new Vector2(700f, 62f), fondo);
        rellenoProgreso = CrearRelleno(cajaProgreso.transform, "Relleno", new Vector2(ANCHO_BARRA_PROGRESO, 54f), Color.white, out imagenRellenoProgreso);
        textoProgreso = MenuUIBuilder.CrearTexto(cajaProgreso.transform, "Texto", "", 30, FontStyle.Bold, TextAnchor.MiddleCenter,
            Vector2.zero, new Vector2(680f, 56f), Color.white);
        Outline bordeProgreso = cajaProgreso.AddComponent<Outline>();
        bordeProgreso.effectColor = new Color(0f, 0.9f, 1f, 0.8f);
        bordeProgreso.effectDistance = new Vector2(3f, -3f);

        // Líneas de mensajes
        for (int i = 0; i < LINEAS; i++)
        {
            cajasLinea[i] = MenuUIBuilder.CrearCaja(raiz, "Mensaje" + i, new Vector2(0f, 115f - i * 66f), new Vector2(960f, 58f), fondo);
            textosLinea[i] = MenuUIBuilder.CrearTexto(cajasLinea[i].transform, "Texto", "", 30, FontStyle.Bold, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(940f, 54f), Color.white);
            cajasLinea[i].SetActive(false);
        }

        // Salud (abajo a la izquierda)
        cajaVida = MenuUIBuilder.CrearCaja(raiz, "Salud", new Vector2(-262f, -235f), new Vector2(440f, 100f), fondo);
        bordeVida = cajaVida.AddComponent<Outline>();
        bordeVida.effectDistance = new Vector2(3f, -3f);
        textoVida = MenuUIBuilder.CrearTexto(cajaVida.transform, "Texto", "SALUD", 30, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0f, 22f), new Vector2(400f, 40f), Color.white);
        GameObject pista = MenuUIBuilder.CrearCaja(cajaVida.transform, "Pista", new Vector2(0f, -24f), new Vector2(ANCHO_BARRA_VIDA + 8f, 32f), new Color(0.12f, 0.14f, 0.18f, 0.95f));
        retrasoVida = CrearRelleno(pista.transform, "Retraso", new Vector2(ANCHO_BARRA_VIDA, 24f), new Color(1f, 0.85f, 0.35f, 0.75f), out _);
        rellenoVida = CrearRelleno(pista.transform, "Vida", new Vector2(ANCHO_BARRA_VIDA, 24f), Color.green, out imagenRellenoVida);

        // Inventario (abajo a la derecha)
        cajaInventario = MenuUIBuilder.CrearCaja(raiz, "Inventario", new Vector2(262f, -235f), new Vector2(440f, 100f), fondo);
        Outline bordeInventario = cajaInventario.AddComponent<Outline>();
        bordeInventario.effectColor = new Color(0f, 0.85f, 1f, 0.5f);
        bordeInventario.effectDistance = new Vector2(3f, -3f);
        textoInventario = MenuUIBuilder.CrearTexto(cajaInventario.transform, "Texto", "", 24, FontStyle.Bold, TextAnchor.MiddleLeft,
            Vector2.zero, new Vector2(410f, 92f), Color.white);

        cajaProgreso.SetActive(false);
        cajaVida.SetActive(false);
        cajaInventario.SetActive(false);
        textoModo.gameObject.SetActive(false);

        UIVR.AplicarMaterialSiempreVisible(canvas); // el HUD nunca queda tapado por paredes
        ultimaVidaMostrada = -1;
        colocado = false;
    }

    /// <summary>Barra anclada a la izquierda de su recuadro: el ancho indica el valor.</summary>
    private static RectTransform CrearRelleno(Transform padre, string nombre, Vector2 tamano, Color color, out Image imagen)
    {
        GameObject go = MenuUIBuilder.CrearCaja(padre, nombre, Vector2.zero, tamano, color);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(4f, 0f);
        imagen = go.GetComponent<Image>();
        imagen.raycastTarget = false;
        return rt;
    }

    /// <summary>
    /// Cambia los atajos de teclado por los botones del mando y quita los emojis
    /// (la fuente del juego no los tiene y saldrían como cuadrados).
    /// </summary>
    private static string AdaptarTexto(string s)
    {
        for (int i = 0; i < Reemplazos.GetLength(0); i++)
        {
            if (s.IndexOf(Reemplazos[i, 0], System.StringComparison.Ordinal) >= 0)
            {
                s = s.Replace(Reemplazos[i, 0], Reemplazos[i, 1]);
            }
        }

        System.Text.StringBuilder sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool quitar = char.IsSurrogate(c) || c == '️' || c == '‍' || c == 'ℹ' ||
                          (c >= '☀' && c <= '➿') || (c >= '⬀' && c <= '⯿');
            if (quitar)
            {
                if (sb == null) sb = new System.Text.StringBuilder(s.Length).Append(s, 0, i);
                continue;
            }
            if (sb != null) sb.Append(c);
        }
        return sb != null ? sb.ToString().Trim() : s;
    }
}

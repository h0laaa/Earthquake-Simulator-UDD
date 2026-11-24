using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sistema integral de salud y barra de vida del jugador.
/// - Se visualiza en la esquina inferior izquierda de la pantalla con un diseño moderno y pulido.
/// - Sufre daño continuo al entrar en contacto cercano con el fuego de la máquina expendedora.
/// - Sufre daño leve al chocar contra paredes, muebles u obstáculos mientras ocurre el sismo (CameraShake).
/// - Permite restaurar salud consumiendo el Botiquín del inventario.
/// - Incluye efectos visuales en pantalla (destello rojo por daño, viñeta pulsante por salud crítica)
///   y efectos de sonido procedurales integrados.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    public enum DamageType
    {
        Generic,
        Fire,
        ShakeCollision
    }

    [Header("--- AJUSTES DE SALUD Y CURACIÓN (EDITABLES) ---")]
    [Tooltip("Salud máxima del jugador")]
    [Range(20f, 300f)]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("Salud actual del jugador")]
    [SerializeField] private float currentHealth = 100f;

    [Tooltip("Cantidad de salud que restaura el Botiquín al interactuar con él o usarlo")]
    [Range(5f, 100f)]
    [SerializeField] private float botiquinHealAmount = 50f;

    [Header("--- DAÑO POR CONTACTO CON FUEGO (EDITABLE) ---")]
    [Tooltip("Puntos de salud que se pierden por segundo de exposición al fuego")]
    [Range(1f, 60f)]
    [SerializeField] private float fireDamagePerSecond = 18f;

    [Tooltip("Distancia en metros al fuego para considerarse en contacto y recibir daño")]
    [Range(0.5f, 6f)]
    [SerializeField] private float fireContactDistance = 2.4f;

    [Header("--- DAÑO POR CHOQUE DURANTE TEMBLOR (EDITABLE) ---")]
    [Tooltip("Puntos de salud que se pierden al chocar levemente con un obstáculo durante el sismo")]
    [Range(0.5f, 25f)]
    [SerializeField] private float shakeCollisionDamage = 3.5f;

    [Tooltip("Tiempo de enfriamiento en segundos entre golpes consecutivos por sismo")]
    [Range(0.05f, 2f)]
    [SerializeField] private float shakeCollisionCooldown = 0.45f;

    [Header("Visualización del HUD (Esquina Inferior Izquierda)")]
    [Tooltip("Muestra la barra de salud gráfica en pantalla")]
    [SerializeField] private bool showHUD = true;

    [Tooltip("Margen horizontal desde el borde izquierdo")]
    [SerializeField] private float marginX = 22f;

    [Tooltip("Margen vertical desde el borde inferior")]
    [SerializeField] private float marginY = 22f;

    [Tooltip("Ancho total de la tarjeta de salud")]
    [SerializeField] private float cardWidth = 240f;

    [Tooltip("Alto total de la tarjeta de salud")]
    [SerializeField] private float cardHeight = 56f;

    [Header("Efectos Visuales en Pantalla")]
    [Tooltip("Muestra destellos en pantalla al recibir daño o curarse")]
    [SerializeField] private bool showScreenEffects = true;

    [Tooltip("Muestra una viñeta roja pulsante cuando la salud está en nivel crítico (< 28%)")]
    [SerializeField] private bool showLowHealthVignette = true;

    [Header("Efectos de Sonido")]
    [Tooltip("Habilita sonidos de impacto, quemadura y curación")]
    [SerializeField] private bool playAudio = true;

    [Tooltip("Sonido opcional de golpe/impacto (si es nulo, se genera audio procedural)")]
    [SerializeField] private AudioClip customHitSound;

    [Tooltip("Sonido opcional de quemadura (si es nulo, se genera audio procedural)")]
    [SerializeField] private AudioClip customBurnSound;

    [Tooltip("Sonido opcional de curación (si es nulo, se genera audio procedural)")]
    [SerializeField] private AudioClip customHealSound;

    [Header("--- PANTALLA DE MUERTE / FOTO DE FAMILIA (EDITABLE) ---")]
    [Tooltip("Textura de la foto familiar que se muestra al morir")]
    [SerializeField] private Texture2D familyPhotoTexture;

    [Tooltip("Duración en segundos del efecto fade in a negro")]
    [Range(0.5f, 6f)]
    [SerializeField] private float deathFadeInDuration = 2.0f;

    [Header("--- GAME OVER (UI) ---")]
    [Tooltip("Panel de Game Over que se activa al morir. Si se deja vacío y 'Generar UI Game Over Automáticamente' está activo, se crea uno")]
    [SerializeField] private GameObject panelGameOver;

    [Tooltip("Botón para volver al menú principal")]
    [SerializeField] private Button botonMenuPrincipal;

    [Tooltip("Botón para volver a intentar desde el inicio (reinicia la escena)")]
    [SerializeField] private Button botonReintentar;

    [Tooltip("(Opcional) Nombre de la escena del menú principal. Vacío = recargar esta escena y mostrar el menú de inicio")]
    [SerializeField] private string escenaMenuPrincipal = "";

    [Tooltip("Segundos tras morir antes de mostrar el panel de Game Over (deja ver el fundido)")]
    [Range(0f, 6f)]
    [SerializeField] private float retrasoPanelGameOver = 1.5f;

    [Tooltip("Construye el panel de Game Over automáticamente si no se asigna en el Inspector")]
    [SerializeField] private bool generarUIGameOverAutomaticamente = true;

    // Estados internos
    private float displayedHealth = 100f;
    private float lastShakeCollisionTime = -10f;
    private float fireBurnAudioTimer = 0f;
    private bool isInFireContact = false;
    private bool isDead = false;
    private float respawnTimer = 0f;
    private bool panelGameOverVisible = false;
    private bool listenersGameOver = false;
    private bool fotoFamiliaBuscada = false;
    private Canvas canvasGameOver;

    // Feedback visual temporal
    private float hurtFlashAlpha = 0f;
    private float healFlashAlpha = 0f;
    private string combatBannerMessage = "";
    private Color combatBannerColor = Color.white;
    private float combatBannerTimer = 0f;

    // Componentes
    private Camera playerCamera;
    private AudioSource audioSource;

    // Clips procedurales
    private AudioClip generatedHitAudio;
    private AudioClip generatedBurnAudio;
    private AudioClip generatedHealAudio;
    private AudioClip generatedHeartbeatAudio;
    private float heartbeatAudioTimer = 0f;

    // Texturas y estilos OnGUI
    private Texture2D cardBackgroundTex;
    private Texture2D borderTex;
    private Texture2D whitePixelTex;
    private Texture2D crossIconTex;
    private Texture2D vignetteTex;

    private GUIStyle labelStyle;
    private GUIStyle hpValueStyle;
    private GUIStyle bannerStyle;
    private GUIStyle deathTitleStyle;
    private GUIStyle deathSubtitleStyle;

    // Eventos
    public event Action<float, DamageType> OnDamageTaken;
    public event Action<float> OnHealed;
    public event Action OnPlayerDied;
    public event Action OnPlayerRevived;

    // Propiedades públicas (Lectura y Edición en tiempo de ejecución)
    public float CurrentHealth => currentHealth;
    public float HealthRatio => Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    public bool IsAlive => !isDead && currentHealth > 0.001f;
    public bool IsInFireContact => isInFireContact;

    public float MaxHealth
    {
        get => maxHealth;
        set
        {
            maxHealth = Mathf.Max(1f, value);
            currentHealth = Mathf.Min(currentHealth, maxHealth);
        }
    }

    public float BotiquinHealAmount
    {
        get => botiquinHealAmount;
        set => botiquinHealAmount = Mathf.Max(1f, value);
    }

    public float FireDamagePerSecond
    {
        get => fireDamagePerSecond;
        set => fireDamagePerSecond = Mathf.Max(0f, value);
    }

    public float FireContactDistance
    {
        get => fireContactDistance;
        set => fireContactDistance = Mathf.Max(0.1f, value);
    }

    public float ShakeCollisionDamage
    {
        get => shakeCollisionDamage;
        set => shakeCollisionDamage = Mathf.Max(0f, value);
    }

    public float ShakeCollisionCooldown
    {
        get => shakeCollisionCooldown;
        set => shakeCollisionCooldown = Mathf.Max(0.01f, value);
    }

    private static bool sceneHookInstalled = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();

        // AfterSceneLoad solo se ejecuta UNA vez al arrancar: sin esto, al reiniciar la escena
        // (botón Reintentar) el jugador se quedaba sin PlayerHealth.
        if (!sceneHookInstalled)
        {
            sceneHookInstalled = true;
            SceneManager.sceneLoaded += (escena, modo) => EnsureInstance();
        }
    }

    public static PlayerHealth EnsureInstance()
    {
        if (Instance != null) return Instance;

        PlayerHealth existente = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        if (existente != null) return existente;

        Camera cam = Camera.main;
        if (cam == null) cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            PlayerHealth ph = cam.GetComponent<PlayerHealth>();
            if (ph == null)
            {
                ph = cam.gameObject.AddComponent<PlayerHealth>();
            }
            return ph;
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

        // AudioSource PROPIO: compartirlo con CameraShake hacía que su Stop()/fundido silenciara los golpes y latidos
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D para que se escuche siempre nítido

        currentHealth = maxHealth;
        displayedHealth = currentHealth;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UIVR.Soltar(canvasGameOver);
    }

    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) playerCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        }

        GenerateProceduralAudio();
        EnsureFamilyPhotoTexture();
    }

    private void OnValidate()
    {
        EnsureFamilyPhotoTexture();
    }

    public void EnsureFamilyPhotoTexture()
    {
        if (familyPhotoTexture != null) return;

        // En juego solo se busca una vez (antes se accedía al disco en cada OnGUI de la pantalla de muerte)
        if (Application.isPlaying)
        {
            if (fotoFamiliaBuscada) return;
            fotoFamiliaBuscada = true;
        }

#if UNITY_EDITOR
        familyPhotoTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imagenes/Foto_Familia.png");
        if (familyPhotoTexture != null) return;
#endif

        string diskPath = System.IO.Path.Combine(Application.dataPath, "Imagenes", "Foto_Familia.png");
        if (System.IO.File.Exists(diskPath))
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(diskPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    tex.name = "Foto_Familia";
                    familyPhotoTexture = tex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerHealth] No se pudo cargar Foto_Familia.png: " + ex.Message);
            }
        }
    }

    private void Update()
    {
        // Si está en estado de inconsciencia/reanimación
        if (isDead)
        {
            UpdateDeathState();
            return;
        }

        // 1. Suavizar barra de vida de retraso (damage lag bar)
        displayedHealth = Mathf.MoveTowards(displayedHealth, currentHealth, Time.deltaTime * 32f);

        // 2. Comprobar contacto con el fuego de la máquina expendedora
        CheckFireContactDamage();

        // 3. Reducir destellos en pantalla
        if (hurtFlashAlpha > 0f)
        {
            hurtFlashAlpha = Mathf.MoveTowards(hurtFlashAlpha, 0f, Time.deltaTime * 2.2f);
        }

        if (healFlashAlpha > 0f)
        {
            healFlashAlpha = Mathf.MoveTowards(healFlashAlpha, 0f, Time.deltaTime * 1.8f);
        }

        // 4. Reducir temporizador del banner de advertencia de combate
        if (combatBannerTimer > 0f)
        {
            combatBannerTimer -= Time.deltaTime;
        }

        // 5. Latido cardíaco en salud crítica (< 28%)
        if (showLowHealthVignette && currentHealth <= maxHealth * 0.28f && currentHealth > 0f)
        {
            heartbeatAudioTimer += Time.deltaTime;
            if (heartbeatAudioTimer >= 0.85f)
            {
                heartbeatAudioTimer = 0f;
                PlayHeartbeatAudio();
            }
        }
        else
        {
            heartbeatAudioTimer = 0.5f;
        }
    }

    private void LateUpdate()
    {
        PublicarHUDVR();

        // VR (Meta Quest): el Game Over flota delante del visor y sus botones se pulsan con el láser del mando.
        // Un Canvas "Screen Space - Overlay" no se ve dentro del visor (ver UIVR en MovementSystemSelector.cs).
        if (!isDead || !panelGameOverVisible || panelGameOver == null || !panelGameOver.activeInHierarchy) return;

        if (canvasGameOver == null)
        {
            Canvas c = panelGameOver.GetComponentInParent<Canvas>();
            canvasGameOver = c != null ? c.rootCanvas : null;
        }
        UIVR.Actualizar(canvasGameOver);
    }

    /// <summary>
    /// VR: lo mismo que dibuja OnGUI (barra de vida, destellos, viñeta y fundido a negro) pero en el HUD VR,
    /// porque OnGUI no se ve dentro del visor. Mismo orden de prioridad que OnGUI.
    /// </summary>
    private void PublicarHUDVR()
    {
        if (!HUDVR.Activo) return;
        if (MovementSystemSelector.Instance != null && MovementSystemSelector.Instance.IsOpen) return;

        if (isDead)
        {
            // Fundido a negro hasta que aparece el panel de Game Over (que ya tiene su propio fondo)
            if (!panelGameOverVisible)
            {
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(respawnTimer / Mathf.Max(0.1f, deathFadeInDuration)));
                HUDVR.Velo(new Color(0f, 0f, 0f, fade));
            }
            return;
        }

        if (showHUD) HUDVR.Vida(currentHealth, maxHealth, displayedHealth, isInFireContact);

        if (showScreenEffects && hurtFlashAlpha > 0.005f)
        {
            HUDVR.Velo(new Color(0.85f, 0.08f, 0.08f, Mathf.Clamp01(hurtFlashAlpha * 0.4f)));
        }
        else if (showScreenEffects && healFlashAlpha > 0.005f)
        {
            HUDVR.Velo(new Color(0.12f, 0.9f, 0.35f, Mathf.Clamp01(healFlashAlpha * 0.3f)));
        }
        else if (showLowHealthVignette && currentHealth <= maxHealth * 0.28f && currentHealth > 0f)
        {
            if (vignetteTex == null) vignetteTex = CreateVignetteTexture(128, 128);
            float pulse = (Mathf.Sin(Time.time * 7.5f) + 1f) * 0.5f;
            HUDVR.Velo(new Color(0.95f, 0.08f, 0.08f, Mathf.Lerp(0.25f, 0.55f, pulse)), vignetteTex);
        }
    }

    /// <summary>
    /// Comprueba continuamente si el jugador se encuentra dentro del radio del fuego ardiente.
    /// </summary>
    private void CheckFireContactDamage()
    {
        isInFireContact = false;

        if (VendingMachineHazard.Instance == null) return;
        if (!VendingMachineHazard.Instance.IsOnFire || VendingMachineHazard.Instance.IsExtinguished) return;

        Vector3 playerPos = playerCamera != null ? playerCamera.transform.position : transform.position;

        // Comprobar distancia a la superficie del fuego de la máquina
        // GetComponentInChildren incluye el propio objeto (el patrón "GetComponent() ?? ..." fallaba en el Editor)
        Collider machineCol = VendingMachineHazard.Instance.GetComponentInChildren<Collider>();

        float distanceToFire;
        if (machineCol != null)
        {
            Vector3 closestPoint = CameraMovement.PuntoMasCercano(machineCol, playerPos);
            distanceToFire = Vector3.Distance(playerPos, closestPoint);
        }
        else
        {
            distanceToFire = Vector3.Distance(playerPos, VendingMachineHazard.Instance.GetMachineWorldCenter());
        }

        if (distanceToFire <= fireContactDistance)
        {
            isInFireContact = true;

            // Factor de reducción si el fuego ya está siendo apagado por el extintor
            float fireIntensity = Mathf.Clamp(1f - VendingMachineHazard.Instance.ExtinguishProgress, 0.2f, 1f);
            float damageToApply = (fireDamagePerSecond * fireIntensity) * Time.deltaTime;

            ApplyDamageDirect(damageToApply, DamageType.Fire);

            // Audio de crepitar de quemadura cada 0.28s
            fireBurnAudioTimer += Time.deltaTime;
            if (fireBurnAudioTimer >= 0.28f)
            {
                fireBurnAudioTimer = 0f;
                PlayBurnAudio();
            }

            // Destello visual de fuego
            hurtFlashAlpha = Mathf.Max(hurtFlashAlpha, 0.22f);

            ShowCombatBanner("🔥 ¡CONTACTO CON EL FUEGO! Aléjate de las llamas", new Color(1f, 0.45f, 0.15f, 1f), 0.35f);
        }
        else
        {
            fireBurnAudioTimer = 0.2f;
        }
    }

    /// <summary>
    /// Registra el choque del jugador contra un obstáculo mientras ocurre el temblor de tierra.
    /// Llamado desde CameraMovement al detectar colisión con SphereCast o Overlap durante el sismo.
    /// </summary>
    public void RegisterShakeCollision(Vector3 hitPoint, Vector3 hitNormal, Collider obstacle)
    {
        if (isDead) return;

        // El piso o superficies de apoyo horizontales NUNCA deben infligir daño
        if (obstacle != null)
        {
            if (CameraMovement.EsPisoOSuelo(obstacle)) return;
            if (obstacle.GetComponent<PisoEditable>() != null) return;
            string obsName = obstacle.name.ToLower();
            if (obsName.Contains("piso") || obsName.Contains("suelo") || obsName.Contains("floor") ||
                obsName.Contains("ground") || obsName.Contains("terrain") || obsName.Contains("pavimento"))
            {
                return;
            }
        }

        // Si el impacto fue contra una superficie horizontal (suelo, rampa transitable)
        if (hitNormal.y > 0.55f)
        {
            return;
        }

        if (Time.time - lastShakeCollisionTime < shakeCollisionCooldown) return;

        lastShakeCollisionTime = Time.time;

        string obstacleName = obstacle != null ? obstacle.name : "un obstáculo";
        TakeDamage(shakeCollisionDamage, DamageType.ShakeCollision, $"Golpe contra {obstacleName}");

        PlayHitAudio();

        // Destello breve en pantalla
        hurtFlashAlpha = Mathf.Max(hurtFlashAlpha, 0.35f);

        ShowCombatBanner($"💥 ¡Impacto por sismo! -{shakeCollisionDamage:F1} HP", new Color(1f, 0.75f, 0.25f, 1f), 1.6f);
    }

    /// <summary>
    /// Aplica una cantidad de daño al jugador.
    /// </summary>
    public void TakeDamage(float amount, DamageType type = DamageType.Generic, string source = "")
    {
        if (isDead || amount <= 0.0001f) return;

        ApplyDamageDirect(amount, type);

        if (type != DamageType.Fire)
        {
            hurtFlashAlpha = Mathf.Max(hurtFlashAlpha, 0.38f);
        }

        OnDamageTaken?.Invoke(amount, type);
    }

    private void ApplyDamageDirect(float amount, DamageType type)
    {
        // Modo de juego: Simulacro = sin daño (x0), Supervivencia = x1, Extremo = más daño
        amount *= ModoDeJuego.MultiplicadorDanio;
        if (amount <= 0f) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        if (currentHealth <= 0.0001f && !isDead)
        {
            Die();
        }
    }

    /// <summary>
    /// Restaura una cantidad de salud al jugador (por ejemplo al usar el Botiquín).
    /// </summary>
    public void Heal(float amount)
    {
        if (isDead || amount <= 0.0001f) return;

        float prevHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        float gained = currentHealth - prevHealth;

        if (gained > 0.01f)
        {
            healFlashAlpha = 0.32f;
            PlayHealAudio();
            OnHealed?.Invoke(gained);
            ShowCombatBanner($"🩹 +{Mathf.RoundToInt(gained)} Salud restaurada", new Color(0.2f, 1f, 0.45f, 1f), 2.2f);
        }
    }

    private void Die()
    {
        isDead = true;
        currentHealth = 0f;
        respawnTimer = 0f;
        hurtFlashAlpha = 0f;
        isInFireContact = false;
        panelGameOverVisible = false;

        // Cursor libre y visible para poder pulsar los botones del Game Over.
        // CameraMovement, el inventario, la interacción y el extintor se bloquean solos al consultar IsAlive.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureFamilyPhotoTexture();

        OnPlayerDied?.Invoke();
        Debug.LogWarning("[PlayerHealth] El jugador ha caído a 0 puntos de vida. Mostrando Game Over.");
    }

    private void UpdateDeathState()
    {
        respawnTimer += Time.unscaledDeltaTime;

        // Mantener el cursor libre aunque otro sistema intente bloquearlo
        if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!panelGameOverVisible && respawnTimer >= retrasoPanelGameOver)
        {
            MostrarPanelGameOver();
        }
    }

    private void MostrarPanelGameOver()
    {
        panelGameOverVisible = true;

        if (panelGameOver == null && generarUIGameOverAutomaticamente)
        {
            ConstruirUIGameOver();
        }

        ConectarBotonesGameOver();
        MenuUIBuilder.AsegurarEventSystem();

        if (panelGameOver != null)
        {
            panelGameOver.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[PlayerHealth] No hay Panel Game Over asignado y la generación automática está desactivada.");
        }
    }

    private void ConectarBotonesGameOver()
    {
        if (listenersGameOver) return;
        listenersGameOver = true;

        if (botonMenuPrincipal != null) botonMenuPrincipal.onClick.AddListener(VolverAlMenuPrincipal);
        if (botonReintentar != null) botonReintentar.onClick.AddListener(ReintentarDesdeElInicio);
    }

    /// <summary>Botón MENÚ PRINCIPAL: carga la escena de menú indicada o recarga esta mostrando el menú de inicio.</summary>
    public void VolverAlMenuPrincipal()
    {
        Time.timeScale = 1f;
        MovementSystemSelector.SaltarMenuEnProximaCarga = false;

        if (!string.IsNullOrEmpty(escenaMenuPrincipal) && Application.CanStreamedLevelBeLoaded(escenaMenuPrincipal))
        {
            SceneManager.LoadScene(escenaMenuPrincipal);
            return;
        }

        RecargarEscenaActual();
    }

    /// <summary>Botón REINTENTAR: reinicia la escena desde el principio y empieza a jugar sin pasar por el menú.</summary>
    public void ReintentarDesdeElInicio()
    {
        Time.timeScale = 1f;
        MovementSystemSelector.SaltarMenuEnProximaCarga = true;
        RecargarEscenaActual();
    }

    private static void RecargarEscenaActual()
    {
        Scene actual = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // En el Editor también funciona aunque la escena no esté añadida en Build Settings
        if (actual.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(actual.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        if (actual.buildIndex >= 0) SceneManager.LoadScene(actual.buildIndex);
        else SceneManager.LoadScene(actual.name);
    }

    private void ConstruirUIGameOver()
    {
        Canvas canvas = MenuUIBuilder.CrearCanvas("GameOver_Canvas (auto)", 200);
        panelGameOver = canvas.gameObject;

        // Fondo rojizo semitransparente (estilo Minecraft) sobre negro
        MenuUIBuilder.CrearPanel(canvas.transform, "FondoNegro", new Color(0f, 0f, 0f, 0.85f));
        MenuUIBuilder.CrearPanel(canvas.transform, "FondoRojo", new Color(0.45f, 0f, 0f, 0.35f));

        MenuUIBuilder.CrearTexto(canvas.transform, "Titulo", "¡HAS MUERTO!", 80, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, 370f), new Vector2(1400f, 130f), Color.white);

        if (familyPhotoTexture != null)
        {
            MenuUIBuilder.CrearImagen(canvas.transform, "FotoFamilia", familyPhotoTexture, new Vector2(0f, 60f), new Vector2(760f, 400f));
        }

        botonReintentar = MenuUIBuilder.CrearBoton(canvas.transform, "BotonReintentar", "Volver a intentarlo", new Vector2(0f, -230f), new Vector2(600f, 76f));
        botonMenuPrincipal = MenuUIBuilder.CrearBoton(canvas.transform, "BotonMenuPrincipal", "Menú principal", new Vector2(0f, -330f), new Vector2(600f, 76f));

        panelGameOver.SetActive(false);
    }

    public void ShowCombatBanner(string message, Color color, float duration = 1.5f)
    {
        combatBannerMessage = message;
        combatBannerColor = color;
        combatBannerTimer = duration;
        HUDVR.Mensaje(CanalHUD.Combate, message, color, duration);
    }

    // --------------------------------------------------------------------------------------------
    // DIBUJADO DE LA BARRA DE VIDA Y EFECTOS EN PANTALLA (OnGUI)
    // --------------------------------------------------------------------------------------------

    private void EnsureStyles()
    {
        if (whitePixelTex == null)
        {
            whitePixelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whitePixelTex.SetPixel(0, 0, Color.white);
            whitePixelTex.Apply();
        }

        if (borderTex == null)
        {
            borderTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            borderTex.SetPixel(0, 0, Color.white);
            borderTex.Apply();
        }

        if (cardBackgroundTex == null)
        {
            cardBackgroundTex = CreateRoundedCardTexture(128, 64, 8, new Color(0.06f, 0.08f, 0.12f, 0.92f));
        }

        if (crossIconTex == null)
        {
            crossIconTex = CreateMedicalCrossTexture(32);
        }

        if (vignetteTex == null)
        {
            vignetteTex = CreateVignetteTexture(128, 128);
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = new Color(0.72f, 0.85f, 0.95f, 0.95f);
        }

        if (hpValueStyle == null)
        {
            hpValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            hpValueStyle.normal.textColor = Color.white;
        }

        if (bannerStyle == null)
        {
            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            bannerStyle.normal.textColor = Color.white;
        }

        if (deathTitleStyle == null)
        {
            deathTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            deathTitleStyle.normal.textColor = new Color(1f, 0.25f, 0.25f, 1f);
        }

        if (deathSubtitleStyle == null)
        {
            deathSubtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            deathSubtitleStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        }
    }

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (PublicarHUDVR): OnGUI no se ve dentro del visor
        if (MovementSystemSelector.Instance != null && MovementSystemSelector.Instance.IsOpen) return;

        EnsureStyles();

        // 1. Pantalla completa de desmayo/inconsciencia
        if (isDead)
        {
            DrawDeathScreen();
            return;
        }

        // 2. Destello de daño o curación en pantalla completa
        if (showScreenEffects)
        {
            DrawScreenFlashes();
        }

        // 3. Viñeta roja pulsante en salud crítica (< 28%)
        if (showLowHealthVignette && currentHealth <= maxHealth * 0.28f && currentHealth > 0f)
        {
            DrawLowHealthVignette();
        }

        // 4. Banner de advertencia de combate sobre la barra de vida
        if (combatBannerTimer > 0f && !string.IsNullOrEmpty(combatBannerMessage))
        {
            DrawCombatBanner();
        }

        // 5. Barra de vida en la esquina inferior izquierda
        if (showHUD)
        {
            DrawHealthBarHUD();
        }
    }

    private void DrawScreenFlashes()
    {
        Color prevColor = GUI.color;

        // Destello rojo por daño
        if (hurtFlashAlpha > 0.005f)
        {
            GUI.color = new Color(0.85f, 0.08f, 0.08f, Mathf.Clamp01(hurtFlashAlpha * 0.48f));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), whitePixelTex);
        }

        // Destello verde por curación
        if (healFlashAlpha > 0.005f)
        {
            GUI.color = new Color(0.12f, 0.9f, 0.35f, Mathf.Clamp01(healFlashAlpha * 0.35f));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), whitePixelTex);
        }

        GUI.color = prevColor;
    }

    private void DrawLowHealthVignette()
    {
        // Pulso sincronizado con ritmo cardíaco
        float pulse = (Mathf.Sin(Time.time * 7.5f) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(0.25f, 0.55f, pulse);

        Color prevColor = GUI.color;
        GUI.color = new Color(0.95f, 0.08f, 0.08f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignetteTex);
        GUI.color = prevColor;
    }

    private void DrawCombatBanner()
    {
        float alpha = Mathf.Clamp01(combatBannerTimer * 2f);
        float bannerY = Screen.height - marginY - cardHeight - 34f;
        Rect bannerRect = new Rect(marginX, bannerY, 320f, 26f);

        Color prevColor = GUI.color;

        // Fondo oscuro translúcido
        GUI.color = new Color(0.08f, 0.1f, 0.15f, 0.88f * alpha);
        GUI.DrawTexture(bannerRect, cardBackgroundTex);

        // Borde fino
        GUI.color = new Color(combatBannerColor.r, combatBannerColor.g, combatBannerColor.b, 0.7f * alpha);
        DrawRectBorder(bannerRect, 1);

        // Texto
        bannerStyle.normal.textColor = new Color(combatBannerColor.r, combatBannerColor.g, combatBannerColor.b, alpha);
        Rect textRect = new Rect(bannerRect.x + 10f, bannerRect.y + 2f, bannerRect.width - 20f, bannerRect.height);
        GUI.Label(textRect, combatBannerMessage, bannerStyle);

        GUI.color = prevColor;
    }

    private void DrawHealthBarHUD()
    {
        float cardY = Screen.height - marginY - cardHeight;
        Rect cardRect = new Rect(marginX, cardY, cardWidth, cardHeight);

        Color prevColor = GUI.color;

        // 1. Tarjeta de fondo oscuro translúcido
        GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        GUI.DrawTexture(cardRect, cardBackgroundTex);

        // 2. Borde de la tarjeta (Verde/Cian normal, Rojo pulsante si está en peligro o ardiendo)
        bool isCritical = currentHealth <= maxHealth * 0.28f;
        Color borderColor;

        if (isInFireContact)
        {
            borderColor = new Color(1f, 0.45f, 0.1f, 0.9f);
        }
        else if (isCritical)
        {
            float pulse = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
            borderColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f, 0.95f), new Color(1f, 0.5f, 0.5f, 0.5f), pulse);
        }
        else
        {
            borderColor = new Color(0f, 0.85f, 1f, 0.35f);
        }

        GUI.color = borderColor;
        DrawRectBorder(cardRect, 2);

        // 3. Recuadro e Icono Médico a la izquierda
        Rect iconBox = new Rect(cardRect.x + 9f, cardRect.y + 9f, 38f, 38f);
        GUI.color = new Color(0.12f, 0.15f, 0.22f, 0.9f);
        GUI.DrawTexture(iconBox, cardBackgroundTex);

        GUI.color = borderColor * 0.6f;
        DrawRectBorder(iconBox, 1);

        // Icono de Cruz Médica / Corazón
        Color crossColor;
        if (currentHealth > maxHealth * 0.5f) crossColor = new Color(0.2f, 0.88f, 0.4f, 1f);
        else if (currentHealth > maxHealth * 0.25f) crossColor = new Color(0.98f, 0.65f, 0.15f, 1f);
        else crossColor = new Color(0.95f, 0.22f, 0.22f, 1f);

        GUI.color = crossColor;
        Rect crossRect = new Rect(iconBox.x + 7f, iconBox.y + 7f, 24f, 24f);
        GUI.DrawTexture(crossRect, crossIconTex);

        // 4. Textos: Título "SALUD" a la izquierda y número "100 / 100" a la derecha
        float contentX = cardRect.x + 55f;
        float contentW = cardRect.width - 64f;

        Rect titleRect = new Rect(contentX, cardRect.y + 7f, 70f, 16f);
        GUI.color = Color.white;
        GUI.Label(titleRect, "SALUD", labelStyle);

        Rect valueRect = new Rect(contentX + contentW - 100f, cardRect.y + 7f, 100f, 16f);
        hpValueStyle.normal.textColor = crossColor;
        GUI.Label(valueRect, $"{Mathf.CeilToInt(currentHealth)} / {(int)maxHealth}", hpValueStyle);

        // 5. Barra de Vida (Pista, Barra de Retraso de Daño y Barra Activa)
        float barY = cardRect.y + 27f;
        float barHeight = 16f;
        Rect barTrackRect = new Rect(contentX, barY, contentW, barHeight);

        // Pista de fondo oscura
        GUI.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        GUI.DrawTexture(barTrackRect, whitePixelTex);

        // Borde sutil de la pista
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        DrawRectBorder(barTrackRect, 1);

        // Barra de retraso de daño (Damage Lag Bar, suavemente menguante)
        float lagRatio = Mathf.Clamp01(displayedHealth / Mathf.Max(1f, maxHealth));
        if (lagRatio > 0.001f)
        {
            Rect lagRect = new Rect(barTrackRect.x + 1f, barTrackRect.y + 1f, (barTrackRect.width - 2f) * lagRatio, barTrackRect.height - 2f);
            GUI.color = new Color(1f, 0.85f, 0.35f, 0.75f);
            GUI.DrawTexture(lagRect, whitePixelTex);
        }

        // Barra activa de salud
        float activeRatio = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
        if (activeRatio > 0.001f)
        {
            Rect activeRect = new Rect(barTrackRect.x + 1f, barTrackRect.y + 1f, (barTrackRect.width - 2f) * activeRatio, barTrackRect.height - 2f);
            GUI.color = crossColor;
            GUI.DrawTexture(activeRect, whitePixelTex);

            // Brillo superior para aspecto 3D pulido
            Rect glossRect = new Rect(activeRect.x, activeRect.y, activeRect.width, 3f);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(glossRect, whitePixelTex);
        }

        GUI.color = prevColor;
    }

    private void DrawDeathScreen()
    {
        // Cuando el panel de Game Over (uGUI) está visible, dejar de dibujar el fundido OnGUI encima de sus botones
        if (panelGameOverVisible) return;

        EnsureFamilyPhotoTexture();

        // Progresión suave de desvanecimiento (Fade In a negro)
        float fade = Mathf.Clamp01(respawnTimer / Mathf.Max(0.1f, deathFadeInDuration));
        float smoothFade = Mathf.SmoothStep(0f, 1f, fade);

        Color prevColor = GUI.color;

        // 1. Pantalla completa en negro puro (fade in)
        GUI.color = new Color(0f, 0f, 0f, smoothFade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), whitePixelTex);

        // 2. Imagen Foto_Familia en el centro de la pantalla
        if (familyPhotoTexture != null)
        {
            // El fade in de la foto familiar acompaña la oscuridad de manera armónica
            float photoFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((respawnTimer - 0.25f) / Mathf.Max(0.1f, deathFadeInDuration - 0.25f)));

            float texW = familyPhotoTexture.width;
            float texH = familyPhotoTexture.height;
            float aspect = texW / Mathf.Max(1f, texH);

            // Escala proporcional elegante (máximo 62% del alto y 68% del ancho)
            float maxH = Screen.height * 0.62f;
            float maxW = Screen.width * 0.68f;

            float photoH = maxH;
            float photoW = photoH * aspect;
            if (photoW > maxW)
            {
                photoW = maxW;
                photoH = photoW / aspect;
            }

            float photoX = (Screen.width - photoW) * 0.5f;
            float photoY = (Screen.height - photoH) * 0.47f; // Centrado ligeramente hacia arriba
            Rect photoRect = new Rect(photoX, photoY, photoW, photoH);

            // Marco blanco sutil estilo fotografía física impresa
            float borderMargin = 6f;
            Rect borderRect = new Rect(photoRect.x - borderMargin, photoRect.y - borderMargin, photoRect.width + borderMargin * 2f, photoRect.height + borderMargin * 2f);
            GUI.color = new Color(0.95f, 0.95f, 0.95f, 0.95f * photoFade);
            GUI.DrawTexture(borderRect, whitePixelTex);

            // Fotografía familiar en el centro
            GUI.color = new Color(1f, 1f, 1f, photoFade);
            GUI.DrawTexture(photoRect, familyPhotoTexture, ScaleMode.StretchToFill);
        }

        // (La pista "Espacio / Clic para reanimar" se sustituye por los botones del panel de Game Over)

        GUI.color = prevColor;
    }

    private void DrawRectBorder(Rect r, int thickness)
    {
        // Superior
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), borderTex);
        // Inferior
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), borderTex);
        // Izquierda
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), borderTex);
        // Derecha
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), borderTex);
    }

    // --------------------------------------------------------------------------------------------
    // GENERACIÓN DE AUDIO PROCEDURAL
    // --------------------------------------------------------------------------------------------

    private void GenerateProceduralAudio()
    {
        generatedHitAudio = GenerateHitAudioClip();
        generatedBurnAudio = GenerateBurnAudioClip();
        generatedHealAudio = GenerateHealAudioClip();
        generatedHeartbeatAudio = GenerateHeartbeatAudioClip();
    }

    private void PlayHitAudio()
    {
        if (!playAudio || audioSource == null) return;
        AudioClip clip = customHitSound != null ? customHitSound : generatedHitAudio;
        if (clip != null)
        {
            audioSource.PlayOneShot(clip, 0.85f);
        }
    }

    private void PlayBurnAudio()
    {
        if (!playAudio || audioSource == null) return;
        AudioClip clip = customBurnSound != null ? customBurnSound : generatedBurnAudio;
        if (clip != null)
        {
            audioSource.PlayOneShot(clip, 0.65f);
        }
    }

    private void PlayHealAudio()
    {
        if (!playAudio || audioSource == null) return;
        AudioClip clip = customHealSound != null ? customHealSound : generatedHealAudio;
        if (clip != null)
        {
            audioSource.PlayOneShot(clip, 0.8f);
        }
    }

    private void PlayHeartbeatAudio()
    {
        if (!playAudio || audioSource == null) return;
        if (generatedHeartbeatAudio != null)
        {
            audioSource.PlayOneShot(generatedHeartbeatAudio, 0.7f);
        }
    }

    private AudioClip GenerateHitAudioClip()
    {
        int sampleRate = 44100;
        float duration = 0.32f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 14f);
            float thud = Mathf.Sin(2f * Mathf.PI * 75f * (1f - t * 0.7f) * t);
            float noise = (UnityEngine.Random.value * 2f - 1f) * 0.25f;
            data[i] = (thud * 0.75f + noise) * env;
        }

        AudioClip clip = AudioClip.Create("ShakeHitAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip GenerateBurnAudioClip()
    {
        int sampleRate = 44100;
        float duration = 0.25f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 9f);
            float hiss = (UnityEngine.Random.value * 2f - 1f) * 0.65f;
            float snap = (UnityEngine.Random.value < 0.04f) ? (UnityEngine.Random.value * 2f - 1f) * 0.8f : 0f;
            data[i] = (hiss + snap) * env;
        }

        AudioClip clip = AudioClip.Create("FireBurnAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip GenerateHealAudioClip()
    {
        int sampleRate = 44100;
        float duration = 0.55f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            float tone1 = Mathf.Sin(2f * Mathf.PI * (523.25f + t * 200f) * t); // C5 a D5
            float tone2 = Mathf.Sin(2f * Mathf.PI * 659.25f * t) * 0.5f;        // E5
            float tone3 = Mathf.Sin(2f * Mathf.PI * 783.99f * t) * 0.4f;        // G5
            data[i] = (tone1 + tone2 + tone3) * env * 0.35f;
        }

        AudioClip clip = AudioClip.Create("HealChimeAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip GenerateHeartbeatAudioClip()
    {
        int sampleRate = 44100;
        float duration = 0.45f;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            // Doble latido: pum... pum...
            float env1 = Mathf.Exp(-Mathf.Max(0f, t - 0.02f) * 18f) * (t < 0.2f ? 1f : 0f);
            float env2 = Mathf.Exp(-Mathf.Max(0f, t - 0.18f) * 20f) * (t >= 0.18f ? 0.7f : 0f);
            float wave1 = Mathf.Sin(2f * Mathf.PI * 55f * t);
            float wave2 = Mathf.Sin(2f * Mathf.PI * 50f * t);
            data[i] = (wave1 * env1 + wave2 * env2) * 0.85f;
        }

        AudioClip clip = AudioClip.Create("HeartbeatAudio", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // --------------------------------------------------------------------------------------------
    // TEXTURAS PROCEDURALES
    // --------------------------------------------------------------------------------------------

    private Texture2D CreateRoundedCardTexture(int width, int height, int radius, Color fillColor)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int dx = Mathf.Min(x, width - 1 - x);
                int dy = Mathf.Min(y, height - 1 - y);

                if (dx < radius && dy < radius)
                {
                    float dist = Vector2.Distance(new Vector2(dx, dy), new Vector2(radius, radius));
                    if (dist > radius)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }
                }

                tex.SetPixel(x, y, fillColor);
            }
        }

        tex.Apply();
        return tex;
    }

    private Texture2D CreateMedicalCrossTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        int barThickness = Mathf.Max(4, size / 3);
        int halfThickness = barThickness / 2;
        int center = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inVertical = Mathf.Abs(x - center) <= halfThickness && y >= 3 && y < size - 3;
                bool inHorizontal = Mathf.Abs(y - center) <= halfThickness && x >= 3 && x < size - 3;

                if (inVertical || inHorizontal)
                {
                    tex.SetPixel(x, y, Color.white);
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        return tex;
    }

    private Texture2D CreateVignetteTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(width * 0.5f, height * 0.5f);
        float maxDist = center.magnitude;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float factor = Mathf.Clamp01(dist / maxDist);
                float alpha = Mathf.Pow(factor, 2.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        return tex;
    }

    // --------------------------------------------------------------------------------------------
    // PRUEBAS EN EL EDITOR (ContextMenu)
    // --------------------------------------------------------------------------------------------

    [ContextMenu("Prueba: Recibir Daño (15 HP)")]
    public void TestTakeDamage()
    {
        TakeDamage(15f, DamageType.Generic, "Prueba Editor");
    }

    [ContextMenu("Prueba: Daño Choque Sismo (3.5 HP)")]
    public void TestShakeCollisionDamage()
    {
        RegisterShakeCollision(transform.position, Vector3.up, null);
    }

    [ContextMenu("Prueba: Curar (40 HP)")]
    public void TestHeal()
    {
        Heal(40f);
    }

    [ContextMenu("Prueba: Restaurar Salud Completa")]
    public void TestFullHeal()
    {
        Heal(maxHealth);
    }

    [ContextMenu("Prueba: Morir (Game Over)")]
    public void TestDie()
    {
        TakeDamage(maxHealth * 10f, DamageType.Generic, "Prueba Editor");
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Detecta objetos interactuables frente al jugador (Botiquín, Extintor, Lata...) y permite recogerlos
/// con la tecla E, el botón táctil en pantalla o el grip de los mandos VR.
///
/// Prioridad de la tecla E:
///   1. Si se apunta a un objeto recogible -> recogerlo.
///   2. Si no, y hay un objeto en la mano  -> usarlo (beber lata, curarse, rociar extintor).
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    public static PlayerInteraction Instance { get; private set; }

    [Header("Detección de Objetos")]
    [Tooltip("Alcance base del rayo de interacción en metros. Si algún objeto tiene una distancia mayor " +
             "(por ejemplo 'Distancia Recoger Lata' en Compra), el rayo se alarga hasta esa distancia")]
    [SerializeField] private float maxDistance = 3.5f;

    [Tooltip("Radio de la esfera de detección para facilitar apuntar a objetos pequeños")]
    [SerializeField] private float detectionRadius = 0.35f;

    [Tooltip("Capas con las que interactúa el raycast")]
    [SerializeField] private LayerMask interactableLayers = ~0;

#if ENABLE_INPUT_SYSTEM
    [Header("Controles de Teclado (PC)")]
    [Tooltip("Tecla para interactuar y recoger objetos")]
    [SerializeField] private Key interactKey = Key.E;
#else
    [Header("Controles de Teclado (PC)")]
    [Tooltip("Tecla para interactuar y recoger objetos")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
#endif

    [Header("Interfaz en Pantalla")]
    [Tooltip("Muestra el botón táctil y prompt flotante en pantalla")]
    [SerializeField] private bool showPromptUI = true;

    [Tooltip("Muestra una retícula central sutil")]
    [SerializeField] private bool showCrosshair = true;

    private const float RADIO_BUSQUEDA_CERCANA = 2.2f;

    private Camera playerCamera;
    private InteractableItem currentTarget;

    // Buffers reutilizables (sin basura de memoria por frame)
    private readonly RaycastHit[] bufferHits = new RaycastHit[24];
    private readonly Collider[] bufferOverlaps = new Collider[24];

    // Dispositivos VR
    private readonly List<UnityEngine.XR.InputDevice> vrRightControllers = new List<UnityEngine.XR.InputDevice>();
    private readonly List<UnityEngine.XR.InputDevice> vrLeftControllers = new List<UnityEngine.XR.InputDevice>();
    private bool vrInteractLastFrame = false;

    // Estilos GUI
    private GUIStyle promptButtonStyle;
    private GUIStyle promptKeyboardStyle;
    private Texture2D promptButtonBgTex;
    private Texture2D buttonBorderTex;

    /// <summary>Objeto recogible al que se está apuntando (o null).</summary>
    public InteractableItem CurrentTarget => currentTarget;
    public bool HasTarget => currentTarget != null;

    /// <summary>Raíz del jugador: sus colliders nunca cuentan como obstáculo ni como objetivo.</summary>
    private Transform RaizJugador
    {
        get
        {
            if (CameraMovement.Instance != null) return CameraMovement.Instance.transform;
            return playerCamera != null ? playerCamera.transform : transform;
        }
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

        BuscarCamara();

        if (playerCamera != null && playerCamera.CompareTag("Untagged"))
        {
            try { playerCamera.tag = "MainCamera"; } catch { }
        }
    }

    private void Start()
    {
        // En Start (no en Awake) para no crear un inventario duplicado antes de que el de la escena despierte
        if (InventorySystem.Instance == null && FindAnyObjectByType<InventorySystem>() == null)
        {
            GameObject invGO = new GameObject("InventorySystem");
            invGO.AddComponent<InventorySystem>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuscarCamara()
    {
        playerCamera = GetComponent<Camera>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamera == null) playerCamera = FindAnyObjectByType<Camera>();
    }

    private void Update()
    {
        if (MovementSystemSelector.IsGameplayBlocked)
        {
            LimpiarObjetivo();
            return;
        }

        if (playerCamera == null)
        {
            BuscarCamara();
            if (playerCamera == null) return;
        }

        DetectInteractable();
        HandleInteractionInput();
    }

    private void LimpiarObjetivo()
    {
        if (currentTarget != null)
        {
            currentTarget.SetHighlight(false);
        }
        currentTarget = null;
    }

    private void DetectInteractable()
    {
        Transform raiz = RaizJugador;
        Ray headRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        InteractableItem found = null;

        // 1. En VR se apunta con el mando derecho; si no, con la mirada
        CameraMovement cm = CameraMovement.Instance;
        bool usandoMano = cm != null && cm.IsVRPresent && cm.VRRightControllerAnchor != null;
        if (usandoMano)
        {
            Transform mano = cm.VRRightControllerAnchor;
            found = BuscarEnRayo(new Ray(mano.position, mano.forward), raiz);
        }

        if (found == null)
        {
            found = BuscarEnRayo(headRay, raiz);
        }

        // 2. Ayuda de cercanía: objetos muy próximos, delante y con línea de visión (antes encontraba objetos detrás o tras paredes)
        if (found == null)
        {
            Vector3 origen = usandoMano ? cm.VRRightControllerAnchor.position : playerCamera.transform.position;
            Vector3 adelante = usandoMano ? cm.VRRightControllerAnchor.forward : playerCamera.transform.forward;
            found = BuscarCercanoVisible(origen, adelante, raiz);
        }

        if (currentTarget != found)
        {
            if (currentTarget != null) currentTarget.SetHighlight(false);
            currentTarget = found;
            if (currentTarget != null) currentTarget.SetHighlight(true);
        }
    }

    /// <summary>
    /// SphereCast que recorre los impactos por distancia: ignora al propio jugador, devuelve el primer objeto
    /// recogible y se detiene en la primera superficie sólida (no se recoge a través de paredes).
    /// </summary>
    private InteractableItem BuscarEnRayo(Ray ray, Transform raiz)
    {
        // El rayo llega hasta el objeto con más alcance; luego cada objeto aplica su propia distancia
        float alcance = Mathf.Max(maxDistance, InteractableItem.DistanciaMaximaRegistrada);
        int n = Physics.SphereCastNonAlloc(ray, detectionRadius, bufferHits, alcance, interactableLayers, QueryTriggerInteraction.Collide);
        if (n == 0) return null;

        System.Array.Sort(bufferHits, 0, n, ComparadorDistancia.Instancia);

        for (int i = 0; i < n; i++)
        {
            Collider c = bufferHits[i].collider;
            if (c == null) continue;
            if (c.transform == raiz || c.transform.IsChildOf(raiz)) continue;

            InteractableItem item = c.GetComponentInParent<InteractableItem>();
            if (item != null && item.isActiveAndEnabled)
            {
                float distancia = bufferHits[i].distance;
                return distancia <= item.DistanciaInteraccion ? item : null;
            }

            if (!c.isTrigger) return null; // pared u objeto sólido delante
        }
        return null;
    }

    private InteractableItem BuscarCercanoVisible(Vector3 origen, Vector3 adelante, Transform raiz)
    {
        int n = Physics.OverlapSphereNonAlloc(origen, RADIO_BUSQUEDA_CERCANA, bufferOverlaps, interactableLayers, QueryTriggerInteraction.Collide);
        InteractableItem mejor = null;
        float mejorDist = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            Collider c = bufferOverlaps[i];
            if (c == null) continue;
            if (c.transform == raiz || c.transform.IsChildOf(raiz)) continue;

            InteractableItem item = c.GetComponentInParent<InteractableItem>();
            if (item == null || !item.isActiveAndEnabled) continue;

            Vector3 punto = c.bounds.center;
            Vector3 hacia = punto - origen;
            float dist = hacia.magnitude;
            if (dist < 0.001f || dist > item.DistanciaInteraccion) continue;

            // Debe estar delante (cono de ~50°)
            if (Vector3.Dot(adelante, hacia / dist) < 0.64f) continue;
            if (dist >= mejorDist) continue;
            if (!HayLineaDeVision(origen, punto, item.transform, raiz)) continue;

            mejorDist = dist;
            mejor = item;
        }
        return mejor;
    }

    private bool HayLineaDeVision(Vector3 origen, Vector3 destino, Transform objetivo, Transform raiz)
    {
        Vector3 dir = destino - origen;
        float dist = dir.magnitude;
        if (dist < 0.001f) return true;

        int n = Physics.RaycastNonAlloc(origen, dir / dist, bufferHits, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform t = bufferHits[i].collider != null ? bufferHits[i].collider.transform : null;
            if (t == null) continue;
            if (t == raiz || t.IsChildOf(raiz)) continue;
            if (t == objetivo || t.IsChildOf(objetivo)) continue;
            return false;
        }
        return true;
    }

    private void HandleInteractionInput()
    {
        bool interactPressed = false;

        // 1. Teclado (PC)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && interactKey != Key.None && Keyboard.current[interactKey].wasPressedThisFrame)
        {
            interactPressed = true;
        }
#else
        if (interactKey != KeyCode.None && Input.GetKeyDown(interactKey))
        {
            interactPressed = true;
        }
#endif

        // 2. Mandos VR / Gamepad
        if (CheckVRInteraction())
        {
            interactPressed = true;
        }

        if (!interactPressed) return;

        if (currentTarget != null)
        {
            // Recoger tiene prioridad: antes, con algo en la mano, E nunca recogía nada
            TriggerInteraction();
        }
        else if (InventorySystem.Instance != null && InventorySystem.Instance.HasSelectedItem)
        {
            InventorySystem.Instance.UseSelectedItem();
        }
    }

    /// <summary>
    /// Grip de los mandos VR (gesto de "agarrar") o botón X/Cuadrado del gamepad.
    /// Ya no usa el botón A ni el gatillo: el A es saltar y los gatillos son teletransporte / extintor / golpear.
    /// </summary>
    private bool CheckVRInteraction()
    {
        bool pressed = false;

        try
        {
            if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger) || OVRInput.Get(OVRInput.Button.SecondaryHandTrigger))
            {
                pressed = true;
            }
        }
        catch { }

        if (!pressed)
        {
            vrRightControllers.Clear();
            vrLeftControllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, vrRightControllers);
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, vrLeftControllers);

            if (vrRightControllers.Count > 0 && vrRightControllers[0].isValid &&
                vrRightControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip)
            {
                pressed = true;
            }
            if (vrLeftControllers.Count > 0 && vrLeftControllers[0].isValid &&
                vrLeftControllers[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripL) && gripL)
            {
                pressed = true;
            }
        }

#if ENABLE_INPUT_SYSTEM
        if (!pressed && Gamepad.current != null && Gamepad.current.buttonWest.isPressed)
        {
            pressed = true;
        }
#endif

        bool triggered = pressed && !vrInteractLastFrame;
        vrInteractLastFrame = pressed;
        return triggered;
    }

    public void TriggerInteraction()
    {
        InteractableItem objetivo = currentTarget;
        if (objetivo == null) return;

        objetivo.SetHighlight(false);
        bool recogido = objetivo.Interact(gameObject);

        // El objeto se desactiva o destruye al recogerlo: soltar la referencia evita NullReference/MissingReference
        if (recogido) currentTarget = null;
    }

    private static bool EsModoTactil()
    {
        return CameraMovement.Instance != null ? CameraMovement.Instance.IsTouchActive : (Application.isMobilePlatform && !UIVR.Activo);
    }

    // VR: texto del aviso de recoger (se rehace solo cuando cambia el objeto apuntado: sin basura cada frame)
    private InteractableItem objetivoHUDVR;
    private string textoHUDVR = "";

    private void LateUpdate()
    {
        // En VR OnGUI no se ve: el aviso "[Grip] Recoger ..." va al HUD VR
        if (!HUDVR.Activo || !showPromptUI || currentTarget == null || MovementSystemSelector.IsGameplayBlocked) return;

        if (currentTarget != objetivoHUDVR)
        {
            objetivoHUDVR = currentTarget;
            textoHUDVR = $"[Grip] {currentTarget.TextoAccion} {currentTarget.NombreItem}";
        }
        HUDVR.Mensaje(CanalHUD.Interaccion, textoHUDVR, new Color(0.35f, 0.95f, 1f, 1f));
    }

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (LateUpdate)
        if (MovementSystemSelector.IsGameplayBlocked) return;

        EnsureStyles();

        // 1. Retícula central
        if (showCrosshair)
        {
            float crosshairSize = 10f;
            Rect crosshairRect = new Rect((Screen.width - crosshairSize) * 0.5f, (Screen.height - crosshairSize) * 0.5f, crosshairSize, crosshairSize);
            Color dotColor = currentTarget != null ? new Color(0f, 1f, 0.85f, 0.95f) : new Color(1f, 1f, 1f, 0.45f);

            Color prevC = GUI.color;
            GUI.color = dotColor;
            GUI.DrawTexture(crosshairRect, promptButtonBgTex);
            GUI.color = prevC;
        }

        if (!showPromptUI || currentTarget == null) return;

        // 2. Indicación de interacción con el objeto apuntado
        string promptText = $"[E] {currentTarget.TextoAccion} {currentTarget.NombreItem}";
        Rect promptRect = new Rect((Screen.width - 280f) * 0.5f, Screen.height * 0.5f + 45f, 280f, 32f);

        Color prevColor = GUI.color;
        GUI.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
        GUI.DrawTexture(promptRect, promptButtonBgTex);
        GUI.color = new Color(0f, 0.9f, 1f, 0.8f);
        DrawBorder(promptRect, 2);
        GUI.color = prevColor;

        GUI.Label(promptRect, promptText, promptKeyboardStyle);

        // 3. Botón táctil (solo en modo táctil: con el cursor bloqueado en PC no se puede pulsar)
        if (EsModoTactil())
        {
            float btnWidth = 150f;
            float btnHeight = 65f;
            Rect touchBtnRect = new Rect(Screen.width - btnWidth - 30f, Screen.height * 0.5f - 30f, btnWidth, btnHeight);
            if (GUI.Button(touchBtnRect, $"RECOGER\n{currentTarget.NombreItem}", promptButtonStyle))
            {
                TriggerInteraction();
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

    private void EnsureStyles()
    {
        if (promptButtonBgTex == null)
        {
            promptButtonBgTex = new Texture2D(1, 1);
            promptButtonBgTex.SetPixel(0, 0, Color.white);
            promptButtonBgTex.Apply();

            buttonBorderTex = new Texture2D(1, 1);
            buttonBorderTex.SetPixel(0, 0, Color.white);
            buttonBorderTex.Apply();
        }

        if (promptKeyboardStyle == null)
        {
            promptKeyboardStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            promptKeyboardStyle.normal.textColor = Color.white;
        }

        if (promptButtonStyle == null)
        {
            promptButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            promptButtonStyle.normal.textColor = Color.white;
        }
    }

    private sealed class ComparadorDistancia : IComparer<RaycastHit>
    {
        public static readonly ComparadorDistancia Instancia = new ComparadorDistancia();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
}

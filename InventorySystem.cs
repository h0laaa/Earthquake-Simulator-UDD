using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sistema de inventario con exactamente 2 casillas visible en la esquina superior izquierda de la pantalla.
/// Permite seleccionar el objeto con las teclas numéricas (1 o 2), sujetarlo en primera persona en la
/// esquina inferior derecha de la pantalla y utilizarlo presionando el botón de interactuar ([E], VR o Táctil).
/// Compatible con Teclado, Ratón, Pantalla Táctil y VR.
/// </summary>
public class InventorySystem : MonoBehaviour
{
    public static InventorySystem Instance { get; private set; }

    [System.Serializable]
    public class SlotItem
    {
        public string id;
        public string itemName;
        public Texture2D icon;
        public GameObject originalObject;
        public GameObject prefabToDrop;

        /// <summary>Tipo estandarizado del objeto (curativo o herramienta). Evita depender solo del nombre.</summary>
        public InteractableItem.ItemType tipo = InteractableItem.ItemType.Personalizado;
        public bool tipoDefinido = false;

        public SlotItem(string itemName, Texture2D icon, GameObject originalObject = null, GameObject prefabToDrop = null)
        {
            this.id = Guid.NewGuid().ToString();
            this.itemName = itemName;
            this.icon = icon;
            this.originalObject = originalObject;
            this.prefabToDrop = prefabToDrop;
        }

        public SlotItem(string itemName, Texture2D icon, GameObject originalObject, GameObject prefabToDrop, InteractableItem.ItemType tipo)
            : this(itemName, icon, originalObject, prefabToDrop)
        {
            this.tipo = tipo;
            this.tipoDefinido = true;
        }
    }

    [Header("Configuración de Capacidad")]
    [Tooltip("Capacidad fija del inventario: exactamente 2 casillas")]
    [SerializeField] private int maxSlots = 2;

    [Header("Interacción con Casillas")]
    [Tooltip("Permite presionar las teclas numéricas para seleccionar o guardar el objeto")]
    [SerializeField] private bool enableKeySelection = true;

    [Tooltip("Permite hacer clic o pulsar sobre la casilla en pantalla para seleccionarla")]
    [SerializeField] private bool enableClickSelection = true;

    [Header("Visualización del HUD (Esquina Superior Izquierda)")]
    [Tooltip("Muestra el inventario gráfico en pantalla")]
    [SerializeField] private bool showHUD = true;

    [Tooltip("Margen horizontal desde el borde izquierdo de la pantalla")]
    [SerializeField] private float marginX = 22f;

    [Tooltip("Margen vertical desde el borde superior de la pantalla")]
    [SerializeField] private float marginY = 22f;

    [Tooltip("Tamaño en píxeles de cada casilla cuadrada")]
    [SerializeField] private float slotSize = 74f;

    [Tooltip("Espacio en píxeles entre las dos casillas")]
    [SerializeField] private float slotSpacing = 12f;

    [Header("Visualización en Mano (Esquina Inferior Derecha)")]
    [Tooltip("Posición relativa del objeto sostenido respecto a la cámara")]
    [SerializeField] private Vector3 heldItemOffset = new Vector3(0.24f, -0.21f, 0.48f);

    [Tooltip("Rotación inicial del objeto sostenido")]
    [SerializeField] private Vector3 heldItemRotation = new Vector3(6f, -18f, 5f);

    [Header("Consumibles")]
    [Tooltip("Salud que recupera beber una lata de refresco")]
    [Range(1f, 100f)]
    [SerializeField] private float curacionLata = 15f;

    // Almacenamiento de las 2 casillas
    private SlotItem[] slots = new SlotItem[2];

    // Casilla actualmente seleccionada en mano (-1 = manos vacías, 0 = casilla 1, 1 = casilla 2)
    private int selectedSlotIndex = -1;

    // Componentes del modelo 3D sostenido en mano (Viewmodel en primera persona)
    private Transform heldItemRoot;
    private GameObject currentHeldVisual;
    private float equipTransition = 1f;
    private float useRecoilTimer = 0f;
    private int lastUseFrame = -1;
    private Transform stagingInactivo;

    // Notificaciones temporales de recogida o inventario lleno
    private string notificationMessage = "";
    private Color notificationColor = Color.white;
    private float notificationTimer = 0f;

    // Texturas y estilos procedurales para OnGUI
    private Texture2D panelBackgroundTex;
    private Texture2D slotEmptyTex;
    private Texture2D slotBorderTex;
    private Texture2D badgeTex;
    private Texture2D actionCardBgTex;
    private Texture2D actionCardBorderTex;

    private GUIStyle headerTitleStyle;
    private GUIStyle slotNumberStyle;
    private GUIStyle slotEmptyTextStyle;
    private GUIStyle slotItemNameStyle;
    private GUIStyle notificationStyle;
    private GUIStyle dropHintStyle;
    private GUIStyle slotActionButtonStyle;
    private GUIStyle actionCardTitleStyle;
    private GUIStyle actionCardButtonStyle;
    private GUIStyle actionCardHintStyle;

    public event Action OnInventoryChanged;
    public event Action<int> OnSlotSelected;

    public int MaxSlots => maxSlots;
    public SlotItem[] Slots => slots;
    public bool IsFull => slots[0] != null && slots[1] != null;
    public bool IsEmpty => slots[0] == null && slots[1] == null;

    // Propiedades de selección en mano
    public int SelectedSlotIndex => selectedSlotIndex;
    public SlotItem SelectedItem => (selectedSlotIndex >= 0 && selectedSlotIndex < maxSlots) ? slots[selectedSlotIndex] : null;
    public bool HasSelectedItem => SelectedItem != null;
    public bool IsExtinguisherSelected => HasSelectedItem && IsExtinguisherSlot(SelectedItem);
    public bool IsBotiquinSelected => HasSelectedItem && IsBotiquinSlot(SelectedItem);
    public bool IsLataSelected => HasSelectedItem && IsLataSlot(SelectedItem);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            // Solo el componente duplicado: NUNCA el GameObject (podía ser la cámara del jugador)
            Destroy(this);
            return;
        }

        if (slots == null || slots.Length != maxSlots)
        {
            slots = new SlotItem[maxSlots];
        }
    }

    private void Start()
    {
        EnsureHeldItemRoot();
    }

    private void Update()
    {
        bool vivo = PlayerHealth.Instance == null || PlayerHealth.Instance.IsAlive;
        if (!vivo)
        {
            if (currentHeldVisual != null && currentHeldVisual.activeSelf)
            {
                currentHeldVisual.SetActive(false);
            }
            return;
        }

        if (currentHeldVisual != null && !currentHeldVisual.activeSelf && HasSelectedItem)
        {
            currentHeldVisual.SetActive(true);
        }

        if (notificationTimer > 0f)
        {
            notificationTimer -= Time.deltaTime;
        }

        if (!MovementSystemSelector.IsGameplayBlocked)
        {
            HandleInput();
        }
        UpdateHeldVisualMotion();
    }

    // =========================================================================
    // REALIDAD VIRTUAL: botón Y para cambiar de objeto y recuadro en el HUD VR
    // =========================================================================

    private bool vrCambiarAnterior = false;
    private readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> vrMandosIzquierdos =
        new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    private SlotItem[] slotsHUDVR;
    private int seleccionHUDVR = -2;
    private string textoInventarioHUDVR = "";

    private void LateUpdate()
    {
        // En VR OnGUI no se ve: el inventario va al HUD VR
        if (!HUDVR.Activo || !showHUD || MovementSystemSelector.IsGameplayBlocked) return;
        if (PlayerHealth.Instance != null && !PlayerHealth.Instance.IsAlive) return;

        if (InventarioCambioParaHUDVR()) textoInventarioHUDVR = ConstruirTextoInventarioVR();
        HUDVR.Inventario(textoInventarioHUDVR);
    }

    /// <summary>True si cambió la selección o el contenido de alguna casilla (el texto solo se rehace entonces).</summary>
    private bool InventarioCambioParaHUDVR()
    {
        if (slotsHUDVR == null || slotsHUDVR.Length != slots.Length)
        {
            slotsHUDVR = new SlotItem[slots.Length];
            seleccionHUDVR = -2;
        }

        bool cambio = seleccionHUDVR != selectedSlotIndex;
        seleccionHUDVR = selectedSlotIndex;
        for (int i = 0; i < slots.Length; i++)
        {
            if (ReferenceEquals(slotsHUDVR[i], slots[i])) continue;
            slotsHUDVR[i] = slots[i];
            cambio = true;
        }
        return cambio;
    }

    private string ConstruirTextoInventarioVR()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
        SlotItem enMano = SelectedItem;
        sb.Append(enMano != null ? "EN MANO: " + enMano.itemName : "MANOS LIBRES");
        sb.Append('\n');
        for (int i = 0; i < slots.Length; i++)
        {
            if (i > 0) sb.Append("     ");
            sb.Append(i + 1).Append(": ").Append(slots[i] != null ? slots[i].itemName : "vacía");
        }
        sb.Append("\n[Y] cambiar     [Grip] usar");
        return sb.ToString();
    }

    /// <summary>Botón Y del mando izquierdo (en el visor no hay teclas 1 / 2 / Q). Devuelve true solo al pulsarlo.</summary>
    private bool BotonCambiarObjetoVR()
    {
        bool pulsado = false;

        // Con un solo mando indicado, "Two" es el botón Y del mando izquierdo
        try { pulsado = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch); } catch { }

        if (!pulsado && UIVR.Activo)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, vrMandosIzquierdos);
            for (int i = 0; i < vrMandosIzquierdos.Count && !pulsado; i++)
            {
                if (vrMandosIzquierdos[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool y) && y) pulsado = true;
            }
        }

        bool alPulsar = pulsado && !vrCambiarAnterior;
        vrCambiarAnterior = pulsado;
        return alPulsar;
    }

    /// <summary>
    /// VR: manos libres -> casilla 1 -> casilla 2 -> manos libres (saltando las casillas vacías).
    /// Sirve para guardar el extintor y poder golpear la máquina, o para sacar el botiquín.
    /// </summary>
    public void CambiarObjetoVR()
    {
        for (int i = selectedSlotIndex + 1; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            SelectSlot(i);
            return;
        }

        if (selectedSlotIndex >= 0) DeselectSlot();
        else ShowNotification("Inventario vacío: recoge objetos con [Grip]", new Color(0.7f, 0.8f, 0.9f, 1f), 2f);
    }

    private void HandleInput()
    {
        // VR: no hay teclas 1 / 2 / Q en el visor -> botón Y del mando izquierdo
        if (BotonCambiarObjetoVR()) CambiarObjetoVR();

        if (!enableKeySelection) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current[Key.Digit1].wasPressedThisFrame || Keyboard.current[Key.Numpad1].wasPressedThisFrame)
                HandleSlotSelection(0);
            if (Keyboard.current[Key.Digit2].wasPressedThisFrame || Keyboard.current[Key.Numpad2].wasPressedThisFrame)
                HandleSlotSelection(1);
            if (Keyboard.current[Key.Q].wasPressedThisFrame || Keyboard.current[Key.Digit0].wasPressedThisFrame)
                DeselectSlot();
            if (Keyboard.current[Key.G].wasPressedThisFrame)
                DropSelectedItem();
            // [E] lo gestiona SOLO PlayerInteraction (recoger si se apunta a un objeto, si no usar el de la mano).
            // Antes también se procesaba aquí y una pulsación podía usar el objeto y recoger otro a la vez.
            if (Keyboard.current[Key.H].wasPressedThisFrame)
                UseBotiquinIfPresent();
        }
#else
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            HandleSlotSelection(0);
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            HandleSlotSelection(1);
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Alpha0))
            DeselectSlot();
        if (Input.GetKeyDown(KeyCode.G))
            DropSelectedItem();
        // [E] lo gestiona SOLO PlayerInteraction
        if (Input.GetKeyDown(KeyCode.H))
            UseBotiquinIfPresent();
#endif
    }

    /// <summary>
    /// Alterna la selección de una casilla. Si ya está seleccionada, la deselecciona (guarda el objeto).
    /// Si está vacía o se pulsa otra casilla, selecciona la nueva.
    /// </summary>
    public void HandleSlotSelection(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots) return;

        if (slots[slotIndex] == null)
        {
            if (selectedSlotIndex == slotIndex) DeselectSlot();
            return;
        }

        if (selectedSlotIndex == slotIndex)
        {
            DeselectSlot();
        }
        else
        {
            SelectSlot(slotIndex);
        }
    }

    /// <summary>
    /// Selecciona y equipa en mano el objeto de la casilla indicada.
    /// </summary>
    public void SelectSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots || slots[slotIndex] == null)
        {
            DeselectSlot();
            return;
        }

        selectedSlotIndex = slotIndex;
        equipTransition = 0f;
        UpdateHeldVisual();
        OnSlotSelected?.Invoke(selectedSlotIndex);
        ShowNotification($"🖐️ Sujetando: {slots[slotIndex].itemName} (Presiona [E] para usar)", new Color(0.25f, 0.95f, 1f, 1f), 2.2f);
    }

    /// <summary>
    /// Deselecciona el objeto en mano y lo guarda en el inventario.
    /// </summary>
    public void DeselectSlot()
    {
        if (selectedSlotIndex == -1) return;
        selectedSlotIndex = -1;
        UpdateHeldVisual();
        OnSlotSelected?.Invoke(-1);
    }

    /// <summary>
    /// Utiliza el objeto actualmente seleccionado en mano.
    /// </summary>
    public bool UseSelectedItem()
    {
        if (lastUseFrame == Time.frameCount) return false;
        lastUseFrame = Time.frameCount;

        if (!HasSelectedItem) return false;

        useRecoilTimer = 0.22f;

        SlotItem item = SelectedItem;
        int currentIndex = selectedSlotIndex;

        // 1. Botiquín: cura al jugador si está herido
        if (IsBotiquinSlot(item))
        {
            if (PlayerHealth.Instance != null && PlayerHealth.Instance.CurrentHealth < PlayerHealth.Instance.MaxHealth)
            {
                float healAmount = PlayerHealth.Instance.BotiquinHealAmount;
                PlayerHealth.Instance.Heal(healAmount);
                ShowNotification($"🩹 ¡Usaste el Botiquín! +{Mathf.RoundToInt(healAmount)} de Salud", new Color(0.2f, 1f, 0.45f, 1f), 3.5f);

                if (item.originalObject != null)
                {
                    Destroy(item.originalObject);
                }
                slots[currentIndex] = null;
                DeselectSlot();
                OnInventoryChanged?.Invoke();
                return true;
            }
            else
            {
                ShowNotification("ℹ️ Salud al máximo (100 HP)", new Color(0.6f, 0.85f, 1f, 1f), 2.2f);
                return false;
            }
        }
        // 2. Extintor: rocía chorro de polvo químico blanco y apaga el fuego si está en rango
        else if (IsExtinguisherSlot(item))
        {
            // El extintor ya NO apaga el fuego de golpe al pulsar E (se saltaba la barra de progreso).
            // Solo dispara el chorro; ExtinguisherSprayController llena la barra mientras se apunta a las llamas.
            ExtinguisherSprayController spray = ExtinguisherSprayController.Instance != null
                ? ExtinguisherSprayController.Instance
                : ExtinguisherSprayController.EnsureInstance();
            if (spray != null)
            {
                spray.TriggerSprayPulse(0.9f);
            }

            ShowNotification("🧯 ¡Rociando! Mantén [Clic Izq] o [E] apuntando a las llamas", new Color(0.2f, 1f, 0.85f, 1f), 1.5f);
            // El extintor permanece equipado para poder seguir rociando
            return true;
        }
        // 3. Lata de refresco: consume la lata y recupera +15 HP
        else if (IsLataSlot(item))
        {
            if (PlayerHealth.Instance != null)
            {
                PlayerHealth.Instance.Heal(curacionLata);
            }
            ShowNotification($"🥤 ¡Bebiste la lata de refresco! (+{Mathf.RoundToInt(curacionLata)} Salud)", new Color(0.2f, 1f, 0.55f, 1f), 3.5f);

            if (item.originalObject != null)
            {
                Destroy(item.originalObject);
            }
            slots[currentIndex] = null;
            DeselectSlot();
            OnInventoryChanged?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Suelta el objeto actualmente seleccionado de vuelta al mundo.
    /// </summary>
    public bool DropSelectedItem()
    {
        if (!HasSelectedItem) return false;
        int idx = selectedSlotIndex;
        DeselectSlot();
        return DropItem(idx);
    }

    /// <summary>
    /// Intenta agregar un item al inventario en la primera casilla vacía.
    /// Si las manos están libres, lo selecciona automáticamente para sujetarlo de inmediato.
    /// </summary>
    public bool AddItem(SlotItem item)
    {
        if (item == null) return false;

        int emptyIndex = -1;
        for (int i = 0; i < maxSlots; i++)
        {
            if (slots[i] == null)
            {
                emptyIndex = i;
                break;
            }
        }

        if (emptyIndex == -1)
        {
            ShowNotification("¡Inventario Lleno! (2/2)", new Color(1f, 0.35f, 0.35f, 1f));
            return false;
        }

        slots[emptyIndex] = item;
        ShowNotification($"[+] {item.itemName} añadido a Casilla {emptyIndex + 1}", new Color(0.2f, 1f, 0.65f, 1f));

        // Si tenemos las manos libres, equipamos automáticamente el nuevo objeto
        if (selectedSlotIndex == -1)
        {
            SelectSlot(emptyIndex);
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Suelta el item de la casilla indicada de vuelta al mundo frente a la cámara.
    /// </summary>
    public bool DropItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= maxSlots) return false;
        if (slots[slotIndex] == null) return false;

        if (selectedSlotIndex == slotIndex)
        {
            DeselectSlot();
        }

        SlotItem item = slots[slotIndex];
        slots[slotIndex] = null;

        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        Transform camTransform = cam != null ? cam.transform : transform;
        Vector3 forward = camTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = camTransform.forward;
        forward.Normalize();

        // No soltar el objeto dentro de una pared: acortar la distancia si hay un obstáculo delante
        Transform raizJugador = CameraMovement.Instance != null ? CameraMovement.Instance.transform : camTransform;
        float distancia = 1.2f;
        RaycastHit[] hits = Physics.RaycastAll(camTransform.position, forward, distancia + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(raizJugador)) continue;
            distancia = Mathf.Min(distancia, Mathf.Max(0.25f, h.distance - 0.3f));
        }

        Vector3 spawnPos = camTransform.position + forward * distancia + Vector3.down * 0.3f;
        Quaternion spawnRot = Quaternion.Euler(0f, camTransform.eulerAngles.y, 0f);

        if (item.originalObject != null)
        {
            item.originalObject.transform.SetPositionAndRotation(spawnPos, spawnRot);
            item.originalObject.SetActive(true);

            // Que no conserve velocidades de antes de recogerlo
            Rigidbody rbSoltado = item.originalObject.GetComponent<Rigidbody>();
            if (rbSoltado != null && !rbSoltado.isKinematic)
            {
                rbSoltado.linearVelocity = Vector3.zero;
                rbSoltado.angularVelocity = Vector3.zero;
            }
        }
        else if (item.prefabToDrop != null)
        {
            Instantiate(item.prefabToDrop, spawnPos, spawnRot);
        }

        ShowNotification($"[-] {item.itemName} soltado", new Color(1f, 0.85f, 0.25f, 1f));
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool HasExtinguisher()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            if (slots[i] != null && IsExtinguisherSlot(slots[i]))
            {
                return true;
            }
        }
        return false;
    }

    public bool IsExtinguisherSlot(SlotItem slot)
    {
        if (slot == null) return false;
        if (slot.tipoDefinido) return slot.tipo == InteractableItem.ItemType.Extintor;
        if (!string.IsNullOrEmpty(slot.itemName))
        {
            if (slot.itemName.IndexOf("extint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                slot.itemName.IndexOf("extinguis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        if (slot.originalObject != null)
        {
            string on = slot.originalObject.name;
            if (on.IndexOf("extint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                on.IndexOf("extinguis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            InteractableItem ii = slot.originalObject.GetComponent<InteractableItem>();
            if (ii != null && ii.Tipo == InteractableItem.ItemType.Extintor)
            {
                return true;
            }
        }
        return false;
    }

    public bool IsBotiquinSlot(SlotItem slot)
    {
        if (slot == null) return false;
        if (slot.tipoDefinido) return slot.tipo == InteractableItem.ItemType.Botiquin;
        if (!string.IsNullOrEmpty(slot.itemName))
        {
            if (slot.itemName.IndexOf("botiqu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                slot.itemName.IndexOf("firstaid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                slot.itemName.IndexOf("medkit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        if (slot.originalObject != null)
        {
            string on = slot.originalObject.name;
            if (on.IndexOf("botiqu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                on.IndexOf("medkit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            InteractableItem ii = slot.originalObject.GetComponent<InteractableItem>();
            if (ii != null && ii.Tipo == InteractableItem.ItemType.Botiquin)
            {
                return true;
            }
        }
        return false;
    }

    public bool IsLataSlot(SlotItem slot)
    {
        if (slot == null) return false;
        if (slot.tipoDefinido) return slot.tipo == InteractableItem.ItemType.Lata;
        if (!string.IsNullOrEmpty(slot.itemName) && slot.itemName.IndexOf("lata", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        if (slot.originalObject != null && slot.originalObject.name.IndexOf("lata", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        return false;
    }

    public bool HasBotiquin()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            if (slots[i] != null && IsBotiquinSlot(slots[i]))
            {
                return true;
            }
        }
        return false;
    }

    public bool UseBotiquinIfPresent()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            if (slots[i] != null && IsBotiquinSlot(slots[i]))
            {
                SelectSlot(i);
                return UseSelectedItem();
            }
        }
        return false;
    }

    public bool HasItem(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return false;

        if (itemName.IndexOf("extint", StringComparison.OrdinalIgnoreCase) >= 0 ||
            itemName.IndexOf("extinguis", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return HasExtinguisher();
        }

        for (int i = 0; i < maxSlots; i++)
        {
            if (slots[i] != null && slots[i].itemName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public void ShowNotification(string message, Color color, float duration = 2.5f)
    {
        notificationMessage = message;
        notificationColor = color;
        notificationTimer = duration;
        HUDVR.Mensaje(CanalHUD.Inventario, message, color, duration);
    }

    // =========================================================================
    // SISTEMA DEL MODELO 3D EN MANO (VIEWMODEL EN PRIMERA PERSONA)
    // =========================================================================

    private void EnsureHeldItemRoot()
    {
        if (heldItemRoot != null) return;

        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null) return;

        Transform existing = cam.transform.Find("_HeldItemRoot");
        if (existing != null)
        {
            heldItemRoot = existing;
        }
        else
        {
            GameObject rootGo = new GameObject("_HeldItemRoot");
            rootGo.transform.SetParent(cam.transform, false);
            heldItemRoot = rootGo.transform;
        }

        heldItemRoot.localPosition = heldItemOffset;
        heldItemRoot.localRotation = Quaternion.Euler(heldItemRotation);
        heldItemRoot.localScale = Vector3.one;
    }

    private void UpdateHeldVisual()
    {
        if (currentHeldVisual != null)
        {
            Destroy(currentHeldVisual);
            currentHeldVisual = null;
        }

        if (!HasSelectedItem) return;

        EnsureHeldItemRoot();
        if (heldItemRoot == null) return;

        SlotItem item = SelectedItem;
        GameObject fuente = item.originalObject != null ? item.originalObject : item.prefabToDrop;
        GameObject clone;

        if (fuente != null)
        {
            // Se clona dentro de un contenedor INACTIVO: así el clon nunca ejecuta Awake/OnEnable ni simula
            // su Rigidbody/Collider pegado a la cámara (antes vivía un frame con físicas y podía frenar al jugador).
            clone = Instantiate(fuente, ObtenerStagingInactivo());
            clone.SetActive(true); // sigue inactivo en jerarquía porque el padre está desactivado
            LimpiarComponentesFisicos(clone);
            clone.transform.SetParent(heldItemRoot, false);
        }
        else
        {
            clone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            clone.name = item.itemName;
            Collider cuboCol = clone.GetComponent<Collider>();
            if (cuboCol != null) DestroyImmediate(cuboCol);
            clone.transform.SetParent(heldItemRoot, false);
            clone.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        }

        clone.SetActive(true);
        currentHeldVisual = clone;

        // Medir y escalar con el objeto ya activo (los bounds de un objeto inactivo valen cero)
        NormalizeHeldVisual(clone, item);
    }

    private Transform ObtenerStagingInactivo()
    {
        if (stagingInactivo == null)
        {
            GameObject go = new GameObject("_HeldItemStaging");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            stagingInactivo = go.transform;
        }
        return stagingInactivo;
    }

    /// <summary>Quita (de forma inmediata) todo lo que pueda chocar o ejecutar lógica en el objeto sostenido.</summary>
    private static void LimpiarComponentesFisicos(GameObject clone)
    {
        foreach (Joint j in clone.GetComponentsInChildren<Joint>(true))
        {
            DestroyImmediate(j);
        }
        foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.GetType().Name.StartsWith("Mesh")) continue;
            DestroyImmediate(mb);
        }
        foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            DestroyImmediate(rb);
        }
        foreach (Collider c in clone.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(c);
        }
        foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void NormalizeHeldVisual(GameObject clone, SlotItem item)
    {
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;

        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        float targetSize = 0.22f; // ~22 cm en pantalla primera persona

        if (IsExtinguisherSlot(item))
        {
            targetSize = 0.28f;
        }
        else if (IsLataSlot(item))
        {
            targetSize = 0.14f;
        }

        if (maxDim > 0.001f)
        {
            float scaleFactor = targetSize / maxDim;
            clone.transform.localScale = clone.transform.localScale * scaleFactor;
        }

        // Recalcular centro para equilibrar el agarre
        Renderer[] scaledRenderers = clone.GetComponentsInChildren<Renderer>();
        if (scaledRenderers.Length > 0)
        {
            Bounds scaledB = scaledRenderers[0].bounds;
            for (int i = 1; i < scaledRenderers.Length; i++)
            {
                scaledB.Encapsulate(scaledRenderers[i].bounds);
            }

            Vector3 centerOffset = clone.transform.position - scaledB.center;
            centerOffset.y -= scaledB.extents.y * 0.35f;
            clone.transform.position += centerOffset;
        }

        // Ajustes de orientación estéticos según el tipo de objeto
        if (IsExtinguisherSlot(item))
        {
            clone.transform.localRotation = Quaternion.Euler(6f, 32f, -10f);
        }
        else if (IsBotiquinSlot(item))
        {
            clone.transform.localRotation = Quaternion.Euler(-12f, -22f, 16f);
        }
        else if (IsLataSlot(item))
        {
            clone.transform.localRotation = Quaternion.Euler(14f, -28f, 10f);
        }
    }

    private void UpdateHeldVisualMotion()
    {
        if (equipTransition < 1f)
        {
            equipTransition = Mathf.MoveTowards(equipTransition, 1f, Time.deltaTime * 5f);
        }

        if (useRecoilTimer > 0f)
        {
            useRecoilTimer -= Time.deltaTime;
            if (useRecoilTimer < 0f) useRecoilTimer = 0f;
        }

        if (heldItemRoot == null) return;

        // Animación suave de elevación al equipar
        float equipOffset = (1f - equipTransition) * -0.22f;

        // Animación de cabeceo/retroceso al usar
        float recoilNorm = useRecoilTimer > 0f ? Mathf.Sin((useRecoilTimer / 0.22f) * Mathf.PI) : 0f;
        float recoilY = recoilNorm * -0.045f;
        float recoilZ = recoilNorm * -0.03f;
        float recoilRotX = recoilNorm * 9f;

        // Oscilación sutil de respiración inactiva
        float idleY = Mathf.Sin(Time.time * 2.2f) * 0.003f;
        float idleX = Mathf.Cos(Time.time * 1.1f) * 0.002f;

        Vector3 targetPos = heldItemOffset + new Vector3(idleX, equipOffset + recoilY + idleY, recoilZ);
        Quaternion targetRot = Quaternion.Euler(heldItemRotation) * Quaternion.Euler(recoilRotX, 0f, 0f);

        heldItemRoot.localPosition = targetPos;
        heldItemRoot.localRotation = targetRot;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (heldItemRoot != null)
        {
            Destroy(heldItemRoot.gameObject);
        }
        if (stagingInactivo != null)
        {
            Destroy(stagingInactivo.gameObject);
        }
    }

    // =========================================================================
    // INTERFAZ GRÁFICA HUD (OnGUI)
    // =========================================================================

    private void OnGUI()
    {
        if (HUDVR.Activo) return; // en VR se usa el HUD VR (LateUpdate)
        if (!showHUD || (PlayerHealth.Instance != null && !PlayerHealth.Instance.IsAlive)) return;
        if (MovementSystemSelector.IsGameplayBlocked) return;

        EnsureStyles();

        // 1. Título superior del inventario
        float totalWidth = (slotSize * maxSlots) + (slotSpacing * (maxSlots - 1));
        Rect headerRect = new Rect(marginX, marginY, totalWidth, 18f);
        GUI.Label(headerRect, "INVENTARIO", headerTitleStyle);

        float currentY = marginY + 22f;

        // 2. Dibujar las dos casillas en la esquina superior izquierda
        for (int i = 0; i < maxSlots; i++)
        {
            float currentX = marginX + i * (slotSize + slotSpacing);
            Rect slotRect = new Rect(currentX, currentY, slotSize, slotSize);

            DrawSlot(i, slotRect);

            // Clic o toque sobre la casilla para equipar o guardar
            if (enableClickSelection && slots[i] != null)
            {
                if (GUI.Button(slotRect, GUIContent.none, GUIStyle.none))
                {
                    HandleSlotSelection(i);
                }
            }
        }

        // 3. Tarjeta de acción en la esquina inferior derecha cuando hay un objeto sujeto en mano
        if (HasSelectedItem)
        {
            DrawEquippedActionCard();
        }

        // 4. Notificación flotante de recogida / estado
        if (notificationTimer > 0f && !string.IsNullOrEmpty(notificationMessage))
        {
            float alpha = Mathf.Clamp01(notificationTimer * 1.5f);
            Color prevColor = GUI.color;
            GUI.color = new Color(notificationColor.r, notificationColor.g, notificationColor.b, alpha);

            Rect notifRect = new Rect(marginX, currentY + slotSize + 24f, 320f, 26f);
            GUI.Label(notifRect, notificationMessage, notificationStyle);

            GUI.color = prevColor;
        }
    }

    private void DrawSlot(int index, Rect rect)
    {
        bool isSelected = (index == selectedSlotIndex);
        bool hasItem = slots[index] != null;

        // Fondo translúcido de la casilla
        Color prevColor = GUI.color;
        GUI.color = isSelected ? new Color(0.12f, 0.18f, 0.28f, 0.95f) : new Color(0.08f, 0.11f, 0.16f, 0.88f);
        GUI.DrawTexture(rect, panelBackgroundTex);

        // Borde elegante: Amarillo/Oro brillante si está equipado en mano, Cyan si tiene objeto, Gris si está vacía
        if (isSelected)
        {
            float pulse = 0.85f + Mathf.PingPong(Time.time * 2f, 0.15f);
            GUI.color = new Color(1f, 0.85f, 0.2f, pulse);
            DrawRectBorder(rect, 3);
        }
        else if (hasItem)
        {
            GUI.color = new Color(0f, 0.85f, 1f, 0.8f);
            DrawRectBorder(rect, 2);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            DrawRectBorder(rect, 2);
        }

        GUI.color = prevColor;

        // Indicador de tecla numérico [1] o [2]
        Rect badgeRect = new Rect(rect.x + 4f, rect.y + 3f, 18f, 16f);
        GUI.Label(badgeRect, (index + 1).ToString(), slotNumberStyle);

        if (hasItem)
        {
            SlotItem item = slots[index];

            // Dibujar icono centrado dentro de la casilla
            if (item.icon != null)
            {
                float iconPadding = 12f;
                Rect iconRect = new Rect(rect.x + iconPadding, rect.y + iconPadding, rect.width - iconPadding * 2f, rect.height - iconPadding * 2f);
                GUI.DrawTexture(iconRect, item.icon, ScaleMode.ScaleToFit);
            }

            // Nombre del objeto debajo de la casilla
            Rect nameRect = new Rect(rect.x - 10f, rect.y + rect.height + 2f, rect.width + 20f, 16f);
            GUI.Label(nameRect, item.itemName, slotItemNameStyle);

            // Etiqueta de estado [EQUIPADO] o botón para equipar/guardar
            Rect actionBtnRect = new Rect(rect.x - 4f, rect.y + rect.height + 18f, rect.width + 8f, 22f);
            Color prevC = GUI.color;

            if (isSelected)
            {
                GUI.color = new Color(1f, 0.88f, 0.25f, 0.95f);
                if (GUI.Button(actionBtnRect, $"✋ EN MANO", slotActionButtonStyle))
                {
                    DeselectSlot();
                }
            }
            else
            {
                GUI.color = new Color(0.25f, 0.85f, 1f, 0.85f);
                if (GUI.Button(actionBtnRect, $"[{index + 1}] EQUIPAR", slotActionButtonStyle))
                {
                    SelectSlot(index);
                }
            }
            GUI.color = prevC;
        }
        else
        {
            // Texto cuando la casilla está vacía
            GUI.Label(rect, "Vacía", slotEmptyTextStyle);
        }
    }

    /// <summary>
    /// Dibuja una tarjeta HUD elegante en la esquina inferior derecha mostrando el objeto sujeto y su acción [E].
    /// </summary>
    private void DrawEquippedActionCard()
    {
        float cardWidth = 270f;
        float cardHeight = 92f;
        float rightMargin = 26f;
        float bottomMargin = 26f;

        Rect cardRect = new Rect(Screen.width - cardWidth - rightMargin, Screen.height - cardHeight - bottomMargin, cardWidth, cardHeight);

        // Fondo de tarjeta con efecto oscuro y borde cyan/oro
        Color prevC = GUI.color;
        GUI.color = new Color(0.06f, 0.09f, 0.14f, 0.92f);
        GUI.DrawTexture(cardRect, actionCardBgTex);

        GUI.color = new Color(0f, 0.88f, 1f, 0.85f);
        DrawRectBorder(cardRect, 2);
        GUI.color = prevC;

        // Título de objeto sostenido
        SlotItem item = SelectedItem;
        string itemHeader = $"✋ SUJETANDO: {item.itemName.ToUpper()}";
        Rect titleRect = new Rect(cardRect.x + 12f, cardRect.y + 8f, cardRect.width - 24f, 18f);
        GUI.Label(titleRect, itemHeader, actionCardTitleStyle);

        // Botón principal de acción [E]
        Rect btnRect = new Rect(cardRect.x + 12f, cardRect.y + 28f, cardRect.width - 24f, 32f);
        string btnText = "[E] USAR OBJETO";
        Color btnColor = new Color(0.2f, 0.9f, 0.5f, 0.95f);

        if (IsBotiquinSlot(item))
        {
            bool wounded = PlayerHealth.Instance != null && PlayerHealth.Instance.CurrentHealth < PlayerHealth.Instance.MaxHealth;
            btnText = wounded ? "🩹 [E] USAR BOTIQUÍN (CURAR)" : "✔️ [E] 100% SALUD (COMPLETA)";
            btnColor = wounded ? new Color(0.18f, 0.92f, 0.45f, 0.95f) : new Color(0.45f, 0.7f, 0.9f, 0.8f);
        }
        else if (IsExtinguisherSlot(item))
        {
            btnText = "🧯 [E] ROCIAR EXTINTOR";
            btnColor = new Color(0f, 0.88f, 1f, 0.95f);
        }
        else if (IsLataSlot(item))
        {
            btnText = "🥤 [E] BEBER LATA (+15 HP)";
            btnColor = new Color(0.2f, 0.95f, 0.6f, 0.95f);
        }

        prevC = GUI.color;
        GUI.color = btnColor;
        if (GUI.Button(btnRect, btnText, actionCardButtonStyle))
        {
            UseSelectedItem();
        }
        GUI.color = prevC;

        // Pista de atajos secundarios en la parte inferior de la tarjeta
        Rect hintRect = new Rect(cardRect.x + 12f, cardRect.y + 64f, cardRect.width - 24f, 18f);
        string hintText = $"[{selectedSlotIndex + 1}/Q] Guardar  •  [G] Soltar";
        GUI.Label(hintRect, hintText, actionCardHintStyle);
    }

    private void DrawRectBorder(Rect r, int thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), slotBorderTex);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), slotBorderTex);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), slotBorderTex);
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), slotBorderTex);
    }

    private void EnsureStyles()
    {
        if (panelBackgroundTex == null)
        {
            panelBackgroundTex = CreateColorTexture(Color.white);
            slotBorderTex = CreateColorTexture(Color.white);
            slotEmptyTex = CreateColorTexture(new Color(1f, 1f, 1f, 0.05f));
            badgeTex = CreateColorTexture(Color.white);
            actionCardBgTex = CreateColorTexture(Color.white);
            actionCardBorderTex = CreateColorTexture(Color.white);
        }

        if (headerTitleStyle == null)
        {
            headerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            headerTitleStyle.normal.textColor = new Color(0.7f, 0.85f, 1f, 0.75f);
        }

        if (slotNumberStyle == null)
        {
            slotNumberStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            slotNumberStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        }

        if (slotEmptyTextStyle == null)
        {
            slotEmptyTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter
            };
            slotEmptyTextStyle.normal.textColor = new Color(1f, 1f, 1f, 0.25f);
        }

        if (slotItemNameStyle == null)
        {
            slotItemNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            slotItemNameStyle.normal.textColor = Color.white;
        }

        if (dropHintStyle == null)
        {
            dropHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperCenter
            };
            dropHintStyle.normal.textColor = new Color(0.3f, 0.9f, 1f, 0.65f);
        }

        if (notificationStyle == null)
        {
            notificationStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
        }

        if (slotActionButtonStyle == null)
        {
            slotActionButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            slotActionButtonStyle.normal.textColor = Color.white;
        }

        if (actionCardTitleStyle == null)
        {
            actionCardTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            actionCardTitleStyle.normal.textColor = new Color(0.3f, 0.92f, 1f, 0.95f);
        }

        if (actionCardButtonStyle == null)
        {
            actionCardButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            actionCardButtonStyle.normal.textColor = Color.white;
        }

        if (actionCardHintStyle == null)
        {
            actionCardHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            actionCardHintStyle.normal.textColor = new Color(0.85f, 0.9f, 1f, 0.75f);
        }
    }

    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}

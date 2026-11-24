using UnityEngine;

/// <summary>
/// Convierte cualquier asset u objeto 3D en un elemento interactuable recogible para el inventario.
/// Tipos estandarizados:
///  - Curativos: Botiquín y Lata (se consumen al usarlos).
///  - Herramienta equipable: Extintor (se mantiene en la mano para rociar).
/// </summary>
public class InteractableItem : MonoBehaviour
{
    // IMPORTANTE: 'Lata' se añade al FINAL para no cambiar los valores ya guardados en las escenas (0, 1, 2).
    public enum ItemType
    {
        Botiquin = 0,
        Extintor = 1,
        Personalizado = 2,
        Lata = 3
    }

    [Header("Configuración del Item")]
    [Tooltip("Tipo de item: Botiquín / Lata (curativos) o Extintor (herramienta)")]
    [SerializeField] private ItemType tipoItem = ItemType.Botiquin;

    [Tooltip("Nombre del objeto que aparecerá en el inventario")]
    [SerializeField] private string nombreItem = "Botiquín";

    [Tooltip("Icono que se mostrará en la casilla del inventario")]
    [SerializeField] private Texture2D iconoInventario;

    [Tooltip("Distancia máxima en metros a la que el jugador puede interactuar")]
    [SerializeField] private float distanciaInteraccion = 3.5f;

    [Tooltip("Texto del botón o indicación de interacción")]
    [SerializeField] private string textoAccion = "Recoger";

    [Tooltip("Si es true, desactiva el objeto en la escena; si es false, lo destruye")]
    [SerializeField] private bool desactivarAlRecoger = true;

    [Tooltip("Prefab opcional para volver a instanciar si se destruyó")]
    [SerializeField] private GameObject prefabAlSoltar;

    [Header("Feedback Visual")]
    [Tooltip("Resalta ligeramente el objeto cuando el jugador lo está mirando")]
    [SerializeField] private bool resaltarAlMirar = true;

    [Tooltip("Multiplicador de brillo del color base al resaltar")]
    [Range(1f, 2.5f)]
    [SerializeField] private float intensidadResaltado = 1.45f;

    private static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int IdColor = Shader.PropertyToID("_Color");

    private bool estaSiendoMirado = false;
    private Renderer[] mallas;
    private MaterialPropertyBlock bloquePropiedades;

    public string NombreItem => nombreItem;
    public string TextoAccion => textoAccion;
    public float DistanciaInteraccion => distanciaInteraccion;
    public Texture2D Icono => iconoInventario;
    public ItemType Tipo => tipoItem;
    public bool EstaSiendoMirado => estaSiendoMirado;

    /// <summary>
    /// Mayor distancia de interacción configurada entre todos los objetos. PlayerInteraction alarga su rayo
    /// hasta aquí para que un objeto con más alcance (por ejemplo la lata de Compra) se pueda recoger de verdad.
    /// </summary>
    public static float DistanciaMaximaRegistrada { get; private set; } = 0f;

    private static void RegistrarDistancia(float distancia)
    {
        if (distancia > DistanciaMaximaRegistrada) DistanciaMaximaRegistrada = distancia;
    }

    /// <summary>Botiquín y Lata: se consumen y curan.</summary>
    public bool EsCurativo => tipoItem == ItemType.Botiquin || tipoItem == ItemType.Lata;

    /// <summary>Extintor: herramienta que se queda en la mano.</summary>
    public bool EsHerramientaEquipable => tipoItem == ItemType.Extintor;

    private void Awake()
    {
        AsegurarCollider();
        AutoDetectarTipo();
        CargarIconoPorDefecto();
        mallas = GetComponentsInChildren<Renderer>(true);
        RegistrarDistancia(distanciaInteraccion);
    }

    private void OnDisable()
    {
        // Al recogerlo (o desactivarlo) no debe quedarse resaltado
        SetHighlight(false);
    }

    /// <summary>Configura el objeto desde código (lo usa Compra para la lata). Sustituye a la antigua reflexión.</summary>
    public void Configurar(ItemType tipo, string nombre, string accion, float distancia, Texture2D icono)
    {
        tipoItem = tipo;
        if (!string.IsNullOrEmpty(nombre)) nombreItem = nombre;
        if (!string.IsNullOrEmpty(accion)) textoAccion = accion;
        distanciaInteraccion = Mathf.Max(0.5f, distancia);
        RegistrarDistancia(distanciaInteraccion);
        if (icono != null) iconoInventario = icono;
        CargarIconoPorDefecto();
    }

    private void AsegurarCollider()
    {
        // Necesario para que el raycast lo detecte (se busca también en los hijos, incluidos inactivos)
        if (GetComponentInChildren<Collider>(true) != null) return;

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            // Bounds en mundo -> espacio local (antes se ignoraba la escala y el collider quedaba gigante o diminuto)
            Bounds b = rend.bounds;
            Vector3 escala = transform.lossyScale;
            box.center = transform.InverseTransformPoint(b.center);
            box.size = new Vector3(
                b.size.x / Mathf.Max(0.0001f, Mathf.Abs(escala.x)),
                b.size.y / Mathf.Max(0.0001f, Mathf.Abs(escala.y)),
                b.size.z / Mathf.Max(0.0001f, Mathf.Abs(escala.z)));
        }
    }

    private void AutoDetectarTipo()
    {
        string objName = gameObject.name.ToLower();
        bool nombrePorDefecto = string.IsNullOrEmpty(nombreItem) || nombreItem == "Botiquín";

        if (objName.Contains("botiquin") || objName.Contains("firstaid") || objName.Contains("medkit"))
        {
            tipoItem = ItemType.Botiquin;
            if (nombrePorDefecto) nombreItem = "Botiquín";
        }
        else if (objName.Contains("extint") || objName.Contains("extinguis"))
        {
            tipoItem = ItemType.Extintor;
            if (nombrePorDefecto) nombreItem = "Extintor";
        }
        else if (objName.Contains("lata"))
        {
            tipoItem = ItemType.Lata;
            if (nombrePorDefecto) nombreItem = "Lata de refresco";
        }
    }

    private void CargarIconoPorDefecto()
    {
        if (iconoInventario != null) return;

#if UNITY_EDITOR
        string path = "";
        if (tipoItem == ItemType.Botiquin) path = "Assets/Materials/Icon_Botiquin.png";
        else if (tipoItem == ItemType.Extintor) path = "Assets/Materials/Icon_Extintor.png";

        if (!string.IsNullOrEmpty(path))
        {
            iconoInventario = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
#endif
    }

    /// <summary>
    /// Llamado cuando el jugador interactúa con el objeto (tecla E, toque o grip VR).
    /// </summary>
    public bool Interact(GameObject interactor)
    {
        InventorySystem inventario = InventorySystem.Instance;
        if (inventario == null) inventario = FindAnyObjectByType<InventorySystem>();
        if (inventario == null)
        {
            Debug.LogWarning("[InteractableItem] No se encontró InventorySystem en la escena.");
            return false;
        }

        SetHighlight(false);

        InventorySystem.SlotItem slotItem = new InventorySystem.SlotItem(
            nombreItem,
            iconoInventario,
            desactivarAlRecoger ? gameObject : null,
            prefabAlSoltar,
            tipoItem
        );

        if (!inventario.AddItem(slotItem))
        {
            return false;
        }

        // Pasar al inventario de forma limpia: sin velocidades residuales y sin colliders activos en la escena
        DetenerFisicas();

        if (desactivarAlRecoger)
        {
            gameObject.SetActive(false);
        }
        else
        {
            Destroy(gameObject);
        }
        return true;
    }

    private void DetenerFisicas()
    {
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            if (rb.isKinematic) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Resalta el objeto con un MaterialPropertyBlock (no crea copias de materiales).</summary>
    public void SetHighlight(bool highlight)
    {
        if (estaSiendoMirado == highlight) return;
        estaSiendoMirado = highlight;

        if (!resaltarAlMirar || mallas == null) return;

        if (bloquePropiedades == null) bloquePropiedades = new MaterialPropertyBlock();

        foreach (Renderer r in mallas)
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                bloquePropiedades.Clear();
                Material m = mats[i];
                if (highlight && m != null)
                {
                    if (m.HasProperty(IdBaseColor))
                    {
                        Color c = m.GetColor(IdBaseColor);
                        bloquePropiedades.SetColor(IdBaseColor, new Color(c.r * intensidadResaltado, c.g * intensidadResaltado, c.b * intensidadResaltado, c.a));
                    }
                    else if (m.HasProperty(IdColor))
                    {
                        Color c = m.GetColor(IdColor);
                        bloquePropiedades.SetColor(IdColor, new Color(c.r * intensidadResaltado, c.g * intensidadResaltado, c.b * intensidadResaltado, c.a));
                    }
                }
                r.SetPropertyBlock(bloquePropiedades, i);
            }
        }
    }
}

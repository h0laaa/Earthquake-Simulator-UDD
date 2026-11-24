using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reacción de pánico de Girl_low_poly durante los temblores (CameraShake):
/// corre con los brazos en alto y/o se agacha cubriéndose la cabeza con las manos.
/// Animación 100 % procedimental sobre el esqueleto (Mixamo / Humanoid / Blender).
///
/// ERRORES CORREGIDOS respecto a la versión anterior
/// -------------------------------------------------
///  1. Miembros "invertidos" / mal orientados: la rotación de cada hueso se calculaba usando la rotación ACTUAL del
///     padre, así que al inclinar la columna o la cadera todos los hijos (cabeza, brazos, piernas) heredaban esa
///     inclinación DOS veces. Ahora la dirección objetivo es absoluta (respecto al cuerpo) y se resuelve contra la
///     pose de reposo guardada, sin importar cómo estén rotados los padres.
///  2. Se quedaba congelada en la pose de pánico al terminar el temblor: durante el fundido de salida ningún bloque
///     de pose se ejecutaba y RestaurarPoseReposoSuave nunca llegaba a correr. Ahora la pose se desvanece de forma
///     continua y al final se restaura exactamente la pose de reposo.
///  3. Flotaba / se hundía al agacharse: la cadera bajaba una cantidad fija en el eje local del padre (que en
///     modelos de Blender no es el eje vertical ni tiene la misma escala). Ahora se mide la longitud real de las
///     piernas, se calculan los ángulos de la sentadilla y se coloca la cadera para que los pies pisen el suelo.
///     Los pies se mantienen planos.
///  4. Cabeza al revés al agacharse: el objetivo de la cabeza apuntaba hacia ABAJO (por debajo de la horizontal).
///  5. Manos que no llegaban a la cabeza: ahora se resuelve un IK de 2 huesos que lleva las muñecas a la cabeza.
///  6. Orientación: el sentido "adelante" se detecta con la línea de hombros/caderas (se conserva el interruptor
///     corregirRotacion180 como respaldo manual).
///  7. Movimiento "de cangrejo": se movía en la dirección nueva mientras el cuerpo aún giraba. Ahora gira con
///     velocidad limitada y avanza hacia donde mira, con evasión de obstáculos estable y sin chocar con su
///     propio collider.
///  8. Salto brusco al pasar de correr a agacharse (la pose cambiaba de golpe): ahora se mezclan suavemente.
///  9. AutoDetectarHuesos: la mano derecha se asignaba al dedo anular izquierdo ("handr" dentro de "lefthandring"),
///     se desactivaba cualquier objeto cuyo nombre contuviera "light", y el Animator se desactivaba ANTES de leer
///     los huesos humanoides.
/// 10. OnDisable iniciaba una corrutina (error al desactivar el objeto) y el fundido de audio podía cortar la
///     respiración de un pánico nuevo.
/// 11. "Iniciar Pánico (Probar)" del menú contextual se cancelaba al siguiente frame.
/// 12. SetBool/SetFloat sobre parámetros inexistentes del Animator llenaban la consola de avisos.
/// 13. AlinearCentroDelPersonaje movía el Armature en el espacio equivocado si no era hijo directo de la raíz.
/// 14. Con un Animator con controlador activo, la pose de reposo guardada ya no coincidía con la animada.
///
/// Requiere la clase CameraShake del proyecto con: static CameraShake Instance, bool IsShaking,
/// static event OnShakeStarted y static event OnShakeEnded (ambos sin parámetros).
/// Respeta 100 % la escala del transform asignada por el usuario.
/// </summary>
[DisallowMultipleComponent]
public class GirlPanicReaction : MonoBehaviour
{
    public enum ModoPanico
    {
        [Tooltip("Corre hacia adelante agitando los brazos en alto")]
        CorrerConBrazosEnAlto,

        [Tooltip("Se agacha protegiéndose la cabeza con las manos mientras tiembla de miedo")]
        AgacharseConManosEnCabeza,

        [Tooltip("Corre despavorida unos segundos y luego se agacha a cubrirse la cabeza")]
        HuirYAgacharse,

        [Tooltip("Alterna continuamente entre correr despavorida y agacharse")]
        AlternarCiclo,

        [Tooltip("Elige aleatoriamente entre correr o agacharse cada vez que comienza un temblor")]
        Aleatorio
    }

    public enum EstadoPanico
    {
        Calma,
        CorriendoAsustada,
        AgachadaConMiedo,
        VolviendoACalma
    }

    // =====================================================================================================
    //  INSPECTOR
    // =====================================================================================================

    [Header("--- MODO DE COMPORTAMIENTO ---")]
    [Tooltip("Estilo de reacción cuando ocurre el temblor")]
    [SerializeField] private ModoPanico modoPanico = ModoPanico.HuirYAgacharse;

    [Tooltip("Tiempo en segundos que corre antes de agacharse (para el modo HuirYAgacharse)")]
    [Range(1f, 10f)]
    [SerializeField] private float tiempoHuyendoAntesDeAgacharse = 3.5f;

    [Tooltip("Intervalo de alternancia entre correr y agacharse (para el modo AlternarCiclo)")]
    [Range(2f, 10f)]
    [SerializeField] private float intervaloAlternancia = 4.0f;

    [Tooltip("Simular temblor para probar la animación directamente en el Inspector")]
    [SerializeField] private bool simularTemblorEnEditor = false;

    [Header("--- ORIENTACIÓN DEL MODELO ---")]
    [Tooltip("Detecta hacia dónde mira el modelo leyendo la posición de hombros/caderas del esqueleto (recomendado)")]
    [SerializeField] private bool detectarOrientacionAutomatica = true;

    [Tooltip("Solo se usa si la detección automática está desactivada o falla: actívalo si el modelo 3D fue exportado de espaldas (180°)")]
    [SerializeField] private bool corregirRotacion180 = true;

    [Tooltip("Recentra automáticamente el Armature si la cadera venía con desfase horizontal de Blender")]
    [SerializeField] private bool autoRecentrarDesfaseBlender = true;

    [Tooltip("Desactiva las cámaras y luces que vengan embebidas dentro del modelo importado (nunca toca la MainCamera)")]
    [SerializeField] private bool desactivarCamarasYLucesDelModelo = true;

    [Header("--- HUESOS DEL RIG (MIXAMO / HUMANOIDE) ---")]
    [Tooltip("Hueso de la pelvis/cadera (mixamorig:Hips)")]
    [SerializeField] private Transform hips;

    [Tooltip("Hueso de la columna vertebral (mixamorig:Spine)")]
    [SerializeField] private Transform spine;

    [Tooltip("Hueso del pecho/tórax superior (mixamorig:Spine1 o Spine2)")]
    [SerializeField] private Transform chest;

    [Tooltip("Hueso de la cabeza (mixamorig:Head)")]
    [SerializeField] private Transform head;

    [Header("Extremidades Superiores (Brazos)")]
    [Tooltip("Brazo superior izquierdo (mixamorig:LeftArm)")]
    [SerializeField] private Transform leftUpperArm;

    [Tooltip("Antebrazo izquierdo / codo (mixamorig:LeftForeArm)")]
    [SerializeField] private Transform leftForeArm;

    [Tooltip("Mano izquierda (mixamorig:LeftHand)")]
    [SerializeField] private Transform leftHand;

    [Tooltip("Brazo superior derecho (mixamorig:RightArm)")]
    [SerializeField] private Transform rightUpperArm;

    [Tooltip("Antebrazo derecho / codo (mixamorig:RightForeArm)")]
    [SerializeField] private Transform rightForeArm;

    [Tooltip("Mano derecha (mixamorig:RightHand)")]
    [SerializeField] private Transform rightHand;

    [Header("Extremidades Inferiores (Piernas)")]
    [Tooltip("Muslo izquierdo (mixamorig:LeftUpLeg)")]
    [SerializeField] private Transform leftUpperLeg;

    [Tooltip("Pantorrilla/rodilla izquierda (mixamorig:LeftLeg)")]
    [SerializeField] private Transform leftLowerLeg;

    [Tooltip("Pie izquierdo (mixamorig:LeftFoot)")]
    [SerializeField] private Transform leftFoot;

    [Tooltip("Muslo derecho (mixamorig:RightUpLeg)")]
    [SerializeField] private Transform rightUpperLeg;

    [Tooltip("Pantorrilla/rodilla derecha (mixamorig:RightLeg)")]
    [SerializeField] private Transform rightLowerLeg;

    [Tooltip("Pie derecho (mixamorig:RightFoot)")]
    [SerializeField] private Transform rightFoot;

    [Header("--- LOCOMOCIÓN Y NAVEGACIÓN ---")]
    [Tooltip("Velocidad de carrera durante el pánico (m/s)")]
    [Range(1.5f, 8f)]
    [SerializeField] private float velocidadCarrera = 3.8f;

    [Tooltip("Velocidad máxima de giro al cambiar de dirección (grados por segundo)")]
    [Range(120f, 900f)]
    [SerializeField] private float velocidadGiro = 480f;

    [Tooltip("Radio máximo en metros desde el punto inicial donde puede correr despavorida")]
    [Range(3f, 30f)]
    [SerializeField] private float radioMaximoPanico = 9f;

    [Tooltip("Distancia frontal a la que detecta paredes y obstáculos para esquivarlos")]
    [Range(0.5f, 4f)]
    [SerializeField] private float distanciaDeteccionObstaculo = 1.8f;

    [Tooltip("Capa de colisión para detectar suelo y paredes (ignora los colliders del propio personaje)")]
    [SerializeField] private LayerMask capasObstaculos = ~0;

    [Tooltip("Ajuste fino de la altura del personaje sobre el suelo en metros (solo si NO usa CharacterController). " +
             "Positivo = más arriba. El pivote del modelo debe estar en los pies.")]
    [Range(-0.5f, 0.5f)]
    [SerializeField] private float ajusteAlturaSuelo = 0f;

    [Tooltip("Frecuencia o cadencia de los pasos al correr")]
    [Range(6f, 20f)]
    [SerializeField] private float cadenciaPaso = 14f;

    [Tooltip("Amplitud del balanceo de las piernas al correr (grados)")]
    [Range(10f, 55f)]
    [SerializeField] private float amplitudZancada = 32f;

    [Tooltip("Rebote vertical de la cadera al correr (metros para un personaje de 1.7 m; se escala solo)")]
    [Range(0.01f, 0.2f)]
    [SerializeField] private float rebotePaso = 0.05f;

    [Tooltip("Inclinación hacia adelante del cuerpo al correr (grados)")]
    [Range(0f, 30f)]
    [SerializeField] private float inclinacionCorrer = 15f;

    [Header("--- ANIMACIÓN PROCEDIMENTAL: AGACHADO Y BRAZOS ---")]
    [Tooltip("Profundidad de la sentadilla: fracción de la longitud de la pierna que baja la cadera (0.42 = sentadilla media)")]
    [Range(0.1f, 0.7f)]
    [SerializeField] private float descensoAgacharse = 0.42f;

    [Tooltip("Cuánto se encorva el torso hacia adelante al agacharse (grados)")]
    [Range(20f, 80f)]
    [SerializeField] private float inclinacionTorsoAgachado = 50f;

    [Tooltip("Velocidad de agitación de los brazos en alto al correr")]
    [Range(6f, 30f)]
    [SerializeField] private float velocidadAgitacionBrazos = 16f;

    [Tooltip("Amplitud de movimiento caótico de los brazos en alto (grados)")]
    [Range(10f, 50f)]
    [SerializeField] private float amplitudAgitacionBrazos = 28f;

    [Header("--- SENSACIÓN DE MIEDO Y TERROR (ESCALOFRÍOS) ---")]
    [Tooltip("Intensidad del temblor / vibración de miedo corporal (metros para un personaje de 1.7 m; se escala solo)")]
    [Range(0.005f, 0.06f)]
    [SerializeField] private float temblorMiedoIntensidad = 0.02f;

    [Tooltip("Frecuencia rápida del escalofrío de pánico")]
    [Range(15f, 45f)]
    [SerializeField] private float frecuenciaTemblorMiedo = 32f;

    [Header("--- AUDIO OPCIONAL ---")]
    [Tooltip("Sonido de quejido o sobresalto asustado al comenzar el temblor")]
    [SerializeField] private AudioClip sonidoGrito;

    [Tooltip("Sonido de respiración agitada de miedo en bucle")]
    [SerializeField] private AudioClip sonidoRespiracionMiedo;

    [Range(0f, 1f)]
    [SerializeField] private float volumenAudio = 0.85f;

    // =====================================================================================================
    //  DATOS INTERNOS
    // =====================================================================================================

    private const float AlturaReferencia = 1.7f;          // altura (m) para la que están pensados los valores en metros
    private const float RelacionPantorrilla = 0.8f;       // ángulo de la pantorrilla = 0.8 * ángulo del muslo al agacharse
    private const float FlexionRodillaCorrer = 80f;       // flexión máxima de rodilla al correr (grados)
    private const float AceleracionCarrera = 25f;         // m/s² para arrancar / frenar

    /// <summary>Hueso con su pose de reposo y la dirección de su segmento, todo expresado respecto a la raíz.</summary>
    private class Hueso
    {
        public Transform t;
        public Quaternion restLocal = Quaternion.identity;     // localRotation en reposo
        public Quaternion restRelRaiz = Quaternion.identity;   // rotación en reposo relativa a la raíz del personaje
        public Vector3 dirRest = Vector3.up;                   // dirección del segmento (hueso -> hijo) en reposo, espacio raíz
        public Vector3 ejeLocal = Vector3.up;                  // esa misma dirección expresada en el espacio local del hueso
    }

    private Hueso hHips, hSpine, hChest, hHead;
    private Hueso hUAL, hFAL, hUAR, hFAR;          // brazo superior / antebrazo izquierdo y derecho
    private Hueso hULL, hLLL, hULR, hLLR;          // muslo / pantorrilla izquierda y derecha
    private Hueso hFootL, hFootR;

    // Ejes del cuerpo expresados en el espacio local de la raíz
    private Vector3 AxisR = Vector3.right;
    private Vector3 AxisF = Vector3.forward;
    private readonly Vector3 AxisU = Vector3.up;
    private float yawFrenteModelo = 0f;            // yaw (grados) hacia el que mira el modelo respecto al +Z de la raíz

    // Geometría medida en reposo (unidades locales de la raíz)
    private float alturaLocal = AlturaReferencia;
    private float alturaMundo = AlturaReferencia;
    private float escalaRef = 1f;                  // alturaLocal / 1.7 : convierte "metros" a unidades locales
    private float largoSupL, largoAnteL, largoSupR, largoAnteR;
    private float largoMuslo, largoPantorrilla;
    private Vector3 hipsRestPosRaiz;
    private bool hayPies;
    private float pieRestYMin;
    private Vector3 centroPiesRest;
    private float anguloMusloAgachado, anguloPantorrillaAgachado;
    private float descensoAnalitico, retrasoAnalitico;

    // Estado
    private EstadoPanico estadoActual = EstadoPanico.Calma;
    private ModoPanico modoActivoEnEsteSismo;
    private bool estaEnPanico = false;
    private bool panicoManual = false;
    private bool poseAplicada = false;
    private float tiempoInicioPanico = 0f;
    private float pesoTransicion = 0f;             // 0 = reposo, 1 = pose de pánico completa
    private float mezclaAgachado = 0f;             // 0 = pose de correr, 1 = pose agachada

    // Navegación
    private Vector3 posicionInicialGlobal;
    private float anguloVisual = 0f;               // rumbo real hacia el que mira el cuerpo (yaw mundo)
    private float anguloObjetivo = 0f;             // rumbo al que quiere girar
    private float temporizadorCambioDireccion = 0f;
    private float cooldownEvasion = 0f;
    private float tiempoSinObstaculos = 10f;
    private float signoEvasion = 1f;
    private float velocidadActual = 0f;
    private float velocidadVertical = 0f;

    private CharacterController characterController;
    private Animator animator;
    private Dictionary<int, AnimatorControllerParameterType> parametrosAnimator;
    private AudioSource audioSourceVoces;
    private AudioSource audioSourceRespiracion;
    private Coroutine corrutinaFade;

    private static readonly RaycastHit[] bufferRayos = new RaycastHit[16];
    private static readonly int HashIsShaking = Animator.StringToHash("IsShaking");
    private static readonly int HashIsPanicking = Animator.StringToHash("IsPanicking");
    private static readonly int HashIsRunning = Animator.StringToHash("IsRunning");
    private static readonly int HashIsCrouching = Animator.StringToHash("IsCrouching");
    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashFearWeight = Animator.StringToHash("FearWeight");

    public EstadoPanico Estado => estadoActual;
    public bool EstaEnPanico => estaEnPanico;
    public bool CorregirRotacion180 { get => corregirRotacion180; set => corregirRotacion180 = value; }

    private bool UsaAnimator => animator != null && animator.enabled && animator.runtimeAnimatorController != null;

    // =====================================================================================================
    //  CICLO DE VIDA
    // =====================================================================================================

    private void Awake()
    {
        posicionInicialGlobal = transform.position;
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // Primero se leen los huesos (el Avatar humanoide necesita el Animator activo) y DESPUÉS se desactiva.
        AutoDetectarHuesos();

        if (animator != null && animator.runtimeAnimatorController == null)
        {
            animator.enabled = false;
        }

        CalcularEscalaYOrientacion();

        if (autoRecentrarDesfaseBlender)
        {
            AlinearCentroDelPersonaje();
        }

        CachearPosesOriginales();
        InicializarFuentesAudio();
    }

    private void OnEnable()
    {
        CameraShake.OnShakeStarted += ManejarInicioTemblor;
        CameraShake.OnShakeEnded += ManejarFinTemblor;
    }

    private void OnDisable()
    {
        CameraShake.OnShakeStarted -= ManejarInicioTemblor;
        CameraShake.OnShakeEnded -= ManejarFinTemblor;

        // Sin corrutinas aquí: al desactivar el GameObject StartCoroutine produce un error.
        estaEnPanico = false;
        panicoManual = false;
        estadoActual = EstadoPanico.Calma;
        pesoTransicion = 0f;
        velocidadActual = 0f;
        DetenerAudioInmediato();

        if (poseAplicada && Application.isPlaying)
        {
            AplicarPose(0f);
            poseAplicada = false;
        }
    }

    private void Start()
    {
        anguloVisual = transform.eulerAngles.y + yawFrenteModelo;
        anguloObjetivo = anguloVisual;
        CachearParametrosAnimator();
        MoverPersonaje(Vector3.zero, Time.deltaTime);
    }

    private void Update()
    {
        bool sismoActivo = (CameraShake.Instance != null && CameraShake.Instance.IsShaking)
                           || simularTemblorEnEditor
                           || panicoManual;

        if (sismoActivo && !estaEnPanico)
        {
            ComenzarPanico();
        }
        else if (!sismoActivo && estaEnPanico)
        {
            TerminarPanico();
        }

        float dt = Time.deltaTime;

        if (estaEnPanico)
        {
            pesoTransicion = Mathf.MoveTowards(pesoTransicion, 1f, dt * 3.5f);
            ActualizarComportamientoPanico();

            float objetivoMezcla = (estadoActual == EstadoPanico.AgachadaConMiedo) ? 1f : 0f;
            mezclaAgachado = Mathf.MoveTowards(mezclaAgachado, objetivoMezcla, dt * 3f);
        }
        else
        {
            pesoTransicion = Mathf.MoveTowards(pesoTransicion, 0f, dt * 2.2f);
            if (estadoActual == EstadoPanico.VolviendoACalma && pesoTransicion <= 0f)
            {
                estadoActual = EstadoPanico.Calma;
            }
        }

        ActualizarLocomocion(dt);
        ActualizarParametrosAnimator();
    }

    private void LateUpdate()
    {
        if (pesoTransicion > 0f)
        {
            AplicarPose(pesoTransicion);
            poseAplicada = true;
        }
        else if (poseAplicada)
        {
            // El peso llegó a cero: deja el esqueleto exactamente en su pose de reposo (una sola vez).
            AplicarPose(0f);
            poseAplicada = false;
        }
    }

    // =====================================================================================================
    //  CONTROL DEL PÁNICO
    // =====================================================================================================

    private void ManejarInicioTemblor() { ComenzarPanico(); }
    private void ManejarFinTemblor() { TerminarPanico(); }

    /// <summary>Inicia el pánico manualmente (se mantiene hasta llamar a DetenerPanico).</summary>
    [ContextMenu("Iniciar Pánico (Probar)")]
    public void IniciarPanico()
    {
        panicoManual = true;
        ComenzarPanico();
    }

    /// <summary>Detiene el pánico manual. Si el CameraShake sigue temblando, volverá a activarse.</summary>
    [ContextMenu("Detener Pánico")]
    public void DetenerPanico()
    {
        panicoManual = false;
        TerminarPanico();
    }

    private void ComenzarPanico()
    {
        if (estaEnPanico) return;

        estaEnPanico = true;
        tiempoInicioPanico = Time.time;

        anguloVisual = transform.eulerAngles.y + yawFrenteModelo;
        anguloObjetivo = anguloVisual + Random.Range(-30f, 30f);
        temporizadorCambioDireccion = Random.Range(1.0f, 2.2f);

        if (modoPanico == ModoPanico.Aleatorio)
        {
            modoActivoEnEsteSismo = (Random.value > 0.5f) ? ModoPanico.CorrerConBrazosEnAlto : ModoPanico.AgacharseConManosEnCabeza;
        }
        else
        {
            modoActivoEnEsteSismo = modoPanico;
        }

        estadoActual = (modoActivoEnEsteSismo == ModoPanico.AgacharseConManosEnCabeza)
            ? EstadoPanico.AgachadaConMiedo
            : EstadoPanico.CorriendoAsustada;

        // Si arranca desde reposo, empieza ya en la pose correcta (sin pasar por la otra).
        if (pesoTransicion < 0.05f)
        {
            mezclaAgachado = (estadoActual == EstadoPanico.AgachadaConMiedo) ? 1f : 0f;
        }

        if (sonidoGrito != null && audioSourceVoces != null)
        {
            audioSourceVoces.pitch = Random.Range(1.05f, 1.25f);
            audioSourceVoces.PlayOneShot(sonidoGrito, volumenAudio);
        }

        if (audioSourceRespiracion != null)
        {
            if (corrutinaFade != null)
            {
                StopCoroutine(corrutinaFade);
                corrutinaFade = null;
            }
            audioSourceRespiracion.volume = volumenAudio * 0.6f;
            if (!audioSourceRespiracion.isPlaying) audioSourceRespiracion.Play();
        }
    }

    private void TerminarPanico()
    {
        if (!estaEnPanico) return;

        estaEnPanico = false;
        estadoActual = EstadoPanico.VolviendoACalma;

        if (audioSourceRespiracion != null && audioSourceRespiracion.isPlaying)
        {
            if (isActiveAndEnabled)
                corrutinaFade = StartCoroutine(DesvanecerAudio(audioSourceRespiracion, 1.5f));
            else
                audioSourceRespiracion.Stop();
        }
    }

    private void ActualizarComportamientoPanico()
    {
        float tiempoTranscurrido = Time.time - tiempoInicioPanico;

        switch (modoActivoEnEsteSismo)
        {
            case ModoPanico.HuirYAgacharse:
                if (tiempoTranscurrido >= tiempoHuyendoAntesDeAgacharse && estadoActual == EstadoPanico.CorriendoAsustada)
                {
                    estadoActual = EstadoPanico.AgachadaConMiedo;
                }
                break;

            case ModoPanico.AlternarCiclo:
                int ciclo = Mathf.FloorToInt(tiempoTranscurrido / intervaloAlternancia);
                estadoActual = (ciclo % 2 == 0) ? EstadoPanico.CorriendoAsustada : EstadoPanico.AgachadaConMiedo;
                break;
        }
    }

    // =====================================================================================================
    //  LOCOMOCIÓN, GRAVEDAD Y NAVEGACIÓN
    // =====================================================================================================

    private void ActualizarLocomocion(float dt)
    {
        bool corriendo = estaEnPanico && estadoActual == EstadoPanico.CorriendoAsustada;

        // Al agacharse frena de forma progresiva, al correr acelera.
        float velObjetivo = corriendo ? velocidadCarrera * (1f - mezclaAgachado) : 0f;
        velocidadActual = Mathf.MoveTowards(velocidadActual, velObjetivo, AceleracionCarrera * dt);

        if (corriendo)
        {
            ActualizarRumbo(dt);
        }

        if (velocidadActual > 0.01f)
        {
            // El frente visual del modelo (yawFrenteModelo) debe apuntar al rumbo.
            transform.rotation = Quaternion.Euler(0f, anguloVisual - yawFrenteModelo, 0f);
        }

        MoverPersonaje(DireccionDeAngulo(anguloVisual) * velocidadActual, dt);
    }

    private void ActualizarRumbo(float dt)
    {
        temporizadorCambioDireccion -= dt;
        cooldownEvasion -= dt;

        if (temporizadorCambioDireccion <= 0f)
        {
            anguloObjetivo += Random.Range(-30f, 30f);
            temporizadorCambioDireccion = Random.Range(1.0f, 2.2f);
        }

        Vector3 origen = transform.position + Vector3.up * (alturaMundo * 0.45f);
        bool frente = RaycastPropio(origen, DireccionDeAngulo(anguloVisual), distanciaDeteccionObstaculo, out _);
        bool izq = RaycastPropio(origen, DireccionDeAngulo(anguloVisual - 35f), distanciaDeteccionObstaculo * 0.85f, out _);
        bool der = RaycastPropio(origen, DireccionDeAngulo(anguloVisual + 35f), distanciaDeteccionObstaculo * 0.85f, out _);

        if (frente || izq || der)
        {
            // Al empezar un episodio de obstáculos se elige un lado y se mantiene (evita el temblor de izquierda a derecha).
            if (tiempoSinObstaculos > 0.6f) signoEvasion = (Random.value > 0.5f) ? 1f : -1f;
            tiempoSinObstaculos = 0f;
        }
        else
        {
            tiempoSinObstaculos += dt;
        }

        if (frente)
        {
            if (cooldownEvasion <= 0f)
            {
                float giro;
                if (izq && !der) giro = 80f;
                else if (der && !izq) giro = -80f;
                else giro = signoEvasion * 110f;

                anguloObjetivo = anguloVisual + giro;
                cooldownEvasion = 0.4f;
                temporizadorCambioDireccion = 1.0f;
            }
        }
        else if (izq)
        {
            anguloObjetivo = anguloVisual + 45f;
        }
        else if (der)
        {
            anguloObjetivo = anguloVisual - 45f;
        }

        // Si se aleja demasiado del punto inicial, vuelve hacia el centro.
        Vector3 offsetDesdeInicio = transform.position - posicionInicialGlobal;
        offsetDesdeInicio.y = 0f;
        if (offsetDesdeInicio.magnitude > radioMaximoPanico)
        {
            float anguloHaciaCentro = Mathf.Atan2(-offsetDesdeInicio.x, -offsetDesdeInicio.z) * Mathf.Rad2Deg;
            anguloObjetivo = Mathf.MoveTowardsAngle(anguloObjetivo, anguloHaciaCentro, dt * 120f);
        }

        // Giro con velocidad limitada: el cuerpo siempre avanza hacia donde mira.
        anguloVisual = Mathf.MoveTowardsAngle(anguloVisual, anguloObjetivo, velocidadGiro * dt);
    }

    /// <summary>
    /// Mueve al personaje y lo mantiene pegado al piso (con o sin CharacterController).
    /// La gravedad es acumulativa y no depende de la tasa de frames.
    /// </summary>
    private void MoverPersonaje(Vector3 velocidadHorizontal, float dt)
    {
        if (characterController != null && characterController.enabled)
        {
            if (characterController.isGrounded && velocidadVertical < 0f)
                velocidadVertical = -2f;
            else
                velocidadVertical += Physics.gravity.y * dt;

            Vector3 velocidad = velocidadHorizontal;
            velocidad.y = velocidadVertical;
            characterController.Move(velocidad * dt);
        }
        else
        {
            transform.position += velocidadHorizontal * dt;
            PegarAlSueloPorRayo(dt);
        }
    }

    private void PegarAlSueloPorRayo(float dt)
    {
        Vector3 origen = transform.position + Vector3.up * (alturaMundo * 0.6f);
        if (RaycastPropio(origen, Vector3.down, Mathf.Max(5f, alturaMundo * 3f), out RaycastHit hit))
        {
            Vector3 pos = transform.position;
            pos.y = Mathf.MoveTowards(pos.y, hit.point.y + ajusteAlturaSuelo, dt * 15f);
            transform.position = pos;
        }
    }

    /// <summary>Raycast que ignora los colliders del propio personaje y devuelve el impacto más cercano.</summary>
    private bool RaycastPropio(Vector3 origen, Vector3 direccion, float distancia, out RaycastHit mejor)
    {
        mejor = default(RaycastHit);
        int cantidad = Physics.RaycastNonAlloc(origen, direccion, bufferRayos, distancia, capasObstaculos, QueryTriggerInteraction.Ignore);

        bool encontrado = false;
        float minimo = float.MaxValue;
        for (int i = 0; i < cantidad; i++)
        {
            RaycastHit h = bufferRayos[i];
            if (h.collider == null) continue;

            Transform tc = h.collider.transform;
            if (tc == transform || tc.IsChildOf(transform)) continue;

            if (h.distance < minimo)
            {
                minimo = h.distance;
                mejor = h;
                encontrado = true;
            }
        }
        return encontrado;
    }

    private static Vector3 DireccionDeAngulo(float grados)
    {
        float r = grados * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    // =====================================================================================================
    //  ANIMACIÓN PROCEDIMENTAL
    // =====================================================================================================

    private static bool Ok(Hueso h) { return h != null && h.t != null; }

    /// <summary>Dirección del cuerpo inclinada hacia adelante: 0° = vertical, 90° = horizontal hacia adelante.</summary>
    private Vector3 Inclinada(float gradosAdelante)
    {
        float r = gradosAdelante * Mathf.Deg2Rad;
        return AxisU * Mathf.Cos(r) + AxisF * Mathf.Sin(r);
    }

    /// <summary>Dirección que apunta hacia abajo, desviada 'gradosAdelante' hacia adelante (negativo = hacia atrás).</summary>
    private Vector3 DesdeVertical(float gradosAdelante)
    {
        float r = gradosAdelante * Mathf.Deg2Rad;
        return -AxisU * Mathf.Cos(r) + AxisF * Mathf.Sin(r);
    }

    private Quaternion BaseLocal(Hueso h)
    {
        // Con un Animator activo se mezcla desde la pose animada; sin él, desde la pose de reposo guardada.
        return UsaAnimator ? h.t.localRotation : h.restLocal;
    }

    /// <summary>
    /// Orienta el segmento de un hueso (hueso -> hijo) para que apunte hacia 'dirObjetivo' (espacio de la raíz).
    /// Solo usa la pose de REPOSO del hueso y la rotación ACTUAL del padre, así el resultado es siempre el mismo
    /// aunque la columna o la cadera estén inclinadas y no depende de los ejes del hueso (Blender / Mixamo).
    /// 'via' (opcional) hace que el giro pase por esa dirección, para que un brazo que parte pegado al cuerpo
    /// no voltee de lado a lado cuando el objetivo cruza la vertical.
    /// </summary>
    private void AplicarDireccion(Hueso h, Vector3 dirObjetivo, float peso, Vector3 via = default(Vector3))
    {
        if (!Ok(h) || dirObjetivo.sqrMagnitude < 1e-8f) return;
        dirObjetivo.Normalize();

        Quaternion giro;
        if (via.sqrMagnitude > 1e-6f)
        {
            via.Normalize();
            giro = Quaternion.FromToRotation(via, dirObjetivo) * Quaternion.FromToRotation(h.dirRest, via);
        }
        else
        {
            giro = Quaternion.FromToRotation(h.dirRest, dirObjetivo);
        }

        Quaternion mundoObjetivo = transform.rotation * giro * h.restRelRaiz;
        Transform padre = h.t.parent;
        Quaternion localObjetivo = (padre != null ? Quaternion.Inverse(padre.rotation) : Quaternion.identity) * mundoObjetivo;

        h.t.localRotation = Quaternion.Slerp(BaseLocal(h), localObjetivo, peso);
    }

    /// <summary>Dirección actual del segmento del hueso, en el espacio de la raíz.</summary>
    private Vector3 DirActualRaiz(Hueso h)
    {
        return Quaternion.Inverse(transform.rotation) * (h.t.rotation * h.ejeLocal);
    }

    private void NivelarPie(Hueso pie, float nivel, float peso)
    {
        if (!Ok(pie)) return;

        Quaternion mundoObjetivo = transform.rotation * pie.restRelRaiz;
        Transform padre = pie.t.parent;
        Quaternion localObjetivo = (padre != null ? Quaternion.Inverse(padre.rotation) : Quaternion.identity) * mundoObjetivo;
        pie.t.localRotation = Quaternion.Slerp(BaseLocal(pie), localObjetivo, nivel * peso);
    }

    private void AplicarPose(float peso)
    {
        if (!Ok(hHips) && !Ok(hSpine) && !Ok(hUAL) && !Ok(hUAR) && !Ok(hULL) && !Ok(hULR)) return;

        float t = Time.time;
        float m = mezclaAgachado;
        bool conAnimator = UsaAnimator;
        float fase = t * cadenciaPaso;

        // Escalofríos de pánico (vibración de alta frecuencia con Perlin Noise).
        // En vertical solo sube (nunca hunde los pies en el suelo) y en horizontal es más suave para que los pies
        // no patinen; el resto del temblor lo aportan la columna y la cabeza.
        float f = frecuenciaTemblorMiedo;
        Vector3 vibracion = new Vector3(
            (Mathf.PerlinNoise(t * f, 1.3f) - 0.5f) * 2f * 0.35f,
            Mathf.PerlinNoise(2.4f, t * f) * 0.5f,
            (Mathf.PerlinNoise(t * f, 3.5f) - 0.5f) * 2f * 0.35f)
            * (temblorMiedoIntensidad * escalaRef * Mathf.Lerp(1f, 1.5f, m));

        // ---- 1) CADERA: orientación y posición base --------------------------------------------------
        Vector3 posBaseCadera = Vector3.zero;
        if (Ok(hHips))
        {
            posBaseCadera = conAnimator ? transform.InverseTransformPoint(hHips.t.position) : hipsRestPosRaiz;

            float inclCadera = Mathf.Lerp(inclinacionCorrer * 0.5f, inclinacionTorsoAgachado * 0.35f, m);
            float balanceo = Mathf.Sin(t * cadenciaPaso * 0.5f) * 4f * (1f - m);
            AplicarDireccion(hHips, Quaternion.AngleAxis(balanceo, AxisF) * Inclinada(inclCadera), peso);
            hHips.t.position = transform.TransformPoint(posBaseCadera);
        }

        // ---- 2) PIERNAS Y PIES -----------------------------------------------------------------------
        PosarPierna(hULL, hLLL, hFootL, -1f, fase, m, peso);
        PosarPierna(hULR, hLLR, hFootR, +1f, fase + Mathf.PI, m, peso);

        // ---- 3) APOYO EN EL SUELO: la cadera se coloca para que el pie más bajo pise el suelo -----------
        if (Ok(hHips))
        {
            Vector3 offset = Vector3.zero;

            if (hayPies)
            {
                float yMin = float.MaxValue, sx = 0f, sz = 0f;
                int n = 0;
                AcumularPie(hFootL, ref yMin, ref sx, ref sz, ref n);
                AcumularPie(hFootR, ref yMin, ref sx, ref sz, ref n);

                if (n > 0)
                {
                    float k = conAnimator ? peso : 1f;
                    offset.y += (pieRestYMin - yMin) * k;
                    // Al agacharse los pies se quedan donde estaban (la cadera se desplaza hacia atrás).
                    offset.x += (centroPiesRest.x - sx / n) * m * k;
                    offset.z += (centroPiesRest.z - sz / n) * m * k;
                }
            }
            else
            {
                // Sin huesos de pie: aproximación geométrica.
                offset += (-AxisU * descensoAnalitico - AxisF * retrasoAnalitico) * (m * peso);
            }

            offset += AxisU * (Mathf.Abs(Mathf.Sin(fase)) * rebotePaso * escalaRef * (1f - m) * peso);
            offset += vibracion * peso;

            hHips.t.position = transform.TransformPoint(posBaseCadera + offset);
        }

        // ---- 4) COLUMNA, PECHO Y CABEZA --------------------------------------------------------------
        float inclEspalda = Mathf.Lerp(inclinacionCorrer * 0.8f, inclinacionTorsoAgachado * 0.7f, m);
        float inclPecho = Mathf.Lerp(inclinacionCorrer, inclinacionTorsoAgachado, m);
        float temblorTorso = (Mathf.PerlinNoise(t * 30f, 0.5f) - 0.5f) * 6f * m;

        if (Ok(hChest))
        {
            AplicarDireccion(hSpine, Inclinada(inclEspalda), peso);
            AplicarDireccion(hChest, Inclinada(inclPecho + temblorTorso), peso);
        }
        else
        {
            AplicarDireccion(hSpine, Inclinada(inclPecho + temblorTorso), peso);
        }

        if (Ok(hHead))
        {
            // Corriendo: mira aterrada a los lados buscando escombros.
            float miraH = Mathf.Sin(t * 7f) * 0.35f;
            float miraV = 0.2f + Mathf.Cos(t * 9f) * 0.15f;
            Vector3 dirCorrer = (AxisU * 0.9f + AxisR * miraH + AxisF * miraV).normalized;

            // Agachada: la cabeza se encoge hacia adelante (siempre por encima de la horizontal, nunca invertida).
            float temblorCabeza = (Mathf.PerlinNoise(0.8f, t * 30f) - 0.5f) * 8f;
            Vector3 dirAgachada = Quaternion.AngleAxis(temblorCabeza, AxisR) * Inclinada(inclinacionTorsoAgachado + 15f);

            AplicarDireccion(hHead, Vector3.Slerp(dirCorrer, dirAgachada, m), peso);
        }

        // ---- 5) BRAZOS (después del torso y la cabeza: el IK necesita sus posiciones finales) ---------
        PosarBrazo(hUAL, hFAL, -1f, largoSupL, largoAnteL, t, 0f, m, peso);
        PosarBrazo(hUAR, hFAR, +1f, largoSupR, largoAnteR, t, 1.8f, m, peso);
    }

    private void AcumularPie(Hueso pie, ref float yMin, ref float sx, ref float sz, ref int n)
    {
        if (!Ok(pie)) return;
        Vector3 p = transform.InverseTransformPoint(pie.t.position);
        yMin = Mathf.Min(yMin, p.y);
        sx += p.x;
        sz += p.z;
        n++;
    }

    private void PosarPierna(Hueso muslo, Hueso pantorrilla, Hueso pie, float lado, float fase, float m, float peso)
    {
        Vector3 lateral = AxisR * lado;

        // Correr: zancada hacia adelante/atrás; la rodilla se flexiona más mientras la pierna avanza.
        float zancada = amplitudZancada * Mathf.Sin(fase);
        float flexion = FlexionRodillaCorrer * (0.2f + 0.8f * Mathf.Max(0f, Mathf.Cos(fase)));
        Vector3 musloCorrer = DesdeVertical(zancada);
        Vector3 pantorrillaCorrer = DesdeVertical(zancada - flexion);

        // Sentadilla: muslo hacia adelante y abajo, pantorrilla hacia atrás y abajo, rodillas ligeramente abiertas.
        Vector3 musloAgachado = (DesdeVertical(anguloMusloAgachado) + lateral * 0.10f).normalized;
        Vector3 pantorrillaAgachada = (DesdeVertical(-anguloPantorrillaAgachado) - lateral * 0.10f).normalized;

        AplicarDireccion(muslo, Vector3.Slerp(musloCorrer, musloAgachado, m), peso);
        AplicarDireccion(pantorrilla, Vector3.Slerp(pantorrillaCorrer, pantorrillaAgachada, m), peso);

        // Los pies se mantienen (casi) planos respecto al suelo.
        NivelarPie(pie, Mathf.Lerp(0.6f, 1f, m), peso);
    }

    private void PosarBrazo(Hueso sup, Hueso ante, float lado, float largoSup, float largoAnte,
                            float t, float desfase, float m, float peso)
    {
        if (!Ok(sup)) return;

        Vector3 lateral = AxisR * lado;     // hacia el lado exterior de este brazo

        // ---- Correr: brazos en alto agitándose en pánico ----
        float v = velocidadAgitacionBrazos;
        float a = amplitudAgitacionBrazos;
        float w1 = Mathf.Sin(t * v + desfase);
        float w2 = Mathf.Sin(t * v * 1.25f + 1.2f + desfase);
        float w3 = Mathf.Cos(t * v * 0.85f + desfase);

        Vector3 baseSup = (AxisU + lateral * 0.45f + AxisF * 0.10f).normalized;
        Vector3 supCorrer = Quaternion.AngleAxis(w1 * a, AxisF) * (Quaternion.AngleAxis(w2 * a * 0.5f, AxisR) * baseSup);

        Vector3 baseAnte = (AxisU + lateral * 0.20f + AxisF * 0.45f).normalized;
        Vector3 anteCorrer = Quaternion.AngleAxis(w3 * a * 0.8f, AxisF) * (Quaternion.AngleAxis(w1 * a * 0.6f, AxisR) * baseAnte);

        // ---- Agachada: manos sobre la cabeza ----
        Vector3 supAgachada, anteAgachada;
        if (m > 0.001f)
        {
            if (Ok(ante) && Ok(hHead) && largoSup > 1e-4f && largoAnte > 1e-4f)
            {
                CalcularManosEnLaCabeza(sup, lado, largoSup, largoAnte, out supAgachada, out anteAgachada);
            }
            else
            {
                // Respaldo por direcciones fijas (cuando faltan huesos para el IK).
                supAgachada = (AxisU * 0.6f + lateral * 0.15f + AxisF * 0.35f).normalized;
                anteAgachada = (-lateral * 0.65f + AxisU * 0.3f - AxisF * 0.12f).normalized;
            }
        }
        else
        {
            supAgachada = supCorrer;
            anteAgachada = anteCorrer;
        }

        // Solo el brazo superior pasa por la dirección lateral (evita el volteo cuando parte pegado al cuerpo).
        // El antebrazo va directo: su objetivo puede apuntar hacia el centro del cuerpo y ahí 'via' sería inestable.
        AplicarDireccion(sup, Vector3.Slerp(supCorrer, supAgachada, m), peso, lateral);
        AplicarDireccion(ante, Vector3.Slerp(anteCorrer, anteAgachada, m), peso);
    }

    /// <summary>
    /// IK de dos huesos: lleva la muñeca a un punto sobre la cabeza y devuelve las direcciones del brazo y del
    /// antebrazo (espacio de la raíz). El codo se abre hacia adelante y afuera.
    /// </summary>
    private void CalcularManosEnLaCabeza(Hueso sup, float lado, float largoSup, float largoAnte,
                                         out Vector3 dirSup, out Vector3 dirAnte)
    {
        Vector3 hombro = transform.InverseTransformPoint(sup.t.position);
        Vector3 cabeza = transform.InverseTransformPoint(hHead.t.position);

        Vector3 arriba = DirActualRaiz(hHead);
        Vector3 frente = Vector3.Cross(AxisR, arriba);
        if (frente.sqrMagnitude < 1e-6f) frente = AxisF;
        frente.Normalize();

        Vector3 objetivo = cabeza
                           + arriba * (alturaLocal * 0.085f)
                           - frente * (alturaLocal * 0.010f)
                           + AxisR * (lado * alturaLocal * 0.050f);

        Vector3 polo = AxisR * (lado * 0.5f) + AxisF * 0.7f - AxisU * 0.3f;

        ResolverIK2Huesos(hombro, objetivo, largoSup, largoAnte, polo, out dirSup, out dirAnte);
    }

    private static void ResolverIK2Huesos(Vector3 origen, Vector3 objetivo, float l1, float l2, Vector3 polo,
                                          out Vector3 dir1, out Vector3 dir2)
    {
        Vector3 haciaObjetivo = objetivo - origen;
        float distancia = haciaObjetivo.magnitude;
        Vector3 u = distancia > 1e-5f ? haciaObjetivo / distancia : Vector3.down;

        float dMin = Mathf.Abs(l1 - l2) + 1e-4f;
        float dMax = Mathf.Max(dMin, l1 + l2 - 1e-4f);
        float d = Mathf.Clamp(distancia, dMin, dMax);

        float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));

        Vector3 poloOrtogonal = polo - u * Vector3.Dot(polo, u);
        if (poloOrtogonal.sqrMagnitude < 1e-6f) poloOrtogonal = Vector3.Cross(u, Vector3.up);
        if (poloOrtogonal.sqrMagnitude < 1e-6f) poloOrtogonal = Vector3.Cross(u, Vector3.right);
        poloOrtogonal.Normalize();

        Vector3 codo = origen + u * a + poloOrtogonal * h;
        Vector3 muneca = origen + u * d;

        dir1 = (codo - origen).normalized;
        dir2 = (muneca - codo).normalized;
    }

    // =====================================================================================================
    //  CACHÉ DE LA POSE DE REPOSO, GEOMETRÍA Y ORIENTACIÓN
    // =====================================================================================================

    /// <summary>Mide la altura del personaje y detecta hacia dónde mira el modelo (necesita la pose de reposo).</summary>
    private void CalcularEscalaYOrientacion()
    {
        float escalaY = Mathf.Max(0.0001f, transform.lossyScale.y);
        float alturaPorDefecto = AlturaReferencia / escalaY;

        // Altura = de los pies a la cabeza (en unidades locales de la raíz)
        float yPie = float.MaxValue;
        bool conPie = false;
        Transform[] referenciasPie = { leftFoot, rightFoot };
        foreach (Transform p in referenciasPie)
        {
            if (p == null) continue;
            yPie = Mathf.Min(yPie, transform.InverseTransformPoint(p.position).y);
            conPie = true;
        }

        alturaLocal = alturaPorDefecto;
        if (head != null && conPie)
        {
            float medida = (transform.InverseTransformPoint(head.position).y - yPie) * 1.2f;
            if (medida > 1e-3f) alturaLocal = medida;
        }
        escalaRef = alturaLocal / AlturaReferencia;
        alturaMundo = alturaLocal * escalaY;

        // Orientación: la línea hombro izquierdo -> hombro derecho (o cadera) es el eje "derecha" del cuerpo.
        AxisR = corregirRotacion180 ? Vector3.left : Vector3.right;

        if (detectarOrientacionAutomatica)
        {
            Vector3 lateral = Vector3.zero;
            if (leftUpperArm != null && rightUpperArm != null)
                lateral = transform.InverseTransformDirection(rightUpperArm.position - leftUpperArm.position);
            else if (leftUpperLeg != null && rightUpperLeg != null)
                lateral = transform.InverseTransformDirection(rightUpperLeg.position - leftUpperLeg.position);

            lateral.y = 0f;
            if (lateral.sqrMagnitude > 1e-8f) AxisR = lateral.normalized;
        }

        AxisF = Vector3.Cross(AxisR, AxisU).normalized;     // derecha x arriba = adelante
        yawFrenteModelo = Mathf.Atan2(AxisF.x, AxisF.z) * Mathf.Rad2Deg;
    }

    [ContextMenu("Guardar Poses Iniciales")]
    public void CachearPosesOriginales()
    {
        CalcularEscalaYOrientacion();

        Transform pechoReal = (chest != null && chest != spine) ? chest : null;
        Transform baseCuello = pechoReal != null ? pechoReal : (spine != null ? spine : hips);
        Vector3 dirCabeza = Vector3.up;
        if (head != null && baseCuello != null)
        {
            Vector3 v = transform.InverseTransformDirection(head.position - baseCuello.position);
            if (v.sqrMagnitude > 1e-10f) dirCabeza = v.normalized;
        }

        hHips = CrearHueso(hips, spine != null ? spine : (pechoReal != null ? pechoReal : head), Vector3.up);
        hSpine = CrearHueso(spine, pechoReal != null ? pechoReal : head, Vector3.up);
        hChest = CrearHueso(pechoReal, head, Vector3.up);
        hHead = CrearHueso(head, null, dirCabeza);

        hUAL = CrearHueso(leftUpperArm, leftForeArm, -AxisR);
        hFAL = CrearHueso(leftForeArm, leftHand, -AxisR);
        hUAR = CrearHueso(rightUpperArm, rightForeArm, AxisR);
        hFAR = CrearHueso(rightForeArm, rightHand, AxisR);

        hULL = CrearHueso(leftUpperLeg, leftLowerLeg, -Vector3.up);
        hLLL = CrearHueso(leftLowerLeg, leftFoot, -Vector3.up);
        hULR = CrearHueso(rightUpperLeg, rightLowerLeg, -Vector3.up);
        hLLR = CrearHueso(rightLowerLeg, rightFoot, -Vector3.up);

        hFootL = CrearHueso(leftFoot, null, AxisF);
        hFootR = CrearHueso(rightFoot, null, AxisF);

        // Posición de reposo de la cadera
        if (hips != null) hipsRestPosRaiz = transform.InverseTransformPoint(hips.position);

        // Longitudes de los huesos
        largoSupL = LongitudLocal(leftUpperArm, leftForeArm);
        largoAnteL = LongitudLocal(leftForeArm, leftHand);
        largoSupR = LongitudLocal(rightUpperArm, rightForeArm);
        largoAnteR = LongitudLocal(rightForeArm, rightHand);
        largoMuslo = PromedioPositivos(LongitudLocal(leftUpperLeg, leftLowerLeg), LongitudLocal(rightUpperLeg, rightLowerLeg));
        largoPantorrilla = PromedioPositivos(LongitudLocal(leftLowerLeg, leftFoot), LongitudLocal(rightLowerLeg, rightFoot));
        if (largoMuslo <= 0f) largoMuslo = alturaLocal * 0.25f;
        if (largoPantorrilla <= 0f) largoPantorrilla = alturaLocal * 0.25f;

        // Posición de reposo de los pies (referencia para el apoyo en el suelo)
        hayPies = false;
        pieRestYMin = float.MaxValue;
        float sx = 0f, sz = 0f;
        int n = 0;
        foreach (Transform p in new[] { leftFoot, rightFoot })
        {
            if (p == null) continue;
            Vector3 pos = transform.InverseTransformPoint(p.position);
            pieRestYMin = Mathf.Min(pieRestYMin, pos.y);
            sx += pos.x;
            sz += pos.z;
            n++;
        }
        if (n > 0)
        {
            hayPies = true;
            centroPiesRest = new Vector3(sx / n, 0f, sz / n);
        }

        CalcularSentadilla();
    }

    private Hueso CrearHueso(Transform hueso, Transform hijoReferencia, Vector3 dirPorDefecto)
    {
        if (hueso == null) return null;

        Hueso h = new Hueso();
        h.t = hueso;
        h.restLocal = hueso.localRotation;
        h.restRelRaiz = Quaternion.Inverse(transform.rotation) * hueso.rotation;

        Vector3 dir = dirPorDefecto;
        if (hijoReferencia != null)
        {
            Vector3 v = transform.InverseTransformDirection(hijoReferencia.position - hueso.position);
            if (v.sqrMagnitude > 1e-10f) dir = v.normalized;
        }

        h.dirRest = dir.normalized;
        h.ejeLocal = Quaternion.Inverse(hueso.rotation) * (transform.rotation * h.dirRest);
        return h;
    }

    private float LongitudLocal(Transform a, Transform b)
    {
        if (a == null || b == null) return 0f;
        return Vector3.Distance(transform.InverseTransformPoint(a.position), transform.InverseTransformPoint(b.position));
    }

    private static float PromedioPositivos(float a, float b)
    {
        if (a > 0f && b > 0f) return (a + b) * 0.5f;
        return Mathf.Max(a, b);
    }

    /// <summary>
    /// Calcula los ángulos de muslo y pantorrilla para que la cadera baje 'descensoAgacharse' veces la longitud de la
    /// pierna. Es geometría pura: no depende de ejes ni de escalas del modelo.
    /// </summary>
    private void CalcularSentadilla()
    {
        float l1 = Mathf.Max(largoMuslo, 1e-4f);
        float l2 = Mathf.Max(largoPantorrilla, 1e-4f);
        float objetivo = Mathf.Clamp(descensoAgacharse, 0.05f, 0.8f) * (l1 + l2);

        float lo = 0f, hi = 95f * Mathf.Deg2Rad;
        for (int i = 0; i < 30; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float baja = l1 * (1f - Mathf.Cos(mid)) + l2 * (1f - Mathf.Cos(RelacionPantorrilla * mid));
            if (baja < objetivo) lo = mid; else hi = mid;
        }

        float alfa = (lo + hi) * 0.5f;
        float beta = alfa * RelacionPantorrilla;

        anguloMusloAgachado = alfa * Mathf.Rad2Deg;
        anguloPantorrillaAgachado = beta * Mathf.Rad2Deg;
        descensoAnalitico = l1 * (1f - Mathf.Cos(alfa)) + l2 * (1f - Mathf.Cos(beta));
        retrasoAnalitico = l1 * Mathf.Sin(alfa) - l2 * Mathf.Sin(beta);
    }

    // =====================================================================================================
    //  DETECCIÓN DE HUESOS
    // =====================================================================================================

    private static readonly string[] NHips = { "hips", "pelvis", "cadera" };
    private static readonly string[] NSpine = { "spine", "spine0", "columna" };
    private static readonly string[] NChest = { "chest", "upperchest", "spine1", "spine001", "spine2", "pecho" };
    private static readonly string[] NHead = { "head", "cabeza" };

    private static readonly string[] NLUpperArm = { "leftarm", "leftupperarm", "upperarml", "arml", "brazoizq" };
    private static readonly string[] NLForeArm = { "leftforearm", "leftlowerarm", "forearml", "lowerarml", "antebrazoizq" };
    private static readonly string[] NLHand = { "lefthand", "handl", "manoizq" };
    private static readonly string[] NRUpperArm = { "rightarm", "rightupperarm", "upperarmr", "armr", "brazoder" };
    private static readonly string[] NRForeArm = { "rightforearm", "rightlowerarm", "forearmr", "lowerarmr", "antebrazoder" };
    private static readonly string[] NRHand = { "righthand", "handr", "manoder" };

    private static readonly string[] NLUpperLeg = { "leftupleg", "leftupperleg", "leftthigh", "thighl", "upperlegl", "uplegl", "musloizq" };
    private static readonly string[] NLLowerLeg = { "leftleg", "leftlowerleg", "leftcalf", "leftshin", "shinl", "calfl", "lowerlegl", "legl", "piernaizq" };
    private static readonly string[] NLFoot = { "leftfoot", "footl", "pieizq" };
    private static readonly string[] NRUpperLeg = { "rightupleg", "rightupperleg", "rightthigh", "thighr", "upperlegr", "uplegr", "musloder" };
    private static readonly string[] NRLowerLeg = { "rightleg", "rightlowerleg", "rightcalf", "rightshin", "shinr", "calfr", "lowerlegr", "legr", "piernader" };
    private static readonly string[] NRFoot = { "rightfoot", "footr", "pieder" };

    [ContextMenu("Auto-Detectar Huesos del Rig")]
    public void AutoDetectarHuesos()
    {
        Animator anim = animator != null ? animator : GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        if (anim != null && anim.isHuman)
        {
            if (hips == null) hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (spine == null) spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (head == null) head = anim.GetBoneTransform(HumanBodyBones.Head);

            if (leftUpperArm == null) leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            if (leftForeArm == null) leftForeArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (leftHand == null) leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

            if (rightUpperArm == null) rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (rightForeArm == null) rightForeArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (rightHand == null) rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftUpperLeg == null) leftUpperLeg = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (leftLowerLeg == null) leftLowerLeg = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (leftFoot == null) leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);

            if (rightUpperLeg == null) rightUpperLeg = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (rightLowerLeg == null) rightLowerLeg = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (rightFoot == null) rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        // Respaldo por nombre: coincidencia EXACTA con el nombre normalizado (evita que "lefthandring" cuente como "handr").
        Transform[] todos = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in todos)
        {
            if (t == transform) continue;

            if (desactivarCamarasYLucesDelModelo && EsCamaraOLuzEmbebida(t))
            {
                t.gameObject.SetActive(false);
                continue;
            }

            if (t.GetComponent<Renderer>() != null) continue;     // las mallas no son huesos

            string n = NormalizarNombre(t.name);

            if (Asignar(ref hips, t, n, NHips)) continue;
            if (Asignar(ref spine, t, n, NSpine)) continue;
            if (Asignar(ref chest, t, n, NChest)) continue;
            if (Asignar(ref head, t, n, NHead)) continue;

            if (Asignar(ref leftUpperArm, t, n, NLUpperArm)) continue;
            if (Asignar(ref leftForeArm, t, n, NLForeArm)) continue;
            if (Asignar(ref leftHand, t, n, NLHand)) continue;
            if (Asignar(ref rightUpperArm, t, n, NRUpperArm)) continue;
            if (Asignar(ref rightForeArm, t, n, NRForeArm)) continue;
            if (Asignar(ref rightHand, t, n, NRHand)) continue;

            if (Asignar(ref leftUpperLeg, t, n, NLUpperLeg)) continue;
            if (Asignar(ref leftLowerLeg, t, n, NLLowerLeg)) continue;
            if (Asignar(ref leftFoot, t, n, NLFoot)) continue;
            if (Asignar(ref rightUpperLeg, t, n, NRUpperLeg)) continue;
            if (Asignar(ref rightLowerLeg, t, n, NRLowerLeg)) continue;
            if (Asignar(ref rightFoot, t, n, NRFoot)) continue;
        }

        AvisarHuesosFaltantes();
    }

    private static bool Asignar(ref Transform destino, Transform candidato, string nombreNormalizado, string[] nombres)
    {
        if (destino != null) return false;
        if (System.Array.IndexOf(nombres, nombreNormalizado) < 0) return false;
        destino = candidato;
        return true;
    }

    private static string NormalizarNombre(string nombre)
    {
        string n = nombre.ToLowerInvariant();

        int i = n.LastIndexOf(':');
        if (i >= 0) n = n.Substring(i + 1);

        n = n.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "");

        // "mixamorig1hips", "mixamorighips" -> "hips"
        if (n.StartsWith("mixamorig"))
        {
            n = n.Substring("mixamorig".Length);
            while (n.Length > 0 && char.IsDigit(n[0])) n = n.Substring(1);
        }
        return n;
    }

    private static bool EsCamaraOLuzEmbebida(Transform t)
    {
        if (t.CompareTag("MainCamera")) return false;

        string n = t.name.ToLowerInvariant();
        bool nombreSospechoso = n.Contains("camera") || n.Contains("light");
        if (!nombreSospechoso) return false;

        return t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null;
    }

    private void AvisarHuesosFaltantes()
    {
        List<string> faltan = new List<string>();
        if (hips == null) faltan.Add("hips");
        if (spine == null) faltan.Add("spine");
        if (head == null) faltan.Add("head");
        if (leftUpperArm == null) faltan.Add("leftUpperArm");
        if (leftForeArm == null) faltan.Add("leftForeArm");
        if (rightUpperArm == null) faltan.Add("rightUpperArm");
        if (rightForeArm == null) faltan.Add("rightForeArm");
        if (leftUpperLeg == null) faltan.Add("leftUpperLeg");
        if (leftLowerLeg == null) faltan.Add("leftLowerLeg");
        if (leftFoot == null) faltan.Add("leftFoot");
        if (rightUpperLeg == null) faltan.Add("rightUpperLeg");
        if (rightLowerLeg == null) faltan.Add("rightLowerLeg");
        if (rightFoot == null) faltan.Add("rightFoot");

        if (faltan.Count > 0)
        {
            Debug.LogWarning("[GirlPanicReaction] No se encontraron estos huesos, asígnalos a mano en el Inspector: "
                             + string.Join(", ", faltan), this);
        }
    }

    [ContextMenu("Recentrar Personaje al Origen")]
    public void AlinearCentroDelPersonaje()
    {
        if (hips == null) return;

        Vector3 posHipsLocal = transform.InverseTransformPoint(hips.position);
        Vector3 offsetHorizontal = new Vector3(posHipsLocal.x, 0f, posHipsLocal.z);

        if (offsetHorizontal.magnitude > 0.15f * escalaRef)
        {
            // El desplazamiento se aplica en el espacio MUNDO, así funciona aunque el Armature no sea hijo directo de la raíz.
            Vector3 desplazamientoMundo = transform.TransformVector(offsetHorizontal);
            Transform armature = hips.parent;

            if (armature != null && armature != transform)
                armature.position -= desplazamientoMundo;
            else
                hips.position -= desplazamientoMundo;
        }
    }

    // =====================================================================================================
    //  ANIMATOR (OPCIONAL)
    // =====================================================================================================

    private void CachearParametrosAnimator()
    {
        parametrosAnimator = null;
        if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null) return;

        parametrosAnimator = new Dictionary<int, AnimatorControllerParameterType>();
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            parametrosAnimator[p.nameHash] = p.type;
        }
    }

    private void FijarBool(int hash, bool valor)
    {
        if (parametrosAnimator != null && parametrosAnimator.TryGetValue(hash, out AnimatorControllerParameterType tipo)
            && tipo == AnimatorControllerParameterType.Bool)
        {
            animator.SetBool(hash, valor);
        }
    }

    private void FijarFloat(int hash, float valor)
    {
        if (parametrosAnimator != null && parametrosAnimator.TryGetValue(hash, out AnimatorControllerParameterType tipo)
            && tipo == AnimatorControllerParameterType.Float)
        {
            animator.SetFloat(hash, valor);
        }
    }

    private void ActualizarParametrosAnimator()
    {
        if (parametrosAnimator == null || !UsaAnimator) return;

        bool corriendo = estaEnPanico && estadoActual == EstadoPanico.CorriendoAsustada;
        FijarBool(HashIsShaking, estaEnPanico);
        FijarBool(HashIsPanicking, estaEnPanico);
        FijarBool(HashIsRunning, corriendo);
        FijarBool(HashIsCrouching, estaEnPanico && estadoActual == EstadoPanico.AgachadaConMiedo);
        FijarFloat(HashSpeed, velocidadActual);
        FijarFloat(HashFearWeight, pesoTransicion);
    }

    // =====================================================================================================
    //  AUDIO
    // =====================================================================================================

    private void InicializarFuentesAudio()
    {
        if (sonidoGrito != null)
        {
            audioSourceVoces = gameObject.AddComponent<AudioSource>();
            audioSourceVoces.spatialBlend = 1f;
            audioSourceVoces.minDistance = 2f;
            audioSourceVoces.maxDistance = 20f;
            audioSourceVoces.playOnAwake = false;
        }

        if (sonidoRespiracionMiedo != null)
        {
            audioSourceRespiracion = gameObject.AddComponent<AudioSource>();
            audioSourceRespiracion.spatialBlend = 1f;
            audioSourceRespiracion.minDistance = 1.5f;
            audioSourceRespiracion.maxDistance = 15f;
            audioSourceRespiracion.loop = true;
            audioSourceRespiracion.clip = sonidoRespiracionMiedo;
            audioSourceRespiracion.volume = volumenAudio * 0.6f;
            audioSourceRespiracion.playOnAwake = false;
        }
    }

    private void DetenerAudioInmediato()
    {
        corrutinaFade = null;      // Unity ya detiene las corrutinas al desactivar el objeto
        if (audioSourceRespiracion != null)
        {
            audioSourceRespiracion.Stop();
            audioSourceRespiracion.volume = volumenAudio * 0.6f;
        }
    }

    private IEnumerator DesvanecerAudio(AudioSource source, float duracion)
    {
        float volInicial = source.volume;
        float elapsed = 0f;

        while (elapsed < duracion)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(volInicial, 0f, elapsed / duracion);
            yield return null;
        }

        source.Stop();
        source.volume = volInicial;
        corrutinaFade = null;
    }

    // =====================================================================================================
    //  GIZMOS
    // =====================================================================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
        Vector3 centro = Application.isPlaying ? posicionInicialGlobal : transform.position;
        Gizmos.DrawWireSphere(centro, radioMaximoPanico);

        Gizmos.color = Color.yellow;
        float altura = Application.isPlaying ? alturaMundo * 0.45f : 0.7f;
        Vector3 origen = transform.position + Vector3.up * altura;
        Vector3 frente = Application.isPlaying
            ? DireccionDeAngulo(anguloVisual)
            : (corregirRotacion180 ? -transform.forward : transform.forward);

        Gizmos.DrawRay(origen, frente * distanciaDeteccionObstaculo);
        Gizmos.DrawRay(origen, (Quaternion.Euler(0f, -35f, 0f) * frente) * (distanciaDeteccionObstaculo * 0.85f));
        Gizmos.DrawRay(origen, (Quaternion.Euler(0f, 35f, 0f) * frente) * (distanciaDeteccionObstaculo * 0.85f));
    }
}

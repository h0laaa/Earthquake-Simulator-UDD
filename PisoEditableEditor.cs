#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor personalizado para PisoEditable:
/// - Modo Puntos de Control: Permite hacer clic en cualquier punto del piso y elevarlo/bajarlo con un gizmo 3D (con radio de influencia opcional).
/// - Modo Pincel de Esculpido: Permite pintar elevaciones, depresiones y suavizado directamente en la vista de Escena.
/// - Menús directos para crear pisos en la escena y exportar a la carpeta Assets/Modelos3d/ en formato .obj.
/// </summary>
[CustomEditor(typeof(PisoEditable))]
public class PisoEditableEditor : Editor
{
    public enum ModoEdicion
    {
        Desactivado = 0,
        PuntosDeControl = 1,
        PincelEsculpido = 2
    }

    private static ModoEdicion modoActual = ModoEdicion.PuntosDeControl;

    // Configuración Puntos de Control
    private static int seleccionadoX = -1;
    private static int seleccionadoZ = -1;
    private static float radioInfluencia = 4f;
    private static int pasoVisualizacion = 1;

    // Configuración Pincel
    private static float radioPincel = 5f;
    private static float fuerzaPincel = 1.5f;
    private static float alturaAplanarObjetivo = 0f;
    private static PisoEditable.TipoSuavizado tipoFalloff = PisoEditable.TipoSuavizado.SuaveHermite;

    // Configuración Ruido
    private float perlinEscala = 0.06f;
    private float perlinAmplitud = 1.5f;

    private PisoEditable piso;

    private void OnEnable()
    {
        piso = (PisoEditable)target;
        piso.AsegurarComponentes();

        if (piso.materialPiso == null)
        {
            Material sueloMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Edificio_Suelo.mat");
            if (sueloMat != null)
            {
                piso.materialPiso = sueloMat;
                EditorUtility.SetDirty(piso);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(5);
        DibujarEncabezado();
        EditorGUILayout.Space(5);

        // Barra de Modos de Edición
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Modo de Edición en Escena:", EditorStyles.boldLabel);
        string[] toolNames = { "Vista Normal (Off)", "📍 Puntos de Control", "🖌️ Pincel Esculpido" };
        modoActual = (ModoEdicion)GUILayout.Toolbar((int)modoActual, toolNames, GUILayout.Height(30));
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        // Opciones del modo activo
        if (modoActual == ModoEdicion.PuntosDeControl)
        {
            DibujarSeccionPuntosControl();
        }
        else if (modoActual == ModoEdicion.PincelEsculpido)
        {
            DibujarSeccionPincel();
        }

        EditorGUILayout.Space(5);
        DibujarSeccionDimensiones();

        EditorGUILayout.Space(5);
        DibujarSeccionHerramientasRapidas();

        EditorGUILayout.Space(5);
        DibujarSeccionExportacion();

        serializedObject.ApplyModifiedProperties();
    }

    private void DibujarEncabezado()
    {
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("🏛️ PISO CON ELEVACIÓN EDITABLE", headerStyle);
        EditorGUILayout.LabelField("Modifica la altura en diferentes puntos o esculpe con el pincel.", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DibujarSeccionPuntosControl()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Configuración: Puntos de Control 📍", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Haz clic sobre cualquier esfera en la vista de Escena (Scene) para seleccionarla.\n" +
            "Aparecerá un manejador 3D (flecha verde Y) para elevar o descender ese punto.\n" +
            "Si el 'Radio de Influencia' es mayor a 0, elevará gradualmente los puntos adyacentes.",
            MessageType.Info);

        radioInfluencia = EditorGUILayout.Slider(new GUIContent("Radio de Influencia (m)", "Área de afectación suave alrededor del punto seleccionado."), radioInfluencia, 0f, 25f);
        tipoFalloff = (PisoEditable.TipoSuavizado)EditorGUILayout.EnumPopup("Tipo de Curva (Falloff):", tipoFalloff);
        pasoVisualizacion = EditorGUILayout.IntSlider(new GUIContent("Paso de Muestreo Visual", "Muestra cada N vértices para mantener la vista limpia en mallas densas."), pasoVisualizacion, 1, 4);

        if (seleccionadoX >= 0 && seleccionadoZ >= 0 && seleccionadoX < piso.VerticesX && seleccionadoZ < piso.VerticesZ)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"Punto Seleccionado: [X: {seleccionadoX}, Z: {seleccionadoZ}]", EditorStyles.boldLabel);

            float alturaActual = piso.GetAltura(seleccionadoX, seleccionadoZ);
            float nuevaAltura = EditorGUILayout.FloatField("Altura Exacta (Y):", alturaActual);
            if (!Mathf.Approximately(alturaActual, nuevaAltura))
            {
                Undo.RecordObject(piso, "Cambiar Altura Exacta");
                piso.SetAltura(seleccionadoX, seleccionadoZ, nuevaAltura);
                piso.ActualizarGeometriaMalla();
                EditorUtility.SetDirty(piso);
            }

            if (GUILayout.Button("Aplanar Solo este Punto (Y = 0)"))
            {
                Undo.RecordObject(piso, "Aplanar Punto");
                piso.SetAltura(seleccionadoX, seleccionadoZ, 0f);
                piso.ActualizarGeometriaMalla();
                EditorUtility.SetDirty(piso);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Ningún punto seleccionado. Haz clic en un punto en la vista de escena.", MessageType.None);
        }

        EditorGUILayout.EndVertical();
    }

    private void DibujarSeccionPincel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Configuración: Pincel de Esculpido 🖌️", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Arrastra el cursor sobre el piso en la vista de Escena (Scene):\n" +
            "• Clic Izquierdo: Elevar altura\n" +
            "• Shift + Clic Izquierdo: Bajar / Cavar\n" +
            "• Ctrl + Clic Izquierdo: Suavizar desniveles\n" +
            "• Teclas [ y ]: Modificar radio del pincel",
            MessageType.Info);

        radioPincel = EditorGUILayout.Slider(new GUIContent("Radio del Pincel (m)", "Tamaño del área circular de esculpido."), radioPincel, 0.5f, 30f);
        fuerzaPincel = EditorGUILayout.Slider(new GUIContent("Fuerza / Intensidad", "Velocidad de deformación al hacer clic."), fuerzaPincel, 0.1f, 5f);
        tipoFalloff = (PisoEditable.TipoSuavizado)EditorGUILayout.EnumPopup("Curva del Pincel:", tipoFalloff);

        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        alturaAplanarObjetivo = EditorGUILayout.FloatField("Altura para Aplanar:", alturaAplanarObjetivo);
        if (GUILayout.Button("Aplanar a esta altura", GUILayout.Width(140)))
        {
            Undo.RecordObject(piso, "Aplanar Todo a Altura");
            piso.AplanarTodo(alturaAplanarObjetivo);
            EditorUtility.SetDirty(piso);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DibujarSeccionDimensiones()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Dimensiones y Resolución de la Malla", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        float nuevoAncho = EditorGUILayout.Slider("Ancho X (metros):", piso.ancho, 2f, 200f);
        float nuevoLargo = EditorGUILayout.Slider("Largo Z (metros):", piso.largo, 2f, 200f);
        int nuevaSubX = EditorGUILayout.IntSlider("Subdivisiones X:", piso.subdivisionesX, 4, 80);
        int nuevaSubZ = EditorGUILayout.IntSlider("Subdivisiones Z:", piso.subdivisionesZ, 4, 80);
        float nuevaAltBase = EditorGUILayout.FloatField("Altura Base Y:", piso.alturaBase);
        Vector2 nuevoTiling = EditorGUILayout.Vector2Field("Tiling de Textura (UV):", piso.tilingUV);
        Material nuevoMat = (Material)EditorGUILayout.ObjectField("Material de Suelo:", piso.materialPiso, typeof(Material), false);
        bool col = EditorGUILayout.Toggle("Actualizar MeshCollider:", piso.actualizarColisionador);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(piso, "Modificar Parámetros de Piso");
            piso.ancho = nuevoAncho;
            piso.largo = nuevoLargo;
            piso.subdivisionesX = nuevaSubX;
            piso.subdivisionesZ = nuevaSubZ;
            piso.alturaBase = nuevaAltBase;
            piso.tilingUV = nuevoTiling;
            piso.materialPiso = nuevoMat;
            piso.actualizarColisionador = col;
            piso.GenerarMalla(true);
            EditorUtility.SetDirty(piso);
        }

        EditorGUILayout.LabelField($"Vértices Totales: {piso.TotalVertices} ({piso.VerticesX} x {piso.VerticesZ})", EditorStyles.miniLabel);

        if (GUILayout.Button("🔄 Regenerar Malla (Preservando Alturas)", GUILayout.Height(24)))
        {
            Undo.RecordObject(piso, "Regenerar Malla");
            piso.GenerarMalla(true);
            EditorUtility.SetDirty(piso);
        }

        EditorGUILayout.EndVertical();
    }

    private void DibujarSeccionHerramientasRapidas()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Acciones y Relieve Automático", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        perlinEscala = EditorGUILayout.FloatField("Escala Ruido:", perlinEscala);
        perlinAmplitud = EditorGUILayout.FloatField("Amplitud (m):", perlinAmplitud);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("⛰️ Generar Relieve Natural (Ruido Perlin)", GUILayout.Height(26)))
        {
            Undo.RecordObject(piso, "Generar Relieve Perlin");
            piso.GenerarRelievePerlin(perlinEscala, perlinAmplitud, Random.Range(0f, 1000f));
            EditorUtility.SetDirty(piso);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("✨ Suavizar Todo el Piso"))
        {
            Undo.RecordObject(piso, "Suavizar Todo");
            piso.SuavizarTodo(0.5f);
            EditorUtility.SetDirty(piso);
        }

        if (GUILayout.Button("🛑 Aplanar Todo (Altura = 0)"))
        {
            if (EditorUtility.DisplayDialog("Aplanar Piso", "¿Deseas restablecer todas las alturas a 0?", "Sí, Aplanar", "Cancelar"))
            {
                Undo.RecordObject(piso, "Aplanar Todo a Cero");
                piso.AplanarTodo(0f);
                EditorUtility.SetDirty(piso);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DibujarSeccionExportacion()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Carpeta Modelos3d (Exportación .OBJ)", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox("Guarda el piso con todas sus elevaciones directamente como un modelo 3D (.obj) en Assets/Modelos3d/.", MessageType.None);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f, 1f);
        if (GUILayout.Button("💾 Guardar en Assets/Modelos3d/PisoEditable.obj", GUILayout.Height(32)))
        {
            string path = piso.ExportarAObj("Assets/Modelos3d/PisoEditable.obj");
            EditorUtility.DisplayDialog("Modelo Guardado", $"El modelo 3D ha sido exportado exitosamente a:\n\n{path}\n\nYa puedes usarlo en cualquier escena o prefabs.", "Aceptar");
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("💾 Guardar como nuevo archivo OBJ con otro nombre..."))
        {
            string defaultFolder = Path.Combine(Application.dataPath, "Modelos3d");
            string fullSavePath = EditorUtility.SaveFilePanel("Guardar Modelo 3D de Piso", defaultFolder, "Piso_Personalizado", "obj");
            if (!string.IsNullOrEmpty(fullSavePath))
            {
                string relativePath = "Assets" + fullSavePath.Substring(Application.dataPath.Length).Replace('\\', '/');
                piso.ExportarAObj(relativePath);
                EditorUtility.DisplayDialog("Modelo Guardado", $"Modelo guardado con éxito en:\n\n{relativePath}", "Aceptar");
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void OnSceneGUI()
    {
        if (piso == null) return;

        // Overlay con instrucciones en la esquina de la vista de escena
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 320, 100), EditorStyles.helpBox);
        GUILayout.Label("<b>Herramienta de Piso Editable</b>", EditorStyles.boldLabel);
        if (modoActual == ModoEdicion.PuntosDeControl)
        {
            GUILayout.Label("• Clic en una esfera: Seleccionar punto");
            GUILayout.Label("• Flecha Verde 3D: Subir o bajar altura");
            GUILayout.Label($"• Radio de influencia suave: {radioInfluencia:F1}m");
        }
        else if (modoActual == ModoEdicion.PincelEsculpido)
        {
            GUILayout.Label("• Clic Izquierdo: Elevar altura");
            GUILayout.Label("• Shift + Clic: Bajar / Cavar");
            GUILayout.Label("• Ctrl + Clic: Suavizar desniveles");
            GUILayout.Label($"• Radio Pincel: {radioPincel:F1}m | Fuerza: {fuerzaPincel:F1}");
        }
        else
        {
            GUILayout.Label("Modo edición desactivado. Selecciónalo en el Inspector.");
        }
        GUILayout.EndArea();
        Handles.EndGUI();

        // Control de atajos [ y ] para tamaño de pincel
        Event e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.LeftBracket)
            {
                radioPincel = Mathf.Max(0.5f, radioPincel - 0.5f);
                radioInfluencia = Mathf.Max(0.5f, radioInfluencia - 0.5f);
                SceneView.RepaintAll();
                e.Use();
            }
            else if (e.keyCode == KeyCode.RightBracket)
            {
                radioPincel += 0.5f;
                radioInfluencia += 0.5f;
                SceneView.RepaintAll();
                e.Use();
            }
        }

        if (modoActual == ModoEdicion.PuntosDeControl)
        {
            ManejarModoPuntosControl(e);
        }
        else if (modoActual == ModoEdicion.PincelEsculpido)
        {
            ManejarModoPincel(e);
        }
    }

    private void ManejarModoPuntosControl(Event e)
    {
        int resX = piso.VerticesX;
        int resZ = piso.VerticesZ;
        int paso = Mathf.Max(1, pasoVisualizacion);

        Vector3 puntoSeleccionadoPos = Vector3.zero;
        bool hayPuntoValido = (seleccionadoX >= 0 && seleccionadoZ >= 0 && seleccionadoX < resX && seleccionadoZ < resZ);

        for (int z = 0; z < resZ; z += paso)
        {
            for (int x = 0; x < resX; x += paso)
            {
                Vector3 posMundo = piso.GetPosicionMundo(x, z);
                float handleSize = HandleUtility.GetHandleSize(posMundo) * 0.08f;

                bool esEsteElSeleccionado = (hayPuntoValido && x == seleccionadoX && z == seleccionadoZ);

                if (esEsteElSeleccionado)
                {
                    puntoSeleccionadoPos = posMundo;
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(0, posMundo, Quaternion.identity, handleSize * 1.5f, EventType.Repaint);
                }
                else
                {
                    Handles.color = new Color(0f, 0.85f, 1f, 0.7f);
                    if (Handles.Button(posMundo, Quaternion.identity, handleSize, handleSize, Handles.DotHandleCap))
                    {
                        seleccionadoX = x;
                        seleccionadoZ = z;
                        Repaint();
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        // Si hay un punto seleccionado, mostrar el Gizmo de posición en Y
        if (hayPuntoValido)
        {
            puntoSeleccionadoPos = piso.GetPosicionMundo(seleccionadoX, seleccionadoZ);

            // Dibujar círculo de influencia si está activo
            if (radioInfluencia > 0.05f)
            {
                Handles.color = new Color(1f, 0.85f, 0f, 0.25f);
                Handles.DrawSolidDisc(puntoSeleccionadoPos, Vector3.up, radioInfluencia);
                Handles.color = new Color(1f, 0.9f, 0.2f, 0.8f);
                Handles.DrawWireDisc(puntoSeleccionadoPos, Vector3.up, radioInfluencia);
            }

            // Etiqueta de altura
            float alt = piso.GetAltura(seleccionadoX, seleccionadoZ);
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.yellow }
            };
            Handles.Label(puntoSeleccionadoPos + Vector3.up * 0.3f, $"Y: {alt:+0.00;-0.00;0.00}m", labelStyle);

            EditorGUI.BeginChangeCheck();
            Vector3 nuevaPosMundo = Handles.PositionHandle(puntoSeleccionadoPos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(piso, "Mover Altura de Punto");
                float deltaY = nuevaPosMundo.y - puntoSeleccionadoPos.y;

                if (radioInfluencia > 0.05f)
                {
                    piso.ModificarAlturaEnPunto(puntoSeleccionadoPos, radioInfluencia, deltaY, tipoFalloff);
                }
                else
                {
                    piso.SetAltura(seleccionadoX, seleccionadoZ, alt + deltaY);
                }

                piso.ActualizarGeometriaMalla();
                EditorUtility.SetDirty(piso);
            }
        }
    }

    private void ManejarModoPincel(Event e)
    {
        // Evitar deseleccionar el objeto al hacer clic en el SceneView
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane pisoPlano = new Plane(piso.transform.up, piso.transform.position);

        Vector3 hitPoint = Vector3.zero;
        bool hit = false;

        // Intentar raycast con el colisionador del piso o plano
        Collider col = piso.GetComponent<Collider>();
        if (col != null && col.Raycast(ray, out RaycastHit rayHit, 1000f))
        {
            hitPoint = rayHit.point;
            hit = true;
        }
        else if (pisoPlano.Raycast(ray, out float enter))
        {
            hitPoint = ray.GetPoint(enter);
            hit = true;
        }

        if (hit)
        {
            // Visualizar disco de pincel
            Color brushColor = Color.cyan;
            if (e.shift) brushColor = new Color(1f, 0.4f, 0.2f); // Naranja para cavar
            else if (e.control) brushColor = new Color(0.4f, 1f, 0.4f); // Verde para suavizar

            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.2f);
            Handles.DrawSolidDisc(hitPoint, Vector3.up, radioPincel);
            Handles.color = brushColor;
            Handles.DrawWireDisc(hitPoint, Vector3.up, radioPincel);

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                Undo.RecordObject(piso, "Esculpir Piso");

                if (e.control)
                {
                    // Suavizar
                    piso.SuavizarEnPunto(hitPoint, radioPincel, fuerzaPincel * 0.15f);
                }
                else
                {
                    // Elevar o Cavar
                    float delta = fuerzaPincel * (e.shift ? -0.1f : 0.1f);
                    piso.ModificarAlturaEnPunto(hitPoint, radioPincel, delta, tipoFalloff);
                }

                piso.ActualizarGeometriaMalla();
                EditorUtility.SetDirty(piso);
                SceneView.RepaintAll();
                e.Use();
            }
        }
    }

    #region Elementos de Menú y Creación Automática

    [MenuItem("Tools/Modelos 3D/Crear Piso Editable en la Escena", false, 10)]
    public static void CrearPisoEditableEnEscena()
    {
        GameObject go = new GameObject("Piso Editable");
        Undo.RegisterCreatedObjectUndo(go, "Crear Piso Editable");

        // Centrar en la escena o cerca de la cámara
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
        {
            spawnPos = SceneView.lastActiveSceneView.camera.transform.position;
            spawnPos.y = 0f;
        }
        go.transform.position = spawnPos;

        PisoEditable comp = go.AddComponent<PisoEditable>();
        comp.ancho = 60f;
        comp.largo = 60f;
        comp.subdivisionesX = 30;
        comp.subdivisionesZ = 30;

        Material sueloMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Edificio_Suelo.mat");
        if (sueloMat != null)
        {
            comp.materialPiso = sueloMat;
        }

        comp.GenerarMalla(false);

        // Exportar automáticamente una versión inicial al directorio Modelos3d
        comp.ExportarAObj("Assets/Modelos3d/PisoEditable.obj");

        Selection.activeGameObject = go;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Debug.Log("[PisoEditable] Objeto 'Piso Editable' creado en la escena y guardado en Assets/Modelos3d/PisoEditable.obj");
        EditorUtility.DisplayDialog("Piso Creado",
            "Se ha creado el objeto 'Piso Editable' en la escena con su material de suelo y MeshCollider.\n\n" +
            "También se guardó una copia en 'Assets/Modelos3d/PisoEditable.obj'.\n\n" +
            "Usa los modos '📍 Puntos de Control' o '🖌️ Pincel Esculpido' en el Inspector para ajustar las alturas en la vista de Escena.", "¡Comenzar!");
    }

    [MenuItem("GameObject/3D Object/Piso Editable", false, 10)]
    public static void CrearPisoDesdeGameObjectMenu()
    {
        CrearPisoEditableEnEscena();
    }

    [MenuItem("Tools/Modelos 3D/Regenerar Archivo OBJ en Modelos3d", false, 11)]
    public static void RegenerarArchivoObjEnModelos3d()
    {
        PisoEditable existente = Object.FindAnyObjectByType<PisoEditable>();
        if (existente == null)
        {
            CrearPisoEditableEnEscena();
        }
        else
        {
            string path = existente.ExportarAObj("Assets/Modelos3d/PisoEditable.obj");
            EditorUtility.DisplayDialog("Archivo Actualizado", $"Se ha actualizado el archivo en:\n{path}", "Aceptar");
        }
    }

    [MenuItem("Tools/Modelos 3D/Convertir Objeto Seleccionado en Piso Editable", false, 12)]
    public static void ConvertirSeleccionadoEnPisoEditable()
    {
        if (Selection.activeGameObject == null)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor selecciona primero un objeto con malla en la jerarquía (Hierarchy).", "Aceptar");
            return;
        }

        GameObject go = Selection.activeGameObject;
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Aviso", "El objeto seleccionado no contiene un MeshFilter con una malla válida.", "Aceptar");
            return;
        }

        PisoEditable comp = go.GetComponent<PisoEditable>();
        if (comp == null)
        {
            comp = Undo.AddComponent<PisoEditable>(go);
        }

        comp.InicializarDesdeMalla(mf.sharedMesh);
        EditorUtility.DisplayDialog("Piso Editable Listo", $"'{go.name}' ahora es un piso con elevación editable.\n\nPuedes ajustar sus alturas en la vista de Escena.", "Aceptar");
    }

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            string objPath = Path.Combine(Application.dataPath, "Modelos3d", "PisoEditable.obj");
            if (!File.Exists(objPath))
            {
                PisoEditable existente = Object.FindAnyObjectByType<PisoEditable>();
                if (existente != null)
                {
                    existente.ExportarAObj("Assets/Modelos3d/PisoEditable.obj");
                }
            }
        };
    }

    #endregion
}
#endif

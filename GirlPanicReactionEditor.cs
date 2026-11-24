#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor personalizado para el componente GirlPanicReaction en el Inspector de Unity.
/// Ofrece botones de acción rápida, visualización del estado actual y pruebas interactivas.
/// </summary>
[CustomEditor(typeof(GirlPanicReaction))]
public class GirlPanicReactionEditor : Editor
{
    private GirlPanicReaction script;

    private void OnEnable()
    {
        script = (GirlPanicReaction)target;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. Banner de Estado
        DibujarPanelEstado();

        EditorGUILayout.Space(6);

        // 2. Botones de acción rápida
        DibujarBotonesAccion();

        EditorGUILayout.Space(6);

        // 3. Propiedades del inspector por defecto
        DrawDefaultInspector();

        serializedObject.ApplyModifiedProperties();
    }

    private void DibujarPanelEstado()
    {
        if (Application.isPlaying)
        {
            if (script.EstaEnPanico)
            {
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                string textoEstado = script.Estado == GirlPanicReaction.EstadoPanico.CorriendoAsustada
                    ? "🏃 ¡EN PÁNICO! Corriendo hacia adelante con brazos en alto"
                    : "🙇 ¡EN PÁNICO! Agachada con manos en la cabeza temblando";

                EditorGUILayout.HelpBox($"{textoEstado}\n(Reaccionando activamente al temblor)", MessageType.Warning);
            }
            else
            {
                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
                EditorGUILayout.HelpBox("🌿 ESTADO: CALMA (Esperando a que ocurra un sismo)", MessageType.Info);
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.HelpBox("💡 Activa el checkbox 'Simular Temblor En Editor' o entra en Play Mode para ver a Girl_low_poly reaccionar.", MessageType.None);
        }
    }

    private void DibujarBotonesAccion()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Acciones Rápidas", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (Application.isPlaying)
        {
            string botonTexto = script.EstaEnPanico ? "⏹️ Detener Pánico" : "▶️ Iniciar Pánico (Probar)";
            GUI.backgroundColor = script.EstaEnPanico ? new Color(1f, 0.6f, 0.6f) : new Color(0.6f, 1f, 0.6f);

            if (GUILayout.Button(botonTexto, GUILayout.Height(28)))
            {
                if (script.EstaEnPanico)
                    script.DetenerPanico();
                else
                    script.IniciarPanico();
            }
            GUI.backgroundColor = Color.white;
        }

        if (GUILayout.Button("🔍 Auto-Detectar Huesos (Rig)", GUILayout.Height(28)))
        {
            Undo.RecordObject(script, "Auto-detectar huesos");
            script.AutoDetectarHuesos();
            script.AlinearCentroDelPersonaje();
            script.CachearPosesOriginales();
            EditorUtility.SetDirty(script);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("📏 Restaurar Escala (1, 1, 1)", GUILayout.Height(24)))
        {
            Undo.RecordObject(script.transform, "Restaurar Escala");
            script.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(script.gameObject);
        }

        if (GUILayout.Button("🎯 Recentrar Personaje", GUILayout.Height(24)))
        {
            Undo.RecordObject(script, "Recentrar personaje");
            script.AlinearCentroDelPersonaje();
            script.CachearPosesOriginales();
            EditorUtility.SetDirty(script);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }
}
#endif

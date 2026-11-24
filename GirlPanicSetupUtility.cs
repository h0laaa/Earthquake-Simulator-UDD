#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Utilidad del Editor para configurar el asset Girl_low_poly en la escena,
/// vincular sus huesos Mixamo y ajustar los modos de reacción al temblor.
/// NOTA: Respeta al 100% la escala y tamaño del objeto que el usuario haya establecido en la escena.
/// </summary>
public static class GirlPanicSetupUtility
{
    private const string GirlFbxPathUpper = "Assets/Modelos3d/Girl_low_poly.fbx";
    private const string GirlFbxPathLower = "Assets/Modelos3d/girl_low_poly.fbx";

    [MenuItem("Tools/Girl Low Poly/Configurar y Colocar en la Escena", false, 30)]
    public static void SetupGirlInSceneMenu()
    {
        SetupGirlInScene(true);
    }

    [MenuItem("Tools/Girl Low Poly/Probar Reacción de Pánico (Play Mode)", false, 31)]
    public static void TestPanicReactionMenu()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Aviso", "Debes entrar en Play Mode para probar la reacción al temblor.", "Aceptar");
            return;
        }

        GirlPanicReaction reaction = Object.FindAnyObjectByType<GirlPanicReaction>();
        if (reaction != null)
        {
            if (reaction.EstaEnPanico)
            {
                reaction.DetenerPanico();
            }
            else
            {
                reaction.IniciarPanico();
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "No se encontró ningún objeto con el componente GirlPanicReaction en la escena.", "Aceptar");
        }
    }

    [MenuItem("Tools/Girl Low Poly/Modo: Correr con Brazos en Alto", false, 32)]
    public static void SetModeRunMenu()
    {
        SetPanicMode(GirlPanicReaction.ModoPanico.CorrerConBrazosEnAlto, "Correr hacia adelante con los brazos en alto moviéndolos");
    }

    [MenuItem("Tools/Girl Low Poly/Modo: Agacharse con Manos en la Cabeza", false, 33)]
    public static void SetModeCrouchMenu()
    {
        SetPanicMode(GirlPanicReaction.ModoPanico.AgacharseConManosEnCabeza, "Agacharse protegiéndose la cabeza con las manos y temblar de miedo");
    }

    [MenuItem("Tools/Girl Low Poly/Modo: Huir y luego Agacharse (Recomendado)", false, 34)]
    public static void SetModeFleeAndCrouchMenu()
    {
        SetPanicMode(GirlPanicReaction.ModoPanico.HuirYAgacharse, "Huir corriendo hacia adelante unos segundos y luego agacharse a cubrirse la cabeza");
    }

    [MenuItem("Tools/Girl Low Poly/Modo: Alternar Ciclo Continuo", false, 35)]
    public static void SetModeAlternateMenu()
    {
        SetPanicMode(GirlPanicReaction.ModoPanico.AlternarCiclo, "Alternar continuamente entre correr despavorida y agacharse");
    }

    [MenuItem("Tools/Girl Low Poly/Restaurar Escala a (1, 1, 1)", false, 36)]
    public static void ResetScaleMenu()
    {
        GirlPanicReaction[] reactions = Object.FindObjectsByType<GirlPanicReaction>(FindObjectsSortMode.None);
        if (reactions.Length == 0)
        {
            EditorUtility.DisplayDialog("Aviso", "No se encontró a Girl_low_poly en la escena.", "Aceptar");
            return;
        }

        foreach (GirlPanicReaction r in reactions)
        {
            Undo.RecordObject(r.transform, "Restaurar Escala (1, 1, 1)");
            r.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(r.gameObject);
        }

        EditorUtility.DisplayDialog("Escala Restaurada", "La escala de Girl_low_poly se ha restablecido a (1, 1, 1).\nPuedes ajustarla al tamaño que desees en el Transform del Inspector y no volverá a modificarse.", "Aceptar");
    }

    [MenuItem("Tools/Girl Low Poly/Alternar Corrección Frente (180 grados)", false, 37)]
    public static void ToggleOrientation180Menu()
    {
        GirlPanicReaction[] reactions = Object.FindObjectsByType<GirlPanicReaction>(FindObjectsSortMode.None);
        if (reactions.Length == 0)
        {
            EditorUtility.DisplayDialog("Aviso", "No se encontró a Girl_low_poly en la escena.", "Aceptar");
            return;
        }

        foreach (GirlPanicReaction r in reactions)
        {
            Undo.RecordObject(r, "Alternar orientación 180");
            r.CorregirRotacion180 = !r.CorregirRotacion180;
            EditorUtility.SetDirty(r);
        }

        bool estado = reactions[0].CorregirRotacion180;
        EditorUtility.DisplayDialog("Orientación Actualizada", $"Corrección de rotación 180°: {(estado ? "ACTIVADA (Recomendada para Mixamo)" : "DESACTIVADA")}", "Aceptar");
    }

    private static void SetPanicMode(GirlPanicReaction.ModoPanico mode, string nombreModo)
    {
        GirlPanicReaction[] reactions = Object.FindObjectsByType<GirlPanicReaction>(FindObjectsSortMode.None);
        if (reactions.Length == 0)
        {
            EditorUtility.DisplayDialog("Aviso", "Primero configura Girl_low_poly en la escena desde el menú Tools > Girl Low Poly > Configurar y Colocar en la Escena.", "Aceptar");
            return;
        }

        foreach (GirlPanicReaction r in reactions)
        {
            SerializedObject so = new SerializedObject(r);
            SerializedProperty prop = so.FindProperty("modoPanico");
            if (prop != null)
            {
                prop.enumValueIndex = (int)mode;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(r);
            }
        }

        EditorUtility.DisplayDialog("Modo de Pánico Actualizado",
            $"Se ha establecido el modo de pánico en:\n\n• {nombreModo}\n\nCuando ocurra un sismo (CameraShake) o actives el modo de prueba, Girl_low_poly reaccionará con este comportamiento.", "Aceptar");
    }

    public static void SetupGirlInScene(bool showDialog)
    {
        GameObject girlAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GirlFbxPathUpper)
                            ?? AssetDatabase.LoadAssetAtPath<GameObject>(GirlFbxPathLower);

        if (girlAsset == null)
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Error", $"No se encontró el asset en la ruta: {GirlFbxPathUpper}", "Aceptar");
            }
            return;
        }

        // 1. Buscar si ya existe en la escena
        GirlPanicReaction existing = Object.FindAnyObjectByType<GirlPanicReaction>();
        GameObject targetGo = null;

        if (existing != null)
        {
            targetGo = existing.gameObject;
        }
        else
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name.ToLower().Contains("girl") || go.name.ToLower().Contains("girl_low_poly"))
                {
                    targetGo = go;
                    break;
                }
            }
        }

        // 2. Si no existe, instanciarla en la escena frente a la cámara
        if (targetGo == null)
        {
            Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
            Vector3 camPos = (cam != null) ? cam.transform.position : new Vector3(0f, 1f, 0f);
            Vector3 camFwd = (cam != null) ? cam.transform.forward : Vector3.forward;
            camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 0.001f) camFwd = Vector3.forward;
            camFwd.Normalize();

            Vector3 spawnPos = camPos + camFwd * 3.5f;

            Ray ray = new Ray(spawnPos + Vector3.up * 2f, Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                spawnPos.y = hit.point.y;
            }
            else
            {
                spawnPos.y = Mathf.Max(0f, camPos.y - 1.2f);
            }

            targetGo = PrefabUtility.InstantiatePrefab(girlAsset) as GameObject;
            if (targetGo == null)
            {
                targetGo = Object.Instantiate(girlAsset);
            }

            targetGo.name = "Girl_low_poly";
            targetGo.transform.position = spawnPos;
            targetGo.transform.rotation = Quaternion.Euler(0f, (cam != null ? cam.transform.eulerAngles.y : 0f) + 180f, 0f);

            Undo.RegisterCreatedObjectUndo(targetGo, "Colocar Girl_low_poly");
        }

        // 3. Desactivar cámara y luz residuales de Blender
        foreach (Transform child in targetGo.GetComponentsInChildren<Transform>(true))
        {
            string n = child.name.ToLowerInvariant();
            if (n.Contains("camera") || n.Contains("light"))
            {
                child.gameObject.SetActive(false);
            }
        }

        // 4. Configurar CharacterController SIN modificar el transform.localScale
        CharacterController cc = targetGo.GetComponent<CharacterController>();
        if (cc == null)
        {
            cc = Undo.AddComponent<CharacterController>(targetGo);
            cc.height = 1.7f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.3f;
        }

        // 5. Configurar GirlPanicReaction
        GirlPanicReaction reactionComponent = targetGo.GetComponent<GirlPanicReaction>();
        if (reactionComponent == null)
        {
            reactionComponent = Undo.AddComponent<GirlPanicReaction>(targetGo);
        }

        reactionComponent.CorregirRotacion180 = true;
        reactionComponent.AutoDetectarHuesos();
        reactionComponent.AlinearCentroDelPersonaje();
        reactionComponent.CachearPosesOriginales();

        // 6. Seleccionar en jerarquía
        Selection.activeGameObject = targetGo;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        EditorUtility.SetDirty(targetGo);
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Girl_low_poly Configurada con Éxito",
                "El asset 'Girl_low_poly' ha sido configurado en la escena con su rig y script de pánico.\n\n" +
                "• Carrera Hacia Adelante: Corregida la orientación para que corra hacia su frente real sin retroceder.\n" +
                "• Escala y Tamaño Intactos: Se respeta totalmente la escala que tú le des al objeto en el Inspector.\n" +
                "• Brazos en Alto: Mientras corre, alza los brazos agitándolos en pánico.\n" +
                "• Agacharse con Manos en Cabeza: Se agacha cubriéndose la cabeza con las manos y tiembla con escalofríos de miedo.\n" +
                "• Prueba Rápida: Pulsa 'Play' o activa 'Simular Temblor En Editor' en el Inspector para probarlo.", "Aceptar");
        }
    }
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Utilidad para configurar rápidamente el colisionador de la cámara y su distancia al piso.
/// Garantiza que la cámara mantenga su distancia del suelo al caminar sobre desniveles y que jamás reciba daño por el piso.
/// </summary>
public static class CameraSetupUtility
{
    [MenuItem("Tools/Cámara/Configurar Colisionador y Altura de Cámara", false, 1)]
    public static void ConfigurarColisionadorCamara()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = Object.FindAnyObjectByType<Camera>();
        }

        if (cam == null)
        {
            EditorUtility.DisplayDialog("Aviso", "No se encontró ninguna cámara en la escena activa.", "Aceptar");
            return;
        }

        GameObject camObj = cam.gameObject;
        Undo.RegisterCompleteObjectUndo(camObj, "Configurar Colisionador de Cámara");

        // 1. Asegurar CameraMovement y MovementSystemSelector
        CameraMovement mov = camObj.GetComponent<CameraMovement>();
        if (mov == null)
        {
            mov = Undo.AddComponent<CameraMovement>(camObj);
        }

        MovementSystemSelector sel = camObj.GetComponent<MovementSystemSelector>();
        if (sel == null)
        {
            sel = Undo.AddComponent<MovementSystemSelector>(camObj);
        }

        // 2. Asegurar SphereCollider
        SphereCollider col = camObj.GetComponent<SphereCollider>();
        if (col == null)
        {
            col = Undo.AddComponent<SphereCollider>(camObj);
        }
        col.radius = 0.35f;
        col.isTrigger = true;
        col.center = Vector3.zero;

        // 3. Asegurar Rigidbody cinemático
        Rigidbody rb = camObj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(camObj);
        }
        rb.isKinematic = true;
        rb.useGravity = false;

        // 4. Alinear inmediatamente sobre el piso si hay uno debajo
        if (Physics.Raycast(camObj.transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            float targetY = hit.point.y + 1.65f;
            camObj.transform.position = new Vector3(camObj.transform.position.x, targetY, camObj.transform.position.z);
        }

        EditorUtility.SetDirty(camObj);
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Selection.activeGameObject = camObj;

        Debug.Log($"[CameraSetupUtility] Cámara '{camObj.name}' configurada con SphereCollider (trigger), Rigidbody cinemático y distancia de 1.65m sobre el piso.");

        EditorUtility.DisplayDialog("Cámara Configurada",
            $"La cámara '{camObj.name}' ha sido configurada con éxito:\n\n" +
            "• Colisionador: SphereCollider (Trigger, radio 0.35m).\n" +
            "• Físicas: Rigidbody cinemático.\n" +
            "• Distancia al Piso: 1.65m (altura de ojos).\n" +
            "• Selector de Movimiento: MovementSystemSelector activado al inicio.\n" +
            "• Protección de Daño: Inmunidad total al piso y suelo.\n\n" +
            "Ahora la cámara seguirá automáticamente las elevaciones del piso y mostrará el menú de selección de movimiento al iniciar.", "¡Aceptar!");
    }

    [MenuItem("Tools/Cámara/Alinear Cámara al Piso Inmediatamente", false, 2)]
    public static void AlinearCamaraAlPiso()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();

        if (cam == null)
        {
            EditorUtility.DisplayDialog("Aviso", "No se encontró ninguna cámara.", "Aceptar");
            return;
        }

        float alt = 2.5f;
        CameraMovement mov = cam.GetComponent<CameraMovement>();
        if (mov != null) alt = mov.DistanciaAlPiso;

        RaycastHit[] hits = Physics.RaycastAll(cam.transform.position + Vector3.up * 5f, Vector3.down, 150f, ~0, QueryTriggerInteraction.Ignore);
        float bestGroundY = float.MinValue;
        Collider bestCol = null;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.collider.transform == cam.transform || h.collider.transform.IsChildOf(cam.transform)) continue;
            if (h.normal.y > 0.25f || CameraMovement.EsPisoOSuelo(h.collider))
            {
                if (h.point.y > bestGroundY)
                {
                    bestGroundY = h.point.y;
                    bestCol = h.collider;
                }
            }
        }

        Undo.RecordObject(cam.transform, "Alinear Cámara al Piso");
        if (bestCol != null)
        {
            cam.transform.position = new Vector3(cam.transform.position.x, bestGroundY + alt, cam.transform.position.z);
            EditorUtility.SetDirty(cam);
            EditorUtility.DisplayDialog("Alineada", $"Cámara posicionada a {alt:F2}m sobre '{bestCol.name}' (Y = {bestGroundY:F2}).", "Aceptar");
        }
        else
        {
            cam.transform.position = new Vector3(cam.transform.position.x, alt, cam.transform.position.z);
            EditorUtility.SetDirty(cam);
            EditorUtility.DisplayDialog("Alineada al Nivel Base", $"No se detectó un colisionador de piso debajo, por lo que la cámara se posicionó a {alt:F2}m sobre el nivel base del escenario (Y = 0).", "Aceptar");
        }
    }

    [MenuItem("Tools/Cámara/Ajustar Altura al Piso/Estándar Ojos (1.65m)", false, 20)]
    public static void SetAltura165() { AplicarAltura(1.65f); }

    [MenuItem("Tools/Cámara/Ajustar Altura al Piso/Elevada (3.0m)", false, 21)]
    public static void SetAltura3() { AplicarAltura(3.0f); }

    [MenuItem("Tools/Cámara/Ajustar Altura al Piso/Vista Alta / Dron (6.0m)", false, 22)]
    public static void SetAltura6() { AplicarAltura(6.0f); }

    [MenuItem("Tools/Cámara/Ajustar Altura al Piso/Vista Aérea (15.0m)", false, 23)]
    public static void SetAltura15() { AplicarAltura(15.0f); }

    [MenuItem("Tools/Cámara/Ajustar Altura al Piso/Vista Panorámica (30.0m)", false, 24)]
    public static void SetAltura30() { AplicarAltura(30.0f); }

    private static void AplicarAltura(float nuevaAltura)
    {
        Camera cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Aviso", "No se encontró ninguna cámara activa.", "Aceptar");
            return;
        }

        CameraMovement mov = cam.GetComponent<CameraMovement>();
        if (mov == null)
        {
            mov = cam.gameObject.AddComponent<CameraMovement>();
        }

        Undo.RecordObject(mov, "Cambiar Altura al Piso");
        mov.DistanciaAlPiso = nuevaAltura;
        EditorUtility.SetDirty(mov);

        // Alinear inmediatamente
        if (Physics.Raycast(cam.transform.position + Vector3.up * (nuevaAltura + 10f), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            Undo.RecordObject(cam.transform, "Alinear Posición Cámara");
            cam.transform.position = new Vector3(cam.transform.position.x, hit.point.y + nuevaAltura, cam.transform.position.z);
            EditorUtility.SetDirty(cam.transform);
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Debug.Log($"[CameraSetupUtility] Distancia al piso establecida en {nuevaAltura:F2}m.");
        EditorUtility.DisplayDialog("Altura Actualizada", $"La distancia de la cámara al piso se configuró a {nuevaAltura:F2} metros.", "Aceptar");
    }

    [MenuItem("Tools/Cámara/Sistemas de Movimiento/Restablecer Preferencias Guardadas", false, 30)]
    public static void ResetMovementPreferences()
    {
        PlayerPrefs.DeleteKey("Movement_SavedPreset");
        PlayerPrefs.DeleteKey("Movement_RememberChoice");
        PlayerPrefs.DeleteKey("Movement_VRSnapTurn");
        PlayerPrefs.DeleteKey("Movement_RightClickLook");
        PlayerPrefs.Save();
        EditorUtility.DisplayDialog("Preferencias Restablecidas", "Las preferencias guardadas del sistema de movimiento han sido eliminadas.\n\nEl selector de movimiento se mostrará automáticamente al iniciar el juego en Play Mode.", "Aceptar");
    }

    [MenuItem("Tools/Cámara/Sistemas de Movimiento/Abrir Selector en Play Mode", false, 31)]
    public static void OpenSelectorInPlayMode()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Aviso", "Esta opción solo funciona durante Play Mode.", "Aceptar");
            return;
        }
        if (MovementSystemSelector.Instance != null)
        {
            MovementSystemSelector.Instance.OpenMenu();
        }
    }
}
#endif

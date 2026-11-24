#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Herramienta de utilidad para añadir colisionadores rápidamente a los modelos 3D de la escena.
/// Permite que la cámara colisione con ellos sin atravesarlos.
/// </summary>
public static class AddCollidersUtility
{
    [MenuItem("Tools/Añadir Colisionadores a Objetos de la Escena", false, 1)]
    public static void AddCollidersToAllMeshes()
    {
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int addedCount = 0;

        foreach (MeshRenderer mr in renderers)
        {
            // Ignorar la cámara o si ya tiene colisionador
            if (mr.GetComponent<Camera>() != null) continue;

            if (mr.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<MeshCollider>(mr.gameObject);
                addedCount++;
            }
        }

        Debug.Log($"[AddCollidersUtility] Se han añadido MeshCollider a {addedCount} objetos en la escena.");
        EditorUtility.DisplayDialog("Colisionadores Añadidos",
            $"Se añadieron con éxito MeshCollider a {addedCount} objetos de la escena.\n\nAhora la cámara chocará contra ellos.", "Aceptar");
    }

    [MenuItem("Tools/Añadir MeshCollider al Objeto Seleccionado", false, 2)]
    public static void AddColliderToSelected()
    {
        if (Selection.activeGameObject == null)
        {
            EditorUtility.DisplayDialog("Aviso", "Por favor selecciona primero un objeto en la jerarquía (Hierarchy).", "Aceptar");
            return;
        }

        MeshRenderer[] renderers = Selection.activeGameObject.GetComponentsInChildren<MeshRenderer>(true);
        int addedCount = 0;

        foreach (MeshRenderer mr in renderers)
        {
            if (mr.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<MeshCollider>(mr.gameObject);
                addedCount++;
            }
        }

        Debug.Log($"[AddCollidersUtility] Se añadieron MeshCollider a {addedCount} elementos en '{Selection.activeGameObject.name}'.");
        EditorUtility.DisplayDialog("Colisionadores Añadidos",
            $"Se añadieron {addedCount} MeshCollider a '{Selection.activeGameObject.name}'.", "Aceptar");
    }
}
#endif

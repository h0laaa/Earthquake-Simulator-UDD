using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Componente que genera y gestiona un piso editable por puntos o pincel.
/// Permite modificar la altura (elevación) en distintos puntos del terreno o piso
/// tanto en el Editor como en tiempo de ejecución, actualizando automáticamente
/// la malla, sus normales y su MeshCollider para física y movimiento de cámara.
/// Puede exportar la geometría resultante como archivo .obj en Assets/Modelos3d/.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[SelectionBase]
public class PisoEditable : MonoBehaviour
{
    public enum TipoSuavizado
    {
        SuaveHermite,
        Lineal,
        CampanaGaussiana,
        Constante
    }

    [Header("Dimensiones del Piso")]
    [Tooltip("Ancho en el eje X (metros)")]
    [Range(2f, 500f)]
    public float ancho = 60f;

    [Tooltip("Largo en el eje Z (metros)")]
    [Range(2f, 500f)]
    public float largo = 60f;

    [Tooltip("Número de divisiones en el eje X")]
    [Range(2, 100)]
    public int subdivisionesX = 30;

    [Tooltip("Número de divisiones en el eje Z")]
    [Range(2, 100)]
    public int subdivisionesZ = 30;

    [Tooltip("Altura base Y del plano")]
    public float alturaBase = 0f;

    [Tooltip("Repetición de la textura de pavimento")]
    public Vector2 tilingUV = new Vector2(10f, 10f);

    [Header("Física y Material")]
    [Tooltip("Actualizar automáticamente el MeshCollider cuando se modifiquen alturas")]
    public bool actualizarColisionador = true;

    [Tooltip("Material para el suelo (por defecto M_Edificio_Suelo)")]
    public Material materialPiso;

    [Header("Datos de Elevación")]
    [SerializeField]
    [HideInInspector]
    private float[] alturas;

    [SerializeField]
    [HideInInspector]
    private int ultimaSubdivisionX = -1;

    [SerializeField]
    [HideInInspector]
    private int ultimaSubdivisionZ = -1;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh mallaInstancia;

    public int VerticesX => subdivisionesX + 1;
    public int VerticesZ => subdivisionesZ + 1;
    public int TotalVertices => VerticesX * VerticesZ;
    public float[] Alturas => alturas;

    private void Awake()
    {
        AsegurarComponentes();
    }

    private void Start()
    {
        AsegurarComponentes();
        if (meshFilter.sharedMesh == null || alturas == null || alturas.Length != TotalVertices)
        {
            GenerarMalla(true);
        }
    }

    private void OnValidate()
    {
        AsegurarComponentes();
        // Si las subdivisiones cambiaron de tamaño, regenerar adaptando alturas
        if (subdivisionesX != ultimaSubdivisionX || subdivisionesZ != ultimaSubdivisionZ)
        {
            GenerarMalla(true);
        }
        else if (meshFilter.sharedMesh != null)
        {
            ActualizarGeometriaMalla();
        }
    }

    public void AsegurarComponentes()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();

        if (actualizarColisionador && meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        if (materialPiso == null && meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            materialPiso = meshRenderer.sharedMaterial;
        }
    }

    /// <summary>
    /// Genera la malla desde cero con la resolución y dimensiones especificadas.
    /// Si preservarAlturas es true y ya existían alturas previas, se mantienen.
    /// </summary>
    public void GenerarMalla(bool preservarAlturas = true)
    {
        AsegurarComponentes();

        int resX = VerticesX;
        int resZ = VerticesZ;
        int totalVerts = resX * resZ;

        float[] alturasAnteriores = alturas;
        int prevSubX = ultimaSubdivisionX;
        int prevSubZ = ultimaSubdivisionZ;

        alturas = new float[totalVerts];
        ultimaSubdivisionX = subdivisionesX;
        ultimaSubdivisionZ = subdivisionesZ;

        // Si ya había datos de elevación y queremos preservarlos
        if (preservarAlturas && alturasAnteriores != null && alturasAnteriores.Length > 0 && prevSubX > 0 && prevSubZ > 0)
        {
            int prevResX = prevSubX + 1;
            int prevResZ = prevSubZ + 1;

            for (int z = 0; z < resZ; z++)
            {
                float uZ = (float)z / subdivisionesZ;
                float oldZ = uZ * prevSubZ;
                int z0 = Mathf.Clamp((int)oldZ, 0, prevSubZ);
                int z1 = Mathf.Clamp(z0 + 1, 0, prevSubZ);
                float fZ = oldZ - z0;

                for (int x = 0; x < resX; x++)
                {
                    float uX = (float)x / subdivisionesX;
                    float oldX = uX * prevSubX;
                    int x0 = Mathf.Clamp((int)oldX, 0, prevSubX);
                    int x1 = Mathf.Clamp(x0 + 1, 0, prevSubX);
                    float fX = oldX - x0;

                    int idx00 = z0 * prevResX + x0;
                    int idx10 = z0 * prevResX + x1;
                    int idx01 = z1 * prevResX + x0;
                    int idx11 = z1 * prevResX + x1;

                    float h00 = idx00 < alturasAnteriores.Length ? alturasAnteriores[idx00] : 0f;
                    float h10 = idx10 < alturasAnteriores.Length ? alturasAnteriores[idx10] : 0f;
                    float h01 = idx01 < alturasAnteriores.Length ? alturasAnteriores[idx01] : 0f;
                    float h11 = idx11 < alturasAnteriores.Length ? alturasAnteriores[idx11] : 0f;

                    float hBottom = Mathf.Lerp(h00, h10, fX);
                    float hTop = Mathf.Lerp(h01, h11, fX);
                    alturas[z * resX + x] = Mathf.Lerp(hBottom, hTop, fZ);
                }
            }
        }

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        int totalQuads = subdivisionesX * subdivisionesZ;
        int[] triangles = new int[totalQuads * 6];

        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float inicioX = -ancho * 0.5f;
        float inicioZ = -largo * 0.5f;

        for (int z = 0; z < resZ; z++)
        {
            float posZ = inicioZ + z * pasoZ;
            float uvV = ((float)z / subdivisionesZ) * tilingUV.y;

            for (int x = 0; x < resX; x++)
            {
                int index = z * resX + x;
                float posX = inicioX + x * pasoX;
                float posY = alturaBase + alturas[index];
                float uvU = ((float)x / subdivisionesX) * tilingUV.x;

                vertices[index] = new Vector3(posX, posY, posZ);
                uvs[index] = new Vector2(uvU, uvV);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < subdivisionesZ; z++)
        {
            for (int x = 0; x < subdivisionesX; x++)
            {
                int v00 = z * resX + x;
                int v10 = z * resX + (x + 1);
                int v01 = (z + 1) * resX + x;
                int v11 = (z + 1) * resX + (x + 1);

                // Triángulo 1
                triangles[triIndex++] = v00;
                triangles[triIndex++] = v01;
                triangles[triIndex++] = v11;

                // Triángulo 2
                triangles[triIndex++] = v00;
                triangles[triIndex++] = v11;
                triangles[triIndex++] = v10;
            }
        }

        if (mallaInstancia == null)
        {
            mallaInstancia = new Mesh();
            mallaInstancia.name = "PisoEditable_Mesh";
        }
        else
        {
            mallaInstancia.Clear();
        }

        mallaInstancia.vertices = vertices;
        mallaInstancia.uv = uvs;
        mallaInstancia.triangles = triangles;
        mallaInstancia.RecalculateNormals();
        mallaInstancia.RecalculateTangents();
        mallaInstancia.RecalculateBounds();

        meshFilter.sharedMesh = mallaInstancia;

        if (actualizarColisionador && meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mallaInstancia;
        }

        if (materialPiso != null && meshRenderer != null)
        {
            meshRenderer.sharedMaterial = materialPiso;
        }
    }

    /// <summary>
    /// Inicializa las dimensiones y configuración del piso tomando como referencia una malla existente.
    /// </summary>
    public void InicializarDesdeMalla(Mesh meshOriginal)
    {
        if (meshOriginal == null) return;
        AsegurarComponentes();

        Bounds b = meshOriginal.bounds;
        ancho = Mathf.Max(2f, b.size.x);
        largo = Mathf.Max(2f, b.size.z);
        alturaBase = b.center.y;

        int total = meshOriginal.vertexCount;
        int lado = Mathf.RoundToInt(Mathf.Sqrt(total));
        if (lado * lado == total && lado > 1)
        {
            subdivisionesX = Mathf.Clamp(lado - 1, 4, 80);
            subdivisionesZ = Mathf.Clamp(lado - 1, 4, 80);
        }

        GenerarMalla(false);
    }

    /// <summary>
    /// Actualiza únicamente las posiciones Y de los vértices y recalcula normales y colisión,
    /// sin reconstruir la topología de triángulos (muy eficiente en tiempo real al pintar o arrastrar).
    /// </summary>
    public void ActualizarGeometriaMalla()
    {
        AsegurarComponentes();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            GenerarMalla(true);
            return;
        }

        int resX = VerticesX;
        int resZ = VerticesZ;
        int totalVerts = resX * resZ;

        if (alturas == null || alturas.Length != totalVerts)
        {
            GenerarMalla(true);
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        if (vertices.Length != totalVerts)
        {
            GenerarMalla(true);
            return;
        }

        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float inicioX = -ancho * 0.5f;
        float inicioZ = -largo * 0.5f;

        for (int z = 0; z < resZ; z++)
        {
            float posZ = inicioZ + z * pasoZ;
            for (int x = 0; x < resX; x++)
            {
                int index = z * resX + x;
                float posX = inicioX + x * pasoX;
                float posY = alturaBase + alturas[index];
                vertices[index] = new Vector3(posX, posY, posZ);
            }
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        if (actualizarColisionador && meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }

    #region Consultas de Vértices y Coordenadas

    public int GetIndice(int x, int z)
    {
        if (x < 0 || x >= VerticesX || z < 0 || z >= VerticesZ) return -1;
        return z * VerticesX + x;
    }

    public Vector3 GetPosicionLocal(int x, int z)
    {
        int resX = VerticesX;
        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float posX = -ancho * 0.5f + x * pasoX;
        float posZ = -largo * 0.5f + z * pasoZ;

        int index = z * resX + x;
        float y = alturaBase + (alturas != null && index < alturas.Length ? alturas[index] : 0f);
        return new Vector3(posX, y, posZ);
    }

    public Vector3 GetPosicionMundo(int x, int z)
    {
        return transform.TransformPoint(GetPosicionLocal(x, z));
    }

    public float GetAltura(int x, int z)
    {
        int idx = GetIndice(x, z);
        if (idx >= 0 && alturas != null && idx < alturas.Length)
        {
            return alturas[idx];
        }
        return 0f;
    }

    public void SetAltura(int x, int z, float nuevaAltura)
    {
        int idx = GetIndice(x, z);
        if (idx >= 0 && alturas != null && idx < alturas.Length)
        {
            alturas[idx] = nuevaAltura;
        }
    }

    #endregion

    #region Modificación de Alturas por Puntos y Pincel

    /// <summary>
    /// Modifica la elevación en una zona circular alrededor de una posición de mundo dada.
    /// delta puede ser positivo (elevar) o negativo (bajar).
    /// </summary>
    public void ModificarAlturaEnPunto(Vector3 posicionMundo, float radio, float delta, TipoSuavizado falloff = TipoSuavizado.SuaveHermite)
    {
        if (alturas == null) return;

        Vector3 localCenter = transform.InverseTransformPoint(posicionMundo);
        float radioCuad = radio * radio;

        int resX = VerticesX;
        int resZ = VerticesZ;
        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float inicioX = -ancho * 0.5f;
        float inicioZ = -largo * 0.5f;

        // Bounding box en índices para optimizar
        int minX = Mathf.Clamp(Mathf.FloorToInt((localCenter.x - radio - inicioX) / pasoX), 0, subdivisionesX);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((localCenter.x + radio - inicioX) / pasoX), 0, subdivisionesX);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((localCenter.z - radio - inicioZ) / pasoZ), 0, subdivisionesZ);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt((localCenter.z + radio - inicioZ) / pasoZ), 0, subdivisionesZ);

        for (int z = minZ; z <= maxZ; z++)
        {
            float posZ = inicioZ + z * pasoZ;
            float dz = posZ - localCenter.z;

            for (int x = minX; x <= maxX; x++)
            {
                float posX = inicioX + x * pasoX;
                float dx = posX - localCenter.x;
                float distCuad = dx * dx + dz * dz;

                if (distCuad <= radioCuad)
                {
                    float dist = Mathf.Sqrt(distCuad);
                    float factor = CalcularFactorFalloff(dist, radio, falloff);
                    int idx = z * resX + x;
                    alturas[idx] += delta * factor;
                }
            }
        }
    }

    /// <summary>
    /// Aplana gradualmente los vértices dentro del radio hacia una altura objetivo dada.
    /// </summary>
    public void AplanarEnPunto(Vector3 posicionMundo, float radio, float alturaObjetivo, float fuerza)
    {
        if (alturas == null) return;

        Vector3 localCenter = transform.InverseTransformPoint(posicionMundo);
        float radioCuad = radio * radio;

        int resX = VerticesX;
        int resZ = VerticesZ;
        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float inicioX = -ancho * 0.5f;
        float inicioZ = -largo * 0.5f;

        int minX = Mathf.Clamp(Mathf.FloorToInt((localCenter.x - radio - inicioX) / pasoX), 0, subdivisionesX);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((localCenter.x + radio - inicioX) / pasoX), 0, subdivisionesX);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((localCenter.z - radio - inicioZ) / pasoZ), 0, subdivisionesZ);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt((localCenter.z + radio - inicioZ) / pasoZ), 0, subdivisionesZ);

        for (int z = minZ; z <= maxZ; z++)
        {
            float posZ = inicioZ + z * pasoZ;
            float dz = posZ - localCenter.z;

            for (int x = minX; x <= maxX; x++)
            {
                float posX = inicioX + x * pasoX;
                float dx = posX - localCenter.x;
                float distCuad = dx * dx + dz * dz;

                if (distCuad <= radioCuad)
                {
                    float dist = Mathf.Sqrt(distCuad);
                    float factor = CalcularFactorFalloff(dist, radio, TipoSuavizado.SuaveHermite);
                    int idx = z * resX + x;
                    float blend = Mathf.Clamp01(fuerza * factor);
                    alturas[idx] = Mathf.Lerp(alturas[idx], alturaObjetivo, blend);
                }
            }
        }
    }

    /// <summary>
    /// Suaviza los desniveles dentro del radio calculando el promedio con los vecinos circundantes.
    /// </summary>
    public void SuavizarEnPunto(Vector3 posicionMundo, float radio, float fuerza)
    {
        if (alturas == null) return;

        Vector3 localCenter = transform.InverseTransformPoint(posicionMundo);
        float radioCuad = radio * radio;

        int resX = VerticesX;
        int resZ = VerticesZ;
        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;
        float inicioX = -ancho * 0.5f;
        float inicioZ = -largo * 0.5f;

        int minX = Mathf.Clamp(Mathf.FloorToInt((localCenter.x - radio - inicioX) / pasoX), 0, subdivisionesX);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((localCenter.x + radio - inicioX) / pasoX), 0, subdivisionesX);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((localCenter.z - radio - inicioZ) / pasoZ), 0, subdivisionesZ);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt((localCenter.z + radio - inicioZ) / pasoZ), 0, subdivisionesZ);

        float[] alturasCopia = (float[])alturas.Clone();

        for (int z = minZ; z <= maxZ; z++)
        {
            float posZ = inicioZ + z * pasoZ;
            float dz = posZ - localCenter.z;

            for (int x = minX; x <= maxX; x++)
            {
                float posX = inicioX + x * pasoX;
                float dx = posX - localCenter.x;
                float distCuad = dx * dx + dz * dz;

                if (distCuad <= radioCuad)
                {
                    float dist = Mathf.Sqrt(distCuad);
                    float factor = CalcularFactorFalloff(dist, radio, TipoSuavizado.SuaveHermite);

                    // Promediar con vecinos 3x3
                    float suma = 0f;
                    int conteo = 0;
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        int nz = z + oz;
                        if (nz < 0 || nz >= resZ) continue;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= resX) continue;
                            suma += alturasCopia[nz * resX + nx];
                            conteo++;
                        }
                    }

                    if (conteo > 0)
                    {
                        float promedio = suma / conteo;
                        int idx = z * resX + x;
                        float blend = Mathf.Clamp01(fuerza * factor);
                        alturas[idx] = Mathf.Lerp(alturas[idx], promedio, blend);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Suaviza toda la superficie del piso para eliminar picos abruptos.
    /// </summary>
    public void SuavizarTodo(float fuerza = 0.5f)
    {
        if (alturas == null) return;
        int resX = VerticesX;
        int resZ = VerticesZ;
        float[] copia = (float[])alturas.Clone();

        for (int z = 0; z < resZ; z++)
        {
            for (int x = 0; x < resX; x++)
            {
                float suma = 0f;
                int count = 0;
                for (int oz = -1; oz <= 1; oz++)
                {
                    int nz = z + oz;
                    if (nz < 0 || nz >= resZ) continue;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = x + ox;
                        if (nx < 0 || nx >= resX) continue;
                        suma += copia[nz * resX + nx];
                        count++;
                    }
                }

                if (count > 0)
                {
                    float prom = suma / count;
                    int idx = z * resX + x;
                    alturas[idx] = Mathf.Lerp(alturas[idx], prom, Mathf.Clamp01(fuerza));
                }
            }
        }

        ActualizarGeometriaMalla();
    }

    /// <summary>
    /// Aplana completamente el piso a una altura fija (por defecto 0).
    /// </summary>
    public void AplanarTodo(float nuevaAltura = 0f)
    {
        if (alturas == null || alturas.Length != TotalVertices)
        {
            alturas = new float[TotalVertices];
        }

        for (int i = 0; i < alturas.Length; i++)
        {
            alturas[i] = nuevaAltura;
        }

        ActualizarGeometriaMalla();
    }

    /// <summary>
    /// Genera ondulaciones o pendientes naturales usando Ruido de Perlin.
    /// </summary>
    public void GenerarRelievePerlin(float escalaRuido = 0.08f, float amplitud = 1.5f, float semilla = 0f)
    {
        if (alturas == null || alturas.Length != TotalVertices)
        {
            alturas = new float[TotalVertices];
        }

        int resX = VerticesX;
        int resZ = VerticesZ;
        float pasoX = ancho / subdivisionesX;
        float pasoZ = largo / subdivisionesZ;

        for (int z = 0; z < resZ; z++)
        {
            float coordZ = (z * pasoZ + semilla) * escalaRuido;
            for (int x = 0; x < resX; x++)
            {
                float coordX = (x * pasoX + semilla) * escalaRuido;
                float perlin = Mathf.PerlinNoise(coordX, coordZ);
                // Centrado en 0 (-0.5 a +0.5)
                float elevacion = (perlin - 0.5f) * 2f * amplitud;
                alturas[z * resX + x] = elevacion;
            }
        }

        ActualizarGeometriaMalla();
    }

    private float CalcularFactorFalloff(float dist, float radio, TipoSuavizado tipo)
    {
        if (radio <= 0.0001f) return 1f;
        float t = Mathf.Clamp01(1f - (dist / radio));

        switch (tipo)
        {
            case TipoSuavizado.SuaveHermite:
                return t * t * (3f - 2f * t); // Smoothstep estándar
            case TipoSuavizado.CampanaGaussiana:
                float dNorm = dist / radio;
                return Mathf.Exp(-3f * dNorm * dNorm);
            case TipoSuavizado.Constante:
                return 1f;
            case TipoSuavizado.Lineal:
            default:
                return t;
        }
    }

    #endregion

    #region Exportación a Modelo 3D (.OBJ) en Modelos3d

    /// <summary>
    /// Exporta la malla actual con todas sus elevaciones a un archivo .obj Wavefront
    /// en la carpeta de Assets/Modelos3d/ para que quede disponible como modelo 3D independiente.
    /// </summary>
    public string ExportarAObj(string rutaRelativa = "Assets/Modelos3d/PisoEditable.obj")
    {
        ActualizarGeometriaMalla();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            GenerarMalla(true);
        }

        Mesh mesh = meshFilter.sharedMesh;
        string fullPath = Path.Combine(Application.dataPath, "..", rutaRelativa);
        fullPath = Path.GetFullPath(fullPath);

        string carpeta = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        string nombreModelo = Path.GetFileNameWithoutExtension(rutaRelativa);
        string mtlFileName = nombreModelo + ".mtl";
        string mtlFullPath = Path.Combine(carpeta, mtlFileName);

        // 1. Escribir archivo MTL
        StringBuilder mtlContent = new StringBuilder();
        mtlContent.AppendLine("# MTL para " + nombreModelo);
        mtlContent.AppendLine("newmtl M_Edificio_Suelo");
        mtlContent.AppendLine("Ka 1.0000 1.0000 1.0000");
        mtlContent.AppendLine("Kd 0.9000 0.9000 0.9000");
        mtlContent.AppendLine("Ks 0.0000 0.0000 0.0000");
        mtlContent.AppendLine("Ns 10.0000");
        mtlContent.AppendLine("d 1.0");
        mtlContent.AppendLine("illum 2");
        mtlContent.AppendLine("map_Kd ../Materials/T_Suelo_Pavimento.png");
        File.WriteAllText(mtlFullPath, mtlContent.ToString());

        // 2. Escribir archivo OBJ
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"# Modelo 3D de Piso con Altura Editable");
        sb.AppendLine($"# Exportado desde Unity - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"mtllib {mtlFileName}");
        sb.AppendLine($"o {nombreModelo}");

        Vector3[] verts = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Vector3[] norms = mesh.normals;
        int[] tris = mesh.triangles;

        // Vértices (v x y z)
        // En formato OBJ estándar para Unity (inversión de Z para coordenadas de mano derecha)
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            sb.AppendLine($"v {v.x:F5} {v.y:F5} {v.z:F5}");
        }

        // UVs (vt u v)
        for (int i = 0; i < uvs.Length; i++)
        {
            Vector2 uv = uvs[i];
            sb.AppendLine($"vt {uv.x:F5} {uv.y:F5}");
        }

        // Normales (vn x y z)
        for (int i = 0; i < norms.Length; i++)
        {
            Vector3 n = norms[i];
            sb.AppendLine($"vn {n.x:F5} {n.y:F5} {n.z:F5}");
        }

        sb.AppendLine("usemtl M_Edificio_Suelo");
        sb.AppendLine("s 1");

        // Caras (f v/vt/vn v/vt/vn v/vt/vn)
        for (int i = 0; i < tris.Length; i += 3)
        {
            int i0 = tris[i] + 1;
            int i1 = tris[i + 1] + 1;
            int i2 = tris[i + 2] + 1;

            sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
        }

        File.WriteAllText(fullPath, sb.ToString());

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.ImportAsset(rutaRelativa, UnityEditor.ImportAssetOptions.ForceUpdate);
        UnityEditor.AssetDatabase.Refresh();
#endif

        Debug.Log($"[PisoEditable] Modelo 3D guardado exitosamente en: {rutaRelativa}");
        return fullPath;
    }

    #endregion
}

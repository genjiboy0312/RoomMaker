using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// OpenCV <-> Texture2D 변환 유틸
/// </summary>
public static class OpenCVTextureUtils
{
    public static Mat Texture2DToMat(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        Mat mat = new Mat(tex.height, tex.width, CvType.CV_8UC4);
        mat.put(0, 0, Color32ArrayToByteArray(pixels));
        return mat;
    }

    public static Texture2D MatToTexture2D(Mat mat)
    {
        int width = mat.cols();
        int height = mat.rows();
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        byte[] data = new byte[width * height * 4];
        mat.get(0, 0, data);
        tex.LoadRawTextureData(data);
        tex.Apply();
        return tex;
    }

    private static byte[] Color32ArrayToByteArray(Color32[] colors)
    {
        byte[] bytes = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            bytes[i * 4] = colors[i].r;
            bytes[i * 4 + 1] = colors[i].g;
            bytes[i * 4 + 2] = colors[i].b;
            bytes[i * 4 + 3] = colors[i].a;
        }
        return bytes;
    }
}

/// <summary>
/// RoomMaker : OpenCV + FloodFill 기반 벽/바닥 자동 생성
/// </summary>
public class RoomMaker : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private RawImage _floorPlanImage;
    [SerializeField] private Vector2 _planeSize = new Vector2(10f, 10f);
    [SerializeField] private Color _wallColor = Color.red;
    [SerializeField] private Color _floorColor = Color.gray;
    [SerializeField] private Color _innerColor = Color.blue;
    [SerializeField] private float _wallHeight = 3f;
    [SerializeField] private float _wallRiseSpeed = 1f;
    [SerializeField] private float _minRoomPixel = 50; // 최소 픽셀수

    [SerializeField] private FlaskClient _flaskClient;

    private Texture2D _texCopy;
    private float[,] _wallMap;
    private List<Vector2[]> _segments;

    #region Unity Lifecycle
    private void Start()
    {
        if (_floorPlanImage == null || _floorPlanImage.texture == null)
        {
            Debug.LogError("FloorPlan RawImage 또는 텍스처가 설정되지 않았습니다.");
            return;
        }

        CopyTexture();
        CreateWallMap();
        ColorFloorInsideWalls();
        ColorInnerWallEdges();

        _segments = DetectWallSegmentsOpenCV(_texCopy);

        GameObject floorPlanGO = new GameObject("FloorPlan");
        floorPlanGO.transform.position = Vector3.zero;

        // 바닥 생성
        GameObject floorGroup = CreateRoomFloors(_texCopy, floorPlanGO.transform);

        // 기존 HoughLines 기반 벽 생성
        GameObject mergedWalls = GenerateWallsFromSegments(_segments, floorPlanGO.transform);

        // _innerColor 경계선 기반 벽 생성
        GameObject innerEdgeWalls = GenerateInnerEdgeWalls(floorPlanGO.transform, _segments);

        SendProjectDataToServer();

        FloorData floorData = GenerateFloorDataFromTexture(_texCopy);
        SaveAndSendFloorData(floorData);

    }
    #endregion

    #region Texture 처리
    private void CopyTexture()
    {
        Texture2D srcTex = (Texture2D)_floorPlanImage.texture;
        _texCopy = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGBA32, false);
        _texCopy.SetPixels32(srcTex.GetPixels32());
        _texCopy.Apply();
    }

    private void CreateWallMap()
    {
        int w = _texCopy.width;
        int h = _texCopy.height;
        _wallMap = new float[w, h];
        Color32[] pixels = _texCopy.GetPixels32();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c = pixels[y * w + x];

                if (c.r < 50 && c.g < 50 && c.b < 50)
                    _wallMap[x, y] = 1;
                else if (c.r > 200 && c.g < 50 && c.b < 50)
                    _wallMap[x, y] = 2;
                else if (Mathf.Abs(c.r - c.g) < 10 && Mathf.Abs(c.g - c.b) < 10 && c.r < 150)
                    _wallMap[x, y] = 1;
                else
                    _wallMap[x, y] = 0;
            }
        }
    }

    private void ColorFloorInsideWalls()
    {
        int w = _texCopy.width;
        int h = _texCopy.height;
        Color32[] pixels = _texCopy.GetPixels32();
        bool[,] visited = new bool[w, h];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        // 외곽 FloodFill
        for (int i = 0; i < w; i++)
        {
            if (!visited[i, 0]) { queue.Enqueue(new Vector2Int(i, 0)); visited[i, 0] = true; }
            if (!visited[i, h - 1]) { queue.Enqueue(new Vector2Int(i, h - 1)); visited[i, h - 1] = true; }
        }
        for (int j = 0; j < h; j++)
        {
            if (!visited[0, j]) { queue.Enqueue(new Vector2Int(0, j)); visited[0, j] = true; }
            if (!visited[w - 1, j]) { queue.Enqueue(new Vector2Int(w - 1, j)); visited[w - 1, j] = true; }
        }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = p.x + dx[i];
                int ny = p.y + dy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (visited[nx, ny] || _wallMap[nx, ny] == 1) continue;
                visited[nx, ny] = true;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        bool[,] filled = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (_wallMap[x, y] != 1 && !visited[x, y] && !filled[x, y])
                {
                    Queue<Vector2Int> rq = new Queue<Vector2Int>();
                    List<Vector2Int> region = new List<Vector2Int>();
                    rq.Enqueue(new Vector2Int(x, y));
                    filled[x, y] = true;

                    while (rq.Count > 0)
                    {
                        var p = rq.Dequeue();
                        region.Add(p);
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = p.x + dx[i], ny = p.y + dy[i];
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (filled[nx, ny] || visited[nx, ny] || _wallMap[nx, ny] == 1) continue;
                            rq.Enqueue(new Vector2Int(nx, ny));
                            filled[nx, ny] = true;
                        }
                    }

                    if (region.Count >= _minRoomPixel)
                    {
                        foreach (var p in region)
                        {
                            pixels[p.y * w + p.x] = _floorColor;
                            _wallMap[p.x, p.y] = 2;
                        }
                    }
                }
            }
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (_wallMap[x, y] == 1)
                    pixels[y * w + x] = _wallColor;

        _texCopy.SetPixels32(pixels);
        _texCopy.Apply();
        _floorPlanImage.texture = _texCopy;
    }

    private void ColorInnerWallEdges()
    {
        int w = _texCopy.width;
        int h = _texCopy.height;
        Color32[] pixels = _texCopy.GetPixels32();
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int tolerance = 20;

        bool[,] markInner = new bool[w, h];
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (IsColorClose(pixels[y * w + x], _floorColor, tolerance))
                {
                    for (int i = 0; i < dx.Length; i++)
                    {
                        int nx = x + dx[i];
                        int ny = y + dy[i];
                        if (IsColorClose(pixels[ny * w + nx], _wallColor, tolerance))
                        {
                            markInner[x, y] = true;
                            break;
                        }
                    }
                }
            }
        }

        Color32[] resultPixels = (Color32[])pixels.Clone();
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (markInner[x, y])
                {
                    resultPixels[y * w + x] = _innerColor;

                    for (int i = 0; i < dx.Length; i++)
                    {
                        int nx = x + dx[i];
                        int ny = y + dy[i];
                        if (IsColorClose(pixels[ny * w + nx], _floorColor, tolerance))
                            resultPixels[ny * w + nx] = _innerColor;
                    }
                }
            }
        }

        _texCopy.SetPixels32(resultPixels);
        _texCopy.Apply();
        _floorPlanImage.texture = _texCopy;
    }

    private bool IsColorClose(Color32 c1, Color c2, int tolerance)
    {
        float r = c1.r - (c2.r * 255f);
        float g = c1.g - (c2.g * 255f);
        float b = c1.b - (c2.b * 255f);

        float distance = Mathf.Sqrt(r * r + g * g + b * b);
        return distance <= tolerance;
    }

    private bool IsColorClose(Color32 c1, Color32 c2, int tolerance)
    {
        return Mathf.Abs(c1.r - c2.r) <= tolerance &&
               Mathf.Abs(c1.g - c2.g) <= tolerance &&
               Mathf.Abs(c1.b - c2.b) <= tolerance;
    }
    #endregion

    #region Floor / Wall 생성
    private List<Vector2[]> DetectWallSegmentsOpenCV(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        bool[,] visited = new bool[w, h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (_wallMap[x, y] != 1 || visited[x, y]) continue;
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                List<Vector2Int> region = new List<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[x, y] = true;

                int[] dx = { 1, -1, 0, 0 };
                int[] dy = { 0, 0, 1, -1 };

                while (q.Count > 0)
                {
                    Vector2Int p = q.Dequeue();
                    region.Add(p);
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = p.x + dx[i];
                        int ny = p.y + dy[i];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        if (visited[nx, ny] || _wallMap[nx, ny] != 1) continue;
                        q.Enqueue(new Vector2Int(nx, ny));
                        visited[nx, ny] = true;
                    }
                }

                if (region.Count < _minRoomPixel)
                {
                    foreach (var p in region)
                        _wallMap[p.x, p.y] = 0;
                }
            }
        }

        Mat mat = OpenCVTextureUtils.Texture2DToMat(tex);
        Imgproc.cvtColor(mat, mat, Imgproc.COLOR_RGBA2GRAY);

        byte[] data = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                data[y * w + x] = (_wallMap[x, y] == 1) ? (byte)255 : (byte)0;

        mat.put(0, 0, data);

        Mat lines = new Mat();
        Imgproc.HoughLinesP(mat, lines, 1, Mathf.PI / 180, 40, 5, 10);

        List<Vector2[]> segs = new List<Vector2[]>();
        for (int i = 0; i < lines.rows(); i++)
        {
            int[] d = new int[4];
            lines.get(i, 0, d);
            segs.Add(new Vector2[] { new Vector2(d[0], d[1]), new Vector2(d[2], d[3]) });
        }

        return segs;
    }

    private GameObject CreateRoomFloors(Texture2D tex, Transform parent)
    {
        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        bool[,] visited = new bool[w, h];

        float floorHeight = 0.05f;
        float scaleX = _planeSize.x / w;
        float scaleZ = _planeSize.y / h;

        GameObject floorGroup = new GameObject("FloorGroups");
        floorGroup.transform.SetParent(parent, false);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        int roomIdx = 0;

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(tmp);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y] || _wallMap[x, y] != 2) continue;

                Queue<Vector2Int> q = new Queue<Vector2Int>();
                List<Vector2Int> area = new List<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[x, y] = true;

                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    area.Add(p);
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = p.x + dx[i], ny = p.y + dy[i];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        if (visited[nx, ny] || _wallMap[nx, ny] != 2) continue;
                        visited[nx, ny] = true;
                        q.Enqueue(new Vector2Int(nx, ny));
                    }
                }

                if (area.Count < _minRoomPixel) continue;

                float avgX = (float)area.Average(p => p.x);
                float avgY = (float)area.Average(p => p.y);
                Vector3 center = new Vector3((avgX / w - 0.5f) * _planeSize.x, floorHeight * 0.5f, (avgY / h - 0.5f) * _planeSize.y);

                List<CombineInstance> combines = new List<CombineInstance>();
                foreach (var p in area)
                {
                    Vector3 pixelPos = new Vector3((p.x + 0.5f) * scaleX - _planeSize.x * 0.5f,
                                                   -floorHeight * 0.5f,
                                                   (p.y + 0.5f) * scaleZ - _planeSize.y * 0.5f);
                    Vector3 localPos = pixelPos - new Vector3(center.x, 0, center.z);

                    combines.Add(new CombineInstance
                    {
                        mesh = cubeMesh,
                        transform = Matrix4x4.TRS(localPos, Quaternion.identity, new Vector3(scaleX, floorHeight, scaleZ))
                    });
                }

                Mesh floorMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                floorMesh.CombineMeshes(combines.ToArray(), true, true);
                floorMesh.RecalculateNormals();
                floorMesh.RecalculateBounds();

                GameObject floorGO = new GameObject($"Floor_{roomIdx:D2}");
                floorGO.transform.SetParent(floorGroup.transform, false);
                floorGO.transform.localPosition = center;

                var mf = floorGO.AddComponent<MeshFilter>();
                mf.sharedMesh = floorMesh;
                var mr = floorGO.AddComponent<MeshRenderer>();
                mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = _floorColor };

                roomIdx++;
            }
        }

        return floorGroup;
    }

    private GameObject GenerateWallsFromSegments(List<Vector2[]> segments, Transform parent)
    {
        if (segments == null || segments.Count == 0) return null;

        float scaleX = _planeSize.x / _texCopy.width;
        float scaleZ = _planeSize.y / _texCopy.height;
        float wallThickness = Mathf.Max(0.3f, Mathf.Min(scaleX, scaleZ));
        float wallYoffset = _wallHeight * 0.5f;

        GameObject merged = new GameObject("MergedWalls");
        merged.transform.SetParent(parent, false);

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(tmp);

        List<CombineInstance> combines = new List<CombineInstance>();
        List<Vector3> centers = new List<Vector3>();

        foreach (var seg in segments)
        {
            Vector3 p0 = new Vector3(seg[0].x * scaleX - _planeSize.x * 0.5f, 0f, seg[0].y * scaleZ - _planeSize.y * 0.5f);
            Vector3 p1 = new Vector3(seg[1].x * scaleX - _planeSize.x * 0.5f, 0f, seg[1].y * scaleZ - _planeSize.y * 0.5f);
            centers.Add((p0 + p1) * 0.5f);
        }

        Vector3 avgCenter = centers.Count > 0 ? Vector3.zero : Vector3.zero;
        if (centers.Count > 0)
        {
            foreach (var c in centers) avgCenter += c;
            avgCenter /= centers.Count;
        }

        foreach (var seg in segments)
        {
            Vector3 p0 = new Vector3(seg[0].x * scaleX - _planeSize.x * 0.5f, 0f, seg[0].y * scaleZ - _planeSize.y * 0.5f);
            Vector3 p1 = new Vector3(seg[1].x * scaleX - _planeSize.x * 0.5f, 0f, seg[1].y * scaleZ - _planeSize.y * 0.5f);
            Vector3 dir = p1 - p0;
            float len = dir.magnitude;
            if (len < 0.01f) continue;

            Vector3 mid = (p0 + p1) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 localMid = mid - avgCenter;

            combines.Add(new CombineInstance
            {
                mesh = cubeMesh,
                transform = Matrix4x4.TRS(localMid + Vector3.up * wallYoffset, rot, new Vector3(wallThickness, _wallHeight, len))
            });
        }

        if (combines.Count > 0)
        {
            Mesh combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.CombineMeshes(combines.ToArray(), true, true);
            combined.RecalculateNormals();
            combined.RecalculateBounds();

            var mf = merged.AddComponent<MeshFilter>();
            mf.sharedMesh = combined;
            var mr = merged.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };

            merged.transform.localPosition = avgCenter;
            StartCoroutine(RiseWalls(merged.transform));
        }
        else
        {
            DestroyImmediate(merged);
            return null;
        }

        return merged;
    }
    private GameObject GenerateInnerEdgeWalls(Transform parent, List<Vector2[]> mergedSegments)
    {
        int w = _texCopy.width;
        int h = _texCopy.height;
        Color32[] pixels = _texCopy.GetPixels32();

        float scaleX = _planeSize.x / w;
        float scaleZ = _planeSize.y / h;
        float wallThickness = Mathf.Max(0.1f, Mathf.Min(scaleX, scaleZ));
        float wallYoffset = _wallHeight * 0.5f;
        int tolerance = 5;

        GameObject group = new GameObject("InnerEdgeWalls");
        group.transform.SetParent(parent, false);

        Material wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.red };

        int wallCounter = 1;

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        bool[,] visited = new bool[w, h];

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (visited[x, y]) continue;
                if (!pixels[y * w + x].Equals(_innerColor)) continue;

                bool nearWall = false;
                for (int ny = y - 1; ny <= y + 1 && !nearWall; ny++)
                {
                    for (int nx = x - 1; nx <= x + 1; nx++)
                    {
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        if (IsColorClose(pixels[ny * w + nx], _wallColor, tolerance))
                        {
                            nearWall = true;
                            break;
                        }
                    }
                }
                if (nearWall) continue;

                // BFS로 붙어있는 영역 찾기
                List<Vector2Int> cluster = new List<Vector2Int>();
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[x, y] = true;

                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    cluster.Add(cur);

                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nx = cur.x + dx[dir];
                        int ny = cur.y + dy[dir];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        if (visited[nx, ny]) continue;
                        if (pixels[ny * w + nx].Equals(_innerColor))
                        {
                            visited[nx, ny] = true;
                            q.Enqueue(new Vector2Int(nx, ny));
                        }
                    }
                }

                if (cluster.Count == 0) continue;

                // MergedWall 생성
                GameObject mergedWall = new GameObject($"MergedWall_{wallCounter:00}");
                mergedWall.transform.SetParent(group.transform, false);
                wallCounter++;

                // Cluster를 Cube로 만들고 Mesh 합치기
                List<GameObject> tempCubes = new List<GameObject>();
                foreach (var p in cluster)
                {
                    Vector3 pos = new Vector3(p.x * scaleX - _planeSize.x * 0.5f, 0f, p.y * scaleZ - _planeSize.y * 0.5f);
                    Vector3 worldPos = pos + Vector3.up * wallYoffset;

                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(mergedWall.transform, false);
                    cube.transform.localPosition = worldPos;
                    cube.transform.localRotation = Quaternion.identity;
                    cube.transform.localScale = new Vector3(wallThickness, _wallHeight, wallThickness);
                    cube.GetComponent<MeshRenderer>().material = wallMat;

                    tempCubes.Add(cube);
                }

                // Mesh 합치기
                MeshFilter[] meshFilters = mergedWall.GetComponentsInChildren<MeshFilter>();
                CombineInstance[] combine = new CombineInstance[meshFilters.Length];
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    combine[i].mesh = meshFilters[i].sharedMesh;
                    combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
                }

                Mesh combinedMesh = new Mesh();
                combinedMesh.CombineMeshes(combine);

                // 자식 Cube 삭제
                for (int i = mergedWall.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(mergedWall.transform.GetChild(i).gameObject);

                // 합쳐진 Mesh 적용
                var mf = mergedWall.AddComponent<MeshFilter>();
                mf.sharedMesh = combinedMesh;
                var mr = mergedWall.AddComponent<MeshRenderer>();
                mr.material = wallMat;

                // 피봇 중앙 이동
                Vector3 center = mf.sharedMesh.bounds.center;
                Vector3 offset = center;

                var verts = mf.sharedMesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                    verts[i] -= offset;
                mf.sharedMesh.vertices = verts;
                mf.sharedMesh.RecalculateBounds();

                mergedWall.transform.localPosition += offset;

                // 방향별 자식 Mesh 생성
                SplitMeshByDirection(mergedWall);
            }
        }

        if (group.transform.childCount == 0)
        {
            DestroyImmediate(group);
            return null;
        }

        StartCoroutine(RiseWalls(group.transform));
        return group;
    }

    // 방향별 Mesh 분리
    private void SplitMeshByDirection(GameObject mergedWall)
    {
        MeshFilter mf = mergedWall.GetComponent<MeshFilter>();
        if (mf == null) return;

        Mesh originalMesh = mf.sharedMesh;
        Vector3[] vertices = originalMesh.vertices;
        int[] triangles = originalMesh.triangles;
        Vector3[] normals = originalMesh.normals;

        Dictionary<string, List<int>> dirTris = new Dictionary<string, List<int>>()
    {
        { "Up", new List<int>() },
        { "Down", new List<int>() },
        { "Forward", new List<int>() },
        { "Back", new List<int>() }
    };

        // 삼각형 단위로 분류
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 n0 = normals[triangles[i]];
            Vector3 n1 = normals[triangles[i + 1]];
            Vector3 n2 = normals[triangles[i + 2]];

            Vector3 avgNormal = (n0 + n1 + n2).normalized;

            string dir = "";
            if (avgNormal.y > 0.9f) dir = "Up";
            else if (avgNormal.y < -0.9f) dir = "Down";
            else if (avgNormal.z > 0.9f) dir = "Forward";
            else if (avgNormal.z < -0.9f) dir = "Back";

            if (!string.IsNullOrEmpty(dir))
            {
                dirTris[dir].Add(triangles[i]);
                dirTris[dir].Add(triangles[i + 1]);
                dirTris[dir].Add(triangles[i + 2]);
            }
        }

        Material mat = mergedWall.GetComponent<MeshRenderer>().material;

        foreach (var kvp in dirTris)
        {
            if (kvp.Value.Count == 0) continue;

            GameObject child = new GameObject(kvp.Key);
            child.transform.SetParent(mergedWall.transform, false);

            Mesh newMesh = new Mesh();
            newMesh.vertices = vertices;
            newMesh.triangles = kvp.Value.ToArray();
            newMesh.normals = normals;

            MeshFilter childMF = child.AddComponent<MeshFilter>();
            childMF.sharedMesh = newMesh;

            MeshRenderer childMR = child.AddComponent<MeshRenderer>();
            childMR.material = mat;
        }

        // 기존 MeshFilter 제거
        UnityEngine.Object.DestroyImmediate(mf);
        UnityEngine.Object.DestroyImmediate(mergedWall.GetComponent<MeshRenderer>());
    }


    private IEnumerator RiseWalls(Transform wallParent)
    {
        wallParent.localScale = new Vector3(1f, 0f, 1f);
        while (wallParent.localScale.y < 1f)
        {
            Vector3 s = wallParent.localScale;
            s.y += _wallRiseSpeed * Time.deltaTime;
            s.y = Mathf.Min(s.y, 1f);
            wallParent.localScale = s;
            yield return null;
        }
    }

    #endregion

    #region Flask ProjectData 기반 생성
    // FlaskClient에서 받은 ProjectData를 기반으로 씬에 건물/층/벽/문/창문 생성
    public void BuildFromProjectData(FlaskClient.ProjectData project)
    {
        if (project == null || project.buildings == null || project.buildings.Count == 0)
            return;

        // 🔥 기존 생성된 오브젝트 모두 삭제
        foreach (Transform child in transform)
        {
            GameObject.DestroyImmediate(child.gameObject);
        }

        // 이후 기존 로직 그대로
        foreach (var building in project.buildings)
        {
            GameObject buildingGO = new GameObject(building.name);
            buildingGO.transform.SetParent(transform, false);
            buildingGO.transform.position = new Vector3(building.position.x, building.position.y, building.position.z);

            if (building.floors == null) continue;

            foreach (var floor in building.floors)
            {
                GameObject floorGO = new GameObject(floor.name);
                floorGO.transform.SetParent(buildingGO.transform, false);
                floorGO.transform.localPosition = Vector3.up * floor.height;

                if (floor.walls == null) continue;

                foreach (var wall in floor.walls)
                {
                    Vector3 startPos = Vector3.zero;
                    Vector3 endPos = Vector3.right;

                    GameObject wallGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wallGO.name = wall.name;
                    wallGO.transform.SetParent(floorGO.transform, false);
                    wallGO.transform.position = (startPos + endPos) * 0.5f + Vector3.up * (_wallHeight * 0.5f);
                    wallGO.transform.localScale = new Vector3(0.1f, _wallHeight, Vector3.Distance(startPos, endPos));

                    if (wall.children != null)
                    {
                        foreach (var child in wall.children)
                        {
                            GameObject childGO = null;
                            if (child.type == "door") childGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            else if (child.type == "window") childGO = GameObject.CreatePrimitive(PrimitiveType.Cube);

                            if (childGO != null)
                            {
                                childGO.name = child.name;
                                childGO.transform.SetParent(wallGO.transform, false);
                                childGO.transform.localPosition = new Vector3(child.properties.offset.x, child.properties.offset.y, 0);
                                childGO.transform.localScale = new Vector3(child.properties.width, child.properties.height, 0.1f);
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion
    #region Wall / FloorData 생성
    public class FloorData
    {
        public float height;
        public List<WallData> walls = new List<WallData>();
        public List<FloorArea> floorAreas = new List<FloorArea>();
    }

    public class WallData
    {
        public Vector3 startPoint;
        public Vector3 endPoint;
    }

    public class FloorArea
    {
        public List<Vector3> vertices = new List<Vector3>();
    }

    public FloorData GenerateFloorDataFromTexture(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();

        FloorData floor = new FloorData();
        floor.height = 3f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c = pixels[y * w + x];

                if (IsColorClose(c, _wallColor, 10))
                {
                    WallData wall = new WallData();
                    wall.startPoint = new Vector3(x, 0, y);
                    wall.endPoint = new Vector3(x + 1, 0, y);
                    floor.walls.Add(wall);
                }
                else if (IsColorClose(c, _floorColor, 10))
                {
                    FloorArea area = new FloorArea();
                    area.vertices.Add(new Vector3(x, 0, y));
                    floor.floorAreas.Add(area);
                }
            }
        }

        return floor;
    }
    #endregion

    #region JSON 직렬화용 ProjectData
    [Serializable]
    public class ProjectData
    {
        public List<BuildingData> buildings = new List<BuildingData>();
    }

    [Serializable]
    public class BuildingData
    {
        public string id = "b_1";
        public string name = "generated_building";
        public Position position = new Position();
        public List<FloorData> floors = new List<FloorData>();
    }

    [Serializable]
    public class Position
    {
        public float x, y, z;
    }

    public ProjectData GenerateProjectDataFromTexture()
    {
        if (_texCopy == null)
        {
            Debug.LogError("TextureCopy가 없습니다.");
            return null;
        }

        int w = _texCopy.width;
        int h = _texCopy.height;
        Color32[] pixels = _texCopy.GetPixels32();

        ProjectData project = new ProjectData();
        BuildingData building = new BuildingData();
        project.buildings.Add(building);

        FloorData floor = GenerateFloorDataFromTexture(_texCopy);
        building.floors.Add(floor);

        return project;
    }
    #endregion
    /// <summary>
    /// ProjectData를 JSON으로 직렬화 후 Flask 서버로 전송
    /// </summary>
    public void SendProjectDataToServer()
    {
        ProjectData project = GenerateProjectDataFromTexture();
        if (project == null)
        {
            Debug.LogError("ProjectData 생성 실패");
            return;
        }

        string json = JsonUtility.ToJson(project, true);
        Debug.Log("Generated JSON:\n" + json);

        StartCoroutine(PostJsonToFlask("http://127.0.0.1:5000/upload_project", json));
    }

    private IEnumerator PostJsonToFlask(string url, string json)
    {
        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("JSON 전송 성공: " + www.downloadHandler.text);
            }
            else
            {
                Debug.LogError("JSON 전송 실패: " + www.error);
            }
        }
    }
    public void SaveAndSendFloorData(FloorData floor)
    {
        if (floor == null)
        {
            Debug.LogError("FloorData가 없습니다.");
            return;
        }

        // 1️⃣ JSON 저장
        string folderPath = Path.Combine(Application.dataPath, "JsonFolder");
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string filePath = Path.Combine(folderPath, "project.json");
        string jsonString = JsonUtility.ToJson(floor, true);
        File.WriteAllText(filePath, jsonString);
        Debug.Log($"JSON 파일 저장 완료: {filePath}");

        // 2️⃣ Flask 서버 전송
        StartCoroutine(PostJsonToFlask(jsonString));
    }

    private IEnumerator PostJsonToFlask(string json)
    {
        string url = "http://127.0.0.1:5000/upload_project"; // Flask 서버 주소
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (request.result != UnityWebRequest.Result.Success)
#else
    if (request.isNetworkError || request.isHttpError)
#endif
        {
            Debug.LogError($"JSON 전송 실패: {request.responseCode} {request.error}");
        }
        else
        {
            Debug.Log($"JSON 전송 성공: {request.downloadHandler.text}");
        }
    }
}

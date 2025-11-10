using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        //GameObject innerEdgeWalls = GenerateInnerEdgeWalls(floorPlanGO.transform, _segments);
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

                // 검정
                if (c.r < 50 && c.g < 50 && c.b < 50)
                {
                    _wallMap[x, y] = 1;
                }
                // 빨강
                else if (c.r > 200 && c.g < 50 && c.b < 50)
                {
                    _wallMap[x, y] = 2;
                }
                // 어두운 회색 계열(R≈G≈B, 밝기 낮은 회색)
                else if (Mathf.Abs(c.r - c.g) < 10 && Mathf.Abs(c.g - c.b) < 10 && c.r < 150)
                {
                    _wallMap[x, y] = 1;
                }
                else
                {
                    _wallMap[x, y] = 0; // 바닥
                }
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

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

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

        // 내부 영역 채우기
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

        // 벽 색상 적용
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

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                Color32 cur = pixels[y * w + x];
                if (IsColorClose(cur, _floorColor, tolerance))
                {
                    bool touchingWall = false;
                    for (int i = 0; i < dx.Length; i++)
                    {
                        int nx = x + dx[i];
                        int ny = y + dy[i];
                        if (IsColorClose(pixels[ny * w + nx], _wallColor, tolerance))
                        {
                            touchingWall = true;
                            break;
                        }
                    }
                    if (touchingWall)
                        pixels[y * w + x] = _innerColor;
                }
            }
        }

        // 경계 확장
        Color32[] pixelsCopy = (Color32[])pixels.Clone();
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (pixelsCopy[y * w + x].Equals(_innerColor))
                {
                    for (int i = 0; i < dx.Length; i++)
                    {
                        int nx = x + dx[i];
                        int ny = y + dy[i];
                        if (IsColorClose(pixelsCopy[ny * w + nx], _floorColor, tolerance))
                            pixels[ny * w + nx] = _innerColor;
                    }
                }
            }
        }

        _texCopy.SetPixels32(pixels);
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

    #endregion

    #region Floor / Wall 생성
    private List<Vector2[]> DetectWallSegmentsOpenCV(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        bool[,] visited = new bool[w, h];

        // 작은 벽 제거
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

        // OpenCV HoughLinesP
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

        // Material 재사용
        Material wallMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.red };

        HashSet<string> createdLines = new HashSet<string>();
        HashSet<string> mergedKeys = new HashSet<string>();

        foreach (var seg in mergedSegments)
        {
            Vector2 start = seg[0];
            Vector2 end = seg[1];
            if (start.x > end.x || (Mathf.Approximately(start.x, end.x) && start.y > end.y))
            {
                (start, end) = (end, start);
            }
            string key = $"{start.x:F3}_{start.y:F3}_{end.x:F3}_{end.y:F3}";
            mergedKeys.Add(key);
        }

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
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

                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = x + dx[dir];
                    int ny = y + dy[dir];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (!pixels[ny * w + nx].Equals(_innerColor)) continue;

                    Vector2 start = new Vector2(x, y);
                    Vector2 end = new Vector2(nx, ny);
                    if (start.x > end.x || (Mathf.Approximately(start.x, end.x) && start.y > end.y))
                        (start, end) = (end, start);

                    string key = $"{start.x:F3}_{start.y:F3}_{end.x:F3}_{end.y:F3}";
                    if (createdLines.Contains(key) || mergedKeys.Contains(key)) continue;
                    createdLines.Add(key);

                    Vector3 p0 = new Vector3(start.x * scaleX - _planeSize.x * 0.5f, 0f, start.y * scaleZ - _planeSize.y * 0.5f);
                    Vector3 p1 = new Vector3(end.x * scaleX - _planeSize.x * 0.5f, 0f, end.y * scaleZ - _planeSize.y * 0.5f);
                    Vector3 dirVec = p1 - p0;
                    float len = dirVec.magnitude;
                    if (len < 0.01f) continue;

                    Vector3 mid = (p0 + p1) * 0.5f;
                    Quaternion rot = Quaternion.LookRotation(dirVec.normalized, Vector3.up);

                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(group.transform);
                    cube.transform.localPosition = mid + Vector3.up * wallYoffset;
                    cube.transform.localRotation = rot;
                    cube.transform.localScale = new Vector3(wallThickness, _wallHeight, len);
                    cube.GetComponent<MeshRenderer>().material = wallMat;
                }
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
}

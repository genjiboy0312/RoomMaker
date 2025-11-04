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
        Color32[] _pixels = tex.GetPixels32();
        Mat _mat = new Mat(tex.height, tex.width, CvType.CV_8UC4);
        _mat.put(0, 0, Color32ArrayToByteArray(_pixels));
        return _mat;
    }

    public static Texture2D MatToTexture2D(Mat mat)
    {
        int _width = mat.cols();
        int _height = mat.rows();
        Texture2D _tex = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
        byte[] data = new byte[_width * _height * 4];
        mat.get(0, 0, data);
        _tex.LoadRawTextureData(data);
        _tex.Apply();
        return _tex;
    }

    private static byte[] Color32ArrayToByteArray(Color32[] colors)
    {
        int _length = colors.Length;
        byte[] _bytes = new byte[_length * 4];
        for (int i = 0; i < _length; i++)
        {
            _bytes[i * 4] = colors[i].r;
            _bytes[i * 4 + 1] = colors[i].g;
            _bytes[i * 4 + 2] = colors[i].b;
            _bytes[i * 4 + 3] = colors[i].a;
        }
        return _bytes;
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
    [SerializeField] private float _wallHeight = 3f;
    [SerializeField] private float _wallRiseSpeed = 1f;
    [SerializeField] private int _minRoomPixel = 50; // 최소 픽셀수

    private Texture2D _texCopy;
    private int[,] _wallMap;
    private List<Vector2[]> _segments;

    private void Start()
    {
        if (_floorPlanImage == null || _floorPlanImage.texture == null)
        {
            Debug.LogError("FloorPlan RawImage 또는 텍스처가 설정되지 않았습니다.");
            return;
        }

        // 텍스처 복사
        Texture2D srcTex = (Texture2D)_floorPlanImage.texture;
        _texCopy = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGBA32, false);
        _texCopy.SetPixels32(srcTex.GetPixels32());
        _texCopy.Apply();

        //  wall map 생성
        CreateWallMap();

        //  wall 내부를 floorColor로 칠함 (빨간 벽 포함)
        ColorFloorInsideWalls();

        //  OpenCV로 선분 검출
        _segments = DetectWallSegmentsOpenCV(_texCopy);

        //  최상위 루트(FloorPlan)를 명시적으로 원점에 생성
        GameObject floorPlanGO = new GameObject("FloorPlan");
        floorPlanGO.transform.position = Vector3.zero; // << 확실히 0,0,0

        //  바닥들 생성 (FloorGroups 하위)
        GameObject floorGroup = CreateRoomFloors(_texCopy, floorPlanGO.transform);
        floorGroup.name = "FloorGroups";

        //  벽 생성 (MergedWalls)
        GameObject mergedWalls = GenerateWallsFromSegments(_segments, floorPlanGO.transform);
        if (mergedWalls != null) mergedWalls.name = "MergedWalls";
    }

    // ------------------------------
    // wall map 생성
    private void CreateWallMap()
    {
        int _w = _texCopy.width;
        int _h = _texCopy.height;
        _wallMap = new int[_w, _h];
        Color32[] _pixels = _texCopy.GetPixels32();

        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                Color32 _c = _pixels[y * _w + x];
                if (_c.r < 50 && _c.g < 50 && _c.b < 50) _wallMap[x, y] = 1; // 검정 = 벽
                else if (_c.r > 200 && _c.g < 50 && _c.b < 50) _wallMap[x, y] = 2; // 빨강 = 벽으로 포함
                else _wallMap[x, y] = 0; // 바닥
            }
        }
    }

    // ------------------------------
    // 벽 내부를 바닥색으로 칠해서 UI에 반영 (빨간 벽 포함)
    private void ColorFloorInsideWalls()
    {
        int w = _texCopy.width;
        int h = _texCopy.height;
        Color32[] _pixels = _texCopy.GetPixels32();

        // 외부 flood fill 영역 생성
        bool[,] _visited = new bool[w, h];
        Queue<Vector2Int> _q = new Queue<Vector2Int>();
        for (int i = 0; i < w; i++)
        {
            if (!_visited[i, 0]) { _q.Enqueue(new Vector2Int(i, 0)); _visited[i, 0] = true; }
            if (!_visited[i, h - 1]) { _q.Enqueue(new Vector2Int(i, h - 1)); _visited[i, h - 1] = true; }
        }
        for (int j = 0; j < h; j++)
        {
            if (!_visited[0, j]) { _q.Enqueue(new Vector2Int(0, j)); _visited[0, j] = true; }
            if (!_visited[w - 1, j]) { _q.Enqueue(new Vector2Int(w - 1, j)); _visited[w - 1, j] = true; }
        }

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (_q.Count > 0)
        {
            var p = _q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = p.x + dx[i];
                int ny = p.y + dy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (_visited[nx, ny] || _wallMap[nx, ny] == 1) continue;
                _visited[nx, ny] = true;
                _q.Enqueue(new Vector2Int(nx, ny));
            }
        }

        // -----------------------------
        // 내부 영역 채우기 (작은 영역 무시)
        bool[,] _filled = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (_wallMap[x, y] != 1 && !_visited[x, y] && !_filled[x, y])
                {
                    Queue<Vector2Int> _rq = new Queue<Vector2Int>();
                    List<Vector2Int> _region = new List<Vector2Int>();
                    _rq.Enqueue(new Vector2Int(x, y));
                    _filled[x, y] = true;

                    while (_rq.Count > 0)
                    {
                        var _p = _rq.Dequeue();
                        _region.Add(_p);

                        for (int i = 0; i < 4; i++)
                        {
                            int nx = _p.x + dx[i];
                            int ny = _p.y + dy[i];
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (_filled[nx, ny] || _visited[nx, ny] || _wallMap[nx, ny] == 1) continue;
                            _rq.Enqueue(new Vector2Int(nx, ny));
                            _filled[nx, ny] = true;
                        }
                    }

                    if (_region.Count >= _minRoomPixel)
                    {
                        foreach (var p in _region)
                        {
                            _pixels[p.y * w + p.x] = _floorColor;
                            _wallMap[p.x, p.y] = 2;
                        }
                    }
                }
            }
        }

        // -----------------------------
        // 벽 색칠
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (_wallMap[x, y] == 1)
                    _pixels[y * w + x] = _wallColor;

        _texCopy.SetPixels32(_pixels);
        _texCopy.Apply();
        _floorPlanImage.texture = _texCopy;
    }

    // ------------------------------
    // OpenCV 선검출
    private List<Vector2[]> DetectWallSegmentsOpenCV(Texture2D tex)
    {
        int _w = tex.width;
        int _h = tex.height;

        // 1. _wallMap에서 작은 내부 영역 제거 (화살표 같은 것)
        bool[,] _visited = new bool[_w, _h];
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                if (_wallMap[x, y] != 1 || _visited[x, y]) continue;

                Queue<Vector2Int> _q = new Queue<Vector2Int>();
                List<Vector2Int> _region = new List<Vector2Int>();
                _q.Enqueue(new Vector2Int(x, y));
                _visited[x, y] = true;

                while (_q.Count > 0)
                {
                    Vector2Int _p = _q.Dequeue();
                    _region.Add(_p);

                    int[] _dx = { 1, -1, 0, 0 };
                    int[] _dy = { 0, 0, 1, -1 };
                    for (int i = 0; i < 4; i++)
                    {
                        int _nx = _p.x + _dx[i];
                        int _ny = _p.y + _dy[i];
                        if (_nx < 0 || _ny < 0 || _nx >= _w || _ny >= _h) continue;
                        if (_visited[_nx, _ny] || _wallMap[_nx, _ny] != 1) continue;
                        _q.Enqueue(new Vector2Int(_nx, _ny));
                        _visited[_nx, _ny] = true;
                    }
                }

                // 영역이 작으면 벽에서 제거
                if (_region.Count < _minRoomPixel)
                {
                    foreach (var p in _region)
                        _wallMap[p.x, p.y] = 0;
                }
            }
        }

        // 2. OpenCV로 선분 검출
        Mat _mat = OpenCVTextureUtils.Texture2DToMat(tex);
        Imgproc.cvtColor(_mat, _mat, Imgproc.COLOR_RGBA2GRAY);

        // _wallMap == 1(벽)인 곳만 255, 나머지는 0
        byte[] _data = new byte[_mat.rows() * _mat.cols()];
        for (int y = 0; y < _mat.rows(); y++)
        {
            for (int x = 0; x < _mat.cols(); x++)
            {
                _data[y * _mat.cols() + x] = (_wallMap[x, y] == 1) ? (byte)255 : (byte)0;
            }
        }
        _mat.put(0, 0, _data);

        Mat _lines = new Mat();
        Imgproc.HoughLinesP(_mat, _lines, 1, Mathf.PI / 180f, 10, 20, 15);

        List<Vector2[]> _segs = new List<Vector2[]>();
        for (int i = 0; i < _lines.rows(); i++)
        {
            int[] _d = new int[4];
            _lines.get(i, 0, _d);
            _segs.Add(new Vector2[] { new Vector2(_d[0], _d[1]), new Vector2(_d[2], _d[3]) });
        }

        return _segs;
    }


    // ------------------------------
    // 방 단위 바닥 생성
    private GameObject CreateRoomFloors(Texture2D tex, Transform parent)
    {
        int _w = tex.width;
        int _h = tex.height;
        Color32[] _pixels = tex.GetPixels32();
        bool[,] _visited = new bool[_w, _h];

        float _floorHeight = 0.05f;
        float _scaleX = _planeSize.x / _w;
        float _scaleZ = _planeSize.y / _h;

        GameObject _floorGroup = new GameObject("FloorGroups");
        _floorGroup.transform.SetParent(parent, false);

        int[] _dx = { 1, -1, 0, 0 };
        int[] _dy = { 0, 0, 1, -1 };
        int _roomIdx = 0;

        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                if (_visited[x, y]) continue;
                if (_wallMap[x, y] != 2) continue;

                // 영역 수집 (flood)
                List<Vector2Int> _area = new List<Vector2Int>();
                Queue<Vector2Int> _q = new Queue<Vector2Int>();
                _q.Enqueue(new Vector2Int(x, y));
                _visited[x, y] = true;

                while (_q.Count > 0)
                {
                    var _p = _q.Dequeue();
                    _area.Add(_p);
                    for (int i = 0; i < 4; i++)
                    {
                        int _nx = _p.x + _dx[i], _ny = _p.y + _dy[i];
                        if (_nx < 0 || _ny < 0 || _nx >= _w || _ny >= _h) 
                            continue;
                        if (_visited[_nx, _ny]) 
                            continue;
                        if (_wallMap[_nx, _ny] == 2)
                        {
                            _visited[_nx, _ny] = true;
                            _q.Enqueue(new Vector2Int(_nx, _ny));
                        }
                    }
                }

                if (_area.Count < _minRoomPixel) 
                    continue;

                // 중심(center) 계산
                float _avgX = (float)_area.Average(p => p.x);
                float _avgY = (float)_area.Average(p => p.y);
                Vector3 _center = new Vector3(
                    (_avgX / _w - 0.5f) * _planeSize.x,
                    _floorHeight * 0.5f,
                    (_avgY / _h - 0.5f) * _planeSize.y
                );

                List<CombineInstance> _floorCombs = new List<CombineInstance>();
                GameObject _tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh _cubeMesh = _tmp.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(_tmp);

                foreach (var p in _area)
                {
                    Vector3 _pixelPos = new Vector3((p.x + 0.5f) * _scaleX - _planeSize.x * 0.5f,
                                                   -_floorHeight * 0.5f,
                                                   (p.y + 0.5f) * _scaleZ - _planeSize.y * 0.5f);

                    Vector3 _localPos = _pixelPos - new Vector3(_center.x, 0, _center.z);
                    CombineInstance _ci = new CombineInstance
                    {
                        mesh = _cubeMesh,
                        transform = Matrix4x4.TRS(_localPos, Quaternion.identity, new Vector3(_scaleX, _floorHeight, _scaleZ))
                    };
                    _floorCombs.Add(_ci);
                }

                Mesh _floorMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                _floorMesh.CombineMeshes(_floorCombs.ToArray(), true, true);
                _floorMesh.RecalculateNormals();
                _floorMesh.RecalculateBounds();

                GameObject _floorGO = new GameObject($"Floor_{_roomIdx:D2}");
                _floorGO.transform.SetParent(_floorGroup.transform, false);
                _floorGO.transform.localPosition = _center;

                var _mf = _floorGO.AddComponent<MeshFilter>();
                var _mr = _floorGO.AddComponent<MeshRenderer>();
                _mf.sharedMesh = _floorMesh;
                _mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = _floorColor };

                _roomIdx++;
            }
        }

        return _floorGroup;
    }

    // ------------------------------
    // 벽(segments)으로부터 merged walls 생성
    private GameObject GenerateWallsFromSegments(List<Vector2[]> segments, Transform parent)
    {
        if (segments == null || segments.Count == 0) 
            return null;

        float _scaleX = _planeSize.x / _texCopy.width;
        float _scaleZ = _planeSize.y / _texCopy.height;
        float _wallThickness = Mathf.Max(0.3f, Mathf.Min(_scaleX, _scaleZ));
        float _wallYoffset = _wallHeight * 0.5f;

        GameObject _merged = new GameObject("MergedWalls");
        _merged.transform.SetParent(parent, false);

        GameObject _tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh _cubeMesh = _tmp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(_tmp);

        List<CombineInstance> _combines = new List<CombineInstance>();
        List<Vector3> _centers = new List<Vector3>();

        foreach (var seg in segments)
        {
            Vector3 _p0 = new Vector3(seg[0].x * _scaleX - _planeSize.x * 0.5f, 0f, seg[0].y * _scaleZ - _planeSize.y * 0.5f);
            Vector3 _p1 = new Vector3(seg[1].x * _scaleX - _planeSize.x * 0.5f, 0f, seg[1].y * _scaleZ - _planeSize.y * 0.5f);
            Vector3 _mid = (_p0 + _p1) * 0.5f;
            _centers.Add(_mid);
        }

        Vector3 _avgCenter = Vector3.zero;
        if (_centers.Count > 0)
        {
            foreach (var c in _centers) 
                _avgCenter += c;
            _avgCenter /= _centers.Count;
        }

        foreach (var seg in segments)
        {
            Vector3 _p0 = new Vector3(seg[0].x * _scaleX - _planeSize.x * 0.5f, 0f, seg[0].y * _scaleZ - _planeSize.y * 0.5f);
            Vector3 _p1 = new Vector3(seg[1].x * _scaleX - _planeSize.x * 0.5f, 0f, seg[1].y * _scaleZ - _planeSize.y * 0.5f);
            Vector3 _dir = _p1 - _p0;
            float _len = _dir.magnitude;
            if (_len < 0.01f) continue;

            Vector3 _mid = (_p0 + _p1) * 0.5f;
            Quaternion _rot = Quaternion.LookRotation(_dir.normalized, Vector3.up);
            Vector3 _localMid = _mid - _avgCenter;

            CombineInstance _ci = new CombineInstance
            {
                mesh = _cubeMesh,
                transform = Matrix4x4.TRS(_localMid + Vector3.up * _wallYoffset, _rot, new Vector3(_wallThickness, _wallHeight, _len))
            };
            _combines.Add(_ci);
        }

        if (_combines.Count > 0)
        {
            Mesh _combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _combined.CombineMeshes(_combines.ToArray(), true, true);
            _combined.RecalculateNormals();
            _combined.RecalculateBounds();

            var _mf = _merged.AddComponent<MeshFilter>();
            _mf.sharedMesh = _combined;
            var _mr = _merged.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };

            _merged.transform.localPosition = _avgCenter;
            StartCoroutine(RiseWalls(_merged.transform));
        }
        else
        {
            DestroyImmediate(_merged);
            _merged = null;
        }

        return _merged;
    }

    private IEnumerator RiseWalls(Transform wallParent)
    {
        wallParent.localScale = new Vector3(1f, 0f, 1f);
        while (wallParent.localScale.y < 1f)
        {
            Vector3 _s = wallParent.localScale;
            _s.y += _wallRiseSpeed * Time.deltaTime;
            _s.y = Mathf.Min(_s.y, 1f);
            wallParent.localScale = _s;
            yield return null;
        }
    }
}

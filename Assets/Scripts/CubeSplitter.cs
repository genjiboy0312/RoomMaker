using UnityEngine;

public class CubeSplitter : MonoBehaviour
{
    [SerializeField] private Vector3 cubeSize = Vector3.one;
    [SerializeField] private Material cubeMaterial;

    void Start()
    {
        CreateOuterFaces(transform.position, cubeSize);
    }

    void CreateOuterFaces(Vector3 center, Vector3 size)
    {
        Vector3 half = size * 0.5f;

        // 위/아래
        CreateFace(center + new Vector3(0, half.y, 0), new Vector3(size.x, 0.01f, size.z));
        CreateFace(center + new Vector3(0, -half.y, 0), new Vector3(size.x, 0.01f, size.z));

        // 앞/뒤
        CreateFace(center + new Vector3(0, 0, half.z), new Vector3(size.x, size.y, 0.01f));
        CreateFace(center + new Vector3(0, 0, -half.z), new Vector3(size.x, size.y, 0.01f));

        // 왼쪽/오른쪽
        CreateFace(center + new Vector3(-half.x, 0, 0), new Vector3(0.01f, size.y, size.z));
        CreateFace(center + new Vector3(half.x, 0, 0), new Vector3(0.01f, size.y, size.z));
    }

    void CreateFace(Vector3 pos, Vector3 scale)
    {
        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.transform.position = pos;
        face.transform.localScale = scale;
        if (cubeMaterial != null)
            face.GetComponent<Renderer>().material = cubeMaterial;
        face.transform.SetParent(transform);
    }
}

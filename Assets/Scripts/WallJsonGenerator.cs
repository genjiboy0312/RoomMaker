using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class WallJsonGenerator : MonoBehaviour
{
    [SerializeField] private Texture2D floorPlanTex;

    private void Start()
    {
        // 시작 시 서버로 이미지 전송
        StartCoroutine(SendImageToServer(floorPlanTex));
    }

    private IEnumerator SendImageToServer(Texture2D tex)
    {
        // Texture2D -> PNG -> Base64
        byte[] imgBytes = tex.EncodeToPNG();
        string imgBase64 = System.Convert.ToBase64String(imgBytes);

        // JSON 문자열 직접 생성
        string jsonData = "{\"image\":\"" + imgBase64 + "\"}";

        using (UnityWebRequest request = new UnityWebRequest("http://127.0.0.1:5000/analyze", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log("Server Response: " + responseText);

                // JSON 파일 저장 (Unity 프로젝트 내)
                string folderPath = Application.dataPath + "/JsonFolder";
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string path = folderPath + "/walls.json";
                File.WriteAllText(path, responseText, Encoding.UTF8);
                Debug.Log("walls.json 생성 완료: " + path);
            }
            else
            {
                Debug.LogError("Server request failed: " + request.error);
            }
        }
    }
}

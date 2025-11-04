using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.IO; // 파일 저장용

public class FlaskClient : MonoBehaviour
{
    public IEnumerator SendImage(Texture2D tex)
    {
        byte[] pngBytes = tex.EncodeToPNG();
        string base64Img = System.Convert.ToBase64String(pngBytes);

        // JSON 문자열 직접 생성
        string jsonData = "{\"image\":\"" + base64Img + "\"}";

        using (UnityWebRequest www = new UnityWebRequest("http://localhost:5000/analyze", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string responseText = www.downloadHandler.text;
                Debug.Log("Server Response: " + responseText);

                // JSON 폴더 경로
                string folderPath = Application.dataPath + "/JsonFolder";
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath); // 폴더 없으면 생성
                }

                string filePath = Path.Combine(folderPath, "walls.json");
                File.WriteAllText(filePath, responseText); // 파일 저장
                Debug.Log("walls.json 생성 완료: " + filePath);
            }
            else
            {
                Debug.LogError("Error: " + www.error);
            }
        }
    }
}

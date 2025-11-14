using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class FlaskClient : MonoBehaviour
{
    [Serializable]
    public class ProjectData
    {
        public string projectId;
        public string lastUpdated;
        public List<BuildingData> buildings = new List<BuildingData>();
    }

    [Serializable]
    public class BuildingData
    {
        public string id;
        public string name;
        public Position position;
        public List<FloorData> floors = new List<FloorData>();
    }

    [Serializable]
    public class FloorData
    {
        public string id;
        public string name;
        public float height;
        public List<WallData> walls = new List<WallData>();
    }

    [Serializable]
    public class WallData
    {
        public string id;
        public string name;
        public WallProperties properties;
        public List<WallChild> children = new List<WallChild>();
    }

    [Serializable]
    public class WallProperties
    {
        public Position startPoint;
        public Position endPoint;
        public float thickness;
    }

    [Serializable]
    public class WallChild
    {
        public string id;
        public string name;
        public string type;
        public WallChildProperties properties;
    }

    [Serializable]
    public class WallChildProperties
    {
        public float width;
        public float height;
        public Position offset;
    }

    [Serializable]
    public class Position
    {
        public float x;
        public float y;
        public float z;
    }

    public RoomMaker roomMaker;

    [Header("Flask Server URL")]
    [Tooltip("예: http://127.0.0.1:5000/upload_project")]
    public string serverURL;

    // ---------------- 서버 전송 ----------------

    public void SendFloorPlan(Texture2D tex, Action<string> callback = null)
    {
        StartCoroutine(SendImageCoroutine(tex, callback));
    }

    private IEnumerator SendImageCoroutine(Texture2D tex, Action<string> callback = null)
    {
        byte[] pngBytes = tex.EncodeToPNG();
        string base64Img = Convert.ToBase64String(pngBytes);

        // JSON 포맷 맞춤
        string jsonData = "{\"image\":\"" + base64Img + "\"}";

        yield return SendJsonCoroutine(jsonData, callback);
    }

    public IEnumerator SendJsonToServer(string jsonData, Action<string> callback = null)
    {
        yield return SendJsonCoroutine(jsonData, callback);
    }

    private IEnumerator SendJsonCoroutine(string jsonData, Action<string> callback = null)
    {
        using (UnityWebRequest www = new UnityWebRequest(serverURL, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string responseText = www.downloadHandler.text;
                Debug.Log("✅ Server Response: " + responseText);

                // ---------------- 파일 저장 ----------------
                string folderPath = Path.Combine(Application.dataPath, "JsonFolder");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string filePath = Path.Combine(folderPath, "project.json").Replace("\\", "/");
                File.WriteAllText(filePath, responseText);
                Debug.Log("Project JSON saved at: " + filePath);

                // ---------------- 씬 빌드 ----------------
                if (!string.IsNullOrEmpty(responseText) && roomMaker != null)
                {
                    try
                    {
                        var project = JsonUtility.FromJson<ProjectData>(responseText);
                        if (project != null)
                        {
                            roomMaker.BuildFromProjectData(project);
                        }
                        else
                        {
                            Debug.LogError("❌ JSON Parsing returned null!");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("❌ JSON Parsing Error: " + e.Message);
                    }
                }

                callback?.Invoke(responseText);
            }
            else
            {
                Debug.LogError("❌ Server Error: " + www.error + " | URL: " + serverURL);
                callback?.Invoke(null);
            }
        }
    }
}

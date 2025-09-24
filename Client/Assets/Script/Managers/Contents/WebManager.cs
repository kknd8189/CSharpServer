using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class WebManager
{
    public string BaseUrl { get; set; } = "http://localhost:5000/api";

    public void SendPostRequest<T>(string url, object obj, Action<T> res)
    {
        Managers.Instance.StartCoroutine(CoSendWebRequest(url, UnityWebRequest.kHttpVerbPOST, obj, res));
    }

    IEnumerator CoSendWebRequest<T>(string url, string method, object obj, Action<T> res)
    {
        string sendUrl = $"{BaseUrl}/{url}";

        Debug.Log(sendUrl);

        byte[] jsonBytes = null;
        if (obj != null)
        {
            string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
            jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
        }

        using (var uwr = new UnityWebRequest(sendUrl, method))
        {
            uwr.uploadHandler = new UploadHandlerRaw(jsonBytes);
            uwr.downloadHandler = new DownloadHandlerBuffer();
            uwr.SetRequestHeader("Content-Type", "application/json");

            yield return uwr.SendWebRequest();
            if (uwr.result == UnityWebRequest.Result.ConnectionError ||
                uwr.result == UnityWebRequest.Result.ProtocolError ||
                uwr.result == UnityWebRequest.Result.DataProcessingError)
            {
                Debug.LogError(uwr.error);
            }

            else if (uwr.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    T resObj = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(uwr.downloadHandler.text);
                    res?.Invoke(resObj);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("JSON ÆÄ½Ì ¿À·ù: " + ex.Message);
                }
            }
        }
    }
}

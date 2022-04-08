using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class APIManager : MonoBehaviour
{
    public async Task<GameSessionPlacementInfo> PostGetResponse(string api, string postData)
    {
        bool isuccess = false;
        GameSessionPlacementInfo gameSessionPlacementInfo = new GameSessionPlacementInfo();
        var formData = System.Text.Encoding.UTF8.GetBytes(postData);

        UnityWebRequest webRequest = UnityWebRequest.Post(api, "");

        // add body
        webRequest.uploadHandler = new UploadHandlerRaw(formData);
        webRequest.SetRequestHeader("Content-Type", "application/json");

        // add header so we know where the reqest came from, if reusing same lambda function for both API GW and Step Function
        webRequest.SetRequestHeader("source", "unity");

        // able to await because of GetAwaiter function in ExtensionMethod class.
        await webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.Success)

        {
            Debug.Log("Success, API call complete!");
            Debug.Log(webRequest.downloadHandler.text);
            //TODO may have to cleanup the message first...
            // TODO: make this method more generic, do this somewhere else
            gameSessionPlacementInfo = JsonConvert.DeserializeObject<GameSessionPlacementInfo>(webRequest.downloadHandler.text);
            //isuccess = true;
        }
        else
        {
            Debug.Log("API call failed: " + webRequest.error + "\n" + webRequest.result + "\n" + webRequest.responseCode);
        }

        webRequest.Dispose();

        return gameSessionPlacementInfo;
    }

    public async Task<bool> Post(string api, string postData)
    {
        bool isuccess = false;
        var formData = System.Text.Encoding.UTF8.GetBytes(postData);

        UnityWebRequest webRequest = UnityWebRequest.Post(api, "");

        // add body
        webRequest.uploadHandler = new UploadHandlerRaw(formData);
        webRequest.SetRequestHeader("Content-Type", "application/json");

        // add header so we know where the reqest came from, if reusing same lambda function for both API GW and Step Function
        webRequest.SetRequestHeader("source", "unity");

        // able to await because of GetAwaiter function in ExtensionMethod class.
        await webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.Success)

        {
            Debug.Log("Success, API call complete!");
            Debug.Log(webRequest.downloadHandler.text);
            isuccess = true;
        }
        else
        {
            Debug.Log("API call failed: " + webRequest.error + "\n" + webRequest.result + "\n" + webRequest.responseCode);
        }

        webRequest.Dispose();

        return isuccess;
    }
}
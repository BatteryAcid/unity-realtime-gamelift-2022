using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;

public class APIManager : MonoBehaviour
{
    public async Task<string> Post(string api, string postData)
    {
        string response = "";

        var formData = System.Text.Encoding.UTF8.GetBytes(postData);

        UnityWebRequest webRequest = UnityWebRequest.Post(api, "");

        // add body
        webRequest.uploadHandler = new UploadHandlerRaw(formData);
        webRequest.SetRequestHeader("Content-Type", "application/json");

        // add header so we know where the reqest came from, not required, helpful if you need to
        // differentiate between multiple client types in your lambda function
        webRequest.SetRequestHeader("source", "unity");

        // able to await because of GetAwaiter function in ExtensionMethod class.
        await webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Success, API call complete!");
            // Debug.Log(webRequest.downloadHandler.text);
            
            response = webRequest.downloadHandler.text;
        }
        else
        {
            Debug.Log("API call failed: " + webRequest.error + "\n" + webRequest.result + "\n" + webRequest.responseCode);
        }

        webRequest.Dispose();

        return response;
    }
}
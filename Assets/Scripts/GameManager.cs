
using System;
using UnityEngine;
using Aws.GameLift.Realtime.Types;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Text;
using System.Net;
using System.Net.Sockets;

public class GameManager : MonoBehaviour
{
    // TODO: these need to be underscores
    private APIManager apiManager;
    private Button findMatchButton;
    private Button playCardButton;
    private bool findingMatch = false;
    private string _playerId;

    private SQSMessageProcessing _sqsMessageProcessing;
    private RealTimeClient _realTimeClient;
    private byte[] connectionPayload = new Byte[64];
    private static readonly IPEndPoint DefaultLoopbackEndpoint = new IPEndPoint(IPAddress.Loopback, port: 0);

    public async void OnFindMatchPressed()
    {
        Debug.Log("Find match pressed");
        findingMatch = true;
        // findMatchButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Searching...";


        // bool isSuccess = await buildableObjectManager.BuildObject(FindObjectOfType<Temple>());
        // BuildComplete();
        


        // TODO:
        // - make call using APIClient to find or create game session
        // if creating:
        //   PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = await sqsMessageProcessing.SubscribeToFulfillmentNotifications(queuePlacement.PlacementId);
        // else, GS found:
        //

        MatchMessage matchMessage = new MatchMessage("1", _playerId);
        string jsonPostData = JsonUtility.ToJson(matchMessage);
        Debug.Log(jsonPostData);
        GameSessionPlacementInfo gameSessionPlacementInfo = await apiManager.PostGetResponse("https://0zco9bhj7c.execute-api.us-east-1.amazonaws.com/demo/", jsonPostData);
        if (gameSessionPlacementInfo != null)
        {
            Debug.Log("Success");
            Debug.Log(gameSessionPlacementInfo.PlacementId);
            // subscribe to receive the player placement fulfillment notification
            // TODO: use the placement id from lambda request to subscribe to fulfillment notifications
            await SubscribeToFulfillmentNotifications(gameSessionPlacementInfo.PlacementId);

        }

        // TODO: once match found, update to false
        findingMatch = false;
    }


    private async Task<bool> SubscribeToFulfillmentNotifications(string placementId)
    {
        PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = await _sqsMessageProcessing.SubscribeToFulfillmentNotifications(placementId);

        if (playerPlacementFulfillmentInfo != null)
        {
            Debug.Log("PlayerPlacementFulfillmentInfo fulfilled...");
            Debug.Log("PlacedPlayerSessions count: " + playerPlacementFulfillmentInfo.placedPlayerSessions.Count);

            int localUdpPort = GetAvailableUdpPort();
            
            _realTimeClient = new RealTimeClient(playerPlacementFulfillmentInfo.ipAddress, playerPlacementFulfillmentInfo.port, localUdpPort,
                playerPlacementFulfillmentInfo.placedPlayerSessions[0].playerSessionId, connectionPayload, ConnectionType.RT_OVER_WS_UDP_UNSECURED);


            return true;
        }
        else
        {
            // if null something went wrong
            Debug.Log("PlayerPlacementFulfillmentInfo was null...");
            return false;
        }
    }

    public async void OnPlayCardPressed()
    {
        Debug.Log("Play card pressed");

    }

    void Start()
    {
        Debug.Log("Starting...");
        apiManager = FindObjectOfType<APIManager>();
        _sqsMessageProcessing = FindObjectOfType<SQSMessageProcessing>();

        findMatchButton = GameObject.Find("FindMatch").GetComponent<Button>();
        findMatchButton.onClick.AddListener(OnFindMatchPressed);

        playCardButton = GameObject.Find("PlayCard").GetComponent<Button>();
        playCardButton.onClick.AddListener(OnPlayCardPressed);

        _playerId = System.Guid.NewGuid().ToString();
    }

    void Update()
    {
        if (findingMatch)
        {
            // TODO: probably move to not active, hide it.
            findMatchButton.enabled = false;
        }
    }

    public static int GetAvailableUdpPort()
    {
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            socket.Bind(DefaultLoopbackEndpoint);
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }
    }
}

[System.Serializable]
public class MatchMessage
{
    public string opCode;
    public string playerId;
    public MatchMessage() { }
    public MatchMessage(string opCodeIn, string playerIdIn)
    {
        this.opCode = opCodeIn;
        this.playerId = playerIdIn;
    }
}


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

    private const int PLAY_CARD_OP = 300;

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
        Debug.Log(gameSessionPlacementInfo);

        if (gameSessionPlacementInfo != null)
        {
            Debug.Log("Success");
            if (gameSessionPlacementInfo.PlacementId != null)
            {
                // The response was from a placement request
                Debug.Log(gameSessionPlacementInfo.PlacementId);

                // subscribe to receive the player placement fulfillment notification
                await SubscribeToFulfillmentNotifications(gameSessionPlacementInfo.PlacementId);

            }
            else if (gameSessionPlacementInfo.GameSessionId != null)
            {
                // The response was for a found game session which also contains infor for created player session
                Debug.Log(gameSessionPlacementInfo.GameSessionId);

                Int32.TryParse(gameSessionPlacementInfo.Port, out int portAsInt);

                // Once connected, the Realtime service moves the Player session from Reserved to Active.
                // https://docs.aws.amazon.com/gamelift/latest/apireference/API_CreatePlayerSession.html
                EstablishConnectionToRealtimeServer(gameSessionPlacementInfo.IpAddress, portAsInt, gameSessionPlacementInfo.PlayerSessionId, connectionPayload);
            } else
            {
                Debug.Log("Game session response not valid...");
            }
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

            // Once connected, the Realtime service moves the Player session from Reserved to Active.
            // https://docs.aws.amazon.com/gamelift/latest/apireference/API_CreatePlayerSession.html
            EstablishConnectionToRealtimeServer(playerPlacementFulfillmentInfo.ipAddress, playerPlacementFulfillmentInfo.port,
                playerPlacementFulfillmentInfo.placedPlayerSessions[0].playerSessionId, connectionPayload);

            return true;
        }
        else
        {
            // if null something went wrong
            Debug.Log("PlayerPlacementFulfillmentInfo was null...");
            return false;
        }
    }

    private void EstablishConnectionToRealtimeServer(string ipAddress, int port, string playerSessionId, byte[] connectionPayload)
    {
        int localUdpPort = GetAvailableUdpPort();

        _realTimeClient = new RealTimeClient(ipAddress, port, localUdpPort, playerSessionId, connectionPayload, ConnectionType.RT_OVER_WS_UDP_UNSECURED);

    }

    public void OnPlayCardPressed()
    {
        Debug.Log("Play card pressed");

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);
        // You will use the Realtime client's send message function to pass data to the server
         _realTimeClient.SendMessage(PLAY_CARD_OP, realtimePayload);

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
        playCardButton.enabled = false;

        _playerId = System.Guid.NewGuid().ToString();
    }

    void Update()
    {
        if (findingMatch)
        {
            // TODO: probably move to not active, hide it.
            findMatchButton.enabled = false;
        }

        // TODO: hack to get the button to only work when game is started
        if (_realTimeClient != null && _realTimeClient.GameStarted && playCardButton.enabled == false)
        {
            playCardButton.enabled = true;
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

[System.Serializable]
public class RealtimePayload
{
    public string playerId;
    // Other fields you wish to pass as payload to the realtime server
    public RealtimePayload() { }
    public RealtimePayload(string playerIdIn)
    {
        this.playerId = playerIdIn;
    }
}


using System;
using UnityEngine;
using Aws.GameLift.Realtime.Types;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    private const string GameSessionPlacementEndpoint = "https://0zco9bhj7c.execute-api.us-east-1.amazonaws.com/demo/";

    private static readonly IPEndPoint DefaultLoopbackEndpoint = new IPEndPoint(IPAddress.Loopback, port: 0);
    private SQSMessageProcessing _sqsMessageProcessing;
    private RealTimeClient _realTimeClient;
    private APIManager _apiManager;

    private Button _findMatchButton;
    private Button _playCardButton;
    private Button _quitButton;

    private bool _findingMatch = false;
    private string _playerId;

    // card play UI, assumes the player one spot is always taken by the local player client.
    private string _localPlayerFirstCardPlayUI = "";
    private string _localPlayerSecondCardPlayUI = "";

    private string _remotePlayerFirstCardPlayUI = "";
    private string _remotePlayerSecondCardPlayUI = "";

    private string _updateRemotePlayerId = "";

    // Lambda opcodes
    private const string REQUEST_FIND_MATCH_OP = "1";

    // GameLift server opcodes 
    // An opcode defined by client and your server script that represents a custom message type
    public const int OP_CODE_PLAYER_ACCEPTED = 113;
    public const int GAME_START_OP = 201;
    public const int GAMEOVER_OP = 209;
    public const int PLAY_CARD_OP = 300;
    public const int DRAW_CARD_ACK_OP = 301;

    public TMPro.TextMeshProUGUI localClientPlayerName;
    public TMPro.TextMeshProUGUI Player1Card1;
    public TMPro.TextMeshProUGUI Player1Card2;
    public TMPro.TextMeshProUGUI Player1Result;

    public TMPro.TextMeshProUGUI remoteClientPlayerName;
    public TMPro.TextMeshProUGUI Player2Card1;
    public TMPro.TextMeshProUGUI Player2Card2;
    public TMPro.TextMeshProUGUI Player2Result;

    public List<CardPlayed> cardsPlayed = new List<CardPlayed>();

    public async void OnFindMatchPressed()
    {
        Debug.Log("Find match pressed");
        _findingMatch = true;

        FindMatch matchMessage = new FindMatch(REQUEST_FIND_MATCH_OP, _playerId);
        string jsonPostData = JsonUtility.ToJson(matchMessage);
        Debug.Log(jsonPostData);

        localClientPlayerName.text = _playerId;

        GameSessionPlacementInfo gameSessionPlacementInfo = await _apiManager.PostGetResponse(GameSessionPlacementEndpoint, jsonPostData);
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
                EstablishConnectionToRealtimeServer(gameSessionPlacementInfo.IpAddress, portAsInt, gameSessionPlacementInfo.PlayerSessionId);
            }
            else
            {
                Debug.Log("Game session response not valid...");
            }
        }

        _findingMatch = false;
        _findMatchButton.gameObject.SetActive(false);
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
                playerPlacementFulfillmentInfo.placedPlayerSessions[0].playerSessionId);

            return true;
        }
        else
        {
            // if null something went wrong
            Debug.Log("PlayerPlacementFulfillmentInfo was null...");
            return false;
        }
    }

    private void EstablishConnectionToRealtimeServer(string ipAddress, int port, string playerSessionId)
    {
        int localUdpPort = GetAvailableUdpPort();

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);
        string payload = JsonUtility.ToJson(realtimePayload);

        _realTimeClient = new RealTimeClient(ipAddress, port, localUdpPort, playerSessionId, payload, ConnectionType.RT_OVER_WS_UDP_UNSECURED, this);

    }

    public void UpdateRemotePlayerUI(string remotePlayerId)
    {
        _updateRemotePlayerId = remotePlayerId;
    }

    public void OnPlayCardPressed()
    {
        Debug.Log("Play card pressed");

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);
        // You will use the Realtime client's send message function to pass data to the server
        _realTimeClient.SendMessage(PLAY_CARD_OP, realtimePayload);
    }

    public void CardPlayed(CardPlayed cardPlayed)
    {
        Debug.Log("card played");
        Debug.Log(cardPlayed.card);

        if (cardPlayed.playedBy == _playerId)
        {
            // local card played
            if (cardPlayed.plays == 1)
            {
                _localPlayerFirstCardPlayUI = cardPlayed.card.ToString();
            } else if (cardPlayed.plays == 2)
            {
                _localPlayerSecondCardPlayUI = cardPlayed.card.ToString();
            }
        } else
        {
            // remote card played
            if (cardPlayed.plays == 1)
            {
                _remotePlayerFirstCardPlayUI = cardPlayed.card.ToString();
            }
            else if (cardPlayed.plays == 2)
            {
                _remotePlayerSecondCardPlayUI = cardPlayed.card.ToString();
            }
        }
    }

    void Update()
    {
        if (_findingMatch)
        {
            // TODO: probably move to not active, hide it.
            _findMatchButton.enabled = false;
            _findMatchButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Searching...";
        }

        // TODO: hack to get the button to only work when game is started
        // TODO: this will have to be updated to work only after the 2nd player is connected
        if (_realTimeClient != null && _realTimeClient.GameStarted)
        {
            _playCardButton.gameObject.SetActive(true);
        }

        // TODO: this will probably have to be rolled into the above conditional
        if (_updateRemotePlayerId != null && _updateRemotePlayerId != "")
        {
            remoteClientPlayerName.text = _updateRemotePlayerId;
            _updateRemotePlayerId = ""; // clean up to stop triggering update
        }

        // Card plays - there's a better way to do this...
        // TODO: think of a better way to manage the card play and updating the UI...
        if (_localPlayerFirstCardPlayUI != "")
        {
            Player1Card1.text = _localPlayerFirstCardPlayUI;
            _localPlayerFirstCardPlayUI = "";
        }

        if (_localPlayerSecondCardPlayUI != "")
        {
            Player1Card2.text = _localPlayerSecondCardPlayUI;
            _localPlayerSecondCardPlayUI = "";
        }

        if (_remotePlayerFirstCardPlayUI != "")
        {
            Player2Card1.text = _remotePlayerFirstCardPlayUI;
            _remotePlayerFirstCardPlayUI = "";
        }

        if (_remotePlayerSecondCardPlayUI != "")
        {
            Player2Card2.text = _remotePlayerSecondCardPlayUI;
            _remotePlayerSecondCardPlayUI = "";
        }

        if (_realTimeClient != null && _realTimeClient.GameOver == true)
        {
            MatchResults matchResults = _realTimeClient.GetMatchResults();

            string localPlayerResults = "";
            string remotePlayerResults = "";

            if (matchResults.winnerId == _playerId)
            {
                localPlayerResults = "WINNER! Score ";
                remotePlayerResults = "Loser. Score ";
            }
            else
            {
                remotePlayerResults = "WINNER! Score ";
                localPlayerResults = "Loser. Score ";
            }

            if (matchResults.playerOneId == _playerId)
            {
                // our local player matches player one data
                localPlayerResults += matchResults.playerOneScore;
                remotePlayerResults += matchResults.playerTwoScore;
            } else
            {
                // our local player matches player two data
                localPlayerResults += matchResults.playerTwoScore;
                remotePlayerResults += matchResults.playerOneScore;
            }

            Player1Result.text = localPlayerResults;
            Player2Result.text = remotePlayerResults;
        }
    }

    void Start()
    {
        Debug.Log("Starting...");
        _apiManager = FindObjectOfType<APIManager>();
        _sqsMessageProcessing = FindObjectOfType<SQSMessageProcessing>();

        _findMatchButton = GameObject.Find("FindMatch").GetComponent<Button>();
        _findMatchButton.onClick.AddListener(OnFindMatchPressed);

        _playCardButton = GameObject.Find("PlayCard").GetComponent<Button>();
        _playCardButton.onClick.AddListener(OnPlayCardPressed);
        _playCardButton.gameObject.SetActive(false);

        _quitButton = GameObject.Find("Quit").GetComponent<Button>();
        _quitButton.onClick.AddListener(OnQuitPressed);

        _playerId = System.Guid.NewGuid().ToString();
    }

    public static int GetAvailableUdpPort()
    {
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            socket.Bind(DefaultLoopbackEndpoint);
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }
    }

    public void OnQuitPressed()
    {
        Debug.Log("OnQuitPressed");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnApplicationQuit()
    {
        // clean up the connection if the game gets killed
        if (_realTimeClient != null && _realTimeClient.IsConnected())
        {
            _realTimeClient.Disconnect();
        }
    }
}

[System.Serializable]
public class FindMatch
{
    public string opCode;
    public string playerId;
    public FindMatch() { }
    public FindMatch(string opCodeIn, string playerIdIn)
    {
        this.opCode = opCodeIn;
        this.playerId = playerIdIn;
    }
}

[System.Serializable]
public class ConnectMessage
{
    public string playerConnected;
    public ConnectMessage() { }
    public ConnectMessage(string playerConnectedIn)
    {
        this.playerConnected = playerConnectedIn;
    }
}

[System.Serializable]
public class StartMatch
{
    public string remotePlayerId;
    public StartMatch() { }
    public StartMatch(string remotePlayerIdIn)
    {
        this.remotePlayerId = remotePlayerIdIn;
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

[System.Serializable]
public class CardPlayed
{
    public int card;
    public string playedBy;
    public int plays;

    public CardPlayed() { }
    public CardPlayed(int cardIn, string playedByIn, int playsIn)
    {
        this.card = cardIn;
        this.playedBy = playedByIn;
        this.plays = playsIn;
    }
}

[System.Serializable]
public class MatchResults
{
    public string playerOneId;
    public string playerTwoId;

    public string playerOneScore;
    public string playerTwoScore;

    public string winnerId;

    public MatchResults() { }
    public MatchResults(string playerOneIdIn, string playerTwoIdIn, string playerOneScoreIn, string playerTwoScoreIn, string winnerIdIn)
    {
        this.playerOneId = playerOneIdIn;
        this.playerTwoId = playerTwoIdIn;
        this.playerOneScore = playerOneScoreIn;
        this.playerTwoScore = playerTwoScoreIn;
        this.winnerId = winnerIdIn;
    }
}

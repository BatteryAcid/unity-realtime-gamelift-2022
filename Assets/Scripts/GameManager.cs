
using System;
using UnityEngine;
using Aws.GameLift.Realtime.Types;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using Newtonsoft.Json;

public class GameManager : MonoBehaviour
{
    private const string GameSessionPlacementEndpoint = "YOUR_API_GW_ENDPOINT";

    private static readonly IPEndPoint DefaultLoopbackEndpoint = new IPEndPoint(IPAddress.Loopback, port: 0);
    private SQSMessageProcessing _sqsMessageProcessing;
    private RealTimeClient _realTimeClient;
    private APIManager _apiManager;

    private Button _findMatchButton;
    private Button _playCardButton;
    private Button _quitButton;

    private MatchResults _matchResults = new MatchResults();
    private MatchStats _matchStats = new MatchStats();
    private string _playerId;
    private string _remotePlayerId = "";
    private bool _processCardPlay = false;
    private bool _updateRemotePlayerId = false;
    private bool _findingMatch = false;
    private bool _gameOver = false;

    // Lambda opcodes
    private const string REQUEST_FIND_MATCH_OP = "1";

    // these are in order from left-to-right on screen, first 2 are for local player, second 2 for remote player
    private List<Vector3> cardLocationsInUI = new List<Vector3>() { new Vector3(314, 340, 0), new Vector3(710.8f, 340, 0), new Vector3(1217.5f, 331.3f, 0), new Vector3(1618.1f, 345.9f, 0) };
    private List<TMPro.TextMeshProUGUI> cardUIObjects = new List<TMPro.TextMeshProUGUI>();
    private List<CardPlayed> cardsPlayed = new List<CardPlayed>();

    public GameObject CardPrefab;

    public TMPro.TextMeshProUGUI localClientPlayerName;
    public TMPro.TextMeshProUGUI Player1Result;

    public TMPro.TextMeshProUGUI remoteClientPlayerName;
    public TMPro.TextMeshProUGUI Player2Result;

    // GameLift server opcodes 
    // An opcode defined by client and your server script that represents a custom message type
    public const int OP_CODE_PLAYER_ACCEPTED = 113;
    public const int GAME_START_OP = 201;
    public const int GAMEOVER_OP = 209;
    public const int PLAY_CARD_OP = 300;
    public const int DRAW_CARD_ACK_OP = 301;

    public async void OnFindMatchPressed()
    {
        Debug.Log("Find match pressed");
        _findingMatch = true;

        FindMatch matchMessage = new FindMatch(REQUEST_FIND_MATCH_OP, _playerId);
        string jsonPostData = JsonUtility.ToJson(matchMessage);
        // Debug.Log(jsonPostData);

        localClientPlayerName.text = _playerId;

        string response = await _apiManager.Post(GameSessionPlacementEndpoint, jsonPostData);
        GameSessionPlacementInfo gameSessionPlacementInfo = JsonConvert.DeserializeObject<GameSessionPlacementInfo>(response);

        // Debug.Log(gameSessionPlacementInfo);

        if (gameSessionPlacementInfo != null)
        {
            // GameSessionPlacementInfo is a model used to handle both game session placement and game session search results from the Lambda response.
            if (gameSessionPlacementInfo.PlacementId != null)
            {
                // The response was from a placement request
                Debug.Log("Game session placement request submitted.");

                // Debug.Log(gameSessionPlacementInfo.PlacementId);

                // subscribe to receive the player placement fulfillment notification
                await SubscribeToFulfillmentNotifications(gameSessionPlacementInfo.PlacementId);

            }
            else if (gameSessionPlacementInfo.GameSessionId != null)
            {
                // The response was for a found game session which also contains info for created player session
                Debug.Log("Game session found!");
                // Debug.Log(gameSessionPlacementInfo.GameSessionId);

                Int32.TryParse(gameSessionPlacementInfo.Port, out int portAsInt);

                // Once connected, the Realtime service moves the Player session from Reserved to Active, which means we're ready to connect.
                // https://docs.aws.amazon.com/gamelift/latest/apireference/API_CreatePlayerSession.html
                EstablishConnectionToRealtimeServer(gameSessionPlacementInfo.IpAddress, portAsInt, gameSessionPlacementInfo.PlayerSessionId);
            }
            else
            {
                Debug.Log("Game session response not valid...");
            }
        }

        _findMatchButton.gameObject.SetActive(false); // remove from UI
    }

    private async Task<bool> SubscribeToFulfillmentNotifications(string placementId)
    {
        PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = await _sqsMessageProcessing.SubscribeToFulfillmentNotifications(placementId);

        if (playerPlacementFulfillmentInfo != null)
        {
            Debug.Log("Player placement was fulfilled...");
            // Debug.Log("Placed Player Sessions count: " + playerPlacementFulfillmentInfo.placedPlayerSessions.Count);

            // Once connected, the Realtime service moves the Player session from Reserved to Active, which means we're ready to connect.
            // https://docs.aws.amazon.com/gamelift/latest/apireference/API_CreatePlayerSession.html
            EstablishConnectionToRealtimeServer(playerPlacementFulfillmentInfo.ipAddress, playerPlacementFulfillmentInfo.port,
                playerPlacementFulfillmentInfo.placedPlayerSessions[0].playerSessionId);

            return true;
        }
        else
        {
            Debug.Log("Player placement was null, something went wrong...");
            return false;
        }
    }

    private void EstablishConnectionToRealtimeServer(string ipAddress, int port, string playerSessionId)
    {
        int localUdpPort = GetAvailableUdpPort();

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);
        string payload = JsonUtility.ToJson(realtimePayload);

        _realTimeClient = new RealTimeClient(ipAddress, port, localUdpPort, playerSessionId, payload, ConnectionType.RT_OVER_WS_UDP_UNSECURED);
        _realTimeClient.CardPlayedEventHandler += OnCardPlayedEvent;
        _realTimeClient.RemotePlayerIdEventHandler += OnRemotePlayerIdEvent;
        _realTimeClient.GameOverEventHandler += OnGameOverEvent;
    }

    void OnCardPlayedEvent(object sender, CardPlayedEventArgs cardPlayedEventArgs)
    {
        Debug.Log($"The card {cardPlayedEventArgs.card} was played by {cardPlayedEventArgs.playedBy}, and had {cardPlayedEventArgs.plays} plays.");
        CardPlayed(cardPlayedEventArgs);
    }

    private void CardPlayed(CardPlayedEventArgs cardPlayedEventArgs)
    {
        Debug.Log($"card played {cardPlayedEventArgs.card}");

        if (cardPlayedEventArgs.playedBy == _playerId)
        {
            Debug.Log("local card played");
            _matchStats.localPlayerCardsPlayed.Add(cardPlayedEventArgs.card.ToString());

        }
        else
        {
            Debug.Log("remote card played");
            _matchStats.remotePlayerCardsPlayed.Add(cardPlayedEventArgs.card.ToString());
        }

        _processCardPlay = true;
    }

    void OnRemotePlayerIdEvent(object sender, RemotePlayerIdEventArgs remotePlayerIdEventArgs)
    {
        Debug.Log($"Remote player id received: {remotePlayerIdEventArgs.remotePlayerId}.");
        UpdateRemotePlayerId(remotePlayerIdEventArgs);
    }

    private void UpdateRemotePlayerId(RemotePlayerIdEventArgs remotePlayerIdEventArgs)
    {
        _remotePlayerId = remotePlayerIdEventArgs.remotePlayerId;
        _updateRemotePlayerId = true;
    }

    void OnGameOverEvent(object sender, GameOverEventArgs gameOverEventArgs)
    {
        Debug.Log($"Game over event received with winner: {gameOverEventArgs.matchResults.winnerId}.");
        this._matchResults = gameOverEventArgs.matchResults;
        this._gameOver = true;
    }

    void Update()
    {
        if (_findingMatch)
        {
            _findingMatch = false;
            _findMatchButton.enabled = false;
            _findMatchButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Searching...";
        }

        if (_realTimeClient != null && _realTimeClient.GameStarted)
        {
            _playCardButton.gameObject.SetActive(true);
            _realTimeClient.GameStarted = false;
        }

        if (_updateRemotePlayerId)
        {
            _updateRemotePlayerId = false;
            remoteClientPlayerName.text = _remotePlayerId;
        }

        // Card plays - there's a better way to do this...
        if (_processCardPlay)
        {
            _processCardPlay = false;

            ProcessCardPlay();
        }

        // determine match results once game is over
        if (this._gameOver == true)
        {
            this._gameOver = false;
            DisplayMatchResults();
        }
    }

    private void ProcessCardPlay()
    {
        for (int cardIndex = 0; cardIndex < _matchStats.localPlayerCardsPlayed.Count; cardIndex++)
        {
            cardUIObjects[cardIndex].text = _matchStats.localPlayerCardsPlayed[cardIndex];
        }

        for (int cardIndex = 0; cardIndex < _matchStats.remotePlayerCardsPlayed.Count; cardIndex++)
        {
            // Added + 2 because cardUIObjects holds all UI cards, first 2 are local, last 2 are remote 
            cardUIObjects[cardIndex + 2].text = _matchStats.remotePlayerCardsPlayed[cardIndex];
        }
    }

    private void DisplayMatchResults()
    {
        string localPlayerResults = "";
        string remotePlayerResults = "";

        if (_matchResults.winnerId == _playerId)
        {
            localPlayerResults = "You WON! Score ";
            remotePlayerResults = "Loser. Score ";
        }
        else
        {
            remotePlayerResults = "WINNER! Score ";
            localPlayerResults = "You Lost. Score ";
        }

        if (_matchResults.playerOneId == _playerId)
        {
            // our local player matches player one data
            localPlayerResults += _matchResults.playerOneScore;
            remotePlayerResults += _matchResults.playerTwoScore;
        }
        else
        {
            // our local player matches player two data
            localPlayerResults += _matchResults.playerTwoScore;
            remotePlayerResults += _matchResults.playerOneScore;
        }

        Player1Result.text = localPlayerResults;
        Player2Result.text = remotePlayerResults;
    }

    private void BuildCardsIntoUI()
    {
        // build cards into UI from prefab
        GameObject canvas = GameObject.Find("PlayPanel");
        foreach (Vector3 cardLocation in cardLocationsInUI)
        {
            GameObject card = Instantiate(CardPrefab, cardLocation, Quaternion.identity, canvas.transform);
            cardUIObjects.Add(card.GetComponentInChildren<TMPro.TextMeshProUGUI>());
        }

        CardPrefab.gameObject.SetActive(false); // turn off source prefab 
    }

    public void OnPlayCardPressed()
    {
        Debug.Log("Play card pressed");

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);

        // Use the Realtime client's SendMessage function to pass data to the server
        _realTimeClient.SendMessage(PLAY_CARD_OP, realtimePayload);
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

        BuildCardsIntoUI();
    }

    public static int GetAvailableUdpPort()
    {
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            socket.Bind(DefaultLoopbackEndpoint);
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }
    }

    void OnApplicationQuit()
    {
        // clean up the connection if the game gets killed
        if (_realTimeClient != null && _realTimeClient.IsConnected())
        {
            _realTimeClient.Disconnect();
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
}

public class MatchStats
{
    public List<string> localPlayerCardsPlayed = new List<string>();
    public List<string> remotePlayerCardsPlayed = new List<string>();
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

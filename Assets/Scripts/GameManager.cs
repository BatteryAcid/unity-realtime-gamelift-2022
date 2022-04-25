
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

    private MatchStats _matchStats = new MatchStats();
    private bool _processCardPlay = false;

    // these are listed in order from right-to-left on screen, first 2 are for local player, second 2 for remote player
    private List<Vector3> cardLocationsInUI = new List<Vector3>() { new Vector3(314, 340, 0), new Vector3(710.8f, 340, 0), new Vector3(1217.5f, 331.3f, 0), new Vector3(1618.1f, 345.9f, 0) };
    private List<TMPro.TextMeshProUGUI> cardUIObjects = new List<TMPro.TextMeshProUGUI>();


    // GameLift server opcodes 
    // An opcode defined by client and your server script that represents a custom message type
    public const int OP_CODE_PLAYER_ACCEPTED = 113;
    public const int GAME_START_OP = 201;
    public const int GAMEOVER_OP = 209;
    public const int PLAY_CARD_OP = 300;
    public const int DRAW_CARD_ACK_OP = 301;

    //
    public GameObject CardPrefab;

    public TMPro.TextMeshProUGUI localClientPlayerName;
    //public TMPro.TextMeshProUGUI Player1Card1;
    //public TMPro.TextMeshProUGUI Player1Card2;
    public TMPro.TextMeshProUGUI Player1Result;

    public TMPro.TextMeshProUGUI remoteClientPlayerName;
    //public TMPro.TextMeshProUGUI Player2Card1;
    //public TMPro.TextMeshProUGUI Player2Card2;
    public TMPro.TextMeshProUGUI Player2Result;

    public List<CardPlayed> cardsPlayed = new List<CardPlayed>();

    public async void OnFindMatchPressed()
    {
        Debug.Log("Find match pressed");
        _findingMatch = true;

        FindMatch matchMessage = new FindMatch(REQUEST_FIND_MATCH_OP, _playerId);
        string jsonPostData = JsonUtility.ToJson(matchMessage);
        // Debug.Log(jsonPostData);

        localClientPlayerName.text = _playerId;

        GameSessionPlacementInfo gameSessionPlacementInfo = await _apiManager.PostGetResponse(GameSessionPlacementEndpoint, jsonPostData);
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

        _findingMatch = false;
        _findMatchButton.gameObject.SetActive(false);
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

        _realTimeClient = new RealTimeClient(ipAddress, port, localUdpPort, playerSessionId, payload, ConnectionType.RT_OVER_WS_UDP_UNSECURED, this);
        _realTimeClient.CardPlayedEventHandler += OnCardPlayedEvent;
    }

    void OnCardPlayedEvent(object sender, CardPlayedEventArgs cardPlayedEventArgs)
    {
        Debug.Log($"The card {cardPlayedEventArgs.card} was played by {cardPlayedEventArgs.playedBy}, and had {cardPlayedEventArgs.plays} plays.");
        CardPlayed(cardPlayedEventArgs);
    }

    public void CardPlayed(CardPlayedEventArgs cardPlayedEventArgs)
    {
        Debug.Log("card played");
        Debug.Log(cardPlayedEventArgs.card);

        if (cardPlayedEventArgs.playedBy == _playerId)
        {
            Debug.Log("local card played");
            _matchStats.localPlayerCardsPlayed.Add(cardPlayedEventArgs.card.ToString());

            Debug.Log(_matchStats.localPlayerCardsPlayed[cardPlayedEventArgs.plays - 1]);

            // remove...?
            // local card played
            //if (cardPlayedEventArgs.plays == 1)
            //{
            //    _localPlayerFirstCardPlayUI = cardPlayedEventArgs.card.ToString();
            //} else if (cardPlayedEventArgs.plays == 2)
            //{
            //    _localPlayerSecondCardPlayUI = cardPlayedEventArgs.card.ToString();
            //}
        } else
        {
            Debug.Log("remote card played");
            _matchStats.remotePlayerCardsPlayed.Add(cardPlayedEventArgs.card.ToString());
            Debug.Log(_matchStats.remotePlayerCardsPlayed[cardPlayedEventArgs.plays - 1]);

            // remote card played
            //if (cardPlayedEventArgs.plays == 1)
            //{
            //    _remotePlayerFirstCardPlayUI = cardPlayedEventArgs.card.ToString();
            //}
            //else if (cardPlayedEventArgs.plays == 2)
            //{
            //    _remotePlayerSecondCardPlayUI = cardPlayedEventArgs.card.ToString();
            //}
        }

        _processCardPlay = true;
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
        if (_processCardPlay)
        {
            _processCardPlay = false;

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


        // TODO: think of a better way to manage the card play and updating the UI...
        //if (_localPlayerFirstCardPlayUI != "")
        //{
        //    Player1Card1.text = _localPlayerFirstCardPlayUI;
        //    _localPlayerFirstCardPlayUI = "";
        //}

        //if (_localPlayerSecondCardPlayUI != "")
        //{
        //    Player1Card2.text = _localPlayerSecondCardPlayUI;
        //    _localPlayerSecondCardPlayUI = "";
        //}

        //if (_remotePlayerFirstCardPlayUI != "")
        //{
        //    Player2Card1.text = _remotePlayerFirstCardPlayUI;
        //    _remotePlayerFirstCardPlayUI = "";
        //}

        //if (_remotePlayerSecondCardPlayUI != "")
        //{
        //    Player2Card2.text = _remotePlayerSecondCardPlayUI;
        //    _remotePlayerSecondCardPlayUI = "";
        //}

        if (_realTimeClient != null && _realTimeClient.GameOver == true)
        {
            MatchResults matchResults = _realTimeClient.GetMatchResults();

            string localPlayerResults = "";
            string remotePlayerResults = "";

            if (matchResults.winnerId == _playerId)
            {
                localPlayerResults = "You WON! Score ";
                remotePlayerResults = "Loser. Score ";
            }
            else
            {
                remotePlayerResults = "WINNER! Score ";
                localPlayerResults = "You Lost. Score ";
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

        // build cards into UI from prefab
        GameObject canvas = GameObject.Find("PlayPanel");
        foreach (Vector3 cardLocation in cardLocationsInUI)
        {
            GameObject card = Instantiate(CardPrefab, cardLocation, Quaternion.identity, canvas.transform);
            cardUIObjects.Add(card.GetComponentInChildren<TMPro.TextMeshProUGUI>());
        }

        CardPrefab.gameObject.SetActive(false); // turn off source prefab 
    }

    public void UpdateRemotePlayerUI(string remotePlayerId)
    {
        _updateRemotePlayerId = remotePlayerId;
    }

    public void OnPlayCardPressed()
    {
        Debug.Log("Play card pressed");

        RealtimePayload realtimePayload = new RealtimePayload(_playerId);

        // Use the Realtime client's SendMessage function to pass data to the server
        _realTimeClient.SendMessage(PLAY_CARD_OP, realtimePayload);
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

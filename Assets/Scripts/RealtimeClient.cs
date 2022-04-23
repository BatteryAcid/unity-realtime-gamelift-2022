using UnityEngine;
using System;
using System.Text;
using Aws.GameLift.Realtime;
using Aws.GameLift.Realtime.Event;
using Aws.GameLift.Realtime.Types;
using Newtonsoft.Json;

/**
 * @BatteryAcid
 * I've modified this example to demonstrate a simple two player card game.
 * 
 * The base code is sourced from the AWS GameLift Docs: 
 * https://docs.aws.amazon.com/gamelift/latest/developerguide/realtime-client.html#realtime-client-examples
 *
 * -----
 * 
 * An example client that wraps the GameLift Realtime client SDK
 * 
 * You can redirect logging from the SDK by setting up the LogHandler as such:
 * ClientLogger.LogHandler = (x) => Console.WriteLine(x);
 *
 * Based on: https://docs.aws.amazon.com/gamelift/latest/developerguide/realtime-client.html#realtime-client-examples
 */
public class RealTimeClient
{
    public Aws.GameLift.Realtime.Client Client { get; private set; }
    public bool OnCloseReceived { get; private set; }
    public bool GameStarted = false;
    public bool GameOver = false;
    private GameManager _gameManager;

    private MatchResults matchResults;

    /// <summary>
    /// Initialize a client for GameLift Realtime and connects to a player session.
    /// </summary>
    /// <param name="endpoint">The endpoint for the GameLift Realtime server to connect to</param>
    /// <param name="tcpPort">The TCP port for the GameLift Realtime server</param>
    /// <param name="localUdpPort">Local Udp listen port to use</param>
    /// <param name="playerSessionId">The player session Id in use - from CreatePlayerSession</param>
    /// <param name="connectionPayload"></param>
    /// 
    public RealTimeClient(string endpoint, int tcpPort, int localUdpPort, string playerSessionId, string connectionPayload, ConnectionType connectionType, GameManager gameManagerIn)
    {
        _gameManager = gameManagerIn;

        this.OnCloseReceived = false;

        // Create a client configuration to specify a secure or unsecure connection type
        // Best practice is to set up a secure connection using the connection type RT_OVER_WSS_DTLS_TLS12.
        ClientConfiguration clientConfiguration = new ClientConfiguration()
        {
            // C# notation to set the field ConnectionType in the new instance of ClientConfiguration
            ConnectionType = connectionType
        };

        // Create a Realtime client with the client configuration            
        Client = new Client(clientConfiguration);

        Client = new Aws.GameLift.Realtime.Client(clientConfiguration);
        Client.ConnectionOpen += new EventHandler(OnOpenEvent);
        Client.ConnectionClose += new EventHandler(OnCloseEvent);
        Client.GroupMembershipUpdated += new EventHandler<GroupMembershipEventArgs>(OnGroupMembershipUpdate);
        Client.DataReceived += new EventHandler<DataReceivedEventArgs>(OnDataReceived);

        ConnectionToken token = new ConnectionToken(playerSessionId, StringToBytes(connectionPayload));
        Client.Connect(endpoint, tcpPort, localUdpPort, token);
    }

    /// <summary>
    /// Example of sending to a custom message to the server.
    /// 
    /// Server could be replaced by known peer Id etc.
    /// </summary>
    /// <param name="payload">Custom payload to send with message</param>
    public void SendMessage(int opcode, RealtimePayload realtimePayload)
    {
        // You can also pass in the DeliveryIntent depending on your message delivery requirements
        // https://docs.aws.amazon.com/gamelift/latest/developerguide/realtime-sdk-csharp-ref-datatypes.html#realtime-sdk-csharp-ref-datatypes-rtmessage

        string payload = JsonUtility.ToJson(realtimePayload);
        //Debug.Log(payload);

        Client.SendMessage(Client.NewMessage(opcode)
            .WithDeliveryIntent(DeliveryIntent.Reliable)
            .WithTargetPlayer(Constants.PLAYER_ID_SERVER)
            .WithPayload(StringToBytes(payload)));
    }

    /**
     *  Handle data received from the Realtime server 
     */
    public virtual void OnDataReceived(object sender, DataReceivedEventArgs e)
    {
        Debug.Log("OnDataReceived");

        // handle message based on OpCode
        switch (e.OpCode)
        {
            
            case GameManager.OP_CODE_PLAYER_ACCEPTED:
                Debug.Log("Player accepted into game session!");
                
                // TODO: this would get moved to match started op
                GameStarted = true;
                break;

            case GameManager.GAME_START_OP:
                Debug.Log("Start game op received...");

                string startGameData = BytesToString(e.Data);
                Debug.Log(startGameData);

                // TODO: Should this go here? yes, as we have to wait until 2nd player joins before starting.  While local testing, we have to keep it in the first case...
                //GameStarted = true;

                StartMatch startMatch = JsonConvert.DeserializeObject<StartMatch>(startGameData);

                _gameManager.UpdateRemotePlayerUI(startMatch.remotePlayerId);

                break;

            case GameManager.DRAW_CARD_ACK_OP:
                Debug.Log("Player draw card ack...");

                string data = BytesToString(e.Data);
                // Debug.Log(data);

                CardPlayed cardPlayedMessage = JsonConvert.DeserializeObject<CardPlayed>(data);
                // Debug.Log(cardPlayedMessage.playedBy);
                // Debug.Log(cardPlayedMessage.card);

                _gameManager.CardPlayed(cardPlayedMessage);

                break;

            case GameManager.GAMEOVER_OP:
                Debug.Log("Game over op...");

                string gameoverData = BytesToString(e.Data);
                Debug.Log(gameoverData);

                matchResults = JsonConvert.DeserializeObject<MatchResults>(gameoverData);
                GameOver = true;

                break;

            default:
                Debug.Log("OpCode not found: " + e.OpCode);
                break;
        }
    }

    public MatchResults GetMatchResults()
    {
        return matchResults;
    }

    /**
     * Handle connection open events
     */
    public void OnOpenEvent(object sender, EventArgs e)
    {
    }

    /**
     * Handle connection close events
     */
    public void OnCloseEvent(object sender, EventArgs e)
    {
        OnCloseReceived = true;
    }

    /**
     * Handle Group membership update events 
     */
    public void OnGroupMembershipUpdate(object sender, GroupMembershipEventArgs e)
    {
    }

    public void Disconnect()
    {
        if (Client.Connected)
        {
            Client.Disconnect();
        }
    }

    public bool IsConnected()
    {
        return Client.Connected;
    }

    /**
     * Helper method to simplify task of sending/receiving payloads.
     */
    public static byte[] StringToBytes(string str)
    {
        return Encoding.UTF8.GetBytes(str);
    }

    /**
     * Helper method to simplify task of sending/receiving payloads.
     */
    public static string BytesToString(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }
}

using System;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon;
using Amazon.CognitoIdentity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class SQSMessageProcessing : MonoBehaviour
{
    private const string IdentityPool = "YOUR_IDENTITY_POOL_ID";
    private const string SQSURL = "YOUR_SQS_ENDPOINT";
    private const int MaxMessages = 1;
    private const int WaitTime = 20;

    private AmazonSQSClient _sqsClient;
    private Coroutine _fulfillmentFailsafeCoroutine;
    private bool _fulfillmentMessageReceived = false;

    public async Task<PlayerPlacementFulfillmentInfo> SubscribeToFulfillmentNotifications(string placementId)
    {
        Debug.Log("SubscribeToFulfillmentNotifications...");

        _fulfillmentFailsafeCoroutine = StartCoroutine(FailsafeTimer());
        PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = null;

        do
        {
            var msg = await GetMessage(_sqsClient, SQSURL, WaitTime);
            if (msg.Messages.Count != 0)
            {
                Debug.Log("SubscribeToFulfillmentNotifications received message...");

                playerPlacementFulfillmentInfo = ConvertMessage(msg.Messages[0].Body);

                // make sure this notification was for our player
                if (playerPlacementFulfillmentInfo != null && playerPlacementFulfillmentInfo.placementId == placementId)
                {
                    Debug.Log("Placement fulfilled, break loop...");
                    _fulfillmentMessageReceived = true; // break loop

                    // Delete consumed message as it is no longer necessary to leave it in the queue.
                    await DeleteMessage(_sqsClient, msg.Messages[0], SQSURL);

                    if (_fulfillmentFailsafeCoroutine != null)
                    {
                        // kill failsafe coroutine
                        StopCoroutine(_fulfillmentFailsafeCoroutine);
                    }
                }

                // we don't break loop here because the message received wasn't for this player
            }
        } while (!_fulfillmentMessageReceived);

        return playerPlacementFulfillmentInfo;
    }

    private static PlayerPlacementFulfillmentInfo ConvertMessage(string convertMessage)
    {
        Debug.Log("ConvertMessage...");
        // Debug.Log(convertMessage);

        string cleanedMessage = CleanupMessage(convertMessage);

        SQSMessage networkMessage = JsonConvert.DeserializeObject<SQSMessage>(cleanedMessage);

        if (networkMessage != null)
        {
            // Debug.Log("networkMessage.Message: " + networkMessage.Message);
            // Debug.Log("networkMessage.TopicArn: " + networkMessage.TopicArn);
            // Debug.Log("networkMessage.Type: " + networkMessage.Type);

            if (networkMessage.Type == "Notification")
            {
                if (networkMessage.Message != null)
                {
                    // Debug.Log("networkMessage.Message: " + networkMessage.Message.id);

                    if (networkMessage.Message.detail != null)
                    {
                        // Debug.Log("ipAddress: " + networkMessage.Message.detail.ipAddress);
                        // Debug.Log("port: " + networkMessage.Message.detail.port);
                        // Debug.Log("placementId: " + networkMessage.Message.detail.placementId);
                        // Debug.Log("gameSessionArn: " + networkMessage.Message.detail.gameSessionArn);

                        Int32.TryParse(networkMessage.Message.detail.port, out int portAsInt);

                        PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = new PlayerPlacementFulfillmentInfo
                        {
                            ipAddress = networkMessage.Message.detail.ipAddress,
                            port = portAsInt,
                            placementId = networkMessage.Message.detail.placementId,
                            gameSessionId = ParseGameSessionIdFromArn(networkMessage.Message.detail.gameSessionArn),
                            placedPlayerSessions = networkMessage.Message.detail.placedPlayerSessions
                        };
                        return playerPlacementFulfillmentInfo;

                        // Side note: If you're creating a game session as a group, you can use the placePlayerSessions
                        // to perform any post create-gameSession logic, like if in a steam group, you could send out a
                        // steam message to the other members with some pre-match game properties or whatever clean up you need to do.
                    }
                    else
                    {
                        Debug.Log("NetworkMessage.Message.detail was null");
                    }
                }
                else
                {
                    Debug.Log("NetworkMessage.Message was null");
                }
            }
            else
            {
                Debug.Log("NetworkMessage.Type was not Notification");
            }
        }
        else
        {
            Debug.Log("NetworkMessage was null");
        }
        return null;
    }

    // Method to read a message from the given queue
    private static async Task<ReceiveMessageResponse> GetMessage(IAmazonSQS sqsClient, string qUrl, int waitTime = 0)
    {
        return await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = qUrl,
            MaxNumberOfMessages = MaxMessages,
            WaitTimeSeconds = waitTime
        });
    }

    private static string CleanupMessage(string messageToClean)
    {
        // The Message is JSON inside a string, this removes the quotes around the braces so it can be serialized as an object
        string cleanedMessage = messageToClean.Replace("\"{", "{");
        cleanedMessage = cleanedMessage.Replace("}\"", "}");
        // Debug.Log("cleanedMessage1 " + cleanedMessage);

        // remove escape slashes from message string so it can be properly read as object
        cleanedMessage = cleanedMessage.Replace("\\", "");
        // Debug.Log("cleanedMessage2 " + cleanedMessage);

        return cleanedMessage;
    }

    // Method to delete a message from a queue
    private static async Task DeleteMessage(IAmazonSQS sqsClient, Message message, string qUrl)
    {
        Debug.Log($"\nDeleting message {message.MessageId} from queue...");
        try
        {
            await sqsClient.DeleteMessageAsync(qUrl, message.ReceiptHandle);
        }
        catch (System.Exception ex)
        {
            Debug.Log("Failed to delete SQS queue message: " + qUrl + ", " + message.MessageId + ", exception: " + ex);
        }
    }

    void Start()
    {
        CognitoAWSCredentials credentials = new CognitoAWSCredentials(
            IdentityPool, // Your Identity pool ID
            RegionEndpoint.USEast1 // Your GameLift Region
        );

        _sqsClient = new AmazonSQSClient(credentials, Amazon.RegionEndpoint.USEast1);
    }

    private static string ParseGameSessionIdFromArn(string gameSessionArn)
    {
        // EX:
        //   arn:aws:gamelift:us-east-1::gamesession/fleet-123e953d-aeef-41c9-8711-3b8f6ee7bb8f/abc21ec4-7856-421d-9cd1-0acc539f3e0e
        // Splitting on / means id is at index 2
        string[] arnSections = gameSessionArn.Split('/');
        return arnSections[2];
    }

    // This creates a timer to kill the fulfillment listener above if it doesn't receive a response within the wait time below.
    private IEnumerator FailsafeTimer()
    {
        Debug.Log("FailsafeTimer setup...");
        yield return new WaitForSecondsRealtime(10 * 60); // 10 minutes
        Debug.Log("FailsafeTimer activated and stopping loop...");
        _fulfillmentMessageReceived = true;
    }
}

// The lambda function handles both cases where there are existing game sessions and also
// when there are none and we submit a placement request.
// To make the demo easy, I combined the placement and game session response types into one model.
[System.Serializable]
public class GameSessionPlacementInfo
{
    // placement details
    public string PlacementId;
    public string GameSessionQueueName;

    // game session details
    public string GameSessionId;
    public string FleetId;
    public string FleetArn;
    public string IpAddress;
    public string DnsName;
    public string Port;
    public string Location;
    public string GameSessionStatus;

    // player session details
    public string PlayerSessionId;
    public string PlayerId;
    public string PlayerSessionStatus;

    // shared properties
    public string MaximumPlayerSessionCount;
    public string CurrentPlayerSessionCount;

    // you can also pass game properties if your game needs to send setup data for the match  
}

public class PlayerPlacementFulfillmentInfo
{
    public string ipAddress;
    public int port;
    public string placementId;
    public string gameSessionId;
    public List<GameliftFulfillmentPlacedPlayerSession> placedPlayerSessions;

    public PlayerPlacementFulfillmentInfo() { }
}

[System.Serializable]
public class GameliftFulfillmentMessage
{
    public string id;
    public GameliftFulfillmentMessageDetail detail;
}

[System.Serializable]
public class GameliftFulfillmentMessageDetail
{
    public string placementId;
    public string port;
    public string ipAddress;
    public string gameSessionArn;
    public List<GameliftFulfillmentPlacedPlayerSession> placedPlayerSessions;
}

[System.Serializable]
public class GameliftFulfillmentPlacedPlayerSession
{
    public string playerId;
    public string playerSessionId;
}

[System.Serializable]
public class SQSMessage
{
    public string Type;
    public string MessageId;
    public string TopicArn;
    public GameliftFulfillmentMessage Message;
    public string Timestamp;
}

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
    private AmazonSQSClient sqsClient;
    private static string SQSURL = "https://sqs.us-east-1.amazonaws.com/654368844800/realtime-gamelift-2022-fulfillment-queue-discovery";
    private const int MaxMessages = 1;
    private const int WaitTime = 20;

    private Coroutine fulfillmentFailsafeCoroutine;
    private bool fulfillmentMessageReceived = false;
    private float timeToSearch;
    private float timeOriginal;

    public async Task<PlayerPlacementFulfillmentInfo> SubscribeToFulfillmentNotifications(string placementId)
    {
        Debug.Log("SubscribeToFulfillmentNotifications...");

        fulfillmentFailsafeCoroutine = StartCoroutine(FailsafeTimer());
        PlayerPlacementFulfillmentInfo playerPlacementFulfillmentInfo = null;

        do
        {
            var msg = await GetMessage(sqsClient, SQSURL, WaitTime);
            if (msg.Messages.Count != 0)
            {
                Debug.Log("SubscribeToFulfillmentNotifications received message...");

                playerPlacementFulfillmentInfo = ConvertMessage(msg.Messages[0].Body);

                // make sure this notification was for our player
                if (playerPlacementFulfillmentInfo != null && playerPlacementFulfillmentInfo.placementId == placementId)
                {
                    Debug.Log("Placement fulfilled, break loop...");
                    fulfillmentMessageReceived = true; // break loop

                    // Delete consumed message 
                    await DeleteMessage(sqsClient, msg.Messages[0], SQSURL);

                    if (fulfillmentFailsafeCoroutine != null)
                    {
                        // kill failsafe coroutine
                        StopCoroutine(fulfillmentFailsafeCoroutine);
                    }
                }

                // we don't break loop here because the message received wasn't for this player
            }
        } while (!fulfillmentMessageReceived);

        return playerPlacementFulfillmentInfo;
    }

    private static PlayerPlacementFulfillmentInfo ConvertMessage(string convertMessage)
    {
        Debug.Log("ConvertMessage...");
        Debug.Log(convertMessage);

        string cleanedMessage = CleanupMessage(convertMessage);

        SQSMessage networkMessage = JsonConvert.DeserializeObject<SQSMessage>(cleanedMessage);
        if (networkMessage != null)
        {
            Debug.Log("networkMessage.Message: " + networkMessage.Message);
            Debug.Log("networkMessage.TopicArn: " + networkMessage.TopicArn);
            Debug.Log("networkMessage.Type: " + networkMessage.Type);

            if (networkMessage.Type == "Notification")
            {
                if (networkMessage.Message != null)
                {
                    Debug.Log("networkMessage.Message: " + networkMessage.Message.id);

                    if (networkMessage.Message.detail != null)
                    {
                        Debug.Log("ipAddress: " + networkMessage.Message.detail.ipAddress);
                        Debug.Log("port: " + networkMessage.Message.detail.port);
                        Debug.Log("placementId: " + networkMessage.Message.detail.placementId);
                        Debug.Log("gameSessionArn: " + networkMessage.Message.detail.gameSessionArn);

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

                        // Side note: If you're creating a game session as a group, you can use the placePlayerSessions to perform any post create-gameSession logic, like if in a steam group,
                        // you could send out a steam message to the other members with some pre-match game properties or whatever clean up you need to do.

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
        //Debug.Log("cleanedMessage1 " + cleanedMessage);

        // remove escape slashes from message string so it can be properly read as object
        cleanedMessage = cleanedMessage.Replace("\\", "");
        //Debug.Log("cleanedMessage2 " + cleanedMessage);

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

    private static string ParseGameSessionIdFromArn(string gameSessionArn)
    {
        //arn:aws:gamelift:us-east-1::gamesession/fleet-31fe953d-aeef-41c9-8711-3b8f6ee7bb8f/a8221ec4-7856-421d-9cd1-0acc539f3e0e
        // splitting on / means id is at index 2
        string[] arnSections = gameSessionArn.Split('/');
        return arnSections[2];
    }

    void Start()
    {
        // TODO: think we need CognitoIdentity dll:https://docs.aws.amazon.com/sdkfornet/latest/apidocs/items/TCognitoIdentityCognitoAWSCredentialsNET35.html
        CognitoAWSCredentials credentials = new CognitoAWSCredentials(
            "us-east-1:0f2ccd51-c118-4358-99ed-5fb8ac8322c7", // Identity pool ID
            RegionEndpoint.USEast1 // Region
        );

        sqsClient = new AmazonSQSClient(credentials, Amazon.RegionEndpoint.USEast1);
        // TODO: update this to leverage the Identity Pool credentials, not this hardcoded stuff
        //sqsClient = new AmazonSQSClient("asdfasdf", "asdfasdf", Amazon.RegionEndpoint.USEast1);
    }

    private IEnumerator FailsafeTimer()
    {
        Debug.Log("FailsafeTimer setup...");
        yield return new WaitForSecondsRealtime(10 * 60); // 10 minutes
        Debug.Log("FailsafeTimer activated and stopping loop...");
        fulfillmentMessageReceived = true;
    }
}

[System.Serializable]
public class GameSessionPlacementInfo
{
    public string PlacementId;
    public string GameSessionQueueName;
    public string Status;
    public string MaximumPlayerSessionCount;
  //  PlacementId: '1d381bfd-f560-43cd-9065-8e0315c6f245',
  //GameSessionQueueName: '29OCT2021-queue',
  //Status: 'PENDING',
  //GameProperties: [],
  //MaximumPlayerSessionCount: 2,
  //PlayerLatencies: [],
  //StartTime: 2022-04-07T18:25:40.547Z
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

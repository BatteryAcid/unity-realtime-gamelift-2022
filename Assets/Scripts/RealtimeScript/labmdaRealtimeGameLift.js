var aws = require('aws-sdk');
const gameLiftClient = new aws.GameLift({ region: 'us-east-1' });
// var ddb = new aws.DynamoDB();
const { v4: uuidv4 } = require('uuid');

const REQUEST_FIND_MATCH = "1";
const MAX_PLAYER_COUNT = 2;

async function getActiveQueue() {
    var input = {
        "Limit": 1
    }
    
    return await gameLiftClient.describeGameSessionQueues(input).promise().then(data => {
        console.log(data);
        
        if (data.GameSessionQueues && data.GameSessionQueues.length > 0) {
            // for now just grab the first Queue
            console.log("we have some queues");
            return data.GameSessionQueues[0];
        }
        else {
            console.log("No queues available");
            return [];
        }
    }).catch(err => {
        console.log(err);
        return [];
    });
}

async function searchGameSessions(targetAliasARN) {
    var gameSessionFilterExpression = "hasAvailablePlayerSessions=true";

    var searchGameSessionsRequest = {
        AliasId: targetAliasARN,
        FilterExpression: gameSessionFilterExpression,
        SortExpression: "creationTimeMillis ASC"
    }

    return await gameLiftClient.searchGameSessions(searchGameSessionsRequest).promise().then(data => {
        console.log(data);
        if (data.GameSessions && data.GameSessions.length > 0) {
            console.log("we have game sessions");
            return data.GameSessions[0]
        }
        else {
            console.log("no game sessions");
            return null;
        }
    }).catch(err => {
        console.log(err);
        return null;
    });
}

async function createGameSessionPlacement(targetQueueName, playerId) {
    console.log("createGameSessionPlacement");
    var createSessionInQueueRequest = {
        GameSessionQueueName: targetQueueName,
        PlacementId: uuidv4(), // generate unique placement id
        MaximumPlayerSessionCount: MAX_PLAYER_COUNT,
        DesiredPlayerSessions: [{
            PlayerId: playerId   
        }]
    };
    console.log("calling startGameSessionPlacement...");
    return await gameLiftClient.startGameSessionPlacement(createSessionInQueueRequest).promise().then(data => {
        console.log(data);
        return data;
        
    }).catch(err => {
        console.log(err);
        return [];
    });
}

async function createPlayerSession(playerId, gameSessionId) {
    var createPlayerSessionRequest = {
      GameSessionId: gameSessionId,
      PlayerId: playerId
    };
    
    return await gameLiftClient.createPlayerSession(createPlayerSessionRequest).promise().then(data => {
        console.log(data);
        return data;
    }).catch(err => {
        console.log(err);
        return null;
    });
    
}

exports.handler = async (event, context, callback) => {
    // insert code to be executed by your lambda trigger
    console.log("inside function...");
    console.log("environment: " + process.env.ENV);
    console.log(JSON.stringify(event, null, 2));

    let message = JSON.parse(event.body);
    console.log("message: %j", message);
    
    let responseMsg = {};

    if (message && message.opCode) {

        switch (message.opCode) {
            case REQUEST_FIND_MATCH:
                console.log("opCode 1 hit");

                var activeQueue = await getActiveQueue();
                console.log(activeQueue);

                var gameSession = await searchGameSessions(activeQueue.Destinations[0].DestinationArn);

                if (gameSession) {
                    console.log("We have a game session to join!");
                    console.log(gameSession);
                    
                    console.log("Creating player session...");
                    var playerSession = await createPlayerSession(message.playerId, gameSession.GameSessionId);
                    console.log("Player session created");
                    console.log(playerSession);
                    
                    responseMsg = playerSession.PlayerSession;
                    responseMsg.PlayerSessionStatus = playerSession.PlayerSession.Status;
                    responseMsg.GameSessionId = gameSession.GameSessionId;
                    responseMsg.GameSessionStatus = gameSession.Status;
                    
                }
                else {
                    console.log("No game sessions to join! " + activeQueue.Name);
                    var gameSessionPlacement = await createGameSessionPlacement(activeQueue.Name, message.playerId);
                    console.log(gameSessionPlacement.GameSessionPlacement);
                    responseMsg = gameSessionPlacement.GameSessionPlacement;
                }

                break;
        }
    }


    return callback(null, {
        statusCode: 200,
        body: JSON.stringify(
            responseMsg
        )
    });
};

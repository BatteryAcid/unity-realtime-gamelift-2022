// Original source: https://docs.aws.amazon.com/gamelift/latest/developerguide/realtime-script.html

// Example Realtime Server Script
'use strict';

// Example override configuration
const configuration = {
    pingIntervalTime: 30000
};

// Timing mechanism used to trigger end of game session. Defines how long, in milliseconds, between each tick in the example tick loop
const tickTime = 1000;

// Defines how to long to wait in Seconds before beginning early termination check in the example tick loop
const minimumElapsedTime = 30;//120;

var session;                        // The Realtime server session object
var logger;                         // Log at appropriate level via .info(), .warn(), .error(), .debug()
var startTime;                      // Records the time the process started
var activePlayers = 0;              // Records the number of connected players
var onProcessStartedCalled = false; // Record if onProcessStarted has been called

// Example custom op codes for user-defined messages
// Any positive op code number can be defined here. These should match your client code.
const OP_CODE_CUSTOM_OP1 = 111;
const OP_CODE_CUSTOM_OP1_REPLY = 112;
const OP_CODE_PLAYER_ACCEPTED = 113;
const OP_CODE_DISCONNECT_NOTIFICATION = 114;

// Example groups for user defined groups
// Any positive group number can be defined here. These should match your client code.
const RED_TEAM_GROUP = 1;
const BLUE_TEAM_GROUP = 2;

// @BatteryAcid
const GAMEOVER_OP_CODE = 209;
const PLAY_CARD_OP = 300;
const OPPONENT_DREW_CARD_OP = 301;

let players = [];
let winner = null;
let cardPlays = {};

// Called when game server is initialized, passed server's object of current session
function init(rtSession) {
    session = rtSession;
    logger = session.getLogger();
    logger.info("init");
}

// On Process Started is called when the process has begun and we need to perform any
// bootstrapping.  This is where the developer should insert any code to prepare
// the process to be able to host a game session, for example load some settings or set state
//
// Return true if the process has been appropriately prepared and it is okay to invoke the
// GameLift ProcessReady() call.
function onProcessStarted(args) {
    onProcessStartedCalled = true;
    logger.info("Starting process with args: " + args);
    logger.info("Ready to host games...");

    return true;
}

// Called when a new game session is started on the process
function onStartGameSession(gameSession) {
    logger.info("onStartGameSession: ");
    logger.info(gameSession);
    // Complete any game session set-up

    // Set up an example tick loop to perform server initiated actions
    startTime = getTimeInS();
    tickLoop();
}

// Handle process termination if the process is being terminated by GameLift
// You do not need to call ProcessEnding here
function onProcessTerminate() {
    // Perform any clean up
}

// Return true if the process is healthy
function onHealthCheck() {
    return true;
}

// On Player Connect is called when a player has passed initial validation
// Return true if player should connect, false to reject
function onPlayerConnect(connectMsg) {
    logger.info("onPlayerConnect: " + connectMsg);
    // Perform any validation needed for connectMsg.payload, connectMsg.peerId
    return true;
}

// Called when a Player is accepted into the game
function onPlayerAccepted(player) {
    players.push(player.peerId);

    // This player was accepted -- let's send them a message
    const msg = session.newTextGameMessage(OP_CODE_PLAYER_ACCEPTED, player.peerId,
        "Peer " + player.peerId + " accepted");
    session.sendReliableMessage(msg, player.peerId);
    activePlayers++;
}

// On Player Disconnect is called when a player has left or been forcibly terminated
// Is only called for players that actually connected to the server and not those rejected by validation
// This is called before the player is removed from the player list
function onPlayerDisconnect(peerId) {
    logger.info("onPlayerDisconnect: " + peerId);
    // send a message to each remaining player letting them know about the disconnect
    const outMessage = session.newTextGameMessage(OP_CODE_DISCONNECT_NOTIFICATION,
        session.getServerId(),
        "Peer " + peerId + " disconnected");
    session.getPlayers().forEach((player, playerId) => {
        if (playerId != peerId) {
            session.sendReliableMessage(outMessage, peerId);
        }
    });
    activePlayers--;
}

// @BatteryAcid
// Handle a message to the server
function onMessage(gameMessage) {
    logger.info("onMessage");
    logger.info(gameMessage);
    logger.info(players);

    var payloadRaw = new Buffer.from(gameMessage.payload);
    var payload = JSON.parse(payloadRaw);
    logger.info(payload);
    logger.info(payload.playerId);

    switch (gameMessage.opCode) {
        case PLAY_CARD_OP:
            {
                logger.info("PLAY_CARD_OP hit");

                const cardDrawn = randomIntFromInterval(1, 10);
                addCardDraw(cardDrawn, payload.playerId);

                
                // TODO: need to send a message back to card player
                var testReturnCode = 200;
                const outMessage = session.newTextGameMessage(testReturnCode, session.getServerId(), "OK");
                session.sendReliableMessage(outMessage, gameMessage.sender);
                logger.info("Card draw message ack");

                
                // TODO: need to send message to other player(s) that indicates player drew card
                var allSessionPlayers = players;
                let allPlayersLength = allSessionPlayers.length;
                const cardDrawMsg = session.newTextGameMessage(OPPONENT_DREW_CARD_OP, session.getServerId(), "OK");

                for (let index = 0; index < allPlayersLength; ++index) {
                    if (allSessionPlayers[index] != gameMessage.sender) {
                        logger.info("Sending draw card message to opponent...");
                        session.sendReliableMessage(cardDrawMsg, allSessionPlayers[index]);
                    }
                };
                break;
            }
    }
}

// The cardPlays object looks like this:
// {"eb051e15-1337-4071-b8a9-b9b0da32d7e2":[1,5],"27f87c33-c6f8-45f2-b403-801eaf4f4a2d":[5,6]}
// Where each player's uuid acts as the key for an array of their card play numbers
function addCardDraw(cardNumber, playerId) {
    logger.info("addCardDraw " + cardNumber + " to player " + playerId);

    if (cardPlays[playerId]) {
        if (cardPlays[playerId].length < 2) {
            cardPlays[playerId].push(cardNumber);
        } else {
            logger.info("Player " + playerId + " has played maximum amount of cards.");
        }
    } else {
        cardPlays[playerId] = [];
        cardPlays[playerId].push(cardNumber);
    }
    logger.info(cardPlays);
}

function randomIntFromInterval(min, max) { // min and max included 
    return Math.floor(Math.random() * (max - min + 1) + min)
}

// Return true if the send should be allowed
function onSendToPlayer(gameMessage) {
    logger.info("onSendToPlayer: ");
    logger.info(gameMessage);

    // This example rejects any payloads containing "Reject"
    return (!gameMessage.getPayloadAsText().includes("Reject"));
}

// Return true if the send to group should be allowed
// Use gameMessage.getPayloadAsText() to get the message contents
function onSendToGroup(gameMessage) {
    logger.info("onSendToGroup: " + gameMessage);
    return true;
}

// Return true if the player is allowed to join the group
function onPlayerJoinGroup(groupId, peerId) {
    logger.info("onPlayerJoinGroup: " + groupId + ", " + peerId);
    return true;
}

// Return true if the player is allowed to leave the group
function onPlayerLeaveGroup(groupId, peerId) {
    logger.info("onPlayerLeaveGroup: " + groupId + ", " + peerId);
    return true;
}

// A simple tick loop example
// Checks to see if a minimum amount of time has passed before seeing if the game has ended
async function tickLoop() {
    const elapsedTime = getTimeInS() - startTime;
    logger.info("Tick... " + elapsedTime + " activePlayers: " + activePlayers);

    // In Tick loop - see if all players have left early after a minimum period of time has passed
    // Call processEnding() to terminate the process and quit

    // if we had 2 players but both are gone
    if (players.length == 2 && activePlayers == 0) { //&& (elapsedTime > minimumElapsedTime)) {
        logger.info("All players disconnected. Ending game");
        const outcome = await session.processEnding();
        logger.info("Completed process ending with: " + outcome);
        process.exit(0);
    }
    else {
        setTimeout(tickLoop, tickTime);
    }
}

// Calculates the current time in seconds
function getTimeInS() {
    return Math.round(new Date().getTime() / 1000);
}

exports.ssExports = {
    configuration: configuration,
    init: init,
    onProcessStarted: onProcessStarted,
    onMessage: onMessage,
    onPlayerConnect: onPlayerConnect,
    onPlayerAccepted: onPlayerAccepted,
    onPlayerDisconnect: onPlayerDisconnect,
    onSendToPlayer: onSendToPlayer,
    onSendToGroup: onSendToGroup,
    onPlayerJoinGroup: onPlayerJoinGroup,
    onPlayerLeaveGroup: onPlayerLeaveGroup,
    onStartGameSession: onStartGameSession,
    onProcessTerminate: onProcessTerminate,
    onHealthCheck: onHealthCheck
};
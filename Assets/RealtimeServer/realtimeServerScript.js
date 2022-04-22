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
const minimumElapsedTime = 30; //120;

var session; // The Realtime server session object
var logger; // Log at appropriate level via .info(), .warn(), .error(), .debug()
var startTime; // Records the time the process started
var activePlayers = 0; // Records the number of connected players
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
const GAME_START_OP = 201;
const GAMEOVER_OP = 209;
const PLAY_CARD_OP = 300;
const DRAW_CARD_ACK_OP = 301;

let players = [];
let playersInfo = [];
let winner = null;
let cardPlays = {};
let gameover = false;

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
    logger.info("onPlayerConnect: ");
    logger.info(connectMsg);

    let payloadRaw = new Buffer.from(connectMsg.payload);
    let payload = JSON.parse(payloadRaw);
    logger.info("onPlayerConnect payload: ");
    logger.info(payload);

    let playerConnected = {
        peerId: connectMsg.player.peerId,
        playerId: payload.playerId,
        playerSessionId: connectMsg.player.playerSessionId,
        accepted: false,
        active: false
    };
    logger.info(playerConnected);

    playersInfo.push(playerConnected); // TODO: consider removing players array

    // Perform any validation needed for connectMsg.payload, connectMsg.peerId
    return true;
}

// Called when a Player is accepted into the game
function onPlayerAccepted(player) {
    logger.info("onPlayerAccepted");
    logger.info(player);

    players.push(player.peerId);
    //playersInfo.push(player); // TODO: consider removing players array
    playersInfo.forEach((playerInfo) => {
        logger.info("onPlayerAccepted playersInfo checking peerId");
        if (playerInfo.peerId == player.peerId) {
            logger.info("onPlayerAccepted playersInfo mark active");
            // not sure if we need to do this...
            playerInfo.accepted = true;
            playerInfo.active = true;
        }
    });

    // This player was accepted -- let's send them a message
    const msg = session.newTextGameMessage(OP_CODE_PLAYER_ACCEPTED, player.peerId,
        "Peer " + player.peerId + " accepted");
    session.sendReliableMessage(msg, player.peerId);
    activePlayers++;

    logger.info("onPlayerAccepted checking players length");
    logger.info(players.length);
    // this would have to adjusted to handle games where players can come and go within a single match
    if (players.length > 1) { //if (activePlayers > 1) { // TODO: move this back to just active players, as I'm not testing simultaneous players right now.

        logger.info("onPlayerAccepted players > 1");
        // getPlayers returns "a list of peer IDs for players that are currently connected to the game session"
        // So, let's match these players to the ones stored in playersInfo
        session.getPlayers().forEach((playerSession, playerId) => {

            logger.info("onPlayerAccepted players loop");
            logger.info(playerSession);
            logger.info(playerId);

            playersInfo.forEach((playerInfo) => {
                logger.info("onPlayerAccepted players playerInfo loop");
                logger.info("playerInfo.peerId: " + playerInfo.peerId + ", playerSession.peerId: " + playerSession.peerId + ", playerInfo.active: " + playerInfo.active);

                // find the other active player
                if (playerInfo.peerId != playerSession.peerId) { // && playerInfo.active == true) { TODO: will have to enable this back to prevent sending messages to non-active players... probably overkill
                    var gameStartPayload = {
                        remotePlayerId: playerInfo.playerId
                    };

                    logger.info("Sending start match message...");
                    logger.info(gameStartPayload);

                    // send out the match has started along with the opponent's playerId
                    const startMatchMessage = session.newTextGameMessage(GAME_START_OP, session.getServerId(), JSON.stringify(gameStartPayload));
                    session.sendReliableMessage(startMatchMessage, playerSession.peerId);
                }
            });
        });

    }

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

    playersInfo.forEach((playerInfo) => {
        if (playerInfo.peerId == peerId) {
            playerInfo.active = false;
        }
    });
}

// @BatteryAcid
// Handle a message to the server
function onMessage(gameMessage) {
    logger.info("onMessage");
    logger.info(gameMessage);
    logger.info(players);

    // pass data through the payload field
    var payloadRaw = new Buffer.from(gameMessage.payload);
    var payload = JSON.parse(payloadRaw);
    logger.info(payload);
    logger.info(payload.playerId);

    switch (gameMessage.opCode) {
        case PLAY_CARD_OP:
            {
                logger.info("PLAY_CARD_OP hit");

                const cardDrawn = randomIntFromInterval(1, 10);
                var cardDrawnSuccess = addCardDraw(cardDrawn, payload.playerId);

                if (cardDrawnSuccess) {

                    let allPlayersLength = players.length;
                    let cardDrawData = {
                        card: cardDrawn,
                        playedBy: payload.playerId,
                        plays: cardPlays[payload.playerId].length
                    };
                    logger.info(cardDrawData);

                    const cardDrawMsg = session.newTextGameMessage(DRAW_CARD_ACK_OP, session.getServerId(), JSON.stringify(cardDrawData));

                    for (let index = 0; index < allPlayersLength; ++index) {
                        logger.info("Sending draw card message to player " + players[index]);
                        session.sendReliableMessage(cardDrawMsg, players[index]);
                    }

                    checkGameOver();

                } else {
                    // ignore action as the player has already played max allowed cards
                    logger.info("Player " + payload.playerId + " attempted extra card!");
                }

                break;
            }
    }
}

//TODO: this is not getting hit...???
function checkGameOver() {
    // TODO: remove, for testing
    // var test = { "eb051e15-1337-4071-b8a9-b9b0da32d7e2": [1, 5], "27f87c33-c6f8-45f2-b403-801eaf4f4a2d": [5, 3] };
    // cardPlays = test;

    var gameCompletedPlayers = 0;

    for (const [key, value] of Object.entries(cardPlays)) {
        // has player made two plays
        if (value.length == 2) {
            gameCompletedPlayers++;
        }
    }

    logger.info(gameCompletedPlayers);
    // If at least two players completed two turns, signal game over.
    // This partially handles the case where a player joins but leaves the game after one play or something,
    // and another joins and plays two turns. Update for your game requirements.
    if (gameCompletedPlayers >= 2) {
        logger.info("setting game over...");
        determineWinner();
        gameover = true;
    }
}

// assumes both players played two cards
function determineWinner() {
    // TODO: remove, for testing
    // var test = { "eb051e15-1337-4071-b8a9-b9b0da32d7e2": [1, 5], "27f87c33-c6f8-45f2-b403-801eaf4f4a2d": [5, 3] };
    // cardPlays = test;

    var result = {
        playerOneId: "",
        playerTwoId: "",
        playerOneScore: "",
        playerTwoScore: "",
        winnerId: ""
    }

    var playersExamined = 0;
    for (const [key, value] of Object.entries(cardPlays)) {
        // make sure we're only looking at players with two plays
        if (value.length == 2) {
            if (playersExamined == 0) {
                result.playerOneId = key;
                result.playerOneScore = value[0] + value[1];
            } else if (playersExamined == 1) {
                result.playerTwoId = key;
                result.playerTwoScore = value[0] + value[1];
            }
            playersExamined++;
        }
    }

    if (result.playerOneScore > result.playerTwoScore) {
        result.winnerId = result.playerOneId;
    } else if (result.playerOneScore < result.playerTwoScore) {
        result.winnerId = result.playerTwoId;
    } else if (result.playerOneScore == result.playerTwoScore) {
        result.winnerId = "tie";
    }

    logger.info(result);

    // send out game over messages with winner
    const gameoverMsg = session.newTextGameMessage(GAMEOVER_OP, session.getServerId(), JSON.stringify(result));

    for (let index = 0; index < players.length; ++index) {
        logger.info("Sending game over message to player " + players[index]);
        session.sendReliableMessage(gameoverMsg, players[index]);
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
            return false;
        }
    } else {
        cardPlays[playerId] = [];
        cardPlays[playerId].push(cardNumber);
    }
    logger.info(cardPlays);
    return true;
}

// A simple tick loop example
async function tickLoop() {
    // const elapsedTime = getTimeInS() - startTime;
    // logger.info("Tick... " + elapsedTime + " activePlayers: " + activePlayers);

    if (!gameover) {

        // If we had 2 players that are no longer active, end game.
        // You can add a minimum elapsed time check here if you'd like
        if (players.length == 2 && activePlayers == 0) { // && (elapsedTime > minimumElapsedTime)) {
            logger.info("All players disconnected. Ending game");

            gameoverCleanup();
        } else {
            setTimeout(tickLoop, tickTime);
        }
    } else {
        logger.info("game over");
        gameoverCleanup();
    }
}

async function gameoverCleanup() {
    // Call processEnding() to terminate the process and quit
    const outcome = await session.processEnding();
    logger.info("Completed process ending with: " + outcome);
    process.exit(0);
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
## Unity Realtime GameLift 2022 

An example project of how to setup Unity to work with Realtime GameLift to enable multiplayer functionality.

## Companion AWS Lambda Project

* https://github.com/BatteryAcid/unity-realtime-gl-lambda-2022

## Tutorial Video  

* https://youtu.be/rw0SkOTo31E

## Notes

Create realtime gamelift script with AWS CLI   
    `aws gamelift create-script --name YOUR_SCRIPT_NAME --script-version 0.0.1 --zip-file fileb://YOUR_SCRIPT_PATH.zip`

Update realtime gamelift script  with AWS CLI  
     `aws gamelift update-script --script-id YOUR_SCRIPT_ID --name YOUR_SCRIPT_NAME --script-version 0.0.1 --zip-file fileb://YOUR_SCRIPT_PATH.zip`
     
## Resources

❗️❗️ Required Configuration ❗️❗️  
[API Gateway Endpoint] https://github.com/BatteryAcid/unity-realtime-gamelift-2022/blob/master/Assets/Scripts/GameManager.cs#L14  
[Identity Pool ID] https://github.com/BatteryAcid/unity-realtime-gamelift-2022/blob/master/Assets/Scripts/SQSMessageProcessing.cs#L14  
[SQS Endpoint] https://github.com/BatteryAcid/unity-realtime-gamelift-2022/blob/master/Assets/Scripts/SQSMessageProcessing.cs#L15   
[Lambda - GameLift Region] https://github.com/BatteryAcid/unity-realtime-gl-lambda-2022/blob/master/index.js#L5  
[Lambda - GameLift Queue]  https://github.com/BatteryAcid/unity-realtime-gl-lambda-2022/blob/master/index.js#L6  

[Resources]
[Get started with Realtime servers] https://docs.aws.amazon.com/gamelift/latest/developerguide/realtime-plan.html   
[Setup GameLift client] https://docs.aws.amazon.com/gamelift/latest/developerguide/gamelift-sdk-client-api.html  
[Special Considerations AWS SDK Unity Support] https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/unity-special.html  
[GameLift Realtime Client SDK] https://aws.amazon.com/gamelift/getting-started/  
[GameLift service API reference (AWS SDK)] https://docs.aws.amazon.com/gamelift/latest/developerguide/reference-awssdk.html  
[Setting up queues for game session placement] https://docs.aws.amazon.com/gamelift/latest/developerguide/queues-intro.html  
[New AWS re:Post for GameLift questions] https://repost.aws/tags/TAF8-XUqojTsadH5jSz3IfGQ/amazon-game-lift  
[Complex Realtime script setup answers] https://forums.awsgametech.com/t/realtime-server-project-setup/6607  
 

#gametech #gamedev #awscommunitybuilder #unity

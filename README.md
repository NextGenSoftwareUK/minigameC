# Mars Minigame Server

## Overview
Mars Minigame Server is a real-time multiplayer game server developed by Infinite Star Studios. This server manages a strategic capture-the-point game set on Mars, where players control tanks to capture and defend various points on the map.

## Features
- Real-time multiplayer gameplay
- Strategic point capture mechanics
- Tank battle system
- Artillery strike capabilities
- Intermission and match duration management
- Persistent game state across server restarts

## Technical Stack
- ASP.NET Core
- SignalR for real-time communication
- Entity Framework Core (if used for data persistence)

## Setup and Installation
1. Clone the repository
2. Ensure you have .NET Core SDK installed
3. Navigate to the project directory
4. Run `dotnet restore` to restore dependencies
5. Run `dotnet build` to build the project
6. Run `dotnet run` to start the server

## Configuration
- `appsettings.json`: Contains configuration for allowed origins, logging, and other server settings
- Game settings such as match duration and intermission time can be adjusted in the `GameState.cs` file

## API Endpoints
- `/gameHub`: SignalR hub for real-time game communication
- `/health`: Health check endpoint
- `/`: Welcome message endpoint

## Development
To contribute to this project:
1. Fork the repository
2. Create a new branch for your feature
3. Commit your changes
4. Push to your fork
5. Create a pull request

## Deployment
Deployment instructions are available in the deployment guide (link to be added).

## License
This project is proprietary software. All rights reserved.

## Copyright
© 2023 Infinite Star Studios. All rights reserved.

---

For more information, please contact Infinite Star Studios.
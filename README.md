# Skylytics - Personal Hypixel Skyblock Auction Tracker

A personal backend for Hypixel Skyblock auction house tracking, bazaar prices, flip finding, and market analysis. Based on the [Coflnet HypixelSkyblock](https://github.com/Coflnet/HypixelSkyblock) project.

**Note:** This is configured for personal use - no payment system required. All features are available without subscriptions.

## Features

- **Auction Tracking**: Browse, search, and analyze auction house data
- **Bazaar Prices**: Real-time bazaar price tracking and history
- **Flip Finder**: Find profitable flip opportunities
- **Price History**: Track item price trends over time
- **Player Stats**: View player auction and bid history
- **Search**: Universal search across items, players, and auctions

## Quick Start (Personal Use)

### Using Docker Compose (Recommended)

1. Clone this repository:
```bash
git clone https://github.com/xtqr/Skylytics-Backend.git
cd Skylytics-Backend
```

2. Start with the simplified personal configuration:
```bash
docker-compose -f docker-compose.personal.yml up -d
```

3. Access the API documentation at: http://localhost:5000/api

### Manual Setup

1. Install prerequisites:
   - .NET 8.0 SDK
   - MariaDB 10.11+
   - Redis (optional, for caching)

2. Configure `appsettings.json` with your database connection

3. Run the application:
```bash
dotnet run
```

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/auctions` | Auction related endpoints |
| `GET /api/bazaar` | Bazaar prices and orders |
| `GET /api/items` | Item details and search |
| `GET /api/players` | Player profiles and stats |
| `GET /api/prices` | Price history and analytics |
| `GET /api/flipper` | Flip finding opportunities |
| `GET /api/search` | Universal search |
| `GET /api/status` | Health and system stats |

### Example API Calls

```bash
# Get recent auctions
curl http://localhost:5000/api/auctions/recent

# Get bazaar prices
curl http://localhost:5000/api/bazaar

# Search for an item
curl http://localhost:5000/api/items/search?query=hyperion

# Get flip opportunities
curl http://localhost:5000/api/flipper/opportunities
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DBConnection` | MariaDB connection string | See appsettings.json |
| `REDIS_HOST` | Redis host:port | localhost:6379 |
| `KAFKA_HOST` | Kafka host:port | localhost:9092 |
| `EnableRateLimiting` | Enable API rate limiting | false |
| `ITEMS_BASE_URL` | Items service URL | http://localhost:5014 |

### appsettings.json

The main configuration file. Key settings:
- `DBConnection`: Database connection string
- `EnableRateLimiting`: Set to `false` for personal use (unlimited API calls)
- `KAFKA_HOST`: For real-time auction updates

## Full Setup (with all microservices)

For the complete system with all features (flip finding, real-time updates, etc.), you'll need to set up additional microservices. See the original documentation below.

---

## Original Coflnet Documentation

This is the back-end for https://sky.coflnet.com 
You can get the same data and play around with it by using this project.

Some endpoints are exposed via REST, see the open-api docs: https://sky.coflnet.com/api


## Kafka topics
This project uses a kafka server to distribute workloads.  
Topics produced are:
* `sky-newauction`
* `sky-newbid`
* `sky-soldauction`
* `sky-canceledauction`
* `sky-endedauction`
* `sky-bazaarprice`  
* `sky-update-player` (players whose names should be updated)
* `sky-updated-player`  (players who got updated)
* `sky-flips`  found flips, producer: flipper, consumer: light-clients

You can modify them by changing appsettings.json or setting the enviroment variables.
To get a full list check appsettings.json.  
Note that to set them as enviroment variables you have to prefix them with `TOPICS__` because you can't add `:` in an env variable.  
Example:  
To set `"MISSING_AUCTION":"sky-canceledauction"` you have to set `TOPICS__MISSING_AUCTION=mycooltopic`

## Full Microservices Setup

Development of this project is done with docker-compose. The whole system is split into so called [microservices](https://en.wikipedia.org/wiki/Microservices) ie multiple smaller projects each doing one thing eg. flip finding.

1. Install docker and docker-compose if you are a windows user these come with docker desktop.
1. create a new folder `skyblock`, enter it and clone this repository with `git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev`
2. copy `docker-compose.yml` to the `skyblock` folder (one folder above)
3. Open a terminal in the `skyblock` folder and Start up the databases with `docker-compose up -d mariadb phpmyadmin kafka redis`
3. Clone the indexer `git clone https://github.com/Coflnet/SkyIndexer.git` The indexer is the service that manages and indexes skyblock data in the database. Make sure to modify the migration for first setup in SkyIndexer/Program.cs.  
4. Also clone the updater `git clone https://github.com/Coflnet/SkyUpdater.git`, commands `git clone https://github.com/Coflnet/SkyCommands.git` and the website `git clone https://github.com/Coflnet/hypixel-react.git`
5. Start these services with `docker-compose up -d indexer updater commands api modcommands frontend` after that is done you have a complete setup to archive and browse auctions locally.
7. If you want flips you will also want to clone the flipper and/or sniper flip finders  
`git clone https://github.com/Coflnet/SkyFlipper.git`   
`git clone https://github.com/Coflnet/SkySniper.git`
`docker-compose up -d flipper sniper`
Note that you only need to clone services that have a `build` section. The ones with image are just downloaded.


For basic website functunality you need
* this repo
* SkyCommands
* hypixel-react (frontend)
* SkyUpdater (downloading process)

## List of repos to clone (for full setup)
```
git clone --depth=1 https://github.com/Coflnet/SkyItems
git clone --depth=1 https://github.com/Coflnet/SkyIndexer
git clone --depth=1 https://github.com/Coflnet/SkyCommands
git clone --depth=1 https://github.com/Coflnet/hypixel-react
git clone --depth=1 https://github.com/Coflnet/SkyApi
git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend
git clone --depth=1 https://github.com/Coflnet/SkyFlipper
git clone --depth=1 https://github.com/Coflnet/SkyMcConnect
git clone --depth=1 https://github.com/Coflnet/SkySubscriptions
git clone --depth=1 https://github.com/Coflnet/SkyUpdater
git clone --depth=1 https://github.com/Coflnet/SkyModCommands
git clone --depth=1 https://github.com/Coflnet/SkySettings
git clone --depth=1 https://github.com/Coflnet/SkyPlayerName
git clone --depth=1 https://github.com/Coflnet/SkyFilter
git clone --depth=1 https://github.com/Coflnet/SkyCrafts
git clone --depth=1 https://github.com/Coflnet/SkyFlipTracker
```

## Future Plans

- AI-powered wiki assistant (separate repository)
- Investment advice based on mayors, updates, and market trends
- Enhanced flip finding algorithms

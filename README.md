UltraMafia
==========
The first OpenSource Mafia Bot! Now only Russian version, but we are working on i8n!
## General info
This repository contains Open Source implementation of "Mafia" game bot. The bot currently works with Telegram.   
We plan to add other social platforms in the future.  
You are welcome with your ideas and contrubutions.
## Dependencies
This project depends on: 
 - [EFCore](https://github.com/dotnet/efcore) - used to presist game data.
 - [MySQL EFCore driver from Pomelo](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql) - used as EFCore data provider.
 - [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) - OpenSource library to interact with Telegram Bot API.
## How to Run?
### From sources
This project developed on C# and builds under .NET Core 3.1. To run project, set settings by updating `settings.json`. Settings info available after this section.
### With Docker
You can get our images from [Docker Hub](https://hub.docker.com/u/dotnetnomads).
If you want setup development environment use `docker-compose up` in project folder.
You can change setting by setting environment variables, sample from `docker-compose.yml`:
```
environment: 
      "mafia_Db__Host": "mysql"
      "mafia_Db__User": "root"
      "mafia_Db__Password": "root"
      "mafia_Db__DbName": "ultra-mafia"
      "mafia_Frontend__Token": "place-your-token"
      "mafia_Frontend__BotUserName": "place-your-bot-username"
      "mafia_Game__DevelopmentMode": "true"
```
### Settings description
```json
{
  "Db": {
    "Host": "<databse-host, eg. localhost>",
    "Port": 3306,
    "User": "<databse-user, eg. root>",
    "Password": "<database-password, eg. root>",
    "DbName": "UltraMafia"
  },
  "Frontend": {
    "Token": "<bot-token, get it from BotFather>",
    "BotUserName": "<bot-username, get it also from BotFather>"
  },
  "Game": {
    "MinGamerCount": 4, // minimal count of gamers for session.
    "DevelopmentMode": false // development mode, bypass unique gamer validation.
  }, 
  "Serilog": { // logging level, see: google/Serilog
        "MinimumLevel": "Information"
   }
}
```
## Development

### Development conventions
   In progress... :)
### Creating database migration
If you have new changes in models, just create new migration with a following command inside the `UltraMafia.DAL` project directory:
`dotnet ef --startup-project ../UltraMafia/ migrations  add <migrationName>`, where `<migrationName>` is the name of new migration.

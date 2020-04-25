# UltraMafia - Open Source Mafia Game Bot

## General info

## Dependencies

## How to Run?

## Development

### Development conventions
   In progress... :)
### Creating database migration
If you have new changes in models, just create new migration with a following command inside the `UltraMafia.DAL` project directory:
`dotnet ef --startup-project ../UltraMafia/ migrations  add <migrationName>`, where `<migrationName>` is the name of new migration.
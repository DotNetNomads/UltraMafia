FROM  mcr.microsoft.com/dotnet/sdk:5.0 AS build
COPY . /tmp
WORKDIR /tmp
RUN dotnet publish -c Release src/UltraMafia.App/
FROM mcr.microsoft.com/dotnet/runtime:5.0
WORKDIR /app
COPY --from=build tmp/src/UltraMafia.App/bin/Release/net5.0/publish/* ./
ENTRYPOINT [ "dotnet" ]
CMD [ "UltraMafia.App.dll"]

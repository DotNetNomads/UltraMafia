FROM bitnami/dotnet-sdk:3.1 AS build
COPY . /tmp
WORKDIR /tmp
RUN dotnet publish -c Release
FROM bitnami/aspnet-core:3.1
WORKDIR /app
COPY --from=build tmp/src/UltraMafia/bin/Release/netcoreapp3.1/publish/* ./
ENTRYPOINT [ "dotnet" ]
CMD [ "UltraMafia.dll"]

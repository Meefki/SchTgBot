
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SchTgBot/SchTgBot.csproj", "SchTgBot/"]
RUN dotnet restore "SchTgBot/SchTgBot.csproj"

COPY . .
WORKDIR /src/SchTgBot

RUN dotnet publish "SchTgBot.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV TZ=Europe/Lisbon

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SchTgBot.dll"]

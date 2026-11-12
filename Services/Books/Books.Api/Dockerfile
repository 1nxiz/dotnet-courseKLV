# Багатоетапна збірка Books.Api (контекст збірки — корінь репозиторію):
#   docker build -t books-api:v1 -f Services/Books/Books.Api/Dockerfile .
# Етап 1: SDK лише компілює (спершу тільки *.csproj -> restore кешується окремим шаром)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["Platform.ServiceDefaults/Platform.ServiceDefaults.csproj", "Platform.ServiceDefaults/"]
COPY ["Services/Books/Books.Api/Books.Api.csproj", "Services/Books/Books.Api/"]
COPY ["Services/Books/Books.Bll/Books.Bll.csproj", "Services/Books/Books.Bll/"]
COPY ["Services/Books/Books.Dal/Books.Dal.csproj", "Services/Books/Books.Dal/"]
COPY ["Services/Books/Books.Domain/Books.Domain.csproj", "Services/Books/Books.Domain/"]
RUN dotnet restore "Services/Books/Books.Api/Books.Api.csproj"
COPY . .
WORKDIR "/src/Services/Books/Books.Api"
RUN dotnet build "Books.Api.csproj" -c Release -o /app/build --no-restore

# Етап 2: публікація
FROM build AS publish
RUN dotnet publish "Books.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Етап 3: фінальний легкий образ лише з runtime (ASP.NET Core 8 слухає порт 8080)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Books.Api.dll"]

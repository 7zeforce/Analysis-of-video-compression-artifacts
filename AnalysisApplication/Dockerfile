# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AnalysisApplication.csproj", "./"]
RUN dotnet restore "AnalysisApplication.csproj"
COPY . .
RUN dotnet publish "AnalysisApplication.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libatomic1 libgtk-3-0 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AnalysisApplication.dll"]

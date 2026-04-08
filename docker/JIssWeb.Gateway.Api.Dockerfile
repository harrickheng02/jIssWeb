FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/JIssWeb.sln ./
COPY backend/src/JIssWeb.Common/JIssWeb.Common.csproj JIssWeb.Common/
COPY backend/src/JIssWeb.Gateway.Api/JIssWeb.Gateway.Api.csproj JIssWeb.Gateway.Api/
RUN dotnet restore JIssWeb.Gateway.Api/JIssWeb.Gateway.Api.csproj
COPY backend/src/JIssWeb.Common/ JIssWeb.Common/
COPY backend/src/JIssWeb.Gateway.Api/ JIssWeb.Gateway.Api/
RUN dotnet publish JIssWeb.Gateway.Api/JIssWeb.Gateway.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5094
ENV ASPNETCORE_URLS=http://+:5094
ENTRYPOINT ["dotnet", "JIssWeb.Gateway.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/JIssWeb.sln ./
COPY backend/src/JIssWeb.Common/JIssWeb.Common.csproj JIssWeb.Common/
COPY backend/src/JIssWeb.Frontend.Bff/JIssWeb.Frontend.Bff.csproj JIssWeb.Frontend.Bff/
RUN dotnet restore JIssWeb.Frontend.Bff/JIssWeb.Frontend.Bff.csproj
COPY backend/src/JIssWeb.Common/ JIssWeb.Common/
COPY backend/src/JIssWeb.Frontend.Bff/ JIssWeb.Frontend.Bff/
RUN dotnet publish JIssWeb.Frontend.Bff/JIssWeb.Frontend.Bff.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5095
ENV ASPNETCORE_URLS=http://+:5095
ENTRYPOINT ["dotnet", "JIssWeb.Frontend.Bff.dll"]
